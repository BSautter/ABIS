// dasvalidate — exercises the DAS write lifecycle against the non-prod Oracle (.230).
//
// WHY THIS IS A .NET TOOL AND NOT A POWERSHELL SCRIPT
// It was a .ps1 first. That could not run: this plant's boxes have Windows PowerShell 5.1, which is
// .NET Framework, and the managed ODP.NET the repo uses is a .NET 8 assembly — Add-Type refuses it
// ("Unable to load one or more of the requested types"). The SQL half of the script was therefore
// dead on arrival, and a `catch [Microsoft.PowerShell.Commands.HttpResponseException]` in it was a
// PowerShell 7 type that throws TypeNotFound on 5.1 the moment an expected 409 arrives. A .NET 8
// console tool runs wherever the rest of tools/ runs, with no shell-version trap.
//
// WHAT IT IS FOR
// The 22 xUnit DAS tests run on SQLite. The failures that matter for these paths are the ones SQLite
// structurally cannot show: ORA-02289 (missing id sequence), ORA-01745 (reserved-word bind),
// ORA-01861 (DATE bound as a string), ORA-01400 (NOT NULL), ORA-00932 (CHAR-null COALESCE), and
// ORA-00001 when a sequence sits behind its table max after a Data Pump refresh.
//
// SAFETY
//  * Refuses any host but 192.168.1.230. .9 is live production and .11 is dev/EDI; both are read-only
//    by policy and this tool writes. Checked before a connection is opened.
//  * Works inside a scope it creates: its own shift, on a line with NO shift open, against a FINISHED
//    job's coil. It refuses to run rather than commandeer a line the plant started.
//  * Snapshots LINE_CURRENT_STATUS and the coil's status/balance columns up front and restores them
//    in a finally, on success or failure. The board is what the floor reads.
//  * Deletes every row it creates, and prints the SQL for anything it could not.
//  * Fires nothing downstream — no EDI, no scheduled job.
//
// USAGE
//   dotnet run --project tools/dasvalidate -- --cs "Data Source=192.168.1.230:1521/abc11;User Id=dbo;Password=..." \
//                                             --base http://127.0.0.1:5231
//   ORA_CS="..." dotnet run --project tools/dasvalidate -- --base http://127.0.0.1:5231
//
// The API must already be running against the SAME connection string in Oracle mode. Start it with:
//   Database__Provider=Oracle Database__ConnectionString="..." Database__Seed=false \
//   ApiKeys__Enabled=false ASPNETCORE_URLS=http://127.0.0.1:5231 dotnet run --project api/src/ABIS.Api

using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Oracle.ManagedDataAccess.Client;

static string? Opt(string[] a, string name)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

var cs = Opt(args, "--cs") ?? Environment.GetEnvironmentVariable("ORA_CS");
var baseUrl = (Opt(args, "--base") ?? "http://127.0.0.1:5231").TrimEnd('/');
var keepRows = args.Contains("--keep-rows");

if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("usage: dasvalidate --cs \"<oracle connection string>\" [--base http://127.0.0.1:5231] [--keep-rows]");
    Console.Error.WriteLine("       (or set ORA_CS)");
    return 2;
}

// --- Guard: the sandbox, and nothing else ---------------------------------------------
// This tool writes. Only the non-prod database is a write sandbox.
if (!cs.Contains("192.168.1.230", StringComparison.Ordinal))
{
    Console.Error.WriteLine("REFUSED: this tool writes, and only 192.168.1.230 (non-prod) is a write sandbox.");
    Console.Error.WriteLine("         192.168.1.9 is live production and 192.168.1.11 is dev/EDI - both read-only.");
    return 3;
}
foreach (var forbidden in new[] { "192.168.1.9;", "192.168.1.9:", "192.168.1.11" })
    if (cs.Contains(forbidden, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"REFUSED: the connection string also names {forbidden.TrimEnd(':', ';')}, which is read-only.");
        return 3;
    }

var failures = new List<string>();
void Step(string m) { Console.WriteLine(); Console.WriteLine($"=== {m} ==="); }
void Ok(string m)   { Console.WriteLine($"  PASS  {m}"); }
void Bad(string m)  { Console.WriteLine($"  FAIL  {m}"); failures.Add(m); }

// --- Oracle helpers --------------------------------------------------------------------
DataTable Query(string sql)
{
    using var conn = new OracleConnection(cs);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    var t = new DataTable();
    new OracleDataAdapter(cmd).Fill(t);
    return t;
}
object? Scalar(string sql)
{
    var t = Query(sql);
    if (t.Rows.Count == 0) return null;
    var v = t.Rows[0][0];
    return v == DBNull.Value ? null : v;
}
decimal? Dec(string sql) => Scalar(sql) is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null;
long Count(string sql) => Convert.ToInt64(Scalar(sql) ?? 0L, CultureInfo.InvariantCulture);
int Exec(string sql)
{
    using var conn = new OracleConnection(cs);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    return cmd.ExecuteNonQuery();
}
static string Lit(object? v) => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture)!;

// --- HTTP helpers ----------------------------------------------------------------------
using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

async Task<JsonElement> GetJson(string path)
{
    var r = await http.GetAsync(path);
    r.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
}
// Returns the status alongside the body, because a REFUSAL (409) is the pass condition in places.
async Task<(HttpStatusCode Code, JsonElement Body)> Post(string path, object? body)
{
    var r = body is null
        ? await http.PostAsync(path, new StringContent("", System.Text.Encoding.UTF8, "application/json"))
        : await http.PostAsJsonAsync(path, body);
    var text = await r.Content.ReadAsStringAsync();
    JsonElement el = default;
    try { el = JsonDocument.Parse(text).RootElement.Clone(); } catch { /* empty or non-JSON body */ }
    return (r.StatusCode, el);
}
async Task<JsonElement> PostOk(string path, object? body)
{
    var (code, el) = await Post(path, body);
    if ((int)code >= 300) throw new InvalidOperationException($"POST {path} -> {(int)code} {el}");
    return el;
}

var cleanup = new List<string>();
DataRow? boardSnap = null, coilSnap = null;
long lineNum = 0, coilNum = 0, jobNum = 0, testShift = 0;

try
{
    Step("Waiting for /health/ready (proves live DB connectivity)");
    var ready = false;
    for (var i = 0; i < 40 && !ready; i++)
    {
        try { ready = (await GetJson("/health/ready")).GetProperty("status").GetString() == "ready"; }
        catch { await Task.Delay(2000); }
    }
    if (!ready) throw new InvalidOperationException(
        $"The API at {baseUrl} never became ready. Start it in Oracle mode against the SAME connection string first.");
    Ok("API reachable and its DB connection is live");

    // --- Pre-flight: the sequences these writes mint from -------------------------------
    // Names come from the app's own resolution, NOT the {id_column}_SEQ convention: error_evt and
    // dt_instance are both overridden in Database:Sequences, and guessing reports a healthy database
    // as broken. Startup self-heals drift; this checks the heal actually ran.
    Step("Pre-flight: the id sequences the DAS writes mint from");
    foreach (var (seq, table, col) in new[]
             {
                 ("ERROR_EVT_SEQ", "error_evt", "error_evt_id"),
                 ("DT_INSTANCE_SEQ", "dt_instance", "instance_num"),
                 ("SHIFT_NUM_SEQ", "shift", "shift_num"),
             })
    {
        // all_sequences, NOT user_sequences: .230 carries a PUBLIC synonym USER_SEQUENCES -> DBO.USER_SEQUENCES
        // with nothing behind it, so any query naming user_sequences dies with ORA-01775 (looping chain
        // of synonyms). Found by running this. The app's own self-heal is unaffected — it uses NEXTVAL
        // and a jump rather than reading the dictionary.
        if (Count($"SELECT COUNT(*) FROM all_sequences WHERE sequence_owner = USER AND sequence_name = '{seq}'") == 0)
        { Bad($"{seq} does not exist - {table} inserts will raise ORA-02289"); continue; }

        var last = Dec($"SELECT last_number FROM all_sequences WHERE sequence_owner = USER AND sequence_name = '{seq}'") ?? 0;
        var max = Dec($"SELECT COALESCE(MAX({col}), 0) FROM {table}") ?? 0;
        if (last <= max) Bad($"{seq} is at {last} but {table}.{col} max is {max} - the next insert collides (ORA-00001)");
        else Ok($"{seq} is ahead of {table} max ({last} > {max})");
    }

    // --- Discovery: an idle line and a job/coil that can legally run --------------------
    Step("Discovery (read-only): an idle line, and a runnable job/coil");
    var lines = await GetJson("/api/das/line-board");
    var idle = lines.EnumerateArray().FirstOrDefault(l =>
        !l.TryGetProperty("shiftNum", out var s) || s.ValueKind == JsonValueKind.Null);
    if (idle.ValueKind == JsonValueKind.Undefined)
        throw new InvalidOperationException(
            "Every line has a shift open. This tool will not commandeer a line the plant started - " +
            "end a sandbox shift first, or free a line you know is idle.");
    lineNum = idle.GetProperty("lineNum").GetInt64();
    Console.WriteLine($"  line_num={lineNum}");

    // shift_coil FKs (coil, job) to process_coil, so the pair must already exist or the insert raises
    // ORA-02291 and proves nothing about the DAS path. A FINISHED job is chosen so nothing live
    // depends on the rows this touches.
    var pair = Query("""
        SELECT * FROM (
          SELECT pc.ab_job_num, pc.coil_abc_num
            FROM process_coil pc
            JOIN ab_job j ON j.ab_job_num = pc.ab_job_num
            JOIN coil   c ON c.coil_abc_num = pc.coil_abc_num
           WHERE j.time_date_finished IS NOT NULL
             AND c.net_wt_balance > 0
           ORDER BY pc.ab_job_num DESC
        ) WHERE ROWNUM = 1
        """);
    if (pair.Rows.Count == 0)
        throw new InvalidOperationException("No finished job with a weighted coil found - cannot exercise the run ledger safely.");
    jobNum = Convert.ToInt64(pair.Rows[0]["AB_JOB_NUM"], CultureInfo.InvariantCulture);
    coilNum = Convert.ToInt64(pair.Rows[0]["COIL_ABC_NUM"], CultureInfo.InvariantCulture);
    Console.WriteLine($"  ab_job_num={jobNum}  coil_abc_num={coilNum}  (finished job - nothing live depends on it)");

    // --- Snapshot BEFORE anything is written --------------------------------------------
    Step($"Snapshotting LINE_CURRENT_STATUS (line {lineNum}) and coil {coilNum}");
    var bs = Query($"SELECT shift_num, ab_job_num, coil_abc_num, coil_process_rate FROM line_current_status WHERE line_num = {lineNum}");
    boardSnap = bs.Rows.Count > 0 ? bs.Rows[0] : null;
    var cse = Query($"SELECT coil_status, coil_status_from_line, net_wt_balance, net_wt_balance_from_line FROM coil WHERE coil_abc_num = {coilNum}");
    coilSnap = cse.Rows.Count > 0 ? cse.Rows[0] : null;
    Ok("snapshot taken (restored in the finally block, pass or fail)");

    // --- 1. Shift start ------------------------------------------------------------------
    Step($"1. POST /api/shifts, then /das/lines/{lineNum}/shift/start");
    var shift = await PostOk("/api/shifts", new
    {
        startTime = DateTime.Now.ToString("s"), lineNum, operatorInitial = "ZZ", note = "ZZ_DAS_VALIDATION",
    });
    testShift = shift.GetProperty("shiftNum").GetInt64();
    cleanup.Add($"DELETE FROM shift WHERE shift_num = {testShift}");
    Console.WriteLine($"  shift_num={testShift}");

    var board = await PostOk($"/api/das/lines/{lineNum}/shift/start", new { shiftNum = testShift });
    if (board.GetProperty("shiftNum").GetInt64() == testShift) Ok("shift bound to the board");
    else Bad($"board shift_num={board.GetProperty("shiftNum")}, expected {testShift}");

    // --- 2. Coil-run start, and its idempotence -------------------------------------------
    Step($"2. coil-run/start, twice (the second must NOT open a duplicate run)");
    await PostOk($"/api/das/lines/{lineNum}/coil-run/start", new { coilAbcNum = coilNum, abJobNum = jobNum });
    cleanup.Add($"DELETE FROM shift_coil WHERE shift_num = {testShift}");
    var runs = Count($"SELECT COUNT(*) FROM shift_coil WHERE shift_num = {testShift}");
    if (runs == 1) Ok("run opened (shift_coil rows = 1)"); else Bad($"expected 1 shift_coil row, found {runs}");

    await PostOk($"/api/das/lines/{lineNum}/coil-run/start", new { coilAbcNum = coilNum, abJobNum = jobNum });
    runs = Count($"SELECT COUNT(*) FROM shift_coil WHERE shift_num = {testShift}");
    if (runs == 1) Ok("idempotent - still 1 row after a second start"); else Bad($"a second start opened a duplicate run (rows = {runs})");

    var beginWt = Dec($"SELECT coil_begin_wt FROM shift_coil WHERE shift_num = {testShift}") ?? 0;
    Console.WriteLine($"  coil_begin_wt={beginWt}");

    // --- 3. Reverse refuses a run that has produced ---------------------------------------
    Step("3. coil-run/reverse must refuse once the run has weight against it");
    Exec($"UPDATE shift_coil SET process_wt = 100 WHERE shift_num = {testShift}");
    var (revCode, _) = await Post($"/api/das/lines/{lineNum}/coil-run/reverse", new { errorTypeId = 1, note = "ZZ_DAS_VALIDATION" });
    if (revCode == HttpStatusCode.Conflict) Ok("refused with 409 (run has produced)");
    else Bad($"expected 409, got {(int)revCode} - a produced run was reversible");
    Exec($"UPDATE shift_coil SET process_wt = NULL WHERE shift_num = {testShift}");

    // --- 4. Coil-run end -------------------------------------------------------------------
    Step("4. coil-run/end: process_wt, the coil roll-through, and the board");
    var endWt = Math.Max(0m, beginWt - 500m);
    await PostOk($"/api/das/lines/{lineNum}/coil-run/end",
        new { coilAbcNum = coilNum, abJobNum = jobNum, endWeight = endWt, endStatus = 2, note = "ZZ_DAS_VALIDATION" });

    var pw = Dec($"SELECT process_wt FROM shift_coil WHERE shift_num = {testShift}") ?? -1;
    if (pw == beginWt - endWt) Ok($"process_wt = begin - end ({pw})"); else Bad($"process_wt={pw}, expected {beginWt - endWt}");

    var bal = Dec($"SELECT net_wt_balance_from_line FROM coil WHERE coil_abc_num = {coilNum}");
    if (bal == endWt) Ok($"coil balance rolled through ({bal})"); else Bad($"net_wt_balance_from_line={bal}, expected {endWt}");

    if (Count($"SELECT COUNT(*) FROM line_current_status WHERE line_num = {lineNum} AND coil_abc_num IS NOT NULL") == 0)
        Ok("coil taken off the board");
    else Bad("coil still on the board after its run ended");

    // --- 5. The flooring, on a real Oracle NUMBER -------------------------------------------
    Step("5. A re-weigh heavier than the start records no pass, never a negative one");
    await PostOk($"/api/das/lines/{lineNum}/coil-run/start", new { coilAbcNum = coilNum, abJobNum = jobNum });
    var b2 = Dec($"SELECT coil_begin_wt FROM shift_coil WHERE shift_num = {testShift} AND coil_abc_num = {coilNum}") ?? 0;
    await PostOk($"/api/das/lines/{lineNum}/coil-run/end",
        new { coilAbcNum = coilNum, abJobNum = jobNum, endWeight = b2 + 250m, endStatus = 2 });
    var pw2 = Dec($"SELECT process_wt FROM shift_coil WHERE shift_num = {testShift} AND coil_abc_num = {coilNum}") ?? -1;
    if (pw2 >= 0) Ok($"process_wt floored at {pw2}"); else Bad($"process_wt went negative: {pw2}");

    // --- 6. Board writes ---------------------------------------------------------------------
    Step("6. current-job / current-coil");
    await PostOk($"/api/das/lines/{lineNum}/current-job", new { abJobNum = jobNum });
    var running = Count($"SELECT COUNT(*) FROM line_priority WHERE line_num = {lineNum} AND status = 1");
    if (running <= 1) Ok($"at most one job reads as Running ({running})");
    else Bad($"{running} jobs read as Running on line {lineNum}");

    await PostOk($"/api/das/lines/{lineNum}/current-coil", new { coilAbcNum = coilNum });
    var rate = Dec($"SELECT coil_process_rate FROM line_current_status WHERE line_num = {lineNum}");
    if (rate == 0) Ok("loading zeroed the process rate"); else Bad($"coil_process_rate={rate} after loading, expected 0");

    await PostOk($"/api/das/lines/{lineNum}/current-coil", new { coilAbcNum = (long?)null });
    if (Scalar($"SELECT coil_status_from_line FROM coil WHERE coil_abc_num = {coilNum}") is { } fromLine)
        Ok($"dropping kept coil_status_from_line ({fromLine}) - the coil has been on a line");
    else Bad("dropping the coil reset coil_status_from_line - history erased");

    // --- 7. Shift end -------------------------------------------------------------------------
    Step($"7. shift/end: dt_total in SECONDS, stamped on the plant clock");
    var dt = await PostOk("/api/downtime", new
    {
        abJobNum = jobNum, lineNum, shiftNum = testShift,
        startingTime = DateTime.Now.AddMinutes(-30).ToString("s"),
        endingTime = DateTime.Now.AddMinutes(-15).ToString("s"),
        note = "ZZ_DAS_VALIDATION",
    });
    cleanup.Add($"DELETE FROM dt_instance WHERE instance_num = {dt.GetProperty("instanceNum").GetInt64()}");

    await PostOk($"/api/das/lines/{lineNum}/shift/end", null);

    var total = Dec($"SELECT dt_total FROM shift WHERE shift_num = {testShift}");
    if (total == 900) Ok("dt_total = 900 (15 minutes in SECONDS)"); else Bad($"dt_total={total}, expected 900 seconds");

    if (Scalar($"SELECT end_time FROM shift WHERE shift_num = {testShift}") is DateTime stamped)
    {
        var skew = Math.Abs((DateTime.Now - stamped).TotalMinutes);
        if (skew < 5) Ok($"end_time on the plant clock (skew {skew:F1} min)");
        else Bad($"end_time={stamped:s} against a local now of {DateTime.Now:s} - looks like UtcNow");
    }
    else Bad("shift end_time was not stamped");

    var open = Count($"SELECT COUNT(*) FROM shift_coil WHERE shift_num = {testShift} AND coil_end_time IS NULL");
    if (open == 0) Ok("no run left open (the cross-shift carry closed them)"); else Bad($"{open} run(s) still open after the shift ended");
}
catch (Exception ex)
{
    Bad($"ABORTED: {ex.Message}");
}
finally
{
    Step("Restoring the board and removing what this run created");
    if (keepRows)
    {
        Console.WriteLine("  --keep-rows: nothing removed. To undo:");
        foreach (var c in cleanup) Console.WriteLine($"    {c};");
    }
    else
    {
        // The board first: it is what the floor reads, and it goes back even if a step threw.
        if (boardSnap is not null)
            try
            {
                Exec($"UPDATE line_current_status SET shift_num = {Lit(boardSnap["SHIFT_NUM"])}, " +
                     $"ab_job_num = {Lit(boardSnap["AB_JOB_NUM"])}, coil_abc_num = {Lit(boardSnap["COIL_ABC_NUM"])}, " +
                     $"coil_process_rate = {Lit(boardSnap["COIL_PROCESS_RATE"])} WHERE line_num = {lineNum}");
                Ok("line_current_status restored");
            }
            catch (Exception ex) { Bad($"could not restore the board: {ex.Message}"); }

        if (coilSnap is not null)
            try
            {
                Exec($"UPDATE coil SET coil_status = {Lit(coilSnap["COIL_STATUS"])}, " +
                     $"coil_status_from_line = {Lit(coilSnap["COIL_STATUS_FROM_LINE"])}, " +
                     $"net_wt_balance = {Lit(coilSnap["NET_WT_BALANCE"])}, " +
                     $"net_wt_balance_from_line = {Lit(coilSnap["NET_WT_BALANCE_FROM_LINE"])} " +
                     $"WHERE coil_abc_num = {coilNum}");
                Ok($"coil {coilNum} restored");
            }
            catch (Exception ex) { Bad($"could not restore coil {coilNum}: {ex.Message}"); }

        // Then the rows, children before parents.
        foreach (var sql in Enumerable.Reverse(cleanup))
            try { Exec(sql); } catch { Console.WriteLine($"  LEFT BEHIND, run by hand: {sql};"); }

        try { Exec("DELETE FROM error_evt WHERE error_comment LIKE 'ZZ_DAS_VALIDATION%'"); } catch { /* column may differ */ }
        Ok("cleanup complete");
    }

    Step("Result");
    if (failures.Count == 0)
        Console.WriteLine("  All DAS write checks passed against live Oracle.");
    else
    {
        Console.WriteLine($"  {failures.Count} check(s) FAILED:");
        foreach (var f in failures) Console.WriteLine($"    - {f}");
        Console.WriteLine();
        Console.WriteLine("  Each is a live-only defect the SQLite suite cannot see. Report them back.");
    }
}

return failures.Count == 0 ? 0 : 1;
