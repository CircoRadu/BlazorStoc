[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('start', 'finish', 'status')]
    [string]$Action,

    [ValidateSet('codex', 'claude')]
    [string]$Agent,

    [string]$Task,
    [string]$Summary,
    [string]$Tests,
    [string]$CommitMessage
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$StatePath = Join-Path $ProjectRoot '.collaboration\state.json'
$ProjectStatePath = Join-Path $ProjectRoot 'docs\PROJECT_STATE.md'
$ChangelogPath = Join-Path $ProjectRoot 'docs\AGENT_CHANGELOG.md'

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $output = & git -C $ProjectRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
    return $output
}

function Read-State {
    if (-not (Test-Path -LiteralPath $StatePath)) { throw "Lipsește $StatePath." }
    return Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
}

function Write-State {
    param($State)
    $State | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $StatePath -Encoding utf8
}

function Require-Agent {
    if ([string]::IsNullOrWhiteSpace($Agent)) { throw 'Parametrul -Agent este obligatoriu.' }
}

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot '.git'))) {
    throw 'Repository-ul Git nu este inițializat.'
}

switch ($Action) {
    'status' {
        $state = Read-State
        $state | ConvertTo-Json -Depth 10
        Write-Host "`nGit status:"
        Invoke-Git -Arguments @('status', '--short')
        Write-Host "`nUltimul commit:"
        & git -C $ProjectRoot rev-parse --verify HEAD *> $null
        if ($LASTEXITCODE -eq 0) {
            Invoke-Git -Arguments @('log', '-1', '--oneline')
        }
        else {
            Write-Host 'Repository-ul nu conține încă niciun commit.'
        }
        break
    }

    'start' {
        Require-Agent
        if ([string]::IsNullOrWhiteSpace($Task)) { throw 'Parametrul -Task este obligatoriu la start.' }
        $dirty = @(Invoke-Git -Arguments @('status', '--porcelain'))
        if ($dirty.Count -gt 0) { throw "Working tree-ul nu este curat. Finalizează sau recuperează ciclul existent înainte de start.`n$($dirty -join "`n")" }
        $state = Read-State
        if ($state.status -ne 'ready_for_handoff') { throw "Starea curentă este '$($state.status)', nu 'ready_for_handoff'." }
        if ($state.nextAgent -and $state.nextAgent -ne $Agent) {
            throw "Următorul agent declarat este '$($state.nextAgent)', nu '$Agent'. Ordinea poate fi schimbată numai la cererea explicită a utilizatorului."
        }
        $state.status = 'in_progress'
        $state.activeAgent = $Agent
        $state.nextAgent = $null
        $state.currentTask = $Task.Trim()
        $state.startedAtUtc = [DateTime]::UtcNow.ToString('o')
        $state.completedAtUtc = $null
        Write-State $state
        Write-Host "Ciclu început pentru $Agent. Citește PROJECT_STATE.md, TODO.md și ultimele intrări din AGENT_CHANGELOG.md."
        break
    }

    'finish' {
        Require-Agent
        foreach ($required in @('Summary', 'Tests', 'CommitMessage')) {
            if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $required -ValueOnly))) {
                throw "Parametrul -$required este obligatoriu la finish."
            }
        }
        $expectedPrefix = "${Agent}:"
        if (-not $CommitMessage.Trim().StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Mesajul commitului trebuie să înceapă cu '$expectedPrefix'."
        }
        $state = Read-State
        if ($state.status -ne 'in_progress' -or $state.activeAgent -ne $Agent) {
            throw "Ciclul activ nu aparține agentului '$Agent'."
        }
        $changedBeforeFinish = @(Invoke-Git -Arguments @('status', '--porcelain', '--untracked-files=all') | ForEach-Object { $_.Substring(3) })
        if ($changedBeforeFinish.Count -eq 0) { throw 'Nu există modificări de documentat și comis.' }
        if (-not ($changedBeforeFinish -contains 'docs/PROJECT_STATE.md')) {
            throw 'Actualizează docs/PROJECT_STATE.md înainte de finish.'
        }

        # Verifică întregul candidat la commit înainte de a marca handoff-ul drept finalizat.
        Invoke-Git -Arguments @('add', '-A')
        Invoke-Git -Arguments @('diff', '--cached', '--check')

        $nextAgent = if ($state.mode -eq 'claude_only') { 'claude' } elseif ($state.mode -eq 'codex_only') { 'codex' } elseif ($Agent -eq 'codex') { 'claude' } else { 'codex' }
        $completed = [DateTime]::UtcNow.ToString('o')
        $state.status = 'ready_for_handoff'
        $state.activeAgent = $null
        $state.lastAgent = $Agent
        $state.nextAgent = $nextAgent
        $state.completedAtUtc = $completed
        $state.lastSummary = $Summary.Trim()
        $state.lastCommitSubject = $CommitMessage.Trim()
        Write-State $state

        $changedPaths = @($changedBeforeFinish + '.collaboration/state.json' + 'docs/AGENT_CHANGELOG.md' | Sort-Object -Unique)
        $entry = @"

## $completed — $Agent

- **Task:** $($state.currentTask)
- **Rezumat:** $($Summary.Trim())
- **Fișiere modificate:**
$($changedPaths | ForEach-Object { "  - ``$_``" } | Out-String)- **Validare:** $($Tests.Trim())
- **Commit:** ``$($CommitMessage.Trim())``
- **Predat către:** $nextAgent
"@
        Add-Content -LiteralPath $ChangelogPath -Value $entry -Encoding utf8

        Invoke-Git -Arguments @('add', '-A')
        Invoke-Git -Arguments @('diff', '--cached', '--check')
        Invoke-Git -Arguments @('commit', '-m', $CommitMessage.Trim())
        $remaining = @(Invoke-Git -Arguments @('status', '--porcelain'))
        if ($remaining.Count -gt 0) { throw "Commitul a fost creat, dar working tree-ul nu este curat.`n$($remaining -join "`n")" }
        Write-Host "Ciclu încheiat. Proiectul a fost predat către $nextAgent. Oprește lucrul."
        break
    }
}
