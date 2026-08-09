using Abis.Api.Data;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The BY-LOT job sheet's two computed columns.
///
/// <para><b>Why these get their own tests.</b> Everything else on a job sheet is a column or a sum;
/// these two decide how many skids an operator sets up and how many pieces go on each, and every
/// rounding in them goes DOWN for a different reason. A version that rounded either one to nearest
/// would produce numbers that look right on paper and are wrong on the floor — and the fixture can
/// only ever exercise one set of values, so the interesting cases live here.</para>
///
/// <para>Recovered from the computed columns of <c>downtime2/d_report_ab_job_coil_by_lot.srd</c>.</para>
/// </summary>
public sealed class JobSheetMathTests
{
    // ---- Skids: ceiling, because a remainder is still a skid --------------------------------

    [Theory]
    [InlineData(10000, 0.9, 4000, 3)]     // 9000 lb over 4000 = 2.25 -> 3, not 2
    [InlineData(8000, 1.0, 4000, 2)]      // exactly two skids stays two
    [InlineData(100, 0.9, 4000, 1)]       // a lot too small for one full skid still needs a skid
    public void A_part_skid_still_counts_as_a_skid(double qty, double yield, double maxSkid, int expected) =>
        Assert.Equal(expected, JobSheetMath.Skids((decimal)qty, (decimal)yield, (decimal)maxSkid));

    [Fact]
    public void No_maximum_skid_weight_gives_zero_rather_than_a_division_by_zero()
    {
        // Legacy guards this explicitly — if(max_skid_wt > 0, ceiling(...), 0) — because plenty of
        // order items carry no skid weight and the sheet still has to print.
        Assert.Equal(0m, JobSheetMath.Skids(10000m, 0.9m, 0m));
        Assert.Equal(0m, JobSheetMath.Skids(10000m, 0.9m, -1m));
    }

    // ---- Pieces per skid: truncate, then round down to whole stacks -------------------------

    [Fact]
    public void The_division_truncates_so_a_skid_is_never_planned_a_piece_the_coil_cannot_supply()
    {
        // 10,000 lb at 90% = 9,000 lb of sheet -> 3 skids (2.25 rounded up).
        // 9,000 / 12.5 lb a piece = 720 pieces, over 3 skids = 240 each.
        Assert.Equal(240, JobSheetMath.PiecesPerSkid(10000m, 0.9m, 4000m, 12.5m, stacksSkid: null));

        // A case that does not divide evenly: 9,000 / 13 = 692.3 pieces over 3 skids = 230.7 -> 230.
        Assert.Equal(230, JobSheetMath.PiecesPerSkid(10000m, 0.9m, 4000m, 13m, stacksSkid: null));
    }

    [Fact]
    public void A_part_stack_is_dropped_not_rounded_up()
    {
        // 240 pieces at 50 to a stack is 4 stacks and 40 spare. The spare do not become a fifth stack
        // and do not stay on the skid — a part-stack is not something the plant bands and ships, so
        // they go on the partial instead. Rounding to NEAREST would give 250, a skid that cannot be
        // built from this lot at all.
        Assert.Equal(200, JobSheetMath.PiecesPerSkid(10000m, 0.9m, 4000m, 12.5m, stacksSkid: 50));
    }

    [Fact]
    public void An_exact_multiple_of_the_stack_size_is_left_alone()
    {
        // 240 at 40 to a stack is exactly 6 stacks — nothing to drop.
        Assert.Equal(240, JobSheetMath.PiecesPerSkid(10000m, 0.9m, 4000m, 12.5m, stacksSkid: 40));
    }

    [Fact]
    public void A_stack_larger_than_the_skid_holds_empties_it()
    {
        // 240 pieces, 500 to a stack: not one full stack, so not one shippable skid. Zero is the
        // honest answer and it is the answer legacy gives; a planner reading it can see the item is
        // mis-specified, which "240" would hide.
        Assert.Equal(0, JobSheetMath.PiecesPerSkid(10000m, 0.9m, 4000m, 12.5m, stacksSkid: 500));
    }

    [Theory]
    [InlineData(0, 0.9, 4000, 12.5)]      // nothing committed to the job yet
    [InlineData(10000, 0.9, 0, 12.5)]     // no maximum skid weight
    [InlineData(10000, 0.9, 4000, 0)]     // no piece weight
    public void Every_missing_input_yields_zero_rather_than_a_crash_or_an_infinity(
        double qty, double yield, double maxSkid, double unitWt) =>
        Assert.Equal(0, JobSheetMath.PiecesPerSkid((decimal)qty, (decimal)yield, (decimal)maxSkid, (decimal)unitWt, null));

    [Fact]
    public void A_zero_material_yield_yields_no_pieces_without_dividing_by_a_zero_skid_count()
    {
        // Yield 0 makes the skid count 0 too, so the pieces-per-skid division would be x/0. Legacy's
        // guards do not cover this one — its skid-count compute and its pieces compute each test
        // max_skid_wt independently — so it is guarded here rather than left to throw on a job whose
        // yield has not been entered.
        Assert.Equal(0m, JobSheetMath.Skids(10000m, 0m, 4000m));
        Assert.Equal(0, JobSheetMath.PiecesPerSkid(10000m, 0m, 4000m, 12.5m, null));
    }
}
