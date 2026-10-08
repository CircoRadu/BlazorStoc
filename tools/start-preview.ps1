<#
.SYNOPSIS
  Publishes BlazorStoc and (re)starts the preview from the published build, on MariaDB.
.DESCRIPTION
  Starts the local MariaDB if needed, stops whatever listens on -Port (same Windows account), publishes to
  artifacts\notif-build, starts the app with the private configuration and checks that the stylesheet is served.
  ASPNETCORE_WEBROOT must point at the published wwwroot: with the project folder as content root the compressed static
  assets are not found and browsers get a 0-byte stylesheet (the page looks unstyled).
.EXAMPLE
  .\tools\start-preview.ps1
  .\tools\start-preview.ps1 -Port 5090 -NoPublish
#>
[CmdletBinding()]
param(
    [int]$Port = 5087,
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$SecretsDir,
    [string]$MariaBinDir,
    [switch]$NoPublish,
    [switch]$CleanAssets
)
$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
function Write-Step([string]$Name) { Write-Output ("[{0,5:N1}s] {1}" -f $clock.Elapsed.TotalSeconds, $Name) }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsDir) { $SecretsDir = Join-Path $repo 'local-secrets' }
$config = Join-Path $SecretsDir 'application-connection.private.json'
if (-not (Test-Path $config)) { throw "$config not found. Run tools\setup-dev-environment.ps1 first." }
$build = Join-Path $repo 'artifacts\notif-build'

$databasePort = (Get-Content $config -Raw | ConvertFrom-Json).Database.Port
$mariaArguments = @{ Action = 'start'; Root = $MariaRoot; Port = $databasePort }
if ($MariaBinDir) { $mariaArguments.BinDir = $MariaBinDir }
& (Join-Path $PSScriptRoot 'dev-mariadb.ps1') @mariaArguments

foreach ($listener in @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)) {
    Stop-Process -Id $listener.OwningProcess -Force
    Start-Sleep -Seconds 1
}

if (-not $NoPublish -or -not (Test-Path (Join-Path $build 'BlazorStoc.dll'))) {
    # Stale compressed-asset caches otherwise survive a change of static files. Clearing them forces every static asset to be compressed
    # again (the slowest part of the publish), so it is done only when a file of wwwroot is newer than the last publish (or with -CleanAssets).
    $published = Join-Path $build 'BlazorStoc.dll'
    $staticChanged = $CleanAssets -or -not (Test-Path $published) -or
        (Get-ChildItem (Join-Path $repo 'wwwroot') -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\lib\\' -and $_.LastWriteTimeUtc -gt (Get-Item $published).LastWriteTimeUtc } | Select-Object -First 1)
    if ($staticChanged) {
        Remove-Item (Join-Path $repo 'obj\Release\net9.0\compressed'), (Join-Path $repo 'obj\Release\net9.0\staticwebassets') -Recurse -Force -ErrorAction SilentlyContinue
        Write-Step 'static files changed: asset caches cleared'
    }
    Push-Location $repo
    try {
        & dotnet publish BlazorStoc.csproj -c Release -o $build
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    }
    finally { Pop-Location }
    Write-Step 'published'
}

$env:Database__PrivateConfigPath = $config
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:ASPNETCORE_WEBROOT = Join-Path $build 'wwwroot'
# The backup and restore tools (mariadb-dump, mariadb) of the same MariaDB distribution the preview runs on; without these paths the application looks for them under
# %LocalAppData%\BlazorStoc-MariaDB and a backup fails with "mariadb-dump nu a fost gasit".
$toolsBin = if ($MariaBinDir) { $MariaBinDir } else { Get-ChildItem -Path $MariaRoot -Directory -Filter 'mariadb-*' -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName 'bin' } }
if ($toolsBin -and (Test-Path (Join-Path $toolsBin 'mariadb-dump.exe'))) {
    $env:Database__MariaDumpExecutablePath = Join-Path $toolsBin 'mariadb-dump.exe'
    $env:Database__MariaClientExecutablePath = Join-Path $toolsBin 'mariadb.exe'
}
try {
    Start-Process dotnet -ArgumentList "`"$(Join-Path $build 'BlazorStoc.dll')`"" -WorkingDirectory $repo -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $repo "preview-$Port.stdout.log") -RedirectStandardError (Join-Path $repo "preview-$Port.stderr.log")
}
finally { Remove-Item Env:\Database__PrivateConfigPath, Env:\ASPNETCORE_URLS, Env:\ASPNETCORE_WEBROOT -ErrorAction SilentlyContinue }

$page = $null
for ($attempt = 0; $attempt -lt 40 -and -not $page; $attempt++) {
    Start-Sleep -Milliseconds 750
    try { $page = Invoke-WebRequest "http://127.0.0.1:$Port/Account/Login" -UseBasicParsing -TimeoutSec 5 } catch { }
}
Write-Step 'application answers'
if (-not $page) { throw "The preview did not answer on port $Port; see preview-$Port.stderr.log and preview-$Port.stdout.log." }
$stylesheet = [regex]::Match($page.Content, 'href="([^"]*app[^"]*\.css)"').Groups[1].Value
if ($stylesheet) {
    $css = Invoke-WebRequest "http://127.0.0.1:$Port/$($stylesheet.TrimStart('/'))" -UseBasicParsing -Headers @{ 'Accept-Encoding' = 'gzip, deflate, br' }
    if ($css.RawContentLength -le 0) { throw 'The stylesheet is served with 0 bytes (check ASPNETCORE_WEBROOT).' }
}
Write-Output "Preview ready: http://127.0.0.1:$Port/  (stylesheet OK)"
