using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// A decommissioned line takes no new work, but keeps everything it ever did.
///
/// <para><b>What was missing.</b> BL 60 (line_num 3) came off the floor in 2026-08, and hiding it from
/// the floor board was the easy half. Nothing stopped new work being booked to it: the downtime form's
/// line field is a free-text number, and neither the shift, downtime nor job write validated
/// <c>line_num</c> at all — "3" was accepted without question, and so was "999".</para>
///
/// <para><b>The line that is deliberately not drawn.</b> Reads and history are untouched. BL 60 carries
/// <b>1,163 jobs</b> on the live database and each must keep saying it ran on BL 60; restating the past
/// to tidy the present would be the worse bug. So this is a guard on <i>new</i> work only.</para>
///
/// <para><b>And winding down still works.</b> Ending a shift, closing a coil run, reversing one and
/// removing a queue entry stay available on a retired line. A line does not always come off the floor
/// cleanly — BL 60 left 9 open shift rows and a queued job behind on live — and a guard that blocked
/// the closing operations would strand exactly that, with no way in the app to clear it.</para>
/// </summary>
public sealed class RetiredLineTests
{
    // Line 3 is BL 60 in both the fixture and the live LINE table (verified on .230), so the seeded
    // number and the real one agree and the test is not asserting against a fixture-only id.
    private const long RetiredLine = 3;
    private const long LiveLine = 110;

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"abis_retired_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "Sqlite");
            b.UseSetting("Database:ConnectionString", $"Data Source={_db}");
            b.UseSetting("Database:Seed", "true");
            b.UseSetting("ApiKeys:Enabled", "true");
            b.UseSetting("ApiKeys:Keys:0", "test-key");
            b.UseSetting("Board:DecommissionedLines:0", RetiredLine.ToString());
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_db)) File.Delete(_db); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(Factory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    // ---- New work is refused --------------------------------------------------------

    [Fact]
    public async Task Downtime_cannot_be_logged_against_a_retired_line()
    {
        // The concrete hole: the form's line field is free text, so nothing but this stops a typed 3.
        using var f = new Factory();
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/downtime", new
        {
            abJobNum = 1001, lineNum = RetiredLine, startingTime = DateTime.Now.ToString("s"), note = "x",
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("decommissioned", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_shift_cannot_be_opened_on_a_retired_line()
    {
        using var f = new Factory();
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/shifts", new
        {
            startTime = DateTime.Now.ToString("s"), lineNum = RetiredLine, operatorInitial = "ZZ",
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task A_job_cannot_be_scheduled_onto_a_retired_line()
    {
        // Scheduling a job onto a line nobody is standing at puts it in a queue that is never worked.
        using var f = new Factory();
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/jobs", new
        {
            orderAbcNum = 9001, orderItemNum = 7001, lineNum = RetiredLine,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData("POST", "current-job")]
    [InlineData("POST", "current-coil")]
    [InlineData("POST", "shift/start")]
    [InlineData("POST", "coil-run/start")]
    [InlineData("POST", "change-job")]
    [InlineData("POST", "queue/reorder")]
    public async Task The_DAS_operations_that_assign_work_are_refused_on_a_retired_line(string method, string op)
    {
        using var f = new Factory();
        var c = Client(f);

        var r = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/das/lines/{RetiredLine}/{op}")
        { Content = JsonContent.Create(new { }) });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("decommissioned", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- …but the line can still be wound down, and history is untouched -------------

    [Theory]
    [InlineData("shift/end")]
    [InlineData("coil-run/end")]
    [InlineData("coil-run/reverse")]
    public async Task Winding_a_retired_line_down_is_still_allowed(string op)
    {
        // BL 60 left 9 open shift rows and a queued job behind on the live database. If the guard also
        // blocked the closing operations, that state would be stranded with no way to clear it from
        // the app — so these must answer on their own terms, NOT with the decommissioned refusal.
        using var f = new Factory();
        var c = Client(f);

        var r = await c.PostAsJsonAsync($"/api/das/lines/{RetiredLine}/{op}", new { endWeight = 0 });
        var body = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain("decommissioned", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_queue_entry_can_still_be_removed_from_a_retired_line()
    {
        // The other half of winding down: clearing what the line was left holding.
        using var f = new Factory();
        var c = Client(f);

        var r = await c.DeleteAsync($"/api/das/lines/{RetiredLine}/queue/1001");
        Assert.DoesNotContain("decommissioned", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_line_still_appears_in_the_lookup_with_its_name_and_the_flag()
    {
        // Identity, not floor presence. A job row must be able to say it ran on BL 60, so the lookup
        // keeps the line and marks it — it does not drop it.
        using var f = new Factory();
        var c = Client(f);

        var lines = await c.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/lookups/lines");
        var bl60 = lines!.FirstOrDefault(l => Convert.ToInt64(l["lineNum"].ToString()) == RetiredLine);

        Assert.NotNull(bl60);
        Assert.Equal("True", bl60!["decommissioned"].ToString(), ignoreCase: true);
        Assert.Contains("60", bl60["lineDesc"].ToString()!);
    }

    [Fact]
    public async Task A_historical_record_on_a_retired_line_can_still_be_corrected()
    {
        // PUT is a full replace, so an edit resends the stored line. Refusing every retired line here
        // would make a historical BL 60 record permanently uneditable — a typo in a 2019 downtime note
        // could never be fixed. Only a CHANGE onto a retired line is refused.
        using var f = new Factory();
        var c = Client(f);

        // Seed one directly, since the API now (correctly) refuses to create it.
        var created = await c.PostAsJsonAsync("/api/downtime", new
        {
            abJobNum = 1001, lineNum = LiveLine, startingTime = DateTime.Now.ToString("s"), note = "before",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var instance = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["instanceNum"].ToString();

        // Editing it while KEEPING its live line is fine…
        var ok = await c.PutAsJsonAsync($"/api/downtime/{instance}", new
        {
            abJobNum = 1001, lineNum = LiveLine, startingTime = DateTime.Now.ToString("s"), note = "after",
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // …but MOVING it onto the retired line is not.
        var moved = await c.PutAsJsonAsync($"/api/downtime/{instance}", new
        {
            abJobNum = 1001, lineNum = RetiredLine, startingTime = DateTime.Now.ToString("s"), note = "moved",
        });
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
    }

    [Fact]
    public async Task A_live_line_is_completely_unaffected()
    {
        // The guard must be narrow. If it caught a working line, the floor would stop.
        using var f = new Factory();
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/downtime", new
        {
            abJobNum = 1001, lineNum = LiveLine, startingTime = DateTime.Now.ToString("s"), note = "normal",
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var s = await c.PostAsJsonAsync("/api/shifts", new
        {
            startTime = DateTime.Now.ToString("s"), lineNum = LiveLine, operatorInitial = "ZZ",
        });
        Assert.Equal(HttpStatusCode.Created, s.StatusCode);
    }

    [Fact]
    public async Task The_guard_follows_configuration_rather_than_a_hardcoded_line()
    {
        // Point the setting at a DIFFERENT line and line 3 must go back to being ordinary. Without
        // this, a guard that simply hardcoded 3 would pass every other test in this class.
        using var noneRetired = new NoRetirementFactory();
        var c = noneRetired.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");

        var r = await c.PostAsJsonAsync("/api/downtime", new
        {
            abJobNum = 1001, lineNum = RetiredLine, startingTime = DateTime.Now.ToString("s"), note = "x",
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    private sealed class NoRetirementFactory : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"abis_noretire_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "Sqlite");
            b.UseSetting("Database:ConnectionString", $"Data Source={_db}");
            b.UseSetting("Database:Seed", "true");
            b.UseSetting("ApiKeys:Enabled", "true");
            b.UseSetting("ApiKeys:Keys:0", "test-key");
            b.UseSetting("Board:DecommissionedLines:0", "999");   // a different line, not BL 60
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_db)) File.Delete(_db); } catch { /* best effort */ }
        }
    }
}
