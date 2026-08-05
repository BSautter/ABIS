using Abis.Api.Data;
using Abis.Api.Documents;
using Abis.Api.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The per-skid packing ticket (legacy <c>rpabco/d_packaging_ticket_{sheet,scrap,rejcoil}_4skid</c>) —
/// the paper stapled to a single unit, distinct from the packing list for a whole shipment.
/// </summary>
public sealed class PackingTicketTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public PackingTicketTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_ticket_{Guid.NewGuid():N}.db");
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

    private void SeedSheet(string usePackageNum = "N")
    {
        Exec($"""
            INSERT INTO customer (customer_id, customer_full_name, customer_short_name, use_package_num)
                 VALUES (7200, 'Consignee Motors', 'CONSIGNEE', '{usePackageNum}');
            INSERT INTO shipment (packing_list, bill_of_lading, customer_id, des_sh_cust_id, shipment_athorization_code)
                 VALUES (9600, 5600, 7200, 7200, 'AUTH-42');
            INSERT INTO customer_order (order_abc_num, orig_customer_id, orig_customer_po, enduser_po)
                 VALUES (4500, 7200, 'PO-CUST-1', 'PO-END-1');
            INSERT INTO order_item (order_abc_num, order_item_num, enduser_part_num, sheet_type,
                                    alloy2, temper, gauge, govt_contract_num)
                 VALUES (4500, 1, 'PART-777', 'RECTANGLE', '5052', 'H32', 0.125, 'GC-9');
            INSERT INTO rectangle (order_abc_num, order_item_num, rt_length, rt_width)
                 VALUES (4500, 1, 48.5, 24.25);
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3400, 4500, 1);
            INSERT INTO sheet_skid (sheet_skid_num, sheet_skid_display_num, ab_job_num, sheet_net_wt,
                                    sheet_tare_wt, skid_pieces, ref_order_abc_num, ref_order_abc_item)
                 VALUES (8990, 'SS-8990', 3400, 1000, 50, 120, 4500, 1);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
                 VALUES (1, 9600, 8990, 31);
            INSERT INTO sheet_skid_package (sheet_skid_num, package_num) VALUES (8990, 'PKG-ABC');
            """);
    }

    [Fact]
    public async Task Sheet_ticket_carries_the_skid_order_and_shipment_data()
    {
        SeedSheet();
        var t = await _repo.GetSkidPackingTicketAsync("SHEET", 9600, 8990, CancellationToken.None);

        Assert.NotNull(t);
        Assert.Equal("SHEET", t!.ItemType);
        Assert.Equal(31, t.PackagingTicket);
        Assert.Equal("SS-8990", t.SkidDisplayNum);
        Assert.Equal(3400, t.AbJobNum);
        Assert.Equal("PART-777", t.PartNum);
        Assert.Equal("5052", t.Alloy);
        Assert.Equal("H32", t.Temper);
        Assert.Equal(0.125m, t.Gauge);
        Assert.Equal("GC-9", t.GovtContractNum);
        Assert.Equal("PO-CUST-1", t.OrigCustomerPo);
        Assert.Equal(5600, t.BillOfLading);
        Assert.Equal(1050m, t.GrossWeight);   // 1000 + 50
        Assert.Equal(120, t.Pieces);
    }

    [Fact]
    public async Task Sheet_ticket_resolves_the_blanks_shape_dimensions()
    {
        // Legacy outer-joins eight shape tables at once; here the line's sheet_type picks the one table.
        SeedSheet();
        var t = await _repo.GetSkidPackingTicketAsync("SHEET", 9600, 8990, CancellationToken.None);

        Assert.Equal("RECTANGLE", t!.Shape?.ShapeType);
        Assert.Equal(48.5m, t.Shape!.Dimensions.Single(d => d.Name == "length").Value);
        Assert.Equal(24.25m, t.Shape.Dimensions.Single(d => d.Name == "width").Value);
    }

    [Fact]
    public async Task Package_number_prints_only_for_customers_that_use_them()
    {
        SeedSheet(usePackageNum: "N");
        Assert.Null((await _repo.GetSkidPackingTicketAsync("SHEET", 9600, 8990, CancellationToken.None))!.CustomerPackageNum);

        Exec("UPDATE customer SET use_package_num = 'Y' WHERE customer_id = 7200;");
        Assert.Equal("PKG-ABC", (await _repo.GetSkidPackingTicketAsync("SHEET", 9600, 8990, CancellationToken.None))!.CustomerPackageNum);
    }

    [Fact]
    public async Task Scrap_ticket_carries_its_own_fields()
    {
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9610, 5610);
            INSERT INTO scrap_skid (scrap_skid_num, scrap_skid_display_num, scrap_ab_job_num, scrap_alloy2,
                                    scrap_temper, scrap_net_wt, scrap_tare_wt, scrap_cust_po, scrap_notes, trailer_name)
                 VALUES (7900, 'SC-7900', 'J-77', '3003', 'H14', 300, 25, 'PO-SCRAP', 'mixed skeleton', 'TRL-9');
            INSERT INTO scrap_packing_item (sc_packing_item, packing_list, scrap_skid_num, scrap_packaging_ticket)
                 VALUES (1, 9610, 7900, 12);
            """);

        var t = await _repo.GetSkidPackingTicketAsync("SCRAP", 9610, 7900, CancellationToken.None);

        Assert.Equal("SCRAP", t!.ItemType);
        Assert.Equal("SC-7900", t.SkidDisplayNum);
        Assert.Equal("J-77", t.ScrapJobNum);
        Assert.Equal("PO-SCRAP", t.ScrapCustomerPo);
        Assert.Equal("mixed skeleton", t.Notes);
        Assert.Equal("TRL-9", t.TrailerName);
        Assert.Equal(325m, t.GrossWeight);
        Assert.Null(t.AuthorizationCode);   // authorization is a rejected-coil concern
    }

    [Fact]
    public async Task Reject_coil_ticket_carries_the_coil_identity_and_authorization()
    {
        SeedSheet();   // reuses shipment 9600, which has the authorization code
        Exec("""
            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, coil_alloy2, coil_temper,
                              coil_gauge, coil_width, net_wt, net_wt_balance, coil_notes)
                 VALUES (6800, 'CUST-6800', 'LOT-9', '6061', 'T6', 0.08, 60.5, 12500, 12000, 'edge damage');
            INSERT INTO reject_coil_packing_item (rej_coil_packing_item, packing_list, coil_abc_num, rej_coil_packaging_ticket)
                 VALUES (1, 9600, 6800, 55);
            """);

        var t = await _repo.GetSkidPackingTicketAsync("REJECT_COIL", 9600, 6800, CancellationToken.None);

        Assert.Equal("REJECT_COIL", t!.ItemType);
        Assert.Equal("CUST-6800", t.CoilOrgNum);
        Assert.Equal("LOT-9", t.LotNum);
        Assert.Equal(60.5m, t.Width);
        Assert.Equal(12000m, t.NetWeight);
        Assert.Equal(12000m, t.GrossWeight);   // a coil has no skid tare
        Assert.Equal("edge damage", t.Notes);
        Assert.Equal("AUTH-42", t.AuthorizationCode);
        Assert.Equal(55, t.PackagingTicket);
    }

    [Fact]
    public async Task Unknown_type_or_a_unit_not_on_the_list_returns_null()
    {
        SeedSheet();
        Assert.Null(await _repo.GetSkidPackingTicketAsync("NOPE", 9600, 8990, CancellationToken.None));
        Assert.Null(await _repo.GetSkidPackingTicketAsync("SHEET", 9600, 999999, CancellationToken.None));
        Assert.Null(await _repo.GetSkidPackingTicketAsync("SHEET", 999999, 8990, CancellationToken.None));
    }

    [Fact]
    public async Task Renders_a_self_contained_printable_ticket()
    {
        SeedSheet(usePackageNum: "Y");
        var t = await _repo.GetSkidPackingTicketAsync("SHEET", 9600, 8990, CancellationToken.None);
        var html = HtmlDocuments.SkidPackingTicketDoc(t!);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("SHEET SKID PACKING TICKET", html);
        Assert.Contains("SS-8990", html);
        Assert.Contains("PART-777", html);
        Assert.Contains("length 48.5", html);        // shape dimensions on the ticket
        Assert.Contains("PKG-ABC", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task A_row_with_no_data_is_absent_rather_than_blank()
    {
        // Legacy uses a separate DataWindow per variant; one layout here achieves the same by omitting
        // rows a variant has nothing for — a scrap ticket must not show an empty "Part #" line.
        Exec("""
            INSERT INTO shipment (packing_list, bill_of_lading) VALUES (9620, 5620);
            INSERT INTO scrap_skid (scrap_skid_num, scrap_skid_display_num, scrap_net_wt, scrap_tare_wt)
                 VALUES (7910, 'SC-7910', 100, 0);
            INSERT INTO scrap_packing_item (sc_packing_item, packing_list, scrap_skid_num, scrap_packaging_ticket)
                 VALUES (1, 9620, 7910, 1);
            """);

        var html = HtmlDocuments.SkidPackingTicketDoc(
            (await _repo.GetSkidPackingTicketAsync("SCRAP", 9620, 7910, CancellationToken.None))!);

        Assert.Contains("SCRAP SKID PACKING TICKET", html);
        Assert.DoesNotContain("Part #", html);
        Assert.DoesNotContain("Govt contract", html);
        Assert.DoesNotContain("Tare wt", html);        // tare is 0 here, so the row is dropped
        Assert.Contains("Net wt", html);
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
