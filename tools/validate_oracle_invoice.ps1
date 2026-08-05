<#
  validate_oracle_invoice.ps1 — validate the greenfield INVOICE billing against live non-prod
  Oracle (192.168.1.230:1521/abc11, schema DBO). READ-ONLY: every query goes through tools/oraq,
  which refuses anything but a single SELECT/WITH.

  What it proves:
    1. Connectivity + that the invoice tables exist on the real schema (INVOICE, PRODUCTION_SHEET_ITEM,
       RETURN_SCRAP_ITEM) and that the reserved-word column "TIMESTAMP" reads back when quoted.
    2. That the exact rejected/rebanded billed-weight SQL (the legacy w_invoice.wf_rejected_coil_wt
       rule: MAX(shift-end-or-balance, prior-process-qty)) runs on Oracle and, on REAL data, differs
       from the naive SUM(process_end_wt) the browser used — i.e. the fix is material.
    3. The full weight-bucket computation for a real divergent job (net/unapplied/processed/scrap/tare
       + the reject/reband detail), so the figures can be eyeballed against expectations.

  Usage (run from inside the user's network — the cloud sandbox cannot reach Oracle):
    pwsh tools/validate_oracle_invoice.ps1 -Cs "Data Source=192.168.1.230:1521/abc11;User Id=dbo;Password=<pw>;"
  or set ORA_CS and omit -Cs.
#>
param(
  [string]$Cs = $env:ORA_CS,
  [string]$Owner = "DBO"
)

if ([string]::IsNullOrWhiteSpace($Cs)) { Write-Error "No connection string. Pass -Cs or set ORA_CS."; exit 2 }

$oraq = "dotnet"
$proj = Join-Path $PSScriptRoot "oraq/oraq.csproj"

function Q([string]$sql, [switch]$Json) {
  $a = @("run","--project",$proj,"-c","Debug","--no-build","--","--cs",$Cs,"--sql",$sql,"--timeout","120")
  if ($Json) { $a += "--json" }
  & $oraq @a
}

Write-Host "== 1. Connectivity ==" -ForegroundColor Cyan
Q "SELECT 1 AS ok FROM dual"

Write-Host "`n== 2. Invoice tables present on $Owner ==" -ForegroundColor Cyan
Q @"
SELECT
  (SELECT COUNT(*) FROM all_tables WHERE owner='$Owner' AND table_name='INVOICE') AS has_invoice,
  (SELECT COUNT(*) FROM all_tables WHERE owner='$Owner' AND table_name='PRODUCTION_SHEET_ITEM') AS has_psi,
  (SELECT COUNT(*) FROM all_tables WHERE owner='$Owner' AND table_name='RETURN_SCRAP_ITEM') AS has_rsi
FROM dual
"@

Write-Host "`n== 3. Reserved-word column reads when quoted ("TIMESTAMP") ==" -ForegroundColor Cyan
Q 'SELECT ab_job_num, invoice_num, "TIMESTAMP", notes FROM invoice WHERE ROWNUM <= 3'

Write-Host "`n== 4. Real jobs where billed (MAX rule) != naive SUM(process_end_wt) ==" -ForegroundColor Cyan
$divSql = @"
SELECT * FROM (
  SELECT ab_job_num, coils, billed_total, naive_total, (billed_total - naive_total) AS diff FROM (
    SELECT pc.ab_job_num,
           COUNT(*) AS coils,
           SUM(GREATEST(
                 COALESCE(pc.process_end_wt, c.net_wt_balance, 0),
                 COALESCE((SELECT MAX(pp.process_quantity) FROM process_coil pp
                            WHERE pp.coil_abc_num = pc.coil_abc_num
                              AND pp.process_quantity < pc.process_quantity), 0))) AS billed_total,
           SUM(COALESCE(pc.process_end_wt, 0)) AS naive_total
    FROM process_coil pc
    JOIN coil c ON c.coil_abc_num = pc.coil_abc_num
    WHERE pc.process_coil_status IN (3,7)
    GROUP BY pc.ab_job_num
  ) WHERE billed_total <> naive_total
  ORDER BY ABS(billed_total - naive_total) DESC
) WHERE ROWNUM <= 8
"@
$divJson = Q $divSql -Json | Out-String
Write-Output $divJson
$job = $null
try { $job = (($divJson | ConvertFrom-Json) | Select-Object -First 1).AB_JOB_NUM } catch {}
if (-not $job) {
  Write-Host "No divergent job found (or parse failed); pick any reject/reband job for step 5 manually." -ForegroundColor Yellow
  $job = (Q "SELECT ab_job_num FROM (SELECT ab_job_num FROM process_coil WHERE process_coil_status IN (3,7) GROUP BY ab_job_num ORDER BY COUNT(*) DESC) WHERE ROWNUM=1" -Json | Out-String | ConvertFrom-Json | Select-Object -First 1).AB_JOB_NUM
}
Write-Host "`n-- Deep-diving job $job --" -ForegroundColor Green

Write-Host "`n== 5a. Header / spec for job $job ==" -ForegroundColor Cyan
Q @"
SELECT j.ab_job_num, l.line_desc, cust.customer_short_name, co.orig_customer_po,
       oi.sheet_type, oi.alloy2, oi.temper, oi.gauge, oi.enduser_part_num
FROM ab_job j
LEFT JOIN line l ON l.line_num = j.line_num
LEFT JOIN customer_order co ON co.order_abc_num = j.order_abc_num
LEFT JOIN customer cust ON cust.customer_id = co.orig_customer_id
LEFT JOIN order_item oi ON oi.order_abc_num = j.order_abc_num AND oi.order_item_num = j.order_item_num
WHERE j.ab_job_num = $job
"@

Write-Host "`n== 5b. Per-coil billed breakdown (billed vs naive) for job $job ==" -ForegroundColor Cyan
Q @"
SELECT pc.coil_abc_num, pc.process_coil_status AS status,
       pc.process_quantity AS this_qty, pc.process_end_wt AS shift_end, c.net_wt_balance AS balance,
       (SELECT MAX(pp.process_quantity) FROM process_coil pp
         WHERE pp.coil_abc_num = pc.coil_abc_num AND pp.process_quantity < pc.process_quantity) AS max_prior,
       GREATEST(COALESCE(pc.process_end_wt, c.net_wt_balance, 0),
                COALESCE((SELECT MAX(pp.process_quantity) FROM process_coil pp
                           WHERE pp.coil_abc_num = pc.coil_abc_num AND pp.process_quantity < pc.process_quantity),0)) AS billed,
       COALESCE(pc.process_end_wt,0) AS naive
FROM process_coil pc JOIN coil c ON c.coil_abc_num = pc.coil_abc_num
WHERE pc.ab_job_num = $job AND pc.process_coil_status IN (3,7)
ORDER BY pc.coil_abc_num
"@

Write-Host "`n== 5c. Weight buckets for job $job ==" -ForegroundColor Cyan
Q @"
SELECT
  (SELECT COALESCE(SUM(process_quantity),0) FROM process_coil WHERE ab_job_num=$job) AS net_wt,
  (SELECT COALESCE(SUM(process_quantity),0) FROM process_coil WHERE ab_job_num=$job AND process_coil_status=2) AS unapplied_wt,
  (SELECT COALESCE(SUM(prod_item_net_wt),0) FROM production_sheet_item WHERE ab_job_num=$job) AS processed_wt,
  (SELECT COALESCE(SUM(return_item_net_wt),0) FROM return_scrap_item WHERE ab_job_num=$job) AS scrap_wt,
  (SELECT COALESCE(SUM(sheet_tare_wt),0) FROM sheet_skid WHERE ab_job_num=$job) AS tare_wt,
  (SELECT COUNT(*) FROM sheet_skid WHERE ab_job_num=$job) AS skid_count
FROM dual
"@

Write-Host "`nDone. Compare step 5b 'billed' vs 'naive' - any row where they differ is a real case the" -ForegroundColor Cyan
Write-Host "old browser sum would have mis-billed. Reconcile the buckets against docs/PARITY_AUDIT.md #1." -ForegroundColor Cyan
