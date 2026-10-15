<#
.SYNOPSIS
  Checks tools\supervisor.ps1 against a fake application (tests\supervisor\fake-app.ps1) that crashes, hangs, loses its database and exits at once.
.DESCRIPTION
  Prints PASS/FAIL lines and a summary; the exit code is the number of failures. About two minutes. Writes to logs\ and artifacts\supervisor\ like the real supervisor.
.EXAMPLE
  .\tools\test-supervisor.ps1
#>
[CmdletBinding()]
param([int]$BasePort = 5101)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$supervisor = Join-Path $PSScriptRoot 'supervisor.ps1'
$fake = Join-Path $repo 'tests\supervisor\fake-app.ps1'
$stateDir = Join-Path $repo 'artifacts\supervisor'
$logDir = Join-Path $repo 'logs'
$script:failures = 0
$script:passes = 0
$jobs = @()

function Check([bool]$Condition, [string]$Message) {
    if ($Condition) { $script:passes++; Write-Output "PASS $Message" } else { $script:failures++; Write-Output "FAIL $Message" }
}
function Read-State([int]$Port) {
    $path = Join-Path $stateDir "state-$Port.json"
    if (-not (Test-Path $path)) { return $null }
    try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}
function Wait-Until([scriptblock]$Condition, [int]$Seconds) {
    $end = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $end) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 300 }
    return $false
}
function Alive([int]$ProcessId) { return ($ProcessId -gt 0) -and ($null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) }
function Log-Lines([int]$Port) {
    $file = Join-Path $logDir ('supervisor-{0:yyyyMMdd}.log' -f (Get-Date))
    if (-not (Test-Path $file)) { return @() }
    return @(Get-Content $file | Where-Object { $_ -match "\[$Port\]" })
}
function Start-Supervisor([int]$Port, [string[]]$AppArguments, [hashtable]$Extra = @{}, [switch]$KeepState) {
    # (KeepState: the state file of the supervisor that died stays, as it does in real life, so that its leftover application is known)
    if (-not $KeepState) { Remove-Item (Join-Path $stateDir "state-$Port.json") -Force -ErrorAction SilentlyContinue }
    Remove-Item (Join-Path $stateDir "stop-$Port.flag") -Force -ErrorAction SilentlyContinue
    $parameters = @{
        Action = 'run'; Port = $Port; AppCommand = 'powershell.exe'; AppArguments = $AppArguments
        HealthIntervalSeconds = 1; HealthFailures = 2; HealthTimeoutSeconds = 1; StartupGraceSeconds = 3
        BackoffInitialSeconds = 1; BackoffMaxSeconds = 4; StableSeconds = 30; CrashLimit = 3; CrashWindowSeconds = 60
    }
    foreach ($key in $Extra.Keys) { $parameters[$key] = $Extra[$key] }
    return Start-Job -ScriptBlock { param($script, $parameters) & $script @parameters } -ArgumentList $supervisor, $parameters
}
function Fake([int]$Port, [string]$Mode, [double]$After = 2, [string]$ChildPidFile = '') {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$fake`"", '-Port', $Port, '-Mode', $Mode, '-After', $After)
    if ($ChildPidFile) { $arguments += @('-ChildPidFile', "`"$ChildPidFile`"") }
    return $arguments
}
function Stop-Supervisor([int]$Port) { & $supervisor -Action stop -Port $Port | Out-Null }

try {
    # 1. normal run, status, stop (the whole process tree goes)
    $port = $BasePort
    $pidFile = Join-Path $stateDir "child-$port.pid"
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    $jobs += Start-Supervisor $port (Fake $port 'normal' 2 $pidFile)
    $up = Wait-Until { $s = Read-State $port; $s -and $s.childPid -gt 0 -and $s.healthLive -eq 200 } 25
    Check $up 'Supervisor: starts the application and sees /health/live answer 200'
    $state = Read-State $port
    $firstPid = [int]$state.childPid
    Start-Sleep -Seconds 5
    $state = Read-State $port
    Check ($state.restarts -eq 0 -and [int]$state.childPid -eq $firstPid -and $state.status -eq 'running') 'Supervisor: a healthy application is left alone (no restart in 5 s)'
    $statusText = (& $supervisor -Action status -Port $port | Out-String)
    Check ($statusText -match 'SupervisorAlive\s*:\s*True' -and $statusText -match 'ChildAlive\s*:\s*True' -and $statusText -match 'HealthLive\s*:\s*200') 'Supervisor: status shows both processes alive and the health answers'
    $grandchild = if (Test-Path $pidFile) { [int](Get-Content $pidFile) } else { 0 }
    Check (Alive $grandchild) 'Supervisor: the application has a child process of its own (to check the tree)'
    $second = & $supervisor -Action run -Port $port -AppCommand powershell.exe -AppArguments (Fake $port 'normal') | Out-String
    Check ($second -match 'already running') 'Supervisor: a second supervisor for the same port refuses to start'
    # (the marker the real application keeps while it runs; the fake one does not, so a stand-in is made)
    Set-Content -Path (Join-Path $stateDir "run-$port.json") -Value 'marker' -Encoding ASCII
    Stop-Supervisor $port
    $ended = Wait-Until { $jobs[-1].State -ne 'Running' } 20
    $state = Read-State $port
    Check (-not (Test-Path (Join-Path $stateDir "run-$port.json")) -and -not (Test-Path (Join-Path $stateDir "run-$port.json.exit"))) 'Supervisor: a stop that was asked for removes the run marker (it is not reported as an unexpected stop at the next start)'
    Check ($ended -and $state.status -eq 'stopped') 'Supervisor: stop ends the supervisor and records the state "stopped"'
    Check (-not (Alive $firstPid)) 'Supervisor: stop ends the application'
    Check (-not (Alive $grandchild)) 'Supervisor: stop ends the processes the application started too'

    # 2. the application crashes: restarted with a delay that doubles
    $port = $BasePort + 1
    $jobs += Start-Supervisor $port (Fake $port 'crash' 2) @{ CrashLimit = 20 }
    $restarted = Wait-Until { $s = Read-State $port; $s -and [int]$s.restarts -ge 3 } 60
    Check $restarted 'Supervisor: an application that keeps crashing is restarted again and again'
    $lines = Log-Lines $port
    Check (@($lines | Where-Object { $_ -match 'restarting in 1 s' }).Count -ge 1 -and @($lines | Where-Object { $_ -match 'restarting in 2 s' }).Count -ge 1 -and @($lines | Where-Object { $_ -match 'restarting in 4 s' }).Count -ge 1) `
        'Supervisor: the delay before a restart doubles (1 s, 2 s, 4 s) up to the maximum'
    Check (@($lines | Where-Object { $_ -match 'exited with code 1' }).Count -ge 2) 'Supervisor: the log says why the application ended (exit code)'
    $exitNote = Join-Path $stateDir "run-$port.json.exit"
    $note = if (Test-Path $exitNote) { try { Get-Content $exitNote -Raw | ConvertFrom-Json } catch { $null } } else { $null }
    Check ($note -and [int]$note.exitCode -eq 1 -and $note.reason -match 'exited with code 1' -and $note.atUtc) 'Supervisor: after a crash it leaves a note beside the run marker with the exit code and the reason (the application reports it in the journal at the next start)'
    Stop-Supervisor $port

    # 3. the application hangs: alive but silent -> killed and restarted
    $port = $BasePort + 2
    $jobs += Start-Supervisor $port (Fake $port 'hang' 2)
    $null = Wait-Until { $s = Read-State $port; $s -and $s.childPid -gt 0 } 20
    $hangPid = [int](Read-State $port).childPid
    $replaced = Wait-Until { $s = Read-State $port; $s -and [int]$s.childPid -gt 0 -and [int]$s.childPid -ne $hangPid } 60
    Check $replaced 'Supervisor: an application that stays alive but stops answering is replaced by a new one'
    Check (-not (Alive $hangPid)) 'Supervisor: the unresponsive application is killed'
    Check (@(Log-Lines $port | Where-Object { $_ -match 'Killing the unresponsive' }).Count -ge 1) 'Supervisor: the log says the application was killed for not answering'
    Stop-Supervisor $port

    # 4. the database does not answer: logged, no restart
    $port = $BasePort + 3
    $jobs += Start-Supervisor $port (Fake $port 'dbdown')
    $null = Wait-Until { $s = Read-State $port; $s -and $s.childPid -gt 0 -and $s.healthLive -eq 200 } 25
    $dbPid = [int](Read-State $port).childPid
    $noted = Wait-Until { @(Log-Lines $port | Where-Object { $_ -match 'Database does not answer' }).Count -ge 1 } 20
    Start-Sleep -Seconds 6
    $state = Read-State $port
    Check ($noted -and $state.health -eq 503 -and $state.restarts -eq 0 -and [int]$state.childPid -eq $dbPid) 'Supervisor: /health 503 (database down) is logged and the application is NOT restarted'
    Stop-Supervisor $port

    # 5. an application that dies at once: the supervisor gives up
    $port = $BasePort + 4
    $jobs += Start-Supervisor $port (Fake $port 'exitnow')
    $gaveUp = Wait-Until { $s = Read-State $port; $s -and $s.status -eq 'gave-up' } 60
    $endedJob = Wait-Until { $jobs[-1].State -ne 'Running' } 20
    Check ($gaveUp -and $endedJob) 'Supervisor: an application that dies at every start ends the supervision (gave-up) instead of restarting for ever'
    Check (@(Log-Lines $port | Where-Object { $_ -match 'giving up' }).Count -ge 1) 'Supervisor: the log says it gave up and why'

    # 6. an application that cannot be started at all
    $port = $BasePort + 5
    $parameters = @{ AppCommand = 'this-program-does-not-exist.exe' }
    Remove-Item (Join-Path $stateDir "state-$port.json") -Force -ErrorAction SilentlyContinue
    $jobs += Start-Job -ScriptBlock { param($script, $port) & $script -Action run -Port $port -AppCommand 'this-program-does-not-exist.exe' -BackoffInitialSeconds 1 -BackoffMaxSeconds 2 -CrashLimit 2 -CrashWindowSeconds 60 } -ArgumentList $supervisor, $port
    $gaveUp = Wait-Until { $s = Read-State $port; $s -and $s.status -eq 'gave-up' } 40
    Check ($gaveUp -and @(Log-Lines $port | Where-Object { $_ -match 'could not be started' }).Count -ge 1) 'Supervisor: an application that cannot even be started is retried a few times and then given up'

    # 7. a stop request while waiting to restart
    $port = $BasePort + 6
    $jobs += Start-Supervisor $port (Fake $port 'crash' 1) @{ BackoffInitialSeconds = 30; BackoffMaxSeconds = 30; CrashLimit = 20 }
    $null = Wait-Until { $s = Read-State $port; $s -and $s.status -eq 'waiting' } 30
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Stop-Supervisor $port
    $ended = Wait-Until { $jobs[-1].State -ne 'Running' } 20
    Check ($ended -and $watch.Elapsed.TotalSeconds -lt 15) "Supervisor: a stop request is obeyed while it waits to restart (not after the 30 s delay; $([int]$watch.Elapsed.TotalSeconds) s)"

    # 8. the database script leaves a long-running process behind (as dev-mariadb.ps1 does): the loop of the supervisor must not wait for it
    $port = $BasePort + 7
    $marker = Join-Path $stateDir 'fake-maria-started.txt'
    Remove-Item $marker -Force -ErrorAction SilentlyContinue
    $jobs += Start-Supervisor $port (Fake $port 'normal') @{ WithMariaDb = $true; MariaPort = $BasePort + 20; MariaScript = (Join-Path $repo 'tests\supervisor\fake-maria.ps1'); MariaRoot = $stateDir }
    $twice = Wait-Until { (Test-Path $marker) -and @(Get-Content $marker).Count -ge 2 } 40
    Check $twice 'Supervisor: the database is started again at the next check when it still does not answer (the loop is not stuck waiting for the database process)'
    $answering = Wait-Until { $s = Read-State $port; $s -and $s.childPid -gt 0 -and $s.healthLive -eq 200 } 20
    Check $answering 'Supervisor: while the database script keeps a process running the health checks of the application go on'
    $noticed = Wait-Until { @(Log-Lines $port | Where-Object { $_ -match 'MariaDB start script ended with code 0' }).Count -ge 1 } 20
    Check $noticed 'Supervisor: the end of the database start script is noticed and logged'
    $watch.Restart()
    Stop-Supervisor $port
    $ended = Wait-Until { $jobs[-1].State -ne 'Running' } 25
    Check ($ended -and $watch.Elapsed.TotalSeconds -lt 20) "Supervisor: a stop request is obeyed with a database process left behind ($([int]$watch.Elapsed.TotalSeconds) s)"

    # 9. the supervisor itself is killed: its application is left alone; a new supervisor stops that one and starts a clean one (no second application on the same port)
    $port = $BasePort + 8
    $jobs += Start-Supervisor $port (Fake $port 'normal')
    $null = Wait-Until { $s = Read-State $port; $s -and $s.childPid -gt 0 -and $s.healthLive -eq 200 } 25
    $before = Read-State $port
    $orphan = [int]$before.childPid
    & taskkill.exe /PID ([int]$before.supervisorPid) /F *> $null
    Start-Sleep -Seconds 2
    Check ((Alive $orphan) -and -not (Alive ([int]$before.supervisorPid))) 'Supervisor: killing the supervisor leaves its application running on its own (the situation to recover from)'
    $jobs += Start-Supervisor $port (Fake $port 'normal') @{} -KeepState
    $replaced = Wait-Until { $s = Read-State $port; $s -and [int]$s.supervisorPid -ne [int]$before.supervisorPid -and [int]$s.childPid -gt 0 -and [int]$s.childPid -ne $orphan -and $s.healthLive -eq 200 } 40
    Check $replaced 'Supervisor: a new supervisor starts a clean application and sees it answer'
    Check (-not (Alive $orphan)) 'Supervisor: the application left by the dead supervisor is stopped (no second application fighting for the port)'
    Check (@(Log-Lines $port | Where-Object { $_ -match 'still running without one' }).Count -ge 1) 'Supervisor: the log says a leftover application was stopped'
    Check ((Read-State $port).restarts -eq 0 -and (Read-State $port).status -eq 'running') 'Supervisor: after taking over the supervision is normal (no failures counted)'
    Stop-Supervisor $port
}
finally {
    foreach ($job in $jobs) { Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue }
    foreach ($offset in 0..8) {
        $state = Read-State ($BasePort + $offset)
        if ($state) { foreach ($id in @($state.childPid, $state.supervisorPid)) { if (Alive ([int]$id)) { & taskkill.exe /PID $id /T /F *> $null } } }
    }
    Get-CimInstance Win32_Process -Filter "Name='ping.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match '-n 300' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
Write-Output ("Supervisor checks: {0} passed, {1} failed." -f $script:passes, $script:failures)
exit $script:failures
