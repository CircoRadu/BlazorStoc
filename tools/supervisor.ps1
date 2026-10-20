<#
.SYNOPSIS
  Keeps BlazorStoc running: starts it, restarts it when it dies or stops answering, and reports its state.
.DESCRIPTION
  Actions:
    run        start the application and supervise it until stopped (this is what the scheduled task runs)
    status     show the supervisor, the application and what /health/live and /health answer now
    stop       ask a running supervisor to stop the application and exit, and keep the scheduled task from starting it again for StopWindowMinutes (a maintenance
               window, for example to publish a new build): without it the 5-minute check would bring the application back in the middle of the work
    start      end that window and start the scheduled task now (use it instead of Start-ScheduledTask after a stop)
    install    register a per-user scheduled task that starts the supervisor at logon and every 5 minutes (a supervisor that is running makes the repetition a no-op,
               a dead one is replaced; a run started by the task passes -Scheduled and respects the stop window)
    uninstall  remove that task

  What it does while running:
    - the application exits (exception, out of memory, killed): restart after a delay that doubles (BackoffInitialSeconds .. BackoffMaxSeconds) and goes back
      to the initial one after the application has run StableSeconds;
    - the application is alive but /health/live does not answer HealthFailures times in a row (after StartupGraceSeconds): the process tree is killed and the
      application restarted;
    - /health says 503 (the database does not answer): only logged. The application starts and works without the database, so restarting it would not help;
    - more than CrashLimit restarts in CrashWindowSeconds: the supervisor gives up (exit code 2, state "gave-up") instead of restarting forever;
    - -WithMariaDb: the local development MariaDB (tools\dev-mariadb.ps1) is started again when its port stops answering.

  Files: logs\supervisor-yyyyMMdd.log (what the supervisor did), logs\supervised-<port>-<time>.out.log / .err.log (output of each run of the application, the
  last ten kept), artifacts\supervisor\state-<port>.json (state), artifacts\supervisor\stop-<port>.flag (stop request),
  artifacts\supervisor\stopped-<port>.flag (the stop window: the time of the stop).
  Exit codes of "run": 0 stopped on request, 1 already running, 2 gave up.
.EXAMPLE
  .\tools\supervisor.ps1 -Action run -Port 5087 -WithMariaDb
  .\tools\supervisor.ps1 -Action status -Port 5087
  .\tools\supervisor.ps1 -Action stop -Port 5087
  .\tools\supervisor.ps1 -Action start -Port 5087
  .\tools\supervisor.ps1 -Action install -Port 5087 -WithMariaDb
#>
[CmdletBinding()]
param(
    [ValidateSet('run', 'status', 'stop', 'start', 'install', 'uninstall')][string]$Action = 'run',
    [int]$Port = 5087,
    [string]$AppDirectory,                 # the published application (default artifacts\notif-build, as tools\start-preview.ps1 publishes it)
    [string]$InstallRoot,                  # a server installation (tools\BlazorStoc.Updater): the version that runs is the one in <InstallRoot>\current.txt, read at EVERY start
    [string]$ConfigPath,                   # Database__PrivateConfigPath (default local-secrets\application-connection.private.json)
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [switch]$WithMariaDb,
    [switch]$Scheduled,                    # set by the scheduled task: the run respects the stop window (a manual run ignores it and ends it)
    [double]$StopWindowMinutes = 10,
    [int]$MariaPort = 3307,
    [string]$MariaScript,                  # default tools\dev-mariadb.ps1 (replaced by the supervisor test)
    [string]$AppCommand,                   # replaces "dotnet BlazorStoc.dll" (used by tools\test-supervisor.ps1)
    [string[]]$AppArguments,
    [double]$HealthIntervalSeconds = 15,
    [int]$HealthFailures = 3,
    [int]$HealthTimeoutSeconds = 5,
    [double]$StartupGraceSeconds = 60,
    [double]$BackoffInitialSeconds = 2,
    [double]$BackoffMaxSeconds = 60,
    [double]$StableSeconds = 300,
    [int]$CrashLimit = 10,
    [double]$CrashWindowSeconds = 600
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# On a server this script sits in <install root>\updater, so the folder above it is the installation (it has current.txt); on the development machine it is the repository.
if (-not $InstallRoot -and (Test-Path (Join-Path $repo 'current.txt'))) { $InstallRoot = $repo }
$stateDir = if ($InstallRoot) { Join-Path $InstallRoot 'state\supervisor' } else { Join-Path $repo 'artifacts\supervisor' }
$logDir = if ($InstallRoot) { Join-Path $InstallRoot 'logs' } else { Join-Path $repo 'logs' }
$statePath = Join-Path $stateDir "state-$Port.json"
# The application writes a marker here while it runs (App__RunMarkerPath) and deletes it on a normal stop; one found at the next start is an unexpected stop, which
# the application records in the journal of activity. The supervisor adds why ("<marker>.exit") and removes the marker when the stop was asked for.
$runMarker = Join-Path $stateDir "run-$Port.json"
$stopFlag = Join-Path $stateDir "stop-$Port.flag"
$stoppedFlag = Join-Path $stateDir "stopped-$Port.flag"
$taskName = "BlazorStocSupervisor-$Port"
New-Item -ItemType Directory -Force $stateDir, $logDir | Out-Null

function Write-Log([string]$Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $Port, $Message
    # A log or state file that cannot be written at this moment (a reader has it open) must never stop the supervisor: retried, then skipped.
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try { Add-Content -Path (Join-Path $logDir ('supervisor-{0:yyyyMMdd}.log' -f (Get-Date))) -Value $line -Encoding ASCII -ErrorAction Stop; break }
        catch { Start-Sleep -Milliseconds 40 }
    }
    Write-Output $line
}

function Test-ProcessAlive([int]$ProcessId) {
    if ($ProcessId -le 0) { return $false }
    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Read-State {
    if (-not (Test-Path $statePath)) { return $null }
    try { return Get-Content $statePath -Raw | ConvertFrom-Json } catch { return $null }
}

# Answer of a URL as a status code (0 = no answer / timeout); the body is never read.
function Get-StatusCode([string]$Url, [int]$TimeoutSeconds) {
    try {
        $request = [System.Net.WebRequest]::Create($Url)
        $request.Timeout = $TimeoutSeconds * 1000
        $request.ReadWriteTimeout = $TimeoutSeconds * 1000
        $request.Proxy = $null
        $response = $request.GetResponse()
        $code = [int]$response.StatusCode
        $response.Close()
        return $code
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode; $_.Exception.Response.Close(); return $code }
        return 0
    }
    catch { return 0 }
}

function Write-ExitNote([object]$ExitCode, [string]$Reason) {
    try { (@{ exitCode = $ExitCode; reason = $Reason; atUtc = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json) | Set-Content -Path "$runMarker.exit" -Encoding ASCII -ErrorAction Stop } catch { }
}

function Stop-Tree([int]$ProcessId) {
    if (Test-ProcessAlive $ProcessId) { & taskkill.exe /PID $ProcessId /T /F *> $null }
}

# The port saved from Setari -> Baza de date -> Conexiune ("appPort" in the override file of the data folder), or $null.
. (Join-Path $PSScriptRoot 'data-root.ps1')
function Get-SavedPort {
    try {
        $file = Join-Path (Get-AppDataRoot) 'database-connection.override.json'
        if (Test-Path $file) { $saved = (Get-Content $file -Raw | ConvertFrom-Json).appPort; if ($saved) { return [int]$saved } }
    }
    catch { }
    return $null
}

# The port the application is started on: the saved one when it is free (the application decides the same way and falls back to the port of this supervisor otherwise),
# so a port changed from the page is the one the supervisor starts, checks and restarts.
function Get-ListenPort {
    $saved = Get-SavedPort
    if ($saved) {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $saved)
        try { $listener.Start(); $listener.Stop(); return $saved } catch { }
    }
    return $Port
}
$script:listenPort = $Port

switch ($Action) {
    'status' {
        $state = Read-State
        if (-not $state) { Write-Output "No supervisor state for port $Port."; exit 0 }
        $supervisorAlive = Test-ProcessAlive $state.supervisorPid
        $childAlive = Test-ProcessAlive $state.childPid
        $listen = $Port
        $savedPort = Get-SavedPort
        if ($savedPort -and $savedPort -ne $Port -and (Get-StatusCode "http://127.0.0.1:$savedPort/health/live" 3) -eq 200) { $listen = $savedPort }
        $live = Get-StatusCode "http://127.0.0.1:$listen/health/live" 5
        $database = Get-StatusCode "http://127.0.0.1:$listen/health" 5
        [pscustomobject]@{
            Port = $Port; ListenPort = $listen; Status = $state.status; SupervisorPid = $state.supervisorPid; SupervisorAlive = $supervisorAlive
            ChildPid = $state.childPid; ChildAlive = $childAlive; Restarts = $state.restarts; LastReason = $state.lastReason
            StartedUtc = $state.startedUtc; HealthLive = $live; Health = $database
        } | Format-List
        exit 0
    }
    'start' {
        Remove-Item $stoppedFlag -Force -ErrorAction SilentlyContinue
        if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { Start-ScheduledTask -TaskName $taskName; Write-Output "Scheduled task $taskName started." }
        else { Write-Output "No scheduled task ${taskName}; start the supervisor with -Action run." }
        exit 0
    }
    'stop' {
        # The stop window first: the scheduled task's next check must not start the application again while the stop is being used (a build published, a file replaced).
        [DateTime]::UtcNow.ToString('o') | Set-Content -Path $stoppedFlag -Encoding ASCII
        $state = Read-State
        if (-not $state -or -not (Test-ProcessAlive $state.supervisorPid)) { Write-Output "No supervisor is running for port $Port (the scheduled task will not start one for $StopWindowMinutes minutes; -Action start ends that)."; exit 0 }
        Set-Content -Path $stopFlag -Value 'stop' -Encoding ASCII
        for ($i = 0; $i -lt 60 -and (Test-ProcessAlive $state.supervisorPid); $i++) { Start-Sleep -Milliseconds 500 }
        if (Test-ProcessAlive $state.supervisorPid) { Stop-Tree $state.supervisorPid; Write-Output 'The supervisor did not stop in time and was killed.' }
        else { Write-Output "Supervisor for port $Port stopped (the scheduled task will not start one for $StopWindowMinutes minutes; -Action start ends that)." }
        exit 0
    }
    'install' {
        $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -Action run -Scheduled -Port $Port"
        if ($WithMariaDb) { $arguments += " -WithMariaDb -MariaRoot `"$MariaRoot`"" }
        if ($AppDirectory) { $arguments += " -AppDirectory `"$AppDirectory`"" }
        if ($InstallRoot) { $arguments += " -InstallRoot `"$InstallRoot`"" }
        if ($ConfigPath) { $arguments += " -ConfigPath `"$ConfigPath`"" }
        $taskAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory $repo
        # At logon, and every 5 minutes after: while the supervisor lives the task ignores the repetition (MultipleInstances IgnoreNew); if the supervisor itself was
        # killed, the next repetition starts a new one (the scheduler's own restart-on-failure does not react to a killed process).
        $logon = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
        $repeat = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
        $trigger = @($logon, $repeat)
        $settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $trigger -Settings $settings `
            -Description "Keeps BlazorStoc running on port $Port (tools\supervisor.ps1)" -Force | Out-Null
        Write-Output "Scheduled task $taskName registered (starts at logon of $env:USERNAME and is checked every 5 minutes). Start it now with: Start-ScheduledTask -TaskName $taskName"
        exit 0
    }
    'uninstall' {
        if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
            Write-Output "Scheduled task $taskName removed."
        }
        else { Write-Output "No scheduled task $taskName." }
        exit 0
    }
}

# ---------------- run ----------------

# The stop window (see "stop"): a run started by the scheduled task inside it ends at once; a manual run ignores the window and ends it.
if (Test-Path $stoppedFlag) {
    $stoppedAt = [DateTime]::MinValue
    try { $stoppedAt = [DateTime]::Parse((Get-Content $stoppedFlag -Raw).Trim(), $null, [System.Globalization.DateTimeStyles]::RoundtripKind) } catch { }
    if ($Scheduled -and $stoppedAt.ToUniversalTime().AddMinutes($StopWindowMinutes) -gt [DateTime]::UtcNow) {
        Write-Log "Stopped on request at $($stoppedAt.ToLocalTime().ToString('HH:mm:ss')): the scheduled task does not start the application until $($stoppedAt.ToLocalTime().AddMinutes($StopWindowMinutes).ToString('HH:mm:ss')) (-Action start ends the window)."
        exit 0
    }
    Remove-Item $stoppedFlag -Force -ErrorAction SilentlyContinue
}

$previous = Read-State
if ($previous -and $previous.status -in @('running', 'stopping') -and (Test-ProcessAlive $previous.supervisorPid) -and $previous.supervisorPid -ne $PID) {
    Write-Output "A supervisor (pid $($previous.supervisorPid)) is already running for port $Port."
    exit 1
}
Remove-Item $stopFlag -Force -ErrorAction SilentlyContinue

# The application of a supervisor that died (killed, crashed, the machine's session ended) is still running, alone, and would hold the port against the new one.
# It is recognised by the state file (pid and start time, so a reused pid is never taken for it) and stopped; the new supervisor then starts a clean one.
if ($previous -and [int]$previous.childPid -gt 0) {
    $leftover = Get-Process -Id ([int]$previous.childPid) -ErrorAction SilentlyContinue
    if ($leftover -and $previous.startedUtc) {
        $recorded = ([datetime]$previous.startedUtc).ToUniversalTime()
        if ([math]::Abs(($leftover.StartTime.ToUniversalTime() - $recorded).TotalSeconds) -lt 15) {
            Write-Log "The application of a previous supervisor (pid $($leftover.Id)) is still running without one: stopping it before starting a new one."
            Write-ExitNote $null 'supervizorul anterior s-a oprit; aplicatia ramasa a fost oprita de noul supervizor'
            & taskkill.exe /PID $leftover.Id /T /F *> $null
            Start-Sleep -Seconds 1
        }
    }
}

if (-not $AppDirectory -and -not $InstallRoot) { $AppDirectory = Join-Path $repo 'artifacts\notif-build' }
# (in an installation the application finds its own configuration in the data root; nothing is imposed)
if (-not $ConfigPath -and -not $InstallRoot) { $ConfigPath = Join-Path $repo 'local-secrets\application-connection.private.json' }

$state = [ordered]@{
    status = 'running'; supervisorPid = $PID; childPid = 0; port = $Port; restarts = 0; lastReason = ''; startedUtc = ''; healthLive = 0; health = 0
}
# Written beside the real file and moved over it, so a reader never sees half a file; a failure is retried and then skipped (the next save writes it again).
function Save-State {
    $json = $state | ConvertTo-Json
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            Set-Content -Path "$statePath.tmp" -Value $json -Encoding ASCII -ErrorAction Stop
            Move-Item -Path "$statePath.tmp" -Destination $statePath -Force -ErrorAction Stop
            return
        }
        catch { Start-Sleep -Milliseconds 40 }
    }
}

function Start-Maria {
    $client = New-Object System.Net.Sockets.TcpClient
    $up = $false
    try { $async = $client.BeginConnect('127.0.0.1', $MariaPort, $null, $null); $up = $async.AsyncWaitHandle.WaitOne(1000) -and $client.Connected } catch { $up = $false } finally { $client.Close() }
    if ($up) { return }
    Write-Log "MariaDB does not answer on ${MariaPort}: starting it."
    # In a process of its own, hidden. NOT with "Start-Process -Wait": in Windows PowerShell that waits for every process the script started too, and the
    # database it starts never ends (the loop of the supervisor would stop for ever). The script's own end is waited for, for at most 90 s.
    $script = if ($MariaScript) { $MariaScript } else { Join-Path $PSScriptRoot 'dev-mariadb.ps1' }
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-Action', 'start', '-Root', "`"$MariaRoot`"")
    $started = Start-Process powershell.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null = $started.Handle
    $deadline = (Get-Date).AddSeconds(90)
    while (-not $started.HasExited -and (Get-Date) -lt $deadline -and -not (Test-Path $stopFlag)) { Start-Sleep -Milliseconds 300 }
    if ($started.HasExited) { Write-Log "MariaDB start script ended with code $($started.ExitCode)." }
    else { Write-Log 'MariaDB start script is still running: going on without waiting for it.' }
}

# The version of an installation that runs: decided at every start, so an update (a new line in current.txt, then a restart) is all it takes to change it.
function Resolve-InstalledApp {
    $currentFile = Join-Path $InstallRoot 'current.txt'
    $version = if (Test-Path $currentFile) { (Get-Content $currentFile -Raw).Trim() } else { '' }
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "$currentFile does not name a version." }
    return (Join-Path $InstallRoot "releases\$version\app")
}

function Start-App {
    if ($InstallRoot -and -not $AppCommand) { $script:AppDirectory = Resolve-InstalledApp }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $out = Join-Path $logDir "supervised-$Port-$stamp.out.log"
    $err = Join-Path $logDir "supervised-$Port-$stamp.err.log"
    Get-ChildItem $logDir -Filter "supervised-$Port-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -Skip 20 | Remove-Item -Force -ErrorAction SilentlyContinue
    if ($AppCommand) {
        $file = $AppCommand
        $arguments = $AppArguments
        $workingDirectory = $repo
    }
    else {
        $dll = Join-Path $AppDirectory 'BlazorStoc.dll'
        if (-not (Test-Path $dll)) { throw "$dll not found (publish first: tools\start-preview.ps1)." }
        $file = 'dotnet'
        $arguments = @("`"$dll`"")
        $workingDirectory = if ($InstallRoot) { $AppDirectory } else { $repo }
        if ($ConfigPath) { $env:Database__PrivateConfigPath = $ConfigPath }
        # The address is NOT imposed: the application listens on the port saved from its Settings page when there is one (and it is free), otherwise on the port proposed here.
        # Exit code 75 = "restart me" (the page restarts the application to take a new port); App__Supervisor tells the page that a restart is possible.
        Remove-Item Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
        $env:Hosting__DefaultPort = "$Port"
        $env:App__Supervisor = 'supervisor'
        # Setari -> Actualizare works only for an installation made by the updater: the application learns from here which one it runs from.
        if ($InstallRoot) { $env:App__InstallRoot = $InstallRoot } else { Remove-Item Env:\App__InstallRoot -ErrorAction SilentlyContinue }
        $script:listenPort = Get-ListenPort
        $env:ASPNETCORE_WEBROOT = Join-Path $AppDirectory 'wwwroot'
        # (the note about the previous run is NOT removed here: the application that starts now reads it and reports the unexpected stop with its cause)
        $env:App__RunMarkerPath = $runMarker
        $toolsBin = Get-ChildItem -Path $MariaRoot -Directory -Filter 'mariadb-*' -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName 'bin' }
        if ($toolsBin -and (Test-Path (Join-Path $toolsBin 'mariadb-dump.exe'))) {
            $env:Database__MariaDumpExecutablePath = Join-Path $toolsBin 'mariadb-dump.exe'
            $env:Database__MariaClientExecutablePath = Join-Path $toolsBin 'mariadb.exe'
        }
    }
    $parameters = @{ FilePath = $file; WorkingDirectory = $workingDirectory; WindowStyle = 'Hidden'; PassThru = $true; RedirectStandardOutput = $out; RedirectStandardError = $err }
    if ($arguments) { $parameters.ArgumentList = $arguments }
    return Start-Process @parameters
}

$recent = New-Object 'System.Collections.Generic.List[datetime]'
$backoff = $BackoffInitialSeconds
$exitCode = 0
Write-Log "Supervisor started (pid $PID); application $(if ($AppCommand) { $AppCommand } elseif ($InstallRoot) { "of the installation $InstallRoot (current.txt)" } else { Join-Path $AppDirectory 'BlazorStoc.dll' })."
Save-State

while ($true) {
    if (Test-Path $stopFlag) { break }
    if ($WithMariaDb) { Start-Maria }
    try { $child = Start-App }
    catch {
        $state.lastReason = "could not start: $($_.Exception.Message)"
        Write-Log "The application could not be started: $($_.Exception.Message)"
        $child = $null
    }
    $startedAt = Get-Date
    $reason = $null
    # (Windows PowerShell gives no exit code for a process that ended before its handle was used.)
    if ($child) { $null = $child.Handle }
    if ($child) {
        $state.childPid = $child.Id; $state.startedUtc = (Get-Date).ToUniversalTime().ToString('o'); $state.status = 'running'; Save-State
        Write-Log "Application started (pid $($child.Id))."
        $failures = 0
        $lastCheck = Get-Date
        $databaseUp = $null
        while ($true) {
            if (Test-Path $stopFlag) { $reason = 'stop'; break }
            if ($child.HasExited) { $reason = "exited with code $($child.ExitCode)"; break }
            Start-Sleep -Milliseconds 250
            if (((Get-Date) - $lastCheck).TotalSeconds -lt $HealthIntervalSeconds) { continue }
            $lastCheck = Get-Date
            # The development database is kept up too (the application itself never needs a restart for it: it works without the database and finds it again).
            if ($WithMariaDb) { Start-Maria }
            $live = Get-StatusCode "http://127.0.0.1:$($script:listenPort)/health/live" $HealthTimeoutSeconds
            $state.healthLive = $live
            if ($live -eq 200) { $failures = 0 }
            elseif (((Get-Date) - $startedAt).TotalSeconds -gt $StartupGraceSeconds) {
                $failures++
                Write-Log "/health/live gave $live ($failures of $HealthFailures)."
                if ($failures -ge $HealthFailures) { $reason = "not answering (/health/live $live, $failures times)"; break }
            }
            if ($live -eq 200) {
                $database = Get-StatusCode "http://127.0.0.1:$($script:listenPort)/health" $HealthTimeoutSeconds
                $state.health = $database
                $isUp = $database -eq 200
                if ($null -eq $databaseUp -or $databaseUp -ne $isUp) {
                    if ($isUp) { Write-Log 'Database answers.' } else { Write-Log "Database does not answer (/health $database): the application keeps running without it, no restart." }
                    $databaseUp = $isUp
                }
            }
            Save-State
        }
        if ($reason -eq 'stop') { break }
        if ($reason -like 'not answering*') { Write-Log "Killing the unresponsive application (pid $($child.Id)) and its processes."; Stop-Tree $child.Id }
        $state.lastReason = $reason
        $childExit = $null
        if ($child.HasExited) { $childExit = $child.ExitCode }
        Write-ExitNote $childExit $reason
        # Asked for by the application itself (a port changed from its page): started again at once, not counted as a failure.
        if ($childExit -eq 75) {
            $state.lastReason = 'restart requested by the application'
            $state.restarts = [int]$state.restarts + 1
            $state.childPid = 0
            Write-Log 'The application asked for a restart (exit code 75): starting it again.'
            Save-State
            continue
        }
    }
    else { $reason = $state.lastReason }

    # The application is gone: how long it lived decides how long to wait, and too many in a short time ends the supervision.
    $now = Get-Date
    $lifetime = ($now - $startedAt).TotalSeconds
    if ($lifetime -ge $StableSeconds) { $backoff = $BackoffInitialSeconds; $recent.Clear() }
    $recent.Add($now)
    while ($recent.Count -gt 0 -and ($now - $recent[0]).TotalSeconds -gt $CrashWindowSeconds) { $recent.RemoveAt(0) }
    $state.restarts = [int]$state.restarts + 1
    $state.childPid = 0
    if ($recent.Count -gt $CrashLimit) {
        $state.status = 'gave-up'
        Save-State
        Write-Log "More than $CrashLimit failures in $([int]$CrashWindowSeconds) s ($reason): giving up. See logs\supervised-$Port-*.err.log."
        $exitCode = 2
        break
    }
    Write-Log "Application ended ($reason) after $([int]$lifetime) s; restarting in $([math]::Round($backoff, 1)) s (failure $($recent.Count) of $CrashLimit in the window)."
    $state.status = 'waiting'
    Save-State
    $waitUntil = (Get-Date).AddSeconds($backoff)
    while ((Get-Date) -lt $waitUntil -and -not (Test-Path $stopFlag)) { Start-Sleep -Milliseconds 200 }
    $backoff = [math]::Min($BackoffMaxSeconds, $backoff * 2)
}

if ($exitCode -eq 0) {
    $state.status = 'stopping'; Save-State
    if ($child -and -not $child.HasExited) { Write-Log "Stop requested: stopping the application (pid $($child.Id))."; Stop-Tree $child.Id }
    # A stop that was asked for is not an unexpected stop of the application: no marker is left for the next start to report.
    Remove-Item $runMarker, "$runMarker.exit" -Force -ErrorAction SilentlyContinue
    $state.status = 'stopped'
}
$state.childPid = 0
Save-State
Remove-Item $stopFlag -Force -ErrorAction SilentlyContinue
Write-Log "Supervisor ended (code $exitCode)."
exit $exitCode
