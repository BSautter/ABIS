<#
.SYNOPSIS
  Allow the ABIS edge / line PC to reach the OPC UA wrapper on TCP 4840.

.DESCRIPTION
  Run as Administrator ON THE OPC BOX (192.168.10.170 / .175) after the UA COM Server
  Wrapper is installed. Creates a single inbound allow rule scoped to just the edge host
  (not "any"), so the UA endpoint isn't exposed to the whole LAN. Idempotent.

.EXAMPLE
  .\Open-UaFirewall.ps1 -EdgeHostIp 192.168.3.110
  .\Open-UaFirewall.ps1 -EdgeHostIp 192.168.3.110 -Port 4840
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EdgeHostIp,   # the line / edge PC IP
    [int]$Port = 4840
)

$ErrorActionPreference = 'Stop'
$name = "ABIS OPC UA wrapper (TCP $Port from $EdgeHostIp)"

if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
    Write-Host "Rule already exists: $name"
    return
}

New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort $Port -RemoteAddress $EdgeHostIp -Profile Any | Out-Null
Write-Host "Created inbound allow: TCP $Port from $EdgeHostIp"
