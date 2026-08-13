<#
.SYNOPSIS
    Proves tools/playtest-sweep.ps1's pure logic (matrix planning, aggregation, defensive field
    reading, recurrence matching) without Godot, ollama, or VRAM.

.DESCRIPTION
    Same shape as tools/test-agent-playtest-modes.ps1 and tools/test-play-launcher-guard.ps1: no
    mocking framework, no Pester (this repo does not use one). What differs here is HOW the
    script under test is invoked: tools/playtest-sweep.ps1's own -DryRun and -AggregateFrom paths
    both end in `exit 0`/`exit 1`, and `exit` inside a script terminates the calling PowerShell
    PROCESS, not just that script's scope -- true whether the script is dot-sourced or invoked via
    `&`. Dot-sourcing it here to poke at its functions directly would therefore kill this test
    runner the first time a real assertion needed the DryRun or AggregateFrom path. So every check
    below invokes tools/playtest-sweep.ps1 as a genuinely separate `powershell -File` process (the
    same pattern test-play-launcher-guard.ps1 uses for play.bat) and inspects only what a real
    caller could see: console output, exit code, and the files it wrote.

    MEASURED (2026-08-10): a `-File` child process does NOT split a comma-joined argument into an
    array the way typing the same text at an interactive prompt would --
    `powershell -File probe.ps1 -Items Full,Scout` binds ONE string "Full,Scout" to a [string[]]
    parameter, not two elements. Verified directly against a throwaway probe script before writing
    this file. tools/playtest-sweep.ps1 now splits and validates -Scopes/-Personas itself
    (Split-CommaList) precisely because of this -- section 2 below is also that fix's regression
    test, not just a convenience for this file.

    Only pure-logic and file-producing paths are exercised. -DryRun and -AggregateFrom both refuse
    to reach Invoke-SweepRun (the only function that can launch Godot or hold ollama's model), so
    nothing here can touch either -- required by this task's own hard constraints.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file in
    tools/agent-playtest/.

.EXAMPLE
    powershell -File tools/agent-playtest/tests/test-playtest-sweep.ps1
#>

$testsDir = $PSScriptRoot
$toolsDir = Split-Path -Parent (Split-Path -Parent $testsDir)
$scriptPath = Join-Path $toolsDir 'playtest-sweep.ps1'

$failures = New-Object System.Collections.ArrayList
$passes = 0

function Check {
    param([bool]$Condition, [string]$Description)
    if ($Condition) {
        $script:passes++
    } else {
        [void]$script:failures.Add($Description)
    }
}

if (-not (Test-Path $scriptPath)) {
    Write-Host ('FAIL: expected tools/playtest-sweep.ps1 at ' + $scriptPath)
    exit 1
}

# --- 1. AST parse check --------------------------------------------------------------------------
# Same reason tools/test-agent-playtest-modes.ps1 does this for its own targets: a BOM-less UTF-8
# save or a mis-indented here-string terminator is cheaper to catch here, by file, than as a
# confusing failure three functions deep during a real sweep.
$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
$errCount = 0
if ($parseErrors) { $errCount = $parseErrors.Count }
Check ($errCount -eq 0) ('parse: ' + $scriptPath + ' has ' + $errCount + ' syntax error(s): ' + (($parseErrors | ForEach-Object { $_.Message }) -join ' | '))

# --- 2. -DryRun prints the exact expected matrix, launches/creates nothing -----------------------

$dryOutMarker = Join-Path $env:TEMP 'playtest-sweep-test-dryrun-outdir-marker'

$dryRunLines = & powershell -NoProfile -File $scriptPath -DryRun -Runs 2 -Scopes Full,Scout -Personas first-timer,veteran -Turns 40 -OutDir $dryOutMarker
$dryRunExit = $LASTEXITCODE
$dryRunText = ($dryRunLines -join "`n")

Check ($dryRunExit -eq 0) ('DryRun must exit 0, got ' + $dryRunExit)
Check ($dryRunText -match '8 run\(s\) would be launched') ('DryRun must report exactly 8 runs (2 scopes x 2 personas x 2 repeats) for the required example. Got: ' + $dryRunText.Substring(0, [Math]::Min(300, $dryRunText.Length)))

$expectedTags = @(
    'Full-first-timer-1', 'Full-first-timer-2', 'Full-veteran-1', 'Full-veteran-2',
    'Scout-first-timer-1', 'Scout-first-timer-2', 'Scout-veteran-1', 'Scout-veteran-2'
)
foreach ($tag in $expectedTags) {
    Check ($dryRunText -match [regex]::Escape($tag)) ('DryRun matrix must list tag ' + $tag)
}

Check (-not (Test-Path $dryOutMarker)) 'DryRun must create NOTHING on disk -- the -OutDir path it was given must still not exist'

# A tighter, single scope/persona case -- proves -Runs multiplies correctly (not just "some rows
# came out") and that a single (unambiguous) value round-trips with no comma-splitting involved.
$singleLines = & powershell -NoProfile -File $scriptPath -DryRun -Runs 3 -Scopes Full -Personas veteran -Turns 10
$singleExit = $LASTEXITCODE
$singleText = ($singleLines -join "`n")
Check ($singleExit -eq 0) ('single-scope/persona DryRun must exit 0, got ' + $singleExit)
Check ($singleText -match '3 run\(s\) would be launched') ('-Runs 3 with one scope/persona must plan exactly 3 runs. Got: ' + $singleText.Substring(0, [Math]::Min(200, $singleText.Length)))
Check ($singleText -match [regex]::Escape('Full-veteran-1')) 'single case must include Full-veteran-1'
Check ($singleText -match [regex]::Escape('Full-veteran-3')) 'single case must include Full-veteran-3 (proves -Runs actually multiplies)'

# Adversarial-audit finding A, THE REGRESSION PIN: before this fix, playtest-sweep.ps1 never passed
# -Seed to the driver at all, so every repeat (including these three) silently reused the driver's
# hardcoded default of 1. Get-SeedForRun derives the seed from each run's own repeat index, and
# Format-RunMatrix's DryRun preview (checked here, no Godot needed) must show three DIFFERENT seeds
# for these three repeats of the same scope/persona pair -- not the same value three times.
Check ($singleText -match 'seed') 'DryRun matrix header must include a seed column'
Check ($singleText -match 'Full-veteran-1\s+Full\s+veteran\s+10\s+1\s') ('repeat 1 must be planned with seed 1. Got: ' + $singleText)
Check ($singleText -match 'Full-veteran-2\s+Full\s+veteran\s+10\s+2\s') ('repeat 2 must be planned with seed 2 (DIFFERENT from repeat 1). Got: ' + $singleText)
Check ($singleText -match 'Full-veteran-3\s+Full\s+veteran\s+10\s+3\s') ('repeat 3 must be planned with seed 3 (DIFFERENT from repeats 1 and 2). Got: ' + $singleText)

# An unknown scope must fail LOUDLY (Split-CommaList's whole reason for existing over ValidateSet
# -- see playtest-sweep.ps1's own comment on why -Scopes has no ValidateSet attribute), not fall
# back to the default silently. Same class of regression this repo has already paid for once
# (a silent fallback reading as success) -- checked here so it cannot happen unnoticed a second
# time in this new tool.
& powershell -NoProfile -File $scriptPath -DryRun -Runs 1 -Scopes NoSuchScope -Personas veteran | Out-Null
$badScopeExit = $LASTEXITCODE
Check ($badScopeExit -ne 0) 'an unknown -Scopes value must exit non-zero (refuse loudly), not silently default'

# --- 3. Aggregation over fixture run directories --------------------------------------------------
# Fixed, reused directory name (never deleted by this script -- matches
# tools/test-play-launcher-guard.ps1's own "deliberately never deletes its sandboxes" note; this
# task's own hard constraints also ban rm/Remove-Item outright). Every write below is -Force /
# overwrite, so re-running this test is idempotent.
$fixtureRoot = Join-Path $env:TEMP 'playtest-sweep-test-fixtures'
$runA = Join-Path $fixtureRoot 'Full-first-timer-1'
$runB = Join-Path $fixtureRoot 'Scout-veteran-1'
$runC = Join-Path $fixtureRoot 'Diff-sceptic-1'
$runD = Join-Path $fixtureRoot 'Full-monkey-1'
foreach ($d in @($fixtureRoot, $runA, $runB, $runC, $runD)) {
    New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop | Out-Null
}

# runA: a clean run. Shares one finding line with runB (verbatim text, different bullet marker)
# to prove recurrence groups match ACROSS runs, and touches {ForgePanel, ShopPanel} for the
# coverage-union check below.
$findingsA = @(
    '# Agent playtest findings (Scope: Full)',
    '',
    '- scope: Full',
    '- persona: first-timer (requested: random), act-prompt hash a1b2c3d4',
    '- model: llava:7b',
    '- turns: 40 (stopped: turn budget reached)',
    '- completion: 40 of 40 budgeted turns (100.0%)',
    '- model-driven turns: 38',
    '- fallback turns: 2 (5.0% of total)',
    '- imageless turns: 0',
    '- artifacts: ' + $runA,
    '- playtest log (day/phase/beat/cause per tick, every action): ' + (Join-Path $runA 'playtest-log.jsonl'),
    '',
    '1. The forge panel bellows meter never resets between crafts so a fresh customer sees leftover heat.',
    '2. Something unique to run A only about the shop haggle button being unresponsive on first click.',
    '',
    '## Turn log',
    '',
    '- day 1 phase Morning beat None location town canMove=True',
    '- day 3 phase Camp beat VigilStop location town canMove=False',
    '- day 5 phase Evening beat None location town canMove=True'
) -join "`n"
Set-Content -Path (Join-Path $runA 'findings.md') -Value $findingsA -Encoding utf8

$turnlogA = @(
    '- day 1 phase Morning beat None location town',
    '- day 3 phase Camp beat VigilStop location town',
    '- day 5 phase Evening beat None location town'
) -join "`n"
Set-Content -Path (Join-Path $runA 'turnlog.md') -Value $turnlogA -Encoding utf8

# The REAL U3 shape (Get-CoverageReport on feat/playtest-keeps-what-it-saw): a Categories array,
# not the flat touched/untouched this fixture originally guessed. One category is enough for the
# union math; the reader prefixes each surface with its category name.
Set-Content -Path (Join-Path $runA 'coverage.json') -Value '{"Categories":[{"Category":"Panel","Total":5,"Touched":["ForgePanel","ShopPanel"],"Untouched":["AlchemyPanel","EngineeringPanel","TanningPanel"],"Percentage":40.0}],"OverallTouched":2,"OverallTotal":5,"OverallPercentage":40.0,"Caveats":[]}' -Encoding utf8
# The REAL U2 shape (Get-BackendSummary): no contradictionCount scalar -- the two
# contradiction-shaped facts ride as separate counts.
Set-Content -Path (Join-Path $runA 'backend.json') -Value '{"Available":true,"AutoAdvanceCount":1,"UnattributedAdvanceCount":0}' -Encoding utf8
Set-Content -Path (Join-Path $runA 'run-meta.json') -Value '{"tag":"Full-first-timer-1","scope":"Full","persona":"first-timer","personaPassedToDriver":false,"exitCode":0}' -Encoding utf8
# W2 (docs/plans/2026-08-10-002): runA's metrics.json -- the product sentence FIRED (screen showed a
# MakersMark item), one day of entropy data. Get-MetricsSummary's REAL shape (metrics.ps1).
Set-Content -Path (Join-Path $runA 'metrics.json') -Value '{"PerDayEntropy":[{"Day":"1","TotalActions":4,"DistinctActionTypes":3,"EntropyBits":1.5}],"ProductSentence":{"ProductSentenceFired":true}}' -Encoding utf8

# runB: DEGRADED AND INCOMPLETE. Shares the SAME finding line as runA (recurrence proof), touches
# {ShopPanel, TavernPanel} (coverage-union proof: union touched = {Forge,Shop,Tavern}, total known
# = {Forge,Shop,Alchemy,Engineering,Tanning,Tavern} = 6, never-touched = {Alchemy,Engineering,
# Tanning} = 3).
$findingsB = @(
    'INCOMPLETE: only 9 of 80 budgeted turns ran (11.3%), under the 50% floor -- stopped early (client wrote no state within 90s). Findings below cover a fraction of the intended run.',
    '',
    'DEGRADED: 3 of 9 turns (33.3%) fell back to "advance" because the model gave no usable command. That is over the 25% floor -- this run mostly pressed advance, not played, and its findings below should be read with that in mind.',
    '',
    '# INCOMPLETE AND DEGRADED -- agent playtest findings (Scope: Scout)',
    '',
    '- scope: Scout',
    '- model: llava:7b',
    '- turns: 9 (stopped: client wrote no state within 90s)',
    '- completion: 9 of 80 budgeted turns (11.3%)',
    '- model-driven turns: 6',
    '- fallback turns: 3 (33.3% of total)',
    '- imageless turns: 1',
    '- artifacts: ' + $runB,
    '- playtest log (day/phase/beat/cause per tick, every action): ' + (Join-Path $runB 'playtest-log.jsonl'),
    '',
    '- The forge panel bellows meter never resets between crafts so a fresh customer sees leftover heat.',
    '- Something unique to run B only about the tavern door never opening from the west side.',
    '',
    '## Turn log',
    '',
    '- day 1 phase Morning beat None location town canMove=True',
    '- day 2 phase Camp beat VigilStop location town canMove=False'
) -join "`n"
Set-Content -Path (Join-Path $runB 'findings.md') -Value $findingsB -Encoding utf8

$turnlogB = @(
    '- day 1 phase Morning beat None location town',
    '- day 2 phase Camp beat VigilStop location town'
) -join "`n"
Set-Content -Path (Join-Path $runB 'turnlog.md') -Value $turnlogB -Encoding utf8

Set-Content -Path (Join-Path $runB 'coverage.json') -Value '{"Categories":[{"Category":"Panel","Total":6,"Touched":["ShopPanel","TavernPanel"],"Untouched":["ForgePanel","AlchemyPanel","EngineeringPanel","TanningPanel"],"Percentage":33.3}],"OverallTouched":2,"OverallTotal":6,"OverallPercentage":33.3,"Caveats":[]}' -Encoding utf8
Set-Content -Path (Join-Path $runB 'backend.json') -Value '{"Available":true,"AutoAdvanceCount":2,"UnattributedAdvanceCount":1}' -Encoding utf8
Set-Content -Path (Join-Path $runB 'run-meta.json') -Value '{"tag":"Scout-veteran-1","scope":"Scout","persona":"veteran","personaPassedToDriver":false,"exitCode":1}' -Encoding utf8
# runB's metrics.json -- the product sentence did NOT fire this run, two days of entropy data (the
# per-day-entropy-table proof needs at least one run with more than a single day of rows). U3
# (playtest-finishes wave): runB also ran -PatienceMode Sweep and logged TWO would-have-quit
# markers (turns 5 and 9) -- runA's metrics.json above carries no WouldHaveQuitMarkers key at all
# (a Quit-mode run, or a driver build that predates U3), so the two fixtures together prove BOTH the
# present-with-turns case and the absent-reads-as-empty case, never a silent zero either way.
Set-Content -Path (Join-Path $runB 'metrics.json') -Value '{"PerDayEntropy":[{"Day":"1","TotalActions":2,"DistinctActionTypes":1,"EntropyBits":0.0},{"Day":"2","TotalActions":3,"DistinctActionTypes":2,"EntropyBits":0.9183}],"ProductSentence":{"ProductSentenceFired":false},"PatienceMode":"Sweep","WouldHaveQuitMarkers":[{"Turn":5,"Day":1,"Phase":"Camp","Trigger":"quit day 1 Camp after 6 refusal(s) at BountiesPanel (turn 5)"},{"Turn":9,"Day":2,"Phase":"Morning","Trigger":"quit day 2 Morning after 6 refusal(s) at TavernDoor (turn 9)"}]}' -Encoding utf8

# runC: no findings.md at all -- proves "reported as missing, not skipped silently". Only a
# run-meta.json exists (proving exit code / scope / persona can still be recovered even when the
# driver's own report is absent). Also has NO metrics.json -- the defensive-reading proof for W2's
# own reader (Get-MetricsData): absent must read as an empty cell/note, never a silent "not fired".
Set-Content -Path (Join-Path $runC 'run-meta.json') -Value '{"tag":"Diff-sceptic-1","scope":"Diff","persona":"sceptic","personaPassedToDriver":false,"exitCode":1}' -Encoding utf8

# runD (U4, eyes-learn-labels wave): backend.json IS present but carries only ONE of the two
# counters -- AutoAdvanceCount, no UnattributedAdvanceCount at all (a driver build that emits one
# but not the other, distinct from runC's "no backend.json at all" case above). Before this unit the
# reader's note only fired when BOTH fields were absent, so the missing UnattributedAdvanceCount
# rendered as a silent empty cell with NO note anywhere -- indistinguishable from "checked, zero."
Set-Content -Path (Join-Path $runD 'run-meta.json') -Value '{"tag":"Full-monkey-1","scope":"Full","persona":"monkey","personaPassedToDriver":false,"exitCode":0}' -Encoding utf8
Set-Content -Path (Join-Path $runD 'backend.json') -Value '{"Available":true,"AutoAdvanceCount":5}' -Encoding utf8

# runD is also this fixture set's INERT case: a run that used its whole budget and stayed
# model-driven -- so DEGRADED and INCOMPLETE both read it as pristine -- while nothing it pressed
# reached the game. That is the exact shape of the 2026-08-11 ten-rounds campaign, and the reason
# the third gauge exists (agent-playtest/completion.ps1's Get-InertVerdict).
$findingsD = @(
    'INERT: 190 of 200 acting commands (95%) changed nothing on screen, at or over the 50% floor. Longest dead streak: 47 turns. This run did not test the game -- treat every finding below as unproven.',
    '',
    '# INERT -- agent playtest findings (Scope: Full)',
    '',
    '- scope: Full',
    '- model: llava:7b',
    '- turns: 200 (stopped: turn budget reached)',
    '- completion: 200 of 200 budgeted turns (100%)',
    '- effective: 10 of 200 acting commands changed the screen (190 inert, 95%; longest dead streak 47)',
    '- model-driven turns: 200',
    '- fallback turns: 0 (0% of total)',
    '- persona: monkey (requested: monkey), act-prompt hash deadbeef',
    '',
    '- Something unique to run D only about the anvil never showing a heat readout.',
    '',
    '## Turn log',
    '',
    '- day 1 phase Morning beat None location town canMove=True'
) -join "`n"
Set-Content -Path (Join-Path $runD 'findings.md') -Value $findingsD -Encoding utf8

# Run the aggregator for real, against these fixtures only -- no Godot, no ollama, no network.
& powershell -NoProfile -File $scriptPath -AggregateFrom $fixtureRoot | Out-Null
$aggExit = $LASTEXITCODE
# CHANGED, deliberately: this used to assert exit 0. That was wrong, and the wrongness is the whole
# point of this fix -- these fixtures contain runB (DEGRADED AND INCOMPLETE) and runC (findings.md
# missing entirely). A sweep carrying two unusable runs out of four must not tell its caller
# "success". The old assertion pinned exactly the behaviour that let the 2026-08-11 ten-rounds
# campaign be reported as "78 runs, zero crashes" while every interact in it was a no-op.
Check ($aggExit -eq 1) ('-AggregateFrom must exit NON-ZERO when the sweep contains unusable runs ' +
    '(these fixtures have a DEGRADED+INCOMPLETE run and a MISSING one), got ' + $aggExit)

$summaryPath = Join-Path $fixtureRoot 'SUMMARY.csv'
$reportPath = Join-Path $fixtureRoot 'REPORT.md'
Check (Test-Path $summaryPath) ('SUMMARY.csv must be written to ' + $summaryPath)
Check (Test-Path $reportPath) ('REPORT.md must be written to ' + $reportPath)

if (Test-Path $summaryPath) {
    $rows = @(Import-Csv $summaryPath)
    Check ($rows.Count -eq 4) ('SUMMARY.csv must have exactly 4 rows (one per fixture run dir), got ' + $rows.Count)

    $rowA = $rows | Where-Object { $_.RunTag -eq 'Full-first-timer-1' } | Select-Object -First 1
    $rowB = $rows | Where-Object { $_.RunTag -eq 'Scout-veteran-1' } | Select-Object -First 1
    $rowC = $rows | Where-Object { $_.RunTag -eq 'Diff-sceptic-1' } | Select-Object -First 1
    $rowD = $rows | Where-Object { $_.RunTag -eq 'Full-monkey-1' } | Select-Object -First 1

    Check ($null -ne $rowA) 'SUMMARY.csv must contain a row for Full-first-timer-1'
    Check ($null -ne $rowB) 'SUMMARY.csv must contain a row for Scout-veteran-1'
    Check ($null -ne $rowC) 'SUMMARY.csv must contain a row for Diff-sceptic-1, not skip it silently'
    Check ($null -ne $rowD) 'SUMMARY.csv must contain a row for Full-monkey-1, not skip it silently'

    # U4 (eyes-learn-labels wave): THE REGRESSION PIN -- a backend.json present with only ONE of the
    # two counters. The present field must carry its real value; the ABSENT field's cell must be
    # empty (never a coerced 0); and the Notes column must carry the ONE fixed, greppable phrase
    # regardless of which field was missing.
    if ($rowD) {
        Check ($rowD.AutoAdvanceCount -eq '5') ('runD AutoAdvanceCount (the field IT DOES emit) must be 5, got [' + $rowD.AutoAdvanceCount + ']')
        Check ([string]::IsNullOrEmpty($rowD.UnattributedAdvanceCount)) ('THE REGRESSION PIN: runD UnattributedAdvanceCount (absent from its backend.json) must be an EMPTY cell, never a silent 0 -- got [' + $rowD.UnattributedAdvanceCount + ']')
        Check ($rowD.Notes -match 'backend counters not in this driver build') ('THE REGRESSION PIN: runD Notes must carry "backend counters not in this driver build" for its one missing counter, got [' + $rowD.Notes + ']')
        Check ($rowD.Notes -match 'UnattributedAdvanceCount') ('runD Notes must NAME the specific missing field, got [' + $rowD.Notes + ']')
    }

    if ($rowA) {
        Check ($rowA.Verdict -eq 'CLEAN') ('runA verdict must be CLEAN, got [' + $rowA.Verdict + ']')
        Check ($rowA.CompletionRatio -eq '100%') ('runA CompletionRatio must be "100%%", got [' + $rowA.CompletionRatio + ']')
        Check ($rowA.LastInGameDay -eq '5') ('runA LastInGameDay must be 5 (max of 1,3,5 in its turnlog), got [' + $rowA.LastInGameDay + ']')
        Check ($rowA.ExitCode -eq '0') ('runA ExitCode must come from run-meta.json (0), got [' + $rowA.ExitCode + ']')
        Check ($rowA.AutoAdvanceCount -eq '1') ('runA AutoAdvanceCount must be 1 (real U2 field, not the guessed contradictionCount), got [' + $rowA.AutoAdvanceCount + ']')
        Check ($rowA.UnattributedAdvanceCount -eq '0') ('runA UnattributedAdvanceCount must be 0, got [' + $rowA.UnattributedAdvanceCount + ']')
        # U4's real combined header line: resolved name captured WITHOUT the "(requested: ...)"
        # parenthetical -- "first-timer (requested: random)" as a grouping key would split every
        # persona into as many groups as there were request spellings.
        Check ($rowA.Persona -eq 'first-timer') ('runA Persona must be the bare resolved name "first-timer", got [' + $rowA.Persona + ']')
        Check ($rowA.PromptHash -eq 'a1b2c3d4') ('runA PromptHash must parse from the combined header line, got [' + $rowA.PromptHash + ']')
        # W2 (docs/plans/2026-08-10-002): runA's metrics.json says the product sentence FIRED.
        Check ($rowA.ProductSentenceFired -eq 'True') ('runA ProductSentenceFired must be True (its metrics.json says so), got [' + $rowA.ProductSentenceFired + ']')
        # U3 (playtest-finishes wave): runA's metrics.json carries NO WouldHaveQuitMarkers key at
        # all (a Quit-mode run, or a driver build predating U3) -- the cell must be EMPTY, never a
        # coerced "0" or a silent blank that could be misread either way.
        Check ([string]::IsNullOrEmpty($rowA.WouldHaveQuitTurns)) ('runA WouldHaveQuitTurns must be empty (no WouldHaveQuitMarkers key at all), got [' + $rowA.WouldHaveQuitTurns + ']')
    }
    if ($rowB) {
        Check ($rowB.Verdict -eq 'DEGRADED + INCOMPLETE') ('runB verdict must name both, got [' + $rowB.Verdict + ']')
        Check ($rowB.LastInGameDay -eq '2') ('runB LastInGameDay must be 2, got [' + $rowB.LastInGameDay + ']')
        Check ($rowB.ProductSentenceFired -eq 'False') ('runB ProductSentenceFired must be False (its metrics.json says so), got [' + $rowB.ProductSentenceFired + ']')
        # U3 (playtest-finishes wave): runB logged TWO would-have-quit markers (turns 5 and 9) --
        # THE REGRESSION PIN for the comma-joined column: both turn numbers must appear, in order,
        # never just the last one or a bare count.
        Check ($rowB.WouldHaveQuitTurns -eq '5,9') ('runB WouldHaveQuitTurns must be "5,9" (both markers, in order), got [' + $rowB.WouldHaveQuitTurns + ']')
    }
    if ($rowC) {
        Check ($rowC.Verdict -eq 'MISSING') ('runC (no findings.md) verdict must be MISSING, got [' + $rowC.Verdict + ']')
        Check ([string]::IsNullOrEmpty($rowC.ModelDrivenTurns)) ('runC must have an EMPTY ModelDrivenTurns cell (findings.md absent), never a coerced 0 -- got [' + $rowC.ModelDrivenTurns + ']')
        Check ($rowC.Notes -match 'findings.md missing') 'runC Notes must say findings.md is missing, not stay blank'
    }

    # Defensive-empty-cell proof (the brief's own required scenario): coverage/backend fields must
    # be EMPTY, not a silent zero, wherever the source file was absent -- runC has neither
    # coverage.json nor backend.json.
    if ($rowC) {
        Check ([string]::IsNullOrEmpty($rowC.CoveragePercentage)) ('runC CoveragePercentage must be empty (no coverage.json), not a silent 0 -- got [' + $rowC.CoveragePercentage + ']')
        Check ([string]::IsNullOrEmpty($rowC.UntouchedSurfaceCount)) ('runC UntouchedSurfaceCount must be empty, not a silent 0 -- got [' + $rowC.UntouchedSurfaceCount + ']')
        Check ([string]::IsNullOrEmpty($rowC.AutoAdvanceCount)) ('runC AutoAdvanceCount must be empty, not a silent 0 -- got [' + $rowC.AutoAdvanceCount + ']')
        Check ([string]::IsNullOrEmpty($rowC.UnattributedAdvanceCount)) ('runC UnattributedAdvanceCount must be empty, not a silent 0 -- got [' + $rowC.UnattributedAdvanceCount + ']')
        # W2's own defensive-reading proof: runC has NO metrics.json, so its ProductSentenceFired
        # cell must be EMPTY -- never a coerced "False", which would read as "checked, and it didn't
        # fire" instead of "never checked at all."
        Check ([string]::IsNullOrEmpty($rowC.ProductSentenceFired)) ('runC ProductSentenceFired must be empty (no metrics.json), not a silent False -- got [' + $rowC.ProductSentenceFired + ']')
        # U3 (playtest-finishes wave): runC has NO metrics.json at all -- WouldHaveQuitTurns must be
        # empty too, same defensive-reading posture as every other metrics.json-derived field.
        Check ([string]::IsNullOrEmpty($rowC.WouldHaveQuitTurns)) ('runC WouldHaveQuitTurns must be empty (no metrics.json), got [' + $rowC.WouldHaveQuitTurns + ']')
    }
}

if (Test-Path $reportPath) {
    $report = Get-Content $reportPath -Raw

    # W2 (docs/plans/2026-08-10-002): REPORT.md must LEAD with "the sentence the game exists to
    # produce fired in K of N runs" -- read from each run's own metrics.json. K=1 (runA fired),
    # N-with-metrics=2 (runA + runB both have metrics.json; runC/runD do not), total=4 (U4 added runD).
    $productSentenceLineIdx = $report.IndexOf('The sentence the game exists to produce fired')
    $deepestDayHeadingIdx = $report.IndexOf('## Deepest day reached')
    Check ($productSentenceLineIdx -ge 0) ('REPORT.md must contain the product-sentence lead line at all. Report:' + [Environment]::NewLine + $report)
    Check ($productSentenceLineIdx -ge 0 -and $productSentenceLineIdx -lt $deepestDayHeadingIdx) 'REPORT.md must LEAD with the product-sentence line -- it must appear BEFORE the Deepest day reached section, not buried after it'
    Check ($report -match [regex]::Escape('fired in 1 of 2 run(s) with metrics.json available')) ('REPORT.md must report exactly 1 of 2 runs-with-metrics fired. Report:' + [Environment]::NewLine + $report)
    Check ($report -match [regex]::Escape('2 of 4 total run(s) had no usable metrics.json')) 'REPORT.md must name runC AND runD as the two runs with no usable metrics.json, not silently drop them from the denominator'

    # Per-day entropy table across runs: runA's day 1 (1.5 bits) and runB's day 1/day 2 (0/0.9183
    # bits) must all appear, each attributed to its own run tag.
    Check ($report -match '## Per-day action entropy across runs') 'REPORT.md must have a per-day entropy table section'
    Check ($report -match '\| Full-first-timer-1 \| 1 \| 1\.5 \|') ('REPORT.md entropy table must include runA''s day 1 row (1.5 bits). Report:' + [Environment]::NewLine + $report)
    Check ($report -match '\| Scout-veteran-1 \| 1 \| 0(\.0+)? \|') 'REPORT.md entropy table must include runB''s day 1 row (0 bits)'
    Check ($report -match '\| Scout-veteran-1 \| 2 \| 0\.9183 \|') 'REPORT.md entropy table must include runB''s day 2 row (0.9183 bits)'

    # Coverage union: total 6 known surfaces, 3 touched (Forge/Shop/Tavern), 3 never touched
    # (Alchemy/Engineering/Tanning) -- see the fixture comments above for the arithmetic.
    Check ($report -match 'Total known surfaces: 6') ('REPORT.md coverage union must report 6 total known surfaces. Report:' + [Environment]::NewLine + $report)
    Check ($report -match 'Touched by at least one run: 3') 'REPORT.md coverage union must report 3 touched'
    Check ($report -match 'AlchemyPanel') 'REPORT.md never-touched list must name AlchemyPanel'
    Check ($report -match 'EngineeringPanel') 'REPORT.md never-touched list must name EngineeringPanel'
    Check ($report -match 'TanningPanel') 'REPORT.md never-touched list must name TanningPanel'
    Check ($report -notmatch 'ForgePanel\b.*never touched|never touched.*ForgePanel') 'ForgePanel was touched by runA and must NOT appear as never-touched'

    # Recurrence: the bellows-meter line appears in BOTH runA and runB (different bullet marker,
    # same normalised text) -- must be reported as a 2-run recurrence, not two separate findings.
    Check ($report -match '\(2 runs:') ('REPORT.md must report a 2-run recurrence group for the shared bellows finding. Report:' + [Environment]::NewLine + $report)
    Check ($report -match 'bellows meter never resets') 'REPORT.md recurrence section must quote the shared finding text'

    # Named bad runs with CAUSE, never a bare count.
    Check ($report -match 'Scout-veteran-1: INCOMPLETE: only 9 of 80') 'REPORT.md must name Scout-veteran-1''s INCOMPLETE cause verbatim from its own findings.md sentence'
    Check ($report -match 'Scout-veteran-1: DEGRADED: 3 of 9 turns') 'REPORT.md must name Scout-veteran-1''s DEGRADED cause verbatim'
    # The INERT gauge must be named with cause exactly like its two older siblings. A run the harness
    # itself disowned for never reaching the game is the LAST thing that should be quietly averaged
    # into a sweep's conclusions.
    Check ($report -match 'INERT: 190 of 200 acting commands') 'REPORT.md must name the INERT run''s cause verbatim, the same way it names DEGRADED and INCOMPLETE'
    Check ($report -match 'Diff-sceptic-1: MISSING') 'REPORT.md must name Diff-sceptic-1 as MISSING, not omit it'

    # Deepest day / day-11 answer: max day across fixtures is 5, well short of day 10 -- the report
    # must say the day-11 question was NOT answered, and name the 11-16 turns/day + 110-160 turn
    # figures from the plan doc rather than staying silent about the shortfall.
    Check ($report -match 'Deepest in-game day reached by any run in this sweep: 5') 'REPORT.md must name the correct deepest day (5) across all fixtures'
    Check ($report -match 'NOT answered') 'REPORT.md must say the day-11 question was NOT answered when the deepest day is well short of 10'
    Check ($report -match '110-160') 'REPORT.md must name the turn budget (110-160) needed to reach day 10 at the measured 11-16 turns/day rate'
}

# --- 4. Partial persona collapse (adversarial-audit finding B) ------------------------------------
# A SEPARATE fixture root: exactly two of four personas (first-timer, speedrunner) share ONE
# act-prompt hash; the other two (veteran, completionist) each have their own distinct hash. The OLD
# Get-PersonaDifferences flattened every row's PromptHash across the WHOLE sweep and fired its caveat
# only when the resulting set had <= 1 distinct value -- i.e. only on TOTAL collapse (every persona
# converged). With 3 distinct hashes present here (one shared, two unique) the old code's count was
# 3, stayed silent, and REPORT.md would have printed a confident bullet for speedrunner as though it
# had been played separately from first-timer. There was zero direct test coverage of this
# aggregation function before this fix -- this is that regression's own test.
$collapseRoot = Join-Path $env:TEMP 'playtest-sweep-test-fixtures-collapse'
$cRunFirstTimer = Join-Path $collapseRoot 'Full-first-timer-1'
$cRunVeteran = Join-Path $collapseRoot 'Full-veteran-1'
$cRunSpeedrunner = Join-Path $collapseRoot 'Full-speedrunner-1'
$cRunCompletionist = Join-Path $collapseRoot 'Full-completionist-1'
foreach ($d in @($collapseRoot, $cRunFirstTimer, $cRunVeteran, $cRunSpeedrunner, $cRunCompletionist)) {
    New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop | Out-Null
}

function New-CollapseFindings {
    param([string]$RunDir, [string]$Persona, [string]$Hash)
    return @(
        '# Agent playtest findings (Scope: Full)',
        '',
        '- scope: Full',
        ('- persona: ' + $Persona + ' (requested: random), act-prompt hash ' + $Hash),
        '- model: llava:7b',
        '- turns: 5 (stopped: turn budget reached)',
        '- completion: 5 of 5 budgeted turns (100.0%)',
        '- model-driven turns: 5',
        '- fallback turns: 0 (0.0% of total)',
        '- imageless turns: 0',
        ('- artifacts: ' + $RunDir),
        ('- playtest log (day/phase/beat/cause per tick, every action): ' + (Join-Path $RunDir 'playtest-log.jsonl')),
        '',
        '## Turn log',
        '',
        '- day 1 phase Morning beat None location town canMove=True'
    ) -join "`n"
}

# first-timer and speedrunner both land on aaaa1111 -- the collapsed pair. veteran (bbbb2222) and
# completionist (cccc3333) are each genuinely distinct and must NEVER be swept into the caveat.
Set-Content -Path (Join-Path $cRunFirstTimer 'findings.md') -Value (New-CollapseFindings -RunDir $cRunFirstTimer -Persona 'first-timer' -Hash 'aaaa1111') -Encoding utf8
Set-Content -Path (Join-Path $cRunVeteran 'findings.md') -Value (New-CollapseFindings -RunDir $cRunVeteran -Persona 'veteran' -Hash 'bbbb2222') -Encoding utf8
Set-Content -Path (Join-Path $cRunSpeedrunner 'findings.md') -Value (New-CollapseFindings -RunDir $cRunSpeedrunner -Persona 'speedrunner' -Hash 'aaaa1111') -Encoding utf8
Set-Content -Path (Join-Path $cRunCompletionist 'findings.md') -Value (New-CollapseFindings -RunDir $cRunCompletionist -Persona 'completionist' -Hash 'cccc3333') -Encoding utf8

& powershell -NoProfile -File $scriptPath -AggregateFrom $collapseRoot | Out-Null
$collapseExit = $LASTEXITCODE
Check ($collapseExit -eq 0) ('collapse-fixture -AggregateFrom must exit 0 (all four runs are CLEAN), got ' + $collapseExit)

$collapseReportPath = Join-Path $collapseRoot 'REPORT.md'
Check (Test-Path $collapseReportPath) ('REPORT.md must be written to ' + $collapseReportPath)
if (Test-Path $collapseReportPath) {
    $collapseReport = Get-Content $collapseReportPath -Raw

    Check ($collapseReport -match 'PARTIAL PERSONA COLLAPSE') ('REPORT.md must fire the partial-collapse caveat when exactly two of four personas share a hash. Report:' + [Environment]::NewLine + $collapseReport)
    Check ($collapseReport -match [regex]::Escape('aaaa1111')) 'REPORT.md must name the colliding hash value'

    # THE REGRESSION PIN: the collapsed pair must be named TOGETHER in the same caveat line, and
    # neither of the two genuinely-distinct personas may be swept into it.
    $collapseLines = @(($collapseReport -split "`r?`n") | Where-Object { $_ -match 'PARTIAL PERSONA COLLAPSE' })
    Check ($collapseLines.Count -eq 1) ('exactly one PARTIAL PERSONA COLLAPSE line must fire for one colliding hash, got ' + $collapseLines.Count)
    if ($collapseLines.Count -ge 1) {
        $collapseLine = $collapseLines[0]
        Check ($collapseLine -match 'first-timer') 'the collapse line must name first-timer'
        Check ($collapseLine -match 'speedrunner') 'the collapse line must name speedrunner'
        Check ($collapseLine -notmatch 'veteran') 'THE REGRESSION PIN: veteran has its OWN distinct hash and must NOT be named in the collapse line'
        Check ($collapseLine -notmatch 'completionist') 'THE REGRESSION PIN: completionist has its OWN distinct hash and must NOT be named in the collapse line'
    }

    Check ($collapseReport -notmatch 'TOTAL PERSONA COLLAPSE') 'this fixture is a PARTIAL collapse (2 of 4 personas), never TOTAL (which would require all 4 to share one hash)'
}

# --- 6. -PruneOnly's own git-ignore safety gate (fix/the-pilot-plays-like-a-person) -------------
# The pure retention.ps1 functions (Get-PlaytestRetentionPlan/Invoke-PlaytestRetentionPrune) already
# have their own thorough unit tests in tools/test-agent-playtest-modes.ps1, against synthetic
# fixtures under $env:TEMP, per this repo's own testing rule for deletion-adjacent code. What THIS
# file can additionally prove -- and the only retention behavior this file's own tests should cover,
# to honor the same "never against the real runs/" rule -- is the DRIVER's own extra safety gate:
# Invoke-RetentionAndReport (playtest-sweep.ps1's own wrapper) refuses to prune anything at all
# unless `git check-ignore` confirms the runs root first. A fixture under $env:TEMP is, by
# construction, never inside this repository at all, so this proves the refusal fires -- and that
# nothing is deleted when it does -- without ever needing a real runs/ directory or a real prune to
# succeed. Fixed, reused directory name (never deleted by this script -- same convention as
# $fixtureRoot/$collapseRoot above); every write below is -Force/overwrite, so re-running this test
# is idempotent.
$pruneOnlyRoot = Join-Path $env:TEMP 'playtest-sweep-test-pruneonly-fixture'
$pruneOnlyRunDir = Join-Path $pruneOnlyRoot 'Full-veteran-1'
$pruneOnlyFramesDir = Join-Path $pruneOnlyRunDir 'frames'
New-Item -ItemType Directory -Path $pruneOnlyFramesDir -Force -ErrorAction Stop | Out-Null
Set-Content -Path (Join-Path $pruneOnlyRunDir 'findings.md') -Value '# findings' -Encoding ascii
Set-Content -Path (Join-Path $pruneOnlyRunDir 'run-meta.json') -Value '{}' -Encoding ascii
Set-Content -Path (Join-Path $pruneOnlyFramesDir 'turn-1.png') -Value 'fixture frame bytes' -Encoding ascii
# Make it look old, so -KeepNewestRuns 0 -RetentionMinAgeMinutes 0 below would make it PRUNE-eligible
# if the git-ignore gate were not there at all -- a meaningful test needs a candidate that WOULD be
# pruned absent the gate, or "nothing was pruned" could just mean "nothing was eligible anyway".
(Get-Item (Join-Path $pruneOnlyFramesDir 'turn-1.png')).LastWriteTimeUtc = (Get-Date).ToUniversalTime().AddDays(-30)

$pruneOnlyOutput = & powershell -NoProfile -File $scriptPath -PruneOnly -OutDir $pruneOnlyRoot -KeepNewestRuns 0 -RetentionMinAgeMinutes 0 2>&1
$pruneOnlyExit = $LASTEXITCODE
Check ($pruneOnlyExit -eq 0) ('-PruneOnly against a non-repo temp fixture must still exit 0 (a refused prune is not a failure), got ' + $pruneOnlyExit)
$pruneOnlyText = ($pruneOnlyOutput | Out-String)
Check ($pruneOnlyText -match 'retention' -and $pruneOnlyText -match 'skipping') ('-PruneOnly must report that it is skipping retention when git cannot confirm the root is ignored. Output:' + [Environment]::NewLine + $pruneOnlyText)
Check (Test-Path (Join-Path $pruneOnlyFramesDir 'turn-1.png')) 'the fixture frame file must still exist -- the git-ignore gate must have refused to prune, not silently succeeded'

# --- Summary -----------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host ('FAIL (' + $failures.Count + ' of ' + ($passes + $failures.Count) + '):')
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}

Write-Host ('PASS: tools/playtest-sweep.ps1 pure logic, ' + $passes + '/' + $passes + ' checks, no Godot/ollama/VRAM needed.')
exit 0
