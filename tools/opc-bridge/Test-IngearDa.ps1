<#
.SYNOPSIS
  Smoke-test the local INGEAR Classic OPC DA connection via the OPC.Automation wrapper. READ-ONLY:
  connect + read only; NEVER writes to a PLC. Run ON the OPC box from 32-bit PowerShell (INGEAR is
  32-bit): C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe

.DESCRIPTION
  SYNCHRONOUS device read - OPCItem.Read(Source=2 OPCDevice) via reflection InvokeMember WITH a
  ParameterModifier for the ByRef out-params. This is the exact technique the ABIS edge's ClassicDa
  provider uses, and a device read forces INGEAR to poll the PLC now (rather than relying on a cache
  that only fills for active subscribers). Good + value => proven; Bad quality => INGEAR isn't getting
  data from the PLC (comm/config), not a client problem.

.EXAMPLE
  ... Test-IngearDa.ps1
  ... Test-IngearDa.ps1 -Items 'Device110.spm','Device84.spm','Device78.spm'
#>
[CmdletBinding()]
param(
    [string]$ProgId = 'CimQuestInc.IGOPCAB.1',
    [string[]]$Items = @('Device110.spm', 'Device110.idle')
)

$ErrorActionPreference = 'Continue'

$type = [Type]::GetTypeFromProgID('OPC.Automation.1')
if (-not $type) {
    Write-Host "OPC.Automation is NOT registered. Register it (32-bit):" -ForegroundColor Red
    Write-Host "  C:\Windows\SysWOW64\regsvr32.exe C:\Windows\SysWOW64\OPCDAAuto.dll"
    return
}

$srv = [Activator]::CreateInstance($type)
try {
    $srv.Connect($ProgId)
    Write-Host ("[ok]   connected  (ServerState={0})" -f $srv.ServerState) -ForegroundColor Green

    $grp = $srv.OPCGroups.Add('abis-smoke')
    $grp.IsActive = $true
    $opcItems = $grp.OPCItems

    $handles = @{}; $h = 1
    foreach ($id in $Items) {
        try { $handles[$id] = $opcItems.AddItem($id, $h); $h++; Write-Host ("[ok]   added item: {0}" -f $id) }
        catch { Write-Host ("[--]   add failed: {0}  ({1})" -f $id, $_.Exception.Message) -ForegroundColor Yellow }
    }

    Write-Host "--- synchronous device reads (OPCItem.Read via InvokeMember + ParameterModifier) ---"
    $invoke = [System.Reflection.BindingFlags]::InvokeMethod
    foreach ($id in $Items) {
        if (-not $handles.ContainsKey($id)) { continue }
        $it = $handles[$id]
        try {
            $a = [object[]]@([int]2, $null, $null, $null)     # Source=OPCDevice, [out]Value, [out]Quality, [out]TimeStamp
            $m = [System.Reflection.ParameterModifier]::new(4)
            $m[1] = $true; $m[2] = $true; $m[3] = $true         # mark Value/Quality/TimeStamp ByRef
            [void]$it.GetType().InvokeMember('Read', $invoke, $null, $it, $a, @($m), $null, $null)
            $q = [int]$a[2]
            $good = (($q -band 0xC0) -eq 0xC0)
            Write-Host ("       {0,-28} value={1,-12} quality={2} ({3})" -f $id, $a[1], $q, $(if ($good) { 'GOOD' } else { 'BAD/UNCERTAIN' }))
        }
        catch { Write-Host ("       {0,-28} Read failed: {1}" -f $id, $_.Exception.Message) -ForegroundColor Yellow }
    }
}
catch { Write-Host ("[ERROR] {0}" -f $_.Exception.Message) -ForegroundColor Red }
finally { try { $srv.Disconnect() } catch { } }
