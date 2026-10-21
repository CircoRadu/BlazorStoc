<#
.SYNOPSIS
  Lets the clients that connect through WireGuard reach BlazorStoc (run on the SERVER as an administrator).
.DESCRIPTION
  The firewall rule "BlazorStoc (port <n>)" made by the installer allows only the local subnet. A phone or laptop on the VPN comes from another subnet (the WireGuard one), so its
  packets are dropped. This script adds that subnet to the rule and then checks the rest of the path:
    1. the port: -Port, else the port of the existing rule "BlazorStoc (port <n>)";
    2. the VPN subnet: -VpnSubnet (e.g. 10.8.0.0/24), else the subnet of the WireGuard adapter of this server, if the server itself runs WireGuard;
    3. the rule: LocalSubnet + the VPN subnet (not the Internet);
    4. checks: the application listens on all interfaces (Setari -> Aplicatie -> Interfata = accesibila din retea), IP forwarding when the server is the WireGuard gateway.
  There is no HTTPS in the application: keep the access to the VPN and the local network only.
.EXAMPLE
  .\allow-wireguard.ps1 -VpnSubnet 10.8.0.0/24
  .\allow-wireguard.ps1 -Port 5087 -VpnSubnet 10.8.0.0/24,10.9.0.0/24
  .\allow-wireguard.ps1 -Remove -Port 5087 -VpnSubnet 10.8.0.0/24      # takes the subnet out again
#>
param(
    [int]$Port,
    [string[]]$VpnSubnet,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ruleaza scriptul dintr-un PowerShell deschis ca Administrator.'
}

# 1. the port
if (-not $Port) {
    $found = @(Get-NetFirewallRule -DisplayName 'BlazorStoc (port *)' -ErrorAction SilentlyContinue)
    if ($found.Count -eq 0) { throw 'Nu exista regula "BlazorStoc (port N)". Da -Port <port-ul aplicatiei>.' }
    if ($found.Count -gt 1) { throw 'Sunt mai multe reguli BlazorStoc; da -Port.' }
    $Port = [int]($found[0].DisplayName -replace '\D', '')
}
$name = "BlazorStoc (port $Port)"

# 2. the VPN subnet
if (-not $VpnSubnet) {
    $wg = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceDescription -match 'WireGuard' -or $_.Name -match 'wg|WireGuard' } | Select-Object -First 1
    if ($wg) {
        $address = Get-NetIPAddress -InterfaceIndex $wg.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($address) {
            $bytes = [System.Net.IPAddress]::Parse($address.IPAddress).GetAddressBytes()
            $mask = [uint32]([math]::Pow(2, 32) - [math]::Pow(2, 32 - $address.PrefixLength))
            $network = [byte[]]@(0, 0, 0, 0)
            for ($i = 0; $i -lt 4; $i++) { $network[$i] = $bytes[$i] -band [byte](($mask -shr (24 - 8 * $i)) -band 255) }
            $VpnSubnet = @(([System.Net.IPAddress]::new($network)).ToString() + '/' + $address.PrefixLength)
            Write-Output "Subreteaua WireGuard gasita pe acest server ($($wg.Name)): $VpnSubnet"
        }
    }
    if (-not $VpnSubnet) { throw 'Nu am gasit adaptorul WireGuard. Da -VpnSubnet cu subreteaua VPN a clientilor (de exemplu 10.8.0.0/24: AllowedIPs/Address din configuratia WireGuard).' }
}

# 3. the rule
$rule = Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
$current = if ($rule) { @(($rule | Get-NetFirewallAddressFilter).RemoteAddress) } else { @('LocalSubnet') }
$wanted = if ($Remove) { @($current | Where-Object { $VpnSubnet -notcontains $_ }) } else { @($current + $VpnSubnet | Select-Object -Unique) }
if ($wanted.Count -eq 0) { $wanted = @('LocalSubnet') }
if ($rule) { Set-NetFirewallRule -DisplayName $name -RemoteAddress $wanted -Profile Any -Enabled True }
else {
    New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Any -RemoteAddress $wanted `
        -Description 'BlazorStoc: access from the local network and the VPN.' | Out-Null
}
Write-Output "Regula '$name': TCP $Port permis de la $($wanted -join ', ')."

# 4. checks
$listen = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
if ($listen.Count -eq 0) { Write-Warning "Nimic nu asculta pe portul $Port acum (aplicatia este oprita?)." }
elseif ($listen.LocalAddress -contains '127.0.0.1' -and $listen.LocalAddress -notcontains '0.0.0.0' -and $listen.LocalAddress -notcontains '::') {
    Write-Warning "Aplicatia asculta doar pe 127.0.0.1. In aplicatie: Setari -> Aplicatie -> Interfata = 'accesibila din retea', apoi Salveaza."
}
else { Write-Output "Aplicatia asculta pe toate interfetele (port $Port)." }

$wgHere = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceDescription -match 'WireGuard' }
if ($wgHere) {
    $forwarding = (Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.InterfaceIndex -eq $wgHere[0].ifIndex }).Forwarding
    Write-Output "Server cu WireGuard local: forwarding pe adaptor = $forwarding."
}
else {
    Write-Output 'Serverul nu ruleaza WireGuard: gateway-ul VPN (router/alt calculator) trebuie sa ruteze subreteaua VPN spre acest server, iar clientul sa aiba in AllowedIPs subreteaua LAN (de exemplu 192.168.100.0/24).'
}
Write-Output "Test de pe client: http://<ip-server-LAN>:$Port/  (de pe server: Test-NetConnection <ip-client-VPN> nu este necesar; verifica intai ping spre IP-ul LAN al serverului)."
