<#
.SYNOPSIS
  Poll the ABIS edge /run-state and print running / stopped / unknown, so you can watch
  it flip as the line starts and stops.

.DESCRIPTION
  Run ON THE LINE PC (where the edge service runs), after the edge is pointed at the UA
  wrapper and Edge:Opc:RunStateTag is set. It hits the edge's HTTP endpoint (not the UA
  server directly). A quick way to confirm the whole chain — PLC → INGEAR → wrapper →
  edge — before checking the DAS console.

.EXAMPLE
  .\Test-RunState.ps1
  .\Test-RunState.ps1 -EdgeUrl http://localhost:8090 -IntervalSeconds 2 -Count 60
#>
[CmdletBinding()]
param(
    [string]$EdgeUrl = 'http://localhost:8090',
    [int]$IntervalSeconds = 2,
    [int]$Count = 30
)

$base = $EdgeUrl.TrimEnd('/')
for ($i = 0; $i -lt $Count; $i++) {
    try {
        $r = Invoke-RestMethod -Uri "$base/run-state" -TimeoutSec 5
        if (-not $r.configured) {
            $state = 'NOT CONFIGURED (set Edge:Opc:RunStateTag)'
        } elseif ($null -eq $r.running) {
            $state = 'unknown (bad/stale read)'
        } elseif ($r.running) {
            $state = 'RUNNING'
        } else {
            $state = 'STOPPED'
        }
        '{0:HH:mm:ss}  tag={1}  value={2}  quality={3}  => {4}' -f (Get-Date), $r.tag, $r.value, $r.quality, $state
    } catch {
        '{0:HH:mm:ss}  edge unreachable: {1}' -f (Get-Date), $_.Exception.Message
    }
    if ($i -lt $Count - 1) { Start-Sleep -Seconds $IntervalSeconds }
}
