using Abis.Api.Data;
using Abis.Api.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The printed bill of lading's data (legacy <c>rpabco/u_default_billoflading.sru</c>). The form is what
/// a driver hands to a receiving dock, so the arithmetic and the section structure are pinned here —
/// including the two places the legacy logic is easy to "clean up" into being wrong.
/// </summary>
public sealed class BolDocumentTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public BolDocumentTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_boldoc_{Guid.NewGuid():N}.db");
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

    /// <summary>One stop with two sheet skids on ONE job, a scrap skid and a reject coil — one of each
    /// section, so every total is distinguishable.</summary>
    private void SeedShipment()
    {
        Exec("""
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, customer_city, customer_state, customer_zip)
                 VALUES (7100, 'Consignee Motors Inc', 'CONSIGNEE', 'Detroit', 'MI', '48201');
            INSERT INTO shipment (packing_list, bill_of_lading, customer_id, des_sh_cust_id, vehicle_id)
                 VALUES (9500, 5500, 7100, 7100, 'TRL-77');

            INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po)
                 VALUES (4400, 7100, 'PO-ORIG-1', 'PO-END-1');
            INSERT INTO order_item (order_abc_num, order_item_num, enduser_part_num, supplier_code, sheet_type)
                 VALUES (4400, 1, 'PART-XYZ', 'SUP-9', 'RECTANGLE');
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3300, 4400, 1);

            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, ab_job_num,
                                    sheet_net_wt, sheet_tare_wt, skid_pieces, ref_order_abc_num, ref_order_abc_item)
                 VALUES (8900, 'SS-8900', 3300, 1000, 50, 120, 4400, 1),
                        (8901, 'SS-8901', 3300, 2000, 60, 240, 4400, 1);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9500, 8900, 1), (2, 9500, 8901, 2);

            INSERT INTO scrap_skid (scrap_skid_num, scrap_skid_display_num, scrap_net_wt, scrap_tare_wt)
                 VALUES (7800, 'SC-7800', 300, 25);
            INSERT INTO scrap_packing_item (sc_packing_item, packing_list, scrap_skid_num, scrap_packaging_ticket)
                 VALUES (1, 9500, 7800, 1);

            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance)
                 VALUES (6700, 'CUST-6700', 'LOT-2', 12500, 12000);
            INSERT INTO reject_coil_packing_item (rej_coil_packing_item, packing_list, coil_abc_num, rej_coil_packaging_ticket)
                 VALUES (1, 9500, 6700, 1);
            """);
    }

    [Fact]
    public async Task Builds_the_three_sections_with_the_legacy_headings()
    {
        SeedShipment();
        var d = await _repo.GetBolDocumentAsync(9500, CancellationToken.None);

        Assert.NotNull(d);
        Assert.Equal("Skids of Aluminum Sheets", d!.Sheet.Heading);
        Assert.Equal("Accumulated Scrap Return", d.Scrap.Heading);
        Assert.Equal("Rejected Coil Return", d.RejectCoil.Heading);

        Assert.Equal(2, d.Sheet.Units);
        Assert.Equal(3110m, d.Sheet.GrossWeight);   // (1000+50) + (2000+60)
        Assert.Equal(3000m, d.Sheet.NetWeight);
        Assert.Equal(360, d.Sheet.Pieces);

        Assert.Equal(1, d.Scrap.Units);
        Assert.Equal(325m, d.Scrap.GrossWeight);    // 300 + 25

        Assert.Equal(1, d.RejectCoil.Units);
        Assert.Equal(12000m, d.RejectCoil.GrossWeight);   // no tare on a coil
    }

    [Fact]
    public async Task Section_weight_is_gross_but_the_job_subtotal_is_net()
    {
        // The easiest thing to "tidy" into a bug. Legacy accumulates net+tare for the section and bare
        // net for the job block; making them agree would silently change what prints on the form.
        SeedShipment();
        var d = await _repo.GetBolDocumentAsync(9500, CancellationToken.None);

        Assert.Equal(3110m, d!.Sheet.GrossWeight);
        Assert.Equal(3000m, Assert.Single(d.Jobs).SubTotalNetWeight);
    }

    [Fact]
    public async Task Totals_are_the_sum_of_the_three_sections()
    {
        SeedShipment();
        var d = await _repo.GetBolDocumentAsync(9500, CancellationToken.None);

        Assert.Equal(3110m + 325m + 12000m, d!.TotalWeight);
        Assert.Equal(4, d.TotalItems);   // 2 sheet + 1 scrap + 1 reject coil
        Assert.False(d.Empty);
    }

    [Fact]
    public async Task Job_block_reads_the_skids_reference_order_not_the_jobs_own()
    {
        // legacy pulls PO/part/supplier via sheet_skid.ref_order_abc_num / ref_order_abc_item. Pointing
        // the skid at a DIFFERENT order from the job's proves which one the form actually prints.
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9510, 5510);
            INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po)
                 VALUES (4410, 7100, 'PO-JOB-ORDER', 'END-JOB'), (4411, 7100, 'PO-REF-ORDER', 'END-REF');
            INSERT INTO order_item (order_abc_num, order_item_num, enduser_part_num, supplier_code, sheet_type)
                 VALUES (4410, 1, 'PART-JOB', 'SUP-JOB', 'RECTANGLE'), (4411, 1, 'PART-REF', 'SUP-REF', 'RECTANGLE');
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3310, 4410, 1);
            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, ab_job_num,
                                    sheet_net_wt, sheet_tare_wt, ref_order_abc_num, ref_order_abc_item)
                 VALUES (8910, 'SS-8910', 3310, 500, 10, 4411, 1);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9510, 8910, 1);
            """);

        var job = Assert.Single((await _repo.GetBolDocumentAsync(9510, CancellationToken.None))!.Jobs);

        Assert.Equal("PO-REF-ORDER", job.OrigCustomerPo);
        Assert.Equal("PART-REF", job.PartNum);
        Assert.Equal("SUP-REF", job.SupplierCode);
    }

    [Fact]
    public async Task Groups_skids_by_job_with_per_job_counts()
    {
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9520, 5520);
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3320, 4400, 1), (3321, 4400, 1);
            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, ab_job_num, sheet_net_wt, sheet_tare_wt)
                 VALUES (8920, 'A', 3320, 100, 5), (8921, 'B', 3320, 200, 5), (8922, 'C', 3321, 300, 5);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9520, 8920, 1), (2, 9520, 8921, 2), (3, 9520, 8922, 3);
            """);

        var d = await _repo.GetBolDocumentAsync(9520, CancellationToken.None);

        Assert.Equal(2, d!.Jobs.Count);
        Assert.Equal(2, d.Jobs.Single(j => j.AbJobNum == 3320).Units);
        Assert.Equal(300m, d.Jobs.Single(j => j.AbJobNum == 3320).SubTotalNetWeight);
        Assert.Equal(1, d.Jobs.Single(j => j.AbJobNum == 3321).Units);
        Assert.True(d.DetailsPrintable);
    }

    [Fact]
    public async Task More_than_three_jobs_cannot_print_details_but_totals_stay_right()
    {
        // The legacy form has exactly three per-job note blocks and refuses past that. Reported as a flag
        // rather than an error, because the TOTALS are still correct — only the layout can't hold it.
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9530, 5530);
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3330, 4400, 1), (3331, 4400, 1), (3332, 4400, 1), (3333, 4400, 1);
            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, ab_job_num, sheet_net_wt, sheet_tare_wt)
                 VALUES (8930, 'A', 3330, 100, 1), (8931, 'B', 3331, 100, 1),
                        (8932, 'C', 3332, 100, 1), (8933, 'D', 3333, 100, 1);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9530, 8930, 1), (2, 9530, 8931, 2), (3, 9530, 8932, 3), (4, 9530, 8933, 4);
            """);

        var d = await _repo.GetBolDocumentAsync(9530, CancellationToken.None);

        Assert.Equal(4, d!.Jobs.Count);
        Assert.False(d.DetailsPrintable);
        Assert.Equal(404m, d.TotalWeight);
        Assert.Equal(4, d.TotalItems);
    }

    [Fact]
    public async Task Exactly_three_jobs_still_prints_details()
    {
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9540, 5540);
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3340, 4400, 1), (3341, 4400, 1), (3342, 4400, 1);
            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, ab_job_num, sheet_net_wt, sheet_tare_wt)
                 VALUES (8940, 'A', 3340, 100, 1), (8941, 'B', 3341, 100, 1), (8942, 'C', 3342, 100, 1);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9540, 8940, 1), (2, 9540, 8941, 2), (3, 9540, 8942, 3);
            """);

        var d = await _repo.GetBolDocumentAsync(9540, CancellationToken.None);
        Assert.Equal(3, d!.Jobs.Count);
        Assert.True(d.DetailsPrintable);
    }

    [Fact]
    public async Task An_empty_shipment_is_flagged_rather_than_printed_blank()
    {
        // Legacy stops with "There is nothing to ship in this shipment!". A blank BOL handed to a driver
        // is worse than no BOL, so the caller has to be able to refuse.
        Exec("INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9550, 5550);");
        var d = await _repo.GetBolDocumentAsync(9550, CancellationToken.None);

        Assert.True(d!.Empty);
        Assert.Equal(0, d.TotalItems);
        Assert.Equal(0m, d.TotalWeight);
        Assert.Empty(d.Jobs);
    }

    [Fact]
    public async Task Carries_the_parties_and_the_whole_bols_totals()
    {
        SeedShipment();
        var d = await _repo.GetBolDocumentAsync(9500, CancellationToken.None);

        Assert.Equal(5500, d!.BillOfLading);
        Assert.Equal("Consignee Motors Inc", d.ConsigneeName);
        Assert.Equal("Detroit", d.ConsigneeCity);
        Assert.Equal("MI", d.ConsigneeState);
        Assert.Equal("TRL-77", d.VehicleId);

        // The BOL-wide rollup rides along, so one read serves the whole printed form.
        Assert.NotNull(d.BolTotals);
        Assert.Equal(5500, d.BolTotals!.BillOfLading);
        Assert.False(d.BolTotals.MultiStop);   // single stop → no package note
        Assert.Null(d.BolTotals.PackageText);
    }

    [Fact]
    public async Task Unknown_packing_list_returns_null()
        => Assert.Null(await _repo.GetBolDocumentAsync(999999, CancellationToken.None));

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
