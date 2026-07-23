using Abis.Api.Data;
using Abis.Api.Models;

namespace Abis.Api.Scheduling;

/// <summary>Bound from the "Scheduler" configuration section.</summary>
public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";
    /// <summary>Master switch — OFF by default. Even when on, only allowlisted operations run, so the
    /// modern stack can never fire legacy work.</summary>
    public bool Enabled { get; set; }
    /// <summary>Background poll cadence in seconds (clamped 10–300). Each UTC minute is processed once.</summary>
    public int PollSeconds { get; set; } = 30;
}

/// <summary>The scheduler execution core: finds due jobs and dispatches each to a registered operation,
/// recording every run. Unknown operations are recorded "unsupported" and never executed. Pure enough
/// to unit-test (inject the repo, registry, and the "now").</summary>
public sealed class SchedulerService(IAbisRepository repo, ScheduledOperationRegistry registry, ILogger<SchedulerService> log)
{
    /// <summary>Run every enabled job whose cron is due at <paramref name="utcNow"/>. Returns how many ran.</summary>
    public async Task<int> RunDueJobsAsync(DateTime utcNow, CancellationToken ct)
    {
        var jobs = await repo.GetScheduledJobsAsync(ct);
        var processed = 0;
        foreach (var job in jobs)
        {
            if (job.Enabled != 1) continue;
            if (!CronSchedule.IsDue(job.CronExpression, utcNow)) continue;
            await RunJobAsync(job, utcNow, ct);
            processed++;
        }
        return processed;
    }

    /// <summary>Run a single job now (manual trigger), recording the run.</summary>
    public Task<ScheduledJobRun> RunJobNowAsync(ScheduledJob job, CancellationToken ct) =>
        RunJobAsync(job, DateTime.UtcNow, ct);

    private async Task<ScheduledJobRun> RunJobAsync(ScheduledJob job, DateTime startedUtc, CancellationToken ct)
    {
        var op = registry.Resolve(job.TargetOperation);
        if (op is null)
        {
            // GUARDRAIL: an unknown/legacy operation is recorded and NEVER executed.
            log.LogWarning("Scheduled job {Id} '{Name}' has unsupported operation '{Op}' — not run.",
                job.ScheduledJobId, job.JobName, job.TargetOperation);
            return await repo.RecordJobRunAsync(job.ScheduledJobId, startedUtc, DateTime.UtcNow,
                "unsupported", null, $"No registered operation '{job.TargetOperation}'.", ct);
        }
        try
        {
            var affected = await op.ExecuteAsync(job.TargetArgs, ct);
            return await repo.RecordJobRunAsync(job.ScheduledJobId, startedUtc, DateTime.UtcNow, "success", affected, null, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Scheduled job {Id} '{Name}' failed.", job.ScheduledJobId, job.JobName);
            return await repo.RecordJobRunAsync(job.ScheduledJobId, startedUtc, DateTime.UtcNow, "error", null, ex.Message, ct);
        }
    }
}
