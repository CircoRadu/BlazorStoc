<#
.SYNOPSIS
  The Windows Firewall rule that lets the other computers of the network reach BlazorStoc (run by the installer as an administrator).
.DESCRIPTION
  -Action add: an inbound TCP rule "BlazorStoc (port <n>)" for the port of the application, for every network profile, from the local subnet only (the computers of the same network, not
  the Internet). -Action remove: removes it. Idempotent. There is no HTTPS in the application itself: do not open the port beyond the local network without a reverse proxy.
.EXAMPLE
  .\firewall.ps1 -Action add -Port 5087
  .\firewall.ps1 -Action remove -Port 5087
#>
param(
    [Parameter(Mandatory)][ValidateSet('add', 'remove')][string]$Action,
    [Parameter(Mandatory)][int]$Port,
    # Who may connect: the local subnet by default; a VPN (WireGuard...) comes from another subnet, e.g. -RemoteAddress LocalSubnet,10.8.0.0/24
    [string[]]$RemoteAddress = @('LocalSubnet')
)
$ErrorActionPreference = 'Stop'
$name = "BlazorStoc (port $Port)"
Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($Action -eq 'add') {
    New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Any -RemoteAddress $RemoteAddress `
        -Description 'BlazorStoc: access to the application from the local network (made by the installer).' | Out-Null
    Write-Output "Firewall: inbound TCP $Port allowed from $($RemoteAddress -join ', ') ($name)."
}
else { Write-Output "Firewall: the rule '$name' is removed (or did not exist)." }
