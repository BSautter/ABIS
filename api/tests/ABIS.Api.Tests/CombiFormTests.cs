using Abis.Api.Data;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The combi form — a packing list and a weight certificate in one, which is why every figure prints
/// in pounds and kilograms.
/// <para>Two things here are commercial rather than technical, so they are pinned:</para>
/// <para><b>The theoretical-weight substitution.</b> Legacy hard-codes <c>customer_id = 2802</c>
/// (TOYOTA TSUSHO AMERICA — 50 shipments on live <c>.230</c>) into four of its combi sheet-detail
/// queries, printing <c>prod_item_theoretical_wt</c> in the column labelled net. Getting this wrong
/// means invoicing a customer on the wrong basis, and nothing in the output would show it.</para>
/// <para><b>The pounds→kilograms factor is 0.45359</b>, which is neither the exact 0.45359237 nor the
/// <c>0.453592</c> that <c>u_default_combi_1999*</c> uses elsewhere in the same feature. Legacy is
/// internally inconsistent; the detail reports' figure is the one the customer has been receiving, and
/// on a certificate matching that matters more than being arithmetically ideal.</para>
/// </summary>
public class CombiFormTests
{
    private readonly IAbisRepository _repo;
    private static readonly long[] None = Array.Empty<long>();

    private const long Pl = 8860;

    public CombiFormTests()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"abis_combi_{Guid.NewGuid():N}.db");
        var options = new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={dbPath}", Seed = true };
        SqliteFixture.EnsureCreatedAndSeeded(options.ConnectionString);
        _repo = new AbisRepository(new DbConnectionFactory(options));

        // Seeded here rather than in the shared fixture: sheet_packing_item is not seeded there at
        // all, and a combi form needs all three sections plus BOTH a net and a theoretical weight that
        // DIFFER — without that difference the substitution rule is untestable, which is exactly how a
        // commercial rule slips through unnoticed.
        using var conn = new DbConnectionFactory(options).Create();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO shipment (packing_list, bill_of_lading, customer_id, vehicle_id, shipment_status, shipment_actualed_date_time)
              VALUES ({Pl}, 13{Pl}, 4001, 'TRK-9', 1, '2026-05-06 08:00:00');
            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance, coil_alloy2, coil_temper, coil_gauge, coil_width)
              VALUES (8861, 'ORG-8861', 'LOT-8861', 5000, 1200, '5052', 'H32', 0.05, 48);
            INSERT INTO sheet_skid (sheet_skid_num, ab_job_num, sheet_skid_display_num, sheet_net_wt, sheet_tare_wt, skid_pieces, skid_sheet_status)
              VALUES (8862, 1001, 'T8862', 300, 40, 120, 2);
            -- Net and theoretical DIFFER on purpose: 300 vs 270.
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt, prod_item_theoretical_wt)
              VALUES (8863, 8861, 1001, 1, 120, 300, 270);
            INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (8862, 8863);
            -- A SECOND production item on the SAME skid, from a different coil. The combi form must
            -- print both: its grain is the production item, not the skid.
            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance, coil_alloy2, coil_temper, coil_gauge, coil_width)
              VALUES (8865, 'ORG-8865', 'LOT-8865', 4000, 900, '5052', 'H32', 0.05, 48);
            INSERT INTO production_sheet_item (prod_item_num, coil_abc_num, ab_job_num, prod_item_status, prod_item_pieces, prod_item_net_wt, prod_item_theoretical_wt)
              VALUES (8866, 8865, 1001, 1, 60, 150, 140);
            INSERT INTO sheet_skid_detail (sheet_skid_num, prod_item_num) VALUES (8862, 8866);
            INSERT INTO sheet_packing_item (sh_packing_item, packing_list, sheet_skid_num, sheet_packaging_ticket)
              VALUES (1, {Pl}, 8862, 7001);
            INSERT INTO scrap_skid (scrap_skid_num, scrap_ab_job_num, scrap_alloy2, scrap_net_wt, scrap_tare_wt)
              VALUES (8864, '1001', '5052', 150, 25);
            INSERT INTO scrap_packing_item (sc_packing_item, packing_list, scrap_skid_num, scrap_packaging_ticket)
              VALUES (1, {Pl}, 8864, 7002);
            INSERT INTO reject_coil_packing_item (rej_coil_packing_item, packing_list, rej_coil_packaging_ticket, coil_abc_num)
              VALUES (1, {Pl}, 7003, 8861);
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task An_unknown_packing_list_is_null()
        => Assert.Null(await _repo.GetCombiDocumentAsync(999999, None, CancellationToken.None));

    [Fact]
    public async Task Every_weight_carries_both_units()
    {
        var d = await _repo.GetCombiDocumentAsync(Pl, None, CancellationToken.None);
        Assert.NotNull(d);
        var row = d!.Sheets.FirstOrDefault();
        Assert.NotNull(row);
        Assert.NotNull(row!.Net);

        // 0.45359 — the combi detail reports' factor, not 0.453592 and not 0.45359237.
        Assert.Equal(Math.Round(row.Net!.Lb * 0.45359m, 2), row.Net.Kg);
    }

    [Fact]
    public async Task A_theoretical_weight_customer_gets_the_theoretical_figure_in_the_net_column()
    {
        // Same shipment, read twice — once with the customer on the theoretical-weight list and once
        // not. The net column must differ, and must equal the theoretical figure when they are on it.
        var plain = await _repo.GetCombiDocumentAsync(Pl, None, CancellationToken.None);
        var theo = await _repo.GetCombiDocumentAsync(Pl, new[] { plain!.CustomerId!.Value }, CancellationToken.None);

        Assert.False(plain.BilledOnTheoreticalWeight);
        Assert.True(theo!.BilledOnTheoreticalWeight);

        var p = plain.Sheets[0];
        var t = theo.Sheets[0];
        Assert.NotNull(t.Theoretical);
        Assert.Equal(t.Theoretical!.Lb, t.Net!.Lb);          // the substitution happened
        Assert.NotEqual(p.Net!.Lb, t.Net.Lb);                // …and it actually changed the figure
        Assert.Equal(p.Theoretical!.Lb, t.Net.Lb);           // to the theoretical weight, specifically
    }

    [Fact]
    public async Task A_customer_not_on_the_list_is_untouched_by_the_rule()
    {
        // Someone else's id on the list must not affect this shipment — the rule is per customer, not
        // "any list is set".
        var d = await _repo.GetCombiDocumentAsync(Pl, new[] { 999999L }, CancellationToken.None);
        Assert.False(d!.BilledOnTheoreticalWeight);
        // The seeded item is net 300 / theoretical 270 — so "untouched" means 300, and asserting the
        // value against ITSELF (as this first did) would have proved nothing at all.
        Assert.Equal(300m, d.Sheets[0].Net!.Lb);
        Assert.Equal(270m, d.Sheets[0].Theoretical!.Lb);
    }

    [Fact]
    public async Task The_document_states_which_weight_basis_it_used()
    {
        // A certificate that swapped its basis without saying so would be indefensible to the customer
        // reading it, so the rendered form must carry the statement.
        var theoDoc = await _repo.GetCombiDocumentAsync(Pl, new[] { 4001L }, CancellationToken.None);   // 4001 is this shipment's customer
        var html = Abis.Api.Documents.HtmlDocuments.CombiForm(theoDoc!);
        if (theoDoc!.BilledOnTheoreticalWeight)
            Assert.Contains("THEORETICAL", html, StringComparison.Ordinal);

        var plainDoc = await _repo.GetCombiDocumentAsync(Pl, None, CancellationToken.None);
        Assert.DoesNotContain("Weight basis", Abis.Api.Documents.HtmlDocuments.CombiForm(plainDoc!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sheet_grain_is_the_production_item_not_the_skid()
    {
        // A skid carrying two production items produces two rows. This is the same fan-out that made
        // the 856 ASN overstate weight (#333) — here it is correct and required, because the combi
        // form itemises what is in the shipment rather than summing per skid.
        var d = await _repo.GetCombiDocumentAsync(Pl, None, CancellationToken.None);

        // ONE skid, ONE packing item, but TWO production items — so two rows, carrying different coils.
        Assert.Equal(2, d!.Sheets.Count);
        Assert.Single(d.Sheets.Select(r => r.PackingItem).Distinct());
        Assert.Equal(new[] { "LOT-8861", "LOT-8865" }, d.Sheets.Select(r => r.LotNum).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 150m, 300m }, d.Sheets.Select(r => r.Net!.Lb).OrderBy(x => x).ToArray());
    }
}
