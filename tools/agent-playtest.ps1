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

    Ignored when -BrainModel is set (split mode): the judge reuses the already-resident brain model
    instead (see -BrainModel's own doc).

.PARAMETER BrainModel
    "the playtest learns to finish" wave, U2 (owner finding 2026-08-11 + fable census: 58 of 58 model
    runs died on patience by day 3, ~1,190 of ~1,260 refusals were the 8B VISION model emitting
    semantically empty commands -- freeform compose-a-command is beyond it). Default qwen3:14b: when
    set (non-empty), the CHOICE call every turn goes to this dedicated reasoning model instead of
    $Model, which keeps doing what it has always done -- narrate the frame -- in single-model mode
    only. Empty string ('') means single-model mode: $Model narrates AND chooses, exactly today's
    behaviour, kept as the A/B control arm.

    RESIDENCY (ruling 10 + #452's unload discipline, made pure and testable in model-call.ps1's own
    Get-ModelResidencyPlan): qwen3-vl:8b (~6.1 GB) + qwen3:14b (~9.3 GB) sum to ~15.4 GB, over this
    project's ~14 GB VRAM ceiling if both stayed resident at once -- vision and brain do NOT swap in
    and out per turn (that would cost a model load/unload twice per turn, all run long, for no
    payoff worth the wall clock). Instead split mode never loads $Model at all: frame narration is
    SKIPPED, not swapped, for the whole run -- the per-turn choice call goes to $BrainModel with the
    state digest, screen text, and this turn's menu (see turn-prompt.ps1's own -NoImage note), which
    already carry the observable facts a model needs to choose. This is a real, named deviation from
    "the vision model narrates every turn" -- reported prominently, not silently absorbed. The judge
    pass also reuses $BrainModel (already warm), so split mode costs exactly ONE model load and ONE
    unload for the entire run -- FEWER swaps than single-model mode's own act-then-judge handoff, not
    more. -JudgeModel is ignored when this is set.

.PARAMETER PatienceMode
    "the playtest learns to finish" wave, U3. Quit (default) is today's exact behaviour: an exhausted
    patience meter ends the run. Sweep logs a would-have-quit MARKER instead (same Turn/Day/Phase
    fields a real quit uses, plus the same drain-history headline text) and CONTINUES to the turn
    budget -- the meter resets after each marker, so a run can log more than one over a long budget.
    tools/playtest-sweep.ps1 passes Sweep by default (a sweep exists to measure the rest of a long
    campaign, not stop the instant the model gets frustrated); this driver's own default stays Quit
    so a bare interactive run behaves exactly as it always has.

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

    -Persona monkey defaults this to 25 instead of 1 (ruling 4, docs/plans/2026-08-10-002) UNLESS you
    pass your own value explicitly -- ~134 KB/frame times monkey's usual 400-turn crash-census budget
    would otherwise be ~54 MB nobody reviews.

.PARAMETER Persona
    Which player this run is pretending to be: first-timer, veteran, speedrunner, completionist,
    monkey, attached, pilot, or random (picks one of the seven, same as monkey/attached already do --
    "random" has never excluded the model-free personas from its pool). act.md carries the JSON contract
    and movement rules that never change; prompts/agent-playtest/prompts/personas/<name>.md supplies
    the KNOWLEDGE and GOAL half (monkey is the one exception -- see below). One persona ("curious,
    slightly impatient") used to drive every run, which measured the same player thirty times over a
    thirty-run sweep. An unrecognized value fails loudly rather than silently becoming the default --
    see personas.ps1's own note.

    sceptic is RETIRED (W3, docs/plans/2026-08-10-002, ruling 6): the dead-verb detector below (see
    -FrameEvery and the "## Dead-verb candidates" findings.md section) runs under every persona and
    catches what sceptic could only ever narrate in prose, without the fabrication risk of a model
    inventing doubt about a turn that worked fine.

    monkey (W4, docs/plans/2026-08-10-002, ruling 9) is model-free: no ollama calls, no GPU gate (both
    skipped entirely -- there is nothing to check VRAM for), no judge pass, no patience meter ("it
    cannot get frustrated" -- it always runs to the full turn budget). Every turn it picks uniformly at
    random, via -Seed, among this turn's enabled controls, legal move directions, and advance -- see
    tools/agent-playtest/monkey.ps1. The mechanical sections (backend, metrics, coverage, dead-verb)
    still run and still populate findings.md; only the model-facing half is gone.

    attached (W4) knows only that heroes exist and can die permanently; its goal is to keep one named
    hero (named by the model itself, turn 1, via the new -Persona-agnostic "note" field) alive. The
    driver watches every later turn's on-screen text for that name next to death vocabulary and, on a
    match, injects one line into the next prompt and applies a major patience hit -- see
    tools/agent-playtest/attached.ps1. The attachment is INJECTED by the harness, not formed by the
    model; the honesty footer on an attached run says so explicitly.

    pilot (S2, scripted-deep-pilot lane) is ALSO model-free, same short-circuit shape as monkey (no
    ollama, no GPU gate, no act/judge prompt) -- but unlike monkey's uniform-random baseline, its
    command logic (tools/agent-playtest/pilot.ps1) is a deliberately imperfect, habit-forming, curious
    policy built to replicate a human across a long (150+ turn) unattended run and surface findings,
    never to maximize turns/day count. It writes two extra findings.md sections no other persona
    does -- "## Friction log" and "## Six decisions this run took" -- both also mirrored into
    metrics.json for the S3 critic pass to consume. See pilot.ps1's own header for the full design.

.PARAMETER Seed
    Seeds -Persona monkey's uniform-random command stream (System.Random). The SAME seed against the
    SAME state sequence produces a byte-identical command sequence -- reproducibility of the command
    STREAM given identical states, never a claim about sim determinism (this file's own dot-sourced
    monkey.ps1 header says so at length). Ignored by every other persona.

.PARAMETER Scenario
    W5 (docs/plans/2026-08-10-002): the slug of a scenario card at
    tools/agent-playtest/scenarios/<slug>.md -- "did this ONE named behaviour work", answered with
    quotes, distinct from -Scope Diff ("what changed recently") and the ordinary open-ended sweep.
    Loaded and validated BEFORE the GPU gate (same fail-cheap-checks-first order as persona
    resolution); a missing or malformed card Die()s loudly rather than falling back to a plain run
    (tools/agent-playtest/scenario.ps1 owns the parsing). A card's Setup (fresh/continue/a scripted
    command prefix) replays blind through the same plumbing -Scripted uses, its Brief is appended to
    the act prompt AFTER persona substitution (a scenario is a task, not a fifth persona), and its
    Expected observation goes to the judge pass ONLY -- never the act prompt (the de-contamination
    requirement; see scenario.ps1's own header). Incompatible with -Scripted and -Persona monkey
    (neither one calls a model at all, and this needs both a real act loop and a real judge pass) --
    passing both together Die()s rather than silently picking one.

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
    [string]$BrainModel = 'qwen3:14b',
    [ValidateSet('Quit', 'Sweep')]
    [string]$PatienceMode = 'Quit',
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
    [string]$Persona = 'first-timer',
    [int]$Seed = 1,
    [string]$Scenario = ''
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
# W4 (docs/plans/2026-08-10-002): temperament.ps1 (the patience meter), monkey.ps1 (the model-free
# persona's own command logic). attached.ps1 must be dot-sourced AFTER metrics.ps1 -- it reuses
# metrics.ps1's own $script:ProductSentenceKeywordPattern rather than a second copy (see its header).
. (Join-Path $PSScriptRoot 'agent-playtest\temperament.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\monkey.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\attached.ps1')
# S2 (scripted-deep-pilot lane): pilot.ps1 owns the model-free, human-shaped policy's own command
# logic -- see its own header. Dot-sourced alongside monkey.ps1 for the same reason (no Godot/ollama/
# VRAM needed to test it standalone).
. (Join-Path $PSScriptRoot 'agent-playtest\pilot.ps1')
# W5 (docs/plans/2026-08-10-002): scenario cards -- "did this ONE named behaviour work", answered
# with quotes. Pure parser + verdict logic; see its own header for the card format.
. (Join-Path $PSScriptRoot 'agent-playtest\scenario.ps1')

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

# W4 (docs/plans/2026-08-10-002): monkey and attached are the two new players. monkey is model-free
# (ruling 9) -- computed once, here, so every later gate (GPU, act-prompt assembly, the patience
# meter) can branch on it without re-deriving $personaName -eq 'monkey' at each site.
$isMonkey = ($personaName -eq 'monkey')
$isAttached = ($personaName -eq 'attached')
# S2: pilot is model-free like monkey (skips the GPU gate, act/judge prompt assembly, and the
# temperament meter identically) -- see every isMonkey-adjacent site below, each amended to also
# check isPilot rather than growing a parallel set of branches.
$isPilot = ($personaName -eq 'pilot')

# U2 (playtest-finishes wave): the eyes/brain residency decision, made once, here -- see this file's
# own .PARAMETER BrainModel doc and model-call.ps1's Get-ModelResidencyPlan for the VRAM math and the
# reasoning. Computed unconditionally (cheap, pure) even for Scripted/monkey, which never reach the
# GPU gate or the act loop's model branch at all -- $residency simply goes unused there, same as
# $temperamentMeter staying $null for those two modes.
$residency = Get-ModelResidencyPlan -Model $Model -BrainModel $BrainModel -JudgeModel $JudgeModel
if ($residency.SplitMode) {
    Say ('eyes/brain split: ACT+JUDGE calls go to brain model ' + $BrainModel +
        ' -- vision model ' + $Model + ' is NOT loaded this run, frame narration is SKIPPED (state digest + screen text + menu only)')
} else {
    Say ('single-model mode: ' + $Model + ' narrates AND chooses (pass -BrainModel to split them)')
}

# W5 (docs/plans/2026-08-10-002): load and validate the scenario card, if any, BEFORE the GPU gate --
# same fail-cheap-checks-first order as persona resolution just above. A missing or malformed card
# Die()s loudly (scenario.ps1's Read-ScenarioCard throws, naming the exact section) rather than
# silently falling back to a plain run.
$scenarioCard = $null
if ($Scenario) {
    $scenarioPath = Join-Path $PSScriptRoot ('agent-playtest\scenarios\' + $Scenario + '.md')
    try {
        $scenarioCard = Read-ScenarioCard -Path $scenarioPath
    } catch {
        Die @(('scenario card failed to load: ' + $_.Exception.Message))
    }
    Say ('scenario: ' + $scenarioCard.Slug + ' (Setup: ' + $scenarioCard.Setup.Type + ')')
    # Neither -Scripted nor monkey ever calls a model -- a scenario needs BOTH a real act loop (to
    # carry out the Brief) and a real judge pass (to answer the Expected observation), so silently
    # picking one over the other would be exactly the confusing-combo shape this repo's own
    # fail-loudly convention exists to prevent.
    if ($Scripted) {
        Die @('-Scenario and -Scripted cannot be combined -- Scripted never calls a model, and a scenario needs both a real act loop and a real judge pass.')
    }
    if ($isMonkey) {
        Die @('-Scenario and -Persona monkey cannot be combined -- monkey never calls a model (ruling 9), and a scenario needs both a real act loop and a real judge pass.')
    }
    if ($isPilot) {
        Die @('-Scenario and -Persona pilot cannot be combined -- pilot never calls a model (S2, same as monkey), and a scenario needs both a real act loop and a real judge pass.')
    }
}

# Ruling 4: monkey defaults -FrameEvery to 25, UNLESS the caller passed their own value explicitly --
# $PSBoundParameters is the only reliable way in PowerShell to tell "the default fired" apart from
# "the caller asked for exactly 1" (both look identical once inside $FrameEvery itself).
if ($isMonkey -and -not $PSBoundParameters.ContainsKey('FrameEvery')) {
    $FrameEvery = 25
    Say 'monkey default: -FrameEvery 25 (ruling 4 -- ~134 KB/frame x 400 turns would be 54 MB nobody reviews)'
}
# S2: pilot runs are long by design (150+ turns for the day-11 floor) -- same disk-budget reasoning
# as monkey's own default above, same escape hatch (an explicit -FrameEvery still wins).
if ($isPilot -and -not $PSBoundParameters.ContainsKey('FrameEvery')) {
    $FrameEvery = 25
    Say 'pilot default: -FrameEvery 25 (same disk-budget reasoning as monkey''s ruling 4)'
}

# W4: the one global temperament clock (ruling 8) -- never for Scripted (no persona in the loop at
# all) or monkey (ruling 9: it cannot get frustrated, it runs to budget regardless). A persona file's
# own front-matter (personas.ps1's Split-PersonaFrontMatter) may scale only the START value.
$temperamentMeter = $null
if (-not $Scripted -and -not $isMonkey -and -not $isPilot) {
    $patienceMultiplier = Get-PersonaPatienceMultiplier -PersonaName $personaName `
        -PersonasDir (Join-Path $PSScriptRoot 'agent-playtest\prompts\personas')
    $temperamentMeter = New-TemperamentMeter -StartMultiplier $patienceMultiplier
    Say ('temperament: ' + $temperamentMeter.Version + ', start patience ' + $temperamentMeter.Max)
}

# W4: monkey's own seeded PRNG, created ONCE and reused for the whole run -- see monkey.ps1's own
# header for why byte-identical same-seed reproduction depends on that (a fresh System.Random per
# turn would not be a seeded STREAM, just N independent single draws).
$monkeyRandom = $null
if ($isMonkey) { $monkeyRandom = New-Object System.Random($Seed) }

# S2: pilot's own seeded PRNG (same reuse-across-the-whole-run contract as monkey's above) plus its
# run-lifetime memory object (habit/curiosity/friction state -- see pilot.ps1's New-PilotMemory).
$pilotRandom = $null
$pilotMemory = $null
if ($isPilot) {
    $pilotRandom = New-Object System.Random($Seed)
    $pilotMemory = New-PilotMemory
}

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
# fix/pilot-finds-its-way: an explicit -OutDir passed as a RELATIVE path used to break the file
# channel silently. This script's own cwd (repo root, wherever it was invoked from) and the spawned
# Godot child's cwd (--path godot, below -- Godot changes its working directory to match) resolve the
# SAME relative string against two DIFFERENT bases. The driver watched
# <caller's cwd>/<OutDir>/state.json forever while the client wrote godot/<OutDir>/state.json --
# "AGENT-PLAYTEST REFUSED: client wrote no state" is exactly what a perfectly healthy client produces
# under that mismatch (measured directly: state.json really was on disk, just under
# godot/.claude/<OutDir>/). The default (-OutDir omitted) never hit this, because $RepoRoot above is
# already absolute -- only an explicit relative value could collide. Resolving once, here, removes
# the ambiguity for both sides: $env:AGENT_PLAYTEST_DIR below carries the same absolute string the
# client receives, so there is only one directory either process could mean.
$OutDir = (Resolve-Path $OutDir).Path

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
# W4 (docs/plans/2026-08-10-002, ruling 2): the model's own scratchpad -- accumulated from the
# schema's optional "note" field, one line per turn that supplies one, wiped at run start (added to
# THIS SAME stale-artifact sweep below) and NEVER seeded across runs or personas. See turn-prompt.ps1
# for how a capped echo of this file's content replaces the old 6-line "Recent turns" history window.
$notesPath = Join-Path $OutDir 'notes.md'

foreach ($stale in @($statePath, $cmdPath, $framePath, $turnlogPath, $findingsPath, $driverLog,
        $playtestLogPath, $backendJsonPath, $coverageMdPath, $coverageJsonPath, $metricsJsonPath,
        $deadVerbStagingPath, $notesPath)) {
    if (Test-Path $stale) { Move-Item $stale ($stale + '.prev') -Force }
}
# frames/ is a directory, not a single file -- Move-Item -Force cannot rename it onto an existing
# ".prev" directory (a PowerShell/.NET limitation on directories, not files), so it is cleared
# outright instead. Every frame it ever held is also sitting in this run's own findings, so nothing
# is lost that the previous run's OWN artifacts do not already carry.
if (Test-Path $framesDir) { Remove-Item $framesDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $framesDir -Force | Out-Null

# --- GPU gate: a precondition, not a hope -------------------------------------------------------
# W4, ruling 9: monkey skips this ENTIRELY, not just the warm-up at the bottom of this block -- there
# is no model in the loop at all, so there is nothing to check VRAM for.
if ($isMonkey) {
    Say 'monkey: skipping the GPU gate and ollama warm-up entirely (ruling 9 -- no model in the loop)'
} elseif ($isPilot) {
    Say 'pilot: skipping the GPU gate and ollama warm-up entirely (S2 -- model-free, same as monkey)'
} elseif (-not $Scripted) {
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

    # U2 (playtest-finishes wave): the gate checks/warms $residency.ActModel -- $Model itself in
    # single-model mode, $BrainModel in split mode. Split mode never checks or warms $Model at all:
    # it is genuinely unused for the whole run (frame narration is skipped, not swapped -- see
    # -BrainModel's own doc), so requiring it be pulled would refuse a run for a model this run will
    # never call.
    if ($resident -contains $residency.ActModel) {
        Say ($residency.ActModel + ' is already loaded, so the free-VRAM floor does not apply to it')
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
    if ($have -notcontains $residency.ActModel) {
        $pullFlag = '-Model'
        if ($residency.SplitMode) { $pullFlag = '-BrainModel' }
        Die @(
            ('model ' + $residency.ActModel + ' is not pulled. Available: ' + ($have -join ', ')),
            ('Pull it (ollama pull ' + $residency.ActModel + ') or pass ' + $pullFlag + ' with one of the above.')
        )
    }
    # Checked here, before ANY turn runs, for the same reason $residency.ActModel is: the judge call
    # happens only after the whole turn budget is spent, and finding out THEN that the judge model was
    # never pulled would waste the entire run instead of one warm-up request. Skipped outright when
    # the judge model IS the act model (split mode's own reuse -- see -BrainModel's doc): it was just
    # verified pulled two lines above, checking it twice would only ever repeat the same answer.
    if (($residency.JudgeModel -ne $residency.ActModel) -and ($have -notcontains $residency.JudgeModel)) {
        Die @(
            ('judge model ' + $residency.JudgeModel + ' is not pulled. Available: ' + ($have -join ', ')),
            ('Pull it (ollama pull ' + $residency.JudgeModel + ') or pass -JudgeModel with one of the above.')
        )
    }

    # Warm it with a real request. A model can be PULLED and still fail to LOAD -- measured
    # 2026-08-04, llama3.2-vision:11b is listed by /api/tags and then dies with
    # "unknown model architecture: 'mllama'". Finding that out on turn 1 of a real run, after
    # launching the game, wastes the run and reads like a game bug.
    Say ('warming ' + $residency.ActModel)
    # Warm through the SAME path a real turn uses, prompt file included. A warm-up that skips the
    # system prompt proves nothing: the 2026-08-04 failure was triggered by the prompt's own length,
    # so a short bespoke warm-up passed while all 22 real turns failed.
    $warm = $null
    try { $warm = Invoke-Model (Get-Content (Join-Path $PSScriptRoot 'agent-playtest\prompts\act.md') -Raw) 'Reply with the single word ok.' $null $residency.ActModel } catch {
        Die @(
            ('model ' + $residency.ActModel + ' is pulled but will not run.'),
            ('ollama said: ' + $_.Exception.Message),
            'If this is an architecture error, that model is unsupported by this ollama build -- pick another.'
        )
    }
    if (-not $warm) { Die @(('model ' + $residency.ActModel + ' returned nothing on a warm-up request through the real prompt path.')) }
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

# The client keeps its OWN turn cap (AgentPlaytest.cs's DefaultMaxTurns = 400) and stops the moment
# it hits one, which is exactly the "two halves of this channel are unrelated numbers in two
# languages" defect the TIMEOUT_MS line above exists to prevent -- fixed there, left open here.
#
# Measured 2026-08-12: three live pilot runs at -Turns 400 / 800 / 900 ALL stopped at turn 400 on
# different navigation paths, and the driver reported "client wrote no state within 90s" every time
# -- a clean, deliberate client shutdown wearing a timeout's error message. Any run budgeted past
# 400 turns has been silently truncated, and blamed on a hang, for as long as both numbers existed.
#
# The margin exists because the two sides count different things: the client counts command
# round-trips it served, the driver counts its own loop iterations, and setup/scenario turns can put
# them a few apart. Erring high is free (the driver still stops at its own $Turns); erring low
# reopens the exact defect.
$env:AGENT_PLAYTEST_MAX_TURNS = [string]($Turns + 25)
Say ('launching client (out: ' + $OutDir + ', playtest log: ' + $playtestLogPath + ')')
# The SCENE must be named explicitly. `--path godot` alone boots the game's main scene, so the
# bridge never runs and the driver waits out its timeout on a client that was never asked to play --
# measured on the first scripted run, which sat for 90s and then reported "scripted run complete".
#
# fix/pilot-finds-its-way: --disable-vsync. Measured 2026-08-11 on this exact machine with an
# unattended/unfocused desktop session: the client printed "[MainUi] campaign started" and then never
# wrote state.json at all, for 240s+ -- alive (memory flat, no crash), not slow. Killing it and
# launching the SAME scene by hand with --disable-vsync produced state.json inside 30s. Vulkan
# Forward+'s present call was blocking on a compositor vsync signal this session's window station
# never delivered (no active foreground/composited desktop), which stalls the WHOLE single-threaded
# main loop -- ProcessFrame signals never fire, so even AgentPlaytestBridge's own Settle() calls never
# return. An automated, unattended tool cannot depend on a human being logged in and watching the
# screen; disabling vsync costs nothing for a client nobody is meant to be looking at frame-perfectly
# anyway (this tool already runs silenced -- DevToolAudio.Silence -- for the same "unattended" reason).
$proc = Start-Process -FilePath $godot -ArgumentList @('--path', (Join-Path $RepoRoot 'godot'), 'res://agentplaytest.tscn', '--disable-vsync') -PassThru

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
# W4: set together, once, at the moment a depleted meter ends the run -- see the loop's own patience
# check for where these are assigned. Only ever set in -PatienceMode Quit (the default) -- Sweep mode
# never breaks the loop on a depleted meter at all, see $wouldHaveQuitMarkers below.
$temperamentQuitTurn = $null
$temperamentQuitDay = $null
$temperamentQuitPhase = $null

# U3 (playtest-finishes wave): -PatienceMode Sweep's own record -- one entry per exhaustion the run
# hit (never just the last), collected instead of ending the run. Stays empty for -PatienceMode Quit
# (the default), and for Scripted/monkey (no meter at all).
$wouldHaveQuitMarkers = New-Object System.Collections.ArrayList

# A1 honesty counters. A run that mostly pressed advance must not read like a run the model
# played -- these three numbers are what let the header and the exit code tell the difference.
$modelDrivenTurns = 0
$fallbackTurns = 0
$imagelessTurns = 0
$imageMissingThisTurn = $false

# INERT bookkeeping -- the third honesty gauge (Get-InertVerdict, completion.ps1, whose header carries
# the full reasoning). $lastDigest/$lastActionWasActing are the PREVIOUS turn's screen and command, so
# the comparison at the top of each iteration answers "did the command I just sent change anything?".
# $inertStreak exists so a dead run DIES instead of burning its whole budget: a 420-turn probe once
# completed zero forge strikes and reported findings as though it had played.
$actingTurns = 0
$inertTurns = 0
$inertStreak = 0
$inertStreakWorst = 0
$lastDigest = $null
$lastActionWasActing = $false
$lastActionLabel = ''
$inertAbortStreak = 15
$inertAbortLabel = ''

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

# W4: the scratchpad -- one line per turn that supplied a "note", in chronological order. Joined and
# capped (turn-prompt.ps1's Get-EchoedNotesText) into the NEXT turn's own prompt; the raw, uncapped
# text is also appended to notes.md on disk as it arrives.
$notesLines = New-Object System.Collections.ArrayList

# W4: attached persona hero-tracking state (attached.ps1's own functions do the actual text matching;
# these four variables are the driver's memory of where in that story this run currently is).
$attachedHeroName = $null
$attachedHeroDied = $false
$attachedDeathTurn = $null
$attachedDeathAttributed = $false

# W5: the scenario card's Setup replay position -- how many of its scripted commands (if any) have
# already been consumed. Stays 0 for a Fresh/Continue Setup (Commands is empty, so the turn loop's
# own scenario branch never matches) and for every ordinary (non-scenario) run ($scenarioCard is
# $null). See the turn loop's own comment on why replay ALSO stops early the instant state.beat
# reads VigilStop, rather than trusting this count alone.
$scenarioSetupIndex = 0

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
if ($isMonkey) {
    Say 'monkey: skipping act-prompt/schema/judge-prompt assembly entirely (ruling 9 -- no model call ever reads them)'
} elseif ($isPilot) {
    Say 'pilot: skipping act-prompt/schema/judge-prompt assembly entirely (S2 -- model-free, same as monkey)'
} elseif (-not $Scripted) {
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

    # W5 (docs/plans/2026-08-10-002): the scenario's Brief -- appended AFTER persona substitution (a
    # scenario is a task layered on the chosen player, never a fifth persona). The Expected
    # observation NEVER reaches this call, or anything $actPrompt feeds from here on -- it goes to
    # the judge pass only (see the judge-input assembly below). This is the de-contamination
    # requirement; tools/test-agent-playtest-modes.ps1 proves it by building this exact prompt and
    # asserting the expected-observation text is absent from the result.
    if ($scenarioCard) {
        $actPrompt = $actPrompt + [Environment]::NewLine + [Environment]::NewLine +
            (Get-ScenarioActPromptAddition -Brief $scenarioCard.Brief)
    }

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
                # W4: a dead-verb candidate drains the meter -- OPTIONAL input (this whole block only
                # ever runs when $pendingDeadVerb was set, itself gated on -not Scripted, so a
                # Scripted run or a build with W3 not yet landed simply never reaches here at all;
                # nothing downstream needs to know the difference).
                if ($temperamentMeter) {
                    Add-TemperamentDrain -Meter $temperamentMeter -Cause 'deadverb' `
                        -Amount $script:PatienceDrainDeadVerbCandidate -Turn $pendingDeadVerb.Turn `
                        -Day $state.day -Phase $pendingDeadVerb.Phase -Detail $pendingDeadVerb.ControlName
                }
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
        # U1 (eyes-learn-labels wave): every "Enabled controls: ..." line the model actually reads
        # (this STUCK note, and the REFUSED feedback below) shows the label painted on screen
        # alongside the name a press must use -- $enabled itself stays names-only, since that is what
        # Get-LegalCommandFromReply's legality check keys on.
        $enabledDescriptors = Get-EnabledControlDescriptors -Controls $state.controls
        $whereabouts = ''
        if ($state.nearby -and @($state.nearby).Count -gt 0) {
            $nearest = @($state.nearby)[0]
            $whereabouts = $nearest.key + '@' + [math]::Round($nearest.distance / 16)
        }
        $digest = ($state.phase + '|' + $state.location + '|' + $whereabouts + '|' + (($state.screenText) -join ';') + '|' + ($enabled -join ','))
        if ($digestSeen.ContainsKey($digest)) { $digestSeen[$digest] = $digestSeen[$digest] + 1 } else { $digestSeen[$digest] = 1 }
        if ($digestSeen[$digest] -eq 4) {
            $note = 'STUCK: the screen was identical for 4 turns at ' + $state.location + ' / ' + $state.phase + '. Enabled controls: ' + ($enabledDescriptors -join ', ')
            Warn $note
            [void]$stuckFindings.Add($note)
            # W4: a stuck-digest repeat drains the meter -- worse than a single refusal (see
            # temperament.ps1's own constant-set comments for the reasoning).
            if ($temperamentMeter) {
                Add-TemperamentDrain -Meter $temperamentMeter -Cause 'stuck' `
                    -Amount $script:PatienceDrainStuckRepeat -Turn $turn -Day $state.day -Phase $state.phase `
                    -Detail ($state.location + '/' + $state.phase)
            }
        }

        # INERT accounting. $digest above is THIS turn's screen, which is the OUTCOME of the command
        # sent at the end of the previous iteration -- so comparing it to $lastDigest answers whether
        # that command did anything at all. Only ACTING commands are judged: 'advance' is allowed to
        # leave the screen alone (it moves the clock, and the phase it lands in may look identical).
        #
        # This is the gauge the STUCK note above is not. STUCK trips on `-eq 4` and so fires once per
        # distinct digest, meaning a frozen run warns a single time and then runs to the end of its
        # budget reporting findings. Here the streak ENDS the run, and the ratio FAILS it.
        if ($null -ne $lastDigest -and $lastActionWasActing) {
            $actingTurns++
            if ($digest -eq $lastDigest) {
                $inertTurns++
                $inertStreak++
                if ($inertStreak -gt $inertStreakWorst) { $inertStreakWorst = $inertStreak }
            } else {
                $inertStreak = 0
            }
        }
        if ($inertStreak -ge $inertAbortStreak) {
            $inertAbortLabel = $lastActionLabel
            $stopReason = ('INERT: ' + $inertStreak + ' consecutive acting commands changed nothing on screen (last: "' +
                $lastActionLabel + '") at ' + $state.location + ' / ' + $state.phase +
                ' -- the driver is pressing into a game that is not receiving it. Enabled controls were: ' +
                ($enabledDescriptors -join ', '))
            Warn $stopReason
            Warn 'Stopping the run. A harness that keeps playing here manufactures clean-looking data over a dead game.'
            break
        }
        $lastDigest = $digest

        # W2: this turn's own pre-refusal reasons, reset every iteration (both modes) -- Scripted never
        # populates it (it has no legality-check attempts loop at all), so it is always an empty array
        # there, which is the correct answer for a mode with no pre-send refusal concept.
        $turnPreRefusalReasons = New-Object System.Collections.ArrayList

        # Decide the command.
        $command = $null
        # W5: true only for a turn that consumed one of the scenario card's own Setup commands --
        # excluded from the dead-verb detector below the same way -Scripted's own deliberate illegal
        # press is (these are driver-constructed synthetic presses proving a path exists, never an
        # organic player decision the detector should judge).
        $isScenarioSetupTurn = $false
        if ($Scripted) {
            $idx = [math]::Min($turn - 1, $scriptedPlan.Count - 1)
            $command = $scriptedPlan[$idx]
        } elseif ($isMonkey) {
            # W4, ruling 9: uniform-random, legal by construction (monkey.ps1's own candidates are
            # built from THIS turn's enabled controls / canMove, never from a fixed vocabulary), so
            # there is no legality re-check, no attempts loop, and no refusal path here at all.
            $command = Get-MonkeyCommand -State $state -Random $monkeyRandom
        } elseif ($isPilot) {
            # S2: the human-shaped scripted policy -- legal by construction the same way monkey's
            # is (every candidate pilot.ps1 builds comes from THIS turn's own enabled controls/
            # nearby/canMove), so no legality re-check or refusal-retry loop here either.
            $command = Get-PilotCommand -State $state -Memory $pilotMemory -Random $pilotRandom
        } elseif ($scenarioCard -and ($scenarioSetupIndex -lt @($scenarioCard.Setup.Commands).Count) -and
                  ([string]$state.beat -ne 'VigilStop')) {
            # W5, ruling 3: Setup may be blind; play may not. Replayed through the SAME plumbing
            # -Scripted uses (a raw command string, no legality re-check) -- but replay stops the
            # INSTANT state.beat reads VigilStop, even with commands still unconsumed: this list's own
            # "advance" is the harness's raw bridge command (AgentPlaytest.ApplyAdvance ->
            # SimAdapter.AdvancePhase directly), which has no VigilStop gate the way the client's own
            # AdvancePhase BUTTON does -- so trusting the count alone risks ticking straight through
            # the very state this card exists to test. See vigil-runner.md's own Setup section for the
            # full reasoning this guards.
            $command = @($scenarioCard.Setup.Commands)[$scenarioSetupIndex]
            $scenarioSetupIndex++
            $isScenarioSetupTurn = $true
        } else {
            $notesFullText = ($notesLines -join [Environment]::NewLine)
            # U1 (playtest-finishes wave): THIS turn's numbered menu, built mechanically from the
            # observation just received -- see model-call.ps1's own Build-ActMenu header for exactly
            # what goes in it and why the ordering is deterministic.
            $menuItems = Build-ActMenu -State $state
            # Surroundings, the interact prompt, and the beat all come straight off state.json (see
            # Build-ActUserText in agent-playtest\turn-prompt.ps1), so this narrates the world, never
            # invents it. Extracted to its own file so the beat-wiring fix below is provable with a
            # stubbed state object instead of a live Godot+ollama run -- see
            # tools/test-agent-playtest-modes.ps1. -NoImage mirrors $residency.ActUsesImage: split
            # mode never attaches a frame (see -BrainModel's own doc), so the prompt says so plainly
            # rather than silently promising a screenshot act.md still describes.
            $userText = Build-ActUserText -State $state -Turn $turn -Turns $Turns -NotesText $notesFullText `
                -MenuItems $menuItems -NoImage:(-not $residency.ActUsesImage)

            # W4: the attached persona's own death check -- runs BEFORE the model is asked anything
            # this turn, so an injected "<name> is dead." line (and the major patience hit) rides
            # along on THIS turn's own prompt, not a turn later. Only fires once per run (the
            # -not $attachedHeroDied guard); every later turn's screenText still gets checked for the
            # attribution-shaped text the quit finding needs, captured at the moment of the match.
            if ($isAttached -and $attachedHeroName -and -not $attachedHeroDied) {
                if (Test-ScreenTextForHeroDeath -HeroName $attachedHeroName -ScreenTextLines @($state.screenText)) {
                    $attachedHeroDied = $true
                    $attachedDeathTurn = $turn
                    $attachedDeathAttributed = Test-ScreenTextForAttribution -ScreenTextLines @($state.screenText)
                    $userText = $userText + [Environment]::NewLine + ($attachedHeroName + ' is dead.')
                    Warn ('attached: ' + $attachedHeroName + ' is dead (turn ' + $turn +
                        '), attribution-shaped text on the same screen: ' + $attachedDeathAttributed)
                    if ($temperamentMeter) {
                        Add-TemperamentDrain -Meter $temperamentMeter -Cause 'attached-death' `
                            -Amount $script:PatienceDrainAttachedDeath -Turn $turn -Day $state.day `
                            -Phase $state.phase -Detail $attachedHeroName
                    }
                }
            }

            $attempts = 0
            $imageMissingThisTurn = $false
            $actImagePath = $null
            if ($residency.ActUsesImage) { $actImagePath = $framePath }
            while ($attempts -lt $ModelCallMaxAttempts -and -not $command) {
                $attempts++
                $reply = ''
                try { $reply = Invoke-Model $actPrompt $userText $actImagePath $residency.ActModel $actionSchemaJson } catch { Warn ('model call failed: ' + $_.Exception.Message) }

                # U1 (playtest-finishes wave): menu-choice acting replaces the old freeform
                # action/target/dir composition -- with format=action-schema.json now constraining
                # decoding to {"choice": <int>, "why": "...", "note": "..."}, a reply can only ever be
                # a well-formed integer pick or a defect (empty reply, missing/non-integer choice).
                # Get-LegalCommandFromMenuChoice resolves it against THIS turn's own $menuItems and
                # rebuilds EXACTLY the command the old free-form path would have sent for that verb
                # (model-call.ps1's Get-CommandTextFromMenuItem, reusing Get-ResolvedPressCommandText
                # verbatim for press) -- ruling 1's "an illegal outcome is signal" still holds: an
                # out-of-range choice refuses here, and the kernel can still separately reject the
                # resulting command once it reaches the client (that rejection still lands in the
                # backend log exactly as before). Get-LegalCommandFromReply (the old free-form checker)
                # is RETAINED in model-call.ps1, not deleted, purely because Get-ResolvedPressCommandText
                # is reused by the new path -- see that file's own header.
                $legal = Get-LegalCommandFromMenuChoice -Reply $reply -MenuItems $menuItems
                if ($legal.Refused) {
                    $userText = $userText + [Environment]::NewLine + ('REFUSED: ' + $legal.Reason + '. Reply with the "choice" number from the menu shown above.')
                    Warn ('turn ' + $turn + ' attempt ' + $attempts + ' refused: ' + $legal.Reason)
                    # W2: raw material for Get-RefusalFrustrationMap -- recorded here, at the ONE place
                    # that already knows both the reason text and which turn/phase it happened in,
                    # rather than re-deriving it later from Warn's own console/driver.log text.
                    $refusalControl = Get-RefusalControlFromReason -Reason $legal.Reason
                    [void]$preRefusals.Add([pscustomobject]@{
                        Turn    = $turn
                        Day     = $state.day
                        Phase   = $state.phase
                        Control = $refusalControl
                        Reason  = $legal.Reason
                    })
                    [void]$turnPreRefusalReasons.Add($legal.Reason)
                    # W4: a refused/pre-refused action drains the meter -- the baseline drain unit
                    # every other cause in temperament.ps1 is sized relative to.
                    if ($temperamentMeter) {
                        Add-TemperamentDrain -Meter $temperamentMeter -Cause 'refusal' `
                            -Amount $script:PatienceDrainRefusal -Turn $turn -Day $state.day `
                            -Phase $state.phase -Detail $refusalControl
                    }
                    continue
                }
                $command = $legal.Command
            }
            if (-not $command) {
                # U2 (eyes-learn-labels wave): an unconditional advance-fallback burns a whole day
                # while an overlay still owns the screen -- see metrics.ps1's Get-FallbackCloseControl
                # for the mechanical (never hardcoded) derivation of which enabled control closes it.
                $fallbackCloseControl = Get-FallbackCloseControl -EnabledControls $enabled
                if ($fallbackCloseControl) {
                    $command = '{"action":"press","target":"' + $fallbackCloseControl + '","why":"driver fallback: model gave no usable command, an overlay owns the screen"}'
                    Warn ('falling back to press ' + $fallbackCloseControl + ' (an overlay owns the screen)')
                } else {
                    $command = '{"action":"advance","why":"driver fallback: model gave no usable command"}'
                    Warn 'falling back to advance'
                }
                $fallbackTurns++
            } else {
                $modelDrivenTurns++
            }
            if ($imageMissingThisTurn) { $imagelessTurns++ }
        }

        $parsedCmd = $command | ConvertFrom-Json
        Say ('turn ' + $turn + ': ' + $parsedCmd.action + ' ' + $parsedCmd.target + ' -- ' + $parsedCmd.why)
        [void]$history.Add('turn ' + $turn + ' @ ' + $state.location + '/' + $state.phase + ' -> ' + $parsedCmd.action + ' ' + $parsedCmd.target + ' (' + $parsedCmd.why + ') ; outcome: ' + $state.lastOutcome)

        # W4: the scratchpad -- a no-op whenever $parsedCmd has no "note" (Scripted's fixed plan and
        # monkey's own command builder never set one, so this is naturally a no-op for both modes).
        if ($parsedCmd.note) {
            $noteLine = 'turn ' + $turn + ': ' + ([string]$parsedCmd.note).Trim()
            [void]$notesLines.Add($noteLine)
            try { Add-Content -Path $notesPath -Value $noteLine -Encoding utf8 } catch { }

            # W4: attached names its one hero via this SAME field, the first time it appears --
            # checked every turn (not just turn 1) so a first-timer-shaped delay in the game actually
            # naming a hero on screen does not permanently miss the window.
            if ($isAttached -and -not $attachedHeroName) {
                $candidateHeroName = Get-AttachedHeroNameFromNote -Note $parsedCmd.note
                if ($candidateHeroName) {
                    $attachedHeroName = $candidateHeroName
                    Say ('attached: hero named -- ' + $attachedHeroName)
                }
            }
        }

        # U2 (eyes-learn-labels wave): a scenario card's own Setup replay -- the "why" text is a QA
        # comment for a HUMAN reading the card ("safety margin 2", "staged -- expect VigilStop here"),
        # not player-facing copy, but it rode the SAME Why field Format-DigestTurnLine puts straight
        # into the judge's per-turn line, unmarked -- a judge graded it as if it were UI text or a
        # real decision. Prefixed here, at the one place that already knows a turn came from Setup
        # replay, rather than downstream where that context is gone; a model-authored why (the normal
        # case) is untouched.
        $turnWhyText = $parsedCmd.why
        if ($isScenarioSetupTurn) { $turnWhyText = '[setup] ' + $turnWhyText }

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
            Why             = $turnWhyText
            Outcome         = $state.lastOutcome
            ScreenText      = @($state.screenText)
            EnabledControls = $enabled
            Refused         = (@($turnPreRefusalReasons).Count -gt 0)
            RefusalReason   = (@($turnPreRefusalReasons) | Select-Object -Last 1)
        })

        # U3: record what this turn's state showed and what got pressed, against the real registries
        # read from source at the top of this script (coverage.ps1's own Add-CoverageTouch). W4 hooks
        # this SAME call site (never a parallel tracker) to reset the temperament meter on the first
        # turn that touches a surface this run had not touched before -- a full reset, not a partial
        # refund (ruling 8), the meter's own "second wind" for genuine novelty.
        $coverageTouchedBefore = 0
        if ($temperamentMeter) { $coverageTouchedBefore = Get-CoverageTrackerTouchedCount -Tracker $coverageTracker }
        Add-CoverageTouch -Tracker $coverageTracker -State $state -Command $parsedCmd
        if ($temperamentMeter) {
            $coverageTouchedAfter = Get-CoverageTrackerTouchedCount -Tracker $coverageTracker
            if ($coverageTouchedAfter -gt $coverageTouchedBefore) {
                Reset-TemperamentMeter -Meter $temperamentMeter -Turn $turn -Day $state.day -Phase $state.phase `
                    -Surface ('coverage +' + ($coverageTouchedAfter - $coverageTouchedBefore))
            }
        }

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
        # W5: a scenario's own Setup replay is excluded the same way -- those presses are also
        # driver-constructed to reach a target state, never an organic player decision.
        # If -FrameEvery already thinned this turn's frame, it is staged now (deadverb.ps1) since
        # frame.png will be the NEXT turn's screenshot by the time the verdict is known.
        if ((-not $Scripted) -and (-not $isScenarioSetupTurn) -and ($parsedCmd.action -eq 'press')) {
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

        # W4: an empty meter ends the run (ruling 8) -- checked once per turn, after every drain/reset
        # site above has had its chance to run, so the very drain that emptied it is already reflected
        # here. Captured turn/day/phase feed Get-TemperamentQuitFinding once the loop has exited.
        #
        # U3 (playtest-finishes wave): -PatienceMode Sweep logs a would-have-quit MARKER instead of
        # ending the run -- the default, Quit, keeps today's exact break-the-loop behaviour UNCHANGED
        # below. A marker carries the same Turn/Day/Phase a real quit uses, plus the same drain-history
        # headline text
        # (Get-WouldHaveQuitMarker reuses Get-TemperamentQuitFinding's own walk, temperament.ps1) --
        # then RESETS the meter (a full second wind, ruling 8's own shape, not a partial refund) so a
        # LATER exhaustion in the same long run produces its own independent marker rather than never
        # firing again. The run continues to the turn budget either way.
        if ($temperamentMeter -and $temperamentMeter.Depleted) {
            if ($PatienceMode -eq 'Sweep') {
                $marker = Get-WouldHaveQuitMarker -Meter $temperamentMeter -Turn $turn -Day $state.day -Phase $state.phase
                [void]$wouldHaveQuitMarkers.Add($marker)
                Warn ('WOULD-HAVE-QUIT (Sweep mode, continuing to the turn budget): ' + $marker.Trigger)
                Reset-TemperamentMeter -Meter $temperamentMeter -Turn $turn -Day $state.day -Phase $state.phase `
                    -Surface 'patience reset after a would-have-quit marker (Sweep mode)'
            } else {
                $temperamentQuitTurn = $turn
                $temperamentQuitDay = $state.day
                $temperamentQuitPhase = $state.phase
                $stopReason = 'patience exhausted (see the Patience section below)'
                break
            }
        }

        if ($parsedCmd.action -eq 'stop') { $stopReason = 'model asked to stop: ' + $parsedCmd.why; break }

        # INERT: remember what this command WAS, so the next iteration's digest comparison knows
        # whether an unchanged screen is a defect ('press'/'key'/'move'/'click'/'scroll' should all do
        # something) or expected ('advance' only moves the clock).
        $lastActionWasActing = ([string]$parsedCmd.action -ne 'advance')
        $lastActionLabel = [string]$parsedCmd.action
        if ($parsedCmd.target) { $lastActionLabel = $lastActionLabel + ' ' + [string]$parsedCmd.target }
        elseif ($parsedCmd.dir) { $lastActionLabel = $lastActionLabel + ' ' + [string]$parsedCmd.dir }

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

# W4: the quit finding (ruling 8) -- computed once, right here, from the exact turn/day/phase captured
# at the moment the loop noticed the meter was empty. $temperamentMeter is $null for Scripted/monkey,
# so this whole block is a no-op for both (Depleted is never even a valid question to ask of $null).
$temperamentQuitFinding = $null
if ($temperamentMeter -and $temperamentMeter.Depleted) {
    $temperamentQuitFinding = Get-TemperamentQuitFinding -Meter $temperamentMeter -Turn $temperamentQuitTurn `
        -Day $temperamentQuitDay -Phase $temperamentQuitPhase
    Say ('PATIENCE EXHAUSTED: ' + $temperamentQuitFinding.Headline)
} elseif ($temperamentMeter) {
    Say ('temperament: ' + (Get-TemperamentBudgetEndNote -Meter $temperamentMeter))
}

# U1: stamp turnlog.md with a frame reference (or an explicit "frame missing" line) per turn, NOW --
# once, after the client has fully exited. turnlog.md is rewritten WHOLESALE on every flush by the
# client's own AgentPlaytestBridge.RunLoop (File.WriteAllText from its in-memory StringBuilder, which
# has no idea this script exists), so annotating it any earlier would be erased on the very next
# flush. See Add-FrameReferencesToTurnLog's own doc (frames.ps1).
if (Test-Path $turnlogPath) {
    $rawTurnLogForFrames = Get-Content $turnlogPath -Raw
    $annotatedTurnLog = Add-FrameReferencesToTurnLog -TurnLogText $rawTurnLogForFrames -FrameNoteByTurn $frameNoteByTurn
    # fix/the-pilot-goes-around (real-run finding): this Set-Content raced a lock on turnlog.md in
    # two of a handful of live pilot runs -- "The process cannot access the file ... because it is
    # being used by another process", thrown immediately after Stop-Process -Force + WaitForExit
    # (10000) above. WaitForExit only guarantees the CLIENT's own handle is gone, not that nothing
    # else on the machine (a real-time antivirus scan touching every freshly-written file is the
    # prime suspect) has grabbed a brief lock on it in between. Before this, that race threw the
    # WHOLE script out with $ErrorActionPreference='Stop' -- discarding a fully completed 220-turn
    # run's entire report (findings.md/metrics.json never got generated) over a few hundred
    # milliseconds of file contention on a cosmetic annotation pass. Retry briefly instead; if every
    # attempt still fails, fall back to leaving the client's own unannotated turnlog.md in place
    # (still a complete, readable turn-by-turn record, just without the frame cross-references) so a
    # transient OS-level lock never again costs an entire successful run's output.
    $turnlogAnnotated = $false
    for ($turnlogAttempt = 1; $turnlogAttempt -le 5 -and -not $turnlogAnnotated; $turnlogAttempt++) {
        try {
            Set-Content -Path $turnlogPath -Value $annotatedTurnLog -Encoding utf8 -ErrorAction Stop
            $turnlogAnnotated = $true
        } catch {
            if ($turnlogAttempt -ge 5) {
                Say ('WARNING: could not write frame-annotated turnlog.md after 5 attempts (' + $_.Exception.Message + ') -- leaving the client''s own unannotated turnlog.md in place.')
            } else {
                Start-Sleep -Milliseconds (200 * $turnlogAttempt)
            }
        }
    }
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

# W5 (docs/plans/2026-08-10-002): the scenario's backend predicate -- a mechanical fact, checked here
# (no model involved) against the SAME parsed rows the backend section above already read. $null
# whenever there is no scenario, or the card carries no predicate, or the log itself was unavailable
# -- Format-ScenarioVerdictSection reads each of those as its own distinct, honestly-worded case
# rather than a silent "absent".
$scenarioBackendResult = $null
if ($scenarioCard -and $scenarioCard.BackendPredicate -and $backendSummary.Available) {
    $scenarioBackendRows = @((Read-BackendLogRows -LogPath $playtestLogPath).Rows)
    $scenarioBackendResult = Test-ScenarioBackendPredicate -Predicate $scenarioCard.BackendPredicate -Rows $scenarioBackendRows
    Say ('scenario backend predicate: ' + $scenarioBackendResult.Detail)
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
$metricsSummary = Get-MetricsSummary -TurnRecords @($turnRecords) -PreRefusals @($preRefusals) -BackendSummary $backendSummary `
    -PatienceMode $PatienceMode -WouldHaveQuitMarkers @($wouldHaveQuitMarkers)
# S2: pilot's own machine-readable friction log + six-decisions ledger, folded into the SAME
# metrics.json every persona already writes -- so the S3 critic pass and the existing report
# pipeline consume it unchanged, per the brief's own artifact-shape requirement. $null/empty for
# every other persona (they never build a $pilotMemory at all).
if ($isPilot) {
    # Owner steer (2026-08-11), kind 5 INVISIBLE STATE CHANGE: "something changed in the backend log
    # (playtest-log.jsonl eventTypes) with no corresponding change in visible screen text." pilot.ps1's
    # own per-turn decision loop never sees the backend log at all (only $State, the same digest a
    # human reading the screen would have) -- this can only be checked here, post-run, cross-
    # referencing $backendSummary.Timeline (each tick's day/eventTypes) against turnlog.md's own
    # screen dumps (read fresh, not via the LATER $fullLog -- that is not built yet at this point in
    # the script, and this must run BEFORE metrics.json is written a few lines down so the finding
    # actually lands in the SAME artifact the rest of the friction log does).
    #
    # Day-granularity, not per-event -- a real narrated event on a day makes that day's ticker grow at
    # least one "Day N:" bullet, so checking for the BULLET rather than matching each eventType name to
    # its own prose (ItemSold -> "sold to", CommissionPosted -> "wants", ...) avoids a fragile,
    # possibly-wrong mapping table while still catching the case the owner named: a whole day's worth
    # of recorded events with zero player-visible trace anywhere in the run.
    $turnLogForCrossref = ''
    if (Test-Path $turnlogPath) { $turnLogForCrossref = Get-Content $turnlogPath -Raw }
    if ($backendSummary.Available -and $turnLogForCrossref) {
        $daysWithEvents = @($backendSummary.Timeline | Where-Object { @($_.EventTypes).Count -gt 0 } |
            ForEach-Object { [int]$_.Day } | Sort-Object -Unique)
        foreach ($eventDay in $daysWithEvents) {
            $dayBullet = 'Day ' + $eventDay + ':'
            if (-not $turnLogForCrossref.Contains($dayBullet)) {
                $typesThatDay = @($backendSummary.Timeline | Where-Object { [int]$_.Day -eq $eventDay } |
                    ForEach-Object { $_.EventTypes } | Select-Object -Unique)
                Add-PilotFriction -Memory $pilotMemory -Turn -1 -Day $eventDay -Phase '(whole day, cross-referenced post-run)' `
                    -Category 'invisible-state-change' -Trying 'read this day''s own events off the screen' `
                    -Detail ('backend log recorded ' + ($typesThatDay -join ', ') + ' on day ' + $eventDay +
                        ' but no "' + $dayBullet + '" line appears anywhere in this run''s turn log -- ' +
                        'candidate for an event the player was never told about (day-granularity check, ' +
                        'not proof a SPECIFIC event went unnarrated if others that day did)')
            }
        }
    }
    $metricsSummary | Add-Member -NotePropertyName FrictionLog -NotePropertyValue @($pilotMemory.FrictionLog)
    $metricsSummary | Add-Member -NotePropertyName SixDecisions -NotePropertyValue @($pilotMemory.SixDecisions)
}
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

# INERT: did the game receive anything? See Get-InertVerdict (completion.ps1) for why this exists as a
# third gauge alongside DEGRADED and INCOMPLETE, and which real overnight campaign it would have caught.
$inertFloor = 0.5
$inertVerdict = Get-InertVerdict -InertTurns $inertTurns -ActingTurns $actingTurns -Scripted:$Scripted -Floor $inertFloor
$inertPct = $inertVerdict.PercentText
$inertRun = $inertVerdict.Inert
if ($inertRun) {
    Warn ('INERT: ' + $inertTurns + ' of ' + $actingTurns + ' acting commands (' + $inertPct +
        '%) changed nothing on screen, at or over the ' + ($inertFloor * 100) + '% floor. Longest dead streak: ' +
        $inertStreakWorst + ' turns. This run did not test the game -- treat every finding below as unproven.')
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
if ($inertRun) { $titleTags += 'INERT' }
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
    # U2 (playtest-finishes wave): both model names, per the plan's own requirement. Split mode's
    # judge line says so explicitly (reusing the brain model, never a stale $JudgeModel name it did
    # not actually call) -- see $residency's own header note (model-call.ps1's Get-ModelResidencyPlan).
    ('- judge model: ' + $(if ($residency.SplitMode) { $residency.JudgeModel + ' (reusing the brain model, already resident)' } else { $JudgeModel })),
    ('- brain model: ' + $(if ($residency.SplitMode) { $BrainModel } else { '(none -- single-model mode, ' + $Model + ' narrates AND chooses)' })),
    $personaHeaderLine,
    ('- turns: ' + $turn + ' (stopped: ' + $stopReason + ')'),
    ('- completion: ' + $turn + ' of ' + $Turns + ' budgeted turns (' + $completionPct + '%)'),
    # The number to read FIRST in any of these reports: if most acting commands changed nothing, the
    # rest of the header is describing a run that never reached the game.
    ('- effective: ' + ($actingTurns - $inertTurns) + ' of ' + $actingTurns + ' acting commands changed the screen (' +
        $inertTurns + ' inert, ' + $inertPct + '%; longest dead streak ' + $inertStreakWorst + ')'),
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
# W4: temperament header lines -- absent (Scripted/monkey never construct a meter) is reported
# explicitly rather than just omitted, so a reader never has to guess whether it was forgotten.
if ($temperamentMeter) {
    $header += ('- temperament version: ' + $temperamentMeter.Version)
    # U3 (playtest-finishes wave): the patience mode is always named, Quit or Sweep -- a reader must
    # never have to guess which ending rule this run used just because -PatienceMode wasn't the
    # default.
    $header += ('- patience mode: ' + $PatienceMode)
    if ($temperamentMeter.Depleted -and $temperamentQuitFinding) {
        $header += ('- patience: exhausted -- ' + $temperamentQuitFinding.Headline)
    } else {
        $header += ('- patience: ' + (Get-TemperamentBudgetEndNote -Meter $temperamentMeter))
    }
    if ($wouldHaveQuitMarkers.Count -gt 0) {
        $header += ('- would-have-quit markers (Sweep mode): ' + $wouldHaveQuitMarkers.Count)
    }
} else {
    $header += '- temperament: not used (Scripted or monkey run -- no persona frustration to measure)'
}
$header += ''
# W4: attached's own header line -- present only when a hero was actually named this run.
if ($isAttached -and $attachedHeroName) {
    $attachedStatus = 'still alive at the end of this run'
    if ($attachedHeroDied) {
        $attachedStatus = 'died turn ' + $attachedDeathTurn + '; death screen carried attribution-shaped text: ' + $attachedDeathAttributed
    }
    $header += ('- attached: named hero "' + $attachedHeroName + '" -- ' + $attachedStatus)
    $header += ''
}
# W5: the scenario's own header line -- present only when -Scenario was given. The verdict itself
# (model observation + backend predicate) is the "## Scenario verdict" section below, not this line.
if ($scenarioCard) {
    $header += ('- scenario: ' + $scenarioCard.Slug + ' -- see the Scenario verdict section below')
    $header += ''
}
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
# W4 (ruling 8): "the quit reason is written as the run's LEAD finding" -- prepended LAST, so a
# patience-exhausted run reads this line before DEGRADED/INCOMPLETE (which may well co-occur, since
# quitting early naturally reads as incomplete too) rather than after them.
if ($temperamentMeter -and $temperamentMeter.Depleted -and $temperamentQuitFinding) {
    $header = @(
        ('PATIENCE EXHAUSTED: ' + $temperamentQuitFinding.Headline + '.'),
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

# W4: the "## Patience" section -- full drain history behind whichever headline the header line
# above already carries. Empty for Scripted/monkey (no meter), so appending it to every Set-Content
# site below is always safe (an empty array contributes nothing to the joined text).
$temperamentSection = @()
if ($temperamentMeter) {
    $temperamentSection = @('', '---', '') + ((Format-TemperamentMarkdown -Meter $temperamentMeter -QuitFinding $temperamentQuitFinding -WouldHaveQuitMarkers @($wouldHaveQuitMarkers)) -split [Environment]::NewLine)
}

# W5: the "## Scenario verdict" section -- declared here (empty) so every Set-Content site below can
# safely include it even before -Scenario's own judge-dependent content exists yet (Scripted/monkey
# exit long before the judge pass, and -Scenario is incompatible with both -- see the scenario-card
# load guard above). Populated for real, once the judge has spoken, just before the judge-input
# assembly below.
$scenarioSection = @()

# W1: the honesty footer (agent-playtest\footer.ps1) -- computed once, appended to every Set-Content
# site below alongside $backendSection, so a run that dies at any stage still ships the same "here is
# what this instrument cannot see" note as a clean one. W4: attached runs get one extra disclosure --
# the attachment to the named hero was INJECTED by the harness, never formed by the model on its own.
$footerExtraLines = @()
if ($isAttached -and $attachedHeroName) {
    $footerExtraLines = @(
        ('- **Attached persona note** -- this run''s attachment to "' + $attachedHeroName + '" was ' +
         'INJECTED by the harness (attached.md''s own protocol told the model to name a hero and care ' +
         'about them), never formed by the model choosing to care on its own. What this run measures ' +
         'is whether the game SURFACES the payoff to an already-attached player, never whether ' +
         'attachment "formed."')
    )
}
# U3 (playtest-finishes wave): the footer names the patience mode explicitly whenever a meter was
# used at all -- Quit or Sweep, never left to be inferred from whether the run happened to stop early.
if ($temperamentMeter) {
    $patienceFooterLine = '- **Patience mode: Quit** -- an exhausted meter ENDS the run (today''s default behaviour).'
    if ($PatienceMode -eq 'Sweep') {
        $patienceFooterLine = '- **Patience mode: Sweep** -- an exhausted meter logs a would-have-quit marker ' +
            '(' + $wouldHaveQuitMarkers.Count + ' this run) and CONTINUES to the turn budget instead of ending ' +
            'the run; see the Patience section above for each marker''s trigger.'
    }
    $footerExtraLines = @($footerExtraLines) + @($patienceFooterLine)
}
$honestyFooterLines = Get-HonestyFooterLines -ExtraLines $footerExtraLines

# A run that played NOTHING is a failure, whatever mode it was in. The first scripted run sat for 90s
# on a client that had never been asked to play, then printed "scripted run complete" and exited 0 --
# the same shape of lie as a truncated test suite reporting "Passed!". Never again from this script.
if ($turn -eq 0) {
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + @('NOTHING WAS PLAYED. ' + $stopReason) + $backendSection + $metricsSection + $deadVerbSection + $temperamentSection + $scenarioSection + $honestyFooterLines)
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
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + $temperamentSection + $scenarioSection + @('', '---', '', 'Scripted run -- no model judged this. The channel was exercised, including one deliberate illegal press.', '', '## Turn log', '', $fullLog) + $honestyFooterLines)
    Say ('scripted run complete, ' + $turn + ' turns. Channel log: ' + $findingsPath)
    exit 0
}

if ($isMonkey) {
    # W4, ruling 9: monkey skips the judge pass (and, further down, Scout's live mechanical
    # detectors and the ollama unload handoff) entirely -- "an essay about uniform-random input is
    # noise by construction." Backend/metrics/coverage/dead-verb are already computed above this
    # point for every mode, so this mirrors the Scripted branch's own shape rather than duplicating
    # any of that work.
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + $temperamentSection + $scenarioSection + @('', '---', '', 'Monkey run -- ruling 9: uniform-random input is noise by construction, so no judge pass was made. The mechanical sections above (backend/metrics/coverage/dead-verb) are the full evidence this run produces.', '', '## Turn log', '', $fullLog) + $honestyFooterLines)
    Say ('monkey run complete, ' + $turn + ' turns (seed ' + $Seed + '). Mechanical-only findings: ' + $findingsPath)
    exit 0
}

if ($isPilot) {
    # S2: pilot skips the judge pass too -- its OWN scripted policy already names what it was
    # trying to do and why (SixDecisions/FrictionLog), so there is no model narration to add; the
    # mechanical sections plus these two pilot-only sections are the full evidence this run produces.
    $frictionLines = @('## Friction log', '', ('Candidate-shaped, never asserted as fact -- same discipline the dead-verb ' +
        'detector above uses. Each entry names the turn/day/phase, what the pilot was TRYING to do, and quotes ' +
        'the on-screen text or refusal copy verbatim (never paraphrased).'), '')
    $frictionEntries = @($pilotMemory.FrictionLog)
    if ($frictionEntries.Count -eq 0) {
        $frictionLines += 'no friction entries this run.'
    } else {
        foreach ($f in $frictionEntries) {
            $frictionLines += ('- turn ' + $f.Turn + ', day ' + $f.Day + ', ' + $f.Phase + ', [' + $f.Category + '] trying: ' +
                $f.Trying + ' -- ' + $f.Detail)
        }
    }
    $frictionSection = @('', '---', '') + $frictionLines

    $decisionLines = @('## Six decisions this run took', '', ('CLAUDE.md names six decisions the game is actually made ' +
        'of. Each resolution below is a seeded coin flip (pilot.ps1''s own named probabilities), never always the ' +
        'same side -- a run where one always resolves the same way tested one player, not a person.'), '')
    $decisionEntries = @($pilotMemory.SixDecisions)
    if ($decisionEntries.Count -eq 0) {
        $decisionLines += 'no decision points of this shape came up this run.'
    } else {
        $byDecision = $decisionEntries | Group-Object -Property Decision
        foreach ($group in $byDecision) {
            $choices = $group.Group | Group-Object -Property Choice | ForEach-Object { $_.Name + ' x' + $_.Count }
            $decisionLines += ('- ' + $group.Name + ': ' + ($choices -join ', ') + ' (' + $group.Count + ' total)')
        }
    }
    $decisionSection = @('', '---', '') + $decisionLines

    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + $frictionSection + $decisionSection + $temperamentSection + $scenarioSection + @('', '---', '', 'Pilot run (S2, scripted-deep-pilot lane) -- a scripted, model-free, human-shaped policy played this session; no judge pass was made (there is no model narration to check for fabrication). The mechanical sections plus the Friction log and Six-decisions sections above are the full evidence this run produces.', '', '## Turn log', '', $fullLog) + $honestyFooterLines)
    Say ('pilot run complete, ' + $turn + ' turns (seed ' + $Seed + '), ' + $frictionEntries.Count + ' friction entr(y/ies), ' + $decisionEntries.Count + ' decision(s). Findings: ' + $findingsPath)
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

# Ruling 10 (docs/plans/2026-08-10-002) + U2 (playtest-finishes wave): single-model mode's own
# $Model (~6.1 GB, qwen3-vl:8b by default) and $JudgeModel (~9.3 GB, qwen3:14b by default) sum past
# this project's 14 GB VRAM ceiling if both stayed resident at once, so $residency.UnloadBeforeJudge
# names what must go first -- $Model in single-model mode, NOTHING in split mode (the brain model is
# already the only thing resident, and the judge call below reuses it -- see -BrainModel's own doc
# and model-call.ps1's Get-ModelResidencyPlan). `ollama stop` is a real, synchronous CLI unload,
# simpler than a bespoke keep_alive=0 request whose reply would just be thrown away. `ollama ps` is
# logged on both sides (Say, so it lands in driver.log too) so a live run can be READ, not just
# assumed, at the exact handoff point.
$residentBefore = @()
try {
    $psBefore = Invoke-RestMethod -Uri ($Endpoint + '/api/ps') -TimeoutSec 10
    $residentBefore = @($psBefore.models | ForEach-Object { $_.name + ' (' + [math]::Round($_.size / 1GB, 1) + ' GB)' })
} catch { Warn ('ollama ps (pre-unload) failed: ' + $_.Exception.Message) }
Say ('ollama ps before judge handoff: ' + (($residentBefore -join ', ')))
if (@($residency.UnloadBeforeJudge).Count -eq 0) {
    Say 'split mode: nothing to unload before the judge call -- the brain model stays resident and is reused directly'
} else {
    foreach ($toUnload in $residency.UnloadBeforeJudge) {
        # NOT `2>&1` on this call. Measured live (W1 verification): `ollama stop` writes its own
        # progress spinner as ANSI control codes to STDERR even on a clean, successful unload --
        # confirmed by `ollama ps` actually showing the model gone right after. Under this script's
        # `$ErrorActionPreference = 'Stop'`, redirecting a native command's stderr with `2>&1` in
        # Windows PowerShell 5.1 wraps each stderr write in a terminating NativeCommandError (see
        # docs/debugging.md-adjacent lesson on this exact PS 5.1 2>&1 trap), so the unload was
        # silently succeeding while this line reported it as failed. Leaving stderr unredirected
        # here lets a REAL failure still surface as non-zero exit / thrown error without
        # manufacturing a false one out of the spinner's own output.
        try { & ollama stop $toUnload | Out-Null } catch { Warn ('ollama stop ' + $toUnload + ' failed: ' + $_.Exception.Message) }
    }
}
$residentAfter = @()
try {
    $psAfter = Invoke-RestMethod -Uri ($Endpoint + '/api/ps') -TimeoutSec 10
    $residentAfter = @($psAfter.models | ForEach-Object { $_.name + ' (' + [math]::Round($_.size / 1GB, 1) + ' GB)' })
} catch { Warn ('ollama ps (post-unload) failed: ' + $_.Exception.Message) }
Say ('ollama ps before judge call to ' + $residency.JudgeModel + ': ' + (($residentAfter -join ', ')))

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
# W5 (docs/plans/2026-08-10-002): the scenario's Expected observation goes to the JUDGE ONLY, here --
# never to $actPrompt (see the act-prompt assembly above). The judge answers from the very log the
# actor produced, never from being told in advance what to expect.
if ($scenarioCard) {
    $judgeInput += @('') + (Get-ScenarioJudgeQuestionText -ExpectedObservation $scenarioCard.ExpectedObservation)
}
$findings = ''
# No image on the judge pass: the LOG is what carries the findings, and a 134 KB frame costs
# context that the log needs. Visual findings come from the act turns, which do see frames.
# $residency.JudgeModel -- $JudgeModel in single-model mode, the already-resident brain model in
# split mode -- see this file's own .PARAMETER JudgeModel/-BrainModel docs for why the judge is a
# dedicated text model rather than the vision model that just played.
try { $findings = Invoke-Model $judgePrompt (($judgeInput) -join [Environment]::NewLine) $null $residency.JudgeModel } catch { Warn ('judge call failed: ' + $_.Exception.Message) }

# THE JUDGE MUST NOT OVERSTAY. Found by the 2026-08-10 shakedown, in the first hour of the first
# sweep on the new stack: ruling 10 unloads $Model before the judge, but nothing unloaded
# $JudgeModel AFTER the run -- so run 1's judge (9.3 GB) sat resident into run 2's GPU gate, which
# saw 0.8 GB free against its 8 GB floor and REFUSED in one second. Four of five persona runs died
# that way, each reported honestly as MISSING by the sweep (the honesty machinery worked; the
# residency was the defect). The gate's resident-model exemption (#433) deliberately covers only
# the model the run itself is about to use, so a leftover judge can never ride it. Same idiom as
# the ruling-10 unload above: synchronous CLI stop, stderr unredirected (the PS 5.1 2>&1 trap).
# $residency.UnloadAfterRun (U2, playtest-finishes wave): $JudgeModel in single-model mode, the
# brain model in split mode -- either way, exactly what was left resident by the judge call above.
foreach ($toUnload in $residency.UnloadAfterRun) {
    try { & ollama stop $toUnload | Out-Null } catch { Warn ('ollama stop ' + $toUnload + ' failed: ' + $_.Exception.Message) }
    Say ($toUnload + ' unloaded -- the next run''s GPU gate starts clean')
}

# W5: the "## Scenario verdict" section -- written ABOVE the model's own prose at every Set-Content
# site below (mirrors $backendSection/$metricsSection/$deadVerbSection/$temperamentSection's own
# build-once-use-everywhere shape). Stays the empty array declared above whenever -Scenario was not
# given at all.
if ($scenarioCard) {
    $scenarioJudgeVerdict = [pscustomobject]@{ Verdict = 'UNKNOWN'; Quote = '' }
    if ($findings) {
        $scenarioJudgeVerdict = Get-ScenarioVerdictFromJudgeText -JudgeText $findings
    } else {
        Warn 'scenario verdict: the judge call itself produced nothing, so the model observation is UNKNOWN (not NOT SEEN -- never fabricate a negative the judge never gave).'
    }
    $scenarioSection = @('', '---', '') +
        ((Format-ScenarioVerdictSection -Card $scenarioCard -JudgeVerdict $scenarioJudgeVerdict -BackendResult $scenarioBackendResult) -split [Environment]::NewLine)
    Say ('scenario verdict: ' + $scenarioJudgeVerdict.Verdict)
}

if (-not $findings) {
    # $fullLog here, matching the label -- "Raw turn log below" should mean the raw text, not $log
    # (the judge's own per-day digest input, which is what just failed to produce anything).
    Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + $temperamentSection + $scenarioSection + @('', '---', '', 'JUDGE FAILED -- no findings written. Raw turn log below.') + $mechanicalSection + @('', $fullLog) + $honestyFooterLines)
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

Set-Content -Path $findingsPath -Encoding utf8 -Value ($header + $backendSection + $metricsSection + $deadVerbSection + $temperamentSection + $scenarioSection + @('', '---', '') + @($findings) + $guardNote + $mechanicalSection + @("", "---", "", "## Turn log", "", $fullLog) + $honestyFooterLines)

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
if ($inertRun) {
    Warn ('INERT run: ' + $inertTurns + ' of ' + $actingTurns + ' acting commands (' + $inertPct +
        '%) changed nothing on screen; longest dead streak ' + $inertStreakWorst + ' turns. The game was not ' +
        'receiving what the driver sent, so nothing below was actually tested. Exiting non-zero.')
}
if ($degraded -or $incomplete -or $inertRun) {
    exit 1
}
exit 0
