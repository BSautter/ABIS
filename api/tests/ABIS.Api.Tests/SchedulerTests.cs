using Abis.Api.Scheduling;
using Xunit;

namespace ABIS.Api.Tests;

public class CronScheduleTests
{
    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Wildcard_is_always_due() => Assert.True(CronSchedule.IsDue("* * * * *", Utc(2026, 7, 20, 13, 47)));

    [Theory]
    [InlineData("30 * * * *", 30, true)]
    [InlineData("30 * * * *", 31, false)]
    [InlineData("*/15 * * * *", 45, true)]
    [InlineData("*/15 * * * *", 7, false)]
    [InlineData("0-30/10 * * * *", 20, true)]
    [InlineData("0-30/10 * * * *", 25, false)]
    public void Minute_field(string cron, int minute, bool due)
        => Assert.Equal(due, CronSchedule.IsDue(cron, Utc(2026, 7, 20, 9, minute)));

    [Fact]
    public void Hour_and_month_and_dom()
    {
        Assert.True(CronSchedule.IsDue("0 9 1 7 *", Utc(2026, 7, 1, 9, 0)));
        Assert.False(CronSchedule.IsDue("0 9 1 7 *", Utc(2026, 7, 2, 9, 0)));   // wrong day
        Assert.False(CronSchedule.IsDue("0 9 1 7 *", Utc(2026, 8, 1, 9, 0)));   // wrong month
    }

    [Fact]
    public void Day_of_week_matches_regardless_of_calendar()
    {
        var d = Utc(2026, 7, 20, 9, 30);
        var dow = (int)d.DayOfWeek;
        Assert.True(CronSchedule.IsDue($"30 9 * * {dow}", d));
        Assert.False(CronSchedule.IsDue($"30 9 * * {(dow + 1) % 7}", d));
        // 7 is an alias for Sunday.
        var sunday = d.AddDays(7 - dow == 7 ? 0 : 7 - dow);  // next Sunday (or d if already Sun)
        Assert.True(CronSchedule.IsDue("30 9 * * 7", Utc(sunday.Year, sunday.Month, sunday.Day, 9, 30)));
    }

    [Fact]
    public void Dom_or_dow_when_both_restricted()
    {
        var d = Utc(2026, 7, 20, 9, 0);                 // day 20
        var dow = (int)d.DayOfWeek;
        // day-of-month matches (20) even though the dow is different → due (OR rule).
        Assert.True(CronSchedule.IsDue($"0 9 20 * {(dow + 2) % 7}", d));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a cron")]
    [InlineData("* * *")]
    public void Invalid_is_not_due(string? cron) => Assert.False(CronSchedule.IsDue(cron, Utc(2026, 7, 20, 9, 0)));

    [Fact]
    public void Six_field_drops_the_seconds() => Assert.True(CronSchedule.IsDue("0 30 9 * * *", Utc(2026, 7, 20, 9, 30)));
}
