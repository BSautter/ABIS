using System.Globalization;

namespace AbisEdge.Tags;

/// <summary>How a raw run-state tag value is judged running vs stopped.</summary>
public enum RunStateMode
{
    /// <summary>Running when the value is one of <see cref="RunStateConfig.RunningValues"/>
    /// (a plain "running" boolean / word / 1).</summary>
    Equals,
    /// <summary>Running when the value is NOT one of <see cref="RunStateConfig.RunningValues"/>
    /// — for an INVERTED signal like an "idle" bit (list the idle/stopped values, e.g. 1/TRUE).</summary>
    NotEquals,
    /// <summary>Running when the value parses as a number greater than
    /// <see cref="RunStateConfig.Threshold"/> — e.g. strokes-per-minute > 0.</summary>
    GreaterThan,
}

/// <summary>Configuration for interpreting one polled tag as the line's run-state: which tag
/// (<see cref="Tag"/>), how to judge it (<see cref="Mode"/>), the value set for equals/not-equals
/// (<see cref="RunningValues"/>, case-insensitive), and the numeric cut-off for greater-than
/// (<see cref="Threshold"/>). From <c>Edge:Opc:RunStateTag</c> / <c>RunStateMode</c> /
/// <c>RunningValues</c> / <c>RunStateThreshold</c>.</summary>
public sealed record RunStateConfig(
    string? Tag,
    IReadOnlyList<string> RunningValues,
    RunStateMode Mode = RunStateMode.Equals,
    double Threshold = 0)
{
    /// <summary>The default "running" set for equals mode — covers boolean, numeric, and word-style
    /// PLC signals.</summary>
    public static readonly string[] DefaultRunningValues = ["RUNNING", "RUN", "ON", "START", "STARTED", "1", "TRUE"];
}

/// <summary>Interprets a raw PLC/OPC tag value as the line running (true) / stopped (false) /
/// unknown (null). Kept pure + separate so it is unit-testable with no OPC server and shared by the
/// <c>/run-state</c> endpoint. The DAS console turns the running→stopped transition into an
/// auto-opened downtime instance (the operator then assigns the reason).</summary>
public static class RunState
{
    /// <summary>True = running, false = stopped, null = unknown (a bad-quality / missing / — under a
    /// numeric rule — non-numeric read). We never fabricate a state from noise; the caller must not
    /// open/close downtime on null.</summary>
    public static bool? IsRunning(string? value, string? quality, RunStateConfig cfg)
    {
        if (value is null || !string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase))
            return null;
        var v = value.Trim();
        if (v.Length == 0) return null;

        return cfg.Mode switch
        {
            RunStateMode.GreaterThan =>
                double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                    ? n > cfg.Threshold
                    : null,                                            // non-numeric under a numeric rule = unknown
            RunStateMode.NotEquals => !Matches(v, cfg.RunningValues),  // inverted (e.g. idle bit)
            _ => Matches(v, cfg.RunningValues),                        // Equals
        };
    }

    /// <summary>Equals-mode convenience (kept for callers/tests that only pass a running-value set).</summary>
    public static bool? IsRunning(string? value, string? quality, IReadOnlyCollection<string> runningValues)
        => IsRunning(value, quality, new RunStateConfig(null, runningValues as IReadOnlyList<string> ?? runningValues.ToArray()));

    private static bool Matches(string v, IReadOnlyList<string> set)
        => set.Any(rv => string.Equals(rv, v, StringComparison.OrdinalIgnoreCase));
}
