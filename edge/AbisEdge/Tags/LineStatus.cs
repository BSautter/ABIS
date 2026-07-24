namespace AbisEdge.Tags;

/// <summary>Which polled tags carry a line's live health/status — the run bit, the active-fault lamp,
/// and the no-auto (manual/lockout) flag the operator and the auto-status controls watch (legacy OPC
/// items <c>PLC5-BL&lt;n&gt;.autorunning</c> / <c>activefault</c> / <c>noauto</c>). Each is optional;
/// from <c>Edge:Opc:{AutoRunning,ActiveFault,NoAuto}Tag</c>. The `/run-state` endpoint stays the
/// authoritative run signal (the plant judges running by the stroke counter climbing); this exposes
/// the direct bits for the fault/health lamps and the no-auto lockout, which run-state doesn't cover.</summary>
public sealed record LineStatusConfig(string? AutoRunningTag, string? ActiveFaultTag, string? NoAutoTag)
{
    public IEnumerable<string> Tags =>
        new[] { AutoRunningTag, ActiveFaultTag, NoAutoTag }.Where(t => !string.IsNullOrWhiteSpace(t))!;
}
