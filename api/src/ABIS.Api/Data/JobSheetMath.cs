namespace Abis.Api.Data;

/// <summary>
/// The two figures a <b>BY-LOT</b> job sheet works out per coil, instead of stating one number for
/// the whole job.
///
/// <para>Recovered from the computed columns of <c>downtime2/d_report_ab_job_coil_by_lot.srd</c>. They
/// live here rather than inline in the repository because they are the only real arithmetic on the
/// sheet: everything else is a column or a sum, while these decide how many skids an operator builds
/// and how many pieces go on each — and the rounding rules are not the ones you would guess.</para>
///
/// <para>A job is by-lot when <c>customer_order.sheet_handling_type = 1</c>. Each incoming coil is one
/// lot, and lots differ in weight, so a single pieces-per-skid figure would be wrong for most of them.
/// Legacy's ordinary sheet prints the number; its by-lot sheet prints the literal text
/// <b>"See Below"</b> and puts these two columns against every coil.</para>
/// </summary>
public static class JobSheetMath
{
    /// <summary>
    /// Skids this coil is expected to make:
    /// <c>ceiling(processQuantity * materialYield / maxSkidWt)</c>.
    ///
    /// <para><b>Ceiling, not round.</b> The remainder of a coil still goes on a skid — that is what a
    /// partial skid IS, and the sheet has a line for them. Rounding to nearest would under-count the
    /// skids the operator has to have banding and tags for.</para>
    ///
    /// <para>Returns 0 when there is no maximum skid weight to divide by, rather than dividing by
    /// zero. Legacy guards this explicitly: <c>if(max_skid_wt > 0, ceiling(...), 0)</c>.</para>
    /// </summary>
    public static decimal Skids(decimal processQuantity, decimal materialYield, decimal maxSkidWt) =>
        maxSkidWt > 0 ? Math.Ceiling(processQuantity * materialYield / maxSkidWt) : 0m;

    /// <summary>
    /// Pieces on each of this coil's skids: the pieces the coil yields, divided across
    /// <see cref="Skids"/>, <b>truncated</b> — and then rounded <b>DOWN</b> to a whole number of
    /// stacks when the item is stacked.
    ///
    /// <para><b>Both roundings go down, and both matter.</b> Truncating the division means a skid is
    /// never planned to hold a piece the coil cannot supply. The stack rounding then drops the
    /// remainder outright (legacy subtracts <c>mod(pieces, stacksSkid)</c>) rather than rounding to
    /// the nearest multiple: a part-stack is not something the plant bands and ships, so 22 pieces at
    /// 5 to a stack is a skid of 20, not 25 and not 22 — and the 2 left over go on the partial.</para>
    ///
    /// <para>Returns 0 unless there is a skid weight, a piece weight and some coil to run, mirroring
    /// legacy's three-part guard. Zero is a real answer here: a lot too small to fill one skid at the
    /// planned weight yields no full skid.</para>
    /// </summary>
    /// <param name="stacksSkid">Stacks per skid (<c>order_item.stacks_skid</c>); null or 0 means the
    /// item is not stacked and the piece count stands as divided.</param>
    public static int PiecesPerSkid(decimal processQuantity, decimal materialYield, decimal maxSkidWt,
        decimal theoreticalUnitWt, int? stacksSkid)
    {
        if (maxSkidWt <= 0 || theoreticalUnitWt <= 0 || processQuantity <= 0) return 0;

        var skids = Skids(processQuantity, materialYield, maxSkidWt);
        if (skids <= 0) return 0;

        var pieces = (int)decimal.Truncate(processQuantity * materialYield / theoreticalUnitWt / skids);
        if (stacksSkid is > 0) pieces -= pieces % stacksSkid.Value;
        return pieces;
    }
}
