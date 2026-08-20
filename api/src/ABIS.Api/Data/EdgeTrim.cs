namespace Abis.Api.Data;

/// <summary>
/// The edge-trim rules an order line must satisfy before it can be saved — legacy
/// <c>order_entry/w_order_entry.srw:505-545</c>.
///
/// <para>Two different kinds of failure, and conflating them loses the distinction that matters:</para>
/// <list type="bullet">
/// <item><b>Hard errors</b> — a missing width or trim type, or a trimmed width WIDER than the
/// incoming coil. Legacy shows "Please correct:" and refuses the save outright. There is no override,
/// because none of these describe a real coil.</item>
/// <item><b>Out of the trimmer's tolerance band</b> — the widths are coherent but the amount being
/// trimmed off is outside what the equipment does. Legacy offers Yes/No: "click 'Yes' to override, or
/// 'No' to go back". An override stamps WHO did it and writes a system_log row.</item>
/// </list>
///
/// <para>The band is checked only once the hard errors are clear — an item missing its trimmed width
/// has no difference to test.</para>
/// </summary>
public static class EdgeTrim
{
    /// <summary>The tolerance band, in inches: the difference between the incoming coil width and the
    /// trimmed width must fall inside it.</summary>
    /// <param name="LowerInches">Below this, too little is being trimmed off.</param>
    /// <param name="UpperInches">Above this, too much.</param>
    public readonly record struct Tolerance(decimal LowerInches, decimal UpperInches);

    /// <summary>
    /// Legacy's fallback when <c>edge_trim_tolearance</c> has no row or the read fails.
    ///
    /// <para><b>These are stale history, not the plant's current numbers.</b> The live table on
    /// <c>.230</c> reads <b>0.75 / 12.00</b>. The source's comment trail explains why they differ:
    /// <c>&lt; 1</c> → <c>&lt; 0.75</c> ("as per Laura Anderson", 2016-12) → <c>1.50–12.00</c>
    /// ("as per Dan Polkinhorne", 2017-06) — and the table was later set back to 0.75. So the TABLE is
    /// authoritative and these constants are only what legacy falls back to when it cannot read it.
    /// Hardcoding 1.50 would demand an override on every trim between 0.75" and 1.5" the plant
    /// accepts today.</para>
    /// </summary>
    public static readonly Tolerance LegacyFallback = new(1.500m, 12.000m);

    /// <summary>Why the line cannot be saved at all, or null when the widths are coherent. Each
    /// message names the item, as legacy's combined "Please correct:" list does.</summary>
    public static string? HardError(decimal? incomingWidth, decimal? trimmedWidth, int? trimTypeCode)
    {
        var missing = new List<string>();
        if (incomingWidth is null) missing.Add("incoming coil width");
        if (trimmedWidth is null) missing.Add("trimmed coil width");
        if (trimTypeCode is null) missing.Add("trim type");
        if (missing.Count > 0)
            return $"Edge trimming is required, so this line needs a {string.Join(", a ", missing)}.";

        // A trimmed width WIDER than the coil it comes off is not a tolerance question — it is not a
        // coil. Legacy words it "Incoming coil width must be greater then trimmed coil width".
        if (incomingWidth!.Value - trimmedWidth!.Value < 0)
            return "The incoming coil width must be greater than the trimmed coil width.";
        return null;
    }

    /// <summary>How much is being trimmed off, in inches. Null when either width is missing.</summary>
    public static decimal? Difference(decimal? incomingWidth, decimal? trimmedWidth) =>
        incomingWidth is { } inc && trimmedWidth is { } trim ? inc - trim : null;

    /// <summary>
    /// Whether the trim falls outside the equipment's band — the overridable failure.
    ///
    /// <para>Legacy's test is <c>difference &lt; lower OR difference &gt; upper</c>, so both bounds
    /// are INCLUSIVE: a trim of exactly the lower limit is acceptable. A band edge is a real setting
    /// someone chose, and flipping the comparison would refuse the exact value the plant configured.</para>
    /// </summary>
    public static bool IsOutsideTolerance(decimal? difference, Tolerance tolerance) =>
        difference is { } d && (d < tolerance.LowerInches || d > tolerance.UpperInches);

    /// <summary>The refusal an operator sees, naming the band — legacy prints both limits so the
    /// person can tell which end they are on and by how much.</summary>
    public static string OutsideToleranceMessage(decimal difference, Tolerance tolerance) =>
        $"Trimmed width is outside the trimmer tolerance: this line trims {difference:0.00}\" off, " +
        $"and the difference between incoming and trimmed coil width must be between " +
        $"{tolerance.LowerInches:0.00}\" and {tolerance.UpperInches:0.00}\". " +
        "Correct the widths, or re-submit with trimmedWidthOverridden set to override it.";
}
