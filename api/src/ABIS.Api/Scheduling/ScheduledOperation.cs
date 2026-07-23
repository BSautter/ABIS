namespace Abis.Api.Scheduling;

/// <summary>A safe, in-process operation the scheduler may run. <b>Only registered operations are ever
/// executed</b> — an unknown <c>target_operation</c> is recorded "unsupported" and NEVER runs anything.
/// There is deliberately no shell-out and no legacy-job path: per the no-live-firing guardrail the
/// modern stack must NEVER fire legacy EDI/cron work (the legacy crontab stays the single owner).</summary>
public interface IScheduledOperation
{
    string Name { get; }
    /// <summary>Run the operation; returns an "affected count" recorded on the run.</summary>
    Task<int> ExecuteAsync(string? args, CancellationToken ct);
}

/// <summary>The allowlist: resolves a job's <c>target_operation</c> to a registered handler, or null.</summary>
public sealed class ScheduledOperationRegistry
{
    private readonly Dictionary<string, IScheduledOperation> _ops;
    public ScheduledOperationRegistry(IEnumerable<IScheduledOperation> ops) =>
        _ops = ops.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
    public IScheduledOperation? Resolve(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _ops.TryGetValue(name.Trim(), out var op) ? op : null;
    public IReadOnlyCollection<string> Names => _ops.Keys;
}

/// <summary>A do-nothing operation — the safe default for exercising the engine end-to-end.</summary>
public sealed class NoopOperation : IScheduledOperation
{
    public string Name => "noop";
    public Task<int> ExecuteAsync(string? args, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>Logs a heartbeat — a harmless, observable operation to confirm the engine is firing.</summary>
public sealed class HeartbeatOperation(ILogger<HeartbeatOperation> log) : IScheduledOperation
{
    public string Name => "heartbeat";
    public Task<int> ExecuteAsync(string? args, CancellationToken ct)
    {
        log.LogInformation("Scheduler heartbeat fired ({Args}).", args ?? "");
        return Task.FromResult(1);
    }
}
