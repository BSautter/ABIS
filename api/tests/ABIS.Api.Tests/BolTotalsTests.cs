using Abis.Api.Data;
using Abis.Api.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The bill-of-lading package note and rollups — the port of legacy <c>rpabco/f_get_bol_totals.srf</c>.
/// <para>The note is printed paperwork a driver and a receiving dock read by eye, so the exact wording
/// is the contract: these tests pin the format character-for-character, including the quirks that would
/// otherwise look like bugs worth "fixing".</para>
/// </summary>
public sealed class BolPackageFormatTests
{
    private static BolTotalsGroup G(int count, decimal weight) => new() { Count = count, GrossWeight = weight };
    private static readonly BolTotalsGroup None = G(0, 0);

    [Fact]
    public void Builds_the_legacy_layout_exactly()
    {
        var text = BolPackage.Build(41234, G(3, 4500), G(2, 900), None);

        // "~n" is a newline and "~n   " a newline + three spaces in PowerBuilder; every present group
        // contributes that run, INCLUDING the last, so the note ends with it.
        Assert.Equal(
            "Shipping with BOL 41234:\n" +
            "3 Sheet Skids. Total Gross Weight 4500\n   " +
            "2 Scrap Skids. Total Gross Weight 900\n   ",
            text);
    }

    [Fact]
    public void Omits_an_empty_group_entirely_rather_than_printing_a_zero_line()
    {
        var text = BolPackage.Build(7, None, None, G(1, 12000));
        Assert.Equal("Shipping with BOL 7:\n1 coil(s). Total Gross Weight 12000\n   ", text);
        Assert.DoesNotContain("Sheet Skids", text);
        Assert.DoesNotContain("0 ", text);
    }

    [Fact]
    public void Coil_group_keeps_the_legacy_wording()
    {
        // Legacy says "N coil(s)." for reject coils, not "N Reject Coils." like the skid groups.
        // Deliberately preserved: the plant reads these forms and inconsistent wording is still the
        // wording they know.
        var text = BolPackage.Build(1, None, None, G(4, 50));
        Assert.Contains("4 coil(s). Total Gross Weight 50", text);
    }

    [Fact]
    public void Weights_render_as_whole_numbers()
    {
        // The source columns are decimal(0) and legacy summed them into a Long, so no decimal ever
        // reached the printed form. A fractional value truncates rather than rendering "4500.60".
        var text = BolPackage.Build(2, G(1, 4500.6m), None, None);
        Assert.Contains("Total Gross Weight 4500", text);
        Assert.DoesNotContain("4500.6", text);
    }

    [Fact]
    public void Empty_bol_still_carries_the_header()
    {
        Assert.Equal("Shipping with BOL 99:\n", BolPackage.Build(99, None, None, None));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("CUSTOMER REF 12", false)]
    [InlineData("Shipping with BOL 41234:\n2 Sheet Skids...", true)]
    [InlineData("REF 9 / Shipping with BOL 8:\n", true)]   // marker anywhere in the field counts
    public void Detects_an_already_stamped_note(string? codes, bool expected) =>
        Assert.Equal(expected, BolPackage.IsStored(codes));
}

/// <summary>The rollups against a seeded database — scope (whole BOL, not one stop), the single- vs
/// multi-stop rule, and the stored-note precedence.</summary>
public sealed class BolTotalsRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public BolTotalsRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_bol_{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        SqliteFixture.EnsureCreatedAndSeeded(_cs);
        _repo = new AbisRepository(new DbConnectionFactory(new DatabaseOptions
        {
            Provider = "Sqlite",
            ConnectionString = _cs,
            Seed = true,
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

    /// <summary>A two-stop BOL 5000: stop 9001 carries two sheet skids, stop 9002 one scrap skid and a
    /// reject coil. The point of the fixture is that the rollup must span BOTH stops.</summary>
    private void SeedMultiStop()
    {
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9001, 5000), (9002, 5000);

            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt)
                 VALUES (8801, 'SS-8801', 1000, 50), (8802, 'SS-8802', 2000, 60);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9001, 8801, 1), (2, 9001, 8802, 2);

            INSERT INTO scrap_skid (scrap_skid_num, scrap_skid_display_num, scrap_net_wt, scrap_tare_wt)
                 VALUES (7701, 'SC-7701', 300, 25);
            INSERT INTO scrap_packing_item (sc_packing_item, packing_list, scrap_skid_num, scrap_packaging_ticket)
                 VALUES (1, 9002, 7701, 1);

            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance)
                 VALUES (6601, 'CUST-6601', 'LOT-1', 12500, 12000);
            INSERT INTO reject_coil_packing_item (rej_coil_packing_item, packing_list, coil_abc_num, rej_coil_packaging_ticket)
                 VALUES (1, 9002, 6601, 1);
            """);
    }

    [Fact]
    public async Task Rolls_up_every_stop_on_the_bill_of_lading_not_just_this_one()
    {
        // The whole reason the totals exist: stop 9001 has no scrap and no reject coil of its OWN, but
        // its paperwork must still describe the truck. A per-packing-list read would report zeroes here.
        SeedMultiStop();
        var t = await _repo.GetBolTotalsAsync(9001, CancellationToken.None);

        Assert.NotNull(t);
        Assert.Equal(5000, t!.BillOfLading);
        Assert.Equal(2, t.StopCount);
        Assert.True(t.MultiStop);

        Assert.Equal(2, t.Sheet.Count);
        Assert.Equal(3110m, t.Sheet.GrossWeight);        // (1000+50) + (2000+60) — gross, tare included
        Assert.Equal(["SS-8801", "SS-8802"], t.Sheet.Items);

        Assert.Equal(1, t.Scrap.Count);
        Assert.Equal(325m, t.Scrap.GrossWeight);         // 300 + 25

        Assert.Equal(1, t.RejectCoil.Count);
        Assert.Equal(12000m, t.RejectCoil.GrossWeight);  // net_wt_balance; a coil has no skid tare
        Assert.Equal(["CUST-6601"], t.RejectCoil.Items); // identified by the CUSTOMER's number
    }

    [Fact]
    public async Task Both_stops_see_the_same_note()
    {
        SeedMultiStop();
        var a = await _repo.GetBolTotalsAsync(9001, CancellationToken.None);
        var b = await _repo.GetBolTotalsAsync(9002, CancellationToken.None);

        Assert.Equal(
            "Shipping with BOL 5000:\n" +
            "2 Sheet Skids. Total Gross Weight 3110\n   " +
            "1 Scrap Skids. Total Gross Weight 325\n   " +
            "1 coil(s). Total Gross Weight 12000\n   ",
            a!.PackageText);
        Assert.Equal(a.PackageText, b!.PackageText);
    }

    [Fact]
    public async Task A_single_stop_bol_gets_totals_but_no_note()
    {
        // Faithful to legacy, which returns 0 immediately and leaves the note untouched: the note exists
        // to tell one stop what ELSE is on the truck, and on a single-stop load that is nothing.
        // The rollups are still computed — the printed BOL needs them either way.
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9100, 5100);
            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt)
                 VALUES (8810, 'SS-8810', 500, 10);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9100, 8810, 1);
            """);

        var t = await _repo.GetBolTotalsAsync(9100, CancellationToken.None);

        Assert.False(t!.MultiStop);
        Assert.Equal(1, t.StopCount);
        Assert.Null(t.PackageText);
        Assert.Equal(1, t.Sheet.Count);
        Assert.Equal(510m, t.Sheet.GrossWeight);
    }

    [Fact]
    public async Task A_note_already_stamped_on_the_stop_wins_over_a_fresh_count()
    {
        // Paperwork already in a driver's hand must not be contradicted by a later recount, so the
        // stored value is returned verbatim even though the live skids would total differently.
        SeedMultiStop();
        Exec("UPDATE shipment SET shipment_reference_codes = 'Shipping with BOL 5000:\nAS PRINTED EARLIER\n   ' WHERE packing_list = 9001;");

        var t = await _repo.GetBolTotalsAsync(9001, CancellationToken.None);

        Assert.True(t!.PackageTextStored);
        Assert.Equal("Shipping with BOL 5000:\nAS PRINTED EARLIER\n   ", t.PackageText);
        Assert.Equal(2, t.Sheet.Count);   // the rollups are still live — only the NOTE is frozen
    }

    [Fact]
    public async Task Reference_codes_without_the_marker_do_not_suppress_a_fresh_note()
    {
        SeedMultiStop();
        Exec("UPDATE shipment SET shipment_reference_codes = 'CUSTOMER REF 12345' WHERE packing_list = 9001;");

        var t = await _repo.GetBolTotalsAsync(9001, CancellationToken.None);

        Assert.False(t!.PackageTextStored);
        Assert.StartsWith("Shipping with BOL 5000:", t.PackageText);
    }

    [Fact]
    public async Task Unknown_packing_list_or_no_bill_of_lading_returns_null()
    {
        Assert.Null(await _repo.GetBolTotalsAsync(999999, CancellationToken.None));

        Exec("INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9200, NULL);");
        Assert.Null(await _repo.GetBolTotalsAsync(9200, CancellationToken.None));
    }

    [Fact]
    public async Task A_bol_with_no_line_items_totals_to_zero_without_failing()
    {
        Exec("INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9300, 5300), (9301, 5300);");
        var t = await _repo.GetBolTotalsAsync(9300, CancellationToken.None);

        Assert.Equal(0, t!.Sheet.Count);
        Assert.Equal(0m, t.Sheet.GrossWeight);
        Assert.Equal("Shipping with BOL 5300:\n", t.PackageText);   // header only
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
