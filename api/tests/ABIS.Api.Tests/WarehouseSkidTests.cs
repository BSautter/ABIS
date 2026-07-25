using Abis.Api.Data;
using Abis.Api.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Warehoused skids and their status-20 "warehouse coil" (legacy warehouse module
/// <c>w_wh_business</c>). The warehouse coil is an empty shell that exists to hang skids off — the
/// tests below pin that it stays weightless, that identity is inherited where it can be, and that a
/// customer who needs a certificate is not given one that nothing backs.
/// </summary>
public sealed class WarehouseSkidTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AbisRepository _repo;
    private readonly string _cs;

    public WarehouseSkidTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abis_wh_{Guid.NewGuid():N}.db");
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

    private T Scalar<T>(string sql)
    {
        using var c = new SqliteConnection(_cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? default! : (T)Convert.ChangeType(v, typeof(T));
    }

    /// <summary>A job on an order for a customer, plus (optionally) the customer's REAL coil that the
    /// warehouse coil can inherit cash date / customer from.</summary>
    private void Seed(string certLabelReq = "N", string cashDateReq = "N", bool withRealCoil = true)
    {
        Exec($"""
            INSERT INTO customer (customer_id, customer_full_name, coil_cert_label_req, cash_date_required)
                 VALUES (8100, 'Warehouse Customer', '{certLabelReq}', '{cashDateReq}');
            INSERT INTO customer_order (order_abc_num, orig_customer_id) VALUES (4700, 8100);
            INSERT INTO order_item (order_abc_num, order_item_num, enduser_part_num) VALUES (4700, 2, 'PART-WH');
            INSERT INTO ab_job (ab_job_num, order_abc_num, order_item_num) VALUES (3600, 4700, 2);
            """);
        if (withRealCoil)
            Exec("""
                INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance, coil_status, customer_id, cash_date)
                     VALUES (6900, 'CUST-WH-1', 'LOT-WH', 20000, 15000, 2, 8100, '2026-03-01');
                """);
    }

    private static WarehouseSkidWrite Body() => new()
    {
        AbJobNum = 3600,
        CoilOrgNum = "CUST-WH-1",
        LotNum = "LOT-WH",
        SheetNetWt = 1000,
        SheetTareWt = 50,
        SkidPieces = 120,
        ProdItemPieces = 120,
        ProdItemNetWt = 1000,
        SkidTicketIfWhed = "WH-TICKET-1",
        SkidTypeIfWhed = 1,
    };

    [Fact]
    public async Task Mints_the_warehouse_coil_as_a_weightless_shell()
    {
        // The whole point of status 20: it hangs skids off, it does not represent metal we hold.
        // If it ever carried weight, every on-hand total would inflate by the warehoused volume.
        Seed();
        var r = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        Assert.True(r.CoilMinted);
        Assert.True(r.CoilAbcNum > 0);
        Assert.Equal(20, Scalar<long>($"SELECT coil_status FROM coil WHERE coil_abc_num = {r.CoilAbcNum}"));
        Assert.Equal(0d, Scalar<double>($"SELECT net_wt FROM coil WHERE coil_abc_num = {r.CoilAbcNum}"));
        Assert.Equal(0d, Scalar<double>($"SELECT net_wt_balance FROM coil WHERE coil_abc_num = {r.CoilAbcNum}"));
        Assert.Equal(0d, Scalar<double>($"SELECT process_quantity FROM process_coil WHERE coil_abc_num = {r.CoilAbcNum}"));
    }

    [Fact]
    public async Task Inherits_cash_date_and_customer_from_the_customers_real_coil()
    {
        Seed();
        var r = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        Assert.Equal(8100, Scalar<long>($"SELECT customer_id FROM coil WHERE coil_abc_num = {r.CoilAbcNum}"));
        Assert.Contains("2026-03-01", Scalar<string>($"SELECT cash_date FROM coil WHERE coil_abc_num = {r.CoilAbcNum}") ?? "");
    }

    [Fact]
    public async Task Reuses_an_existing_warehouse_coil_for_the_same_number_and_lot()
    {
        // (coil number, lot) identifies the warehouse coil — a second skid for the same material must
        // hang off the same shell rather than minting another.
        Seed();
        var first = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        var second = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        Assert.True(first.CoilMinted);
        Assert.False(second.CoilMinted);
        Assert.Equal(first.CoilAbcNum, second.CoilAbcNum);
        Assert.NotEqual(first.SheetSkidNum, second.SheetSkidNum);
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM coil WHERE coil_org_num = 'CUST-WH-1' AND coil_status = 20"));
    }

    [Fact]
    public async Task A_different_lot_gets_its_own_warehouse_coil()
    {
        Seed();
        var a = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        var b = Body(); b.LotNum = "LOT-OTHER";
        var second = await _repo.CreateWarehouseSkidAsync(b, CancellationToken.None);

        Assert.True(second.CoilMinted);
        Assert.NotEqual(a.CoilAbcNum, second.CoilAbcNum);
    }

    [Fact]
    public async Task Writes_the_skid_its_item_and_the_link()
    {
        Seed();
        var r = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = {r.SheetSkidNum}"));
        Assert.Equal(r.CoilAbcNum, Scalar<long>($"SELECT coil_abc_num FROM production_sheet_item WHERE prod_item_num = {r.ProdItemNum}"));
        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM sheet_skid_detail WHERE sheet_skid_num = {r.SheetSkidNum} AND prod_item_num = {r.ProdItemNum}"));
        // Warehouse provenance and the reference order both land on the skid.
        Assert.Equal("WH-TICKET-1", Scalar<string>($"SELECT skid_ticket_if_whed FROM sheet_skid WHERE sheet_skid_num = {r.SheetSkidNum}"));
        Assert.Equal(4700, Scalar<long>($"SELECT ref_order_abc_num FROM sheet_skid WHERE sheet_skid_num = {r.SheetSkidNum}"));
        Assert.Equal(2, Scalar<long>($"SELECT ref_order_abc_item FROM sheet_skid WHERE sheet_skid_num = {r.SheetSkidNum}"));
    }

    [Fact]
    public async Task Refuses_a_shell_the_customers_certificate_would_have_nothing_behind_it()
    {
        // No regular coil to inherit from AND the customer requires a cert label: legacy stops here,
        // because a certificate issued against an invented coil is worse than no skid.
        Seed(certLabelReq: "Y", withRealCoil: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None));

        Assert.Contains("certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Scoped to the coil under test — the base fixture already seeds an unrelated status-20 coil
        // (5007 / ORG-5007) to exercise the on-hand exclusion.
        Assert.Equal(0, Scalar<long>("SELECT COUNT(*) FROM coil WHERE coil_org_num = 'CUST-WH-1' AND coil_status = 20"));
        Assert.Equal(0, Scalar<long>("SELECT COUNT(*) FROM sheet_skid WHERE ab_job_num = 3600"));
    }

    [Fact]
    public async Task Refuses_when_the_customer_requires_a_cash_date_and_none_can_be_inherited()
    {
        Seed(cashDateReq: "Y", withRealCoil: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None));
    }

    [Fact]
    public async Task Mints_without_a_real_coil_when_the_customer_needs_neither()
    {
        // The same missing-coil situation is fine for a customer with no cert/cash-date requirement —
        // the shell is simply created with those fields null.
        Seed(withRealCoil: false);
        var r = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        Assert.True(r.CoilMinted);
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {r.CoilAbcNum} AND customer_id IS NOT NULL"));
    }

    [Fact]
    public async Task A_weight_mismatch_warns_but_still_saves()
    {
        // Legacy asks "save it anyway?" and defaults to No — a mismatch is a warning an operator may
        // knowingly accept, not a refusal. Blocking it would stop real corrections the floor can make.
        Seed();
        var b = Body();
        b.SkidPieces = 120;
        b.ProdItemPieces = 100;     // deliberately short
        b.ProdItemNetWt = 900;      // and lighter than the skid
        var r = await _repo.CreateWarehouseSkidAsync(b, CancellationToken.None);

        Assert.Equal(2, r.Warnings.Count);
        Assert.Contains(r.Warnings, w => w.Contains("pieces"));
        Assert.Contains(r.Warnings, w => w.Contains("net weight"));
        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = {r.SheetSkidNum}"));   // saved anyway
    }

    [Fact]
    public async Task No_warning_when_it_adds_up()
    {
        Seed();
        Assert.Empty((await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None)).Warnings);
    }

    [Fact]
    public async Task Refuses_a_job_with_no_order_behind_it()
    {
        Seed();
        Exec("INSERT INTO ab_job (ab_job_num) VALUES (3610);");   // no order
        var b = Body(); b.AbJobNum = 3610;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.CreateWarehouseSkidAsync(b, CancellationToken.None));
        Assert.Contains("no order", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Requires_both_the_coil_number_and_the_lot()
    {
        Seed();
        var b = Body(); b.LotNum = "  ";
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.CreateWarehouseSkidAsync(b, CancellationToken.None));
    }

    [Fact]
    public async Task The_shell_stays_out_of_on_hand_coil_totals()
    {
        // OnHandCoilPredicate already excludes status 20; this pins that the newly minted shell is
        // covered by it, so warehousing material can't inflate what the floor appears to hold.
        Seed();
        var r = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        var onHand = Scalar<long>(
            $"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {r.CoilAbcNum} " +
            "AND (coil_status IS NULL OR coil_status NOT IN (0, 10, 13, 20))");
        Assert.Equal(0, onHand);
    }

    // ---- delete (legacy action 5) ------------------------------------------------------------

    [Fact]
    public async Task Deleting_the_last_skid_collects_the_warehouse_shell()
    {
        // The shell exists only while something hangs off it — otherwise warehousing leaves orphan
        // status-20 coils behind forever.
        Seed();
        var made = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        var del = await _repo.DeleteWarehouseSkidAsync(made.SheetSkidNum, CancellationToken.None);

        Assert.True(del.Deleted);
        Assert.True(del.CoilRemoved);
        Assert.Equal(1, del.ItemsRemoved);
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = {made.SheetSkidNum}"));
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM production_sheet_item WHERE prod_item_num = {made.ProdItemNum}"));
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM sheet_skid_detail WHERE sheet_skid_num = {made.SheetSkidNum}"));
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {made.CoilAbcNum}"));
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM process_coil WHERE coil_abc_num = {made.CoilAbcNum}"));
    }

    [Fact]
    public async Task A_shell_still_carrying_another_skid_is_kept()
    {
        Seed();
        var first = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        var second = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);   // same coil + lot

        var del = await _repo.DeleteWarehouseSkidAsync(first.SheetSkidNum, CancellationToken.None);

        Assert.True(del.Deleted);
        Assert.False(del.CoilRemoved);
        Assert.Contains("still reference", del.CoilKeptReason);
        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {first.CoilAbcNum}"));
        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM sheet_skid WHERE sheet_skid_num = {second.SheetSkidNum}"));
    }

    [Fact]
    public async Task A_real_coil_is_never_destroyed_by_a_warehouse_delete()
    {
        // Guard added over legacy. In this module the coil is always the shell, so legacy never
        // checked — but a delete path that can remove a row from `coil` is not somewhere to trust
        // "can't happen". Point the item at a REAL coil and the delete must leave it standing.
        Seed();
        var made = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        Exec($"UPDATE production_sheet_item SET coil_abc_num = 6900 WHERE prod_item_num = {made.ProdItemNum};");

        var del = await _repo.DeleteWarehouseSkidAsync(made.SheetSkidNum, CancellationToken.None);

        Assert.True(del.Deleted);
        Assert.False(del.CoilRemoved);
        Assert.Contains("not a status-20", del.CoilKeptReason);
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM coil WHERE coil_abc_num = 6900"));
    }

    [Fact]
    public async Task Deleting_an_unknown_skid_reports_not_found_and_changes_nothing()
    {
        Seed();
        var before = Scalar<long>("SELECT COUNT(*) FROM sheet_skid");
        var del = await _repo.DeleteWarehouseSkidAsync(999999, CancellationToken.None);

        Assert.False(del.Deleted);
        Assert.Equal(before, Scalar<long>("SELECT COUNT(*) FROM sheet_skid"));
    }

    // ---- modify (legacy action 4) ---------------------------------------------------------------

    [Fact]
    public async Task Modify_updates_the_skid_in_place()
    {
        Seed();
        var made = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        var b = Body();
        b.SheetNetWt = 1500; b.SheetTareWt = 60; b.SkidPieces = 180; b.SkidTicketIfWhed = "WH-TICKET-2";
        var r = await _repo.ModifyWarehouseSkidAsync(made.SheetSkidNum, b, CancellationToken.None);

        Assert.True(r.Found);
        Assert.False(r.CoilChanged);
        Assert.Equal(made.CoilAbcNum, r.CoilAbcNum);
        Assert.Equal(1500d, Scalar<double>($"SELECT sheet_net_wt FROM sheet_skid WHERE sheet_skid_num = {made.SheetSkidNum}"));
        Assert.Equal("WH-TICKET-2", Scalar<string>($"SELECT skid_ticket_if_whed FROM sheet_skid WHERE sheet_skid_num = {made.SheetSkidNum}"));
    }

    [Fact]
    public async Task Changing_the_lot_repoints_the_skid_and_collects_the_ORIGINAL_shell()
    {
        // The legacy bug this does NOT reproduce: its modify branch tests whether the ORIGINAL coil is
        // orphaned but deletes the NEWLY MINTED one, which would leave the skid pointing at a coil that
        // no longer exists and strand the original. The original is what must go.
        Seed();
        var made = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        var original = made.CoilAbcNum;

        var b = Body(); b.LotNum = "LOT-MOVED";
        var r = await _repo.ModifyWarehouseSkidAsync(made.SheetSkidNum, b, CancellationToken.None);

        Assert.True(r.CoilChanged);
        Assert.True(r.CoilMinted);
        Assert.NotEqual(original, r.CoilAbcNum);
        Assert.Equal(original, r.PreviousCoilRemoved);

        // The NEW shell survives and the item points at it — the whole point.
        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {r.CoilAbcNum}"));
        Assert.Equal(r.CoilAbcNum, Scalar<long>($"SELECT coil_abc_num FROM production_sheet_item WHERE prod_item_num = {made.ProdItemNum}"));
        // The ORIGINAL, now empty, is gone.
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {original}"));
        Assert.Equal(0, Scalar<long>($"SELECT COUNT(*) FROM process_coil WHERE coil_abc_num = {original}"));
    }

    [Fact]
    public async Task Moving_one_of_two_skids_keeps_the_shared_original_shell()
    {
        Seed();
        var first = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        var second = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);   // same shell
        var shared = first.CoilAbcNum;

        var b = Body(); b.LotNum = "LOT-MOVED";
        var r = await _repo.ModifyWarehouseSkidAsync(first.SheetSkidNum, b, CancellationToken.None);

        Assert.True(r.CoilChanged);
        Assert.Null(r.PreviousCoilRemoved);
        Assert.Equal(1, Scalar<long>($"SELECT COUNT(*) FROM coil WHERE coil_abc_num = {shared}"));
        Assert.Equal(shared, Scalar<long>($"SELECT coil_abc_num FROM production_sheet_item WHERE prod_item_num = {second.ProdItemNum}"));
    }

    [Fact]
    public async Task Moving_onto_an_existing_shell_reuses_it_rather_than_minting()
    {
        Seed();
        var a = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        var otherBody = Body(); otherBody.LotNum = "LOT-B";
        var b2 = await _repo.CreateWarehouseSkidAsync(otherBody, CancellationToken.None);

        // Move skid A onto B's (coil, lot).
        var mv = Body(); mv.LotNum = "LOT-B";
        var r = await _repo.ModifyWarehouseSkidAsync(a.SheetSkidNum, mv, CancellationToken.None);

        Assert.True(r.CoilChanged);
        Assert.False(r.CoilMinted);
        Assert.Equal(b2.CoilAbcNum, r.CoilAbcNum);
        Assert.Equal(a.CoilAbcNum, r.PreviousCoilRemoved);   // A's old shell is now empty
    }

    [Fact]
    public async Task Modify_never_collects_a_real_coil()
    {
        Seed();
        var made = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);
        Exec($"UPDATE production_sheet_item SET coil_abc_num = 6900 WHERE prod_item_num = {made.ProdItemNum};");

        var b = Body(); b.LotNum = "LOT-MOVED";
        var r = await _repo.ModifyWarehouseSkidAsync(made.SheetSkidNum, b, CancellationToken.None);

        Assert.True(r.CoilChanged);
        Assert.Null(r.PreviousCoilRemoved);
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM coil WHERE coil_abc_num = 6900"));
    }

    [Fact]
    public async Task Modifying_an_unknown_skid_reports_not_found()
    {
        Seed();
        Assert.False((await _repo.ModifyWarehouseSkidAsync(999999, Body(), CancellationToken.None)).Found);
    }

    // ---- cash-date identity (bug sweep, 2026-07-25) ----------------------------------------------

    [Fact]
    public async Task Inherits_from_the_newest_CASHED_coil_not_merely_the_newest()
    {
        // Legacy: max(coil_abc_num) WHERE coil_org_num = ? AND cash_date IS NOT NULL AND status <> 20.
        // Dropping the cash_date predicate resolves the NEWER uncashed coil and mints a shell with a
        // null cash date, where legacy would have found the older dated one.
        Seed();   // seeds coil 6900: CUST-WH-1, cash_date 2026-03-01, status 2
        Exec("""
            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance, coil_status, customer_id, cash_date)
                 VALUES (6950, 'CUST-WH-1', 'LOT-LATER', 9000, 9000, 1, 8100, NULL);
            """);

        var r = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        var cash = Scalar<string>($"SELECT cash_date FROM coil WHERE coil_abc_num = {r.CoilAbcNum}");
        Assert.False(string.IsNullOrEmpty(cash));            // NOT the newer, uncashed coil
        Assert.Contains("2026-03-01", cash);                 // the older CASHED one
    }

    [Fact]
    public async Task A_customer_coil_with_no_cash_date_at_all_still_triggers_the_refusal()
    {
        // The old guard asked "customer id AND cash date are both null". An uncashed coil supplies a
        // customer id, so that proxy never fired and a cert-required customer slipped through with a
        // certificate that had nothing dated behind it. The test is the guard now keying on "no row".
        Seed(certLabelReq: "Y", withRealCoil: false);
        Exec("""
            INSERT INTO coil (coil_abc_num, coil_org_num, lot_num, net_wt, net_wt_balance, coil_status, customer_id, cash_date)
                 VALUES (6960, 'CUST-WH-1', 'LOT-UNCASHED', 9000, 9000, 1, 8100, NULL);
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None));

        Assert.Contains("cash date", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Scalar<long>("SELECT COUNT(*) FROM coil WHERE coil_org_num = 'CUST-WH-1' AND coil_status = 20"));
    }

    [Fact]
    public async Task Modify_applies_the_same_refusal_when_it_would_mint_a_shell()
    {
        // Legacy runs the guard for actions 1, 2 AND 4. Modify was minting with no check at all, so a
        // cert-required customer could get an unbacked certificate just by EDITING a skid onto a new lot.
        Seed(certLabelReq: "Y");                         // has a cashed coil, so the create succeeds
        var made = await _repo.CreateWarehouseSkidAsync(Body(), CancellationToken.None);

        // Now move it onto a coil number that has NO cashed coil behind it.
        var b = Body(); b.CoilOrgNum = "CUST-NO-CASH"; b.LotNum = "LOT-X";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.ModifyWarehouseSkidAsync(made.SheetSkidNum, b, CancellationToken.None));

        Assert.Contains("certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Scalar<long>("SELECT COUNT(*) FROM coil WHERE coil_org_num = 'CUST-NO-CASH'"));
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
