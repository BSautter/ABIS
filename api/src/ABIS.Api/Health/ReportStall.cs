namespace Abis.Api.Health;

/// <summary>Config for the "a scheduled report/job didn't fire" alert (Notifications:EdiStall). The
/// flagship, DB-visible signal is outbound EDI: the legacy GXS.ksh / ediprocess crons emit X12
/// transactions every 5 / 30 min during the day, so if the newest one goes stale during business hours
/// the pipeline is likely stuck. OFF by default; thresholds are the deploy-time tuning knob so the alert
/// never cries wolf on a quiet day / off-hours.</summary>
public sealed class ReportStallOptions
{
    public const string SectionName = "Notifications:EdiStall";

    /// <summary>Master switch. When false the check is inert (never stalled).</summary>
    public bool Enabled { get; set; }

    /// <summary>Minutes with no new EDI transaction (during the business window) before it reads stalled.
    /// Default 60 gives margin over the 30-min ediprocess cadence.</summary>
    public int StaleMinutes { get; set; } = 60;

    /// <summary>Business window start hour (server-local, inclusive). Outside it we never alarm.</summary>
    public int BusinessStartHour { get; set; } = 6;

    /// <summary>Business window end hour (server-local, exclusive).</summary>
    public int BusinessEndHour { get; set; } = 20;

    /// <summary>Skip Saturdays/Sundays (EDI runs on business days). Set false for a 7-day operation.</summary>
    public bool BusinessDaysOnly { get; set; } = true;
}

/// <param name="Enabled">Is the check turned on.</param>
/// <param name="Stalled">True = enabled, inside the business window, and the newest EDI is older than the threshold.</param>
/// <param name="LastActivity">The newest outbound-EDI transaction time (null = none on record).</param>
/// <param name="AgeMinutes">Minutes since <see cref="LastActivity"/> (null when there is none).</param>
/// <param name="ThresholdMinutes">The configured staleness threshold.</param>
/// <param name="WithinBusinessWindow">Whether "now" is inside the business window (no alarm outside it).</param>
public sealed record ReportStallResult(bool Enabled, bool Stalled, DateTime? LastActivity, double? AgeMinutes, int ThresholdMinutes, bool WithinBusinessWindow);

/// <summary>Decides whether a scheduled report/job that should have produced output hasn't — currently
/// the outbound-EDI pipeline. Pure + separate so it is unit-testable with fixed clocks, and deliberately
/// conservative: it never alarms when disabled, outside the business window/weekend, or with no EDI on
/// record (a fresh/dev DB), so it can't cry wolf.</summary>
public static class ReportStall
{
    public static ReportStallResult Evaluate(DateTime? lastActivity, DateTime now, ReportStallOptions opts)
    {
        var within = InBusinessWindow(now, opts);
        double? age = lastActivity is { } last ? (now - last).TotalMinutes : null;
        var stalled = opts.Enabled && within && age is { } a && a > opts.StaleMinutes;
        return new ReportStallResult(opts.Enabled, stalled, lastActivity, age, opts.StaleMinutes, within);
    }

    private static bool InBusinessWindow(DateTime now, ReportStallOptions opts)
    {
        if (opts.BusinessDaysOnly && now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        return now.Hour >= opts.BusinessStartHour && now.Hour < opts.BusinessEndHour;
    }
}
