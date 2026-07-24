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
    // The live INGEAR DA server returns .NET-style "True"/"False" for BOOL items (confirmed on .170
    // 2026-07-24: PLC5-BL110.autorunning read "False"). Without an explicit FALSY set those fall
    // through to "unknown", so a STOPPED line would read as unknown instead of not-running — the
    // fault/health lamps would show no state at all rather than the true one.
    private static readonly string[] FalseValues = ["0", "FALSE", "F", "OFF", "NO"];

    public static bool? Parse(string? value, string? quality)
    {
        if (value is null || !string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase))
            return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (TrueValues.Contains(v, StringComparer.OrdinalIgnoreCase)) return true;
        if (FalseValues.Contains(v, StringComparer.OrdinalIgnoreCase)) return false;
        // A number that isn't one of the words above: non-zero is truthy (e.g. activefault carries
        // its fault CODE — 68 means a fault is active). Non-numeric, non-keyword text stays unknown
        // rather than a guessed value.
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n != 0 : (bool?)null;
    }
}
