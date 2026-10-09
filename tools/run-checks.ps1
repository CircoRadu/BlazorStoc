<#
.SYNOPSIS
  Builds and runs tests\BlazorStoc.Checks (Release), keeps the whole output in a log file and prints only a summary.
.DESCRIPTION
  Prints the exit code, the number of PASS/FAIL lines, the FAIL lines and exceptions (with a little context), the lines that
  match -Show (for the checks of the task at hand) and the path of the log. The log is read only when something fails.
.EXAMPLE
  .\tools\run-checks.ps1                                   # whole in-memory suite
  .\tools\run-checks.ps1 -Group suppliers                  # CHECKS_ONLY=suppliers (groups: suppliers, pickup, groups, reasons, components, invoices = pdf + xml, ui; comma separated)
  .	oolsun-checks.ps1 -Group xml                        # XML and ZIP invoices only (seconds); -Group pdf = PDF/OCR invoices only (minutes)
  .\tools\run-checks.ps1 -Mode components                  # every group but invoices (= -Group ui)
  .\tools\run-checks.ps1 -Mode invoices                    # = -Group invoices
  .\tools\run-checks.ps1 -Mode maria -Section 'Suppliers and invoices'   # MariaDB test database, one section (MARIA_ONLY)
  .\tools\run-checks.ps1 -Show 'Invoices page'             # also lists the PASS lines that match
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'components', 'invoices', 'maria')][string]$Mode = 'all',
    [string]$Section,
    [string]$Group,
    [string]$Show,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'tests\BlazorStoc.Checks\BlazorStoc.Checks.csproj'
$logDir = Join-Path $repo 'artifacts\check-logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir ("checks-{0}-{1:yyyyMMdd-HHmmss}.log" -f $Mode, (Get-Date))

$saved = @{}
function Set-Env($name, $value) { $script:saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $value) }
try {
    switch ($Mode) {
        'components' { Set-Env 'CHECKS_ONLY' 'ui' }
        'invoices' { Set-Env 'CHECKS_ONLY' 'invoices' }
        'maria' {
            Set-Env 'RUN_MARIA_INTEGRATION_CHECKS' '1'
            Set-Env 'MARIA_TEST_CONFIG_PATH' (Join-Path $repo 'local-secrets\test-database.private.json')
            if ($Section) { Set-Env 'MARIA_ONLY' $Section; Set-Env 'CHECKS_ONLY' 'maria' }   # only the MariaDB sections, not the whole in-memory suite
        }
    }
    if ($Group) { Set-Env 'CHECKS_ONLY' $Group }
    # Without a corpus directory the real invoices are skipped (declared in the output of the suite).
    if ($Mode -ne 'invoices' -and $Group -notmatch 'invoices' -and -not [Environment]::GetEnvironmentVariable('INVOICE_CORPUS_DIR')) { Set-Env 'INVOICE_CORPUS_DIR' (Join-Path $repo 'artifacts\no-corpus') }

    $arguments = @('run', '--project', $project, '-c', 'Release')
    if ($NoBuild) { $arguments += '--no-build' }
    $ErrorActionPreference = 'Continue'   # stderr of the suite (warnings) must not abort the script
    & dotnet @arguments *> $log
    $ErrorActionPreference = 'Stop'
    $exit = $LASTEXITCODE
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}

$lines = Get-Content $log
$pass = @($lines | Where-Object { $_ -match '^PASS' }).Count
$failIndexes = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match '^FAIL|FAIL \[|Unhandled exception|error CS|: error ' })
Write-Output ("exit={0} mode={1} PASS={2} FAIL/erori={3} log={4}" -f $exit, $Mode, $pass, $failIndexes.Count, $log)
foreach ($index in ($failIndexes | Select-Object -First 15)) {
    $lines[$index..([Math]::Min($index + 2, $lines.Count - 1))] | ForEach-Object { '  ' + $_.Substring(0, [Math]::Min($_.Length, 220)) }
}
if ($Show) { $lines | Where-Object { $_ -match $Show } | Select-Object -First 40 | ForEach-Object { '  ' + $_.Substring(0, [Math]::Min($_.Length, 200)) } }
if ($Mode -eq 'maria' -and -not ($lines | Where-Object { $_ -match 'Extended MariaDB checks: all sections passed' })) { Write-Output 'Atentie: nu apare mesajul „all sections passed” (sectiunea poate fi sarita sau baza indisponibila).' }
exit $exit
