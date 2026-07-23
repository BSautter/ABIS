namespace Abis.Api.Scheduling;

/// <summary>The background loop that drives the scheduler. <b>Inert unless Scheduler:Enabled=true.</b>
/// Polls on a cadence, processing each UTC minute at most once, and delegates to <see cref="SchedulerService"/>
/// (which only ever runs allowlisted operations). A scope is created per pass so the scoped repository +
/// service resolve correctly from this singleton host.</summary>
public sealed class SchedulerHostedService(
    SchedulerOptions options, IServiceScopeFactory scopes, ILogger<SchedulerHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            log.LogInformation("Scheduler engine is disabled (Scheduler:Enabled=false); no jobs will run.");
            return;
        }
        log.LogInformation("Scheduler engine enabled; polling every {Poll}s. Only allowlisted operations run.", options.PollSeconds);

        var poll = TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 10, 300));
        string? lastMinute = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var minuteKey = now.ToString("yyyyMMddHHmm");
                if (minuteKey != lastMinute)      // process each wall-clock minute once
                {
                    lastMinute = minuteKey;
                    using var scope = scopes.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<SchedulerService>();
                    await svc.RunDueJobsAsync(now, stoppingToken);
                }
            }
            catch (Exception ex) { log.LogError(ex, "Scheduler poll failed."); }

            try { await Task.Delay(poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
