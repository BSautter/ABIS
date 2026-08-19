using Abis.Api.Data;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The mill QR code captured against an inbound coil — legacy's <c>addqrcode</c>
/// (<c>legacy/web/db01-prod/cgi-bin/coil_receiving.pl:495</c>):
///
/// <code>
/// if ( (length($coil_qr_code) > 67) &amp;&amp; (index($coil_qr_code, '$') != -1) &amp;&amp; (length($coil_org_num) > 2))
/// </code>
///
/// <para>Three rules, no explanation anywhere in the source, and no live sample to derive them from.
/// They are a shape test for the mill's own payload — long, with <c>$</c> as a field separator — so
/// they are ported <b>verbatim</b>. Loosening one on a guess lets a mis-scan be stored as a coil's
/// certificate reference; tightening one rejects scans the plant makes every day.</para>
/// </summary>
public sealed class HandheldQrTests
{
    /// <summary>68 characters — one past legacy's `> 67` — carrying the separator.</summary>
    private static string ValidQr() => "MILL$" + new string('A', 63);

    [Fact]
    public void A_well_formed_mill_scan_is_accepted()
    {
        Assert.Equal(68, ValidQr().Length);
        Assert.Null(HandheldQrCode.Validate("C-1001", ValidQr()));
    }

    // ---- The three legacy rules --------------------------------------------------------

    [Fact]
    public void The_length_boundary_is_exactly_where_legacy_puts_it()
    {
        // `> 67`, so 67 is refused and 68 is accepted. An off-by-one here either rejects real scans
        // or admits short ones, and both look like the scanner misbehaving rather than a rule.
        Assert.NotNull(HandheldQrCode.Validate("C-1001", "MILL$" + new string('A', 62)));  // 67
        Assert.Null(HandheldQrCode.Validate("C-1001", "MILL$" + new string('A', 63)));     // 68
    }

    [Fact]
    public void A_scan_with_no_separator_is_refused_however_long_it_is()
    {
        Assert.NotNull(HandheldQrCode.Validate("C-1001", new string('A', 200)));
    }

    [Fact]
    public void The_coil_number_boundary_is_exactly_where_legacy_puts_it()
    {
        // `length($coil_org_num) > 2`, so 2 is refused and 3 is accepted.
        Assert.NotNull(HandheldQrCode.Validate("AB", ValidQr()));
        Assert.Null(HandheldQrCode.Validate("ABC", ValidQr()));
    }

    // ---- Beyond legacy ------------------------------------------------------------------

    [Fact]
    public void An_over_long_scan_is_REFUSED_rather_than_truncated()
    {
        // barcode_string is VARCHAR2(4000). Legacy has no such check because its DBI call would just
        // throw; a silent truncation would be worse than either, because a cut-short QR string still
        // looks like a code and nothing downstream could tell it was incomplete.
        var problem = HandheldQrCode.Validate("C-1001", "MILL$" + new string('A', 4100));
        Assert.NotNull(problem);
        Assert.Contains("4000", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_scan_is_refused(string? qr) =>
        Assert.NotNull(HandheldQrCode.Validate("C-1001", qr));

    [Fact]
    public void The_refusal_NAMES_the_rule_that_failed()
    {
        // Legacy answers a bare "Invalid QR Code" for all three. An operator holding a scanner needs
        // to know whether to rescan, reposition, or call someone — and "invalid" tells them none of it.
        Assert.Contains("too short", HandheldQrCode.Validate("C-1001", "MILL$abc")!);
        Assert.Contains("separator", HandheldQrCode.Validate("C-1001", new string('A', 200))!);
        Assert.Contains("coil number", HandheldQrCode.Validate("AB", ValidQr())!);
    }
}

/// <summary>The QR capture's database half — including the multi-row behaviour inherited from
/// legacy's unscoped UPDATE.</summary>
public sealed class InboundCoilQrStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public InboundCoilQrStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_qr_{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        SqliteFixture.EnsureCreatedAndSeeded(_cs);
        _repo = new AbisRepository(new Abis.Api.Data.DbConnectionFactory(new Abis.Api.Data.DatabaseOptions
        {
            Provider = "Sqlite", ConnectionString = _cs, Seed = true,
        }));
    }

    private void Exec(string sql)
    {
        using var c = new Microsoft.Data.Sqlite.SqliteConnection(_cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const string Qr = "MILL$COIL$HEAT$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task The_scan_is_stored_against_the_coil_and_reads_back()
    {
        Exec("INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num) " +
             "VALUES (700, 'BOL-Q', 1, 'C-2001', 0);");

        Assert.Equal(1, await _repo.SaveInboundCoilQrAsync("C-2001", Qr, CancellationToken.None));
        Assert.Equal(Qr, await _repo.GetInboundCoilQrAsync("C-2001", CancellationToken.None));
    }

    [Fact]
    public async Task It_stamps_EVERY_inbound_row_carrying_that_coil_number_and_says_how_many()
    {
        // Legacy's UPDATE is scoped to the coil number alone, and the same customer number can appear
        // on several BOL lines. That is preserved — but the count is returned, so a scan that touched
        // three rows says three instead of looking like it touched one.
        Exec("""
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num) VALUES (700, 'BOL-A', 1, 'C-DUP', 0);
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num) VALUES (700, 'BOL-B', 1, 'C-DUP', 0);
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num) VALUES (701, 'BOL-C', 1, 'C-DUP', 0);
            """);

        Assert.Equal(3, await _repo.SaveInboundCoilQrAsync("C-DUP", Qr, CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_coil_stores_NOTHING_and_reports_zero()
    {
        // The endpoint turns this into a 404. A QR code saved against nothing cannot be told apart
        // from one never scanned.
        Assert.Equal(0, await _repo.SaveInboundCoilQrAsync("NO-SUCH-COIL", Qr, CancellationToken.None));
    }

    [Fact]
    public async Task Re_scanning_REPLACES_the_stored_code()
    {
        // A coil relabelled at the mill scans differently; the newest read is the true one.
        Exec("INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num) " +
             "VALUES (702, 'BOL-R', 1, 'C-3001', 0);");
        await _repo.SaveInboundCoilQrAsync("C-3001", Qr, CancellationToken.None);

        var second = Qr.Replace("HEAT", "HEA2");
        await _repo.SaveInboundCoilQrAsync("C-3001", second, CancellationToken.None);
        Assert.Equal(second, await _repo.GetInboundCoilQrAsync("C-3001", CancellationToken.None));
    }

    [Fact]
    public async Task A_coil_with_no_QR_yet_reads_back_null_rather_than_empty()
    {
        Exec("INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num) " +
             "VALUES (703, 'BOL-N', 1, 'C-4001', 0);");
        Assert.Null(await _repo.GetInboundCoilQrAsync("C-4001", CancellationToken.None));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}

/// <summary>
/// The <b>second</b> QR store — the standalone <c>barcode_string</c> table keyed by the customer coil
/// number (legacy <c>w_qr_manual.wf_update_barcode_string</c>).
///
/// <para>Legacy keeps two QR stores and they are not the same thing: a COLUMN on the inbound BOL line
/// (written by the handheld CGI, 7,080 populated on <c>.230</c>) and this TABLE (written by the
/// PowerBuilder desktop, 6,162 rows). They are near-mirrors — <b>5,996 of the table's coils also
/// carry the column</b> — so code that writes one and reads the other looks correct on almost every
/// coil and is wrong on the rest. These tests exist mostly to hold that distinction in place.</para>
/// </summary>
public sealed class CoilOrgBarcodeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public CoilOrgBarcodeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_orgbc_{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        SqliteFixture.EnsureCreatedAndSeeded(_cs);
        _repo = new AbisRepository(new Abis.Api.Data.DbConnectionFactory(new Abis.Api.Data.DatabaseOptions
        {
            Provider = "Sqlite", ConnectionString = _cs, Seed = true,
        }));
    }

    private const string Qr = "MILL$COIL$HEAT$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task A_first_scan_CREATES_and_a_second_REPLACES()
    {
        // Legacy counts first, then INSERTs or UPDATEs. The distinction is reported so a caller can
        // tell "this coil had no code" from "this coil's code changed" — a relabelled coil is worth
        // noticing.
        Assert.True(await _repo.SaveCoilOrgBarcodeAsync("ORG-9001", Qr, CancellationToken.None));
        Assert.False(await _repo.SaveCoilOrgBarcodeAsync("ORG-9001", Qr.Replace("HEAT", "HEA2"), CancellationToken.None));
        Assert.Equal(Qr.Replace("HEAT", "HEA2"), await _repo.GetCoilOrgBarcodeAsync("ORG-9001", CancellationToken.None));
    }

    [Fact]
    public async Task It_is_a_DIFFERENT_store_from_the_inbound_coil_column()
    {
        // The load-bearing assertion. Writing the table must not populate the column, and vice versa —
        // if it ever does, the two stores have been silently merged and the 166 coils that live in only
        // one of them on real data will start disagreeing with whatever reads the other.
        await _repo.SaveCoilOrgBarcodeAsync("ORG-7777", Qr, CancellationToken.None);
        Assert.Null(await _repo.GetInboundCoilQrAsync("ORG-7777", CancellationToken.None));

        Assert.Equal(Qr, await _repo.GetCoilOrgBarcodeAsync("ORG-7777", CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_coil_reads_back_null()
    {
        Assert.Null(await _repo.GetCoilOrgBarcodeAsync("NO-SUCH-ORG", CancellationToken.None));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
