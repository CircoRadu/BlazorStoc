<#
.SYNOPSIS
  Starts, stops or checks the local MariaDB instance used for BlazorStoc development (not a Windows service).
.EXAMPLE
  .\tools\dev-mariadb.ps1 -Action start
  .\tools\dev-mariadb.ps1 -Action status -Root D:\Dev\BlazorStoc-MariaDB -Port 3307
#>
[CmdletBinding()]
param(
    [ValidateSet('start', 'status', 'stop')][string]$Action = 'status',
    [string]$Root = 'C:\Dev\BlazorStoc-MariaDB',
    [int]$Port = 3307,
    [string]$BinDir
)
$ErrorActionPreference = 'Stop'

if (-not $BinDir) {
    $distribution = Get-ChildItem $Root -Directory -Filter 'mariadb-*-winx64' -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $distribution) { throw "No MariaDB distribution (mariadb-*-winx64) under $Root. Run tools\setup-dev-environment.ps1 first." }
    $BinDir = Join-Path $distribution.FullName 'bin'
}
$adminCnf = Join-Path $Root 'admin.private.cnf'

function Test-Listening {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        return $connect.AsyncWaitHandle.WaitOne(500) -and $client.Connected
    }
    catch { return $false }
    finally { $client.Close() }
}

switch ($Action) {
    'status' {
        if (-not (Test-Listening)) { Write-Output "MariaDB is not running on 127.0.0.1:$Port."; exit 1 }
        & (Join-Path $BinDir 'mariadb.exe') "--defaults-extra-file=$adminCnf" --batch -e 'SELECT VERSION() AS version, @@port AS port, @@bind_address AS bind_address;'
        exit $LASTEXITCODE
    }
    'stop' {
        if (Test-Listening) {
            & (Join-Path $BinDir 'mariadb-admin.exe') "--defaults-extra-file=$adminCnf" shutdown
            if ($LASTEXITCODE -ne 0) { throw 'Graceful shutdown failed.' }
        }
        Write-Output 'MariaDB is stopped.'
    }
    'start' {
        if (Test-Listening) { Write-Output "MariaDB is already running on 127.0.0.1:$Port."; return }
        $process = Start-Process -FilePath (Join-Path $BinDir 'mariadbd.exe') -ArgumentList "--defaults-file=`"$(Join-Path $Root 'my.ini')`"" -WindowStyle Hidden -PassThru
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            Start-Sleep -Milliseconds 500
            if (Test-Listening) { Write-Output "MariaDB is ready on 127.0.0.1:$Port."; return }
            if ($process.HasExited) { throw "The server exited. Inspect $(Join-Path $Root 'server-error.log')." }
        }
        throw "Startup timeout; inspect $(Join-Path $Root 'server-error.log')."
    }
}
