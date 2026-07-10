using Abis.Api.Health;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The report-not-triggered (outbound-EDI stall) evaluator. The notification bell alarms on its
/// verdict, so it must stay quiet unless it's real: never when disabled, off-hours/weekend, or with no EDI
/// on record — only a genuinely stale pipeline inside the business window trips it.</summary>
public class ReportStallTests
{
    private static readonly ReportStallOptions On = new() { Enabled = true, StaleMinutes = 60, BusinessStartHour = 6, BusinessEndHour = 20, BusinessDaysOnly = true };
    // A fixed business-hours weekday clock (Wed 2026-07-08 10:00) so the window logic is deterministic.
    private static readonly DateTime NowBiz = new(2026, 7, 8, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Stale_during_business_hours_trips()
    {
        var r = ReportStall.Evaluate(NowBiz.AddMinutes(-90), NowBiz, On);
        Assert.True(r.Stalled);
        Assert.Equal(90, r.AgeMinutes);
        Assert.True(r.WithinBusinessWindow);
    }

    [Fact]
    public void Recent_activity_is_not_stalled()
        => Assert.False(ReportStall.Evaluate(NowBiz.AddMinutes(-10), NowBiz, On).Stalled);

    [Fact]
    public void Disabled_is_never_stalled()
    {
        var off = new ReportStallOptions { Enabled = false, StaleMinutes = 60 };
        Assert.False(ReportStall.Evaluate(NowBiz.AddDays(-3), NowBiz, off).Stalled);
    }

    [Fact]
    public void No_activity_on_record_is_not_stalled()
        => Assert.False(ReportStall.Evaluate(null, NowBiz, On).Stalled);   // fresh/dev DB — don't cry wolf

    [Fact]
    public void Outside_business_hours_never_alarms()
    {
        var evening = new DateTime(2026, 7, 8, 22, 0, 0, DateTimeKind.Local);
        var r = ReportStall.Evaluate(evening.AddHours(-5), evening, On);
        Assert.False(r.Stalled);
        Assert.False(r.WithinBusinessWindow);
    }

    [Fact]
    public void Weekend_never_alarms_when_business_days_only()
    {
        var saturday = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Local);   // Sat
        Assert.False(ReportStall.Evaluate(saturday.AddHours(-5), saturday, On).Stalled);
    }

    [Fact]
    public void Weekend_can_alarm_when_business_days_only_is_off()
    {
        var saturday = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Local);
        var sevenDay = new ReportStallOptions { Enabled = true, StaleMinutes = 60, BusinessDaysOnly = false };
        Assert.True(ReportStall.Evaluate(saturday.AddHours(-5), saturday, sevenDay).Stalled);
    }

    [Fact]
    public void Threshold_is_the_boundary()
    {
        Assert.False(ReportStall.Evaluate(NowBiz.AddMinutes(-60), NowBiz, On).Stalled);   // exactly at = not yet
        Assert.True(ReportStall.Evaluate(NowBiz.AddMinutes(-61), NowBiz, On).Stalled);
    }
}
