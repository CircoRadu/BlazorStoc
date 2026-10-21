<#
.SYNOPSIS
  Stops and removes the database server that the installer made (called by "BlazorStoc.Updater uninstall" as an administrator).
.DESCRIPTION
  Stops the Windows service -ServiceName and deletes it; when no service exists but a server of the instance answers (started without a service), it is shut down cleanly. -RemoveData also deletes
  the whole instance folder -Root (the database files, the distribution, my.ini): the data of the application are gone for good. Without it the folder stays, and a new installation reuses it.
#>
param(
    [Parameter(Mandatory)][string]$Root,
    [string]$ServiceName = 'BlazorStocMariaDB',
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'
function Say([string]$text) { Write-Output "[database] $text" }
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
# A service of this name that runs a server of ANOTHER instance (another BlazorStoc on this computer, a development database) is not ours: it is left alone.
if ($service) {
    $imagePath = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue).PathName
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not $imagePath -or ($imagePath.Replace('/', '\') -notmatch [regex]::Escape($rootFull))) {
        Say "the service $ServiceName runs a server that is not in $Root ($imagePath): it is NOT touched."
        $service = $null
        $foreignService = $true
    }
}
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; $service.WaitForStatus('Stopped', '00:02:00') }
    & sc.exe delete $ServiceName | Out-Null
    for ($i = 0; $i -lt 20 -and (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
    Say "service $ServiceName stopped and removed."
}
else {
    # A server started without a service (a test, or an installation made with --no-service): shut down cleanly with the admin tool of ITS distribution (named by basedir in my.ini, else one under the
    # instance folder), then, if the process is still there, stopped.
    $adminCnf = Join-Path $Root 'admin.private.cnf'
    $myIni = Join-Path $Root 'my.ini'
    $candidates = @()
    if (Test-Path $myIni) {
        $basedir = [regex]::Match((Get-Content $myIni -Raw), '(?m)^\s*basedir\s*=\s*(.+?)\s*$').Groups[1].Value
        if ($basedir) { $candidates += (Join-Path $basedir.Replace('/', '\') 'bin\mariadb-admin.exe') }
    }
    $candidates += Get-ChildItem -Path $Root -Directory -Filter 'mariadb-*' -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'bin\mariadb-admin.exe' }
    $admin = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($admin -and (Test-Path $adminCnf)) {
        $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $admin "--defaults-file=$adminCnf" shutdown 2>&1 | Out-Null
        $ErrorActionPreference = $previous
        Start-Sleep -Seconds 3
        Say 'the server of the instance was asked to stop.'
    }
    $rootPattern = [regex]::Escape((Resolve-Path -LiteralPath $Root -ErrorAction SilentlyContinue).Path)
    $running = Get-CimInstance Win32_Process -Filter "Name='mariadbd.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $rootPattern -and ($_.CommandLine.Replace('/', '\') -match $rootPattern) }
    foreach ($process in $running) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue; Say "the server process $($process.ProcessId) of the instance was stopped." }
    if (-not $admin -and -not $running) { Say "there is no service $ServiceName and no server of the instance to stop." }
}
if ($RemoveData) {
    if (Test-Path -LiteralPath $Root) {
        # the files may be released a moment after the service stops
        for ($i = 0; $i -lt 10 -and (Test-Path -LiteralPath $Root); $i++) { Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue; if (Test-Path -LiteralPath $Root) { Start-Sleep -Seconds 1 } }
        if (Test-Path -LiteralPath $Root) { throw "The folder $Root could not be removed completely (a file is in use)." }
        Say "the instance folder $Root was deleted (database and files)."
    }
}
else { Say "the instance folder $Root was kept (the data of the database); a new installation reuses it." }
