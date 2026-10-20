<#
.SYNOPSIS
  Puts the new port of the development MariaDB in the files of the development environment, after Setari -> Baza de date -> Conexiune changed it.
.DESCRIPTION
  The page changes the port in my.ini of the server and in the connection of the application (and in the file of the administration account of the data folder). Files
  outside the application keep the old port until this script writes the new one:
    - admin.private.cnf of the instance (MariaRoot): the account that administers the server, used by the development scripts;
    - local-secrets\*.private.json: the connection of the application, the test, demo, migration, backup and restore accounts (the value "Port" of the file or of its
      "Database" section, only when the host is this computer).
  Nothing else is changed; a file that already has the port is left as it is. -WhatIf shows what would change.
.EXAMPLE
  .\tools\sync-dev-port.ps1 -Port 3308
  .\tools\sync-dev-port.ps1 -Port 3307 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateRange(1024, 65535)][int]$Port,
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$SecretsDir
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsDir) { $SecretsDir = Join-Path $repo 'local-secrets' }
$changed = 0

$cnf = Join-Path $MariaRoot 'admin.private.cnf'
if (Test-Path $cnf) {
    $text = Get-Content $cnf -Raw
    $new = [regex]::Replace($text, '(?m)^(\s*port\s*=\s*)\d+', "`${1}$Port")
    if ($new -ne $text) {
        if ($PSCmdlet.ShouldProcess($cnf, "port=$Port")) { [IO.File]::WriteAllText($cnf, $new, (New-Object Text.UTF8Encoding($false))) }
        $changed++; Write-Output "updated $cnf"
    }
}

foreach ($file in Get-ChildItem $SecretsDir -Filter '*.private.json' -File) {
    $text = Get-Content $file.FullName -Raw
    # Only the "Port": number of a connection to this computer; the file is edited as text so its layout and the rest of its values stay as they are.
    $json = $text | ConvertFrom-Json
    $section = if ($json.Database) { $json.Database } else { $json }
    $hostName = if ($section.Host) { [string]$section.Host } else { '' }
    if ($null -eq $section.Port -or $hostName -notin @('127.0.0.1', 'localhost', '::1')) { continue }
    $new = [regex]::Replace($text, '("Port"\s*:\s*)\d+', "`${1}$Port", 1)
    if ($new -ne $text) {
        if ($PSCmdlet.ShouldProcess($file.FullName, "Port=$Port")) { [IO.File]::WriteAllText($file.FullName, $new, (New-Object Text.UTF8Encoding($false))) }
        $changed++; Write-Output "updated $($file.FullName)"
    }
}
Write-Output "$changed file(s) changed. Scripts with a -Port parameter (tools\dev-mariadb.ps1, tools\supervisor.ps1 -MariaPort) take the new port as an argument."
