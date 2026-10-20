<#
.SYNOPSIS
  Puts an existing installation on the predefined database accounts (Assets\Data\database-accounts.json): the accounts of the earlier versions (blazorstoc_dev,
  blazorstoc_migrator, blazorstoc_backup, blazorstoc_restore) are renamed on the server, and the private files that name them are updated. Passwords are not touched.
.DESCRIPTION
  Uses the account that administers the server (admin.private.cnf in -MariaRoot). A rename keeps the password and the rights of the account, so nothing else changes and no
  default password comes in. Run it with the application stopped (tools\supervisor.ps1 -Action stop -Port 5087); the application finds the new names in the private files.
  Safe to run again: an account that already has the new name is left as it is, an account that does not exist is only reported (use Setari -> Baza de date -> Resetează conturile).
.EXAMPLE
  .\tools\upgrade-database-accounts.ps1 -DryRun
  .\tools\upgrade-database-accounts.ps1
#>
[CmdletBinding()]
param(
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$SecretsDir,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsDir) { $SecretsDir = Join-Path $repo 'local-secrets' }
$catalog = Get-Content (Join-Path $repo 'Assets\Data\database-accounts.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$hostName = if ($catalog.host) { $catalog.host } else { '127.0.0.1' }
$distribution = Get-ChildItem $MariaRoot -Directory -Filter 'mariadb-*-winx64' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $distribution) { throw "No MariaDB distribution under $MariaRoot." }
$client = Join-Path $distribution.FullName 'bin\mariadb.exe'
$cnf = Join-Path $MariaRoot 'admin.private.cnf'
if (-not (Test-Path $cnf)) { throw "$cnf not found; the account that administers the server is needed." }

function Invoke-Scalar([string]$Sql) { (& $client "--defaults-extra-file=$cnf" -N -B -e $Sql | Select-Object -First 1) }
function Exists([string]$User) { [int](Invoke-Scalar "SELECT COUNT(*) FROM mysql.user WHERE User='$User' AND Host='$hostName'") -gt 0 }

foreach ($account in $catalog.accounts) {
    $old = $account.legacyUser; $new = $account.user
    if (Exists $new) { Write-Output "$($account.key): $new already exists."; continue }
    if (-not (Exists $old)) { Write-Output "$($account.key): neither $new nor $old exists on the server; create it with Setari -> Baza de date -> Reseteaza conturile."; continue }
    if ($DryRun) { Write-Output "$($account.key): would rename $old to $new."; continue }
    & $client "--defaults-extra-file=$cnf" -e "RENAME USER '$old'@'$hostName' TO '$new'@'$hostName'; FLUSH PRIVILEGES;"
    if ($LASTEXITCODE -ne 0) { throw "Renaming $old failed." }
    Write-Output "$($account.key): renamed $old to $new (password and rights kept)."
}

# The private files that name an account (only the name changes; the rest of each file is written back as it was).
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
foreach ($file in 'application-connection.private.json', 'migration-account.private.json', 'backup-account.private.json', 'restore-account.private.json') {
    $path = Join-Path $SecretsDir $file
    if (-not (Test-Path $path)) { continue }
    $json = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $changed = $false
    foreach ($account in $catalog.accounts) {
        if ($json.Database -and $json.Database.User -eq $account.legacyUser) { $json.Database.User = $account.user; $changed = $true }
    }
    if ($changed) {
        if ($DryRun) { Write-Output "$file : would name the new account."; continue }
        [IO.File]::WriteAllText($path, ($json | ConvertTo-Json -Depth 10), $utf8NoBom)
        Write-Output "$file : now names the predefined account."
    }
}
exit 0
