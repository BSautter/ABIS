using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The DAS station's writes — the shop floor's own transactions.
///
/// <para><b>Why these exist.</b> Every DAS write path had zero test coverage: shift start/end, coil
/// run start/end, change-job, reverse, current-job, current-coil, queue. The line board had read tests
/// and nothing exercised the mutations that fill it. These are also the writes that are still
/// unvalidated against live Oracle, so a test is the only thing standing behind them.</para>
///
/// <para><b>What is actually being protected.</b> Not "the UPDATE runs" — the rules ported out of
/// legacy that a reasonable-looking refactor would quietly drop. Each test names the legacy behaviour
/// it holds: the cross-shift carry that stops a coil spanning midnight being credited to one shift,
/// the flooring that stops a re-weigh recording a negative pass, the NULL that means "never run"
/// rather than "spent", the plant-local clock the uptime report subtracts against. Get any of those
/// wrong and the numbers stay plausible — which is what makes them expensive.</para>
///
/// <para>Seeded state these lean on: line 110 is running (shift 7701, job 1001, coil 5001); its runs
/// 1 and 2 are closed; job 1001 carries coils 5001 and 5002 in <c>process_coil</c>. Line 120 is idle
/// with no shift.</para>
/// </summary>
public sealed class DasWriteLifecycleTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_daswrite_{Guid.NewGuid():N}.db");
    private string Cs => $"Data Source={_dbPath}";

    private sealed class Factory(string cs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "Sqlite");
            b.UseSetting("Database:ConnectionString", cs);
            b.UseSetting("Database:Seed", "true");
            b.UseSetting("ApiKeys:Enabled", "true");
            b.UseSetting("ApiKeys:Keys:0", "test-key");
        }
    }

    private HttpClient Client(Factory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection(Cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql)
    {
        using var c = new SqliteConnection(Cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? default! : (T)Convert.ChangeType(v, typeof(T));
    }

    /// <summary>Re-open run 1 of shift 7701 (coil 5001, job 1001). The fixture closes both its runs;
    /// an open run is what the line is still processing, and it is what the carry paths reach.</summary>
    private void ReopenRun1() => Exec(
        "UPDATE shift_coil SET coil_end_time = NULL, coil_end_wt = NULL, process_wt = NULL " +
        "WHERE shift_num = 7701 AND coil_run_num = 1");

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // ---- Coil-run ledger -------------------------------------------------------------

    [Fact]
    public async Task Starting_a_run_that_already_exists_returns_it_instead_of_opening_a_second()
    {
        // Legacy u_coil.init inserts only when there is no row for this (shift, job, coil). A coil that
        // comes back to the same job in the same shift resumes its run; opening a second would double
        // the shift's production, because both rows carry the coil's weight.
        using var f = new Factory(Cs);
        var c = Client(f);

        var before = Scalar<long>("SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701");
        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start",
            new { coilAbcNum = 5002, abJobNum = 1001 });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(before, Scalar<long>("SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701"));
        // …and it is the ORIGINAL run, not a replacement: run 2's begin weight is untouched.
        Assert.Equal(8000m, Scalar<decimal>(
            "SELECT coil_begin_wt FROM shift_coil WHERE shift_num = 7701 AND coil_abc_num = 5002"));
    }

    [Fact]
    public async Task Starting_a_run_for_a_coil_new_to_the_shift_does_open_one()
    {
        // The negative of the test above. Without this, idempotence could be "never inserts anything"
        // and both tests would still pass.
        using var f = new Factory(Cs);
        var c = Client(f);
        Exec("INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_end_wt, process_quantity) VALUES (1001, 5004, 1, 0, 0)");

        var before = Scalar<long>("SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701");
        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/start",
            new { coilAbcNum = 5004, abJobNum = 1001 });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(before + 1, Scalar<long>("SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701"));
    }

    [Fact]
    public async Task A_re_weigh_heavier_than_the_start_records_no_pass_rather_than_a_negative_one()
    {
        // process_wt = begin − end, floored at zero (legacy). Scales disagree, so an end weight above
        // the begin weight is a real occurrence; letting it through as a negative would SUBTRACT from
        // the shift's production and from the job's consumed weight.
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();   // run 1 began at 12,000 lb

        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { coilAbcNum = 5001, abJobNum = 1001, endWeight = 12500m, endStatus = 2 });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0m, Scalar<decimal>(
            "SELECT process_wt FROM shift_coil WHERE shift_num = 7701 AND coil_run_num = 1"));
    }

    [Fact]
    public async Task Ending_a_run_rolls_the_weight_through_the_coil_and_takes_it_off_the_board()
    {
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();

        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { coilAbcNum = 5001, abJobNum = 1001, endWeight = 4500m, endStatus = 2, note = "done" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // The run: begin 12,000 − end 4,500 = 7,500 consumed.
        Assert.Equal(7500m, Scalar<decimal>(
            "SELECT process_wt FROM shift_coil WHERE shift_num = 7701 AND coil_run_num = 1"));
        // The coil, both the plain and the from-line columns — legacy writes both, and the DAS board
        // reads the from-line pair.
        Assert.Equal(4500m, Scalar<decimal>("SELECT net_wt_balance FROM coil WHERE coil_abc_num = 5001"));
        Assert.Equal(4500m, Scalar<decimal>("SELECT net_wt_balance_from_line FROM coil WHERE coil_abc_num = 5001"));
        Assert.Equal(2L, Scalar<long>("SELECT coil_status_from_line FROM coil WHERE coil_abc_num = 5001"));
        // process_coil carries the job's view of the same weight.
        Assert.Equal(4500m, Scalar<decimal>(
            "SELECT current_wt FROM process_coil WHERE ab_job_num = 1001 AND coil_abc_num = 5001"));
        // …and the coil is off the mandrel.
        Assert.Equal(0L, Scalar<long>(
            "SELECT COUNT(*) FROM line_current_status WHERE line_num = 110 AND coil_abc_num IS NOT NULL"));
    }

    [Fact]
    public async Task A_coil_that_has_never_run_keeps_the_job_open()
    {
        // The job-done predicate counts coils with weight left as (current_wt IS NULL OR <> 0). NULL
        // means "never run", NOT "spent". Dropping the NULL arm — the obvious simplification — would
        // finish a job the moment its FIRST coil emptied, stamping time_date_finished on a job with
        // untouched coils still assigned and cascading it into invoicing.
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();
        // Job 1001 also carries coil 5002, whose current_wt is NULL: never run.

        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { coilAbcNum = 5001, abJobNum = 1001, endWeight = 0m, endStatus = 2 });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0L, Scalar<long>(
            "SELECT COUNT(*) FROM ab_job WHERE ab_job_num = 1001 AND time_date_finished IS NOT NULL"));
    }

    [Fact]
    public async Task The_job_finishes_once_every_coil_on_it_is_spent()
    {
        // The other half: with the job's other coil already spent, emptying this one finishes the job —
        // time_date_finished stamped and the queue entry dropped to status 0 (the legacy cascade).
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();
        Exec("UPDATE process_coil SET current_wt = 0 WHERE ab_job_num = 1001 AND coil_abc_num = 5002");

        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/end",
            new { coilAbcNum = 5001, abJobNum = 1001, endWeight = 0m, endStatus = 2 });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1L, Scalar<long>(
            "SELECT COUNT(*) FROM ab_job WHERE ab_job_num = 1001 AND time_date_finished IS NOT NULL"));
        Assert.Equal(0L, Scalar<long>(
            "SELECT status FROM line_priority WHERE line_num = 110 AND ab_job_num = 1001"));
    }

    [Fact]
    public async Task A_line_with_no_open_shift_refuses_a_coil_run_rather_than_losing_it()
    {
        // The ledger hangs off the shift; with none open there is nowhere to record the run. Refusing
        // is the point — accepting it would let a line run a coil whose weight lands in no shift's
        // production at all. Line 120 is idle in the fixture.
        using var f = new Factory(Cs);
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/das/lines/120/coil-run/start",
            new { coilAbcNum = 5003, abJobNum = 1002 });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0L, Scalar<long>("SELECT COUNT(*) FROM shift_coil WHERE coil_abc_num = 5003 AND ab_job_num = 1002"));
    }

    // ---- Shift lifecycle -------------------------------------------------------------

    [Fact]
    public async Task A_coil_still_on_the_mandrel_opens_a_fresh_run_on_the_new_shift()
    {
        // The cross-shift carry (legacy wf_new_shift). A coil that spans midnight must be split across
        // the two shifts' production, not credited whole to whichever shift happened to close it. The
        // new run begins at the weight the coil has LEFT, so each shift is credited with what it ran.
        using var f = new Factory(Cs);
        var c = Client(f);
        Exec("INSERT INTO shift (shift_num, start_time, line_num, schedule_type) VALUES (7703, '2024-01-02 05:00:00', 110, 1)");
        Exec("UPDATE coil SET net_wt_balance = 6200 WHERE coil_abc_num = 5001");

        var r = await c.PostAsJsonAsync("/api/das/lines/110/shift/start", new { shiftNum = 7703 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(1L, Scalar<long>(
            "SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7703 AND coil_abc_num = 5001 AND ab_job_num = 1001"));
        Assert.Equal(6200m, Scalar<decimal>(
            "SELECT coil_begin_wt FROM shift_coil WHERE shift_num = 7703 AND coil_abc_num = 5001"));
    }

    [Fact]
    public async Task Ending_a_shift_closes_a_run_still_open_so_its_weight_lands_in_that_shift()
    {
        // The other end of the carry (legacy of_save_at_shift_end). Leaving the run open would park the
        // weight in no shift until someone closed it by hand — and the coil stays on the mandrel, so
        // the next shift opens its own run.
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();                                                        // begins at 12,000
        Exec("UPDATE coil SET net_wt_balance = 4000 WHERE coil_abc_num = 5001");

        var r = await c.PostAsync("/api/das/lines/110/shift/end", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(4000m, Scalar<decimal>(
            "SELECT coil_end_wt FROM shift_coil WHERE shift_num = 7701 AND coil_run_num = 1"));
        Assert.Equal(8000m, Scalar<decimal>(   // 12,000 − 4,000 ran in THIS shift
            "SELECT process_wt FROM shift_coil WHERE shift_num = 7701 AND coil_run_num = 1"));
        Assert.Equal(0L, Scalar<long>(
            "SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701 AND coil_end_time IS NULL"));
    }

    [Fact]
    public async Task Shift_downtime_totals_in_seconds_not_minutes_or_days()
    {
        // dt_total is SECONDS: legacy multiplies the Oracle day-difference by 86400. The unit is
        // invisible in the column and every consumer divides by it, so a wrong one turns a 30-minute
        // stoppage into 30 minutes of something else on every uptime report.
        using var f = new Factory(Cs);
        var c = Client(f);
        Exec("DELETE FROM dt_instance WHERE shift_num = 7701");
        Exec("INSERT INTO dt_instance (instance_num, shift_num, starting_time, ending_time) " +
             "VALUES (99001, 7701, '2024-01-01 06:00:00', '2024-01-01 06:30:00')");   // 30 min
        Exec("INSERT INTO dt_instance (instance_num, shift_num, starting_time, ending_time) " +
             "VALUES (99002, 7701, '2024-01-01 07:00:00', '2024-01-01 07:15:00')");   // 15 min

        var r = await c.PostAsync("/api/das/lines/110/shift/end", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(2700L, Scalar<long>("SELECT dt_total FROM shift WHERE shift_num = 7701"));   // 45 min
    }

    [Fact]
    public async Task An_instance_that_never_ended_is_not_counted_as_downtime()
    {
        // An open stoppage has no duration yet. Treating its NULL end as "now" — or as zero-length —
        // would either inflate downtime without limit or silently swallow it.
        using var f = new Factory(Cs);
        var c = Client(f);
        Exec("DELETE FROM dt_instance WHERE shift_num = 7701");
        Exec("INSERT INTO dt_instance (instance_num, shift_num, starting_time, ending_time) " +
             "VALUES (99003, 7701, '2024-01-01 06:00:00', '2024-01-01 06:10:00')");   // 10 min
        Exec("INSERT INTO dt_instance (instance_num, shift_num, starting_time, ending_time) " +
             "VALUES (99004, 7701, '2024-01-01 08:00:00', NULL)");                    // still down

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/das/lines/110/shift/end", null)).StatusCode);
        Assert.Equal(600L, Scalar<long>("SELECT dt_total FROM shift WHERE shift_num = 7701"));
    }

    [Fact]
    public async Task The_shift_is_closed_on_the_plant_clock_not_UTC()
    {
        // start_time is written in plant time by the scheduling screen and the uptime report subtracts
        // the two. Stamping end_time from UtcNow would skew every shift's length by the plant's UTC
        // offset — five hours of phantom runtime, on a number nobody re-derives.
        using var f = new Factory(Cs);
        var c = Client(f);

        var before = DateTime.Now;
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/das/lines/110/shift/end", null)).StatusCode);
        var stamped = Scalar<DateTime>("SELECT end_time FROM shift WHERE shift_num = 7701");

        Assert.InRange(stamped, before.AddMinutes(-2), DateTime.Now.AddMinutes(2));
        // Where the runner's clock IS UTC the two are identical and nothing here can tell them apart,
        // so this half only asserts where there is an offset to detect. CI runs UTC; a developer
        // machine on plant time does not, which is where the regression would first show.
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        if (offset != TimeSpan.Zero)
            Assert.True(Math.Abs((stamped - DateTime.UtcNow).TotalMinutes) > 2,
                "end_time matched UtcNow — it must be stamped on the plant clock.");
    }

    [Fact]
    public async Task Ending_a_shift_the_line_does_not_have_is_refused()
    {
        using var f = new Factory(Cs);
        var c = Client(f);

        var r = await c.PostAsync("/api/das/lines/120/shift/end", null);   // line 120 is idle
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task A_shift_scheduled_on_another_line_cannot_be_bound_to_this_one()
    {
        // Shift 7702 belongs to line 120. Binding it to 110 would put two lines' production in one
        // shift and corrupt both boards.
        using var f = new Factory(Cs);
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/das/lines/110/shift/start", new { shiftNum = 7702 });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(7701L, Scalar<long>("SELECT shift_num FROM line_current_status WHERE line_num = 110"));
    }

    // ---- Board writes ----------------------------------------------------------------

    [Fact]
    public async Task Pointing_the_line_at_a_job_moves_the_running_flag_in_the_queue()
    {
        // LINE_PRIORITY status: 0 Ended, 1 Running, 2 Waiting. Exactly one job may read as Running, so
        // the previous holder has to be demoted in the same breath — otherwise the floor board shows
        // two jobs running on one line and the pickers cannot tell which is live.
        using var f = new Factory(Cs);
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/das/lines/110/current-job", new { abJobNum = 1002 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(1L, Scalar<long>("SELECT status FROM line_priority WHERE line_num = 110 AND ab_job_num = 1002"));
        Assert.Equal(2L, Scalar<long>("SELECT status FROM line_priority WHERE line_num = 110 AND ab_job_num = 1001"));
        Assert.Equal(1L, Scalar<long>("SELECT COUNT(*) FROM line_priority WHERE line_num = 110 AND status = 1"));
    }

    [Fact]
    public async Task Dropping_the_coil_clears_the_board_but_not_the_coils_history()
    {
        // Deliberate legacy fidelity: dropping clears the board's coil AND its process rate, but does
        // NOT reset coil_status_from_line. The coil HAS been on a line, and that fact outlives its
        // time on the mandrel — resetting it would erase the coil's own record of having run.
        using var f = new Factory(Cs);
        var c = Client(f);

        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/api/das/lines/110/current-coil", new { coilAbcNum = 5001 })).StatusCode);
        Assert.Equal(1L, Scalar<long>("SELECT coil_status_from_line FROM coil WHERE coil_abc_num = 5001"));

        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/api/das/lines/110/current-coil", new { coilAbcNum = (long?)null })).StatusCode);

        Assert.Equal(0L, Scalar<long>(
            "SELECT COUNT(*) FROM line_current_status WHERE line_num = 110 AND (coil_abc_num IS NOT NULL OR coil_process_rate IS NOT NULL)"));
        Assert.Equal(1L, Scalar<long>("SELECT coil_status_from_line FROM coil WHERE coil_abc_num = 5001"));
    }

    [Fact]
    public async Task Loading_a_coil_zeroes_the_process_rate_rather_than_keeping_the_last_coils()
    {
        // The rate is per coil. Carrying the previous coil's rate over would show the new coil running
        // at a speed it has not reached, on a board the floor reads at a glance.
        using var f = new Factory(Cs);
        var c = Client(f);
        Assert.Equal(42L, Scalar<long>("SELECT coil_process_rate FROM line_current_status WHERE line_num = 110"));

        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/api/das/lines/110/current-coil", new { coilAbcNum = 5002 })).StatusCode);

        Assert.Equal(0L, Scalar<long>("SELECT coil_process_rate FROM line_current_status WHERE line_num = 110"));
    }

    [Fact]
    public async Task Changing_job_mid_coil_splits_the_coil_between_the_two_jobs()
    {
        // The coil keeps running; the weight it has consumed so far closes on the OLD job and the
        // remainder opens on the new one. Both runs exist afterwards — that is the whole point, and it
        // is how a coil finishing one order and starting the next is billed to each correctly.
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();                                                        // 5001 on job 1001, began 12,000
        Exec("INSERT INTO process_coil (ab_job_num, coil_abc_num, process_coil_status, process_end_wt, process_quantity) VALUES (1002, 5001, 1, 0, 0)");

        var r = await c.PostAsJsonAsync("/api/das/lines/110/change-job",
            new { newJobNum = 1002, remainingWeight = 5000m, endStatus = 2 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Old job's run closed at the remaining weight: 12,000 − 5,000 = 7,000 consumed on job 1001.
        Assert.Equal(7000m, Scalar<decimal>(
            "SELECT process_wt FROM shift_coil WHERE shift_num = 7701 AND coil_abc_num = 5001 AND ab_job_num = 1001"));
        // New job's run opened at what is left.
        Assert.Equal(5000m, Scalar<decimal>(
            "SELECT coil_begin_wt FROM shift_coil WHERE shift_num = 7701 AND coil_abc_num = 5001 AND ab_job_num = 1002"));
        // The coil never left the mandrel.
        Assert.Equal(5001L, Scalar<long>("SELECT coil_abc_num FROM line_current_status WHERE line_num = 110"));
        Assert.Equal(1002L, Scalar<long>("SELECT ab_job_num FROM line_current_status WHERE line_num = 110"));
    }

    [Fact]
    public async Task Reversing_a_wrongly_loaded_coil_removes_its_run_and_logs_who_did_it()
    {
        // The operator loaded the wrong coil. The run is deleted rather than closed at zero, because a
        // zero-weight run is a coil that ran and produced nothing — a different claim entirely, and one
        // that would sit in the shift's ledger as fact.
        using var f = new Factory(Cs);
        var c = Client(f);
        ReopenRun1();

        var eventsBefore = Scalar<long>("SELECT COUNT(*) FROM error_evt");   // the fixture seeds some

        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/reverse",
            new { errorTypeId = 1, note = "wrong coil" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(0L, Scalar<long>(
            "SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701 AND coil_run_num = 1"));
        Assert.Equal(eventsBefore + 1, Scalar<long>("SELECT COUNT(*) FROM error_evt"));
        Assert.Equal(0L, Scalar<long>(
            "SELECT COUNT(*) FROM line_current_status WHERE line_num = 110 AND coil_abc_num IS NOT NULL"));
    }

    [Fact]
    public async Task A_run_that_has_already_produced_is_not_reversed()
    {
        // Reversal is for a mistake caught before anything came off the line. Once the run has weight
        // against it, deleting the row would destroy production that physically happened.
        using var f = new Factory(Cs);
        var c = Client(f);
        Exec("UPDATE shift_coil SET coil_end_time = NULL, coil_end_wt = NULL, process_wt = 3000 " +
             "WHERE shift_num = 7701 AND coil_run_num = 1");

        var r = await c.PostAsJsonAsync("/api/das/lines/110/coil-run/reverse",
            new { errorTypeId = 1, note = "too late" });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1L, Scalar<long>(
            "SELECT COUNT(*) FROM shift_coil WHERE shift_num = 7701 AND coil_run_num = 1"));
    }

    // ---- Queue ------------------------------------------------------------------------

    [Fact]
    public async Task Reordering_the_queue_puts_the_listed_jobs_first_and_keeps_the_rest()
    {
        // Jobs left out of the list must follow in their existing order rather than vanish — the floor
        // reorders the next two jobs without restating the whole queue.
        using var f = new Factory(Cs);
        var c = Client(f);

        var r = await c.PostAsJsonAsync("/api/das/lines/110/queue/reorder", new { abJobNums = new[] { 1002 } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(1L, Scalar<long>("SELECT priority_num FROM line_priority WHERE line_num = 110 AND ab_job_num = 1002"));
        Assert.Equal(2L, Scalar<long>("SELECT priority_num FROM line_priority WHERE line_num = 110 AND ab_job_num = 1001"));
    }

    [Fact]
    public async Task The_queue_will_not_drop_the_job_the_line_is_running()
    {
        // Removing the running job would leave the board pointing at a job with no queue entry.
        using var f = new Factory(Cs);
        var c = Client(f);

        var r = await c.DeleteAsync("/api/das/lines/110/queue/1001");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1L, Scalar<long>(
            "SELECT COUNT(*) FROM line_priority WHERE line_num = 110 AND ab_job_num = 1001"));
    }
}
