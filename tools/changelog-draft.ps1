<#
.SYNOPSIS
  Draft of the section of a version for Assets\Data\changelog.txt, with the categories Functionalitati noi / Bugs / Diverse (PROP-0019).
.DESCRIPTION
  Development machine only. Reads the local application database (local-secrets\application-connection.private.json) and lists, by number only:
    - Functionalitati noi: the proposals taken through "Propune o modificare" that are Implementata in this version (implemented_version);
    - Bugs: the error reports that are Rezolvat in this version (resolved_version, filled when the state becomes Rezolvat).
  Diverse is left empty: the other changes of the implementation are added by hand, after the PROP and BUG lines of the category they fit in (new features after the PROP lines, fixes after the
  BUG lines); what fits no category goes to Diverse. The draft is printed and written to artifacts\changelog-draft-<version>.txt (UTF-8); nothing in the repository is changed.
.EXAMPLE
  .\tools\changelog-draft.ps1 -Version 0.5.72
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$SecretsDir,
    [string]$MariaBinDir,
    [string]$ConnectionFile
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Version '$Version' is not of the form 1.2.3." }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SecretsDir) { $SecretsDir = Join-Path $repo 'local-secrets' }
$config = if ($ConnectionFile) { $ConnectionFile } else { Join-Path $SecretsDir 'application-connection.private.json' }
if (-not (Test-Path $config)) { throw "$config not found. Run tools\setup-dev-environment.ps1 first." }
$database = (Get-Content $config -Raw | ConvertFrom-Json).Database
if (-not $MariaBinDir) { $MariaBinDir = Get-ChildItem -Path $MariaRoot -Directory -Filter 'mariadb-*' -ErrorAction SilentlyContinue | Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName 'bin' } }
$client = Join-Path $MariaBinDir 'mariadb.exe'
if (-not (Test-Path $client)) { throw "mariadb.exe not found under '$MariaBinDir'." }

function Get-Numbers([string]$Query) {
    $env:MYSQL_PWD = $database.Password
    try {
        $rows = & $client --host=$($database.Host) --port=$($database.Port) --user=$($database.User) --database=$($database.Name) --ssl --batch --skip-column-names --execute=$Query
        if ($LASTEXITCODE -ne 0) { throw "mariadb.exe failed (exit code $LASTEXITCODE)." }
        return @($rows | Where-Object { $_ -and $_.Trim() })
    } finally { Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue }
}
$proposals = Get-Numbers "SELECT number FROM proposals WHERE state LIKE 'Implementat%' AND implemented_version = '$Version' ORDER BY id"
# The column resolved_version comes with migration 087: until the new version of the application has started on this database the bugs cannot be listed.
try { $bugs = Get-Numbers "SELECT number FROM bug_reports WHERE state = 'Rezolvat' AND resolved_version = '$Version' ORDER BY id" }
catch { Write-Warning "The bugs could not be listed (migration 087 not applied yet on this database? start the new version of the application first): $($_.Exception.Message)"; $bugs = @() }

# The names of the categories with their diacritics (this file is read as ANSI by Windows PowerShell, so they are built from code points).
$newFeatures = "Func$([char]0x021B)ionalit$([char]0x0103)$([char]0x021B)i noi"
$lines = @("## $Version | $(Get-Date -Format 'dd.MM.yyyy')", "### $newFeatures")
$lines += $proposals | ForEach-Object { "- $_" }
$lines += '### Bugs'
$lines += $bugs | ForEach-Object { "- $_" }
$lines += '### Diverse'
$lines += '- '
$text = $lines -join "`n"
$out = Join-Path $repo "artifacts\changelog-draft-$Version.txt"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[IO.File]::WriteAllText($out, $text + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output $text
Write-Output ''
Write-Output "Draft written to $out ($($proposals.Count) proposals, $($bugs.Count) bugs)."
