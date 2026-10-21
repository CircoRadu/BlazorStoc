<#
.SYNOPSIS
  Makes the database server of a BlazorStoc installation on this server: MariaDB, its Windows service, the accounts, the schema and the private files of the application.
.DESCRIPTION
  Run by "BlazorStoc.Updater init" (the installer BlazorStoc-Actualizare-<version>.exe --init) as an administrator, with the port that the updater found free on this server.
  1. the MariaDB distribution: -MariaBinDir (an existing one), else the folder -Root\mariadb-<version>-winx64, else -MariaZip (a file downloaded beforehand), else the archive
     of archive.mariadb.org (version and SHA-256 fixed in this script; the archive is refused when the hash differs);
  2. the instance in -Root: data directory, my.ini with -Port, the client file of the root account (readable by administrators and SYSTEM only);
  3. the Windows service -ServiceName (virtual account NT SERVICE\<name>, starts with the computer, restarts after a failure; -ControlUser may start and stop it);
     -NoService starts a hidden server instead (checks on a machine without administrator rights);
  4. the database BlazorStoc, the four accounts of Assets\Data\database-accounts.json (-ResourcesDir\database-accounts.json) with generated passwords, the base schema and the
     triggers (the migrations of the application bring the rest when the first version is installed);
  5. the private files: application-connection.private.json, backup-account.private.json and restore-account.private.json in -DataRoot (the application reads them), the file of the
     migration account in -MigratorFile (the updater only). Passwords are never printed. The user "admin" of the application (the administrator of the configuration) has a generated
     password, kept in the first file (Authentication.Password) and shown nowhere; the application has no page that changes it (only editing that file), so it is
     the administrator of last resort: sign in with it once and make the personal administrator accounts in Utilizatori.
  Idempotent: what exists is kept (no password is changed, no data is touched); a second run only makes sure the service runs.
.EXAMPLE
  .\provision-database.ps1 -Root D:\BlazorStoc\mariadb -Port 3307 -DataRoot C:\ProgramData\BlazorStoc -MigratorFile D:\BlazorStoc\private\migration-account.private.json -ResourcesDir D:\BlazorStoc\scripts
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$MigratorFile,
    [Parameter(Mandatory)][string]$ResourcesDir,
    [string]$ServiceName = 'BlazorStocMariaDB',
    [string]$MariaBinDir,
    [string]$MariaZip,
    [string]$ControlUser,
    [switch]$NoService
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$MariaVersion = '11.4.13'
$MariaZipUrl = "https://archive.mariadb.org/mariadb-$MariaVersion/winx64-packages/mariadb-$MariaVersion-winx64.zip"
$MariaZipSha256 = 'D62986D433EEEBFDE218560B276103831604A61E929E87F1A17F5AEBD80257E2'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $ControlUser) { $ControlUser = $identity.Name }
$isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $NoService) { throw 'Administrator rights are needed to make the Windows service of the database server.' }

function Write-Step([string]$Text) { Write-Output "[database] $Text" }
function New-Secret {
    $bytes = New-Object byte[] 24
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    $generator.GetBytes($bytes)
    $generator.Dispose()
    ([Convert]::ToBase64String($bytes)).Replace('+', 'x').Replace('/', 'y').Replace('=', 'z')
}
function Write-Json([string]$Path, $Value) {
    New-Item -ItemType Directory -Force (Split-Path $Path -Parent) | Out-Null
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 6), $utf8NoBom)
}
# Only the administrators, SYSTEM and the account the application runs as may read a file with a password.
function Protect-File([string]$Path) {
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-32-544:(F)' '*S-1-5-18:(F)' "${ControlUser}:(R)" | Out-Null
}
# A native command that prints on stderr is not an error for PowerShell 5.1 under "Stop": its exit code decides.
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $Exe @Arguments 2>&1; return [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output | Out-String) } }
    finally { $ErrorActionPreference = $previous }
}

if ($Port -lt 1024 -or $Port -gt 65535) { throw "Port $Port is not between 1024 and 65535." }
$catalogFile = Join-Path $ResourcesDir 'database-accounts.json'
$schemaFile = Join-Path $ResourcesDir 'schema-mariadb.sql'
$triggersFile = Join-Path $ResourcesDir 'triggers-mariadb.sql'
foreach ($file in $catalogFile, $schemaFile, $triggersFile) { if (-not (Test-Path $file)) { throw "Missing: $file" } }
$catalog = Get-Content $catalogFile -Raw -Encoding UTF8 | ConvertFrom-Json
$acct = @{}
foreach ($entry in $catalog.accounts) { $acct[$entry.key] = $entry }
$accountHost = if ($catalog.host) { $catalog.host } else { '127.0.0.1' }
New-Item -ItemType Directory -Force $Root, $DataRoot | Out-Null

$appFile = Join-Path $DataRoot 'application-connection.private.json'
$backupFile = Join-Path $DataRoot 'backup-account.private.json'
$restoreFile = Join-Path $DataRoot 'restore-account.private.json'
$alreadyDone = (Test-Path $appFile) -and (Test-Path $MigratorFile) -and (Test-Path $backupFile) -and (Test-Path $restoreFile)

# --- 1. Distribution ---------------------------------------------------------------------------------------------------
Write-Step "MariaDB $MariaVersion distribution"
if (-not $MariaBinDir) {
    $MariaBinDir = Join-Path $Root "mariadb-$MariaVersion-winx64\bin"
    if (-not (Test-Path (Join-Path $MariaBinDir 'mariadbd.exe'))) {
        $zip = $MariaZip
        $downloaded = $false
        if (-not $zip) {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $zip = Join-Path ([IO.Path]::GetTempPath()) "mariadb-$MariaVersion-winx64.zip"
            Write-Step "downloading $MariaZipUrl (about 100 MB; use -MariaZip / --maria-zip for a file brought on a stick)"
            Invoke-WebRequest -Uri $MariaZipUrl -OutFile $zip -UseBasicParsing
            $downloaded = $true
            if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $MariaZipSha256) { Remove-Item $zip -Force; throw 'SHA-256 of the downloaded MariaDB archive does not match; refusing to use it.' }
        }
        elseif (-not (Test-Path $zip)) { throw "The archive $zip does not exist." }
        elseif ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $MariaZipSha256) { throw "SHA-256 of $zip is not the one of MariaDB $MariaVersion; refusing to use it." }
        Expand-Archive $zip -DestinationPath $Root -Force
        if ($downloaded) { Remove-Item $zip -Force -ErrorAction SilentlyContinue }
    }
}
$server = Join-Path $MariaBinDir 'mariadbd.exe'
$client = Join-Path $MariaBinDir 'mariadb.exe'
$admin = Join-Path $MariaBinDir 'mariadb-admin.exe'
$dump = Join-Path $MariaBinDir 'mariadb-dump.exe'
foreach ($path in $server, $client, $admin, $dump) { if (-not (Test-Path $path)) { throw "Missing in the distribution: $path" } }

# --- 2. Instance ---------------------------------------------------------------------------------------------------------
Write-Step "instance in $Root (port $Port)"
$dataDir = Join-Path $Root 'data'
$myIni = Join-Path $Root 'my.ini'
$adminCnf = Join-Path $Root 'admin.private.cnf'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and -not $NoService) {
    # A service of this name that belongs to ANOTHER instance (another BlazorStoc, a development database) is never taken over: stop here, nothing was changed.
    $existingPath = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue).PathName
    if (-not $existingPath -or ($existingPath.Replace('/', '\') -notmatch [regex]::Escape([IO.Path]::GetFullPath($Root).TrimEnd('\')))) {
        throw "A Windows service named $ServiceName exists and runs a server that is not in $Root ($existingPath). Another BlazorStoc database is installed on this computer: nothing was changed."
    }
}
if (-not (Test-Path (Join-Path $dataDir 'mysql'))) {
    if (-not $service) {
        $busy = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($busy) { throw "Port $Port is taken by process $($busy.OwningProcess): another port has to be chosen (run the installer again)." }
    }
    $rootPassword = New-Secret
    $install = Invoke-Native (Join-Path $MariaBinDir 'mariadb-install-db.exe') @("--datadir=$dataDir", "--password=$rootPassword", "--port=$Port", '--silent')
    if ($install.Code -ne 0) { throw "mariadb-install-db failed: $($install.Output)" }
    $myIniText = @"
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
log-error=$($Root.Replace('\', '/'))/server-error.log
pid-file=$($Root.Replace('\', '/'))/server.pid
"@
    [IO.File]::WriteAllText($myIni, $myIniText, $utf8NoBom)
    [IO.File]::WriteAllText($adminCnf, "[client]`nhost=127.0.0.1`nport=$Port`nprotocol=tcp`nuser=root`npassword=$rootPassword`ndefault-character-set=utf8mb4`n", $utf8NoBom)
    if ($isAdmin) { Protect-File $adminCnf }
}
elseif (-not (Test-Path $adminCnf)) { throw "$dataDir exists but $adminCnf is missing: the password of the root account is not known." }
elseif ((Get-Content $myIni -Raw) -notmatch "(?m)^\s*port\s*=\s*$Port\s*$") {
    Write-Step "NOTE: the existing instance in $Root uses another port than $Port; its my.ini is the one that counts."
    $Port = [int]([regex]::Match((Get-Content $myIni -Raw), '(?m)^\s*port\s*=\s*(\d+)').Groups[1].Value)
}

function Test-Database {
    return (Invoke-Native $admin @("--defaults-file=$adminCnf", '--connect-timeout=2', 'ping')).Code -eq 0
}
function Wait-Database([int]$Seconds) {
    for ($i = 0; $i -lt ($Seconds * 2); $i++) { if (Test-Database) { return $true }; Start-Sleep -Milliseconds 500 }
    return $false
}

# --- 3. Windows service ----------------------------------------------------------------------------------------------------
if ($NoService) {
    if (-not (Test-Database)) {
        Write-Step 'starting the server as a hidden process (-NoService)'
        Start-Process -FilePath $server -ArgumentList "--defaults-file=`"$myIni`"" -WindowStyle Hidden | Out-Null
    }
}
else {
    if (-not $service) {
        Write-Step "registering the Windows service $ServiceName"
        $install = Invoke-Native $server @('--install', $ServiceName, "--defaults-file=$myIni")
        if ($install.Code -ne 0) { throw "mariadbd --install failed: $($install.Output)" }
        $account = "NT SERVICE\$ServiceName"
        & sc.exe config $ServiceName obj= $account start= auto | Out-Null
        & sc.exe description $ServiceName 'MariaDB server of BlazorStoc (own service name, own my.ini and port).' | Out-Null
        & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
        & sc.exe failureflag $ServiceName 1 | Out-Null
        & icacls.exe $Root /grant "${account}:(OI)(CI)M" /T /C | Out-Null
        # The account the application runs as may only start, stop and query THIS service (Setari -> Baza de date -> Conexiune).
        $sid = ([Security.Principal.NTAccount]$ControlUser).Translate([Security.Principal.SecurityIdentifier]).Value
        $sddl = (& sc.exe sdshow $ServiceName | Where-Object { $_ -match '^D:' } | Select-Object -First 1).Trim()
        if ($sddl -notmatch [regex]::Escape($sid)) {
            $entry = "(A;;CCLCSWRPWPDTLOCRRC;;;$sid)"
            $new = if ($sddl -match '^(D:.*?)(S:.*)$') { $Matches[1] + $entry + $Matches[2] } else { $sddl + $entry }
            & sc.exe sdset $ServiceName $new | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'sc sdset failed: the right to start and stop the service was not granted.' }
        }
        Write-Step "service $ServiceName registered (starts with the computer)"
    }
    if ((Get-Service -Name $ServiceName).Status -ne 'Running') {
        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Running', '00:02:00')
    }
}
if (-not (Wait-Database 90)) { throw "The server does not answer on port $Port. Read $(Join-Path $Root 'server-error.log')." }
Write-Step "the server answers on port $Port"

if ($alreadyDone) {
    Write-Step 'the accounts and the private files exist already: kept as they are.'
    return
}

# --- 4. Database, accounts, schema ----------------------------------------------------------------------------------------------
$pw = @{}
foreach ($entry in $catalog.accounts) { $pw[$entry.key] = New-Secret }
$pw.admin = New-Secret
$sql = "CREATE DATABASE IF NOT EXISTS ``BlazorStoc`` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin;`n"
foreach ($entry in $catalog.accounts) {
    $who = "'$($entry.user)'@'$accountHost'"
    $sql += "CREATE USER IF NOT EXISTS $who IDENTIFIED BY '$($pw[$entry.key])' REQUIRE SSL;`nALTER USER $who IDENTIFIED BY '$($pw[$entry.key])';`n"
    foreach ($grant in $entry.grants) {
        $schema = $grant.database.Replace('_', '\_')
        $sql += "GRANT $($grant.privileges -join ', ') ON ``$schema``.* TO $who;`n"
    }
}
$sql += "FLUSH PRIVILEGES;`n"
$sqlFile = Join-Path $Root 'tmp-provision.sql'   # passwords inside: deleted right away
try {
    [IO.File]::WriteAllText($sqlFile, $sql, $utf8NoBom)
    $run = Invoke-Native $client @("--defaults-extra-file=$adminCnf", '--default-character-set=utf8mb4', '--batch', "--execute=source $($sqlFile.Replace('\', '/'))")
    if ($run.Code -ne 0) { throw "Creating the accounts failed: $($run.Output)" }
}
finally { Remove-Item $sqlFile -Force -ErrorAction SilentlyContinue }
Write-Step 'database BlazorStoc and its accounts made'

$tables = (Invoke-Native $client @("--defaults-extra-file=$adminCnf", '-N', '-e', "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='BlazorStoc'")).Output.Trim()
if ([int]$tables -eq 0) {
    foreach ($file in $schemaFile, $triggersFile) {
        $run = Invoke-Native $client @("--defaults-extra-file=$adminCnf", '--default-character-set=utf8mb4', '--batch', '--database=BlazorStoc', "--execute=source $($file.Replace('\', '/'))")
        if ($run.Code -ne 0) { throw "Loading $file failed: $($run.Output)" }
    }
    Write-Step 'base schema and triggers loaded (the migrations of the first version follow)'
}

# --- 5. Private files ----------------------------------------------------------------------------------------------------------
$assets = Join-Path $DataRoot 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
Write-Json $appFile ([ordered]@{
        Database       = [ordered]@{
            Host = '127.0.0.1'; Port = $Port; Name = 'BlazorStoc'; User = $acct.app.user; Password = $pw.app; SslMode = 'Required'
            MariaDumpExecutablePath = $dump; MariaClientExecutablePath = $client; MariaAssetsRoot = $assets
        }
        App            = [ordered]@{ DataProtectionPath = (Join-Path $assets 'data-protection-keys') }
        Authentication = [ordered]@{ Username = 'admin'; Password = $pw.admin }
    })
Write-Json $MigratorFile ([ordered]@{ Database = [ordered]@{ Host = '127.0.0.1'; Port = $Port; Name = 'BlazorStoc'; User = $acct.migrator.user; Password = $pw.migrator; SslMode = 'Required' } })
Write-Json $backupFile ([ordered]@{ Database = [ordered]@{ User = $acct.backup.user; Password = $pw.backup } })
Write-Json $restoreFile ([ordered]@{ Database = [ordered]@{ User = $acct.restore.user; Password = $pw.restore } })
if ($isAdmin) { foreach ($file in $appFile, $backupFile, $restoreFile) { Protect-File $file } }
Write-Step "private files written in $DataRoot (passwords are inside them, not printed); the migration account in $MigratorFile"
Write-Step "ready: MariaDB answers on 127.0.0.1:$Port"
Write-Step "The installer asks for the first administrator account of the application and makes it after the first version is installed."
Write-Step "Last resort sign-in (configuration): user admin, generated password, NOT shown. Read it, in an administrator console, with:"
Write-Step "  (Get-Content '$appFile' -Raw | ConvertFrom-Json).Authentication.Password"
