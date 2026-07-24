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
        Assert.Equal(1, byJob[1002]);   // now running
        Assert.Equal(2, byJob[1001]);   // dropped to "ran"
        // The running job sorts first whatever its priority number.
        Assert.Equal(1002, queue.EnumerateArray().First().GetProperty("abJobNum").GetInt64());
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
