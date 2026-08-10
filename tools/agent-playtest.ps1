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
    GPU free-VRAM floor. Default 8.

    This used to be 14, copied from the project's hard GPU rule -- and the copy was wrong,
    because that rule guards a different risk. 14GB is the GENERATION floor: SDXL and TRELLIS
    allocate unpredictably, can balloon mid-job, and taking the owner's daily-driver machine
    down is the failure it exists to prevent. Inference is not that shape. llava:7b is a
    quantised 7B vision model resident at roughly 4-5GB, allocated once at load and flat
    thereafter.

    Applying the generation floor here cost real work: with a browser and a game open, 12.4GB
    free is ordinary, and the harness refused runs it could have completed comfortably. 8 leaves
    headroom over the model's actual footprint without pretending an inference job is a
    generation job. Raise it with -MinFreeGb if a bigger vision model is ever pinned.

.PARAMETER Scope
    Which question this run is answering (A4/A5, "the shell around the game" plan). The act loop
    (observe, ask the model, apply the command) is IDENTICAL in all three -- only the prompt content
    and the findings.md contents change:

      Full  (default) -- today's behaviour. A full, unscoped sweep of the game.
      Diff  -- "what did I just break." Derives the files that changed since origin/main and tells
               the model to look at their mapped surfaces first. If the map cannot resolve a changed
               file, or there is no diff at all, the run says so LOUDLY in findings.md and the
               console rather than quietly acting like a full sweep was the intended Diff result.
      Scout -- "is this still the game it says it is." The SAME act loop, judged at the end by a
               different prompt seeded with docs/design/THE-GAME.md (the five spine links, the six
               decisions, the seven laws) instead of the bug-hunting judge.md. Also launches the two
               mechanical detectors that already exist (FullPlaytest's engine-log-anomaly and
               frozen-world motion checks; Playtest3dRecorder's dead-3D-surface map) and folds their
               own reports into the same findings.md. The judgement half is EVIDENCE for the owner to
               read, never a gate -- it cannot fail the build and never edits a design doc.

    Every scope names itself in findings.md's own header, so a report can never be mistaken for a
    different scope's.

.PARAMETER MechanicalTimeoutMin
    Scout only. Per-stage timeout for the two mechanical detectors (FullPlaytest's real 5-launch
    campaign, then the filtered `dotnet test godot/tests` run for Playtest3dRecorder). A stage that
    exceeds this is killed and reported as TIMED OUT in findings.md, not silently omitted.

.EXAMPLE
    .\tools\agent-playtest.ps1 -Turns 40

.EXAMPLE
    .\tools\agent-playtest.ps1 -Scope Diff -Turns 25

.EXAMPLE
    .\tools\agent-playtest.ps1 -Scope Scout -Turns 60
#>
[CmdletBinding()]
param(
    [int]$Turns = 40,
    [string]$Model = 'llava:7b',
    [switch]$Scripted,
    [int]$MinFreeGb = 8,
    [int]$MaxTempC = 83,
    [string]$RepoRoot,
    [string]$OutDir,
    [string]$Endpoint = 'http://127.0.0.1:11434',
    [int]$TurnTimeoutSec = 90,
    [int]$NumCtx = 8192,
    [ValidateSet('Full', 'Diff', 'Scout')]
    [string]$Scope = 'Full',
    [int]$MechanicalTimeoutMin = 15
)

# A4/A5/A6: the diff-to-surface map, the per-turn prompt builder, Scout's mechanical detectors, and
# the completion-floor verdict are split into their own dot-sourced files for one reason -- they
# need no Godot, no ollama, and no VRAM to prove, and this script needs all three. See
# tools/test-agent-playtest-modes.ps1.
. (Join-Path $PSScriptRoot 'agent-playtest\scope-map.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\turn-prompt.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\mechanical.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\completion.ps1')

$ErrorActionPreference = 'Stop'

function Say($text)  { Write-Host ('agent-playtest: ' + $text) -ForegroundColor Cyan }
function Warn($text) {
    Write-Host ('agent-playtest: ' + $text) -ForegroundColor Yellow
    # This is the whole point of A1: warnings used to go to the console only, so an unattended
    # run that degraded overnight left no record of it. $driverLog is a script-scope path set
    # once near the top of the file; Warn is never called before that assignment runs.
    if ($driverLog) {
        $line = '[' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '] ' + $text
        try { Add-Content -Path $driverLog -Value $line -Encoding utf8 } catch { }
    }
}
function Die($lines) {
    Write-Host ''
    Write-Host ('AGENT-PLAYTEST REFUSED: ' + $lines[0]) -ForegroundColor Red
    if ($lines.Count -gt 1) {
        foreach ($line in $lines[1..($lines.Count - 1)]) { Write-Host $line -ForegroundColor Red }
    }
    exit 1
}

# JSON-escape a string by hand.
#
# DO NOT reintroduce ConvertTo-Json on the request path. Measured 2026-08-04: a 2.5 KB prompt file in
# a nested hashtable serialized to a 46,614,552-character body, and ollama answered
#     {"error":"invalid character 't' after object key:value pair"}
# on every single call. Windows PowerShell 5.1's ConvertTo-Json wraps the string rather than emitting
# it (`{"value":"..."}`) and the nesting blows up from there. The failure looked exactly like a broken
# model -- three retries per turn, 22 turns, every one dead -- which is why this note is here instead
# of a one-line fix nobody can explain later. ConvertFrom-Json is fine; only this direction is cursed.
function JsonEsc([string]$s) {
    if ($null -eq $s) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($ch -eq "`t") { [void]$sb.Append('\t') }
        elseif ($code -lt 32 -or $code -gt 126) { [void]$sb.Append('\u' + $code.ToString('x4')) }
        else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

# Named once, used twice: here in Invoke-Model's own HTTP call, and again below (worst case =
# ModelCallMaxAttempts * ModelCallTimeoutSec) to size AGENT_PLAYTEST_TIMEOUT_MS, the env var this
# driver hands the Godot client so it knows how long a real turn can legitimately take before
# giving up on it. Before 2026-08-10 the client had its own unrelated hardcoded 30-second wait
# (AgentPlaytest.cs's DefaultCommandTimeoutMs) with no connection to these numbers at all -- a
# single slow model call here (up to 300s, and this loop allows three of them) would silently
# outrun the client's wait, and the client would quit(0) as if the run had ended cleanly. Change
# these two numbers with the client's fallback constant in mind (its own doc names them back).
$ModelCallTimeoutSec = 300
$ModelCallMaxAttempts = 3

function Invoke-Model($systemPrompt, $userText, $imagePath) {
    $imagesJson = ''
    if ($imagePath) {
        if (Test-Path $imagePath) {
            $b64 = [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($imagePath))
            $imagesJson = ',"images":["' + $b64 + '"]'
        } else {
            # A caller that passed a path meant to attach a frame. A missing frame silently
            # becoming a text-only request used to be invisible; now it is warned and counted
            # by the caller via $imageMissingThisTurn. Callers that intentionally send no image
            # (warm-up, judge pass) pass $null and never reach this branch.
            $script:imageMissingThisTurn = $true
            Warn ('frame missing at model-call time: ' + $imagePath + ' -- sending text-only request')
        }
    }

    # num_ctx must be set explicitly. llava:7b defaults to a 4096-token context and ollama HARD
    # ERRORS past it rather than truncating:
    #   {"error":{"code":400,"message":"request (6052 tokens) exceeds the available context size
    #             (4096 tokens), try increasing it","type":"exceed_context_size_error"}}
    # Measured 2026-08-04 on the judge pass. A screen digest plus a turn log passes 4096 quickly, so
    # without this the harness works for a while and then dies exactly when it has something to say.
    $body = '{"model":"' + (JsonEsc $Model) + '","stream":false,"options":{"num_ctx":' + $NumCtx + '},"messages":[' +
            '{"role":"system","content":"' + (JsonEsc $systemPrompt) + '"},' +
            '{"role":"user","content":"' + (JsonEsc $userText) + '"' + $imagesJson + '}]}'

    # Send bytes, not a string: Invoke-RestMethod would otherwise re-encode, and the body is large.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    try {
        $resp = Invoke-RestMethod -Uri ($Endpoint + '/api/chat') -Method Post -Body $bytes -ContentType 'application/json' -TimeoutSec $ModelCallTimeoutSec
    } catch {
        # Surface what ollama actually said. A bare "(400) Bad Request" cost a whole debugging pass
        # once already -- the useful text is in ErrorDetails, and PowerShell hides it by default.
        $detail = ''
        if ($_.ErrorDetails) { $detail = ' :: ' + $_.ErrorDetails.Message }
        throw ('ollama ' + $_.Exception.Message + $detail + ' (body was ' + $body.Length + ' chars)')
    }
    return $resp.message.content
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
# PlaytestLog.cs's JSONL trail (day/phase/beat/cause per tick, one row per player action) -- the
# client writes this itself once MM_PLAYTEST_LOG names a path (set on the launched process below).
# Before this, the harness's own explicit "advance" turns and the client's real button presses left
# NO reconstructable trail at all -- only this script's own turnlog.md, which never saw inside the
# client (no phase/beat/cause, no immediate actions). Same directory as everything else this run
# produces, so one folder answers "what happened in this run".
$playtestLogPath = Join-Path $OutDir 'playtest-log.jsonl'

foreach ($stale in @($statePath, $cmdPath, $framePath, $turnlogPath, $findingsPath, $driverLog, $playtestLogPath)) {
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

    # A model already resident is NOT a second job. The floor exists to stop us STARTING work the card
    # cannot hold; if ollama is already holding the exact model we are about to drive, the allocation
    # has happened and refusing on the remaining free VRAM would refuse our own model. Measured
    # 2026-08-04: after a warm-up, llava:7b holds ~5 GB and free drops to 9.1 GB, which tripped the
    # gate against the very model it had just loaded. Temperature is still enforced either way.
    $resident = @()
    try {
        $ps = Invoke-RestMethod -Uri ($Endpoint + '/api/ps') -TimeoutSec 10
        $resident = @($ps.models | ForEach-Object { $_.name })
    } catch { }

    if ($resident -contains $Model) {
        Say ($Model + ' is already loaded, so the free-VRAM floor does not apply to it')
    } elseif ($freeGb -lt $MinFreeGb) {
        Die @(
            ('only ' + $freeGb + ' GB VRAM free, floor is ' + $MinFreeGb + ' GB.'),
            ('Resident models: ' + (($resident -join ', ') + '')),
            'Project rule: never risk the machine. Free it first (ollama stop <model>, close ComfyUI).'
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
    # Warm through the SAME path a real turn uses, prompt file included. A warm-up that skips the
    # system prompt proves nothing: the 2026-08-04 failure was triggered by the prompt's own length,
    # so a short bespoke warm-up passed while all 22 real turns failed.
    $warm = $null
    try { $warm = Invoke-Model (Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\act.md') -Raw) 'Reply with the single word ok.' $null } catch {
        Die @(
            ('model ' + $Model + ' is pulled but will not run.'),
            ('ollama said: ' + $_.Exception.Message),
            'If this is an architecture error, that model is unsupported by this ollama build -- pick another.'
        )
    }
    if (-not $warm) { Die @(('model ' + $Model + ' returned nothing on a warm-up request through the real prompt path.')) }
}

# --- Launch the client --------------------------------------------------------------------------
$godot = $env:GODOT_BIN
if (-not $godot) { $godot = 'C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe' }
if (-not (Test-Path $godot)) { Die @(('Godot not found at ' + $godot + '. Set GODOT_BIN.')) }

$env:AGENT_PLAYTEST = '1'
$env:AGENT_PLAYTEST_DIR = $OutDir
# PlaytestLog.cs (godot/scripts/PlaytestLog.cs) is opt-in, gated on this var alone -- unset, an
# automated run left NO reconstructable trail of what the client actually did (day/phase/beat,
# every action, every phase transition and its cause), only this script's own turn-by-turn digest.
$env:MM_PLAYTEST_LOG = $playtestLogPath
# The client's wait for our command.json MUST be sized off the SAME numbers this driver actually
# uses to produce one -- a worst-case turn here is ModelCallMaxAttempts full ModelCallTimeoutSec
# calls back to back (a stuck/overloaded ollama can burn all three), so that is what we tell it to
# wait for, computed, not retyped. See AgentPlaytest.cs's DefaultCommandTimeoutMs doc for the
# fallback this env var overrides and the run (Scout-5, 2026-08-09 sweep) that mismatch cost.
$env:AGENT_PLAYTEST_TIMEOUT_MS = [string]($ModelCallMaxAttempts * $ModelCallTimeoutSec * 1000)
Say ('launching client (out: ' + $OutDir + ', playtest log: ' + $playtestLogPath + ')')
# The SCENE must be named explicitly. `--path godot` alone boots the game's main scene, so the
# bridge never runs and the driver waits out its timeout on a client that was never asked to play --
# measured on the first scripted run, which sat for 90s and then reported "scripted run complete".
$proc = Start-Process -FilePath $godot -ArgumentList @('--path', (Join-Path $RepoRoot 'godot'), 'res://agentplaytest.tscn') -PassThru

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

# A1 honesty counters. A run that mostly pressed advance must not read like a run the model
# played -- these three numbers are what let the header and the exit code tell the difference.
$modelDrivenTurns = 0
$fallbackTurns = 0
$imagelessTurns = 0
$imageMissingThisTurn = $false

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


$actPrompt = ''
$judgePrompt = ''
$diffScopeInfo = $null
if (-not $Scripted) {
    $actPrompt = Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\act.md') -Raw

    # A5: Scout is judged by a different prompt -- the design-doc-seeded question about decisions
    # and boredom, not the bug-hunting judge.md. Full and Diff keep judge.md unchanged.
    $judgePromptFile = 'agent-playtest\prompts\judge.md'
    if ($Scope -eq 'Scout') { $judgePromptFile = 'agent-playtest\prompts\scout-judge.md' }
    $judgePrompt = Get-Content (Join-Path $PSScriptRoot $judgePromptFile) -Raw

    # A4: Diff appends "what changed today" to the SYSTEM prompt, so it rides along on every turn
    # rather than only the first one. No new judgement machinery -- this is a prompt and a map.
    if ($Scope -eq 'Diff') {
        $changedFiles = Get-ChangedFilesAgainstMain -RepoRoot $RepoRoot
        $diffScopeInfo = Get-ScopeDiffSection -ChangedFiles $changedFiles
        $actPrompt = $actPrompt + [Environment]::NewLine + [Environment]::NewLine + '## Scope: Diff' + [Environment]::NewLine + $diffScopeInfo.Text
        if ($diffScopeInfo.FellBack) {
            Warn ('SCOPE DIFF FELL BACK TOWARD A FULL SWEEP -- ' + $diffScopeInfo.UnresolvedCount + ' of ' +
                $diffScopeInfo.ChangedCount + ' changed file(s) unmapped, or no diff was found against origin/main.')
        } else {
            Say ('scope: Diff -- ' + $diffScopeInfo.ChangedCount + ' changed file(s), all mapped to a surface')
        }
    } elseif ($Scope -eq 'Scout') {
        $actPrompt = $actPrompt + [Environment]::NewLine + [Environment]::NewLine +
            '## Scope: Scout' + [Environment]::NewLine +
            'This run also judges whether the game is doing its job, using a question seeded from the ' +
            'design docs at the end. Play exactly as you always would -- explore, go inside things, ' +
            'follow the tutorial -- do not change how you act because of this note.'
    }
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
        # The digest MUST include where the player is standing. Without it, walking across town reads
        # as four identical turns, so the detector fired STUCK on a model that was moving correctly and
        # then nudged it away from walking -- the harness manufacturing the symptom it exists to report.
        # Nearest-target distance is the position proxy: it changes as the player walks and needs no
        # new field. Rounded to 16px so sub-pixel drift is not mistaken for progress.
        $enabled = @($state.controls | Where-Object { $_.enabled } | ForEach-Object { $_.name })
        $whereabouts = ''
        if ($state.nearby -and @($state.nearby).Count -gt 0) {
            $nearest = @($state.nearby)[0]
            $whereabouts = $nearest.key + '@' + [math]::Round($nearest.distance / 16)
        }
        $digest = ($state.phase + '|' + $state.location + '|' + $whereabouts + '|' + (($state.screenText) -join ';') + '|' + ($enabled -join ','))
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
            $recentLines = @()
            if ($history.Count -gt 0) {
                $recentLines = $history[[math]::Max(0, $history.Count - 6)..($history.Count - 1)]
            }
            # Surroundings, the interact prompt, and the beat all come straight off state.json (see
            # Build-ActUserText in agent-playtest\turn-prompt.ps1), so this narrates the world, never
            # invents it. Extracted to its own file so the beat-wiring fix below is provable with a
            # stubbed state object instead of a live Godot+ollama run -- see
            # tools/test-agent-playtest-modes.ps1.
            $userText = Build-ActUserText -State $state -Turn $turn -Turns $Turns -RecentHistory $recentLines

            $attempts = 0
            $imageMissingThisTurn = $false
            while ($attempts -lt $ModelCallMaxAttempts -and -not $command) {
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
                    # Match on LABEL too. llava repeatedly answered {"action":"Buy 2 copper"} -- the on-screen
                    # label, not the control name. That is a correct intention in the wrong field.
                    $match = @($state.controls | Where-Object { $_.name -ieq $parsed.action -or $_.label -ieq $parsed.action }) | Select-Object -First 1
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
                $fallbackTurns++
            } else {
                $modelDrivenTurns++
            }
            if ($imageMissingThisTurn) { $imagelessTurns++ }
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
        # Scout launches a SECOND Godot process later (FullPlaytest) once this one is gone -- the
        # machine's gdUnit/Godot runtime is serialized (see tools/engine-test.ps1's own trap 1), so
        # this waits for the OS to actually finish tearing this one down before anything else starts.
        try { $proc.WaitForExit(10000) } catch { }
    }
    $env:AGENT_PLAYTEST = ''
    $env:AGENT_PLAYTEST_DIR = ''
    $env:MM_PLAYTEST_LOG = ''
    $env:AGENT_PLAYTEST_TIMEOUT_MS = ''
}

Say ('stopped after ' + $turn + ' turns: ' + $stopReason)

# A1: this is the number the whole unit exists to produce. "The model played N turns" and "the
# model failed N times and the driver pressed advance N times" used to write an identical
# findings.md and an identical exit code. They no longer do.
$degradeFloor = 0.25
$fallbackRatio = 0.0
if ($turn -gt 0) { $fallbackRatio = [double]$fallbackTurns / [double]$turn }
$degraded = ($turn -gt 0) -and ($fallbackRatio -gt $degradeFloor)
$fallbackPct = [math]::Round($fallbackRatio * 100, 1)

Say ('model-driven turns: ' + $modelDrivenTurns + ', fallback turns: ' + $fallbackTurns + ' (' + $fallbackPct + '%), imageless turns: ' + $imagelessTurns + ' of ' + $turn + ' total')
if ($degraded) {
    Warn ('DEGRADED: fallback ratio ' + $fallbackPct + '% exceeds the ' + ($degradeFloor * 100) + '% floor -- this run mostly pressed advance, not played')
}

# A6 (2026-08-09 overnight sweep): DEGRADED (above) measures FIDELITY -- of the turns that
# happened, how many were the model actually playing. It says nothing about QUANTITY, so a run
# whose client died, hung, or was talked into quitting after turn 1 has a fallback ratio of 0/1 =
# 0% and reads as pristine -- the exact shape of self-flattery DEGRADED already exists to catch, in
# a new place. Four runs from the same sweep proved it, all verdict=ok exit=0: Scout-5 (1 of 80
# turns), Scout-10 (9 of 80), Full-1 (25 of 80), Full-5 (36 of 80). See
# tools/agent-playtest/completion.ps1 for the verdict logic and its own test coverage.
#
# Floor chosen at 50%: half the requested budget is the plain-language line for "materially fewer
# turns than its budget" the owner asked for, and it is the smallest floor that catches every one
# of the four measured runs above (worst surviving case is Full-5 at 45%). It does NOT care WHY a
# run stopped short -- a client timeout (Scout-5), a model that gave up and issued its own "stop"
# after repeated refusals (Scout-10), or anything else all cost the SAME thing: a run that was
# supposed to observe most of a campaign and instead observed a sliver of it. stopReason (already
# in the header) still carries the specific cause; this flag carries the fact that the cause
# mattered enough to fail the run.
$completionFloor = 0.5
$completionVerdict = Get-CompletionVerdict -Turn $turn -Turns $Turns -Scripted:$Scripted -Floor $completionFloor
$completionRatio = $completionVerdict.Ratio
$completionPct = $completionVerdict.PercentText
$incomplete = $completionVerdict.Incomplete
if ($incomplete) {
    Warn ('INCOMPLETE: only ' + $turn + ' of ' + $Turns + ' budgeted turns ran (' + $completionPct + '%), under the ' +
        ($completionFloor * 100) + '% floor -- stopped early (' + $stopReason + '). Findings below cover a fraction of the intended run.')
}

# --- Judge pass ---------------------------------------------------------------------------------
$fullLog = ''
if (Test-Path $turnlogPath) { $fullLog = Get-Content $turnlogPath -Raw }
if (-not $fullLog) { $fullLog = ($history -join [Environment]::NewLine) }

# Cap what the JUDGE is sent. The bridge's turnlog carries the whole screen digest per turn, so it
# reaches tens of KB fast (57 KB in 22 turns, measured), and a 7B model's context is the smaller
# constraint anyway. The FULL log is still written to findings.md below -- only the model's copy is
# trimmed, and it is trimmed from the FRONT so the most recent turns survive.
$log = $fullLog
$judgeCap = 6000
if ($log.Length -gt $judgeCap) {
    $log = '(earlier turns trimmed)' + [Environment]::NewLine + $log.Substring($log.Length - $judgeCap)
    Say ('judge input trimmed to last ' + $judgeCap + ' chars of ' + $fullLog.Length)
}

# Every scope names itself here, so a report can never be mistaken for a different scope's --
# see this file's own .PARAMETER Scope doc for what Full/Diff/Scout each answer. Both DEGRADED
# (fidelity: the turns that happened were mostly fallback) and INCOMPLETE (quantity: too few turns
# happened at all) can fire independently or together -- the title carries whichever apply so a
# report can never be mistaken for a clean one.
$titleTags = @()
if ($degraded) { $titleTags += 'DEGRADED' }
if ($incomplete) { $titleTags += 'INCOMPLETE' }
$titleLine = '# Agent playtest findings (Scope: ' + $Scope + ')'
if ($titleTags.Count -gt 0) {
    $titleLine = '# ' + ($titleTags -join ' AND ') + ' -- agent playtest findings (Scope: ' + $Scope + ')'
}

$header = @(
    $titleLine,
    '',
    ('- scope: ' + $Scope),
    ('- model: ' + $Model),
    ('- turns: ' + $turn + ' (stopped: ' + $stopReason + ')'),
    ('- completion: ' + $turn + ' of ' + $Turns + ' budgeted turns (' + $completionPct + '%)'),
    ('- model-driven turns: ' + $modelDrivenTurns),
    ('- fallback turns: ' + $fallbackTurns + ' (' + $fallbackPct + '% of total)'),
    ('- imageless turns: ' + $imagelessTurns),
    ('- artifacts: ' + $OutDir),
    ('- playtest log (day/phase/beat/cause per tick, every action): ' + $playtestLogPath),
    ''
)
if ($Scope -eq 'Diff' -and $diffScopeInfo) {
    $fallBackNote = ''
    if ($diffScopeInfo.FellBack) { $fallBackNote = ' (FELL BACK to a full sweep -- see below)' }
    $header += @(
        ('- diff scope: ' + $diffScopeInfo.ChangedCount + ' changed file(s) vs origin/main, ' +
            $diffScopeInfo.UnresolvedCount + ' unmapped' + $fallBackNote),
        ''
    )
}
if ($degraded) {
    $header = @(
        ('DEGRADED: ' + $fallbackTurns + ' of ' + $turn + ' turns (' + $fallbackPct + '%) fell back to ' +
         '"advance" because the model gave no usable command. That is over the ' + ($degradeFloor * 100) +
         '% floor -- this run mostly pressed advance, not played, and its findings below should be read ' +
         'with that in mind.'),
        ''
    ) + $header
}
if ($incomplete) {
    $header = @(
        ('INCOMPLETE: only ' + $turn + ' of ' + $Turns + ' budgeted turns ran (' + $completionPct + '%), under ' +
         'the ' + ($completionFloor * 100) + '% floor. Stopped early -- ' + $stopReason + '. Whatever findings ' +
         'follow are a PARTIAL sample of the intended run, not a completed sweep; do not read them as if the ' +
         'campaign was actually played out.'),
        ''
    ) + $header
}

# A run that played NOTHING is a failure, whatever mode it was in. The first scripted run sat for 90s
# on a client that had never been asked to play, then printed "scripted run complete" and exited 0 --
# the same shape of lie as a truncated test suite reporting "Passed!". Never again from this script.
if ($turn -eq 0) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('NOTHING WAS PLAYED. ' + $stopReason))
    Die @(
        ('zero turns were played: ' + $stopReason),
        '',
        'The client never wrote a state file. Usual causes, in order:',
        '  1. the scene was not launched (the bridge only runs in res://agentplaytest.tscn)',
        '  2. AGENT_PLAYTEST / AGENT_PLAYTEST_DIR did not reach the process',
        '  3. the client crashed on boot -- check for a Godot error window',
        ('Artifacts (may be empty): ' + $OutDir)
    )
}

if ($Scripted) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('Scripted run -- no model judged this. The channel was exercised, including one deliberate illegal press.', '', '## Turn log', '', $log))
    Say ('scripted run complete, ' + $turn + ' turns. Channel log: ' + $findingsPath)
    exit 0
}

# A5: Scout's mechanical half. Deliberately sequential and deliberately BEFORE the judge call, so
# a failed judge pass (Die, below) still leaves a findings.md carrying whatever the detectors found
# -- the two halves are independent evidence and one going wrong must never cost the other. The act
# loop's own Godot process is fully gone by now (see the WaitForExit in the finally block above),
# so this is not a second concurrent runtime.
$mechanicalSection = @()
if ($Scope -eq 'Scout') {
    Say 'scope: Scout -- running mechanical detectors (FullPlaytest, Playtest3dRecorder)'
    $mechanicalText = Invoke-MechanicalDetectors -RepoRoot $RepoRoot -OutDir $OutDir -Godot $godot -TimeoutMinutes $MechanicalTimeoutMin
    $mechanicalSection = @('', '---', '') + @($mechanicalText)
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
# No image on the judge pass: the LOG is what carries the findings, and a 134 KB frame costs
# context that the log needs. Visual findings come from the act turns, which do see frames.
try { $findings = Invoke-Model $judgePrompt (($judgeInput) -join [Environment]::NewLine) $null } catch { Warn ('judge call failed: ' + $_.Exception.Message) }

if (-not $findings) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('JUDGE FAILED -- no findings written. Raw turn log below.') + $mechanicalSection + @('', $log))
    Die @('the judge pass produced nothing. The turn log is still in ' + $findingsPath + '.')
}

# ---------------------------------------------------------------------------
# Fabrication guard. A 7B judge invents defects that read as real ones, and asking it to
# "quote the log" does not stop it: one run reported that the screen said OFFRED and should say
# OFFERED, when the log contains OFFERED thirteen times and OFFRED nowhere in the game or the run.
# A fabricated finding is worse than none -- it sends you fixing what was never observed -- so
# every claim about on-screen text is now CHECKED against the log instead of trusted.
#
# The check is deliberately narrow: SCREAMING_CASE tokens and control-name-shaped words are the
# things a judge quotes verbatim and the things we can verify mechanically. Prose is left alone,
# because flagging ordinary English would bury the real signal.
$unsupported = @()
$logHaystack = $fullLog.ToUpperInvariant()
$quoted = [regex]::Matches($findings, '(?<![A-Za-z0-9_])[A-Z][A-Z0-9_]{3,}(?![A-Za-z0-9_])')
foreach ($m in $quoted) {
    $token = $m.Value
    if ($logHaystack.Contains($token)) { continue }
    if ($unsupported -contains $token) { continue }
    $unsupported += $token
}

$guardNote = @()
if ($unsupported.Count -gt 0) {
    Write-Host ''
    Warn ('FABRICATION GUARD: ' + $unsupported.Count + ' quoted token(s) appear nowhere in the turn log:')
    foreach ($t in $unsupported) { Warn ('  ' + $t + '  <- not in the log; treat this finding as invented until a human confirms it') }
    $guardNote = @(
        '',
        '## Fabrication guard',
        '',
        ('These tokens are quoted in the findings above but appear NOWHERE in the turn log, so the ' +
         'findings that rely on them are unsupported by anything the agent actually saw:'),
        ''
    ) + ($unsupported | ForEach-Object { '- `' + $_ + '`' })
}

Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @($findings) + $guardNote + $mechanicalSection + @("", "---", "", "## Turn log", "", $fullLog))

Write-Host ''
Say ('findings written: ' + $findingsPath)
Write-Host ''
Write-Host $findings
Write-Host ''
Warn 'Read these before trusting them. The acceptance bar for this harness is that it independently'
Warn 'names something a human would also flag. Vacuous praise means the prompts need work, not that'
Warn 'the game is fine.'
if ($unsupported.Count -gt 0) {
    Warn 'At least one finding quotes text the agent never saw -- see the fabrication guard above.'
}
if ($incomplete) {
    Warn ('INCOMPLETE run: only ' + $turn + ' of ' + $Turns + ' budgeted turns ran (' + $completionPct + '%), stopped early (' + $stopReason + '). Exiting non-zero.')
}
if ($degraded) {
    Warn ('DEGRADED run: ' + $fallbackTurns + ' of ' + $turn + ' turns (' + $fallbackPct + '%) were the driver pressing advance, not the model playing. Exiting non-zero.')
}
if ($degraded -or $incomplete) {
    exit 1
}
exit 0
