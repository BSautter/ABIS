using Abis.Api.Data;
using Abis.Api.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The 6x10 shipping label's DATA — the retrieve behind <c>u_default_barcode.sru</c>.
///
/// <para><b>Why this is its own class with its own seed.</b> The shared fixture asserts exact skid ids
/// (<c>MAX(3004) + 1</c>) and per-job skid counts, so adding a skid to it moves other tests' answers.
/// Seeding here keeps the label's data next to the assertions that depend on it.</para>
///
/// <para>The numbers reproduce a PHOTOGRAPHED Novelis label (job 124401, skid T1846085): two coils
/// totalling 250 pieces and 4275 lb, which the label converts to 1939 kg.</para>
///
/// <para><b>What these tests are really guarding</b> is three places where the obvious column is wrong
/// and the wrong answer still prints something plausible — the reference order, the SUMmed weight, and
/// the display-number serial. None of them would look broken on paper.</para>
/// </summary>
public sealed class ShippingLabelDataTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public ShippingLabelDataTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_shiplabel_{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        SqliteFixture.EnsureCreatedAndSeeded(_cs);
        _repo = new AbisRepository(new DbConnectionFactory(new DatabaseOptions
        {
            Provider = "Sqlite", ConnectionString = _cs, Seed = true,
        }));
        Seed();
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection(_cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const long Skid = 9500;
    private const long PackingList = 8801;

    private void Seed()
    {
        // The skid points at job 1001 but its REFERENCE order is 9001/7001 — a RECTANGLE 48 x 24 whose
        // part is PN-3003-A. Job 1001's own order is what a naive port would read instead.
        Exec($"""
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt,
                sheet_tare_wt, skid_pieces, skid_sheet_status, sheet_theoretical_wt,
                ref_order_abc_num, ref_order_abc_item)
            VALUES ({Skid}, 1001, 'T1846085', 9999, 60, 999, 1, 4300, 9001, 7001)
            """);

        // Two production items, from two different coils, on the one skid.
        Exec("""
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status,
                prod_item_pieces, prod_item_net_wt, prod_item_placement)
            VALUES (9601, 5001, 1001, 1, 200, 3420, 'Edge'),
                   (9602, 5002, 1001, 1, 50,  855,  'Center')
            """);
        Exec($"INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES ({Skid}, 9601), ({Skid}, 9602)");

        // SK# 8 on shipment 8801.
        Exec($"""
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
            VALUES (8, {PackingList}, {Skid}, 1)
            """);

        // The inbound 863 behind SMELT and H.T. DATE. cash_date is YYYYMMDD TEXT, not a date.
        Exec("""
            INSERT INTO data_in_863 (edi_file_id, coil_num, primary_cntry_of_smelt, secondary_cntry_of_smelt, cash_date)
            VALUES (424261, 'ORG-5001', 'CA', 'AE', '20260717'),
                   (424262, 'ORG-5002', 'US', NULL, '20260718')
            """);
    }

    private Task<ShippingLabelPrintData?> Load(long? pl = PackingList) =>
        _repo.GetShippingLabelDataAsync(Skid, pl, CancellationToken.None);

    // ---- The three places the obvious column is wrong -------------------------------

    [Fact]
    public async Task The_part_and_PO_come_from_the_skids_REFERENCE_order_not_its_jobs_order()
    {
        // Legacy reads ab_job.order_abc_num, validates it, then does every customer and item lookup
        // against sheet_skid.ref_order_abc_num / ref_order_abc_item. A transferred skid keeps printing
        // its reference order's part — reading the job's order would put a DIFFERENT CUSTOMER'S part
        // number on a real skid, and the label would look perfectly normal.
        var d = await Load();
        Assert.NotNull(d);
        Assert.Equal(9001, d!.RefOrderAbcNum);
        Assert.Equal(7001, d.RefOrderAbcItem);
        Assert.Equal("PN-3003-A", d.PartNum);
        Assert.Equal("EU-7781", d.EnduserPo);
    }

    [Fact]
    public async Task Weight_and_pieces_are_SUMMED_over_the_skids_items_not_read_off_the_skid()
    {
        // sheet_skid.sheet_net_wt (9999) and skid_pieces (999) exist and are close enough to look
        // right on a label. Legacy sums the detail items instead: 3420 + 855 and 200 + 50.
        var d = await Load();
        Assert.Equal(4275m, d!.NetWtLb);
        Assert.Equal(250, d.Pieces);
    }

    [Fact]
    public async Task The_serial_is_the_display_number_which_is_not_a_number_at_all()
    {
        // serial_t.Text = Trim(ls_sheet_skid_display_num); the line printing the skid id is commented
        // out in the source. Real labels read T1846085.
        var d = await Load();
        Assert.Equal("T1846085", d!.SheetSkidDisplayNum);
        Assert.NotEqual(Skid.ToString(), d.SheetSkidDisplayNum);
    }

    // ---- The size block ---------------------------------------------------------------

    [Fact]
    public async Task The_size_comes_from_the_shape_table_for_the_items_sheet_type()
    {
        // Item 7001 is a RECTANGLE 48 long x 24 wide. order_item has a trimmed_coil_width column that
        // is NOT what the label prints.
        var d = await Load();
        Assert.Equal("RECTANGLE", d!.SheetType);
        Assert.Equal(24m, d.Width);
        Assert.Equal(48m, d.Length);
    }

    [Fact]
    public async Task A_circle_prints_its_diameter_as_the_width_and_zero_as_the_length()
    {
        // Legacy sets lr_length = 0 explicitly for a circle, so the label reads "gauge X diameter X 0".
        // Leaving length null would print a blank where the plant expects a zero.
        Exec($"UPDATE sheet_skid SET ref_order_abc_item = 7002 WHERE sheet_skid_num = {Skid}");
        var d = await Load();
        Assert.Equal("CIRCLE", d!.SheetType);
        Assert.Equal(36.5m, d.Width);
        Assert.Equal(0m, d.Length);
    }

    [Fact]
    public async Task An_unknown_sheet_type_leaves_the_size_blank_rather_than_failing()
    {
        // Legacy's CASE ELSE sets both to 0 and prints on. A skid must not become unshippable because
        // its shape is one the label does not know.
        Exec("UPDATE order_item SET sheet_type = 'SOMETHING-NEW' WHERE order_abc_num = 9001 AND order_item_num = 7001");
        var d = await Load();
        Assert.NotNull(d);
        Assert.Null(d!.Width);
        Assert.Null(d.Length);
    }

    // ---- The lot table ------------------------------------------------------------------

    [Fact]
    public async Task Every_coil_on_the_skid_gets_a_lot_row_with_its_smelt_and_heat_date()
    {
        var d = await Load();
        Assert.Equal(2, d!.Lots.Count);

        var first = d.Lots[0];
        Assert.Equal("LOT-1", first.LotNum);
        Assert.Equal("ORG-5001", first.CoilOrgNum);
        Assert.Equal(200, first.Pieces);
        Assert.Equal("CA", first.PrimarySmelt);
        Assert.Equal("AE", first.SecondarySmelt);
        Assert.Equal("20260717", first.HeatDate);   // YYYYMMDD TEXT, not a date
    }

    [Fact]
    public async Task A_coil_with_no_863_still_gets_its_lot_row()
    {
        // The SHIPPING label is not gated on the 863 — only the certificate is. Dropping the row would
        // silently under-report what is on the skid.
        Exec("DELETE FROM data_in_863");
        var d = await Load();
        Assert.Equal(2, d!.Lots.Count);
        Assert.All(d.Lots, l => Assert.Null(l.PrimarySmelt));
        Assert.Equal("LOT-1", d.Lots[0].LotNum);
    }

    [Fact]
    public async Task The_heat_number_is_the_skids_first_lot()
    {
        var d = await Load();
        Assert.Equal("LOT-1", d!.Heat);
    }

    // ---- The placement footer -------------------------------------------------------------

    [Fact]
    public async Task Several_items_join_their_DISTINCT_placements_with_a_slash()
    {
        // Legacy's loop, and why live data holds "Edge/Center".
        var d = await Load();
        Assert.Contains("Edge", d!.Place);
        Assert.Contains("Center", d.Place);
        Assert.Contains("/", d.Place);
    }

    [Fact]
    public async Task Identical_placements_are_not_repeated()
    {
        Exec("UPDATE production_sheet_item SET prod_item_placement = 'Edge' WHERE prod_item_num IN (9601, 9602)");
        var d = await Load();
        Assert.Equal("Edge", d!.Place);
    }

    // ---- What belongs to the shipment rather than the skid -----------------------------------

    [Fact]
    public async Task SK_number_is_the_packing_item_number_not_the_skid_id()
    {
        var d = await Load();
        Assert.Equal(8, d!.PackingItemNum);
        Assert.NotEqual(Skid, (long?)d.PackingItemNum);
    }

    [Fact]
    public async Task A_reprint_with_no_shipment_leaves_SK_and_the_date_blank_rather_than_guessing()
    {
        // Both belong to the shipment. Inventing one would print a skid onto a shipment it is not on.
        var d = await Load(pl: null);
        Assert.NotNull(d);
        Assert.Null(d!.PackingItemNum);
        Assert.Null(d.ShippingDate);
        Assert.Equal("PN-3003-A", d.PartNum);   // everything else still resolves
    }

    // ---- The address ----------------------------------------------------------------------

    [Fact]
    public async Task The_address_is_name_city_state_zip_with_the_street_left_out()
    {
        // Legacy builds it with the street-bearing line commented out directly above, and the
        // photographed label matches: "NOVELIS ALUMINUM CORPORATION-OSWEGO,  OSWEGO,  NY 13126".
        // The exact spacing is legacy's: three spaces after the name, two after the city, two before
        // the zip. Pinned rather than approximated because it is what the plant has always shipped.
        var d = await Load();
        Assert.Equal("ACME METALS,   Detroit,  MI  48201", d!.Address);
    }

    // ---- Absent data ------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_skid_returns_null_rather_than_an_empty_label()
    {
        Assert.Null(await _repo.GetShippingLabelDataAsync(999_999, PackingList, CancellationToken.None));
    }

    [Fact]
    public async Task A_skid_with_no_reference_order_still_returns_a_row()
    {
        // It prints a label with blanks, which is what legacy's separate SELECTs produce when they find
        // nothing. Refusing would make the skid unshippable over a data gap.
        Exec($"UPDATE sheet_skid SET ref_order_abc_num = NULL, ref_order_abc_item = NULL WHERE sheet_skid_num = {Skid}");
        var d = await Load();
        Assert.NotNull(d);
        Assert.Null(d!.PartNum);
        Assert.Equal(4275m, d.NetWtLb);   // the skid's own totals still resolve
    }

    // ---- The shipment's print order -----------------------------------------------------------

    [Fact]
    public async Task A_shipments_skids_come_back_in_packing_item_order()
    {
        // It is the order the labels come off the printer, and the operator matches them to skids by
        // SK#. Seeded out of order on purpose.
        Exec($"""
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
            VALUES (3, {PackingList}, 3001, 1), (5, {PackingList}, 3002, 1)
            """);
        var skids = await _repo.GetShipmentSkidNumbersAsync(PackingList, CancellationToken.None);
        Assert.Equal([3001L, 3002L, Skid], skids);
    }

    [Fact]
    public async Task A_shipment_with_no_skids_comes_back_empty_not_null()
    {
        Assert.Empty(await _repo.GetShipmentSkidNumbersAsync(8802, CancellationToken.None));
    }

    // ---- Configuration that has to survive systemd -------------------------------------------

    [Fact]
    public void The_shipping_printer_device_name_is_a_legal_environment_variable_name()
    {
        // The server configures routing through systemd's EnvironmentFile, where this string becomes
        // part of a variable NAME: LabelPrinters__DeviceRouting__<device>. systemd silently SKIPS a
        // line whose key is not a legal variable name, so a hyphen here does not fail loudly — the
        // label just prints nowhere, and the only symptom is a 503 at the dock.
        //
        // The per-line routing keys already cost a redeploy over exactly this (a ':' in "6:offload").
        // This is the same bug in a different place, caught before it shipped.
        var device = Abis.Api.Endpoints.ApiEndpoints.ShippingLabelDevice;
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", device);
        Assert.DoesNotContain("-", device);
        Assert.DoesNotContain(":", device);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* temp file */ }
    }
}
