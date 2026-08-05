<#
.SYNOPSIS
  Live-validates the DAS write lifecycle against the non-prod Oracle (.230).

.DESCRIPTION
  The DAS writes — shift start/end, coil-run start/end, change-job, reverse, current-job,
  current-coil, queue — are covered by 22 xUnit tests, but those run on SQLite. This exercises
  the same paths against real Oracle, which is where the failures SQLite cannot show up live:
  a missing id sequence (ORA-02289), a reserved-word bind (ORA-01745), a DATE bound as a
  string (ORA-01861), a NOT NULL the fixture did not carry (ORA-01400), a CHAR-null COALESCE
  (ORA-00932), and a sequence sitting behind its table max (ORA-00001).

  SAFETY — read this before running.

  * It REFUSES any host but 192.168.1.230. .9 is live production and .11 is dev/EDI; both are
    strictly read-only and this script writes. The guard is on the connection string, checked
    before anything is opened.
  * It works inside a scope it creates: its own shift, on a line that has NO shift open, using
    a job/coil pair it discovers read-only. It never ends a shift the plant started.
  * It snapshots the line's LINE_CURRENT_STATUS row up front and restores it at the end, on
    success or failure. The board is what the floor reads; leaving it changed is not acceptable
    even on a sandbox.
  * Every row it creates is deleted in the finally block, and any it could not delete is printed
    as ready-to-run SQL.
  * It fires nothing downstream. No EDI, no scheduled job — the DAS paths do not transmit.

.PARAMETER ConnectionString
  Oracle connection string for .230. Supply your own credential; nothing is stored or logged
  (the string is never echoed).

.EXAMPLE
  ./tools/validate_das_writes.ps1 -ConnectionString "Data Source=192.168.1.230:1521/abc11;User Id=dbo;Password=YOURPW;"

.EXAMPLE
  # Against an API already running in Oracle mode:
  ./tools/validate_das_writes.ps1 -ConnectionString "..." -NoRun -BaseUrl http://127.0.0.1:5230
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [string]$BaseUrl = "http://127.0.0.1:5231",
    [switch]$NoRun,
    # Skip the restore/cleanup step to inspect what was written. Prints the SQL to undo it.
    [switch]$KeepRows
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot "api/src/ABIS.Api/ABIS.Api.csproj"

# --- Guard: the sandbox, and nothing else ------------------------------------------
# .9 (production) and .11 (dev/EDI) are read-only by policy. This script writes, so it must
# never be pointed at them by a mistyped host.
if ($ConnectionString -notmatch '192\.168\.1\.230') {
    throw "REFUSED: this script writes, and only 192.168.1.230 (non-prod) is a write sandbox. " +
          "192.168.1.9 is live production and 192.168.1.11 is dev/EDI — both are read-only."
}
foreach ($forbidden in '192\.168\.1\.9\b', '192\.168\.1\.11\b') {
    if ($ConnectionString -match $forbidden) { throw "REFUSED: connection string also names a read-only host." }
}

function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "  PASS  $m" -ForegroundColor Green }
function Bad($m)  { Write-Host "  FAIL  $m" -ForegroundColor Red; $script:failures += $m }

function Get-($p)        { Invoke-RestMethod -Method Get   -Uri "$BaseUrl$p" }
function Post($p, $b)    { Invoke-RestMethod -Method Post  -Uri "$BaseUrl$p" -ContentType 'application/json' -Body ($b | ConvertTo-Json -Depth 6) }
function Del($p)         { Invoke-RestMethod -Method Delete -Uri "$BaseUrl$p" }

# Same call, but an HTTP error is returned rather than thrown — used where a REFUSAL is the
# expected result and a 409 is the pass condition.
function TryPost($p, $b) {
    try { return @{ Code = 200; Body = (Post $p $b) } }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] { return @{ Code = [int]$_.Exception.Response.StatusCode; Body = $null } }
    catch { if ($_.Exception.Response) { return @{ Code = [int]$_.Exception.Response.StatusCode; Body = $null } } throw }
}

$script:failures = @()
$cleanup = New-Object System.Collections.ArrayList
$boardSnapshot = $null
$testShift = $null

# --- Direct SQL, for snapshot/restore and for reading back what the API wrote --------
$odp = Get-ChildItem -Path (Join-Path $repoRoot "api/src/ABIS.Api/bin") -Recurse -Filter "Oracle.ManagedDataAccess.dll" -ErrorAction SilentlyContinue |
       Select-Object -First 1
if (-not $odp) { throw "Oracle.ManagedDataAccess.dll not found — build the API once first: dotnet build $proj -c Release" }
Add-Type -Path $odp.FullName
function Sql($text, $isQuery = $true) {
    $conn = New-Object Oracle.ManagedDataAccess.Client.OracleConnection $ConnectionString
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $text
        if ($isQuery) {
            $t = New-Object System.Data.DataTable
            (New-Object Oracle.ManagedDataAccess.Client.OracleDataAdapter $cmd).Fill($t) | Out-Null
            return $t
        }
        return $cmd.ExecuteNonQuery()
    } finally { $conn.Close() }
}
function Scalar($text) { $t = Sql $text; if ($t.Rows.Count -eq 0) { return $null } return $t.Rows[0][0] }

$env:Database__Provider = "Oracle"
$env:Database__ConnectionString = $ConnectionString
$env:Database__Seed = "false"
$env:ApiKeys__Enabled = "false"
$env:Audit__Enabled = "false"
$env:ASPNETCORE_URLS = $BaseUrl
$env:ASPNETCORE_ENVIRONMENT = "Development"

$apiProc = $null
if (-not $NoRun) {
    Step "Building + launching the API in Oracle mode"
    & dotnet build $proj -c Release | Out-Host
    $apiProc = Start-Process dotnet -ArgumentList "run --no-build --project `"$proj`" -c Release" -PassThru -NoNewWindow
}

try {
    Step "Waiting for /health/ready (proves live DB connectivity)"
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { if ((Get- "/health/ready").status -eq "ready") { $ready = $true; break } } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw "API never became ready — check the listener and the connection string." }
    Ok "DB reachable"

    # --- Pre-flight: the sequences these writes mint from -----------------------------
    # A Data Pump refresh imports rows but leaves sequences behind their new max, which breaks
    # every id-minting insert with ORA-00001. Startup self-heals it (Database:ResyncSequencesOnStartup),
    # so this checks the heal actually ran rather than assuming it did.
    Step "Pre-flight: id sequences for the DAS writes"
    foreach ($pair in @(@("ERROR_EVT_ID_SEQ", "error_evt", "error_evt_id"),
                        @("INSTANCE_NUM_SEQ", "dt_instance", "instance_num"),
                        @("SHIFT_NUM_SEQ",    "shift",      "shift_num"))) {
        $seq, $table, $col = $pair
        $exists = Scalar "SELECT COUNT(*) FROM user_sequences WHERE sequence_name = '$seq'"
        if ($exists -eq 0) { Bad "$seq does not exist — $table inserts will raise ORA-02289"; continue }
        $last = Scalar "SELECT last_number FROM user_sequences WHERE sequence_name = '$seq'"
        $max  = Scalar "SELECT COALESCE(MAX($col), 0) FROM $table"
        if ([decimal]$last -le [decimal]$max) { Bad "$seq is at $last but $table.$col max is $max — next insert collides (ORA-00001)" }
        else { Ok "$seq ahead of $table max ($last > $max)" }
    }

    # --- Discovery: an idle line, and a job/coil pair that can legally run ------------
    Step "Discovery (read-only): an idle line and a runnable job/coil"
    $board = Get- "/api/das/line-board"
    $idle = $board | Where-Object { $null -eq $_.shiftNum } | Select-Object -First 1
    if ($null -eq $idle) {
        throw "Every line on .230 has a shift open. This script will not commandeer a line the " +
              "plant started — end a sandbox shift first, or point it at a line you know is free."
    }
    $lineNum = $idle.lineNum
    Write-Host "  line_num=$lineNum ($($idle.lineDesc))"

    # A coil already assigned to a job (process_coil row) — shift_coil FKs to that pair, so an
    # unassigned coil would raise ORA-02291 and prove nothing about the DAS path itself.
    $pair = Sql @"
SELECT * FROM (
  SELECT pc.ab_job_num, pc.coil_abc_num
    FROM process_coil pc
    JOIN ab_job j ON j.ab_job_num = pc.ab_job_num
    JOIN coil  c ON c.coil_abc_num = pc.coil_abc_num
   WHERE j.time_date_finished IS NOT NULL      -- a FINISHED job: nothing live depends on it
     AND c.net_wt_balance > 0
   ORDER BY pc.ab_job_num DESC
) WHERE ROWNUM = 1
"@
    if ($pair.Rows.Count -eq 0) { throw "No finished job with a weighted coil found — cannot exercise the run ledger safely." }
    $jobNum  = [long]$pair.Rows[0]["AB_JOB_NUM"]
    $coilNum = [long]$pair.Rows[0]["COIL_ABC_NUM"]
    Write-Host "  ab_job_num=$jobNum  coil_abc_num=$coilNum  (finished job — chosen so nothing live depends on it)"

    # --- Snapshot the board row BEFORE anything is written ----------------------------
    Step "Snapshotting LINE_CURRENT_STATUS for line $lineNum"
    $boardSnapshot = Sql "SELECT shift_num, ab_job_num, coil_abc_num, coil_process_rate FROM line_current_status WHERE line_num = $lineNum"
    $coilSnapshot  = Sql "SELECT coil_status, coil_status_from_line, net_wt_balance, net_wt_balance_from_line FROM coil WHERE coil_abc_num = $coilNum"
    Ok "snapshot taken (restored in the finally block, pass or fail)"

    # --- 1. Shift start ---------------------------------------------------------------
    Step "1. POST /api/shifts + /das/lines/$lineNum/shift/start"
    $s = Post "/api/shifts" @{ startTime = (Get-Date).ToString("s"); lineNum = $lineNum; operatorInitial = "ZZ"; note = "ZZ_DAS_VALIDATION" }
    $testShift = $s.shiftNum
    [void]$cleanup.Add("DELETE FROM shift WHERE shift_num = $testShift;")
    Write-Host "  shift_num=$testShift"
    $r = Post "/api/das/lines/$lineNum/shift/start" @{ shiftNum = $testShift }
    if ($r.shiftNum -eq $testShift) { Ok "shift bound to the board" } else { Bad "board shift_num=$($r.shiftNum), expected $testShift" }

    # --- 2. Coil-run start, and its idempotence ---------------------------------------
    Step "2. POST /das/lines/$lineNum/coil-run/start (twice — the second must not open a run)"
    $r = Post "/api/das/lines/$lineNum/coil-run/start" @{ coilAbcNum = $coilNum; abJobNum = $jobNum }
    [void]$cleanup.Add("DELETE FROM shift_coil WHERE shift_num = $testShift;")
    $runs = Scalar "SELECT COUNT(*) FROM shift_coil WHERE shift_num = $testShift"
    if ($runs -eq 1) { Ok "run opened (shift_coil rows = 1)" } else { Bad "expected 1 shift_coil row, found $runs" }

    $null = Post "/api/das/lines/$lineNum/coil-run/start" @{ coilAbcNum = $coilNum; abJobNum = $jobNum }
    $runs = Scalar "SELECT COUNT(*) FROM shift_coil WHERE shift_num = $testShift"
    if ($runs -eq 1) { Ok "idempotent — still 1 row after a second start" } else { Bad "second start opened a duplicate run (rows = $runs)" }

    $beginWt = Scalar "SELECT coil_begin_wt FROM shift_coil WHERE shift_num = $testShift"
    Write-Host "  coil_begin_wt=$beginWt"

    # --- 3. Reverse refuses once the run has produced ---------------------------------
    Step "3. POST /das/lines/$lineNum/coil-run/reverse (must refuse a run with weight against it)"
    $null = Sql "UPDATE shift_coil SET process_wt = 100 WHERE shift_num = $testShift" $false
    $res = TryPost "/api/das/lines/$lineNum/coil-run/reverse" @{ errorTypeId = 1; note = "ZZ_DAS_VALIDATION" }
    if ($res.Code -eq 409) { Ok "refused with 409 (run has produced)" } else { Bad "expected 409, got $($res.Code) — a produced run was reversible" }
    $null = Sql "UPDATE shift_coil SET process_wt = NULL WHERE shift_num = $testShift" $false

    # --- 4. Coil-run end: process_wt, the coil roll-through, and the flooring ----------
    Step "4. POST /das/lines/$lineNum/coil-run/end"
    $endWt = [decimal]$beginWt - 500
    if ($endWt -lt 0) { $endWt = 0 }
    $null = Post "/api/das/lines/$lineNum/coil-run/end" @{ coilAbcNum = $coilNum; abJobNum = $jobNum; endWeight = $endWt; endStatus = 2; note = "ZZ_DAS_VALIDATION" }
    $pw = Scalar "SELECT process_wt FROM shift_coil WHERE shift_num = $testShift"
    if ([decimal]$pw -eq ([decimal]$beginWt - $endWt)) { Ok "process_wt = begin - end ($pw)" } else { Bad "process_wt=$pw, expected $([decimal]$beginWt - $endWt)" }
    $bal = Scalar "SELECT net_wt_balance_from_line FROM coil WHERE coil_abc_num = $coilNum"
    if ([decimal]$bal -eq $endWt) { Ok "coil balance rolled through ($bal)" } else { Bad "net_wt_balance_from_line=$bal, expected $endWt" }
    $onBoard = Scalar "SELECT COUNT(*) FROM line_current_status WHERE line_num = $lineNum AND coil_abc_num IS NOT NULL"
    if ($onBoard -eq 0) { Ok "coil taken off the board" } else { Bad "coil still on the board after its run ended" }

    # --- 5. The flooring, on a real Oracle NUMBER -------------------------------------
    Step "5. A re-weigh heavier than the start records no pass (not a negative one)"
    $null = Post "/api/das/lines/$lineNum/coil-run/start" @{ coilAbcNum = $coilNum; abJobNum = $jobNum }
    $b2 = Scalar "SELECT coil_begin_wt FROM shift_coil WHERE shift_num = $testShift AND coil_end_time IS NULL"
    if ($null -ne $b2) {
        $null = Post "/api/das/lines/$lineNum/coil-run/end" @{ coilAbcNum = $coilNum; abJobNum = $jobNum; endWeight = ([decimal]$b2 + 250); endStatus = 2 }
        $pw2 = Scalar "SELECT process_wt FROM shift_coil WHERE shift_num = $testShift AND coil_abc_num = $coilNum"
        if ([decimal]$pw2 -ge 0) { Ok "process_wt floored at $pw2 (never negative)" } else { Bad "process_wt went negative: $pw2" }
    } else { Write-Host "  (run resumed rather than reopened — flooring covered by the xUnit suite)" -ForegroundColor Yellow }

    # --- 6. Board writes ---------------------------------------------------------------
    Step "6. current-job / current-coil (LINE_PRIORITY re-sequence + the rate reset)"
    $null = Post "/api/das/lines/$lineNum/current-job"  @{ abJobNum = $jobNum }
    $running = Scalar "SELECT COUNT(*) FROM line_priority WHERE line_num = $lineNum AND status = 1"
    if ([int]$running -le 1) { Ok "at most one job reads as Running ($running)" } else { Bad "$running jobs read as Running on line $lineNum" }

    $null = Post "/api/das/lines/$lineNum/current-coil" @{ coilAbcNum = $coilNum }
    $rate = Scalar "SELECT coil_process_rate FROM line_current_status WHERE line_num = $lineNum"
    if ([decimal]$rate -eq 0) { Ok "loading zeroed the process rate" } else { Bad "coil_process_rate=$rate after loading, expected 0" }

    $null = Post "/api/das/lines/$lineNum/current-coil" @{ coilAbcNum = $null }
    $fromLine = Scalar "SELECT coil_status_from_line FROM coil WHERE coil_abc_num = $coilNum"
    if ($null -ne $fromLine) { Ok "dropping kept coil_status_from_line ($fromLine) — the coil has been on a line" }
    else { Bad "dropping the coil reset coil_status_from_line — history erased" }

    # --- 7. Shift end: dt_total in SECONDS, on the plant clock -------------------------
    Step "7. POST /das/lines/$lineNum/shift/end"
    $dt = Post "/api/downtime" @{ abJobNum = $jobNum; lineNum = $lineNum; shiftNum = $testShift
                                 startingTime = (Get-Date).AddMinutes(-30).ToString("s")
                                 endingTime   = (Get-Date).AddMinutes(-15).ToString("s"); note = "ZZ_DAS_VALIDATION" }
    [void]$cleanup.Add("DELETE FROM dt_instance WHERE instance_num = $($dt.instanceNum);")

    $end = Post "/api/das/lines/$lineNum/shift/end" $null
    $total = Scalar "SELECT dt_total FROM shift WHERE shift_num = $testShift"
    if ([decimal]$total -eq 900) { Ok "dt_total = 900 (15 min in SECONDS)" } else { Bad "dt_total=$total, expected 900 seconds" }

    $stamped = Scalar "SELECT end_time FROM shift WHERE shift_num = $testShift"
    $skew = [math]::Abs(((Get-Date) - [datetime]$stamped).TotalMinutes)
    if ($skew -lt 5) { Ok "end_time on the plant clock (skew ${skew} min)" }
    else { Bad "end_time is $stamped against a local now of $(Get-Date) — looks like UtcNow" }

    $openRuns = Scalar "SELECT COUNT(*) FROM shift_coil WHERE shift_num = $testShift AND coil_end_time IS NULL"
    if ($openRuns -eq 0) { Ok "no run left open by the shift end (cross-shift carry closed them)" }
    else { Bad "$openRuns run(s) still open after the shift ended" }
}
finally {
    Step "Restoring the board and removing the rows this script created"
    if ($KeepRows) {
        Write-Host "  -KeepRows: nothing removed. To undo:" -ForegroundColor Yellow
        $cleanup | ForEach-Object { Write-Host "    $_" }
    }
    else {
        # Board first: it is what the floor reads, and it must go back even if a step threw.
        if ($null -ne $boardSnapshot -and $boardSnapshot.Rows.Count -gt 0) {
            $r = $boardSnapshot.Rows[0]
            $v = { param($x) if ($x -eq [DBNull]::Value -or $null -eq $x) { "NULL" } else { "$x" } }
            try {
                $null = Sql ("UPDATE line_current_status SET shift_num = $(& $v $r['SHIFT_NUM']), " +
                             "ab_job_num = $(& $v $r['AB_JOB_NUM']), coil_abc_num = $(& $v $r['COIL_ABC_NUM']), " +
                             "coil_process_rate = $(& $v $r['COIL_PROCESS_RATE']) WHERE line_num = $lineNum") $false
                Ok "line_current_status restored"
            } catch { Bad "could not restore the board: $_" }
        }
        if ($null -ne $coilSnapshot -and $coilSnapshot.Rows.Count -gt 0) {
            $r = $coilSnapshot.Rows[0]
            $v = { param($x) if ($x -eq [DBNull]::Value -or $null -eq $x) { "NULL" } else { "$x" } }
            try {
                $null = Sql ("UPDATE coil SET coil_status = $(& $v $r['COIL_STATUS']), coil_status_from_line = $(& $v $r['COIL_STATUS_FROM_LINE']), " +
                             "net_wt_balance = $(& $v $r['NET_WT_BALANCE']), net_wt_balance_from_line = $(& $v $r['NET_WT_BALANCE_FROM_LINE']) " +
                             "WHERE coil_abc_num = $coilNum") $false
                Ok "coil $coilNum restored"
            } catch { Bad "could not restore coil $coilNum : $_" }
        }
        # Then the rows, children first.
        foreach ($sql in ($cleanup | Sort-Object -Descending)) {
            try { $null = Sql $sql.TrimEnd(';') $false } catch { Write-Host "  left behind: $sql" -ForegroundColor Yellow }
        }
        try { $null = Sql "DELETE FROM error_evt WHERE error_comment LIKE 'ZZ_DAS_VALIDATION%' OR message LIKE 'ZZ_DAS_VALIDATION%'" $false } catch { }
        Ok "cleanup complete"
    }

    if ($apiProc) { try { Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue } catch { } }

    Step "Result"
    if ($script:failures.Count -eq 0) {
        Write-Host "  All DAS write checks passed against live Oracle." -ForegroundColor Green
    } else {
        Write-Host "  $($script:failures.Count) check(s) FAILED:" -ForegroundColor Red
        $script:failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
        Write-Host "`n  Report these back — each one is a live-only defect the SQLite suite cannot see." -ForegroundColor Yellow
    }
}
