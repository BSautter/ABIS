using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The live line board (legacy <c>LINE_CURRENT_STATUS</c>): one row per line carrying the
/// line's current shift/job/coil and its physical skid positions. Read-only — the DAS write path
/// owns the mutations. Calls run as the API-key service account.</summary>
public sealed class LineBoardTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_lineboard_{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Board_lists_every_line_with_its_shift_job_and_coil()
    {
        using var f = new Factory();
        var c = Client(f);

        var board = await c.GetFromJsonAsync<JsonElement>("/api/das/line-board");
        var lines = board.EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        // Ordered by line number, and each row carries its LINE description (not just the code).
        Assert.Equal(110, lines[0].GetProperty("lineNum").GetInt64());
        Assert.Equal("Cut-to-length 1", lines[0].GetProperty("lineDesc").GetString());

        var running = lines[0];
        Assert.Equal(7701, running.GetProperty("shiftNum").GetInt64());
        Assert.Equal(1001, running.GetProperty("abJobNum").GetInt64());
        Assert.Equal(5001, running.GetProperty("coilAbcNum").GetInt64());
        Assert.Equal(42, running.GetProperty("coilProcessRate").GetInt32());
        // Enriched from the joined rows, not just the raw ids.
        Assert.False(string.IsNullOrWhiteSpace(running.GetProperty("coilOrgNum").GetString()));
        Assert.Equal(JsonValueKind.Number, running.GetProperty("shiftScheduleType").ValueKind);
        Assert.Equal(4001, running.GetProperty("scrapSkidNum").GetInt64());
    }

    [Fact]
    public async Task Board_reports_an_idle_line_as_null_shift_job_and_coil()
    {
        using var f = new Factory();
        var c = Client(f);

        var idle = await c.GetFromJsonAsync<JsonElement>("/api/das/line-board/120");
        Assert.Equal(JsonValueKind.Null, idle.GetProperty("shiftNum").ValueKind);
        Assert.Equal(JsonValueKind.Null, idle.GetProperty("abJobNum").ValueKind);
        Assert.Equal(JsonValueKind.Null, idle.GetProperty("coilAbcNum").ValueKind);
        // A skid parked on the board survives the shift ending.
        var skids = idle.GetProperty("skids").EnumerateArray().ToList();
        Assert.Single(skids);
        Assert.Equal(3003, skids[0].GetProperty("sheetSkidNum").GetInt64());
    }

    [Fact]
    public async Task Skid_positions_are_unpivoted_in_line_order_and_resolved_against_sheet_skid()
    {
        using var f = new Factory();
        var c = Client(f);

        var running = await c.GetFromJsonAsync<JsonElement>("/api/das/line-board/110");
        var skids = running.GetProperty("skids").EnumerateArray().ToList();
        // Floor positions 0 and 5 plus one stacker head — occupied slots only, in board order
        // (the ordinal sort, not a string sort).
        Assert.Equal(new[] { "0", "5", "STACKER_1" }, skids.Select(s => s.GetProperty("slot").GetString()).ToList());
        Assert.Equal(3001, skids[0].GetProperty("sheetSkidNum").GetInt64());
        Assert.Equal("110-1001-01", skids[0].GetProperty("sheetSkidDisplayNum").GetString());
        Assert.Equal(100, skids[0].GetProperty("skidPieces").GetInt32());
        Assert.Equal(1980m, skids[0].GetProperty("sheetNetWt").GetDecimal());
        // The stacker head is occupied by a skid whose sheet_skid row does not exist yet (the DAS
        // station writes the position first): the slot still reports, with the detail left null.
        Assert.Equal("STACKER_1", skids[2].GetProperty("slot").GetString());
        Assert.Equal(3099, skids[2].GetProperty("sheetSkidNum").GetInt64());
        Assert.Equal(JsonValueKind.Null, skids[2].GetProperty("sheetSkidDisplayNum").ValueKind);
    }

    [Fact]
    public async Task Pointing_the_line_at_a_job_resequences_its_queue()
    {
        using var f = new Factory();
        var c = Client(f);

        // Seeded: 1001 running (status 1), 1002 queued (0). Point the line at 1002.
        var resp = await c.PostAsJsonAsync("/api/das/lines/110/current-job", new { abJobNum = 1002 });
        resp.EnsureSuccessStatusCode();
        var board = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1002, board.GetProperty("abJobNum").GetInt64());

        var queue = await c.GetFromJsonAsync<JsonElement>("/api/das/lines/110/queue");
        var byJob = queue.EnumerateArray().ToDictionary(r => r.GetProperty("abJobNum").GetInt64(), r => r.GetProperty("status").GetInt32());
        Assert.Equal(1, byJob[1002]);   // Running
        Assert.Equal(2, byJob[1001]);   // back to Waiting — displaced, not finished
        // Pointing at a job does not re-sequence the schedule: it stays in priority order.
        Assert.Equal(1001, queue.EnumerateArray().First().GetProperty("abJobNum").GetInt64());
    }

    [Fact]
    public async Task Clearing_the_job_leaves_the_queue_alone()
    {
        using var f = new Factory();
        var c = Client(f);

        var resp = await c.PostAsJsonAsync("/api/das/lines/110/current-job", new { abJobNum = (long?)null });
        resp.EnsureSuccessStatusCode();
        var board = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, board.GetProperty("abJobNum").ValueKind);

        // Legacy only nulls the board's job on the clear branch — the queue is untouched.
        var queue = await c.GetFromJsonAsync<JsonElement>("/api/das/lines/110/queue");
        var byJob = queue.EnumerateArray().ToDictionary(r => r.GetProperty("abJobNum").GetInt64(), r => r.GetProperty("status").GetInt32());
        Assert.Equal(1, byJob[1001]);
    }

    [Fact]
    public async Task Loading_a_coil_marks_it_on_line_and_dropping_clears_the_rate()
    {
        using var f = new Factory();
        var c = Client(f);

        var load = await c.PostAsJsonAsync("/api/das/lines/120/current-coil", new { coilAbcNum = 5003 });
        load.EnsureSuccessStatusCode();
        var board = await load.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5003, board.GetProperty("coilAbcNum").GetInt64());
        Assert.Equal(0, board.GetProperty("coilProcessRate").GetInt32());
        // The coil carries the spec through the join, so the operator sees what is on the mandrel.
        Assert.False(string.IsNullOrWhiteSpace(board.GetProperty("coilOrgNum").GetString()));

        var drop = await c.PostAsJsonAsync("/api/das/lines/120/current-coil", new { coilAbcNum = (long?)null });
        drop.EnsureSuccessStatusCode();
        var after = await drop.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, after.GetProperty("coilAbcNum").ValueKind);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("coilProcessRate").ValueKind);
    }

    [Fact]
    public async Task Unknown_job_or_coil_is_rejected_and_an_unknown_line_404s()
    {
        using var f = new Factory();
        var c = Client(f);

        var badJob = await c.PostAsJsonAsync("/api/das/lines/110/current-job", new { abJobNum = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, badJob.StatusCode);

        var badCoil = await c.PostAsJsonAsync("/api/das/lines/110/current-coil", new { coilAbcNum = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, badCoil.StatusCode);

        var badLine = await c.PostAsJsonAsync("/api/das/lines/999/current-job", new { abJobNum = 1001 });
        Assert.Equal(HttpStatusCode.NotFound, badLine.StatusCode);
    }

    [Fact]
    public async Task Ending_the_shift_stamps_its_downtime_total_and_clears_the_board()
    {
        using var f = new Factory();
        var c = Client(f);

        var end = await c.PostAsJsonAsync("/api/das/lines/110/shift/end", new { });
        end.EnsureSuccessStatusCode();
        var closed = await end.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7701, closed.GetProperty("shiftNum").GetInt64());
        // Shift 7701 carries two downtime instances — 20 min (9101) + 5 min (9103) = 1500 s
        // (legacy stores dt_total in SECONDS: the Oracle day-difference times 86400).
        Assert.Equal(1500, closed.GetProperty("dtTotalSeconds").GetInt64());
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("board").GetProperty("shiftNum").ValueKind);

        var shift = await c.GetFromJsonAsync<JsonElement>("/api/shifts/7701");
        Assert.Equal(1500, shift.GetProperty("dtTotal").GetDecimal());

        // A second end has nothing to close.
        var again = await c.PostAsJsonAsync("/api/das/lines/110/shift/end", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Starting_a_shift_binds_it_and_refuses_another_lines_shift()
    {
        using var f = new Factory();
        var c = Client(f);

        // Shift 7702 is scheduled on line 120 — binding it to 110 would corrupt both boards.
        var wrongLine = await c.PostAsJsonAsync("/api/das/lines/110/shift/start", new { shiftNum = 7702 });
        Assert.Equal(HttpStatusCode.Conflict, wrongLine.StatusCode);

        var ok = await c.PostAsJsonAsync("/api/das/lines/120/shift/start", new { shiftNum = 7702 });
        ok.EnsureSuccessStatusCode();
        var board = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7702, board.GetProperty("shiftNum").GetInt64());
        Assert.Equal("RM", board.GetProperty("shiftOperatorInitial").GetString());

        var unknown = await c.PostAsJsonAsync("/api/das/lines/120/shift/start", new { shiftNum = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task A_coil_run_opens_on_the_shift_and_closes_with_its_processed_weight()
    {
        using var f = new Factory();
        var c = Client(f);

        // Coil 5003 (balance 9000) onto line 110's open shift 7701, job 1001.
        var start = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start", new { coilAbcNum = 5003, abJobNum = 1001 });
        start.EnsureSuccessStatusCode();
        var opened = await start.Content.ReadFromJsonAsync<JsonElement>();
        var run = opened.GetProperty("run");
        Assert.Equal(7701, run.GetProperty("shiftNum").GetInt64());
        Assert.Equal(3, run.GetProperty("coilRunNum").GetInt32());          // runs 1 + 2 are seeded
        Assert.Equal(9000m, run.GetProperty("coilBeginWt").GetDecimal());   // defaulted from the coil's balance
        Assert.Equal(JsonValueKind.Null, run.GetProperty("coilEndTime").ValueKind);
        // Starting a run also puts the coil on the board.
        Assert.Equal(5003, opened.GetProperty("board").GetProperty("coilAbcNum").GetInt64());

        // Run it down to 2500 lb left.
        var end = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end", new { endWeight = 2500, endStatus = 2, note = "ran out" });
        end.EnsureSuccessStatusCode();
        var closed = await end.Content.ReadFromJsonAsync<JsonElement>();
        var done = closed.GetProperty("run");
        Assert.Equal(2500m, done.GetProperty("coilEndWt").GetDecimal());
        Assert.Equal(6500m, done.GetProperty("processWt").GetDecimal());    // 9000 - 2500
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("coilEndTime").ValueKind);
        // The coil comes off the mandrel and carries the new balance.
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("board").GetProperty("coilAbcNum").ValueKind);
        var coil = await c.GetFromJsonAsync<JsonElement>("/api/coils/5003");
        Assert.Equal(2500m, coil.GetProperty("netWtBalance").GetDecimal());

        var runs = await c.GetFromJsonAsync<JsonElement>("/api/das/shifts/7701/coil-runs");
        Assert.Equal(3, runs.EnumerateArray().Count());
    }

    [Fact]
    public async Task Starting_the_same_coil_run_twice_does_not_open_a_second_run()
    {
        using var f = new Factory();
        var c = Client(f);

        var first = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start", new { coilAbcNum = 5003, abJobNum = 1001 });
        first.EnsureSuccessStatusCode();
        var again = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start", new { coilAbcNum = 5003, abJobNum = 1001 });
        again.EnsureSuccessStatusCode();
        var run = (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run");
        Assert.Equal(3, run.GetProperty("coilRunNum").GetInt32());

        var runs = await c.GetFromJsonAsync<JsonElement>("/api/das/shifts/7701/coil-runs");
        Assert.Equal(3, runs.EnumerateArray().Count());   // still 3, not 4
    }

    [Fact]
    public async Task Spending_every_coil_on_a_job_finishes_it()
    {
        using var f = new Factory();
        var c = Client(f);

        // Job 1001 carries coils 5001 + 5002. Run each to zero.
        foreach (var coil in new[] { 5001, 5002 })
        {
            (await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start", new { coilAbcNum = coil, abJobNum = 1001 })).EnsureSuccessStatusCode();
            var end = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end", new { endWeight = 0, endStatus = 2, coilAbcNum = coil, abJobNum = 1001 });
            end.EnsureSuccessStatusCode();
            var body = await end.Content.ReadFromJsonAsync<JsonElement>();
            // Only the LAST coil finishes the job — until then one still has weight.
            Assert.Equal(coil == 5002, body.GetProperty("jobFinished").GetBoolean());
        }

        var job = await c.GetFromJsonAsync<JsonElement>("/api/jobs/1001");
        Assert.NotEqual(JsonValueKind.Null, job.GetProperty("timeDateFinished").ValueKind);
        // Ended (status 0) drops off the schedule the DAS shows…
        var queue = await c.GetFromJsonAsync<JsonElement>("/api/das/lines/110/queue");
        Assert.DoesNotContain(queue.EnumerateArray(), r => r.GetProperty("abJobNum").GetInt64() == 1001);
        // …but is still there with the history flag on.
        var full = await c.GetFromJsonAsync<JsonElement>("/api/das/lines/110/queue?includeEnded=true");
        Assert.Equal(0, full.EnumerateArray().First(r => r.GetProperty("abJobNum").GetInt64() == 1001).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_open_run_is_closed_at_shift_end_and_reopened_on_the_next_shift()
    {
        using var f = new Factory();
        var c = Client(f);

        // Coil 5003 (9000 lb) goes on, then the shift ends with the coil still on the mandrel.
        (await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start", new { coilAbcNum = 5003, abJobNum = 1001 })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/das/lines/110/shift/end", new { })).EnsureSuccessStatusCode();

        var runs = await c.GetFromJsonAsync<JsonElement>("/api/das/shifts/7701/coil-runs");
        var carried = runs.EnumerateArray().Single(r => r.GetProperty("coilRunNum").GetInt32() == 3);
        Assert.NotEqual(JsonValueKind.Null, carried.GetProperty("coilEndTime").ValueKind);  // closed by the shift end
        Assert.Equal(0m, carried.GetProperty("processWt").GetDecimal());                    // nothing run off it yet
        // The seeded closed runs are left exactly as they were.
        Assert.Equal(5000m, runs.EnumerateArray().Single(r => r.GetProperty("coilRunNum").GetInt32() == 1).GetProperty("processWt").GetDecimal());

        // A new shift on the same line picks the coil back up with a fresh run at its current weight.
        var newShift = await c.PostAsJsonAsync("/api/shifts", new { lineNum = 110, scheduleType = 2, startTime = "2026-01-03T00:00:00" });
        newShift.EnsureSuccessStatusCode();
        var shiftNum = (await newShift.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shiftNum").GetInt64();
        (await c.PostAsJsonAsync($"/api/das/lines/110/shift/start", new { shiftNum })).EnsureSuccessStatusCode();

        var carriedRuns = await c.GetFromJsonAsync<JsonElement>($"/api/das/shifts/{shiftNum}/coil-runs");
        var fresh = carriedRuns.EnumerateArray().Single();
        Assert.Equal(5003, fresh.GetProperty("coilAbcNum").GetInt64());
        Assert.Equal(1, fresh.GetProperty("coilRunNum").GetInt32());        // run numbers are per shift
        Assert.Equal(9000m, fresh.GetProperty("coilBeginWt").GetDecimal()); // begins at what is left on the coil
        Assert.Equal(JsonValueKind.Null, fresh.GetProperty("coilEndTime").ValueKind);
    }

    [Fact]
    public async Task A_coil_run_needs_an_open_shift_and_an_end_needs_a_run()
    {
        using var f = new Factory();
        var c = Client(f);

        // Line 120 is between shifts — the ledger has nowhere to hang a run.
        var noShift = await c.PostAsJsonAsync("/api/das/lines/120/coil-run/start", new { coilAbcNum = 5003, abJobNum = 1003 });
        Assert.Equal(HttpStatusCode.Conflict, noShift.StatusCode);

        // Line 110 has a shift but no run for coil 5003 yet.
        var noRun = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end", new { endWeight = 100, coilAbcNum = 5003, abJobNum = 1001 });
        Assert.Equal(HttpStatusCode.Conflict, noRun.StatusCode);

        var noWeight = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end", new { endStatus = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, noWeight.StatusCode);
    }

    [Fact]
    public async Task Queue_jobs_can_be_added_edited_and_removed()
    {
        using var f = new Factory();
        var c = Client(f);

        // Job 1003 is not on line 110's queue yet — a new row lands at the end, Waiting.
        var add = await c.PutAsJsonAsync("/api/das/lines/110/queue/1003", new { note = "rush" });
        add.EnsureSuccessStatusCode();
        var row = await add.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, row.GetProperty("priorityNum").GetInt32());
        Assert.Equal(2, row.GetProperty("status").GetInt32());
        Assert.Equal("rush", row.GetProperty("note").GetString());

        // An omitted field keeps its current value (the grid edits one cell at a time).
        var edit = await c.PutAsJsonAsync("/api/das/lines/110/queue/1003", new { coilRequired = 1 });
        edit.EnsureSuccessStatusCode();
        var edited = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rush", edited.GetProperty("note").GetString());
        Assert.Equal(1, edited.GetProperty("coilRequired").GetInt32());

        var remove = await c.DeleteAsync("/api/das/lines/110/queue/1003");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/das/lines/110/queue/1003")).StatusCode);

        // The job the line is RUNNING cannot be pulled out from under the board.
        var running = await c.DeleteAsync("/api/das/lines/110/queue/1001");
        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);

        var unknownJob = await c.PutAsJsonAsync("/api/das/lines/110/queue/999999", new { });
        Assert.Equal(HttpStatusCode.BadRequest, unknownJob.StatusCode);
    }

    [Fact]
    public async Task Reordering_the_queue_numbers_the_listed_jobs_first()
    {
        using var f = new Factory();
        var c = Client(f);

        (await c.PutAsJsonAsync("/api/das/lines/110/queue/1003", new { note = "third" })).EnsureSuccessStatusCode();

        // Name only 1003 — it takes priority 1, and the jobs left out follow in their existing order.
        var resp = await c.PostAsJsonAsync("/api/das/lines/110/queue/reorder", new { abJobNums = new[] { 1003 } });
        resp.EnsureSuccessStatusCode();
        var queue = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var byJob = queue.EnumerateArray().ToDictionary(r => r.GetProperty("abJobNum").GetInt64(), r => r.GetProperty("priorityNum").GetInt32());
        Assert.Equal(1, byJob[1003]);
        Assert.Equal(2, byJob[1001]);   // was priority 1
        Assert.Equal(3, byJob[1002]);   // was priority 2

        var empty = await c.PostAsJsonAsync("/api/das/lines/110/queue/reorder", new { abJobNums = Array.Empty<long>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Board_filters_by_line_and_404s_for_a_line_with_no_board_row()
    {
        using var f = new Factory();
        var c = Client(f);

        var filtered = await c.GetFromJsonAsync<JsonElement>("/api/das/line-board?lineNum=110");
        Assert.Single(filtered.EnumerateArray());
        Assert.Equal(110, filtered.EnumerateArray().First().GetProperty("lineNum").GetInt64());

        var missing = await c.GetAsync("/api/das/line-board/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
