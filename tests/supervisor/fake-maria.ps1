# A stand-in for tools\dev-mariadb.ps1 used by tools\test-supervisor.ps1: like the real one it starts a long-running process of its own (the "database"),
# which outlives the script, writes a marker and returns.
param([string]$Action, [string]$Root)
$child = Start-Process ping.exe -ArgumentList '-n', '3001', '127.0.0.1' -WindowStyle Hidden -PassThru
Add-Content -Path (Join-Path $Root 'fake-maria-started.txt') -Value $child.Id -Encoding ASCII
