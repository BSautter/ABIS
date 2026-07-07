namespace Abis.Api.Data;

/// <summary>
/// The legacy invoice billing rules recovered verbatim from <c>accounting/w_invoice.srw</c>.
/// Kept as a pure, dependency-free function so the exact rule is unit-testable and shared by
/// both the computed-invoice read path and the printable invoice document.
/// </summary>
public static class InvoiceBilling
{
    /// <summary>
    /// The billed weight of a single rejected (status 3) or rebanded (status 7) coil, reproducing
    /// <c>w_invoice.wf_rejected_coil_wt</c> (lines 509–536) exactly:
    /// <code>
    ///   ll_wt1 = process_end_wt          -- the shift-end weight for this coil on this job
    ///   IF ll_wt1 IS NULL THEN
    ///       ll_wt1 = coil.net_wt_balance -- fall back to the coil's remaining balance
    ///   ll_wt2 = MAX(process_quantity) over this coil WHERE process_quantity &lt; this job's qty
    ///   RETURN MAX(ll_wt1, ll_wt2)
    /// </code>
    /// The naive greenfield path summed <c>process_end_wt</c> (or <c>net_wt</c>) alone, which
    /// undercounts whenever a prior process pass moved more material than the shift-end/balance
    /// figure — see PARITY_AUDIT §1 "Rejected-coil billed-weight derivation".
    /// </summary>
    /// <param name="processEndWt">This job's <c>process_coil.process_end_wt</c> (the shift-end
    /// weight); <c>null</c> when the shift never recorded one.</param>
    /// <param name="netWtBalance">The coil's <c>coil.net_wt_balance</c>; used only when
    /// <paramref name="processEndWt"/> is null (the legacy fallback).</param>
    /// <param name="maxPriorProcessQuantity"><c>MAX(process_quantity)</c> across all of this
    /// coil's process rows whose quantity is strictly less than this job's process quantity;
    /// <c>null</c> when there is no such prior pass.</param>
    public static decimal RejectedCoilBilledWeight(
        decimal? processEndWt, decimal? netWtBalance, decimal? maxPriorProcessQuantity)
    {
        // ll_wt1: the shift-end weight if present (even zero — the legacy tests IsNull, not > 0),
        // otherwise the coil's remaining balance, otherwise 0.
        var wt1 = processEndWt ?? netWtBalance ?? 0m;
        // ll_wt2: the largest prior-process quantity below this job's quantity (0 when none).
        var wt2 = maxPriorProcessQuantity ?? 0m;
        return Math.Max(wt1, wt2);
    }
}
