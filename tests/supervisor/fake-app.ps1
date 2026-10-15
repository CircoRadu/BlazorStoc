# A stand-in for BlazorStoc used by tools\test-supervisor.ps1: answers /health/live and /health on a port and misbehaves on request.
#   -Mode normal   answers for ever
#   -Mode crash    exits with code 1 after -After seconds
#   -Mode hang     stays alive but stops answering after -After seconds
#   -Mode dbdown   /health/live is 200, /health is 503 (the database does not answer)
#   -Mode exitnow  exits at once with code 3
# -ChildPidFile: starts a long-running child process and writes its pid there (to check that the whole tree is stopped).
param([int]$Port, [string]$Mode = 'normal', [double]$After = 2, [string]$ChildPidFile)
if ($Mode -eq 'exitnow') { exit 3 }
if ($ChildPidFile) {
    $child = Start-Process ping.exe -ArgumentList '-n', '3000', '127.0.0.1' -WindowStyle Hidden -PassThru
    Set-Content -Path $ChildPidFile -Value $child.Id -Encoding ASCII
}
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
$started = Get-Date
while ($true) {
    $task = $listener.GetContextAsync()
    while (-not $task.Wait(200)) {
        if (((Get-Date) - $started).TotalSeconds -ge $After) {
            if ($Mode -eq 'crash') { exit 1 }
            if ($Mode -eq 'hang') { Start-Sleep -Seconds 3600 }
        }
    }
    $context = $task.Result
    $code = 200
    if ($context.Request.Url.AbsolutePath -eq '/health' -and $Mode -eq 'dbdown') { $code = 503 }
    $context.Response.StatusCode = $code
    $bytes = [Text.Encoding]::ASCII.GetBytes('x')
    $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $context.Response.Close()
    if (((Get-Date) - $started).TotalSeconds -ge $After) {
        if ($Mode -eq 'crash') { exit 1 }
        if ($Mode -eq 'hang') { Start-Sleep -Seconds 3600 }
    }
}
