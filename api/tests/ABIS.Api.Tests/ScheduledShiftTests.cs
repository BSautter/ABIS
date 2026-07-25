using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Creating the day's shifts from the plant's SHIFT_SCHEDULE calendar. This is an IMPROVEMENT
/// over legacy (which required a human to create every shift row) so the rules matter: it must create
/// only what is genuinely scheduled, never invent a time, never duplicate, and never resurrect a
/// cancelled shift.</summary>
public sealed class ScheduledShiftTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_schedshift_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(WebApplicationFactory<Program> f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    private static async Task<JsonElement> CreateToday(HttpClient c)
    {
        var r = await c.PostAsync("/api/das/shifts/create-scheduled", null);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Scheduled_shifts_are_created_from_the_calendar_with_their_scheduled_times()
    {
        using var f = new Factory();
        var c = Client(f);

        var created = (await CreateToday(c)).EnumerateArray().ToList();
        // The fixture schedules 4 rows for today: two creatable, one CANCELLED, one with no times.
        Assert.Equal(2, created.Count);

        // Line 110 type 1: times come from the calendar row itself (05:00).
        var l110 = created.Single(s => s.GetProperty("lineNum").GetInt64() == 110);
        Assert.Equal(1, l110.GetProperty("scheduleType").GetInt32());
        var start110 = l110.GetProperty("startTime").GetDateTime();
        Assert.Equal(DateTime.Today, start110.Date);          // grafted onto TODAY, not the pattern's fossil date
        Assert.Equal(new TimeSpan(5, 0, 0), start110.TimeOfDay);
        // Auto-created shifts are OPEN — the DAS ends them (which is what stamps dt_total).
        Assert.Equal(JsonValueKind.Null, l110.GetProperty("endTime").ValueKind);

        // Line 120 type 1: the calendar row has no times, so the LINE's standing pattern supplies 06:30.
        var l120 = created.Single(s => s.GetProperty("lineNum").GetInt64() == 120);
        Assert.Equal(new TimeSpan(6, 30, 0), l120.GetProperty("startTime").GetDateTime().TimeOfDay);
    }

    [Fact]
    public async Task A_cancelled_calendar_row_is_never_created()
    {
        using var f = new Factory();
        var c = Client(f);

        await CreateToday(c);
        // Line 110 type 2 is on the calendar for today but cancelled — it must not exist as a shift.
        var shifts = await c.GetFromJsonAsync<JsonElement>("/api/shifts?pageSize=200&lineNum=110");
        Assert.DoesNotContain(shifts.GetProperty("items").EnumerateArray(),
            s => s.GetProperty("scheduleType").GetInt32() == 2
                 && s.GetProperty("startTime").GetDateTime().Date == DateTime.Today);
    }

    [Fact]
    public async Task A_calendar_row_with_no_times_anywhere_is_skipped_not_invented()
    {
        using var f = new Factory();
        var c = Client(f);

        var created = (await CreateToday(c)).EnumerateArray().ToList();
        // Line 120 type 3 is scheduled and NOT cancelled, but neither the calendar row nor the line
        // pattern carries a time — guessing one would put a wrong start on the production record.
        Assert.DoesNotContain(created, s => s.GetProperty("scheduleType").GetInt32() == 3);
    }

    [Fact]
    public async Task Running_it_again_creates_nothing_new()
    {
        using var f = new Factory();
        var c = Client(f);

        var first = (await CreateToday(c)).EnumerateArray().Count();
        Assert.Equal(2, first);
        // Idempotent: the scheduled job may fire more than once, and an operator can hit the endpoint
        // any time — neither may produce a duplicate shift for the same (line, type, day).
        var second = (await CreateToday(c)).EnumerateArray().Count();
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task A_date_with_nothing_scheduled_creates_nothing()
    {
        using var f = new Factory();
        var c = Client(f);

        // The calendar only carries today in the fixture; a quiet day must be a clean no-op, which is
        // what makes running this daily safe even when the plant isn't working.
        var r = await c.PostAsync($"/api/das/shifts/create-scheduled?onDate={DateTime.Today.AddDays(-30):yyyy-MM-dd}", null);
        r.EnsureSuccessStatusCode();
        Assert.Empty((await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Fact]
    public async Task The_operation_is_on_the_scheduler_allowlist()
    {
        using var f = new Factory();
        var c = Client(f);

        // The engine only ever runs registered operations; this one must be resolvable by name or a
        // job targeting it would be recorded "unsupported" and silently do nothing.
        var ops = await c.GetFromJsonAsync<JsonElement>("/api/admin/jobs/operations");
        Assert.Contains(ops.EnumerateArray(), o => o.GetString() == "create-scheduled-shifts");
    }
}
