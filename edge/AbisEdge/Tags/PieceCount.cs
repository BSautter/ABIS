using System.Globalization;

namespace AbisEdge.Tags;

/// <summary>Configuration for the stacker piece-count read: which polled tag holds the stacker's
/// running piece counter (<see cref="Tag"/>), the default when <c>/piece-count</c> is called without
/// <c>?tag=</c>. From <c>Edge:Opc:PieceCountTag</c>. The per-skid delta (pieces since the last skid)
/// is computed by the DAS console, not here — the edge just exposes the raw counter.</summary>
public sealed record PieceCountConfig(string? Tag);

/// <summary>Parses a raw stacker piece-counter tag value into a count. Kept pure + separate so it is
/// unit-testable with no OPC server and shared by the <c>/piece-count</c> endpoint. The DAS console
/// reads this to auto-fill the pieces-per-skid the operator would otherwise hand-count.</summary>
public static class PieceCount
{
    /// <summary>The counter as a non-negative whole number, or null = unknown (a bad-quality, missing,
    /// non-numeric, or negative read). We never fabricate a count from noise; the console leaves the
    /// pieces field for the operator when this is null.</summary>
    public static long? Parse(string? value, string? quality)
    {
        if (value is null || !string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase))
            return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        // Tolerate a float-formatted counter ("1234.0") like the run-state numeric rule; a piece count
        // is a whole number, so round. Negative = not a valid count → unknown.
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n >= 0
            ? (long)Math.Round(n)
            : null;
    }
}
