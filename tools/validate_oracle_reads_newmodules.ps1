<#
.SYNOPSIS
  Live-validates the ABIS API READ paths for the modules built AFTER the original
  Oracle validation (which covered dies/sketches/shipments/orders/etc via
  validate_oracle_writes.ps1). Read-only: it never mutates data.

.DESCRIPTION
  Runs the API in Oracle mode locally, then GETs every newer-module read + report
  endpoint against the live schema, capturing the HTTP status and any ORA-* error
  body. Reporting (19 aggregation queries with joins + date ranges) is the highest
  live-only SQL risk (ORA-00933 12c paging, ORA-00932 COALESCE typing), so it is
  swept in full. Prints a PASS/FAIL table at the end.

  Run from a machine that can reach the Oracle listener directly (192.168.1.230:1521).
  Requires the .NET 8 SDK and this repo checked out.

.EXAMPLE
  ./tools/validate_oracle_reads_newmodules.ps1 -ConnectionString "Data Source=192.168.1.230:1521/abc11;User Id=dbo;Password=YOURPW;"

  # SID form (if the service-name form gives ORA-12514):
  ./tools/validate_oracle_reads_newmodules.ps1 -ConnectionString "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.1.230)(PORT=1521))(CONNECT_DATA=(SID=abc11)));User Id=dbo;Password=YOURPW;"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [string]$BaseUrl = "http://127.0.0.1:5230",
    [switch]$NoRun,           # skip launching the API (use if it is already running)
    [switch]$SkipReporting    # skip the slow reporting suite (use to re-verify the rest quickly)
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot "api/src/ABIS.Api/ABIS.Api.csproj"
$results = [System.Collections.Generic.List[object]]::new()

function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

# GET a path; record PASS (2xx), or FAIL with the ORA-* code / status pulled from
# the error body. Never throws — a failing endpoint must not abort the sweep.
function Check($module, $path) {
    $uri = "$BaseUrl$path"
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $resp = Invoke-WebRequest -Method Get -Uri $uri -UseBasicParsing
        $sw.Stop()
        $results.Add([pscustomobject]@{ Module = $module; Path = $path; Status = [int]$resp.StatusCode; Result = "PASS"; Detail = "$($sw.ElapsedMilliseconds) ms" })
        Write-Host ("  PASS  {0}" -f $path) -ForegroundColor Green
    }
    catch {
        $status = $null; $body = $null
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $body = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
            } catch { }
        }
        # Surface the Oracle error code if the API bubbled it into the ProblemDetails body.
        $ora = if ($body -match "ORA-\d{5}") { $Matches[0] } else { $null }
        $detail = if ($ora) { $ora } elseif ($body) { ($body -replace '\s+', ' ').Substring(0, [Math]::Min(160, $body.Length)) } else { $_.Exception.Message }
        $results.Add([pscustomobject]@{ Module = $module; Path = $path; Status = $status; Result = "FAIL"; Detail = $detail })
        Write-Host ("  FAIL  {0}  [{1}] {2}" -f $path, $status, $detail) -ForegroundColor Red
    }
}

function FirstId($path, $prop) {
    try {
        $r = Invoke-RestMethod -Method Get -Uri "$BaseUrl$path"
        $items = if ($null -ne $r.items) { $r.items } else { $r }
        return ($items | Where-Object { $_.$prop } | Select-Object -First 1).$prop
    } catch { return $null }
}

# --- Configure + launch the API in Oracle mode ---------------------------------
$env:Database__Provider = "Oracle"
$env:Database__ConnectionString = $ConnectionString
$env:Database__Seed = "false"
$env:ApiKeys__Enabled = "false"      # local validation only; no key needed
$env:Audit__Enabled = "false"        # opc_action_log may not exist in DBO
$env:ASPNETCORE_URLS = $BaseUrl
$env:ASPNETCORE_ENVIRONMENT = "Development"

$apiProc = $null
if (-not $NoRun) {
    Step "Building + launching the API in Oracle mode"
    & dotnet build $proj -c Release | Out-Host
    $apiProc = Start-Process dotnet -ArgumentList "run --no-build --project `"$proj`" -c Release" -PassThru -NoNewWindow
}

try {
    # --- Readiness (proves the DB connection) ---------------------------------
    Step "Waiting for /health/ready (live DB connectivity)"
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { if ((Invoke-RestMethod "$BaseUrl/health/ready").status -eq "ready") { $ready = $true; break } } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw "API never became ready - check the connection string / listener (ORA-12514 => DB instance stopped)." }
    Write-Host "DB reachable." -ForegroundColor Green

    # --- Resolve real ids the id-bearing reads need ---------------------------
    Step "Resolving real ids from list endpoints"
    $jobNum   = FirstId "/api/jobs?pageSize=1" "abJobNum"
    $custId   = (Invoke-RestMethod "$BaseUrl/api/customers?pageSize=5").items | Where-Object { $_.customerId -gt 0 } | Select-Object -First 1 | ForEach-Object customerId
    $skidNum  = FirstId "/api/sheet-skids?pageSize=1" "sheetSkidNum"
    Write-Host "  abJobNum=$jobNum  customerId=$custId  sheetSkidNum=$skidNum"

    # --- Reporting suite (19) — highest live-only SQL risk --------------------
    Step "Reporting suite (aggregations, date ranges, joins)"
    $from = "2020-01-01"; $to = "2026-12-31"
    if ($SkipReporting) {
      Write-Host "  (reporting suite skipped - already validated/fixed)" -ForegroundColor DarkYellow
    } else {
    Check "reporting" "/api/reporting/production-summary?from=$from&to=$to"
    Check "reporting" "/api/reporting/line-efficiency?from=$from&to=$to"
    Check "reporting" "/api/reporting/monthly-production?from=$from&to=$to"
    Check "reporting" "/api/reporting/downtime?from=$from&to=$to"
    Check "reporting" "/api/reporting/on-time?from=$from&to=$to"
    Check "reporting" "/api/reporting/customer-shipments?from=$from&to=$to"
    Check "reporting" "/api/reporting/open-shipments"
    Check "reporting" "/api/reporting/customer-orders"
    Check "reporting" "/api/reporting/customer-skid-count"
    Check "reporting" "/api/reporting/coil-inventory"
    Check "reporting" "/api/reporting/coil-on-hold"
    Check "reporting" "/api/reporting/skid-inventory"
    Check "reporting" "/api/reporting/unmatched-coils"
    Check "reporting" "/api/reporting/qa-mechanical?from=$from&to=$to"
    Check "reporting" "/api/reporting/scrap-summary"
    Check "reporting" "/api/reporting/scrap-by-job"
    }

    # --- Accounting / invoicing ----------------------------------------------
    Step "Accounting"
    if ($jobNum) { Check "accounting" "/api/accounting/rej-reband-coils?abJobNum=$jobNum" }

    # --- Quality / recovery ---------------------------------------------------
    Step "Quality / recovery"
    Check "quality" "/api/quality/scrap-types"
    Check "quality" "/api/quality/product-types"
    Check "quality" "/api/quality/recovery-customers"
    if ($custId) { Check "quality" "/api/quality/customer-defects?customerId=$custId" }

    # --- Coil eval / QC -------------------------------------------------------
    Step "Coil eval / QC"
    if ($jobNum)  { Check "coil-eval" "/api/coil-eval/coils?abJobNum=$jobNum" }
    if ($skidNum) { Check "coil-eval" "/api/coil-eval/skids/$skidNum/dimension-checks" }
    if ($jobNum)  { Check "coil-eval" "/api/coil-eval/jobs/$jobNum/eval-scrap" }

    # --- Production folder ----------------------------------------------------
    Step "Production folder"
    if ($jobNum) { Check "prod-folder" "/api/prod-folder/jobs/$jobNum" }
    if ($jobNum) { Check "prod-folder" "/api/prod-folder/jobs/$jobNum/notes" }

    # --- Stacker line board ---------------------------------------------------
    Step "Stacker"
    Check "stacker" "/api/stacker/board"
    Check "stacker" "/api/stacker/line-errors?from=$from&to=$to"

    # --- Sales / quote lifecycle ---------------------------------------------
    Step "Sales"
    Check "sales" "/api/sales/quotes"
    Check "sales" "/api/sales/contacts"
    $q = $null
    try { $q = (Invoke-RestMethod "$BaseUrl/api/sales/quotes") | Select-Object -First 1 } catch { }
    if ($q -and $q.quoteId -and $q.revisionId) {
        Check "sales" "/api/sales/quotes/$($q.quoteId)/$($q.revisionId)"
        Check "sales" "/api/sales/quotes/$($q.quoteId)/$($q.revisionId)/events"
        Check "sales" "/api/sales/quotes/$($q.quoteId)/$($q.revisionId)/probability"
    } else { Write-Host "  (no quote rows to drill into detail/events/probability)" -ForegroundColor DarkYellow }

    # --- Coil ownership / toll transfer --------------------------------------
    Step "Coil ownership"
    Check "coil-ownership" "/api/coil-ownership/transfers"
    Check "coil-ownership" "/api/coil-ownership/transferable-coils"
    $cert = FirstId "/api/coil-ownership/transfers" "certificateNum"
    if ($cert) { Check "coil-ownership" "/api/coil-ownership/transfers/$cert/certificate" }

    # --- Parts / carriers (newer master-data reads) --------------------------
    Step "Parts / carriers"
    Check "parts"    "/api/parts?pageSize=5"
    Check "carriers" "/api/carriers?pageSize=5"

    # --- Warehouse (sheet-skid read surface behind the warehouse PATCH) -------
    Step "Warehouse"
    Check "warehouse" "/api/sheet-skids?pageSize=5&sort=sheetSkidNum"
    if ($skidNum) { Check "warehouse" "/api/sheet-skids/$skidNum" }

    # --- Security reads -------------------------------------------------------
    Step "Security"
    Check "security" "/api/security/users"
    Check "security" "/api/security/groups"
    Check "security" "/api/security/applications"

    # --- Summary --------------------------------------------------------------
    Step "SUMMARY"
    $pass = ($results | Where-Object Result -eq "PASS").Count
    $fail = ($results | Where-Object Result -eq "FAIL").Count
    $results | Format-Table Module, @{L='St';E={$_.Status}}, Result, Path, Detail -AutoSize | Out-Host
    Write-Host ("`n{0} passed, {1} failed, {2} total." -f $pass, $fail, $results.Count) -ForegroundColor ($(if ($fail) { "Yellow" } else { "Green" }))
    if ($fail) {
        Write-Host "`nFailures by module:" -ForegroundColor Yellow
        $results | Where-Object Result -eq "FAIL" | Group-Object Module | ForEach-Object { Write-Host ("  {0,-16} {1}" -f $_.Name, $_.Count) }
    }
}
finally {
    if ($apiProc -and -not $apiProc.HasExited) {
        Step "Stopping the API"
        Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
    }
}
