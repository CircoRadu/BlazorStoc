<#
.SYNOPSIS
  Starts (or restarts) the demo instance of the help pictures (see the header of tools/help-capture.mjs).
.DESCRIPTION
  Without switches: the instance on port 5089 from artifacts\help-build, on the demo database, with a fake installation (tools/help-demo-updater.mjs init) for Setari -> Actualizare.
  -Second: a second instance on port 5090, the same data but pointed at a Windows service that does not exist, so that Setari -> Baza de date shows why the server cannot be
  controlled (the pictures with an absolute address http://127.0.0.1:5090/... in their topic).
  -Publish: rebuilds artifacts\help-build first.
#>
param([switch]$Publish, [switch]$Second)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$port = if ($Second) { 5090 } else { 5089 }
$owner = (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue).OwningProcess
if ($owner) { Stop-Process -Id $owner -Force; Start-Sleep 2 }
if ($Publish) { dotnet publish BlazorStoc.csproj -c Release -o artifacts\help-build 2>&1 | Select-String -Pattern 'error' | Select-Object -First 3 }
$bin = (Get-ChildItem 'C:\Dev\BlazorStoc-MariaDB' -Directory -Filter 'mariadb-*-winx64' | Sort-Object Name | Select-Object -Last 1).FullName + '\bin'
$login = Get-Content local-secrets\test-login.private.json -Raw | ConvertFrom-Json
$artifacts = (Resolve-Path artifacts).Path
$vars = @{
    Database__PrivateConfigPath = (Resolve-Path artifacts\help-demo-secrets\demo-database.private.json).Path
    Database__OverridePath = Join-Path $artifacts 'help-demo-data\database-override.json'
    App__DataRoot = (Resolve-Path artifacts\help-demo-data).Path
    App__Supervisor = if ($Second) { 'help-demo-2' } else { 'help-demo' }
    App__InstallRoot = (Resolve-Path artifacts\help-demo-install).Path
    Database__BackupPrivateConfigPath = Join-Path $artifacts 'help-demo-secrets\none.json'
    Database__RestorePrivateConfigPath = Join-Path $artifacts 'help-demo-secrets\none.json'
    Database__MigratorPrivateConfigPath = Join-Path $artifacts 'help-demo-secrets\none.json'
    Authentication__Username = $login.Username
    Authentication__Password = $login.Password
    ASPNETCORE_URLS = "http://127.0.0.1:$port"
    ASPNETCORE_WEBROOT = (Resolve-Path artifacts\help-build\wwwroot).Path
    Database__MariaDumpExecutablePath = "$bin\mariadb-dump.exe"
    Database__MariaClientExecutablePath = "$bin\mariadb.exe"
}
if ($Second) { $vars['Database__ServerServiceName'] = 'BlazorStocDemoMariaDB' }
foreach ($k in $vars.Keys) { Set-Item "Env:\$k" $vars[$k] }
Start-Process dotnet -ArgumentList '"artifacts\help-build\BlazorStoc.dll"' -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput "artifacts\help-$port.out.log" -RedirectStandardError "artifacts\help-$port.err.log"
foreach ($k in $vars.Keys) { Remove-Item "Env:\$k" }
for ($i = 0; $i -lt 30; $i++) { try { $r = Invoke-WebRequest "http://127.0.0.1:$port/Account/Login" -UseBasicParsing -TimeoutSec 3; "up $($r.StatusCode) on $port"; break } catch { Start-Sleep 2 } }
