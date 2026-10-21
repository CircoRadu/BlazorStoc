<#
.SYNOPSIS
  Removes BlazorStoc completely from this server: the application, its supervisor and tasks, the firewall rule, the database server (Windows service) and, unless -KeepData, the database and all the data.
.DESCRIPTION
  Stand-alone: it needs nothing from the installer and works on any installation made by the installer, old or new. Run it in a console started AS ADMINISTRATOR.
  What it finds and removes, in this order (everything is looked up on this server, nothing is typed in):
    1. the installation folder: -InstallRoot, else the one registered in HKLM\SOFTWARE\BlazorStoc (InstallRoot), else C:\BlazorStoc;
    2. the scheduled tasks BlazorStocSupervisor-* and BlazorStocUpdater-* of that installation, the processes of the supervisor and of the application started from it;
    3. the firewall rules "BlazorStoc*";
    4. the database server of the installation: the Windows service named in updater\settings.json (BlazorStocMariaDB) - only when its program is inside the installation - and any server
       process started from the instance folder;
    5. the registry entry, the installation folder and, unless -KeepData, the data of the application (the files and keys in the data root named by settings.json, App__DataRoot or
       C:\ProgramData\BlazorStoc: assets, private files of the accounts, logs). The database is inside the installation folder (mariadb\): with -KeepData that folder stays.
  It lists what it will do and asks for the word STERGE (-Yes skips the question). -DryRun lists and changes nothing. Deleted data cannot be recovered: make a backup first
  (the application, Setari -> Baza de date -> copie de siguranta) or use the installer with --uninstall --remove-data --backup-to <folder>.
.EXAMPLE
  .\remove-blazorstoc.ps1 -DryRun
  .\remove-blazorstoc.ps1
  .\remove-blazorstoc.ps1 -KeepData
  .\remove-blazorstoc.ps1 -InstallRoot D:\BlazorStoc -Yes
#>
[CmdletBinding()]
param(
    [string]$InstallRoot,
    [switch]$KeepData,
    [switch]$Yes,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script in a console started as administrator.' }
function Say([string]$text) { Write-Output $text }

# ---- what is installed ----
$registryKey = 'HKLM:\SOFTWARE\BlazorStoc'
$registered = (Get-ItemProperty $registryKey -ErrorAction SilentlyContinue).InstallRoot
if (-not $InstallRoot) { $InstallRoot = if ($registered) { $registered } elseif (Test-Path 'C:\BlazorStoc\updater\settings.json') { 'C:\BlazorStoc' } else { $null } }
if (-not $InstallRoot -or -not (Test-Path -LiteralPath $InstallRoot)) { throw "No installation was found (nothing in the registry, no C:\BlazorStoc). Give the folder with -InstallRoot <folder>." }
$root = (Resolve-Path -LiteralPath $InstallRoot).Path.TrimEnd('\')
$settings = $null
$settingsFile = Join-Path $root 'updater\settings.json'
if (Test-Path -LiteralPath $settingsFile) { try { $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json } catch { Say "Note: $settingsFile could not be read; the usual names are used." } }
$serviceName = if ($settings.mariaServiceName) { $settings.mariaServiceName } else { 'BlazorStocMariaDB' }
$mariaRoot = if ($settings.mariaRoot) { $settings.mariaRoot.TrimEnd('\') } else { Join-Path $root 'mariadb' }
$dataRoot = $null
if ($settings.environment -and $settings.environment.App__DataRoot) { $dataRoot = $settings.environment.App__DataRoot }
elseif ($env:App__DataRoot) { $dataRoot = $env:App__DataRoot }
else { $dataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'BlazorStoc' }
$rootPattern = [regex]::Escape($root)

# this console and the ones above it are never touched, even when the script was copied into the installation folder
$self = @($PID)
for ($next = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId; $next -and $self.Count -lt 8; $next = (Get-CimInstance Win32_Process -Filter "ProcessId=$next" -ErrorAction SilentlyContinue).ParentProcessId) { $self += $next }
function Own-Processes { Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $self -notcontains $_.ProcessId -and ($_.CommandLine.Replace('/', '\') -match $rootPattern) } }
$tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'BlazorStocSupervisor-*' -or $_.TaskName -like 'BlazorStocUpdater-*' } |
    Where-Object { ($_.Actions | ForEach-Object { "$($_.Arguments) $($_.Execute)" }) -match $rootPattern })
$rules = @(Get-NetFirewallRule -DisplayName 'BlazorStoc*' -ErrorAction SilentlyContinue)
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
$serviceIsOurs = $service -and ($service.PathName.Replace('/', '\') -match [regex]::Escape($mariaRoot) -or $service.PathName.Replace('/', '\') -match $rootPattern -or $service.PathName.Replace('/', '\') -match [regex]::Escape((Join-Path $mariaRoot 'my.ini')))
$running = @(Own-Processes)

Say "BlazorStoc on this server"
Say "  installation folder : $root"
Say "  database instance   : $mariaRoot"
Say "  data of the app     : $dataRoot"
Say "  scheduled tasks     : $(if ($tasks) { ($tasks.TaskName -join ', ') } else { '(none)' })"
Say "  firewall rules      : $(if ($rules) { ($rules.DisplayName -join ', ') } else { '(none)' })"
Say "  database service    : $(if ($service) { "$serviceName ($($service.State))$(if (-not $serviceIsOurs) { ' - its program is NOT in this installation: it will be left alone' })" } else { '(none)' })"
Say "  running processes   : $($running.Count) started from the installation"
Say ""
Say $(if ($KeepData) { "WILL DO: stop and remove everything above; the database folder $mariaRoot and the data of the application ($dataRoot) are KEPT." } else { "WILL DO: stop and remove everything above AND DELETE THE DATABASE AND ALL THE DATA of the application. This cannot be undone." })
if ($DryRun) { Say 'Dry run: nothing was changed.'; return }
if (-not $Yes) {
    $answer = Read-Host 'Type STERGE and Enter to go on, anything else cancels'
    if ($answer -cne 'STERGE') { Say 'Cancelled; nothing was changed.'; return }
}

# ---- 1. tasks and processes ----
foreach ($task in $tasks) {
    Stop-ScheduledTask -TaskName $task.TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $task.TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Say "scheduled task $($task.TaskName) removed."
}
# the supervisor first (it would start the application again), then the application and whatever else was started from the installation (not the database server: it is stopped cleanly below)
foreach ($process in (Own-Processes | Where-Object { $_.CommandLine -match 'supervisor\.ps1' })) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue; Say "supervisor process $($process.ProcessId) stopped." }
foreach ($process in (Own-Processes | Where-Object { $_.Name -ne 'mariadbd.exe' })) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue; Say "process $($process.Name) $($process.ProcessId) stopped." }

# ---- 2. firewall ----
foreach ($rule in $rules) { $rule | Remove-NetFirewallRule; Say "firewall rule '$($rule.DisplayName)' removed." }

# ---- 3. database server ----
if ($service -and $serviceIsOurs) {
    if ($service.State -ne 'Stopped') { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue; try { (Get-Service $serviceName).WaitForStatus('Stopped', '00:02:00') } catch { } }
    & sc.exe delete $serviceName | Out-Null
    for ($i = 0; $i -lt 20 -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
    Say "database service $serviceName stopped and removed."
}
# a server started without a service (or left over): shut down cleanly with the admin tool next to it, then stopped
$adminCnf = Join-Path $mariaRoot 'admin.private.cnf'
$mariadbd = @(Get-CimInstance Win32_Process -Filter "Name='mariadbd.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine.Replace('/', '\') -match [regex]::Escape($mariaRoot) -or $_.CommandLine.Replace('/', '\') -match $rootPattern) })
if ($mariadbd) {
    $adminTool = Get-ChildItem -Path $mariaRoot -Recurse -Filter 'mariadb-admin.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($adminTool -and (Test-Path $adminCnf)) { $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; & $adminTool.FullName "--defaults-file=$adminCnf" shutdown 2>&1 | Out-Null; $ErrorActionPreference = $previous; Start-Sleep -Seconds 3 }
    foreach ($process in (Get-CimInstance Win32_Process -Filter "Name='mariadbd.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine.Replace('/', '\') -match [regex]::Escape($mariaRoot) })) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue; Say "database server process $($process.ProcessId) stopped." }
}

# ---- 4. registry and files ----
if ((Get-ItemProperty $registryKey -ErrorAction SilentlyContinue).InstallRoot -and ((Get-ItemProperty $registryKey).InstallRoot.TrimEnd('\') -ieq $root)) {
    Remove-ItemProperty $registryKey -Name InstallRoot -ErrorAction SilentlyContinue
    if (-not (Get-Item $registryKey -ErrorAction SilentlyContinue).Property) { Remove-Item $registryKey -ErrorAction SilentlyContinue }
    Say 'registry entry removed.'
}
$left = New-Object System.Collections.Generic.List[string]
function Remove-Path([string]$path) {
    for ($i = 0; $i -lt 10 -and (Test-Path -LiteralPath $path); $i++) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue; if (Test-Path -LiteralPath $path) { Start-Sleep -Seconds 1 } }
    if (Test-Path -LiteralPath $path) { $left.Add($path) }
}
if (-not $KeepData) {
    # only what the installation made in the data root, never the other files of that folder
    if (Test-Path -LiteralPath $dataRoot) {
        if ((Split-Path $dataRoot -Leaf) -ieq 'BlazorStoc') {
            # the folder the installer makes by itself (C:\ProgramData\BlazorStoc): all of it is BlazorStoc's
            Remove-Path $dataRoot
        }
        else {
            # a folder chosen by the administrator may hold other things: only what the installation made
            foreach ($name in 'assets', 'application-connection.private.json', 'backup-account.private.json', 'restore-account.private.json', 'database-connection.override.json', 'admin.private.cnf', 'logs', 'update-journal.json') {
                $path = Join-Path $dataRoot $name
                if (Test-Path -LiteralPath $path) { Remove-Path $path }
            }
            if (-not (Get-ChildItem -LiteralPath $dataRoot -Force -ErrorAction SilentlyContinue)) { Remove-Path $dataRoot }
            else { Say "kept in ${dataRoot}: $((Get-ChildItem -LiteralPath $dataRoot -Force | Select-Object -ExpandProperty Name) -join ', ') (not made by BlazorStoc's installation)" }
        }
        Say "data of the application removed from $dataRoot."
    }
    Remove-Path $root
    Say "installation folder $root removed (with the database)."
}
else {
    foreach ($entry in Get-ChildItem -LiteralPath $root -Force) { if ($entry.FullName.TrimEnd('\') -ine $mariaRoot) { Remove-Path $entry.FullName } }
    if (-not (Get-ChildItem -LiteralPath $root -Force -ErrorAction SilentlyContinue)) { Remove-Path $root }
    Say "installation removed; kept: the database in $mariaRoot and the data of the application in $dataRoot."
}
foreach ($path in $left) { Say "LEFT BEHIND (a file is in use; restart the computer and delete by hand): $path" }
Say $(if ($left.Count -eq 0) { 'Done: BlazorStoc is removed from this server.' } else { 'Done, with the leftovers listed above.' })
