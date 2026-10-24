<#
.SYNOPSIS
  Versions of the modules (Modules/<Dir>/*Module.cs) and their ledger Modules/module-versions.json.
.DESCRIPTION
  A module whose sources changed gets a new version: -Module <Dir> -Bump major|minor|patch -Note "what changed" sets Version in the module class, adds the entry to the
  history and records the hash of the sources (the test group moduleversions compares it). -Init records the present state of a module (or of all with -All) as it is,
  for a module not yet in the ledger. -Refresh records the new hash of a module under the version it already got (more edits of the same unreleased change, no new version). -Status lists the modules whose sources differ from the ledger. Same hash as tests/BlazorStoc.Checks/ModuleVersionChecks.cs.
.EXAMPLE
  .\tools\module-version.ps1 -Module Invoices -Bump minor -Note "Zona CUI desenata cand analiza nu gaseste CUI-ul"
  .\tools\module-version.ps1 -Status
#>
param([string]$Module, [ValidateSet('major', 'minor', 'patch')][string]$Bump, [string]$Note, [switch]$Init, [switch]$All, [switch]$Status, [switch]$Refresh)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$modulesDir = Join-Path $repo 'Modules'
$ledgerPath = Join-Path $modulesDir 'module-versions.json'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Get-SourceHash([string]$directory) {
    $files = Get-ChildItem $directory -Recurse -File | ForEach-Object { $_.FullName.Substring($directory.Length).TrimStart('\', '/').Replace('\', '/') } |
        Where-Object { -not $_.StartsWith('bin/', [StringComparison]::Ordinal) -and -not $_.StartsWith('obj/', [StringComparison]::Ordinal) }
    $sorted = [string[]]$files
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $builder = New-Object System.Text.StringBuilder
    foreach ($path in $sorted) {
        [void]$builder.Append($path).Append("`n")
        [void]$builder.Append([IO.File]::ReadAllText((Join-Path $directory $path), $utf8).Replace("`r`n", "`n")).Append("`n")
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    -join ($sha.ComputeHash($utf8.GetBytes($builder.ToString())) | ForEach-Object { $_.ToString('x2') })
}

function Get-ModuleFile([string]$dir) { (Get-ChildItem (Join-Path $modulesDir $dir) -Filter '*Module.cs' -File | Select-Object -First 1).FullName }
function Get-CodeVersion([string]$file) {
    $m = [regex]::Match([IO.File]::ReadAllText($file, $utf8), 'Version\s+Version\s*\{\s*get;\s*\}\s*=\s*new\((\d+),\s*(\d+),\s*(\d+)\)')
    if (-not $m.Success) { throw "Version not found in $file" }
    "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"
}

$ledger = [ordered]@{ modules = [ordered]@{} }
if (Test-Path $ledgerPath) {
    $loaded = Get-Content $ledgerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $loaded.modules.PSObject.Properties) {
        $history = @($property.Value.history | ForEach-Object { [ordered]@{ version = $_.version; date = $_.date; note = $_.note } })
        $ledger.modules[$property.Name] = [ordered]@{ version = $property.Value.version; hash = $property.Value.hash; history = $history }
    }
}
function Save-Ledger { [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json -Depth 6) + "`n", $utf8) }
$today = (Get-Date).ToString('dd.MM.yyyy')

if ($Status) {
    foreach ($dir in (Get-ChildItem $modulesDir -Directory | Where-Object { Get-ModuleFile $_.Name })) {
        $entry = $ledger.modules[$dir.Name]
        $state = if (-not $entry) { 'nu e in evidenta' } elseif ($entry.hash -ne (Get-SourceHash $dir.FullName)) { 'MODIFICAT (cere versiune noua)' } else { 'la zi' }
        '{0,-20} {1,-8} {2}' -f $dir.Name, $(if ($entry) { $entry.version } else { (Get-CodeVersion (Get-ModuleFile $dir.Name)) }), $state
    }
    return
}

$targets = if ($All) { Get-ChildItem $modulesDir -Directory | Where-Object { Get-ModuleFile $_.Name } | ForEach-Object Name } elseif ($Module) { @($Module) } else { throw 'Give -Module <Dir> (or -All with -Init, or -Status).' }
foreach ($name in $targets) {
    $dir = Join-Path $modulesDir $name
    if (-not (Test-Path $dir) -or -not (Get-ModuleFile $name)) { throw "No module in Modules/$name." }
    $file = Get-ModuleFile $name
    $version = Get-CodeVersion $file
    if ($Init) {
        if ($ledger.modules.Contains($name)) { Write-Host "$name already in the ledger."; continue }
        $ledger.modules[$name] = [ordered]@{ version = $version; hash = (Get-SourceHash $dir); history = @([ordered]@{ version = $version; date = $today; note = 'Versiune de pornire (inceputul versionarii modulelor)' }) }
        Write-Host "$name recorded at $version."
        continue
    }
    if ($Refresh) {
        if (-not $ledger.modules.Contains($name)) { throw "$name is not in the ledger." }
        $ledger.modules[$name].hash = Get-SourceHash $dir
        Write-Host "$name hash refreshed at $version."
        continue
    }
    if (-not $Bump) { throw 'Give -Bump major|minor|patch.' }
    if (-not $Note) { throw 'Give -Note "what changed".' }
    if (-not $ledger.modules.Contains($name)) { throw "$name is not in the ledger: run -Module $name -Init first." }
    $parts = $version.Split('.') | ForEach-Object { [int]$_ }
    switch ($Bump) { 'major' { $parts = @(($parts[0] + 1), 0, 0) } 'minor' { $parts = @($parts[0], ($parts[1] + 1), 0) } 'patch' { $parts = @($parts[0], $parts[1], ($parts[2] + 1)) } }
    $new = $parts -join '.'
    $text = [IO.File]::ReadAllText($file, $utf8)
    $text = [regex]::Replace($text, '(Version\s+Version\s*\{\s*get;\s*\}\s*=\s*)new\(\d+,\s*\d+,\s*\d+\)', "`${1}new($($parts[0]), $($parts[1]), $($parts[2]))")
    [IO.File]::WriteAllText($file, $text, $utf8)
    $entry = $ledger.modules[$name]
    $entry.version = $new
    $entry.history = @($entry.history) + @([ordered]@{ version = $new; date = $today; note = $Note })
    $entry.hash = Get-SourceHash $dir
    Write-Host "$name $version -> $new"
}
Save-Ledger
