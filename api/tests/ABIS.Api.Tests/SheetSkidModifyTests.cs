using Abis.Api.Data;
using Abis.Api.Models;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// Correcting a sheet skid's own figures — legacy <c>w_office_skid_entry</c> CASE 4 ("modify").
/// <para>Two behaviours here are decisions rather than mechanics, so they are pinned:</para>
/// <para><b>Every field is optional.</b> Beyond convenience, <c>sheet_net_wt</c> and
/// <c>sheet_tare_wt</c> are NOT NULL on Oracle — a partial update that wrote a null into either would
/// raise ORA-01400 rather than clearing it. COALESCE means a null can never reach them, so omitting a
/// field is always safe.</para>
/// <para><b>Totals are reconciled but never corrected.</b> A weighed skid legitimately differs from
/// the sum of its items; legacy asks rather than silently reconciling, and the warehouse paths in this
/// codebase already behave that way. Quietly rewriting an operator's weighed figure to match
/// arithmetic would destroy the measurement they took.</para>
/// </summary>
public class SheetSkidModifyTests
{
    private readonly IAbisRepository _repo;

    public SheetSkidModifyTests()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"abis_skidmod_{Guid.NewGuid():N}.db");
        var options = new DatabaseOptions { Provider = "Sqlite", ConnectionString = $"Data Source={dbPath}", Seed = true };
        SqliteFixture.EnsureCreatedAndSeeded(options.ConnectionString);
        _repo = new AbisRepository(new DbConnectionFactory(options));
    }

    [Fact]
    public async Task An_unknown_skid_reports_not_found()
    {
        var r = await _repo.ModifySheetSkidAsync(999999, new SheetSkidModify { SkidPieces = 1 }, CancellationToken.None);
        Assert.False(r.Found);
        Assert.Null(r.Skid);
    }

    [Fact]
    public async Task Omitted_fields_are_left_alone()
    {
        var before = await _repo.GetSheetSkidAsync(3001, CancellationToken.None);
        Assert.NotNull(before);

        // Change only the tare. Everything else must survive untouched — this is a correction tool,
        // not a form that has to be re-filled.
        var r = await _repo.ModifySheetSkidAsync(3001, new SheetSkidModify { SheetTareWt = 77m }, CancellationToken.None);

        Assert.True(r.Found);
        Assert.Equal(77m, r.Skid!.SheetTareWt);
        Assert.Equal(before!.SheetNetWt, r.Skid.SheetNetWt);
        Assert.Equal(before.SkidPieces, r.Skid.SkidPieces);
        Assert.Equal(before.SkidSheetStatus, r.Skid.SkidSheetStatus);
        Assert.Equal(before.SheetSkidDisplayNum, r.Skid.SheetSkidDisplayNum);
    }

    [Fact]
    public async Task A_restated_total_that_disagrees_with_the_items_warns_but_still_saves()
    {
        // Skid 3001 carries two production items. Restate its pieces to something they cannot add up
        // to and the change must still be applied — the operator counted, and the count stands.
        var r = await _repo.ModifySheetSkidAsync(3001, new SheetSkidModify { SkidPieces = 4242 }, CancellationToken.None);

        Assert.True(r.Found);
        Assert.Equal(4242, r.Skid!.SkidPieces);                       // saved, not rejected
        Assert.Contains(r.Warnings, w => w.Contains("Skid pieces (4242)"));
        Assert.Contains(r.Warnings, w => w.Contains("items' pieces"));

        // …and re-reading confirms it was persisted rather than only reported.
        var reread = await _repo.GetSheetSkidAsync(3001, CancellationToken.None);
        Assert.Equal(4242, reread!.SkidPieces);
    }

    [Fact]
    public async Task A_skid_with_no_items_raises_no_reconciliation_warning()
    {
        // Nothing to disagree with. Warning on a skid that simply has no items yet would train
        // operators to dismiss the warning that matters.
        var r = await _repo.ModifySheetSkidAsync(3002, new SheetSkidModify { SkidPieces = 999 }, CancellationToken.None);
        Assert.True(r.Found);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public async Task The_fields_legacy_modify_writes_are_all_settable()
    {
        // The legacy UPDATE sets exactly these seven columns; two of them (theoretical weight and
        // on-hold reason) were missing from the model entirely until this change.
        var r = await _repo.ModifySheetSkidAsync(3003, new SheetSkidModify
        {
            SheetNetWt = 1234m,
            SheetTareWt = 56m,
            SkidPieces = 78,
            SkidDate = new DateTime(2026, 3, 4),
            SkidSheetStatus = 2,
            SheetTheoreticalWt = 1200m,
            OnholdReasonCode = 5,
        }, CancellationToken.None);

        Assert.True(r.Found);
        var s = r.Skid!;
        Assert.Equal(1234m, s.SheetNetWt);
        Assert.Equal(56m, s.SheetTareWt);
        Assert.Equal(78, s.SkidPieces);
        Assert.Equal(new DateTime(2026, 3, 4), s.SkidDate);
        Assert.Equal(2, s.SkidSheetStatus);
        Assert.Equal(1200m, s.SheetTheoreticalWt);
        Assert.Equal(5, s.OnholdReasonCode);
    }
}
