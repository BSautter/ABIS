using System.Globalization;

namespace AbisEdge.Tags;

/// <summary>Which polled tags carry the line's four running production counters — good pieces,
/// reject pieces, press strokes, and cut feed-length — the PLC increments as the line runs (legacy
/// OPC items <c>goodpartcnt</c> / <c>rejectpartcnt</c> / <c>strokecnt</c> / <c>feedlength</c>). Each
/// is optional; from <c>Edge:Opc:GoodCountTag</c> / <c>RejectCountTag</c> / <c>StrokeCountTag</c> /
/// <c>FeedLengthTag</c>. The per-coil-run delta (counts since the coil was loaded) is computed by the
/// DAS console against a baseline it captures at coil-run start — the edge exposes only the raw
/// cumulative values, exactly as it does for the stacker piece counter.</summary>
public sealed record CountersConfig(string? GoodTag, string? RejectTag, string? StrokeTag, string? FeedTag)
{
    /// <summary>Every configured counter tag (for the poller to include), skipping the unset ones.</summary>
    public IEnumerable<string> Tags =>
        new[] { GoodTag, RejectTag, StrokeTag, FeedTag }.Where(t => !string.IsNullOrWhiteSpace(t))!;
}

/// <summary>Parses a raw feed-length tag value into a length. Kept separate from
/// <see cref="PieceCount"/> because feed-length is a REAL measure (inches), not a whole count, so it
/// must not be rounded — otherwise a 12.75" cut reads as 13". Same trust rule: bad/missing/non-numeric
/// or negative reads are unknown (null), never a fabricated value.</summary>
public static class FeedLength
{
    public static double? Parse(string? value, string? quality)
    {
        if (value is null || !string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase))
            return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n >= 0
            ? n
            : null;
    }
}
