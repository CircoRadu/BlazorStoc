<#
.SYNOPSIS
  Keeps BlazorStoc running: starts it, restarts it when it dies or stops answering, and reports its state.
.DESCRIPTION
  Actions:
    run        start the application and supervise it until stopped (this is what the scheduled task runs)
    status     show the supervisor, the application and what /health/live and /health answer now
    stop       ask a running supervisor to stop the application and exit
    install    register a per-user scheduled task that starts the supervisor at logon (restarts the supervisor itself on failure)
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
  last ten kept), artifacts\supervisor\state-<port>.json (state), artifacts\supervisor\stop-<port>.flag (stop request).
  Exit codes of "run": 0 stopped on request, 1 already running, 2 gave up.
.EXAMPLE
  .\tools\supervisor.ps1 -Action run -Port 5087 -WithMariaDb
  .\tools\supervisor.ps1 -Action status -Port 5087
  .\tools\supervisor.ps1 -Action stop -Port 5087
  .\tools\supervisor.ps1 -Action install -Port 5087 -WithMariaDb
#>
[CmdletBinding()]
param(
    [ValidateSet('run', 'status', 'stop', 'install', 'uninstall')][string]$Action = 'run',
    [int]$Port = 5087,
    [string]$AppDirectory,                 # the published application (default artifacts\notif-build, as tools\start-preview.ps1 publishes it)
    [string]$ConfigPath,                   # Database__PrivateConfigPath (default local-secrets\application-connection.private.json)
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [switch]$WithMariaDb,
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
$stateDir = Join-Path $repo 'artifacts\supervisor'
$logDir = Join-Path $repo 'logs'
$statePath = Join-Path $stateDir "state-$Port.json"
$stopFlag = Join-Path $stateDir "stop-$Port.flag"
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

function Stop-Tree([int]$ProcessId) {
    if (Test-ProcessAlive $ProcessId) { & taskkill.exe /PID $ProcessId /T /F *> $null }
}

switch ($Action) {
    'status' {
        $state = Read-State
        if (-not $state) { Write-Output "No supervisor state for port $Port."; exit 0 }
        $supervisorAlive = Test-ProcessAlive $state.supervisorPid
        $childAlive = Test-ProcessAlive $state.childPid
        $live = Get-StatusCode "http://127.0.0.1:$Port/health/live" 5
        $database = Get-StatusCode "http://127.0.0.1:$Port/health" 5
        [pscustomobject]@{
            Port = $Port; Status = $state.status; SupervisorPid = $state.supervisorPid; SupervisorAlive = $supervisorAlive
            ChildPid = $state.childPid; ChildAlive = $childAlive; Restarts = $state.restarts; LastReason = $state.lastReason
            StartedUtc = $state.startedUtc; HealthLive = $live; Health = $database
        } | Format-List
        exit 0
    }
    'stop' {
        $state = Read-State
        if (-not $state -or -not (Test-ProcessAlive $state.supervisorPid)) { Write-Output "No supervisor is running for port $Port."; exit 0 }
        Set-Content -Path $stopFlag -Value 'stop' -Encoding ASCII
        for ($i = 0; $i -lt 60 -and (Test-ProcessAlive $state.supervisorPid); $i++) { Start-Sleep -Milliseconds 500 }
        if (Test-ProcessAlive $state.supervisorPid) { Stop-Tree $state.supervisorPid; Write-Output 'The supervisor did not stop in time and was killed.' }
        else { Write-Output "Supervisor for port $Port stopped." }
        exit 0
    }
    'install' {
        $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -Action run -Port $Port"
        if ($WithMariaDb) { $arguments += " -WithMariaDb -MariaRoot `"$MariaRoot`"" }
        if ($AppDirectory) { $arguments += " -AppDirectory `"$AppDirectory`"" }
        if ($ConfigPath) { $arguments += " -ConfigPath `"$ConfigPath`"" }
        $taskAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory $repo
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
        $settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $trigger -Settings $settings `
            -Description "Keeps BlazorStoc running on port $Port (tools\supervisor.ps1)" -Force | Out-Null
        Write-Output "Scheduled task $taskName registered (starts at logon of $env:USERNAME). Start it now with: Start-ScheduledTask -TaskName $taskName"
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

$previous = Read-State
if ($previous -and $previous.status -in @('running', 'stopping') -and (Test-ProcessAlive $previous.supervisorPid) -and $previous.supervisorPid -ne $PID) {
    Write-Output "A supervisor (pid $($previous.supervisorPid)) is already running for port $Port."
    exit 1
}
Remove-Item $stopFlag -Force -ErrorAction SilentlyContinue

if (-not $AppDirectory) { $AppDirectory = Join-Path $repo 'artifacts\notif-build' }
if (-not $ConfigPath) { $ConfigPath = Join-Path $repo 'local-secrets\application-connection.private.json' }

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

function Start-App {
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
        $workingDirectory = $repo
        $env:Database__PrivateConfigPath = $ConfigPath
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
        $env:ASPNETCORE_WEBROOT = Join-Path $AppDirectory 'wwwroot'
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
Write-Log "Supervisor started (pid $PID); application $(if ($AppCommand) { $AppCommand } else { Join-Path $AppDirectory 'BlazorStoc.dll' })."
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
            $live = Get-StatusCode "http://127.0.0.1:$Port/health/live" $HealthTimeoutSeconds
            $state.healthLive = $live
            if ($live -eq 200) { $failures = 0 }
            elseif (((Get-Date) - $startedAt).TotalSeconds -gt $StartupGraceSeconds) {
                $failures++
                Write-Log "/health/live gave $live ($failures of $HealthFailures)."
                if ($failures -ge $HealthFailures) { $reason = "not answering (/health/live $live, $failures times)"; break }
            }
            if ($live -eq 200) {
                $database = Get-StatusCode "http://127.0.0.1:$Port/health" $HealthTimeoutSeconds
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
    $state.status = 'stopped'
}
$state.childPid = 0
Save-State
Remove-Item $stopFlag -Force -ErrorAction SilentlyContinue
Write-Log "Supervisor ended (code $exitCode)."
exit $exitCode
