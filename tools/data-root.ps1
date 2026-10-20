<#
.SYNOPSIS
  The folder of the application data, decided the same way as Startup/AppDataRoot.cs (dot-source this file, then call Get-AppDataRoot).
.DESCRIPTION
  An explicit App__DataRoot environment variable wins; otherwise %ProgramData%\BlazorStoc once it has its assets folder (see tools\migrate-data-root.ps1),
  and the per-user folder of the earlier versions (%LOCALAPPDATA%\BlazorStoc-MariaDB) until the data have been moved.
#>
function Get-AppDataRoot {
    if ($env:App__DataRoot) { return [IO.Path]::GetFullPath($env:App__DataRoot) }
    $machine = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'BlazorStoc'
    $legacy = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BlazorStoc-MariaDB'
    if ((Test-Path (Join-Path $machine 'assets')) -or -not (Test-Path $legacy)) { return $machine }
    return $legacy
}
