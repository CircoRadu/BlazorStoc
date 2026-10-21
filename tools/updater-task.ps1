<#
.SYNOPSIS
  The scheduled task through which the application asks the updater to work (Setari -> Actualizare): registered by "BlazorStoc.Updater init" (as an administrator).
.DESCRIPTION
  The application runs with the rights of an ordinary account and cannot change the installation. The task has no trigger; it runs "BlazorStoc.Updater run-requests" with the
  highest rights of the account that registered it, and the application starts it with "schtasks /Run" after it has written a request file in updates\staged. A task registered
  by an administrator for his own account can be started by that same account without a prompt, and by no one else. The account must therefore be the one the application
  runs as (the one the supervisor task uses): run "init" signed in as that account, elevated.
.EXAMPLE
  .\updater-task.ps1 -Action register -TaskName BlazorStocUpdater-1a2b3c4d -InstallRoot D:\BlazorStoc -Dotnet 'C:\Program Files\dotnet\dotnet.exe' -UpdaterDll D:\BlazorStoc\updater\BlazorStoc.Updater.dll
#>
param(
    [ValidateSet('register', 'remove', 'status')][string]$Action = 'status',
    [Parameter(Mandatory)][string]$TaskName,
    [string]$InstallRoot,
    [string]$Dotnet,
    [string]$UpdaterDll
)
$ErrorActionPreference = 'Stop'
switch ($Action) {
    'register' {
        if (-not $InstallRoot -or -not $Dotnet -or -not $UpdaterDll) { throw 'register needs -InstallRoot, -Dotnet and -UpdaterDll.' }
        $command = "& '$Dotnet' '$UpdaterDll' run-requests --install-root '$InstallRoot'"
        $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command `"$command`""
        if ([Environment]::OSVersion.Version.Build -ge 17763) { $taskAction = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless powershell.exe $arguments" -WorkingDirectory $InstallRoot }
        else { $taskAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory $InstallRoot }
        $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 3) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $TaskName -Action $taskAction -Principal $principal -Settings $settings `
            -Description "BlazorStoc: the updater that works for Setari -> Actualizare of the application in $InstallRoot (no trigger: the application starts it)" -Force | Out-Null
        Write-Output "Scheduled task $TaskName registered for $env:USERDOMAIN\$env:USERNAME (highest rights, no trigger)."
    }
    'remove' {
        if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
            Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-Output "Scheduled task $TaskName removed."
        }
        else { Write-Output "No scheduled task $TaskName." }
    }
    'status' {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($task) { Write-Output "Scheduled task $TaskName exists: $($task.State), account $($task.Principal.UserId), run level $($task.Principal.RunLevel)." }
        else { Write-Output "No scheduled task $TaskName." }
    }
}
