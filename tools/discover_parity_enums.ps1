<#
.SYNOPSIS
  READ-ONLY discovery of the value-sets the deferred parity guards need before they can be
  hard-enforced (coil/skid/job status enums, die enums, cash-date flag, date-column types).

.DESCRIPTION
  Runs a fixed set of SELECT-only queries through tools/oraq (which HARD-REFUSES anything but a
  single SELECT/WITH), so it writes nothing and is safe to point at any ABIS database. Target the
  NON-PROD .230 / abc11 sandbox. Each block prints what a deferred guard is waiting on; paste the
  output back and the enum guards get finalized from real data (not guesses).

.EXAMPLE
  $env:ORA_CS = "Data Source=192.168.1.230:1521/abc11;User Id=dbo;Password=YOURPW;"
  ./tools/discover_parity_enums.ps1

  # or pass it explicitly (SID form if the service-name form gives ORA-12514):
  ./tools/discover_parity_enums.ps1 -ConnectionString "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.1.230)(PORT=1521))(CONNECT_DATA=(SID=abc11)));User Id=dbo;Password=YOURPW;"
#>
[CmdletBinding()]
param(
    [string]$ConnectionString = $env:ORA_CS
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "No connection string. Set `$env:ORA_CS or pass -ConnectionString (target the non-prod .230/abc11 sandbox)."
}
$oraq = Join-Path $PSScriptRoot "oraq/oraq.csproj"

function Q($rank, $label, $sql) {
    Write-Host "`n=== [$rank] $label ===" -ForegroundColor Cyan
    Write-Host $sql -ForegroundColor DarkGray
    & dotnet run --project $oraq -c Release -- --cs $ConnectionString --sql $sql --max 200 | Out-Host
}

Write-Host "READ-ONLY parity-guard enum discovery (writes nothing)." -ForegroundColor Green
Write-Host "Connection: $($ConnectionString -replace 'Password=[^;]*','Password=***')"

# --- Status value-sets (enum guards deferred pending live confirmation) ------------------
Q "13"  "coil_status value-set (mint uses 11; terminal 0/10/13 — confirm the full set)" `
    "SELECT coil_status, COUNT(*) AS n FROM coil GROUP BY coil_status ORDER BY coil_status"

Q "5/11" "sheet_skid.skid_sheet_status value-set (guard assumes {0,1,2,3,4,8,9,10,11})" `
    "SELECT skid_sheet_status, COUNT(*) AS n FROM sheet_skid GROUP BY skid_sheet_status ORDER BY skid_sheet_status"

Q "16"  "scrap_skid.skid_scrap_status value-set" `
    "SELECT skid_scrap_status, COUNT(*) AS n FROM scrap_skid GROUP BY skid_scrap_status ORDER BY skid_scrap_status"

Q "22"  "ab_job.job_status value-set (0=Done confirmed; what does a NEW job default to?)" `
    "SELECT job_status, COUNT(*) AS n FROM ab_job GROUP BY job_status ORDER BY job_status"

# --- Customer-conditional flags (rank 19 Part B / rank 24) -------------------------------
Q "19B" "customer.cash_date_required distribution (drives conditional-required)" `
    "SELECT cash_date_required, COUNT(*) AS n FROM customer GROUP BY cash_date_required ORDER BY cash_date_required"

Q "24"  "customer.coil_cert_label_req distribution (drives cert-label completeness)" `
    "SELECT coil_cert_label_req, COUNT(*) AS n FROM customer GROUP BY coil_cert_label_req ORDER BY coil_cert_label_req"

# --- Die enums (rank 20 — where do the columns live + their value-sets?) -----------------
Q "20"  "locate the die enum columns (num_of_parts_per_hit / status / location / engineered-scrap)" `
    "SELECT table_name, column_name, data_type, data_length FROM all_tab_columns WHERE (column_name LIKE '%PARTS_PER_HIT%' OR column_name LIKE '%DIE%LOCATION%' OR column_name LIKE '%ENGINEER%SCRAP%' OR (table_name = 'DIE' AND column_name = 'STATUS')) ORDER BY table_name, column_name"

Q "20"  "die.status value-set" `
    "SELECT status, COUNT(*) AS n FROM die GROUP BY status ORDER BY status"

# --- Date-column types (rank 18 range-binding safety on Oracle) --------------------------
Q "18"  "shift.start_time column type (DATE/TIMESTAMP → the [day..next-day) range binding is safe)" `
    "SELECT data_type, data_length, data_scale FROM all_tab_columns WHERE table_name = 'SHIFT' AND column_name = 'START_TIME'"

# --- Deferred rollup / config surfaces (ranks 16 + 25 Part B) ----------------------------
Q "16"  "return_scrap_item columns (for the scrap_net = SUM(return_item_net_wt) rollup)" `
    "SELECT column_name, data_type, data_length FROM all_tab_columns WHERE table_name = 'RETURN_SCRAP_ITEM' ORDER BY column_id"

Q "25B" "any edge-trim tolerance config table (band currently hardcoded 1.5-12)" `
    "SELECT table_name FROM all_tables WHERE table_name LIKE '%EDGE%TRIM%' OR table_name LIKE '%TRIM%TOL%' ORDER BY table_name"

Q "25B" "any system_log table (for the override audit row)" `
    "SELECT table_name FROM all_tables WHERE table_name LIKE '%SYSTEM_LOG%' OR table_name = 'SYSTEM_LOG' ORDER BY table_name"

Write-Host "`n=== discovery complete — paste the above back to finalize the enum guards ===" -ForegroundColor Green
