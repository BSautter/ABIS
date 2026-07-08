using Abis.Api.Data;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Pure-function tests for the legacy rejected/rebanded-coil billed-weight rule
/// (<c>w_invoice.wf_rejected_coil_wt</c>, lines 509–536): <c>MAX(shift-end-or-balance,
/// prior-process-qty)</c>. These pin every branch of the rule exactly, independent of any
/// database — the authoritative proof that the greenfield figure matches the legacy one.</summary>
public sealed class InvoiceBillingTests
{
    [Fact]
    public void Uses_shift_end_weight_when_present()
    {
        // shift-end (process_end_wt) present → wt1 = 1500; the coil balance is ignored; no prior pass.
        Assert.Equal(1500m, InvoiceBilling.RejectedCoilBilledWeight(
            processEndWt: 1500m, netWtBalance: 9000m, maxPriorProcessQuantity: null));
    }

    [Fact]
    public void Falls_back_to_coil_balance_when_shift_end_is_null()
    {
        // No shift-end weight → wt1 falls back to the coil's remaining balance.
        Assert.Equal(9000m, InvoiceBilling.RejectedCoilBilledWeight(
            processEndWt: null, netWtBalance: 9000m, maxPriorProcessQuantity: null));
    }

    [Fact]
    public void Zero_when_everything_is_null()
    {
        Assert.Equal(0m, InvoiceBilling.RejectedCoilBilledWeight(null, null, null));
    }

    [Fact]
    public void A_present_zero_shift_end_is_used_not_treated_as_missing()
    {
        // The legacy tests IsNull, not > 0: a present 0 shift-end is used as-is (wt1 = 0), so the
        // coil balance must NOT substitute. Guards a subtle off-by-a-fallback bug.
        Assert.Equal(0m, InvoiceBilling.RejectedCoilBilledWeight(
            processEndWt: 0m, netWtBalance: 9000m, maxPriorProcessQuantity: null));
    }

    [Fact]
    public void Prior_process_quantity_wins_when_larger_than_shift_end()
    {
        // The divergence from the naive sum: a naive path bills process_end_wt (200); the legacy
        // rule bills MAX(200, 4000) = 4000 because a prior pass moved more material.
        Assert.Equal(4000m, InvoiceBilling.RejectedCoilBilledWeight(
            processEndWt: 200m, netWtBalance: 50m, maxPriorProcessQuantity: 4000m));
    }

    [Fact]
    public void Prior_process_quantity_wins_over_the_balance_branch_too()
    {
        Assert.Equal(4000m, InvoiceBilling.RejectedCoilBilledWeight(
            processEndWt: null, netWtBalance: 500m, maxPriorProcessQuantity: 4000m));
    }

    [Fact]
    public void Shift_end_wins_when_it_exceeds_the_prior_process_quantity()
    {
        Assert.Equal(1500m, InvoiceBilling.RejectedCoilBilledWeight(
            processEndWt: 1500m, netWtBalance: null, maxPriorProcessQuantity: 40m));
    }
}
