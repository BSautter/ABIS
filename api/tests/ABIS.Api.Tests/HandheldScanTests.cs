using Abis.Api.Data;
using Abis.Api.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The handheld RF receiving gun's barcode rule (legacy
/// <c>legacy/web/db01-prod/cgi-bin/coil_receiving_12.pl</c>). These are customer coil numbers off mill
/// labels, so the rule is deliberately conservative about what it rewrites.
/// </summary>
public sealed class HandheldBarcodeTests
{
    [Theory]
    [InlineData("S12345", "12345", true)]
    [InlineData("s12345", "s12345", false)]   // lower case is not the header — legacy matches /^S/
    [InlineData("12345", "12345", false)]
    [InlineData("  S12345  ", "12345", true)]
    public void Strips_only_a_leading_capital_S(string raw, string expected, bool stripped)
    {
        var scan = HandheldBarcode.Parse(raw);
        Assert.Equal(expected, scan.CoilNumber);
        Assert.Equal(stripped, scan.HeaderStripped);
    }

    [Fact]
    public void Keeps_an_S_that_is_not_the_header()
    {
        // These are the CUSTOMER's numbers, not ours — reformatting them loses real coils.
        Assert.Equal("12S45", HandheldBarcode.Parse("12S45").CoilNumber);
        Assert.Equal("AB-S-9", HandheldBarcode.Parse("AB-S-9").CoilNumber);
    }

    [Fact]
    public void Keeps_letters_unlike_the_DAS_rule()
    {
        // The DAS scan (CoilBarcode) resolves to our numeric coil_abc_num and rejects non-digits. The
        // handheld resolves INBOUND_COIL.COIL_NUMBER, a VARCHAR2, so letters are perfectly valid here.
        var scan = HandheldBarcode.Parse("ABC123X");
        Assert.True(scan.Valid);
        Assert.Equal("ABC123X", scan.CoilNumber);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("S000000")]   // substitution happens AFTER the header strip
    public void Maps_the_no_barcode_label_to_readable_text(string raw)
    {
        var scan = HandheldBarcode.Parse(raw);
        Assert.True(scan.NoBarcode);
        Assert.Equal("NO BARCODE", scan.CoilNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_scan_is_not_valid(string? raw) => Assert.False(HandheldBarcode.Parse(raw).Valid);

    [Fact]
    public void A_bare_S_is_left_alone_rather_than_emptied()
    {
        // Stripping it would leave nothing to look up and report "unreadable"; keeping it lets the
        // lookup miss honestly and show the operator what was actually scanned.
        var scan = HandheldBarcode.Parse("S");
        Assert.Equal("S", scan.CoilNumber);
        Assert.False(scan.HeaderStripped);
    }

    [Fact]
    public void The_two_scanning_surfaces_do_not_share_a_rule()
    {
        // Guards the finding that motivated a separate port: run each label through the other's rule
        // and it resolves to nothing. Sharing one implementation would break both surfaces.
        var millLabel = "S12345";
        var dasLabel = "XX2S12345";

        Assert.Equal("12345", HandheldBarcode.Parse(millLabel).CoilNumber);
        Assert.False(CoilBarcode.Parse(millLabel).Valid);          // DAS rule can't read a mill label

        Assert.Equal("12345", CoilBarcode.Parse(dasLabel).Normalized);
        Assert.Equal("XX2S12345", HandheldBarcode.Parse(dasLabel).CoilNumber);   // unchanged → won't match
    }
}

/// <summary>The scan's database half: minted-vs-unminted and the mill's advance notice.</summary>
public sealed class InboundCoilScanTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public InboundCoilScanTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_scan_{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        SqliteFixture.EnsureCreatedAndSeeded(_cs);
        _repo = new AbisRepository(new DbConnectionFactory(new DatabaseOptions
        {
            Provider = "Sqlite", ConnectionString = _cs, Seed = true,
        }));
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection(_cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task An_unminted_coil_says_mint_and_carries_the_mills_notice()
    {
        Exec("""
            INSERT INTO inbound_coil (edi_file_id, bol, item_num, coil_number, net_weight, gross_weight,
                                      alloy, temper, coil_gauge, coil_width, lot, pack_id)
                 VALUES (500, 'BOL-1', 1, 'C-1001', 12000, 12250, '5052', 'H32', 0.125, 60.5, 'LOT-A', 'PK-1');
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num)
                 VALUES (500, 'BOL-1', 1, 'C-1001', 0);
            """);

        var s = await _repo.ScanInboundCoilAsync("SC-1001", CancellationToken.None);

        Assert.Equal(InboundScanOutcome.Mint, s.Outcome);
        Assert.Equal("C-1001", s.CoilNumber);
        Assert.True(s.HeaderStripped);
        Assert.Empty(s.MintedAbcNums);
        Assert.Equal(12000m, s.Detail!.NetWeight);
        Assert.Equal("5052", s.Detail.Alloy);
        Assert.Equal("LOT-A", s.Detail.Lot);
    }

    [Fact]
    public async Task Coil_abc_num_zero_counts_as_unminted()
    {
        // The column carries 0 for "received but not yet minted", so `> 0` IS the minted test —
        // reading a 0 as a real ABC number would tell the operator to reprint a label that doesn't exist.
        Exec("""
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num)
                 VALUES (501, 'BOL-2', 1, 'C-1002', 0);
            """);

        var s = await _repo.ScanInboundCoilAsync("C-1002", CancellationToken.None);
        Assert.Equal(InboundScanOutcome.Mint, s.Outcome);
        Assert.Empty(s.MintedAbcNums);
    }

    [Fact]
    public async Task An_already_minted_coil_reports_every_abc_not_just_the_last()
    {
        // Legacy shows whichever ABC its row loop left in the variable; a coil can legitimately carry
        // several because the operator is allowed to mint again. Returning all of them keeps that real.
        Exec("""
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num)
                 VALUES (502, 'BOL-3', 1, 'C-1003', 90001),
                        (502, 'BOL-3', 2, 'C-1003', 90002);
            """);

        var s = await _repo.ScanInboundCoilAsync("SC-1003", CancellationToken.None);

        Assert.Equal(InboundScanOutcome.AlreadyMinted, s.Outcome);
        Assert.Equal([90001L, 90002L], s.MintedAbcNums);
    }

    [Fact]
    public async Task Already_minted_is_a_choice_not_a_refusal()
    {
        // Legacy puts "Reprint Labels" AND "New Coil ABC Num" on the same screen. The outcome reports
        // the situation; it must not block the operator from minting another.
        Exec("""
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num)
                 VALUES (503, 'BOL-4', 1, 'C-1004', 90010);
            """);

        var s = await _repo.ScanInboundCoilAsync("C-1004", CancellationToken.None);
        Assert.Equal(InboundScanOutcome.AlreadyMinted, s.Outcome);
        Assert.Single(s.MintedAbcNums);
    }

    [Fact]
    public async Task A_coil_with_no_advance_notice_still_scans()
    {
        // Legacy shows "NONE" for every field and lets receiving continue — a coil physically on the
        // dock has to be receivable whether or not its EDI arrived.
        var s = await _repo.ScanInboundCoilAsync("C-NOT-ON-FILE", CancellationToken.None);

        Assert.Equal(InboundScanOutcome.Mint, s.Outcome);
        Assert.Null(s.Detail);
        Assert.Equal("C-NOT-ON-FILE", s.CoilNumber);
    }

    [Fact]
    public async Task A_coil_notified_on_several_edi_files_takes_the_newest_and_does_not_throw()
    {
        // SingleOrDefault would throw here — on exactly the coils most likely to need a human.
        Exec("""
            INSERT INTO inbound_coil (edi_file_id, bol, item_num, coil_number, net_weight, lot)
                 VALUES (600, 'BOL-OLD', 1, 'C-1005', 11000, 'LOT-OLD'),
                        (601, 'BOL-NEW', 1, 'C-1005', 12000, 'LOT-NEW');
            """);

        var s = await _repo.ScanInboundCoilAsync("C-1005", CancellationToken.None);

        Assert.Equal("LOT-NEW", s.Detail!.Lot);
        Assert.Equal(601, s.Detail.EdiFileId);
    }

    [Fact]
    public async Task The_no_barcode_label_looks_up_the_literal_text()
    {
        Exec("""
            INSERT INTO inbound_coil (edi_file_id, bol, item_num, coil_number, net_weight)
                 VALUES (700, 'BOL-9', 1, 'NO BARCODE', 9000);
            """);

        var s = await _repo.ScanInboundCoilAsync("000000", CancellationToken.None);

        Assert.True(s.NoBarcode);
        Assert.Equal("NO BARCODE", s.CoilNumber);
        Assert.Equal(9000m, s.Detail!.NetWeight);
    }

    [Fact]
    public async Task An_empty_scan_is_unreadable_and_touches_nothing()
    {
        var s = await _repo.ScanInboundCoilAsync("   ", CancellationToken.None);
        Assert.Equal(InboundScanOutcome.Unreadable, s.Outcome);
        Assert.Empty(s.MintedAbcNums);
        Assert.Null(s.Detail);
    }

    [Fact]
    public async Task A_scanned_quote_cannot_break_out_of_the_query()
    {
        // The CGI interpolated the scanned string straight into SQL. A scanner is an untrusted input
        // device; this must resolve to "no such coil", not an error or a wider match.
        Exec("""
            INSERT INTO inbound_coil_status (edi_file_id, bol, item_num, coil_number, coil_abc_num)
                 VALUES (800, 'BOL-X', 1, 'C-2001', 95001);
            """);

        var s = await _repo.ScanInboundCoilAsync("' OR '1'='1", CancellationToken.None);

        Assert.Equal(InboundScanOutcome.Mint, s.Outcome);   // matched nothing
        Assert.Empty(s.MintedAbcNums);
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
