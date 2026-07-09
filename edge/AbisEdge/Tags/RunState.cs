namespace AbisEdge.Tags;

/// <summary>Configuration for interpreting one polled tag as the line's run-state — which tag to
/// read (<see cref="Tag"/>, a node id already included in the polled set) and the raw values that
/// count as "running" (<see cref="RunningValues"/>, case-insensitive). From <c>Edge:Opc:RunStateTag</c>
/// and <c>Edge:Opc:RunningValues</c>.</summary>
public sealed record RunStateConfig(string? Tag, IReadOnlyList<string> RunningValues)
{
    /// <summary>The default "running" set — covers boolean, numeric, and word-style PLC signals.</summary>
    public static readonly string[] DefaultRunningValues = ["RUNNING", "RUN", "ON", "START", "STARTED", "1", "TRUE"];
}

/// <summary>Interprets a raw PLC/OPC tag value as the line running (true) / stopped (false) /
/// unknown (null). Kept pure + separate so it is unit-testable with no OPC server and shared by the
/// <c>/run-state</c> endpoint. The DAS console turns the running→stopped transition into an
/// auto-opened downtime instance (the operator then assigns the reason).</summary>
public static class RunState
{
    /// <summary>True = running, false = stopped, null = unknown (a bad-quality or missing read — we
    /// never fabricate a state from noise; the caller should not open/close downtime on null).</summary>
    public static bool? IsRunning(string? value, string? quality, IReadOnlyCollection<string> runningValues)
    {
        if (value is null || !string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase))
            return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        return runningValues.Any(rv => string.Equals(rv, v, StringComparison.OrdinalIgnoreCase));
    }
}
