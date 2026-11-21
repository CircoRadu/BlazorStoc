<#
.SYNOPSIS
  Pachetul ZIP al unui anunt, pentru butonul "Incarca anunt" din Setari -> Anunturi.
.DESCRIPTION
  Un folder cu anunt.json, continut.html si imagini\ (formatul din docs/FORMAT_ANUNT_ZIP.md) devine un fisier .zip cu aceleasi nume la radacina.
  Nu publica nimic: la incarcare anuntul devine draft si il verifici in editor.
.EXAMPLE
  .\tools\make-anunt-zip.ps1 -Folder artifacts\anunturi\actualizare-0.6.9
#>
param([Parameter(Mandatory)][string]$Folder, [string]$Output)
$ErrorActionPreference = 'Stop'
$folder = (Resolve-Path $Folder).Path
foreach ($required in 'anunt.json', 'continut.html') {
    if (-not (Test-Path (Join-Path $folder $required))) { throw "Lipseste $required din $folder" }
}
$null = Get-Content (Join-Path $folder 'anunt.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $Output) { $Output = Join-Path (Split-Path $folder -Parent) ((Split-Path $folder -Leaf) + '.zip') }
if (Test-Path $Output) { Remove-Item $Output -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($Output, 'Create')
try {
    Get-ChildItem $folder -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($folder.Length).TrimStart('\', '/').Replace('\', '/')
        $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $relative, 'Optimal')
    }
} finally { $zip.Dispose() }
Write-Host "Pachet: $Output"
