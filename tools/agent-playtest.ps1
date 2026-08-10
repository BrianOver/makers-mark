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
    ollama act-model tag. Default qwen3-vl:8b (W1, docs/plans/2026-08-10-002): llava:7b's OCRBench
    score (~33%) confounds every downstream finding that depends on the model actually reading the
    screen -- a "the model got stuck" report and "the model could not read the button label" report
    look identical in the turn log if the model cannot see text at all. qwen3-vl:8b passed the
    three-frame token-overlap smoke gate that plan's W1 requires before this default could change;
    see that run's own quoted transcriptions for the evidence. llava:7b is still reachable with
    -Model llava:7b (ruling 5's quarantine: if a future model change ever fails the gate, drop back
    to it and file the finding, never force a bad default through silently).

    NOT llama3.2-vision:11b, even though it is pulled and bigger. This ollama build cannot load it:
        {"error":"llama-server process has terminated: exit status 1:
                  error loading model: unknown model architecture: 'mllama'"}
    Measured 2026-08-04. Being PULLED is not being LOADABLE -- which is why the gate below warms the
    model with a real one-token request instead of only checking /api/tags.

.PARAMETER JudgeModel
    ollama model tag for the END-OF-RUN judge pass only. Default qwen3:14b -- a dedicated text model,
    deliberately NOT the vision model that just played: VLM fine-tuning measurably degrades a model's
    text-only quality (NVLM), and the judge never sees an image (see the judge-pass comment below),
    so there is no reason to pay that tax on the pass that writes the actual findings prose.

    qwen3-vl:8b (~6.1 GB) + qwen3:14b (~9.3 GB) sum to ~15.4 GB, over this project's 14 GB VRAM
    ceiling if both stayed resident across the handoff (ruling 10, docs/plans/2026-08-10-002) -- so
    this driver explicitly unloads $Model (ollama stop) before ever calling $JudgeModel, and logs
    `ollama ps` on both sides of that unload rather than assuming ollama's own eviction would have
    caught it in time.

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

.PARAMETER FrameEvery
    Keep every Nth turn's frame under <OutDir>/frames/ (default 1 -- keep all of them). Before this,
    frame.png was overwritten every turn, so an 80-turn run left exactly ONE screenshot on disk: the
    last turn, usually the least interesting one. A long run can pass a higher value to thin the kept
    set; turn 1 is always kept regardless of the value. The kept-frame count is in findings.md's
    header, and turnlog.md's own per-turn entries say which frame (or that none was kept/available)
    each note is about.

.PARAMETER Persona
    Which player this run is pretending to be: first-timer, veteran, speedrunner, completionist,
    or random (picks one of those for this run). act.md carries the JSON contract and movement rules
    that never change; prompts/agent-playtest/prompts/personas/<name>.md supplies the KNOWLEDGE and
    GOAL half. One persona ("curious, slightly impatient") used to drive every run, which measured
    the same player thirty times over a thirty-run sweep. An unrecognized value fails loudly rather
    than silently becoming the default -- see personas.ps1's own note.

    sceptic is RETIRED (W3, docs/plans/2026-08-10-002, ruling 6): the dead-verb detector below (see
    -FrameEvery and the "## Dead-verb candidates" findings.md section) runs under every persona and
    catches what sceptic could only ever narrate in prose, without the fabrication risk of a model
    inventing doubt about a turn that worked fine.

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
    [string]$Model = 'qwen3-vl:8b',
    [string]$JudgeModel = 'qwen3:14b',
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
    [int]$MechanicalTimeoutMin = 15,
    [int]$FrameEvery = 1,
    [string]$Persona = 'first-timer'
)

# A4/A5/A6: the diff-to-surface map, the per-turn prompt builder, Scout's mechanical detectors, and
# the completion-floor verdict are split into their own dot-sourced files for one reason -- they
# need no Godot, no ollama, and no VRAM to prove, and this script needs all three. See
# tools/test-agent-playtest-modes.ps1. frames/backend/coverage/personas (playtest-harness wave, U1-U4)
# join them for the same reason.
. (Join-Path $PSScriptRoot 'agent-playtest\scope-map.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\turn-prompt.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\mechanical.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\completion.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\frames.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\backend.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\coverage.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\personas.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\model-call.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\footer.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\deadverb.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\metrics.ps1')

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

# U4: resolve -Persona before anything expensive (GPU gate, model warm-up, launching Godot) runs --
# a typo'd persona name is a configuration error, and the cheapest possible check should fail first.
# Resolve-PersonaChoice (personas.ps1) throws on anything not in its known list; caught here and
# turned into the same loud Die() every other refusal in this file uses, rather than a raw stack
# trace. "random" resolves to one concrete name now, once, so $personaName is stable for the rest of
# this run (Say below records BOTH the requested value and the resolved one for that reason).
try {
    $personaName = Resolve-PersonaChoice -Persona $Persona
} catch {
    Die @($_.Exception.Message)
}
Say ('persona: ' + $personaName + ' (requested: ' + $Persona + ')')

# JsonEsc/Build-ModelRequestBody/Get-LegalCommandFromReply now live in agent-playtest\model-call.ps1
# (W1, docs/plans/2026-08-10-002) so tools/test-agent-playtest-modes.ps1 can prove the request body and
# the reply-legality check with hand-built strings -- no ollama/Godot/VRAM. See that file's own header
# for the ConvertTo-Json ban this still honors and for why the old NORMALIZE block + regex JSON-extract
# that used to live in this script's per-turn loop are gone rather than moved.

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

function Invoke-Model($systemPrompt, $userText, $imagePath, $modelOverride, $formatSchema) {
    $imageB64 = $null
    if ($imagePath) {
        if (Test-Path $imagePath) {
            $imageB64 = [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($imagePath))
        } else {
            # A caller that passed a path meant to attach a frame. A missing frame silently
            # becoming a text-only request used to be invisible; now it is warned and counted
            # by the caller via $imageMissingThisTurn. Callers that intentionally send no image
            # (warm-up, judge pass) pass $null and never reach this branch.
            $script:imageMissingThisTurn = $true
            Warn ('frame missing at model-call time: ' + $imagePath + ' -- sending text-only request')
        }
    }

    $modelForCall = $Model
    if ($modelOverride) { $modelForCall = $modelOverride }

    # W1 ruling: temperature 0 rides ONLY on schema-constrained act calls, never the warm-up or judge
    # calls -- see Build-ModelRequestBody's own doc (agent-playtest\model-call.ps1) for why the two are
    # decoupled rather than one flag meaning both.
    $temperature = -1
    if ($formatSchema) { $temperature = 0 }

    # num_ctx must be set explicitly. llava:7b defaults to a 4096-token context and ollama HARD
    # ERRORS past it rather than truncating:
    #   {"error":{"code":400,"message":"request (6052 tokens) exceeds the available context size
    #             (4096 tokens), try increasing it","type":"exceed_context_size_error"}}
    # Measured 2026-08-04 on the judge pass. A screen digest plus a turn log passes 4096 quickly, so
    # without this the harness works for a while and then dies exactly when it has something to say.
    $body = Build-ModelRequestBody -Model $modelForCall -SystemPrompt $systemPrompt -UserText $userText `
        -ImageBase64 $imageB64 -NumCtx $NumCtx -FormatSchema $formatSchema -Temperature $temperature

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
# U1: every frame the model saw, not just the last one -- frame.png itself is still overwritten each
# turn by the client (AgentPlaytestBridge.RunLoop), so this is a SEPARATE directory the driver copies
# into after each turn's frame has been used, not a rename of frame.png's own path.
$framesDir       = Join-Path $OutDir 'frames'
# U2/U3: the backend record and the coverage census -- written once at the end, alongside everything
# else this run produces, so one folder still answers "what happened in this run."
$backendJsonPath   = Join-Path $OutDir 'backend.json'
$coverageMdPath    = Join-Path $OutDir 'coverage.md'
$coverageJsonPath  = Join-Path $OutDir 'coverage.json'
# W3: a single provisional holding spot for the ONE press turn awaiting its dead-verb verdict at any
# given moment (turns are strictly sequential, so at most one is ever pending -- see deadverb.ps1's
# own frame-retention header). Not a "stale run" artifact in the same sense as the others below (it
# is always resolved -- promoted or deleted -- before the next press turn stages into it), but a run
# that dies mid-verdict could leave one behind, so it gets the same prior-run cleanup.
$deadVerbStagingPath = Join-Path $OutDir 'deadverb-staging.png'
# W2 (docs/plans/2026-08-10-002): mechanical fun metrics -- per-day entropy, legal-vs-chosen ratio,
# the refusal frustration map, the product-sentence counter (metrics.ps1). Same one-folder-answers-
# everything convention as backend.json/coverage.json above.
$metricsJsonPath   = Join-Path $OutDir 'metrics.json'

foreach ($stale in @($statePath, $cmdPath, $framePath, $turnlogPath, $findingsPath, $driverLog,
        $playtestLogPath, $backendJsonPath, $coverageMdPath, $coverageJsonPath, $metricsJsonPath,
        $deadVerbStagingPath)) {
    if (Test-Path $stale) { Move-Item $stale ($stale + '.prev') -Force }
}
# frames/ is a directory, not a single file -- Move-Item -Force cannot rename it onto an existing
# ".prev" directory (a PowerShell/.NET limitation on directories, not files), so it is cleared
# outright instead. Every frame it ever held is also sitting in this run's own findings, so nothing
# is lost that the previous run's OWN artifacts do not already carry.
if (Test-Path $framesDir) { Remove-Item $framesDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $framesDir -Force | Out-Null

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
    # Checked here, before ANY turn runs, for the same reason $Model is: the judge call happens only
    # after the whole turn budget is spent, and finding out THEN that $JudgeModel was never pulled
    # would waste the entire run instead of one warm-up request.
    if ($have -notcontains $JudgeModel) {
        Die @(
            ('judge model ' + $JudgeModel + ' is not pulled. Available: ' + ($have -join ', ')),
            ('Pull it (ollama pull ' + $JudgeModel + ') or pass -JudgeModel with one of the above.')
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

# U1: kept-frame bookkeeping. $frameNoteByTurn is turned into turnlog.md's own per-turn frame lines
# once the client has fully exited (see the note by Add-FrameReferencesToTurnLog itself for why that
# ordering is not optional).
$keptFrameCount = 0
$missingFrameCount = 0
$frameNoteByTurn = @{}

# U2: the driver's own per-turn record of accepted-vs-refused, bucketed by day+phase at the moment
# the outcome was OBSERVED (the turn after the command that produced it -- state.lastOutcome always
# reports the PRECEDING command's result). Cross-referenced against the backend log's own rejections
# after the run (Get-DriverBackendMismatches, backend.ps1) to catch a UI/kernel disagreement.
$driverTurns = New-Object System.Collections.ArrayList

# W2 (docs/plans/2026-08-10-002 "the playtest becomes a player"): metrics.ps1's own per-turn record --
# one entry per turn, chronological by construction, carrying everything Get-PerDayActionEntropy,
# Get-LegalVsChosenByPhase, Get-ProductSentenceReport, and Build-PerDayJudgeDigest each need. Built
# alongside $driverTurns rather than replacing it -- $driverTurns' Day/Phase/Accepted shape is already
# load-bearing for Get-DriverBackendMismatches and this file adds a second, richer record instead of
# reshaping that one underneath it.
$turnRecords = New-Object System.Collections.ArrayList
# Every driver-side pre-send refusal (Get-LegalCommandFromReply catching an illegal press/key/move
# before it ever reaches the client) -- one entry per REFUSED ATTEMPT, not per turn (a single turn can
# refuse more than once across its retry attempts before either landing on a legal command or falling
# back to advance). Feeds Get-RefusalFrustrationMap.
$preRefusals = New-Object System.Collections.ArrayList

# U3: what this run actually touched, derived against the real registries read from source (never a
# hand-typed list -- see coverage.ps1's own header note). Registries are read once, up front: they
# are pure function of the checked-out code, not of anything that happens during the run.
$coverageRegistries = Get-CoverageRegistries -RepoRoot $RepoRoot
$coverageTracker = New-CoverageTracker

# W3 (docs/plans/2026-08-10-002): the dead-verb detector. $pendingDeadVerb holds the ONE press turn
# still awaiting its verdict (see deadverb.ps1's own header for why the verdict is necessarily one
# turn behind the press); $deadVerbCandidates collects the CANDIDATE lines findings.md renders.
$pendingDeadVerb = $null
$deadVerbCandidates = New-Object System.Collections.ArrayList

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
$actPromptHash = ''
$judgePrompt = ''
$diffScopeInfo = $null
$actionSchemaJson = ''
if (-not $Scripted) {
    # W1: JSON-schema constrained decoding on every act call (never the warm-up or judge call -- see
    # Invoke-Model's own temperature note). Read once, trimmed to one compact blob so it splices
    # cleanly into the hand-built request body (Build-ModelRequestBody, model-call.ps1). Parsed here
    # and only here, purely as a guard: a malformed schema file should fail loudly before the first
    # real model call, not surface as a mysterious ollama 400 twenty turns in.
    $actionSchemaJson = (Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\action-schema.json') -Raw).Trim()
    try { $null = $actionSchemaJson | ConvertFrom-Json } catch {
        Die @(('action-schema.json does not parse as JSON: ' + $_.Exception.Message))
    }

    $actProtocolText = Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\act.md') -Raw
    # U4: act.md is PROTOCOL only (the {{PERSONA}} marker); Build-PersonaActPrompt (personas.ps1)
    # substitutes in $personaName's own KNOWLEDGE+GOAL text. Throws loudly (never silently plays a
    # protocol-only prompt) if the marker or the persona file ever goes missing.
    $actPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName $personaName `
        -PersonasDir (Join-Path $PSScriptRoot 'agent-playtest\prompts\personas')
    $actPromptHash = Get-PromptHash -Text $actPrompt
    Say ('act-prompt hash: ' + $actPromptHash + ' (persona: ' + $personaName + ')')

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

        # U2: record accepted-vs-refused for THIS observation, bucketed by the day/phase active when
        # the outcome landed (see Get-DriverBackendMismatches, backend.ps1, for why day+phase is the
        # join key rather than a single turn -- the control name a player presses is not the kernel
        # action-type name the backend log records, so no exact per-turn key exists between the two).
        [void]$driverTurns.Add([pscustomobject]@{
            Day      = $state.day
            Phase    = $state.phase
            Accepted = -not (([string]$state.lastOutcome).StartsWith('refused:'))
        })

        # W3: resolve the PREVIOUS press turn's dead-verb check now that ITS "after" state has
        # arrived -- $state above IS that after-state (see where $pendingDeadVerb is set, near the
        # frame-save call below, for why the verdict is necessarily one turn behind the press).
        if ($pendingDeadVerb) {
            $fingerprintAfter = Get-StateFingerprint -State $state
            $backendRowsNow = @((Read-BackendLogRows -LogPath $playtestLogPath).Rows)
            $deadVerbSlice = Get-BackendEventsForSlice -AllRows $backendRowsNow -RowCountBefore $pendingDeadVerb.BackendRowCountBefore
            $deadVerbVerdict = Get-DeadVerbVerdict -FingerprintBefore $pendingDeadVerb.FingerprintBefore `
                -FingerprintAfter $fingerprintAfter -BackendSlice $deadVerbSlice -Turn $pendingDeadVerb.Turn `
                -Phase $pendingDeadVerb.Phase -ControlName $pendingDeadVerb.ControlName
            if ($deadVerbVerdict.IsCandidate) {
                [void]$deadVerbCandidates.Add($deadVerbVerdict.Line)
                Warn $deadVerbVerdict.Line
            }
            if ($pendingDeadVerb.Staged) {
                $deadVerbFinalName = Get-KeptFrameFileName -Turn $pendingDeadVerb.Turn
                $deadVerbKept = Resolve-ProvisionalDeadVerbFrame -StagingPath $deadVerbStagingPath `
                    -FinalPath (Join-Path $framesDir $deadVerbFinalName) -IsCandidate $deadVerbVerdict.IsCandidate
                if ($deadVerbKept) {
                    $keptFrameCount++
                    $frameNoteByTurn[$pendingDeadVerb.Turn] = ('frame: frames/' + $deadVerbFinalName +
                        ' (kept: law-3 dead-verb candidate, overrides -FrameEvery)')
                }
            }
            $pendingDeadVerb = $null
        }

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

        # W2: this turn's own pre-refusal reasons, reset every iteration (both modes) -- Scripted never
        # populates it (it has no legality-check attempts loop at all), so it is always an empty array
        # there, which is the correct answer for a mode with no pre-send refusal concept.
        $turnPreRefusalReasons = New-Object System.Collections.ArrayList

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
                try { $reply = Invoke-Model $actPrompt $userText $framePath $null $actionSchemaJson } catch { Warn ('model call failed: ' + $_.Exception.Message) }

                # W1 (docs/plans/2026-08-10-002): with format=action-schema.json constraining decoding,
                # the reply can no longer arrive as prose-wrapped JSON or a folded verb -- the old
                # regex JSON-extract and the NORMALIZE block that used to sit here are DELETED, not
                # relocated (see agent-playtest\model-call.ps1's own header for the full argument).
                # Get-LegalCommandFromReply still catches what schema decoding cannot: an empty/failed
                # call, or (ruling 1, kept ON PURPOSE) a real verb aimed at a control that is disabled
                # right now -- an illegal press is signal the frustration map needs, so this refuses it
                # and feeds the reason back rather than silently rewriting it.
                $legal = Get-LegalCommandFromReply -Reply $reply -EnabledControls $enabled
                if ($legal.Refused) {
                    $userText = $userText + [Environment]::NewLine + ('REFUSED: ' + $legal.Reason + '. Enabled controls: ' + ($enabled -join ', '))
                    Warn ('turn ' + $turn + ' attempt ' + $attempts + ' refused: ' + $legal.Reason)
                    # W2: raw material for Get-RefusalFrustrationMap -- recorded here, at the ONE place
                    # that already knows both the reason text and which turn/phase it happened in,
                    # rather than re-deriving it later from Warn's own console/driver.log text.
                    [void]$preRefusals.Add([pscustomobject]@{
                        Turn    = $turn
                        Day     = $state.day
                        Phase   = $state.phase
                        Control = (Get-RefusalControlFromReason -Reason $legal.Reason)
                        Reason  = $legal.Reason
                    })
                    [void]$turnPreRefusalReasons.Add($legal.Reason)
                    continue
                }
                $command = $legal.Command
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

        # W2: metrics.ps1's per-turn record (see its own header). Day/Phase/ScreenText/EnabledControls
        # come off THIS turn's own $state (the context the action was chosen in); Action/Target/Why come
        # off the command actually decided above, whether that was the model's choice or the driver's
        # own advance-fallback.
        [void]$turnRecords.Add([pscustomobject]@{
            Turn            = $turn
            Day             = $state.day
            Phase           = $state.phase
            Action          = $parsedCmd.action
            Target          = $parsedCmd.target
            Why             = $parsedCmd.why
            Outcome         = $state.lastOutcome
            ScreenText      = @($state.screenText)
            EnabledControls = $enabled
            Refused         = (@($turnPreRefusalReasons).Count -gt 0)
            RefusalReason   = (@($turnPreRefusalReasons) | Select-Object -Last 1)
        })

        # U3: record what this turn's state showed and what got pressed, against the real registries
        # read from source at the top of this script (coverage.ps1's own Add-CoverageTouch).
        Add-CoverageTouch -Tracker $coverageTracker -State $state -Command $parsedCmd

        # U1: archive this turn's frame (or say plainly why none was kept) NOW -- right after the
        # model call that consumed frame.png for this turn's decision, and before the client
        # overwrites frame.png again on the next turn. $imageMissingThisTurn is set by Invoke-Model
        # itself when it could not find frame.png to attach; Save-TurnFrame double-checks Test-Path
        # too, since Scripted mode never calls Invoke-Model and so never sets that flag at all.
        $frameResult = Save-TurnFrame -SourcePath $framePath -FramesDir $framesDir -Turn $turn `
            -FrameEvery $FrameEvery -SourceMissing:$imageMissingThisTurn
        $frameNoteByTurn[$turn] = $frameResult.Note
        if ($frameResult.Kept) { $keptFrameCount++ }
        if ($frameResult.Missing) { $missingFrameCount++ }

        # W3: a press turn's dead-verb check needs the NEXT turn's state to resolve (see the
        # resolution block above, near the U2 driverTurns record) -- capture what THIS press looked
        # like before its outcome exists. Scripted is excluded on purpose: it has no persona, no
        # model, and its one press ($scriptedPlan above) is a DELIBERATE illegal one meant to be
        # refused, which would misfire this detector on a turn that was never a real player action.
        # If -FrameEvery already thinned this turn's frame, it is staged now (deadverb.ps1) since
        # frame.png will be the NEXT turn's screenshot by the time the verdict is known.
        if ((-not $Scripted) -and ($parsedCmd.action -eq 'press')) {
            $deadVerbStaged = $false
            if (-not $frameResult.Kept) {
                $deadVerbStaged = Save-ProvisionalDeadVerbFrame -SourcePath $framePath -StagingPath $deadVerbStagingPath
            }
            $pendingDeadVerb = [pscustomobject]@{
                FingerprintBefore     = (Get-StateFingerprint -State $state)
                BackendRowCountBefore = @((Read-BackendLogRows -LogPath $playtestLogPath).Rows).Count
                Turn                  = $turn
                Phase                 = [string]$state.phase
                ControlName           = [string]$parsedCmd.target
                Staged                = $deadVerbStaged
            }
        }

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
    # W3: the run ended (budget/timeout/error) with the LAST turn still a press awaiting its
    # dead-verb verdict -- there is no next state to resolve it against, so it is never asserted
    # either way (never fabricate), and any staged frame for it is discarded rather than left as an
    # orphan file with no verdict to explain it.
    if ($pendingDeadVerb -and $pendingDeadVerb.Staged) {
        Remove-Item -Path $deadVerbStagingPath -Force -ErrorAction SilentlyContinue
    }
    $env:AGENT_PLAYTEST = ''
    $env:AGENT_PLAYTEST_DIR = ''
    $env:MM_PLAYTEST_LOG = ''
    $env:AGENT_PLAYTEST_TIMEOUT_MS = ''
}

Say ('stopped after ' + $turn + ' turns: ' + $stopReason)

# U1: stamp turnlog.md with a frame reference (or an explicit "frame missing" line) per turn, NOW --
# once, after the client has fully exited. turnlog.md is rewritten WHOLESALE on every flush by the
# client's own AgentPlaytestBridge.RunLoop (File.WriteAllText from its in-memory StringBuilder, which
# has no idea this script exists), so annotating it any earlier would be erased on the very next
# flush. See Add-FrameReferencesToTurnLog's own doc (frames.ps1).
if (Test-Path $turnlogPath) {
    $rawTurnLogForFrames = Get-Content $turnlogPath -Raw
    $annotatedTurnLog = Add-FrameReferencesToTurnLog -TurnLogText $rawTurnLogForFrames -FrameNoteByTurn $frameNoteByTurn
    Set-Content -Path $turnlogPath -Value $annotatedTurnLog -Encoding utf8
}
Say ('frames kept: ' + $keptFrameCount + ' of ' + $turn + ' turn(s) (' + $framesDir + '), missing: ' + $missingFrameCount)

# U2: the backend record -- playtest-log.jsonl read and turned into evidence (backend.ps1). Computed
# here, before the judge pass, so a failed judge call still leaves this in findings.md (mirrors why
# Scout's mechanical section is computed before the judge call too).
$backendSummary = Get-BackendSummary -LogPath $playtestLogPath
# NOT wrapped in an extra @() here -- Get-AutoAdvanceContradictions/Get-DriverBackendMismatches
# already return via the leading-comma pattern (`,@(...)`, see backend.ps1's own ARRAY-RETURN note),
# so a caller-side @() around the CALL ITSELF double-wraps into a 1-element array whose one element
# is the real array -- measured directly: it turned a verified 3-line result into "Count = 1" here.
# The comma trick only needs undoing at the assignment boundary once; +'ing two already-correct
# arrays together needs no further wrapping.
$backendContradictions = (Get-AutoAdvanceContradictions -Summary $backendSummary) +
    (Get-DriverBackendMismatches -Summary $backendSummary -DriverTurns $driverTurns)
$backendMarkdown = Format-BackendMarkdown -Summary $backendSummary -Contradictions $backendContradictions
($backendSummary | ConvertTo-Json -Depth 8) | Set-Content -Path $backendJsonPath -Encoding utf8
if ($backendSummary.Available) {
    Say ('backend record: ' + $backendSummary.RowCount + ' row(s), ' + @($backendSummary.Rejections).Count +
        ' rejection(s), ' + $backendSummary.AutoAdvanceCount + ' auto-advance(s), ' +
        @($backendContradictions).Count + ' contradiction(s)')
} else {
    Warn ('backend record: ' + $backendSummary.Message)
}

# U3: the coverage census -- written as its own coverage.md/coverage.json (not folded into
# findings.md; the brief asks for standalone files here, unlike U2's backend section).
$coverageReport = Get-CoverageReport -Registries $coverageRegistries -Tracker $coverageTracker
$coverageMarkdown = Format-CoverageMarkdown -Report $coverageReport
Set-Content -Path $coverageMdPath -Value $coverageMarkdown -Encoding utf8
($coverageReport | ConvertTo-Json -Depth 8) | Set-Content -Path $coverageJsonPath -Encoding utf8
Say ('coverage: ' + $coverageReport.OverallTouched + ' of ' + $coverageReport.OverallTotal + ' surfaces touched (' + $coverageReport.OverallPercentage + '%) -- ' + $coverageMdPath)

# W2 (docs/plans/2026-08-10-002): mechanical fun metrics -- computed here, same as backend/coverage
# just above, so a run that dies before the judge pass still leaves metrics.json and its findings.md
# section behind. Depends only on $turnRecords/$preRefusals (built during the loop above) and
# $backendSummary (already computed) -- no model, no Godot.
$metricsSummary = Get-MetricsSummary -TurnRecords @($turnRecords) -PreRefusals @($preRefusals) -BackendSummary $backendSummary
$metricsMarkdown = Format-MetricsMarkdown -Metrics $metricsSummary
($metricsSummary | ConvertTo-Json -Depth 8) | Set-Content -Path $metricsJsonPath -Encoding utf8
Say ('metrics: ' + @($metricsSummary.PerDayEntropy).Count + ' day(s) of entropy data, product-sentence fired: ' +
    $metricsSummary.ProductSentence.ProductSentenceFired + ' -- ' + $metricsJsonPath)

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

# W2 (docs/plans/2026-08-10-002): the judge's own copy of the log is now a PER-DAY DIGEST
# (Build-PerDayJudgeDigest, metrics.ps1), not a character-count trim of the raw turnlog text. The
# defect the old $judgeCap tail-trim (6000, then W1's interim 24000) could never fix by raising the
# number further: trimming from the FRONT means a long run's judge input is always "whatever happened
# most recently" -- at 6000 chars the judge saw roughly the last 2-3 turns of a 57 KB log, so a
# question like "did day 11 change shape from day 2" was unanswerable in principle, because day 2 had
# already fallen off the front. The digest instead builds one block PER DAY from $turnRecords (built
# during the loop above) and, only if the total still exceeds the budget, thins EVERY day's block
# toward a floor rather than ever dropping a day outright -- see metrics.ps1's own header. The FULL
# raw turnlog is still written to findings.md below unabridged; only the model's own copy is a digest.
$judgeDigest = Build-PerDayJudgeDigest -TurnRecords @($turnRecords) -MaxChars 24000
$log = $judgeDigest.Text
Say ('judge input: per-day digest, ' + $judgeDigest.DayCount + ' day(s), ' + $judgeDigest.Length +
    ' chars (thinned within days: ' + $judgeDigest.Thinned + ')')

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

$personaHeaderLine = '- persona: ' + $personaName + ' (requested: ' + $Persona + ')'
if ($actPromptHash) { $personaHeaderLine = $personaHeaderLine + ', act-prompt hash ' + $actPromptHash }
if ($Scripted) { $personaHeaderLine = $personaHeaderLine + ' (Scripted mode -- no act prompt was built, no model was called)' }

$header = @(
    $titleLine,
    '',
    ('- scope: ' + $Scope),
    ('- model: ' + $Model),
    ('- judge model: ' + $JudgeModel),
    $personaHeaderLine,
    ('- turns: ' + $turn + ' (stopped: ' + $stopReason + ')'),
    ('- completion: ' + $turn + ' of ' + $Turns + ' budgeted turns (' + $completionPct + '%)'),
    ('- model-driven turns: ' + $modelDrivenTurns),
    ('- fallback turns: ' + $fallbackTurns + ' (' + $fallbackPct + '% of total)'),
    ('- imageless turns: ' + $imagelessTurns),
    ('- artifacts: ' + $OutDir),
    ('- playtest log (day/phase/beat/cause per tick, every action): ' + $playtestLogPath),
    ('- frames kept: ' + $keptFrameCount + ' of ' + $turn + ' turn(s) in ' + $framesDir +
        ' (-FrameEvery ' + $FrameEvery + '), missing: ' + $missingFrameCount),
    ('- coverage: ' + $coverageReport.OverallTouched + ' of ' + $coverageReport.OverallTotal +
        ' surfaces touched (' + $coverageReport.OverallPercentage + '%) -- see ' + $coverageMdPath),
    ('- product sentence (a MakersMark item named on the player''s own screen): ' +
        $metricsSummary.ProductSentence.ProductSentenceFired + ' -- see ' + $metricsJsonPath),
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

# U2: the "## Backend record" section, placed ABOVE the model's prose in every branch below --
# recorded facts first, the model's account second (the brief's own ordering requirement). Built
# once here so every Set-Content site below (zero-turn, Scripted, judge-failed, success) carries it
# identically rather than three call sites drifting apart from a fourth copy-paste.
$backendSection = @('', '---', '') + @($backendMarkdown)

# W3 (docs/plans/2026-08-10-002): the dead-verb detector's candidates -- built once here for the same
# reason $backendSection is, and carried by every Set-Content site below. A press turn only ever gets
# a candidate line if it was BOTH fingerprint-unchanged and backend-silent (see deadverb.ps1's own
# Get-DeadVerbVerdict); a run with none is reported as "no candidates" explicitly, bounded by what
# this run actually pressed -- never a clean bill for verbs the run never touched at all (see
# coverage.md for those).
$deadVerbLines = @('## Dead-verb candidates (law-3)', '')
if (@($deadVerbCandidates).Count -gt 0) {
    $deadVerbLines += @($deadVerbCandidates | ForEach-Object { '- ' + $_ })
} else {
    $deadVerbLines += ('no candidates among the press actions this run exercised -- bounded by ' +
        'what was actually pressed, never a clean bill for the whole game (see coverage.md for what ' +
        'this run never touched at all).')
}
$deadVerbSection = @('', '---', '') + $deadVerbLines

# W2: the "## Mechanical fun metrics" section -- BELOW the Backend record, ABOVE the model's prose
# (recorded facts, then measured facts, then the model's account last). Same build-once-use-four-times
# shape as $backendSection immediately above. Section order at every site: backend, metrics, dead-verb.
$metricsSection = @('', '---', '') + @($metricsMarkdown)

# W1: the honesty footer (agent-playtest\footer.ps1) -- computed once, appended to every Set-Content
# site below alongside $backendSection, so a run that dies at any stage still ships the same "here is
# what this instrument cannot see" note as a clean one.
$honestyFooterLines = Get-HonestyFooterLines

# A run that played NOTHING is a failure, whatever mode it was in. The first scripted run sat for 90s
# on a client that had never been asked to play, then printed "scripted run complete" and exited 0 --
# the same shape of lie as a truncated test suite reporting "Passed!". Never again from this script.
if ($turn -eq 0) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('NOTHING WAS PLAYED. ' + $stopReason) + $backendSection + $metricsSection + $deadVerbSection + $honestyFooterLines)
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
    # $fullLog (unabridged), not $log (the judge-only per-day digest) -- Scripted mode never calls a
    # judge at all ("no model judged this" on the very next line), so there is no reason to show a
    # human the compact judge-oriented digest instead of the real raw turnlog.md text here.
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + @('', '---', '', 'Scripted run -- no model judged this. The channel was exercised, including one deliberate illegal press.', '', '## Turn log', '', $fullLog) + $honestyFooterLines)
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

# Ruling 10 (docs/plans/2026-08-10-002): $Model (~6.1 GB, qwen3-vl:8b by default) and $JudgeModel
# (~9.3 GB, qwen3:14b by default) sum past this project's 14 GB VRAM ceiling if both stayed resident
# at once. Rather than trust ollama's own idle-eviction to win that race, explicitly release $Model
# first -- `ollama stop` is a real, synchronous CLI unload, simpler than a bespoke keep_alive=0 request
# whose reply would just be thrown away. `ollama ps` is logged on both sides of the unload (Say, so it
# lands in driver.log too) so a live run can be READ, not just assumed, at the exact handoff point.
$residentBefore = @()
try {
    $psBefore = Invoke-RestMethod -Uri ($Endpoint + '/api/ps') -TimeoutSec 10
    $residentBefore = @($psBefore.models | ForEach-Object { $_.name + ' (' + [math]::Round($_.size / 1GB, 1) + ' GB)' })
} catch { Warn ('ollama ps (pre-unload) failed: ' + $_.Exception.Message) }
Say ('ollama ps before judge handoff: ' + (($residentBefore -join ', ')))
# NOT `2>&1` on this call. Measured live (W1 verification): `ollama stop` writes its own progress
# spinner as ANSI control codes to STDERR even on a clean, successful unload -- confirmed by `ollama
# ps` actually showing $Model gone right after. Under this script's `$ErrorActionPreference = 'Stop'`,
# redirecting a native command's stderr with `2>&1` in Windows PowerShell 5.1 wraps each stderr write
# in a terminating NativeCommandError (see docs/debugging.md-adjacent lesson on this exact PS 5.1
# 2>&1 trap), so the unload was silently succeeding while this line reported it as failed. Leaving
# stderr unredirected here lets a REAL failure still surface as non-zero exit / thrown error without
# manufacturing a false one out of the spinner's own output.
try { & ollama stop $Model | Out-Null } catch { Warn ('ollama stop ' + $Model + ' failed: ' + $_.Exception.Message) }
$residentAfter = @()
try {
    $psAfter = Invoke-RestMethod -Uri ($Endpoint + '/api/ps') -TimeoutSec 10
    $residentAfter = @($psAfter.models | ForEach-Object { $_.name + ' (' + [math]::Round($_.size / 1GB, 1) + ' GB)' })
} catch { Warn ('ollama ps (post-unload) failed: ' + $_.Exception.Message) }
Say ('ollama ps after unloading ' + $Model + ', before judge call to ' + $JudgeModel + ': ' + (($residentAfter -join ', ')))

Say 'asking the model to write findings'
# W2: $log is now Build-PerDayJudgeDigest's own per-day digest (see this file's own comment above
# where it is built), covering every day of the run -- not a raw-text tail trim. Every turn line it
# does include still carries its real turn number, so judge.md/scout-judge.md's own "point at a
# specific turn number" rule stays honest even when a day has been thinned.
$judgeInput = @(
    'Here is a per-day digest of the session you just played (day, phase sequence, then one line',
    'per turn: action, outcome, any refusal, and up to two lines of on-screen text). A day with a',
    'lot of turns may have its MIDDLE thinned out (marked "N turn(s) omitted for length") -- every',
    'day that happened is represented, even if not every turn within it is.',
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
# $JudgeModel, not $Model -- see this file's own .PARAMETER JudgeModel doc for why the judge is a
# dedicated text model rather than the vision model that just played.
try { $findings = Invoke-Model $judgePrompt (($judgeInput) -join [Environment]::NewLine) $null $JudgeModel } catch { Warn ('judge call failed: ' + $_.Exception.Message) }

if (-not $findings) {
    # $fullLog here, matching the label -- "Raw turn log below" should mean the raw text, not $log
    # (the judge's own per-day digest input, which is what just failed to produce anything).
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + @('', '---', '', 'JUDGE FAILED -- no findings written. Raw turn log below.') + $mechanicalSection + @('', $fullLog) + $honestyFooterLines)
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

Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + @('', '---', '') + @($findings) + $guardNote + $mechanicalSection + @("", "---", "", "## Turn log", "", $fullLog) + $honestyFooterLines)

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
