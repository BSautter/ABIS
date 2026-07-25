using Abis.Api.Data;
using Abis.Api.Models;

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

/// <summary>Creates the day's <c>shift</c> rows from the plant's own SHIFT_SCHEDULE calendar, so the
/// lines that are scheduled to run have a shift to run against without anyone hand-creating it.
/// <para><b>An improvement over legacy, not a port:</b> the calendar has been maintained for years
/// (~18.7k rows) but legacy still required a human to create each SHIFT row, which is why the live DB
/// carries shifts left open for days. This is deliberately the ONE piece of shift lifecycle that is
/// automated — creating a shift is additive and idempotent, whereas auto-CLOSING one would destroy the
/// operator's record of when work actually stopped, so ending stays a human action (the stale-shift
/// monitor surfaces the ones nobody closed).</para>
/// <para><c>args</c> optionally shifts the target day: an integer offset in days ("1" = tomorrow, for
/// a job that runs the evening before). Default is today. Off unless a scheduled job targets it.</para>
/// <para><b>⚠ DO NOT ENABLE without reviving a schedule source first (checked live 2026-07-25).</b> The
/// plant STOPPED maintaining SHIFT_SCHEDULE in <b>2009</b> — newest row 2009-01-14, zero rows in the
/// last 365 days — so on the real database this creates NOTHING. Falling back to LINE_SCHEDULE would be
/// worse, not better: its standing pattern says shift 1 starts 06:00 and shift 2 at 14:30, but the
/// plant actually starts them at <b>05:00</b> and <b>15:31</b>, so every auto-created shift would carry
/// a start time ~1 h from reality — and shift length is the DENOMINATOR of the efficiency calculation.
/// Shifts are created by hand today at the moment work actually begins, which is a truer start time
/// than any stored pattern can give. This operation stays for the case where the plant revives the
/// calendar; until then it is deliberately inert.</para></summary>
/// <remarks>Takes <see cref="IServiceScopeFactory"/>, not the repository: operations are registered as
/// singletons (the allowlist is a singleton) while <c>IAbisRepository</c> is scoped, so the repo is
/// resolved inside a per-execution scope — the same thing the hosted service does around a run.</remarks>
public sealed class CreateScheduledShiftsOperation(IServiceScopeFactory scopes, ILogger<CreateScheduledShiftsOperation> log)
    : IScheduledOperation
{
    public string Name => "create-scheduled-shifts";

    public async Task<int> ExecuteAsync(string? args, CancellationToken ct)
    {
        var offset = int.TryParse(args?.Trim(), out var d) ? d : 0;
        var day = DateTime.Now.Date.AddDays(offset);   // plant-local: shifts are scheduled in plant time
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAbisRepository>();
        var created = await repo.CreateScheduledShiftsAsync(day, ct);
        if (created.Count > 0)
            log.LogInformation("Created {Count} scheduled shift(s) for {Day:yyyy-MM-dd}: {Shifts}.",
                created.Count, day, string.Join(", ", created.Select(s => $"{s.ShiftNum}(line {s.LineNum}/type {s.ScheduleType})")));
        else
            // Say WHY nothing happened: the plant's calendar has been unmaintained since 2009, so
            // "created 0" is the expected result, not a silent failure someone should go hunting for.
            log.LogInformation(
                "No shifts to create for {Day:yyyy-MM-dd} — nothing on the SHIFT_SCHEDULE calendar for that date " +
                "(or they already exist). NOTE: the plant stopped maintaining that calendar in 2009, so this is " +
                "expected until a schedule source is revived.", day);
        return created.Count;
    }
}
