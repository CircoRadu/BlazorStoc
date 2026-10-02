<#
.SYNOPSIS
  Sets up a BlazorStoc development machine: local MariaDB 11.4 instance, database accounts, schema, private config files.
.DESCRIPTION
  Idempotent in the sense that finished steps are skipped (distribution present, data directory initialised, secrets
  written). It never overwrites existing secret files and never prints passwords. To start over, delete the MariaDB root
  directory and the generated *.private.json files first. See docs/SETUP_DEZVOLTARE.md.
.PARAMETER MariaRoot
  Where the MariaDB distribution, data directory, my.ini and admin.private.cnf live (outside the repository).
.PARAMETER SecretsDir
  Where the private JSON files are written. Default: <repo>\local-secrets (ignored by Git).
.PARAMETER MariaBinDir
  Use an existing MariaDB bin directory instead of downloading the distribution.
.EXAMPLE
  .\tools\setup-dev-environment.ps1
  .\tools\setup-dev-environment.ps1 -MariaRoot D:\Dev\BlazorStoc-MariaDB
#>
[CmdletBinding()]
param(
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [int]$Port = 3307,
    [string]$SecretsDir,
    [string]$MariaBinDir,
    [switch]$SkipTestDatabase
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsDir) { $SecretsDir = Join-Path $repo 'local-secrets' }
$MariaVersion = '11.4.13'
$MariaZipUrl = "https://archive.mariadb.org/mariadb-$MariaVersion/winx64-packages/mariadb-$MariaVersion-winx64.zip"
$MariaZipSha256 = 'D62986D433EEEBFDE218560B276103831604A61E929E87F1A17F5AEBD80257E2'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Write-Step([string]$Text) { Write-Host "== $Text" -ForegroundColor Cyan }
function New-Secret {
    $bytes = New-Object byte[] 24
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    $generator.GetBytes($bytes)
    $generator.Dispose()
    ([Convert]::ToBase64String($bytes)).Replace('+', 'x').Replace('/', 'y').Replace('=', 'z')
}
function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 6), $utf8NoBom)
}

# --- 1. Prerequisites -------------------------------------------------------------------------------------------------
Write-Step 'Prerequisites'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not ((& dotnet --list-sdks) -match '^9\.')) {
    throw '.NET 9 SDK is missing. Install it: winget install Microsoft.DotNet.SDK.9'
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is missing. Install it: winget install Git.Git' }
New-Item -ItemType Directory -Force $MariaRoot, $SecretsDir | Out-Null

# --- 2. MariaDB distribution ------------------------------------------------------------------------------------------
Write-Step "MariaDB $MariaVersion distribution"
if (-not $MariaBinDir) {
    $MariaBinDir = Join-Path $MariaRoot "mariadb-$MariaVersion-winx64\bin"
    if (-not (Test-Path (Join-Path $MariaBinDir 'mariadbd.exe'))) {
        $zip = Join-Path ([IO.Path]::GetTempPath()) "mariadb-$MariaVersion-winx64.zip"
        Write-Host "Downloading $MariaZipUrl"
        Invoke-WebRequest -Uri $MariaZipUrl -OutFile $zip -UseBasicParsing
        if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $MariaZipSha256) { throw 'SHA-256 of the downloaded MariaDB archive does not match; refusing to use it.' }
        Expand-Archive $zip -DestinationPath $MariaRoot -Force
        Remove-Item $zip
    }
}
$client = Join-Path $MariaBinDir 'mariadb.exe'
if (-not (Test-Path $client)) { throw "mariadb.exe not found in $MariaBinDir" }

# --- 3. Data directory, my.ini, admin client file ---------------------------------------------------------------------
Write-Step 'Instance (data directory, my.ini, root account)'
$dataDir = Join-Path $MariaRoot 'data'
$adminCnf = Join-Path $MariaRoot 'admin.private.cnf'
if (-not (Test-Path (Join-Path $dataDir 'mysql'))) {
    $rootPassword = New-Secret
    $installLog = Join-Path $MariaRoot 'install-db.log'
    & (Join-Path $MariaBinDir 'mariadb-install-db.exe') "--datadir=$dataDir" "--password=$rootPassword" "--port=$Port" --silent *> $installLog
    if ($LASTEXITCODE -ne 0) { throw "mariadb-install-db failed; see $installLog" }
    Remove-Item $installLog -ErrorAction SilentlyContinue
    $myIni = @"
[mysqld]
basedir=$((Split-Path $MariaBinDir -Parent).Replace('\', '/'))
datadir=$($dataDir.Replace('\', '/'))
port=$Port
bind-address=127.0.0.1
skip-name-resolve
character-set-server=utf8mb4
collation-server=utf8mb4_bin
default-storage-engine=InnoDB
default-time-zone=+00:00
sql-mode=STRICT_ALL_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION
event-scheduler=OFF
local-infile=0
innodb-flush-log-at-trx-commit=1
max-allowed-packet=64M
log-error=$($MariaRoot.Replace('\', '/'))/server-error.log
pid-file=$($MariaRoot.Replace('\', '/'))/server.pid
"@
    [IO.File]::WriteAllText((Join-Path $MariaRoot 'my.ini'), $myIni, $utf8NoBom)
    $cnf = "[client]`nhost=127.0.0.1`nport=$Port`nprotocol=tcp`nuser=root`npassword=$rootPassword`ndefault-character-set=utf8mb4`n"
    [IO.File]::WriteAllText($adminCnf, $cnf, $utf8NoBom)
}
elseif (-not (Test-Path $adminCnf)) { throw "$dataDir exists but $adminCnf is missing; cannot connect as root." }

& (Join-Path $PSScriptRoot 'dev-mariadb.ps1') -Action start -Root $MariaRoot -Port $Port -BinDir $MariaBinDir

function Invoke-SqlText([string]$Sql) {
    $file = Join-Path $SecretsDir 'tmp-setup.sql'   # passwords inside: never on the command line, deleted right away
    [IO.File]::WriteAllText($file, $Sql, $utf8NoBom)
    try {
        & $client "--defaults-extra-file=$adminCnf" --default-character-set=utf8mb4 --batch "--execute=source $($file.Replace('\', '/'))"
        if ($LASTEXITCODE -ne 0) { throw 'SQL execution failed.' }
    }
    finally { Remove-Item $file -ErrorAction SilentlyContinue }
}
function Invoke-SqlFile([string]$Path, [string]$Database) {
    & $client "--defaults-extra-file=$adminCnf" --default-character-set=utf8mb4 --batch "--database=$Database" "--execute=source $($Path.Replace('\', '/'))"
    if ($LASTEXITCODE -ne 0) { throw "Loading $Path failed." }
}

# --- 4. Accounts and databases ----------------------------------------------------------------------------------------
Write-Step 'Databases and accounts'
$names = @{
    app      = 'application-connection.private.json'
    migrator = 'migration-account.private.json'
    backup   = 'backup-account.private.json'
    restore  = 'restore-account.private.json'
    test     = 'test-database.private.json'
}
$existing = $names.Values | Where-Object { Test-Path (Join-Path $SecretsDir $_) }
if ($existing.Count -gt 0 -and $existing.Count -lt $names.Count - $(if ($SkipTestDatabase) { 1 } else { 0 })) {
    throw "Some private files already exist in $SecretsDir ($($existing -join ', ')) but not all. Remove them (and the MariaDB root) to start over, or complete them by hand."
}
if ($existing.Count -eq 0) {
    $pw = @{ app = New-Secret; migrator = New-Secret; backup = New-Secret; restore = New-Secret; test = New-Secret; admin = New-Secret }
    $sql = @"
CREATE DATABASE IF NOT EXISTS ``BlazorStoc`` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin;
CREATE USER IF NOT EXISTS 'blazorstoc_dev'@'127.0.0.1' IDENTIFIED BY '$($pw.app)' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE ON ``BlazorStoc``.* TO 'blazorstoc_dev'@'127.0.0.1';
CREATE USER IF NOT EXISTS 'blazorstoc_migrator'@'127.0.0.1' IDENTIFIED BY '$($pw.migrator)' REQUIRE SSL;
GRANT CREATE, DROP, REFERENCES, INDEX, ALTER, CREATE VIEW, TRIGGER ON ``BlazorStoc``.* TO 'blazorstoc_migrator'@'127.0.0.1';
CREATE USER IF NOT EXISTS 'blazorstoc_backup'@'127.0.0.1' IDENTIFIED BY '$($pw.backup)' REQUIRE SSL;
GRANT SELECT, SHOW VIEW, TRIGGER, LOCK TABLES ON ``BlazorStoc``.* TO 'blazorstoc_backup'@'127.0.0.1';
CREATE USER IF NOT EXISTS 'blazorstoc_restore'@'127.0.0.1' IDENTIFIED BY '$($pw.restore)' REQUIRE SSL;
GRANT ALL PRIVILEGES ON ``BlazorStoc\_bak``.* TO 'blazorstoc_restore'@'127.0.0.1';
GRANT ALL PRIVILEGES ON ``BlazorStoc\_old``.* TO 'blazorstoc_restore'@'127.0.0.1';
GRANT SELECT, ALTER, DROP, CREATE, INSERT, TRIGGER ON ``BlazorStoc``.* TO 'blazorstoc_restore'@'127.0.0.1';
"@
    if (-not $SkipTestDatabase) {
        $sql += @"
CREATE DATABASE IF NOT EXISTS ``blazorstoc_test`` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin;
CREATE USER IF NOT EXISTS 'blazorstoc_test_app'@'127.0.0.1' IDENTIFIED BY '$($pw.test)' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE ON ``blazorstoc_test``.* TO 'blazorstoc_test_app'@'127.0.0.1';
GRANT CREATE, DROP, REFERENCES, INDEX, ALTER, CREATE VIEW, TRIGGER ON ``blazorstoc\_test``.* TO 'blazorstoc_migrator'@'127.0.0.1';
"@
    }
    Invoke-SqlText ($sql + "FLUSH PRIVILEGES;`n")

    $assets = Join-Path $MariaRoot 'assets'
    Write-Json (Join-Path $SecretsDir $names.app) ([ordered]@{
            Database       = [ordered]@{
                Host = '127.0.0.1'; Port = $Port; Name = 'BlazorStoc'; User = 'blazorstoc_dev'; Password = $pw.app; SslMode = 'Required'
                MariaDumpExecutablePath = (Join-Path $MariaBinDir 'mariadb-dump.exe'); MariaClientExecutablePath = $client; MariaAssetsRoot = $assets
            }
            App            = [ordered]@{ DataProtectionPath = (Join-Path $assets 'data-protection-keys') }
            Authentication = [ordered]@{ Username = 'admin'; Password = $pw.admin }
        })
    $account = { param($user, $password) [ordered]@{ Database = [ordered]@{ Host = '127.0.0.1'; Port = $Port; Name = 'BlazorStoc'; User = $user; Password = $password; SslMode = 'Required' } } }
    Write-Json (Join-Path $SecretsDir $names.migrator) (& $account 'blazorstoc_migrator' $pw.migrator)
    Write-Json (Join-Path $SecretsDir $names.backup) ([ordered]@{ Database = [ordered]@{ User = 'blazorstoc_backup'; Password = $pw.backup } })
    Write-Json (Join-Path $SecretsDir $names.restore) ([ordered]@{ Database = [ordered]@{ User = 'blazorstoc_restore'; Password = $pw.restore } })
    if (-not $SkipTestDatabase) {
        Write-Json (Join-Path $SecretsDir $names.test) ([ordered]@{
                Database = [ordered]@{ Host = '127.0.0.1'; Port = $Port; Name = 'blazorstoc_test'; ExpectedName = 'blazorstoc_test'; User = 'blazorstoc_test_app'; Password = $pw.test; SslMode = 'Required' }
            })
    }
    Write-Host "Private files written to $SecretsDir (passwords are inside them; they are not printed)."
}
else { Write-Host 'Private files already exist; accounts left untouched.' }

# --- 5. Schema: base DDL + triggers, then the application's own migrations --------------------------------------------
function Install-Schema([string]$Database, [string]$ConfigPath) {
    $tables = & $client "--defaults-extra-file=$adminCnf" -N -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$Database'"
    if ([int]$tables -eq 0) {
        Invoke-SqlFile (Join-Path $repo 'database\mariadb\schema-mariadb.sql') $Database
        Invoke-SqlFile (Join-Path $repo 'database\mariadb\triggers-mariadb.sql') $Database
    }
    $env:Database__PrivateConfigPath = $ConfigPath
    $env:Database__Name = $Database
    $env:Authentication__Password = New-Secret   # startup validation needs one; the test config has none, the real one overrides it
    Push-Location $repo
    try {
        & dotnet run --project BlazorStoc.csproj -c Release --no-launch-profile -- --migrate-schema
        if ($LASTEXITCODE -ne 0) { throw "Schema migration failed for $Database." }
    }
    finally { Pop-Location; Remove-Item Env:\Database__PrivateConfigPath, Env:\Database__Name, Env:\Authentication__Password -ErrorAction SilentlyContinue }
}
Write-Step 'Schema BlazorStoc'
Install-Schema 'BlazorStoc' (Join-Path $SecretsDir $names.app)
if (-not $SkipTestDatabase) {
    Write-Step 'Schema blazorstoc_test'
    Install-Schema 'blazorstoc_test' (Join-Path $SecretsDir $names.test)
}

Write-Step 'Done'
Write-Host "MariaDB: 127.0.0.1:$Port (start/stop with tools\dev-mariadb.ps1). Config: $SecretsDir"
Write-Host 'Next: .\tools\start-preview.ps1  (login: user "admin", password = Authentication.Password from application-connection.private.json)'
