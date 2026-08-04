<#
.SYNOPSIS
    A local vision model PLAYS the real Godot client and writes playtest notes.

.DESCRIPTION
    Why this exists: on 2026-08-03 the 805-test engine suite was green through a build where the
    player could not walk inside the forge, could not leave it, and could not reach step 3 of the
    tutorial. The tests asserted that walls block you; none asserted that you can walk. The owner
    found it in ninety seconds of play. This harness is the machine that plays.

    Shape (KTD-A of docs/plans/2026-08-04-001-feat-verify-by-playing-plan.md): a file channel. The
    Godot dev tool AgentPlaytest writes state.json + frame.png and polls for command.json. This
    driver reads the state, asks a local ollama model what to do, writes the command back, and at the
    end asks the model to write findings. Every turn stays on disk, so a bad run reads like a
    transcript instead of needing a re-run to diagnose.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??. Windows PowerShell 5.1 reads a BOM-less
    UTF-8 file as ANSI (mojibake) and treats an indented here-string terminator as a parse error.
    Both bit tools/engine-test.ps1 on its first run. Keep this file plain.

.PARAMETER Turns
    Max model turns. Each turn is one observation + one action.

.PARAMETER Model
    ollama model tag. Default llava:7b.

    NOT llama3.2-vision:11b, even though it is pulled and bigger. This ollama build cannot load it:
        {"error":"llama-server process has terminated: exit status 1:
                  error loading model: unknown model architecture: 'mllama'"}
    Measured 2026-08-04. Being PULLED is not being LOADABLE -- which is why the gate below warms the
    model with a real one-token request instead of only checking /api/tags.

.PARAMETER Scripted
    Run a fixed command sequence instead of calling the model. Proves the channel without a model in
    the loop -- build order matters here, because a model in the loop while the channel is unproven
    makes every failure ambiguous.

.PARAMETER MinFreeGb
    GPU free-VRAM floor. Default 14, per the project's hard GPU rule.

.EXAMPLE
    .\tools\agent-playtest.ps1 -Turns 40
#>
[CmdletBinding()]
param(
    [int]$Turns = 40,
    [string]$Model = 'llava:7b',
    [switch]$Scripted,
    [int]$MinFreeGb = 14,
    [int]$MaxTempC = 83,
    [string]$RepoRoot,
    [string]$OutDir,
    [string]$Endpoint = 'http://127.0.0.1:11434',
    [int]$TurnTimeoutSec = 90
)

$ErrorActionPreference = 'Stop'

function Say($text)  { Write-Host ('agent-playtest: ' + $text) -ForegroundColor Cyan }
function Warn($text) { Write-Host ('agent-playtest: ' + $text) -ForegroundColor Yellow }
function Die($lines) {
    Write-Host ''
    Write-Host ('AGENT-PLAYTEST REFUSED: ' + $lines[0]) -ForegroundColor Red
    if ($lines.Count -gt 1) {
        foreach ($line in $lines[1..($lines.Count - 1)]) { Write-Host $line -ForegroundColor Red }
    }
    exit 1
}

# --- Paths --------------------------------------------------------------------------------------
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
$RepoRoot = $RepoRoot.TrimEnd('\', '/')

# Same trap tools/engine-test.ps1 guards: the shared coordination root is ~130 PRs stale and nobody
# checks it out, so running there silently plays an old build.
if ($RepoRoot -ieq 'C:\Code\Game') {
    Die @(
        'that is the SHARED COORDINATION ROOT, which is stale (~130 PRs behind main).',
        'You would be playtesting old code. Use a worktree or C:\Code\Game\play.'
    )
}

if (-not $OutDir) { $OutDir = Join-Path $RepoRoot '.claude\agent-playtest' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$statePath   = Join-Path $OutDir 'state.json'
$cmdPath     = Join-Path $OutDir 'command.json'
$framePath   = Join-Path $OutDir 'frame.png'
$turnlogPath = Join-Path $OutDir 'turnlog.md'
$findingsPath= Join-Path $OutDir 'findings.md'
$driverLog   = Join-Path $OutDir 'driver.log'

foreach ($stale in @($statePath, $cmdPath, $framePath, $turnlogPath, $findingsPath, $driverLog)) {
    if (Test-Path $stale) { Move-Item $stale ($stale + '.prev') -Force }
}

# --- GPU gate: a precondition, not a hope -------------------------------------------------------
if (-not $Scripted) {
    $smi = & nvidia-smi --query-gpu=memory.total,memory.used,temperature.gpu --format=csv,noheader,nounits 2>&1
    if ($LASTEXITCODE -ne 0) {
        Die @('nvidia-smi failed, so the GPU state is unknown.', 'Refusing to load a model blind. Run with -Scripted to prove the channel without a model.')
    }
    $parts = ($smi | Select-Object -First 1) -split ','
    $totalMb = [int]$parts[0].Trim()
    $usedMb  = [int]$parts[1].Trim()
    $tempC   = [int]$parts[2].Trim()
    $freeGb  = [math]::Round(($totalMb - $usedMb) / 1024.0, 1)

    Say ('GPU: ' + $freeGb + ' GB free, ' + $tempC + ' C')
    if ($freeGb -lt $MinFreeGb) {
        Die @(
            ('only ' + $freeGb + ' GB VRAM free, floor is ' + $MinFreeGb + ' GB.'),
            'Project rule: never risk the machine. Close the other GPU job (ComfyUI, another model) first.'
        )
    }
    if ($tempC -gt $MaxTempC) {
        Die @(('GPU is at ' + $tempC + ' C, ceiling is ' + $MaxTempC + ' C. Let it cool.'))
    }

    $tags = try { Invoke-RestMethod -Uri ($Endpoint + '/api/tags') -TimeoutSec 10 } catch { $null }
    if (-not $tags) {
        Die @(('ollama is not reachable at ' + $Endpoint + '.'), 'Start it (ollama serve) and retry.')
    }
    $have = @($tags.models | ForEach-Object { $_.name })
    if ($have -notcontains $Model) {
        Die @(
            ('model ' + $Model + ' is not pulled. Available: ' + ($have -join ', ')),
            ('Pull it (ollama pull ' + $Model + ') or pass -Model with one of the above.')
        )
    }

    # Warm it with a real request. A model can be PULLED and still fail to LOAD -- measured
    # 2026-08-04, llama3.2-vision:11b is listed by /api/tags and then dies with
    # "unknown model architecture: 'mllama'". Finding that out on turn 1 of a real run, after
    # launching the game, wastes the run and reads like a game bug.
    Say ('warming ' + $Model)
    $warmBody = @{ model = $Model; stream = $false; messages = @(@{ role = 'user'; content = 'reply with the single word ok' }) } | ConvertTo-Json -Depth 6 -Compress
    $warm = $null
    try { $warm = Invoke-RestMethod -Uri ($Endpoint + '/api/chat') -Method Post -Body $warmBody -ContentType 'application/json' -TimeoutSec 300 } catch {
        Die @(
            ('model ' + $Model + ' is pulled but will not run.'),
            ('ollama said: ' + $_.Exception.Message),
            'If this is an architecture error, that model is unsupported by this ollama build -- pick another.'
        )
    }
    if (-not $warm.message) { Die @(('model ' + $Model + ' returned no message on a warm-up request.')) }
}

# --- Launch the client --------------------------------------------------------------------------
$godot = $env:GODOT_BIN
if (-not $godot) { $godot = 'C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe' }
if (-not (Test-Path $godot)) { Die @(('Godot not found at ' + $godot + '. Set GODOT_BIN.')) }

$env:AGENT_PLAYTEST = '1'
$env:AGENT_PLAYTEST_DIR = $OutDir
Say ('launching client (out: ' + $OutDir + ')')
$proc = Start-Process -FilePath $godot -ArgumentList @('--path', (Join-Path $RepoRoot 'godot')) -PassThru

# Fixed command sequence for -Scripted: prove the channel end to end with no model. Deliberately
# includes an illegal press so the refusal path is exercised on every scripted run.
$scriptedPlan = @(
    '{"action":"key","target":"cancel","why":"scripted: close whatever opened"}',
    '{"action":"press","target":"NoSuchButton_xyz","why":"scripted: must be REFUSED, not crash"}',
    '{"action":"move","dir":"right","frames":20,"why":"scripted: prove world input is live"}',
    '{"action":"advance","why":"scripted: tick the phase"}',
    '{"action":"stop","why":"scripted: done"}'
)

$history = New-Object System.Collections.ArrayList
$digestSeen = @{}
$stuckFindings = New-Object System.Collections.ArrayList
$turn = 0
$stopReason = 'turn budget reached'

function Wait-ForFile($path, $timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $path) {
            # Let the writer finish: a zero-length or half-written file parses as garbage.
            try {
                $raw = Get-Content $path -Raw -ErrorAction Stop
                if ($raw -and $raw.Trim().Length -gt 1) { return $raw }
            } catch { }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Invoke-Model($systemPrompt, $userText, $imagePath) {
    $userMsg = @{ role = 'user'; content = $userText }
    if ($imagePath -and (Test-Path $imagePath)) {
        $bytes = [System.IO.File]::ReadAllBytes($imagePath)
        $userMsg.images = @([System.Convert]::ToBase64String($bytes))
    }
    $body = @{
        model    = $Model
        stream   = $false
        messages = @(@{ role = 'system'; content = $systemPrompt }, $userMsg)
    } | ConvertTo-Json -Depth 8 -Compress

    $resp = Invoke-RestMethod -Uri ($Endpoint + '/api/chat') -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 300
    return $resp.message.content
}

$actPrompt = ''
$judgePrompt = ''
if (-not $Scripted) {
    $actPrompt = Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\act.md') -Raw
    $judgePrompt = Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\judge.md') -Raw
}

try {
    while ($turn -lt $Turns) {
        $stateRaw = Wait-ForFile $statePath $TurnTimeoutSec
        if (-not $stateRaw) {
            $stopReason = 'client wrote no state within ' + $TurnTimeoutSec + 's'
            Warn $stopReason
            break
        }

        $state = $stateRaw | ConvertFrom-Json
        $turn++

        # Stuck detection (R2). A model that stares at an unchanged screen must be REPORTED, never
        # mistaken for a clean run -- that is the whole failure mode this harness exists to end.
        $enabled = @($state.controls | Where-Object { $_.enabled } | ForEach-Object { $_.name })
        $digest = ($state.phase + '|' + $state.location + '|' + (($state.screenText) -join ';') + '|' + ($enabled -join ','))
        if ($digestSeen.ContainsKey($digest)) { $digestSeen[$digest] = $digestSeen[$digest] + 1 } else { $digestSeen[$digest] = 1 }
        if ($digestSeen[$digest] -eq 4) {
            $note = 'STUCK: the screen was identical for 4 turns at ' + $state.location + ' / ' + $state.phase + '. Enabled controls: ' + ($enabled -join ', ')
            Warn $note
            [void]$stuckFindings.Add($note)
        }

        # Decide the command.
        $command = $null
        if ($Scripted) {
            $idx = [math]::Min($turn - 1, $scriptedPlan.Count - 1)
            $command = $scriptedPlan[$idx]
        } else {
            $recent = ''
            if ($history.Count -gt 0) {
                $tail = $history[[math]::Max(0, $history.Count - 6)..($history.Count - 1)]
                $recent = 'Recent turns:' + [Environment]::NewLine + ($tail -join [Environment]::NewLine)
            }
            $userText = @(
                ('Turn ' + $turn + ' of ' + $Turns + '.'),
                ('Day ' + $state.day + ', phase ' + $state.phase + ', at ' + $state.location + '. canMove=' + $state.canMove + '. Gold ' + $state.gold + ', action slots left ' + $state.actionSlotsRemaining + '.'),
                ('Last outcome: ' + $state.lastOutcome),
                '',
                'On screen:',
                (($state.screenText | ForEach-Object { '  ' + $_ }) -join [Environment]::NewLine),
                '',
                'Controls:',
                (($state.controls | ForEach-Object { '  ' + $_.name + ' [' + $_.label + '] enabled=' + $_.enabled }) -join [Environment]::NewLine),
                '',
                $recent,
                '',
                'Answer with one JSON object only.'
            ) -join [Environment]::NewLine

            $attempts = 0
            while ($attempts -lt 3 -and -not $command) {
                $attempts++
                $reply = ''
                try { $reply = Invoke-Model $actPrompt $userText $framePath } catch { Warn ('model call failed: ' + $_.Exception.Message) }
                if (-not $reply) { continue }
                # Models wrap JSON in prose or fences no matter how firmly you ask. Take the object.
                $m = [regex]::Match($reply, '\{[^{}]*\}')
                if (-not $m.Success) { Warn ('no JSON in reply: ' + ($reply -replace '\s+', ' ')); continue }
                $candidate = $m.Value
                $parsed = try { $candidate | ConvertFrom-Json } catch { $null }
                if (-not $parsed -or -not $parsed.action) { Warn 'reply JSON had no action'; continue }

                # NORMALIZE, do not just reject. A 7B model folds the control name into the action
                # slot -- measured on llava:7b's very first probe, which answered
                # {"action":"openCounter","why":"..."} instead of
                # {"action":"press","target":"OpenCounter"}. That is a well-formed intention in the
                # wrong shape; throwing it away burns a turn and teaches the model nothing. Match the
                # action against the control list case-insensitively and rewrite it as a press.
                $verbs = @('press', 'move', 'key', 'advance', 'stop')
                if ($verbs -notcontains $parsed.action) {
                    $match = @($state.controls | Where-Object { $_.name -ieq $parsed.action }) | Select-Object -First 1
                    if ($match) {
                        Say ('normalized "' + $parsed.action + '" to press ' + $match.name)
                        $parsed = [pscustomobject]@{ action = 'press'; target = $match.name; why = $parsed.why }
                        $candidate = $parsed | ConvertTo-Json -Compress
                    } else {
                        $userText = $userText + [Environment]::NewLine + ('REFUSED: "' + $parsed.action + '" is not an action. Use press, move, key, advance or stop.')
                        Warn ('unknown action: ' + $parsed.action)
                        continue
                    }
                }
                # Pre-refuse an illegal press here, with the reason fed back, rather than spending a
                # game turn on it.
                if ($parsed.action -eq 'press' -and $enabled -notcontains $parsed.target) {
                    $userText = $userText + [Environment]::NewLine + ('REFUSED: "' + $parsed.target + '" is not an enabled control. Pick one of: ' + ($enabled -join ', '))
                    Warn ('model chose a disabled/absent control: ' + $parsed.target)
                    continue
                }
                $command = $candidate
            }
            if (-not $command) {
                $command = '{"action":"advance","why":"driver fallback: model gave no usable command"}'
                Warn 'falling back to advance'
            }
        }

        $parsedCmd = $command | ConvertFrom-Json
        Say ('turn ' + $turn + ': ' + $parsedCmd.action + ' ' + $parsedCmd.target + ' -- ' + $parsedCmd.why)
        [void]$history.Add('turn ' + $turn + ' @ ' + $state.location + '/' + $state.phase + ' -> ' + $parsedCmd.action + ' ' + $parsedCmd.target + ' (' + $parsedCmd.why + ') ; outcome: ' + $state.lastOutcome)

        if ($parsedCmd.action -eq 'stop') { $stopReason = 'model asked to stop: ' + $parsedCmd.why; break }

        Remove-Item $statePath -Force -ErrorAction SilentlyContinue
        Set-Content -Path $cmdPath -Value $command -Encoding utf8
    }
} finally {
    if ($proc -and -not $proc.HasExited) {
        Say 'closing client'
        try { Stop-Process -Id $proc.Id -Force -Confirm:$false } catch { }
    }
    $env:AGENT_PLAYTEST = ''
    $env:AGENT_PLAYTEST_DIR = ''
}

Say ('stopped after ' + $turn + ' turns: ' + $stopReason)

# --- Judge pass ---------------------------------------------------------------------------------
$log = ''
if (Test-Path $turnlogPath) { $log = Get-Content $turnlogPath -Raw }
if (-not $log) { $log = ($history -join [Environment]::NewLine) }

$header = @(
    '# Agent playtest findings',
    '',
    ('- model: ' + $Model),
    ('- turns: ' + $turn + ' (stopped: ' + $stopReason + ')'),
    ('- artifacts: ' + $OutDir),
    ''
)

if ($Scripted) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('Scripted run -- no model judged this. The channel was exercised, including one deliberate illegal press.', '', '## Turn log', '', $log))
    Say ('scripted run complete. Channel log: ' + $findingsPath)
    exit 0
}

Say 'asking the model to write findings'
$judgeInput = @(
    'Here is the full log of the session you just played.',
    '',
    $log,
    ''
)
if ($stuckFindings.Count -gt 0) {
    $judgeInput += @('The harness also detected these automatically -- include them:', '')
    $judgeInput += ($stuckFindings | ForEach-Object { '- ' + $_ })
}
$findings = ''
try { $findings = Invoke-Model $judgePrompt (($judgeInput) -join [Environment]::NewLine) $framePath } catch { Warn ('judge call failed: ' + $_.Exception.Message) }

if (-not $findings) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('JUDGE FAILED -- no findings written. Raw turn log below.', '', $log))
    Die @('the judge pass produced nothing. The turn log is still in ' + $findingsPath + '.')
}

Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @($findings, '', '---', '', '## Turn log', '', $log))

Write-Host ''
Say ('findings written: ' + $findingsPath)
Write-Host ''
Write-Host $findings
Write-Host ''
Warn 'Read these before trusting them. The acceptance bar for this harness is that it independently'
Warn 'names something a human would also flag. Vacuous praise means the prompts need work, not that'
Warn 'the game is fine.'
exit 0
