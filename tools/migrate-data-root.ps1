<#
.SYNOPSIS
  Moves (copies) the application data from the per-user folder to the folder of the machine, %ProgramData%\BlazorStoc, so that the application, its worker
  processes, the scripts and a Windows service all use the same files.
.DESCRIPTION
  Copies assets (packages, pictures, files, the keys that protect the saved passwords, run state), the override file of Setari -> Baza de date and the
  private account files. It also copies admin.private.cnf and connection.private.json (the accounts of the server: kept with the other secrets, protected by the ACL below), and it never
  deletes or changes the source: after checking, rename the old folder yourself (-RenameOld does it). Several sources can be merged (-AlsoFrom): the first source is
  copied as it is, the others only add what is missing (a package or a key that the first one does not have), so nothing a second instance made is lost.
  The keys of every source are kept in the target (the key ring reads all of them), because a password saved with any of them must stay readable.
  The folder gets an ACL: SYSTEM and Administrators full control, -GrantUser (default: the current user) modify, nobody else.
  Run with the applications stopped (tools\supervisor.ps1 -Action stop -Port 5087); after that the applications find the new folder by themselves.
.EXAMPLE
  .\tools\migrate-data-root.ps1 -DryRun
  .\tools\migrate-data-root.ps1 -RenameOld
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $env:LOCALAPPDATA 'BlazorStoc-MariaDB'),
    [string[]]$AlsoFrom,
    [string]$Target = (Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'BlazorStoc'),
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$GrantUser = "$env:USERDOMAIN\$env:USERNAME",
    [switch]$RenameOld,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('AlsoFrom')) {
    # The copy of the folder that the processes started from the Claude desktop app (an MSIX package) write to instead of the real one, when it exists.
    $package = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Directory -Filter 'Claude_*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($package) { $virtual = Join-Path $package.FullName 'LocalCache\Local\BlazorStoc-MariaDB'; if (Test-Path $virtual) { $AlsoFrom = @($virtual) } }
}
if (-not (Test-Path $Source)) { throw "The source folder $Source does not exist; nothing to move." }
$files = @('database-connection.override.json', 'application-connection.private.json', 'migration-account.private.json', 'backup-account.private.json', 'restore-account.private.json', 'admin.private.cnf', 'connection.private.json')

function Copy-Part([string]$From, [bool]$OnlyMissing) {
    $options = @('/E', '/COPY:DAT', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($OnlyMissing) { $options += @('/XC', '/XN', '/XO') }
    if ($DryRun) { $options += '/L' }
    $assets = Join-Path $From 'assets'
    if (Test-Path $assets) { & robocopy $assets (Join-Path $Target 'assets') @options | Out-Null; if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $assets (code $LASTEXITCODE)." } }
    foreach ($name in $files) {
        $file = Join-Path $From $name
        if (-not (Test-Path $file)) { continue }
        $destination = Join-Path $Target $name
        if ($OnlyMissing -and (Test-Path $destination)) { continue }
        if ($DryRun) { Write-Output "  would copy $name"; continue }
        Copy-Item $file $destination -Force
    }
}

Write-Output "Source: $Source"
foreach ($extra in $AlsoFrom) { Write-Output "Also from (only what is missing): $extra" }
Write-Output "Target: $Target$(if ($DryRun) { '  (dry run, nothing is written)' })"
if (-not $DryRun) { New-Item -ItemType Directory -Force $Target | Out-Null }
Copy-Part $Source $false
foreach ($extra in $AlsoFrom) { if (Test-Path $extra) { Copy-Part $extra $true } }

# The account that administers the server is the one of the instance that runs (admin.private.cnf in the MariaDB root): a copy found in the old per-user folder can belong to
# an earlier instance, so the file of the running instance wins.
$active = Join-Path $MariaRoot 'admin.private.cnf'
if (Test-Path $active) {
    if ($DryRun) { Write-Output "  would copy $active as admin.private.cnf" }
    else { Copy-Item $active (Join-Path $Target 'admin.private.cnf') -Force; Write-Output 'admin.private.cnf: taken from the running MariaDB instance.' }
}

if (-not $DryRun) {
    & icacls $Target /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "${GrantUser}:(OI)(CI)M" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Setting the permissions of the target folder failed.' }
    $count = (Get-ChildItem $Target -Recurse -File).Count
    $packages = (Get-ChildItem (Join-Path $Target 'assets\database-backups') -File -Filter '*.zip' -ErrorAction SilentlyContinue).Count
    $keys = (Get-ChildItem (Join-Path $Target 'assets\data-protection-keys') -File -ErrorAction SilentlyContinue).Count
    Write-Output "Done: $count files in $Target ($packages backup packages, $keys key files)."
    if ($RenameOld) {
        $renamed = "$Source.moved-$(Get-Date -Format 'yyyyMMdd')"
        Rename-Item $Source $renamed
        Write-Output "The old folder was renamed to $renamed (delete it when you are sure)."
    }
    else { Write-Output "The old folder was left as it was; the applications use $Target as soon as it has its assets folder." }
}

exit 0   # robocopy ends with 1-7 when it copied something, which is not an error
