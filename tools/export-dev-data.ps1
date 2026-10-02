<#
.SYNOPSIS
  Refreshes database\dev-data (the data of the development database, WITHOUT the audit journal and the change events) and the files it refers to,
  so that a push carries the current data. Run it before every push; tools\setup-dev-environment.ps1 loads it on a new computer.
.PARAMETER MariaRoot
  The MariaDB root with admin.private.cnf and the distribution (default C:\Dev\BlazorStoc-MariaDB).
.PARAMETER Database
  The database to export (default BlazorStoc; on Windows MariaDB may have created it in lower case, the name is matched either way).
#>
[CmdletBinding()]
param(
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$Database
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bin = Get-ChildItem $MariaRoot -Directory -Filter 'mariadb-*-winx64' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $bin) { throw "No MariaDB distribution under $MariaRoot." }
$binDir = Join-Path $bin.FullName 'bin'
$adminCnf = Join-Path $MariaRoot 'admin.private.cnf'
if (-not (Test-Path $adminCnf)) { throw "$adminCnf is missing." }
if (-not $Database) {
    $found = & (Join-Path $binDir 'mariadb.exe') "--defaults-extra-file=$adminCnf" -N -e "SELECT schema_name FROM information_schema.schemata WHERE LOWER(schema_name)='blazorstoc'"
    $Database = ($found | Select-Object -First 1)
    if (-not $Database) { throw 'The database BlazorStoc was not found.' }
}
$target = Join-Path $repo 'database\dev-data'
New-Item -ItemType Directory -Force $target | Out-Null
$file = Join-Path $target 'BlazorStoc_data.sql'
$arguments = @("--defaults-extra-file=$adminCnf", '--no-create-info', '--skip-triggers', '--replace', '--complete-insert', '--hex-blob', '--single-transaction',
    '--skip-comments', '--default-character-set=utf8mb4', "--ignore-table=$Database.audit_events", "--ignore-table=$Database.change_events", "--result-file=$file", $Database)
& (Join-Path $binDir 'mariadb-dump.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'mariadb-dump failed.' }
Write-Host "Data exported to $file (audit_events and change_events left out)."

# The files the data refers to (not the backups): archive files, photos, Data Protection keys.
$assetsRoot = Join-Path $env:LOCALAPPDATA 'BlazorStoc-MariaDB\assets'
$config = Join-Path $repo 'local-secrets\application-connection.private.json'
if (Test-Path $config) {
    $configured = (Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json).Database.MariaAssetsRoot
    if ($configured) { $assetsRoot = $configured }
}
$assetsTarget = Join-Path $target 'assets'
if (Test-Path $assetsRoot) {
    robocopy $assetsRoot $assetsTarget /MIR /XD database-backups /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'robocopy failed.' }
    $global:LASTEXITCODE = 0
    Write-Host "Files copied from $assetsRoot to $assetsTarget."
}
