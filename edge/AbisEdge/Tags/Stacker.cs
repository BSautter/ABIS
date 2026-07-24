using System.Globalization;

namespace AbisEdge.Tags;

/// <summary>Which polled tags carry a line's two stacker stations — each head builds a skid in
/// parallel, so the operator watches both at once (legacy OPC items
/// <c>stacker&lt;n&gt;.station1/2_stack_counter</c>, <c>Sta1/2StackComplete</c>, plus the stacker
/// scale <c>ScaleSkidWt</c> / <c>ScaleSkidId</c>). Each is optional; from
/// <c>Edge:Opc:Stacker{Station1Count,Station2Count,Station1Done,Station2Done,ScaleWeight,ScaleSkidId}Tag</c>.
/// The DAS console pairs the LIVE per-station count from here with the skid AT each head (from
/// LINE_CURRENT_STATUS.SHEET_SKID_STACKER_1/2, which the line board already resolves).</summary>
public sealed record StackerConfig(
    string? Station1CountTag, string? Station2CountTag,
    string? Station1DoneTag, string? Station2DoneTag,
    string? ScaleWeightTag, string? ScaleSkidIdTag)
{
    /// <summary>Every configured stacker tag (for the poller to include), skipping the unset ones.</summary>
    public IEnumerable<string> Tags =>
        new[] { Station1CountTag, Station2CountTag, Station1DoneTag, Station2DoneTag, ScaleWeightTag, ScaleSkidIdTag }
            .Where(t => !string.IsNullOrWhiteSpace(t))!;
}

/// <summary>Parses a raw "stack complete" tag value into a bool. A PLC done bit is a boolean/word/1;
/// same trust rule as the counters — a bad-quality or unparseable read is unknown (null), never
/// fabricated, so the console never falsely shows a station as complete.</summary>
public static class StackDone
{
    private static readonly string[] TrueValues = ["1", "TRUE", "T", "ON", "YES", "COMPLETE", "DONE"];

    public static bool? Parse(string? value, string? quality)
    {
        if (value is null || !string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase))
            return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (TrueValues.Contains(v, StringComparer.OrdinalIgnoreCase)) return true;
        // A numeric other than the listed truthy "1" is false when it parses (e.g. 0); non-numeric,
        // non-truthy text is unknown rather than a guessed false.
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n != 0 : (bool?)null;
    }
}
