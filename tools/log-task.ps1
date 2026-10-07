<#
.SYNOPSIS
  Documents a finished task: appends the entry to IMPLEMENTED.md (date and time of now) and, optionally, a line to docs/TESTE_RAMASE.md.
.DESCRIPTION
  Append only (the files are never read or rewritten). Write the texts without Romanian diacritics (rule for .md files).
.EXAMPLE
  .\tools\log-task.ps1 -Title 'Pagina Facturi' -Details 'Ce s-a implementat; fisiere; decizii', 'Verificari: build Release, teste trecute' -Remaining 'Pagina Facturi: de verificat in browser'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Title,
    [Parameter(Mandatory)][string[]]$Details,
    [string]$Remaining
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$nonAscii = '[^' + [char]0 + '-' + [char]127 + ']'
if (($Title + ($Details -join '')) -cmatch $nonAscii) { Write-Warning 'Textul contine caractere non-ASCII (diacritice); fisierele .md se scriu fara.' }

$entry = "`r`n## Finalizat la {0:dd.MM.yyyy HH:mm} - {1}`r`n`r`n" -f (Get-Date), $Title
foreach ($detail in $Details) { $entry += "- $detail`r`n" }
[System.IO.File]::AppendAllText((Join-Path $repo 'IMPLEMENTED.md'), $entry, $utf8)
Write-Output "IMPLEMENTED.md: intrare adaugata ($Title)"

if ($Remaining) {
    $line = "`r`n{0} ({1:dd.MM.yyyy}): {2}`r`n" -f $Title, (Get-Date), $Remaining
    [System.IO.File]::AppendAllText((Join-Path $repo 'docs\TESTE_RAMASE.md'), $line, $utf8)
    Write-Output 'docs/TESTE_RAMASE.md: verificare ramasa adaugata'
}
