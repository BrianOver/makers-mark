<#
.SYNOPSIS
    Proves -Scope Diff / Scout's pure logic without a live Godot client, ollama, or VRAM.

.DESCRIPTION
    agent-playtest.ps1 itself cannot be run end to end here (needs a real Godot client, a loaded
    vision model, and 14 GB of free VRAM -- see its own header). What it CAN be proven without any
    of that is the part this file targets: the diff-to-surface map (A4), the per-turn prompt builder
    that now wires the digest's "beat" field through (the A5 follow-up), and the diff-section
    fallback logic that has to fire loudly whenever the map cannot resolve something.

    Same shape as tools/test-play-launcher-guard.ps1: dot-source the real logic, feed it synthetic
    (stubbed) input standing in for what the model would actually see, assert on the real output,
    print PASS/FAIL, exit accordingly. No mocking framework, no Pester -- this repo does not use one.

    Also AST-parses every file this change touches, since a BOM-less UTF-8 save or an indented
    here-string terminator has bitten this repo's PowerShell before (see agent-playtest.ps1's own
    style note) and a parse error is cheaper to catch here than on a real run.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it tests.

.EXAMPLE
    powershell -File tools/test-agent-playtest-modes.ps1
#>

$toolsDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $toolsDir

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

# --- 1. AST parse check ---------------------------------------------------------------------------
# A syntax error in a dot-sourced file surfaces as a confusing failure inside agent-playtest.ps1
# itself, far from the line that is actually wrong. Catch it here, directly, by file.
$parseTargets = @(
    (Join-Path $toolsDir 'agent-playtest.ps1'),
    (Join-Path $toolsDir 'agent-playtest\scope-map.ps1'),
    (Join-Path $toolsDir 'agent-playtest\turn-prompt.ps1'),
    (Join-Path $toolsDir 'agent-playtest\mechanical.ps1'),
    (Join-Path $toolsDir 'agent-playtest\completion.ps1'),
    (Join-Path $toolsDir 'agent-playtest\frames.ps1'),
    (Join-Path $toolsDir 'agent-playtest\backend.ps1'),
    (Join-Path $toolsDir 'agent-playtest\coverage.ps1'),
    (Join-Path $toolsDir 'agent-playtest\personas.ps1'),
    (Join-Path $toolsDir 'agent-playtest\model-call.ps1'),
    (Join-Path $toolsDir 'agent-playtest\footer.ps1'),
    (Join-Path $toolsDir 'agent-playtest\deadverb.ps1'),
    (Join-Path $toolsDir 'agent-playtest\metrics.ps1'),
    (Join-Path $toolsDir 'agent-playtest\temperament.ps1'),
    (Join-Path $toolsDir 'agent-playtest\monkey.ps1'),
    (Join-Path $toolsDir 'agent-playtest\attached.ps1'),
    (Join-Path $toolsDir 'agent-playtest\scenario.ps1'),
    (Join-Path $toolsDir 'agent-playtest\pilot.ps1')
)
foreach ($target in $parseTargets) {
    $tokens = $null
    $parseErrors = $null
    if (-not (Test-Path $target)) {
        Check $false ('parse: ' + $target + ' does not exist')
        continue
    }
    [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors) | Out-Null
    $errCount = 0
    if ($parseErrors) { $errCount = $parseErrors.Count }
    Check ($errCount -eq 0) ('parse: ' + $target + ' has ' + $errCount + ' syntax error(s): ' + (($parseErrors | ForEach-Object { $_.Message }) -join ' | '))
}

# --- 2. Scope map (A4) ------------------------------------------------------------------------
. (Join-Path $toolsDir 'agent-playtest\scope-map.ps1')

$panelSurface = Get-ScopeMapSurface 'godot/scripts/panels/ForgePanel.cs'
Check ($panelSurface -and $panelSurface -like '*Forge panel*') ('panel mapping: got [' + $panelSurface + ']')

$minigameSurface = Get-ScopeMapSurface 'godot/scripts/minigames/QuenchMinigame.cs'
Check ($minigameSurface -and $minigameSurface -like '*Quench minigame*') ('minigame mapping: got [' + $minigameSurface + ']')

$simModuleSurface = Get-ScopeMapSurface 'sim/GameSim/Bounties/BountyEngine.cs'
Check ($simModuleSurface -and $simModuleSurface -like '*Bounties panel*') ('sim module mapping: got [' + $simModuleSurface + ']')

$substrateSurface = Get-ScopeMapSurface 'sim/GameSim/Kernel/SimAdapter.cs'
Check ($substrateSurface -and $substrateSurface -like '*no single surface*') ('substrate mapping: got [' + $substrateSurface + ']')

$docsSurface = Get-ScopeMapSurface 'docs/design/THE-GAME.md'
Check ($docsSurface -and $docsSurface -like '*non-player-facing*') ('docs mapping: got [' + $docsSurface + ']')

$unresolvedSurface = Get-ScopeMapSurface 'godot/scripts/nosuchdir/Something.cs'
Check ($null -eq $unresolvedSurface) ('unknown path must resolve to null (unresolved): got [' + $unresolvedSurface + ']')

# Backslash paths (a caller on Windows might pass one) must normalize the same as git's forward slashes.
$backslashSurface = Get-ScopeMapSurface 'godot\scripts\panels\ShopPanel.cs'
Check ($backslashSurface -and $backslashSurface -like '*panel*') ('backslash path mapping: got [' + $backslashSurface + ']')

# Regression (2026-08-09): every pattern above requires a subdirectory segment, so a script sitting
# directly in godot/scripts/ (no panels/, minigames/, town2d/, ui/, audio/, tools/) always fell
# through to UNRESOLVED -- and MainUi.cs and RaidConductor.cs, the two busiest orchestration files
# in the client, live exactly there. Reproduced directly against fix/sendoff-skips-the-day: this is
# what made -Scope Diff "partially fall back" on a run whose diff and git plumbing were both fine.
$mainUiSurface = Get-ScopeMapSurface 'godot/scripts/MainUi.cs'
Check ($null -ne $mainUiSurface) ('top-level MainUi.cs must resolve, not fall through to UNRESOLVED: got [' + $mainUiSurface + ']')
$raidConductorSurface = Get-ScopeMapSurface 'godot/scripts/RaidConductor.cs'
Check ($null -ne $raidConductorSurface) ('top-level RaidConductor.cs must resolve, not fall through to UNRESOLVED: got [' + $raidConductorSurface + ']')
Check ($raidConductorSurface -and $raidConductorSurface -like '*vigil*') ('RaidConductor.cs mapping should name the vigil/raid-watch surface: got [' + $raidConductorSurface + ']')

# --- 3. Diff section fallback logic (A4's scope boundary: fall back LOUDLY) --------------------
$emptyDiff = Get-ScopeDiffSection -ChangedFiles @()
Check ($emptyDiff.FellBack -eq $true) 'empty changed-file list must FellBack=true'
Check ($emptyDiff.Text -like '*FELL BACK*') 'empty changed-file list must say FELL BACK in its text'

$allResolvedDiff = Get-ScopeDiffSection -ChangedFiles @('godot/scripts/panels/ForgePanel.cs', 'sim/GameSim/Heroes/Hero.cs')
Check ($allResolvedDiff.FellBack -eq $false) 'fully-mapped changed-file list must NOT fall back'
Check ($allResolvedDiff.UnresolvedCount -eq 0) 'fully-mapped changed-file list must have zero unresolved'
Check ($allResolvedDiff.Text -like '*WHAT CHANGED TODAY*') 'fully-mapped diff section must lead with the priority list'

$mixedDiff = Get-ScopeDiffSection -ChangedFiles @('godot/scripts/panels/ForgePanel.cs', 'godot/scripts/nosuchdir/Something.cs')
Check ($mixedDiff.FellBack -eq $true) 'partially-unmapped changed-file list must fall back'
Check ($mixedDiff.UnresolvedCount -eq 1) ('partially-unmapped list must count exactly 1 unresolved, got ' + $mixedDiff.UnresolvedCount)
Check ($mixedDiff.Text -like '*PARTIALLY FELL BACK*') 'partially-unmapped diff section must say so explicitly'
Check ($mixedDiff.Text -like '*UNRESOLVED*') 'partially-unmapped diff section must still list the unresolved path'

# Regression (2026-08-09): the exact 7-file diff of fix/sendoff-skips-the-day vs origin/main,
# reproduced directly with `git diff --name-only origin/main...origin/fix/sendoff-skips-the-day`.
# All 10 Diff-scope sweep runs against that branch reported "fell back" even though the branch
# genuinely differs from main -- this is the fixed scope map's real-world proof that it no longer
# does, now that MainUi.cs and RaidConductor.cs resolve.
$sendoffFiles = @(
    'godot/scripts/MainUi.cs',
    'godot/scripts/RaidConductor.cs',
    'godot/scripts/ui/TutorialFlow.cs',
    'godot/tests/DayAdvanceHudTests.cs',
    'godot/tests/PlayableLoopTests.cs',
    'godot/tests/RaidConductorTests.cs',
    'godot/tests/TutorialFlowTests.cs'
)
$sendoffDiff = Get-ScopeDiffSection -ChangedFiles $sendoffFiles
Check ($sendoffDiff.FellBack -eq $false) ('the sendoff-skips-the-day file list must no longer fall back, got FellBack=' + $sendoffDiff.FellBack + ' unresolved=' + ($sendoffDiff.Unresolved -join ', '))
Check ($sendoffDiff.UnresolvedCount -eq 0) ('the sendoff-skips-the-day file list must have zero unresolved, got ' + $sendoffDiff.UnresolvedCount)

# Get-ChangedFilesAgainstMain is real git, but read-only and needs no Godot/ollama/VRAM -- safe to
# exercise for real against this checkout. Only the shape is asserted (an array, never a crash),
# since the actual diff depends on the caller's branch state.
$realChanged = Get-ChangedFilesAgainstMain -RepoRoot $repoRoot
Check ($null -ne $realChanged) 'Get-ChangedFilesAgainstMain must never return $null (empty array on any failure)'

# A repo root that is not a git repo at all -- proves the loud-fallback path fires on a real error,
# not just on "nothing changed". NOTE: git prints a "not a git repository" / diff-usage message to
# the CONSOLE for this case (stderr, never redirected -- see agent-playtest.ps1's own note on why
# 2>&1 is banned here); that text below is expected noise, not a failure of this script.
$scratchDir = Join-Path $env:TEMP 'agent-playtest-scope-map-not-a-repo'
New-Item -ItemType Directory -Path $scratchDir -Force -ErrorAction SilentlyContinue | Out-Null
$notARepoChanged = Get-ChangedFilesAgainstMain -RepoRoot $scratchDir
Check (@($notARepoChanged).Count -eq 0) ('a non-repo RepoRoot must yield zero changed files, got ' + (@($notARepoChanged).Count))

# --- 4. Turn-prompt builder + the A5 follow-up (beat wiring) ------------------------------------
. (Join-Path $toolsDir 'agent-playtest\turn-prompt.ps1')

# Stubbed state standing in for state.json's StateDigest -- the "model" in this test is nothing
# more than string assertions on the text that WOULD have been sent to it. This is exactly the
# gap A5's follow-up names: AgentPlaytest.cs's digest has carried "beat" since A3, but nothing
# downstream ever read it. If this check regresses, the fix regressed with it.
$vigilState = [pscustomobject]@{
    day = 3
    phase = 'Camp'
    beat = 'VigilStop'
    location = 'town'
    canMove = $true
    gold = 120
    actionSlotsRemaining = 2
    lastOutcome = 'advanced -> day 3 Camp'
    screenText = @('Vigil: a party is waiting at the checkpoint')
    interactPrompt = ''
    controls = @([pscustomobject]@{ name = 'SendRunner'; label = 'Send the runner'; enabled = $true })
    nearby = @()
}
$vigilText = Build-ActUserText -State $vigilState -Turn 5 -Turns 40
Check ($vigilText -like '*beat VigilStop*') ('the beat field must reach the model: got [' + $vigilText.Substring(0, [Math]::Min(200, $vigilText.Length)) + ']')
Check ($vigilText -like '*Day 3*') 'day must still be present alongside beat'
Check ($vigilText -like '*SendRunner*') 'controls must still be listed'

# Nearby targets: in-range must say YOU ARE HERE (not a direction), out-of-range must give a
# direction and a distance -- preserved verbatim from the loop this was extracted from.
$nearbyState = [pscustomobject]@{
    day = 1; phase = 'Morning'; beat = 'None'; location = 'town'; canMove = $true; gold = 10
    actionSlotsRemaining = 5; lastOutcome = '(run start)'; screenText = @(); interactPrompt = ''
    controls = @()
    nearby = @(
        [pscustomobject]@{ key = 'forge'; label = 'Forge'; direction = 'here'; distance = 4; inRange = $true }
        [pscustomobject]@{ key = 'market'; label = 'Market'; direction = 'right'; distance = 220; inRange = $false }
    )
}
$nearbyText = Build-ActUserText -State $nearbyState -Turn 1 -Turns 40
Check ($nearbyText -like '*forge*YOU ARE HERE*') 'an in-range target must say YOU ARE HERE, not a walking direction'
Check ($nearbyText -like '*market*right*220px away*') 'an out-of-range target must give a direction and distance'

# W4 (docs/plans/2026-08-10-002, ruling 2): the OLD "Recent turns:" 6-line history window is GONE,
# not extended -- replaced outright by the model's own scratchpad ($NotesText). Mechanical proof the
# parameter itself no longer exists (not just that this test stopped calling it).
Check ((Get-Command Build-ActUserText).Parameters.Keys -notcontains 'RecentHistory') 'Build-ActUserText must no longer accept -RecentHistory at all -- the notes echo REPLACES it (the Pokemon lesson), never rides alongside it'

$noNotesText = Build-ActUserText -State $nearbyState -Turn 2 -Turns 40
Check ($noNotesText -notlike '*Recent turns:*') 'with no notes at all, the old "Recent turns:" heading must never appear -- it is gone, not just usually empty'
Check ($noNotesText -notlike '*Your notes so far:*') 'with no notes at all, the notes heading must not appear either (nothing to echo)'

$withNotesText = Build-ActUserText -State $nearbyState -Turn 2 -Turns 40 -NotesText 'turn 1: remember the smith is out of coal'
Check ($withNotesText -like '*Your notes so far:*') 'when notes are supplied, the "Your notes so far:" heading must appear'
Check ($withNotesText -like '*remember the smith is out of coal*') 'the actual note text must reach the model'

# Get-EchoedNotesText: the cap-with-marker mechanics (turn-prompt.ps1) -- short text passes through
# unchanged; text over the cap is trimmed from the FRONT (oldest dropped) with an explicit marker, and
# the untrimmed TAIL (the model's most recent thoughts) survives verbatim.
$shortNotes = 'short note, well under any cap'
Check ((Get-EchoedNotesText -FullNotesText $shortNotes -MaxChars 2000) -eq $shortNotes) 'notes under the cap must pass through completely unchanged'

$longNotesBuilder = New-Object System.Text.StringBuilder
for ($i = 1; $i -le 400; $i++) { [void]$longNotesBuilder.Append('note line ' + $i + [Environment]::NewLine) }
$longNotes = $longNotesBuilder.ToString()
Check ($longNotes.Length -gt 2000) 'sanity: the long-notes fixture must actually exceed the 2000-char cap used below'
$echoedLong = Get-EchoedNotesText -FullNotesText $longNotes -MaxChars 2000
Check ($echoedLong.Length -le 2000) ('the echoed text must respect the cap, got ' + $echoedLong.Length + ' chars')
Check ($echoedLong -like '(older notes trimmed)*') 'a trimmed echo must lead with the explicit "(older notes trimmed)" marker'
Check ($echoedLong -like '*note line 400*') 'the trimmed echo must keep the TAIL (the most recent note), never the head'
Check ($echoedLong -notlike '*note line 1' + [Environment]::NewLine + '*') 'the trimmed echo must have dropped the OLDEST note line, not the newest'

$emptyNotesEcho = Get-EchoedNotesText -FullNotesText '' -MaxChars 2000
Check ($emptyNotesEcho -eq '') 'empty notes text must echo as an empty string, never throw or add a marker'

# --- 5. Completion floor (A6) -- "a run that dies early reports itself healthy" -----------------
. (Join-Path $toolsDir 'agent-playtest\completion.ps1')

# The four real sweep runs that exposed the defect: all stopped early, all with a clean (0%)
# fallback ratio, so DEGRADED alone missed every one of them. All four must be INCOMPLETE under
# the 50% floor agent-playtest.ps1 chooses.
$scout5 = Get-CompletionVerdict -Turn 1 -Turns 80
Check ($scout5.Incomplete -eq $true) ('Scout-5 (1 of 80 turns) must be INCOMPLETE, got ratio ' + $scout5.Ratio)
$scout10 = Get-CompletionVerdict -Turn 9 -Turns 80
Check ($scout10.Incomplete -eq $true) ('Scout-10 (9 of 80 turns) must be INCOMPLETE, got ratio ' + $scout10.Ratio)
$full1 = Get-CompletionVerdict -Turn 25 -Turns 80
Check ($full1.Incomplete -eq $true) ('Full-1 (25 of 80 turns) must be INCOMPLETE, got ratio ' + $full1.Ratio)
$full5 = Get-CompletionVerdict -Turn 36 -Turns 80
Check ($full5.Incomplete -eq $true) ('Full-5 (36 of 80 turns, the closest surviving case at 45%) must be INCOMPLETE, got ratio ' + $full5.Ratio)

# A run that used its whole budget, or fell only just short of the floor, must NOT be flagged.
$fullBudget = Get-CompletionVerdict -Turn 80 -Turns 80
Check ($fullBudget.Incomplete -eq $false) 'a run that used its entire turn budget must not be INCOMPLETE'
$mostlyThere = Get-CompletionVerdict -Turn 41 -Turns 80
Check ($mostlyThere.Incomplete -eq $false) ('41 of 80 turns (51%) is just over the floor and must not be INCOMPLETE, got ratio ' + $mostlyThere.Ratio)
$exactlyAtFloor = Get-CompletionVerdict -Turn 40 -Turns 80
Check ($exactlyAtFloor.Incomplete -eq $false) ('40 of 80 turns is EXACTLY the 50% floor -- the check is strictly-less-than, so meeting the floor exactly must NOT be INCOMPLETE, got ratio ' + $exactlyAtFloor.Ratio)
$justUnderFloor = Get-CompletionVerdict -Turn 39 -Turns 80
Check ($justUnderFloor.Incomplete -eq $true) ('39 of 80 turns is just UNDER the 50% floor and must be INCOMPLETE, got ratio ' + $justUnderFloor.Ratio)

# Scripted mode is exempt by design -- it is a fixed ~5-command channel proof, not a play session,
# and always stops around turn 5 regardless of -Turns (see agent-playtest.ps1's own .PARAMETER
# Scripted doc). Without the exemption every scripted run would wrongly read as INCOMPLETE.
$scriptedRun = Get-CompletionVerdict -Turn 5 -Turns 40 -Scripted
Check ($scriptedRun.Incomplete -eq $false) ('a Scripted run stopping at its fixed 5-command plan must never be INCOMPLETE, got ratio ' + $scriptedRun.Ratio)

# Turns=0 is a degenerate caller input (never happens through the real param default of 40, but
# must not divide by zero or crash): defined as complete (ratio 1.0), nothing to fall short of.
$zeroTurns = Get-CompletionVerdict -Turn 0 -Turns 0
Check ($zeroTurns.Ratio -eq 1.0) ('Turns=0 must not divide by zero, got ratio ' + $zeroTurns.Ratio)
Check ($zeroTurns.Incomplete -eq $false) 'Turns=0 must not be flagged INCOMPLETE'

# --- 6. Frame archiving (U1) -- "every frame the model saw is kept" -----------------------------
. (Join-Path $toolsDir 'agent-playtest\frames.ps1')

# Default -FrameEvery 1 keeps every turn: N frames for N turns.
for ($n = 1; $n -le 8; $n++) {
    $kept = 0
    for ($t = 1; $t -le $n; $t++) { if (Test-ShouldKeepFrame -Turn $t -FrameEvery 1) { $kept++ } }
    Check ($kept -eq $n) ('FrameEvery=1 must keep all ' + $n + ' turn(s), kept ' + $kept)
}

# -FrameEvery 5 keeps ceil(N/5), not floor(N/5) -- turn 1 is always kept, so the LAST partial group
# of fewer than 5 turns still gets one, matching "N frames for N turns and ceil(N/5) for -FrameEvery
# 5" from the brief. Checked across enough N to catch an off-by-one at every remainder (0..4).
foreach ($n in @(1, 4, 5, 6, 9, 10, 11, 12)) {
    $kept = 0
    for ($t = 1; $t -le $n; $t++) { if (Test-ShouldKeepFrame -Turn $t -FrameEvery 5) { $kept++ } }
    $expected = [math]::Ceiling($n / 5.0)
    Check ($kept -eq $expected) ('FrameEvery=5 with ' + $n + ' turns must keep ceil(' + $n + '/5)=' + $expected + ', kept ' + $kept)
}
Check ((Test-ShouldKeepFrame -Turn 1 -FrameEvery 5) -eq $true) 'turn 1 must always be kept regardless of -FrameEvery'

# Save-TurnFrame: a genuinely missing source (the exact "imageless turn" shape PR #420 fixed
# elsewhere) must say so explicitly, never silently skip with no trace.
$frameScratch = Join-Path $env:TEMP 'agent-playtest-frames-scratch'
if (Test-Path $frameScratch) { Remove-Item $frameScratch -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $frameScratch -Force | Out-Null
$fakeFramesDir = Join-Path $frameScratch 'frames'
$missingSourcePath = Join-Path $frameScratch 'does-not-exist.png'
$missingResult = Save-TurnFrame -SourcePath $missingSourcePath -FramesDir $fakeFramesDir -Turn 3 -FrameEvery 1
Check ($missingResult.Missing -eq $true) 'a nonexistent source frame must be reported Missing=true'
Check ($missingResult.Kept -eq $false) 'a missing frame must never be reported Kept=true'
Check ($missingResult.Note -like '*frame missing*') ('the missing-frame note must say "frame missing" verbatim, got [' + $missingResult.Note + ']')

# A real source that IS kept: gets copied to frames/turn-NNN.png.
$realSourcePath = Join-Path $frameScratch 'frame.png'
Set-Content -Path $realSourcePath -Value 'not a real png, just bytes for the test' -Encoding utf8
$keptResult = Save-TurnFrame -SourcePath $realSourcePath -FramesDir $fakeFramesDir -Turn 1 -FrameEvery 1
Check ($keptResult.Kept -eq $true) 'a real source on a kept turn must report Kept=true'
Check ($keptResult.FileName -eq 'turn-001.png') ('kept-frame filename must be zero-padded to 3 digits, got [' + $keptResult.FileName + ']')
Check (Test-Path (Join-Path $fakeFramesDir 'turn-001.png')) 'Save-TurnFrame must actually copy the file to FramesDir'

# A real source on a THINNED turn (FrameEvery=5, turn 2): not kept, not missing either -- a third,
# distinct outcome from "kept" and "missing".
$thinnedResult = Save-TurnFrame -SourcePath $realSourcePath -FramesDir $fakeFramesDir -Turn 2 -FrameEvery 5
Check ($thinnedResult.Kept -eq $false) 'a thinned-away turn must not be Kept'
Check ($thinnedResult.Missing -eq $false) 'a thinned-away turn is not the same thing as a missing frame'
Remove-Item $frameScratch -Recurse -Force -ErrorAction SilentlyContinue

# Add-FrameReferencesToTurnLog: a turn WITH a "- frame:" line gets its note right after that line; a
# turn with NO such line (the command-timeout branch's own header) gets the note appended at the end
# of its own block instead -- both must reference the exact filename, never silently have no line.
$sampleTurnLog = (@(
    '# Agent playtest turn log',
    '',
    '## Turn 1',
    '- day 1 phase Morning beat None location town gold 100 canMove True slots 5',
    '- screen: Welcome',
    '- frame: captured (non-blank)',
    '- command: action=advance target= dir= frames= why=test',
    '- outcome: advanced',
    '## Turn 2',
    '- day 1 phase Morning beat None location town gold 100 canMove True slots 5',
    '- screen: Welcome',
    '- command: (none) -> timed out after 30000ms waiting for command.json'
) -join [Environment]::NewLine)
$frameNotesForLog = @{
    1 = 'frame: frames/turn-001.png'
    2 = 'frame missing at turn 2 -- no frame.png was available to keep'
}
$annotatedLog = Add-FrameReferencesToTurnLog -TurnLogText $sampleTurnLog -FrameNoteByTurn $frameNotesForLog
$idxFrameLine = $annotatedLog.IndexOf('- frame: captured (non-blank)')
$idxTurn1Note = $annotatedLog.IndexOf('- frame: frames/turn-001.png')
$idxTurn2Header = $annotatedLog.IndexOf('## Turn 2')
$idxTurn2Note = $annotatedLog.IndexOf('- frame missing at turn 2')
Check ($idxFrameLine -ge 0 -and $idxTurn1Note -gt $idxFrameLine -and $idxTurn1Note -lt $idxTurn2Header) 'turn 1''s frame note must land immediately after its own "- frame:" line, before turn 2 starts'
Check ($idxTurn2Note -gt $idxTurn2Header) 'turn 2 (no "- frame:" line at all) must still get its frame note appended to its own block'
Check ($annotatedLog -notlike '*{{PERSONA}}*') 'sanity: the turn-log fixture text itself must not contain a stray template marker'

# --- 7. Backend record (U2) -- "the backend log becomes evidence" -------------------------------
. (Join-Path $toolsDir 'agent-playtest\backend.ps1')

$noBackendLog = Get-BackendSummary -LogPath (Join-Path $env:TEMP 'agent-playtest-no-such-log.jsonl')
Check ($noBackendLog.Available -eq $false) 'a missing backend log must report Available=false, not a silent clean run'
Check ($noBackendLog.Message -like '*no backend log*') ('the absent-log message must say so explicitly, got [' + $noBackendLog.Message + ']')

$backendFixturePath = Join-Path $toolsDir 'agent-playtest\tests\backend-fixture.jsonl'
Check (Test-Path $backendFixturePath) ('backend fixture must exist at ' + $backendFixturePath)
$backendSummary = Get-BackendSummary -LogPath $backendFixturePath

# Exact counts, hand-computed from the fixture's own known mix (see the fixture file's own layout):
# 12 valid rows (1 session + 6 tick + 4 note + 1 action), 1 malformed line ("not json at all"), one
# blank line (must be skipped silently, not counted as malformed).
Check ($backendSummary.Available -eq $true) 'the fixture must parse as Available=true'
Check ($backendSummary.RowCount -eq 12) ('fixture RowCount must be 12, got ' + $backendSummary.RowCount)
Check ($backendSummary.MalformedLineCount -eq 1) ('fixture must report exactly 1 malformed line, got ' + $backendSummary.MalformedLineCount)

# 6 tick rows: A,B (no transition), C (press:AdvancePhase), D (auto:conductor-beat-elapsed),
# E (auto:innkeepers-clock, also completes Evening -> 1 autosave), F (real transition, empty cause).
Check ($backendSummary.Advances.Count -eq 4) ('fixture must show 4 real phase advances (C,D,E,F), got ' + $backendSummary.Advances.Count)
Check ($backendSummary.AutoAdvanceCount -eq 2) ('fixture must show 2 auto: advances (D,E), got ' + $backendSummary.AutoAdvanceCount)
Check ($backendSummary.PressAdvanceCount -eq 1) ('fixture must show 1 press: advance (C), got ' + $backendSummary.PressAdvanceCount)
Check ($backendSummary.UnattributedAdvanceCount -eq 1) ('fixture must show 1 unattributed advance (F, empty cause), got ' + $backendSummary.UnattributedAdvanceCount)

# Exactly 1 rejection: tick B introduces it, tick C's empty rejects[] resets the accumulator (a real
# advance clearing SimAdapter.LastRejections) so it must NOT be double-counted on every later row.
Check ($backendSummary.Rejections.Count -eq 1) ('fixture must show exactly 1 deduplicated rejection, got ' + $backendSummary.Rejections.Count)
if ($backendSummary.Rejections.Count -eq 1) {
    Check ($backendSummary.Rejections[0].Why -eq 'insufficient gold') ('the one rejection''s reason must be "insufficient gold", got [' + $backendSummary.Rejections[0].Why + ']')
}
Check ($backendSummary.RejectionCountsByReason.Count -eq 1) ('fixture must group to exactly 1 reason, got ' + $backendSummary.RejectionCountsByReason.Count)

# events: 0+0+2+3+0+1 = 6 across the six tick rows.
Check ($backendSummary.EventsTotalAcrossTicks -eq 6) ('fixture events total must be 6, got ' + $backendSummary.EventsTotalAcrossTicks)

# Attribution: the ONE "gossip: ..." note row matches the keyword scan. This fixture's tick rows all
# predate the eventTypes field (no row carries the key at all), so AttributionEventTypeHits must be
# zero and the caveat must say the OLD-FORMAT thing honestly -- "no eventTypes key", not "0 hits" (0
# hits on a NEW-format log would mean something different: see the eventtypes-fixture block below).
Check ($backendSummary.AttributionNoteHits.Count -eq 1) ('fixture must find exactly 1 attribution-shaped note, got ' + $backendSummary.AttributionNoteHits.Count)
Check (@($backendSummary.AttributionEventTypeHits).Count -eq 0) ('fixture predates eventTypes -- AttributionEventTypeHits must be 0, got ' + @($backendSummary.AttributionEventTypeHits).Count)
Check ($backendSummary.AttributionCaveat -like '*no "eventTypes" key at all*') 'the attribution caveat on an old-format (no eventTypes) fixture must say so explicitly, not silently read like a checked-and-clean zero'

# Narrator: one voiced ("VOICE: spoke ..."), one text-only ("VOICE: text-only (no audio) ...").
Check ($backendSummary.NarratorVoicedCount -eq 1) ('fixture must show 1 voiced narrator line, got ' + $backendSummary.NarratorVoicedCount)
Check ($backendSummary.NarratorTextOnlyCount -eq 1) ('fixture must show 1 text-only narrator line, got ' + $backendSummary.NarratorTextOnlyCount)

# Autosave: derived from fromPhase=="Evening" -- tick E is the only one (Evening -> Morning).
Check ($backendSummary.AutosaveWriteCount -eq 1) ('fixture must derive exactly 1 autosave write, got ' + $backendSummary.AutosaveWriteCount)

# Contradiction (b): auto-advances (D, E) plus the one unattributed advance (F) = 3 lines.
$autoContradictions = Get-AutoAdvanceContradictions -Summary $backendSummary
Check ($autoContradictions.Count -eq 3) ('fixture must produce exactly 3 auto-advance/unattributed contradiction lines, got ' + $autoContradictions.Count)

# Contradiction (a): the driver's own turn log shows a refusal in day 1 Morning -> no mismatch: the
# UI and the kernel AGREE. Remove that refusal and the same bucket must now flag a mismatch, proving
# the check actually discriminates rather than always firing or never firing.
$driverTurnsAgree = @(
    [pscustomobject]@{ Day = 1; Phase = 'Morning'; Accepted = $true }
    [pscustomobject]@{ Day = 1; Phase = 'Morning'; Accepted = $false }
)
$mismatchesAgree = Get-DriverBackendMismatches -Summary $backendSummary -DriverTurns $driverTurnsAgree
Check ($mismatchesAgree.Count -eq 0) ('when the driver ALSO saw a refusal in day 1 Morning, there must be no mismatch, got ' + $mismatchesAgree.Count)

$driverTurnsDisagree = @(
    [pscustomobject]@{ Day = 1; Phase = 'Morning'; Accepted = $true }
)
$mismatchesDisagree = Get-DriverBackendMismatches -Summary $backendSummary -DriverTurns $driverTurnsDisagree
Check ($mismatchesDisagree.Count -eq 1) ('when the driver saw ONLY acceptances in day 1 Morning but the backend logged a rejection there, exactly 1 mismatch line must fire, got ' + $mismatchesDisagree.Count)
Check ($mismatchesDisagree[0] -like '*day 1 Morning*') ('the mismatch line must name the day/phase bucket, got [' + $mismatchesDisagree[0] + ']')

# Format-BackendMarkdown must actually render (smoke check -- the real content is asserted above).
$backendMarkdownText = Format-BackendMarkdown -Summary $backendSummary -Contradictions $autoContradictions
Check ($backendMarkdownText -like '*Backend record*') 'Format-BackendMarkdown must produce a "Backend record" heading'
Check ($backendMarkdownText -like '*insufficient gold*') 'Format-BackendMarkdown must surface the rejection reason'

# --- 7a. eventTypes corroboration (2026-08-11, backend-log-sees-the-spine): PlaytestLog.Tick now
# carries the DISTINCT event type names that fired each tick, closing the gap the fixture above (an
# OLD-format log) still cannot close. This fixture's middle tick row carries
# eventTypes:["ItemSold","AttributionBeatEvent"] -- proving the log can now directly name the one
# event link 4/5's sweep actually needs, not just a best-effort text guess.
$eventTypesFixturePath = Join-Path $toolsDir 'agent-playtest\tests\backend-eventtypes-fixture.jsonl'
Check (Test-Path $eventTypesFixturePath) ('eventTypes fixture must exist at ' + $eventTypesFixturePath)
$eventTypesSummary = Get-BackendSummary -LogPath $eventTypesFixturePath
Check ($eventTypesSummary.Available -eq $true) 'the eventTypes fixture must parse as Available=true'
Check (@($eventTypesSummary.AttributionEventTypeHits).Count -eq 1) ('fixture must find exactly 1 AttributionBeatEvent-named tick row, got ' + @($eventTypesSummary.AttributionEventTypeHits).Count)
Check ($eventTypesSummary.AttributionEventTypeHits[0].Day -eq 1) ('the one eventTypes hit must be day 1 (the Evening tick), got ' + $eventTypesSummary.AttributionEventTypeHits[0].Day)
Check ($eventTypesSummary.AttributionCaveat -like '*can directly prove an AttributionBeatEvent fired*') 'a NEW-format log (any row carrying eventTypes) must say the caveat now CAN prove it, not still cannot'
Check ($eventTypesSummary.AttributionCaveat -notlike '*no "eventTypes" key at all*') 'a NEW-format log must not be reported as if it predates the field'
$eventTypesMarkdown = Format-BackendMarkdown -Summary $eventTypesSummary -Contradictions @()
Check ($eventTypesMarkdown -like '*AttributionBeatEvent named directly*') 'Format-BackendMarkdown must surface the eventTypes hit as a directly-named beat, not just the note-scan section'

# Regression (found live, W1, docs/plans/2026-08-10-002, while proving the honesty footer end to end):
# Get-BackendRejections' -TickRows and Get-BackendRejectionCountsByReason's -Rejections were BOTH
# [Parameter(Mandatory)][array] with no AllowEmptyCollection -- PowerShell's own binder throws "Cannot
# bind argument ... because it is an empty collection" the moment either receives a real, legal @().
# A CLEAN run (zero backend-level rejections across the whole log -- the ordinary case for a short
# run that never touched an action the kernel would refuse) hits exactly this, and hit it BEFORE
# findings.md could be written at all: not a wrong report, no report. Fixed with AllowEmptyCollection
# on both parameters; this fixture (zero "rejects" on every tick row) is the proof it stays fixed.
$zeroRejectionsFixturePath = Join-Path $toolsDir 'agent-playtest\tests\zero-rejections-fixture.jsonl'
Check (Test-Path $zeroRejectionsFixturePath) ('zero-rejections fixture must exist at ' + $zeroRejectionsFixturePath)
$zeroRejectionsThrew = $false
$zeroRejectionsSummary = $null
try { $zeroRejectionsSummary = Get-BackendSummary -LogPath $zeroRejectionsFixturePath } catch { $zeroRejectionsThrew = $true }
Check ($zeroRejectionsThrew -eq $false) 'Get-BackendSummary must NOT throw on a real log whose every tick has zero rejects'
if ($zeroRejectionsSummary) {
    Check ($zeroRejectionsSummary.Available -eq $true) 'a zero-rejection log must still parse as Available=true'
    Check (@($zeroRejectionsSummary.Rejections).Count -eq 0) 'a zero-rejection log must report exactly zero rejections, not crash trying to'
    Check (@($zeroRejectionsSummary.RejectionCountsByReason).Count -eq 0) 'a zero-rejection log must report an empty (not crashed) RejectionCountsByReason'
}

# --- 8. Coverage census (U3) -- "everything gets a denominator" ---------------------------------
. (Join-Path $toolsDir 'agent-playtest\coverage.ps1')

# A small, fully synthetic registry -- deliberately NOT the real repo's (which can only grow over
# time and would make "the exact untouched complement" a moving target). This proves the
# touch-tracking and report math in isolation from how big the real game happens to be today.
$fakeRegistries = [pscustomobject]@{
    Panel           = @('Forge', 'Shop')
    TownBuilding    = @('forge', 'market', 'tavern')
    InteriorStation = @('forge/anvil', 'forge/furnace')
    DayPhase        = @('Morning', 'Evening')
    ActionType      = @('press', 'advance')
    HudControl      = @('AdvancePhase')
    Caveats         = @('synthetic test registry -- not the real game')
}
$fakeTracker = New-CoverageTracker

# Stub turn history: turn 1 stands at the forge's door outdoors and advances; turn 2 opens the Forge
# panel and presses AdvancePhase. This is deliberately a SMALL, hand-traceable script so the expected
# touched/untouched split can be verified by inspection, not by trusting the code under test.
$turn1State = [pscustomobject]@{
    location = 'town'
    phase    = 'Morning'
    nearby   = @([pscustomobject]@{ key = 'forge'; inRange = $true })
}
$turn1Command = [pscustomobject]@{ action = 'advance' }
Add-CoverageTouch -Tracker $fakeTracker -State $turn1State -Command $turn1Command

$turn2State = [pscustomobject]@{
    location = 'panel:Forge'
    phase    = 'Morning'
    nearby   = @()
}
$turn2Command = [pscustomobject]@{ action = 'press'; target = 'AdvancePhase' }
Add-CoverageTouch -Tracker $fakeTracker -State $turn2State -Command $turn2Command

$fakeReport = Get-CoverageReport -Registries $fakeRegistries -Tracker $fakeTracker
$byCat = @{}
foreach ($c in $fakeReport.Categories) { $byCat[$c.Category] = $c }

Check (($byCat['Panel'].Touched -join ',') -eq 'Forge') ('Panel touched must be exactly [Forge], got [' + ($byCat['Panel'].Touched -join ',') + ']')
Check (($byCat['Panel'].Untouched -join ',') -eq 'Shop') ('Panel untouched must be exactly [Shop], got [' + ($byCat['Panel'].Untouched -join ',') + ']')

Check (($byCat['TownBuilding'].Touched -join ',') -eq 'forge') ('TownBuilding touched must be exactly [forge], got [' + ($byCat['TownBuilding'].Touched -join ',') + ']')
Check (($byCat['TownBuilding'].Untouched -join ',') -eq 'market,tavern') ('TownBuilding untouched must be exactly [market,tavern], got [' + ($byCat['TownBuilding'].Untouched -join ',') + ']')

Check ($byCat['InteriorStation'].Touched.Count -eq 0) 'InteriorStation touched must be empty -- neither stub turn ever entered an interior'
Check (($byCat['InteriorStation'].Untouched -join ',') -eq 'forge/anvil,forge/furnace') ('InteriorStation untouched must list BOTH stations in full, got [' + ($byCat['InteriorStation'].Untouched -join ',') + ']')

Check (($byCat['DayPhase'].Touched -join ',') -eq 'Morning') ('DayPhase touched must be exactly [Morning], got [' + ($byCat['DayPhase'].Touched -join ',') + ']')
Check (($byCat['DayPhase'].Untouched -join ',') -eq 'Evening') ('DayPhase untouched must be exactly [Evening], got [' + ($byCat['DayPhase'].Untouched -join ',') + ']')

Check ($byCat['ActionType'].Untouched.Count -eq 0) ('ActionType must show full coverage (both press and advance used), untouched was [' + ($byCat['ActionType'].Untouched -join ',') + ']')
Check ($byCat['HudControl'].Untouched.Count -eq 0) ('HudControl must show full coverage (AdvancePhase was pressed), untouched was [' + ($byCat['HudControl'].Untouched -join ',') + ']')

Check ($fakeReport.OverallTouched -eq 6) ('overall touched must be 6 (1+1+0+1+2+1), got ' + $fakeReport.OverallTouched)
Check ($fakeReport.OverallTotal -eq 12) ('overall total must be 12 (2+3+2+2+2+1), got ' + $fakeReport.OverallTotal)
Check ($fakeReport.OverallPercentage -eq 50.0) ('overall percentage must be 50.0, got ' + $fakeReport.OverallPercentage)

$fakeCoverageMarkdown = Format-CoverageMarkdown -Report $fakeReport
Check ($fakeCoverageMarkdown -like '*market*') 'Format-CoverageMarkdown must print the untouched list in full (market)'
Check ($fakeCoverageMarkdown -like '*tavern*') 'Format-CoverageMarkdown must print the untouched list in full (tavern)'
Check ($fakeCoverageMarkdown -like '*forge/anvil*') 'Format-CoverageMarkdown must print untouched interior stations by their venue/id key'

# Real-repo registries: not exact-count-asserted (the repo grows), but proven non-empty and spot
# checked against known-stable facts derived from source read earlier while building this file --
# ActionType is a closed 5-verb switch (press/move/key/advance/stop) that will not silently grow.
$realRegistries = Get-CoverageRegistries -RepoRoot $repoRoot
Check (($realRegistries.ActionType | Sort-Object) -join ',' -eq 'advance,key,move,press,stop') ('real ActionType registry must be exactly the 5 bridge verbs, got [' + ($realRegistries.ActionType -join ',') + ']')
Check ($realRegistries.Panel -contains 'Forge') 'real Panel registry must contain Forge (MainUi.cs Drawer.Register)'
Check ($realRegistries.DayPhase -contains 'Morning') 'real DayPhase registry must contain Morning'
Check ($realRegistries.TownBuilding.Count -ge 5) ('real TownBuilding registry must have at least the 5 known outdoor venues, got ' + $realRegistries.TownBuilding.Count)
Check ($realRegistries.InteriorStation.Count -gt 0) 'real InteriorStation registry must be non-empty'
Check ($realRegistries.Caveats.Count -ge 2) ('real registries must carry at least the HUD-control and forge-profession-gating caveats, got ' + $realRegistries.Caveats.Count)

# --- 9. Personas (U4, sceptic retired W3) -- "several players, not one player played N times" ---
. (Join-Path $toolsDir 'agent-playtest\personas.ps1')

$personasDir = Join-Path $toolsDir 'agent-playtest\prompts\personas'
$actMdPath = Join-Path $toolsDir 'agent-playtest\prompts\act.md'
$actProtocolText = Get-Content $actMdPath -Raw

# W4 (docs/plans/2026-08-10-002): monkey and attached join the original four -- "Resolve-PersonaChoice
# accepts monkey+attached (roster = 6)" is the brief's own required proof, and this loop is where it
# lands: Resolve-PersonaChoice only ever checks membership, so it needs no .md file on disk to resolve
# a name (monkey never has one at all -- see personas.ps1's own header). S2 (scripted-deep-pilot lane)
# adds pilot the same way (roster = 7) -- also no .md file, same reason.
foreach ($p in @('first-timer', 'veteran', 'speedrunner', 'completionist', 'monkey', 'attached', 'pilot')) {
    $resolved = Resolve-PersonaChoice -Persona $p
    Check ($resolved -eq $p) ('a known persona name must resolve to itself, got [' + $resolved + '] for [' + $p + ']')
}

# "random" resolves via the injectable scriptblock (overridable so this is deterministic) to one of
# the seven known names -- never an eighth value, never the literal string "random" itself.
$randomResolved = Resolve-PersonaChoice -Persona 'random' -Random { param($items) $items[2] }
Check (@('first-timer', 'veteran', 'speedrunner', 'completionist', 'monkey', 'attached', 'pilot') -contains $randomResolved) ('"random" must resolve to one of the seven known personas, got [' + $randomResolved + ']')

# An unknown persona name must FAIL LOUDLY -- never silently become the default. This is the exact
# silent-fallback defect shape this repo has already fixed twice (A1, A6); a third instance here
# would undo the whole point of U4.
$unknownPersonaThrew = $false
$unknownPersonaMessage = ''
try {
    Resolve-PersonaChoice -Persona 'definitely-not-a-real-persona' | Out-Null
} catch {
    $unknownPersonaThrew = $true
    $unknownPersonaMessage = $_.Exception.Message
}
Check ($unknownPersonaThrew -eq $true) 'an unrecognized -Persona value must throw, not silently resolve to a default'
Check ($unknownPersonaMessage -like '*unknown persona*') ('the thrown message must say "unknown persona", got [' + $unknownPersonaMessage + ']')

# W3 (docs/plans/2026-08-10-002, ruling 6): sceptic is RETIRED -- the dead-verb detector supersedes
# it. Resolve-PersonaChoice must reject the old name LOUDLY, the same as any other unknown persona,
# never silently accept it or silently fall back to a default now that its file is gone.
$scepticThrew = $false
$scepticMessage = ''
try {
    Resolve-PersonaChoice -Persona 'sceptic' | Out-Null
} catch {
    $scepticThrew = $true
    $scepticMessage = $_.Exception.Message
}
Check ($scepticThrew -eq $true) 'sceptic must be rejected -- it is retired (ruling 6), not a valid persona any more'
Check ($scepticMessage -like '*unknown persona*') ('the retired-sceptic rejection message must say "unknown persona", got [' + $scepticMessage + ']')
Check (-not (Test-Path (Join-Path $personasDir 'sceptic.md'))) 'prompts/personas/sceptic.md must be deleted, not merely unregistered'

# Two different personas must produce two different assembled prompts and two different hashes --
# "so two runs claiming to be different players can be checked" (the brief's own acceptance test).
$veteranPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'veteran' -PersonasDir $personasDir
$firstTimerPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'first-timer' -PersonasDir $personasDir
$speedrunnerPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'speedrunner' -PersonasDir $personasDir
$completionistPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'completionist' -PersonasDir $personasDir

Check ($veteranPrompt -ne $completionistPrompt) 'veteran and completionist must assemble to DIFFERENT prompt text'
$veteranHash = Get-PromptHash -Text $veteranPrompt
$completionistHash = Get-PromptHash -Text $completionistPrompt
$firstTimerHash = Get-PromptHash -Text $firstTimerPrompt
Check ($veteranHash -ne $completionistHash) ('veteran and completionist must hash differently, both got [' + $veteranHash + ']')
Check ($veteranHash -ne $firstTimerHash) 'veteran and first-timer must hash differently'
Check ($veteranHash.Length -eq 12) ('the prompt hash must be 12 hex chars, got [' + $veteranHash + '] (' + $veteranHash.Length + ' chars)')

# Same text must hash the SAME way twice (a hash that is not stable would be useless for comparing
# two runs' findings.md headers against each other).
Check ((Get-PromptHash -Text $veteranPrompt) -eq (Get-PromptHash -Text $veteranPrompt)) 'the same prompt text must hash identically on repeat calls'

# The assembled prompt must have substituted the marker away entirely, and must carry the persona's
# own content through.
Check ($veteranPrompt -notlike '*{{PERSONA}}*') 'the assembled prompt must not still contain the {{PERSONA}} marker'
Check ($veteranPrompt -like '*six decisions*') 'the assembled veteran prompt must carry the veteran persona''s own content'

# A missing marker in act.md itself must fail loudly, not silently ship a protocol-only prompt with
# no persona attached.
$noMarkerThrew = $false
try {
    Build-PersonaActPrompt -ActProtocolText 'no marker in this text at all' -PersonaName 'veteran' -PersonasDir $personasDir | Out-Null
} catch { $noMarkerThrew = $true }
Check ($noMarkerThrew -eq $true) 'act.md text with no {{PERSONA}} marker must throw, not silently return protocol-only text'

# A missing persona FILE must also fail loudly.
$noFileThrew = $false
try {
    Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'nonexistent-persona' -PersonasDir $personasDir | Out-Null
} catch { $noFileThrew = $true }
Check ($noFileThrew -eq $true) 'a persona name with no matching .md file must throw'

# --- 10. Noun-purity guard (mid-flight design-review correction) --------------------------------
# act.md is shared PROTOCOL every persona reads, including first-timer -- the ONE persona whose
# entire value is knowing NOTHING the game has not shown it yet. act.md's first draft still taught
# the game in its opening line and named VigilStop by name in a protocol rule; a first-timer layered
# on top of THAT is not a first-timer, it is the builder wearing a name tag. This section is the
# mechanical guard against that regressing silently a second time.
$gameNounDenylist = Get-GameNounDenylist -RepoRoot $repoRoot
Check ($gameNounDenylist.Count -ge 10) ('the glossary-derived denylist must have a healthy number of terms (THE-GAME.md ' +
    'section 8 has 15 rows, one splits into 2), got ' + $gameNounDenylist.Count + ': ' + ($gameNounDenylist -join ', '))
# Spot-check a few terms by name so a parser regression that silently returns the WRONG terms (not
# just zero terms) is still caught.
foreach ($expectedTerm in @('Vigil', 'Bounty', 'Commission', 'Heirloom', 'mark')) {
    Check ($gameNounDenylist -contains $expectedTerm) ('the glossary-derived denylist must contain "' + $expectedTerm + '", got [' + ($gameNounDenylist -join ', ') + ']')
}

# THE actual guard: act.md's raw protocol text, on its own, must be clean.
$actProtocolHits = Test-TextForGameNouns -Text $actProtocolText -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($actProtocolHits.Count -eq 0) ('act.md (protocol only, before persona substitution) must teach ZERO game nouns, found: ' + ($actProtocolHits -join ', '))

# THE brief's own required check: the FULLY ASSEMBLED first-timer prompt (protocol + persona, exactly
# what gets sent to the model) must ALSO be zero-overlap -- a clean act.md is not enough if the one
# persona meant to know nothing is reunited with a leak of its own.
$firstTimerHits = Test-TextForGameNouns -Text $firstTimerPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($firstTimerHits.Count -eq 0) ('the assembled first-timer prompt must teach ZERO game nouns, found: ' + ($firstTimerHits -join ', '))

# Contrast case, so this test is PROVEN to discriminate rather than always reading zero by accident:
# veteran/completionist are SUPPOSED to know the vigil by name (rule 7's relocated content; sceptic
# used to be the third, retired W3 -- see the "sceptic must be rejected" check above).
$veteranHits = Test-TextForGameNouns -Text $veteranPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($veteranHits.Count -gt 0) 'veteran SHOULD legitimately trip the denylist (it is told about VigilStop on purpose) -- zero hits here would mean this test is vacuous'
Check ($veteranHits -contains 'Vigil') ('veteran''s hits should specifically include "Vigil", got [' + ($veteranHits -join ', ') + ']')
$completionistHits = Test-TextForGameNouns -Text $completionistPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($completionistHits -contains 'Vigil') 'completionist must also carry vigil-specific knowledge per the correction'

# speedrunner is the OTHER blind-to-the-vigil persona (deliberately, per the correction: mashing
# through the vigil blind is the only honest test that skipping stays legal) -- it MAY legitimately
# know other game nouns (bounty, etc, already part of its own goal text) but must NOT know "Vigil"
# specifically.
$speedrunnerHits = Test-TextForGameNouns -Text $speedrunnerPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($speedrunnerHits -notcontains 'Vigil') ('speedrunner must NOT be told about the vigil by name (that is the skip-legality probe), found: ' + ($speedrunnerHits -join ', '))

# Test-TextForGameNouns itself: an allowlist entry must actually exempt a term (mechanism check,
# independent of whether act.md currently needs one).
$allowlistDemoHits = Test-TextForGameNouns -Text 'this sentence uses the word mark on purpose' -Denylist @('mark') -Allowlist @('mark')
Check ($allowlistDemoHits.Count -eq 0) 'an allowlisted term must be exempted from the denylist scan'
$noAllowlistDemoHits = Test-TextForGameNouns -Text 'this sentence uses the word mark on purpose' -Denylist @('mark') -Allowlist @()
Check ($noAllowlistDemoHits.Count -eq 1) 'the same term WITHOUT an allowlist entry must be caught (proves the allowlist, not the pattern, is what exempted it above)'

# --- 11. Model request/reply mechanics (W1, docs/plans/2026-08-10-002 "the playtest becomes a
# player") -- request body carries the schema, the schema file itself is well-formed, and the
# reply-legality check that replaced the old NORMALIZE block/regex-extract still drives the honesty
# counters on a semantic refusal. -----------------------------------------------------------------
. (Join-Path $toolsDir 'agent-playtest\model-call.ps1')
. (Join-Path $toolsDir 'agent-playtest\footer.ps1')

$actionSchemaPath = Join-Path $toolsDir 'agent-playtest\prompts\action-schema.json'
Check (Test-Path $actionSchemaPath) ('action-schema.json must exist at ' + $actionSchemaPath)
$actionSchemaRaw = (Get-Content $actionSchemaPath -Raw).Trim()
$actionSchemaObj = $null
try { $actionSchemaObj = $actionSchemaRaw | ConvertFrom-Json } catch { }
Check ($null -ne $actionSchemaObj) 'action-schema.json must parse as valid JSON'

# "the playtest learns to finish" wave, U1: the schema's own reply contract becomes
# {"choice": <int>, "why": "...", "note": "..."} -- menu-choice acting replaces free-form
# action/target/dir composition entirely (58 of 58 model runs died on patience by day 3, ~1,190 of
# ~1,260 refusals were the 8B model emitting semantically EMPTY freeform commands -- fable census).
if ($actionSchemaObj) {
    Check ($actionSchemaObj.type -eq 'object') 'action-schema.json must be a flat object schema'
    Check ($actionSchemaObj.properties.choice.type -eq 'integer') 'action-schema.json''s "choice" property must be typed integer (constrained decoding)'
    Check ($null -ne $actionSchemaObj.properties.why) 'action-schema.json must have a "why" property'
    Check ($actionSchemaObj.properties.why.type -eq 'string') 'action-schema.json''s "why" must be a plain string'
    Check ($null -ne $actionSchemaObj.properties.note) 'action-schema.json must have an optional "note" property'
    Check ($actionSchemaObj.properties.note.type -eq 'string') 'action-schema.json''s "note" must be a plain string'
    $requiredFields = @($actionSchemaObj.required)
    Check (($requiredFields | Sort-Object) -join ',' -eq 'choice,why') ('action-schema.json must require exactly [choice, why], got [' + ($requiredFields -join ',') + ']')
    # The old freeform properties (action/target/dir/frames) must be gone entirely -- a leftover
    # "action" enum property would silently re-legalise composing a verb by hand.
    $propNames = @($actionSchemaObj.properties.PSObject.Properties.Name)
    Check ($propNames -notcontains 'action') 'action-schema.json must NOT carry the old "action" property -- the model no longer composes a verb'
    Check ($propNames -notcontains 'target') 'action-schema.json must NOT carry the old "target" property -- the model no longer names a control'
    Check ($propNames -notcontains 'dir') 'action-schema.json must NOT carry the old "dir" property'
}

# Build-ModelRequestBody: format is spliced in as a real JSON OBJECT value, not a JsonEsc-escaped
# string -- proven by round-tripping the produced body back through ConvertFrom-Json and reading
# .format as a live object, not a string.
$bodyWithSchema = Build-ModelRequestBody -Model 'qwen3-vl:8b' -SystemPrompt 'sys prompt' -UserText 'user text' `
    -NumCtx 8192 -FormatSchema $actionSchemaRaw -Temperature 0
$parsedBodyWithSchema = $null
try { $parsedBodyWithSchema = $bodyWithSchema | ConvertFrom-Json } catch { }
Check ($null -ne $parsedBodyWithSchema) 'Build-ModelRequestBody with a schema must produce parseable JSON'
if ($parsedBodyWithSchema) {
    Check ($parsedBodyWithSchema.model -eq 'qwen3-vl:8b') 'request body must carry the model name'
    Check ($null -ne $parsedBodyWithSchema.format) 'request body must contain "format" when a schema is passed'
    Check ($parsedBodyWithSchema.format.properties.choice.type -eq 'integer') 'request body''s format.properties.choice.type must be integer (the schema survived the splice as a live object)'
    Check ($parsedBodyWithSchema.options.temperature -eq 0) 'request body must set temperature 0 on a schema-constrained call'
    Check ($parsedBodyWithSchema.messages.Count -eq 2) 'request body must carry exactly one system + one user message'
}

# Warm-up/judge shape: no schema, no temperature override -- proves the two are opt-in per call, not
# baked into every request this driver makes.
$bodyNoSchema = Build-ModelRequestBody -Model 'qwen3:14b' -SystemPrompt 'judge prompt' -UserText 'the log' -NumCtx 8192
$parsedBodyNoSchema = $bodyNoSchema | ConvertFrom-Json
Check (@($parsedBodyNoSchema.PSObject.Properties.Name) -notcontains 'format') 'a judge/warm-up call (no -FormatSchema) must NOT carry a "format" field'
Check (@($parsedBodyNoSchema.options.PSObject.Properties.Name) -notcontains 'temperature') 'a judge/warm-up call (no -Temperature) must NOT set temperature'

# Image attachment still round-trips through the extracted body builder.
$bodyWithImage = Build-ModelRequestBody -Model 'qwen3-vl:8b' -SystemPrompt 'sys' -UserText 'user' -ImageBase64 'ZmFrZQ==' -NumCtx 8192
$parsedBodyWithImage = $bodyWithImage | ConvertFrom-Json
Check (@($parsedBodyWithImage.messages[1].images) -contains 'ZmFrZQ==') 'request body must attach the image to the user message when -ImageBase64 is passed'

# Get-LegalCommandFromReply: the redefined refusal path (ruling 1) -- "three attempts produced no
# LEGAL action" now covers a disabled-control press, an empty reply, and (defensively) malformed
# JSON, while a real verb aimed at a legal target still passes straight through.
$illegalPress = Get-LegalCommandFromReply -Reply '{"action":"press","target":"NoSuchButton_xyz","why":"try it"}' -EnabledControls @('OpenShop')
Check ($illegalPress.Refused -eq $true) 'a press at a disabled/absent control must be Refused=true (this IS the honesty-counter path, ruling 1)'
Check ($illegalPress.Reason -like '*NoSuchButton_xyz*') ('the refusal reason must name the illegal target, got [' + $illegalPress.Reason + ']')
Check ($null -eq $illegalPress.Command) 'a refused reply must not also return a Command'

$legalPress = Get-LegalCommandFromReply -Reply '  {"action":"press","target":"OpenShop","why":"try it"}  ' -EnabledControls @('OpenShop')
Check ($legalPress.Refused -eq $false) 'a press at an ENABLED control must not be refused'
Check ($legalPress.Command -eq '{"action":"press","target":"OpenShop","why":"try it"}') ('a legal reply must be returned trimmed and otherwise verbatim, got [' + $legalPress.Command + ']')

$emptyReply = Get-LegalCommandFromReply -Reply '' -EnabledControls @()
Check ($emptyReply.Refused -eq $true) 'an empty reply must be Refused=true'
Check ($emptyReply.Reason -eq 'empty reply') ('an empty reply''s reason must say so exactly, got [' + $emptyReply.Reason + ']')

$malformedReply = Get-LegalCommandFromReply -Reply 'not json at all' -EnabledControls @()
Check ($malformedReply.Refused -eq $true) 'malformed (non-JSON) text must be Refused=true (defensive -- schema decoding should prevent this live)'

$unknownActionReply = Get-LegalCommandFromReply -Reply '{"action":"teleport","why":"nope"}' -EnabledControls @()
Check ($unknownActionReply.Refused -eq $true) 'an action outside the 5 known verbs must be Refused=true (defensive -- schema''s enum should prevent this live)'
Check ($unknownActionReply.Reason -like '*unknown action*') ('the reason must say "unknown action", got [' + $unknownActionReply.Reason + ']')

# Non-press/key verbs never consult any target legality list -- advance/stop/move have no "target"
# that could be illegal in the same sense a press or a key does.
$advanceReply = Get-LegalCommandFromReply -Reply '{"action":"advance","why":"tick the day"}' -EnabledControls @()
Check ($advanceReply.Refused -eq $false) 'advance must never be refused for lacking an enabled target'

# Regression (found live, W1, docs/plans/2026-08-10-002): a "key" action's target is fixed
# (interact/cancel), not per-turn like a press's enabled-control list -- but it was NOT checked at
# all until this fix, so an empty/hallucinated key target passed straight through as "legal" while
# the game itself refused it every time. A real llava:7b veteran run sent
# {"action":"key","target":"","why":"..."} on all 8 of 8 turns and read as 0% fallback (8 model-driven
# turns) right up until this check existed -- the exact "run made zero progress and reported healthy"
# shape A1/A6 already exist to catch, reopened one level down by schema-constrained decoding for this
# one verb.
$illegalKeyReply = Get-LegalCommandFromReply -Reply '{"action":"key","target":"","why":"Open the counter and serve a customer"}' -EnabledControls @()
Check ($illegalKeyReply.Refused -eq $true) 'a "key" action with an empty/illegal target must be Refused=true, not silently pass as legal'
Check ($illegalKeyReply.Reason -like '*illegal key target*') ('the reason must say "illegal key target", got [' + $illegalKeyReply.Reason + ']')

$legalKeyInteract = Get-LegalCommandFromReply -Reply '{"action":"key","target":"interact","why":"use the station"}' -EnabledControls @()
Check ($legalKeyInteract.Refused -eq $false) 'key/interact must be legal regardless of the enabled-control list (key targets InputMap actions, not on-screen controls)'
$legalKeyCancel = Get-LegalCommandFromReply -Reply '{"action":"key","target":"cancel","why":"back out"}' -EnabledControls @()
Check ($legalKeyCancel.Refused -eq $false) 'key/cancel must be legal'
$hallucinatedKeyTarget = Get-LegalCommandFromReply -Reply '{"action":"key","target":"climb","why":"nope"}' -EnabledControls @()
Check ($hallucinatedKeyTarget.Refused -eq $true) 'a key target outside {interact, cancel} must be refused even when non-empty'

# Regression (found live, same verification pass): "move" needs a real "dir" the same way "press"
# needs a real "target" -- dir is OPTIONAL in the flat schema, so a reply that omits it entirely is
# schema-legal. A real qwen3-vl:8b veteran run sent {"action":"move","why":"moving to the market..."}
# with no "dir" field on 3 of 8 turns and every one passed as "legal" here (before this fix) while the
# client refused all three ("unknown move dir ''") -- the same self-flattery shape as the key-target
# gap above, for the third verb that has a legality-bearing field.
$missingDirReply = Get-LegalCommandFromReply -Reply '{"action":"move","why":"moving to the market to buy materials"}' -EnabledControls @()
Check ($missingDirReply.Refused -eq $true) 'a "move" with no "dir" field at all must be Refused=true, not silently pass as legal'
Check ($missingDirReply.Reason -like '*illegal/missing move dir*') ('the reason must say "illegal/missing move dir", got [' + $missingDirReply.Reason + ']')

$emptyDirReply = Get-LegalCommandFromReply -Reply '{"action":"move","dir":"","why":"nope"}' -EnabledControls @()
Check ($emptyDirReply.Refused -eq $true) 'a "move" with an empty-string "dir" must be Refused=true'

$legalCardinalMove = Get-LegalCommandFromReply -Reply '{"action":"move","dir":"right","frames":20,"why":"go right"}' -EnabledControls @()
Check ($legalCardinalMove.Refused -eq $false) 'a move with a legal cardinal dir must not be refused'
$legalDiagonalMove = Get-LegalCommandFromReply -Reply '{"action":"move","dir":"up+left","frames":20,"why":"go diagonal"}' -EnabledControls @()
Check ($legalDiagonalMove.Refused -eq $false) 'a move with a legal diagonal dir (matching $script:KnownMoveDirs -- action-schema.json no longer carries a dir enum at all, U1 of the playtest-finishes wave) must not be refused'
$illegalDirWord = Get-LegalCommandFromReply -Reply '{"action":"move","dir":"north","why":"nope"}' -EnabledControls @()
Check ($illegalDirWord.Refused -eq $true) 'a move dir outside the known 8 (e.g. "north") must be refused'

# --- eyes-learn-labels wave, U1: label->name bridge ----------------------------------------------
# Campaign evidence: models press the visible LABEL ("Close"); the harness only ever accepted the
# node NAME ("CloseLedger") -- full/first-timer-1 died on "disabled/absent control: Close" at the
# exact state first-timer-6 typed "CloseLedger" and proceeded. Every fixture below uses that exact
# pair (name CloseLedger, label Close) as its running example.

# Format-ControlDescriptor: bare name when label is identical or the "<Name>" textless-button
# placeholder; "Name -- label: "Label"" only when the two genuinely differ.
Check ((Format-ControlDescriptor -Name 'CloseLedger' -Label 'Close') -eq 'CloseLedger -- label: "Close"') 'a differing label must render as Name -- label: "Label"'
Check ((Format-ControlDescriptor -Name 'AdvancePhase' -Label 'AdvancePhase') -eq 'AdvancePhase') 'an identical label must render as the bare name, no suffix'
Check ((Format-ControlDescriptor -Name 'OpenLedger' -Label '<OpenLedger>') -eq 'OpenLedger') 'the "<Name>" textless-button placeholder must render as the bare name, not a redundant suffix'
Check ((Format-ControlDescriptor -Name 'Foo' -Label '') -eq 'Foo') 'an empty label must render as the bare name'
Check ((Format-ControlDescriptor -Name 'Foo' -Label $null) -eq 'Foo') 'a null label must render as the bare name, never throw'
Check ((Format-ControlDescriptor -Name 'CloseLedger' -Label '  Close  ') -eq 'CloseLedger -- label: "Close"') 'a label must be trimmed before comparing/rendering'

# Get-EnabledControlDescriptors: enabled-only, one descriptor per control, caller's own order.
$descriptorControls = @(
    [pscustomobject]@{ name = 'CloseLedger'; label = 'Close'; enabled = $true }
    [pscustomobject]@{ name = 'AdvancePhase'; label = 'AdvancePhase'; enabled = $true }
    [pscustomobject]@{ name = 'BuyMat_copper'; label = 'Buy'; enabled = $false }
)
$descriptors = Get-EnabledControlDescriptors -Controls $descriptorControls
Check ($descriptors.Count -eq 2) ('Get-EnabledControlDescriptors must skip disabled controls, got ' + $descriptors.Count + ' of 3')
Check ($descriptors -contains 'CloseLedger -- label: "Close"') 'the enabled differing-label control must appear with its descriptor'
Check ($descriptors -contains 'AdvancePhase') 'the enabled identical-label control must appear as its bare name'
Check ($descriptors -notcontains 'BuyMat_copper -- label: "Buy"' -and $descriptors -notcontains 'BuyMat_copper') 'a DISABLED control must never appear in the enabled-descriptors list at all'

# Get-LegalCommandFromReply: a press naming the LABEL resolves to the control's real NAME when
# exactly one enabled control matches (case-insensitively, trimmed) -- the returned Command has its
# target rewritten (a raw label press would otherwise reach the client as "no visible control named
# 'Close'"), and ResolvedFromLabel/ResolvedToName tell the caller to log the resolution.
$labelControls = @(
    [pscustomobject]@{ name = 'CloseLedger'; label = 'Close'; enabled = $true }
)
$labelResolved = Get-LegalCommandFromReply -Reply '{"action":"press","target":"Close","why":"closing the ledger"}' -EnabledControls @('CloseLedger') -EnabledControlLabels $labelControls
Check ($labelResolved.Refused -eq $false) 'a press naming an unambiguous enabled label must resolve, not refuse'
Check ($labelResolved.ResolvedFromLabel -eq 'Close') ('ResolvedFromLabel must carry the label the model sent, got [' + $labelResolved.ResolvedFromLabel + ']')
Check ($labelResolved.ResolvedToName -eq 'CloseLedger') ('ResolvedToName must carry the real control name, got [' + $labelResolved.ResolvedToName + ']')
$labelResolvedParsed = $labelResolved.Command | ConvertFrom-Json
Check ($labelResolvedParsed.action -eq 'press' -and $labelResolvedParsed.target -eq 'CloseLedger') ('the rewritten Command must press the real name, got [' + $labelResolved.Command + ']')
Check ($labelResolvedParsed.why -eq 'closing the ledger') 'the rewritten Command must preserve the original why text'

# A label resolution with a "note" field must carry the note through too (the scratchpad echo must
# not silently disappear just because the target got rewritten).
$labelResolvedWithNote = Get-LegalCommandFromReply -Reply '{"action":"press","target":"Close","why":"t","note":"remember this"}' -EnabledControls @('CloseLedger') -EnabledControlLabels $labelControls
$labelResolvedWithNoteParsed = $labelResolvedWithNote.Command | ConvertFrom-Json
Check ($labelResolvedWithNoteParsed.note -eq 'remember this') 'a rewritten label-resolved Command must preserve the original note text'

# A control name that ALREADY matches exactly must behave exactly as before -- label resolution is a
# fallback, never consulted when the plain name check already succeeds.
$plainNameStillWorks = Get-LegalCommandFromReply -Reply '{"action":"press","target":"CloseLedger","why":"t"}' -EnabledControls @('CloseLedger') -EnabledControlLabels $labelControls
Check ($plainNameStillWorks.Refused -eq $false -and $plainNameStillWorks.ResolvedFromLabel -eq $null) 'an exact name match must succeed via the ORIGINAL path, never reported as a label resolution'

# Two or more enabled controls sharing a label: refuse, naming the candidates -- never guess.
$ambiguousLabelControls = @(
    [pscustomobject]@{ name = 'BuyOre_1_copper'; label = 'Buy'; enabled = $true }
    [pscustomobject]@{ name = 'BuyOre_3_copper'; label = 'Buy'; enabled = $true }
)
$ambiguousResolved = Get-LegalCommandFromReply -Reply '{"action":"press","target":"Buy","why":"t"}' -EnabledControls @('BuyOre_1_copper', 'BuyOre_3_copper') -EnabledControlLabels $ambiguousLabelControls
Check ($ambiguousResolved.Refused -eq $true) 'a label matching 2+ enabled controls must refuse, never guess which one'
Check ($ambiguousResolved.Reason -like '*ambiguous label*') ('the reason must say "ambiguous label", got [' + $ambiguousResolved.Reason + ']')
Check ($ambiguousResolved.Reason -like '*BuyOre_1_copper*' -and $ambiguousResolved.Reason -like '*BuyOre_3_copper*') ('the ambiguous-label reason must NAME both candidates, got [' + $ambiguousResolved.Reason + ']')
Check ($ambiguousResolved.ResolvedFromLabel -eq $null) 'an ambiguous (refused) label match must not report a resolution'

# Label matching must NEVER resurrect a DISABLED control -- a control sharing the pressed label but
# absent from EnabledControls must be excluded from candidacy entirely, even though its label data is
# present in EnabledControlLabels (the observation includes disabled controls on purpose).
$disabledLabelControls = @(
    [pscustomobject]@{ name = 'CloseLedger'; label = 'Close'; enabled = $false }
)
$neverResurrect = Get-LegalCommandFromReply -Reply '{"action":"press","target":"Close","why":"t"}' -EnabledControls @() -EnabledControlLabels $disabledLabelControls
Check ($neverResurrect.Refused -eq $true) 'a label belonging only to a DISABLED control must still refuse'
Check ($neverResurrect.Reason -like '*disabled/absent control: Close*') ('a label with no enabled candidate must fall through to the plain disabled/absent refusal, got [' + $neverResurrect.Reason + ']')

# Empty target: refuse, and the reason lists up to 5 enabled control names (never all of them
# unbounded -- the refusal text rides in the model's own next prompt).
$manyEnabled = @('Alpha', 'Bravo', 'Charlie', 'Delta', 'Echo', 'Foxtrot', 'Golf')
$emptyTargetPress = Get-LegalCommandFromReply -Reply '{"action":"press","target":"","why":"t"}' -EnabledControls $manyEnabled
Check ($emptyTargetPress.Refused -eq $true) 'an empty press target must refuse'
Check ($emptyTargetPress.Reason -like '*empty press target*') ('the reason must say "empty press target", got [' + $emptyTargetPress.Reason + ']')
Check ($emptyTargetPress.Reason -like '*Alpha*' -and $emptyTargetPress.Reason -like '*Echo*') 'the empty-target reason must list the first 5 enabled controls'
Check ($emptyTargetPress.Reason -notlike '*Foxtrot*' -and $emptyTargetPress.Reason -notlike '*Golf*') 'the empty-target reason must cap at 5 enabled controls, never list all 7'

# Backward compatibility: every existing caller that never passes -EnabledControlLabels at all (the
# default @()) must behave byte-identically to before this unit -- a label that could have matched
# is simply never considered when the caller supplies no label data.
$noLabelDataPress = Get-LegalCommandFromReply -Reply '{"action":"press","target":"Close","why":"t"}' -EnabledControls @('CloseLedger')
Check ($noLabelDataPress.Refused -eq $true -and $noLabelDataPress.Reason -like '*disabled/absent control: Close*') 'with no -EnabledControlLabels supplied at all, a label-shaped target must fall through to the plain refusal exactly as before this unit'

# turn-prompt.ps1's Controls: block must thread the SAME descriptor formatting through the model's
# per-turn observation, not just the refusal-feedback path -- Build-ActUserText already dot-sourced
# above (section 4).
$labelControlState = [pscustomobject]@{
    day = 1; phase = 'Morning'; beat = 'None'; location = 'town'; canMove = $true; gold = 10
    actionSlotsRemaining = 5; lastOutcome = '(run start)'; screenText = @(); interactPrompt = ''
    controls = @(
        [pscustomobject]@{ name = 'CloseLedger'; label = 'Close'; enabled = $true }
        [pscustomobject]@{ name = 'AdvancePhase'; label = 'AdvancePhase'; enabled = $true }
    )
    nearby = @()
}
$labelControlText = Build-ActUserText -State $labelControlState -Turn 1 -Turns 40
Check ($labelControlText -like '*CloseLedger -- label: "Close"*') ('the Controls: block must show the differing-label descriptor, got a text without it: [' + $labelControlText + ']')
Check ($labelControlText -notlike '*AdvancePhase -- label*') 'an identical-label control must render as its bare name with no redundant suffix'

# "the playtest learns to finish" wave, U1: act.md's old "target field is always a NAME" rule is
# GONE, not relocated -- the model never types a name or a label any more, only a menu index.
$actMdRawText = Get-Content (Join-Path $toolsDir 'agent-playtest\prompts\act.md') -Raw
Check ($actMdRawText -notlike '*target*field*always*NAME*' -and $actMdRawText -notlike '*always a control''s NAME*') 'act.md must NOT still carry the old "target field is always a NAME" rule -- U1 replaced typed targets with a menu index'
Check ($actMdRawText -like '*choice*') 'act.md must document the new "choice" reply field'
Check ($actMdRawText -like '*NUMBERED MENU*' -or $actMdRawText -like '*numbered menu*') 'act.md must tell the model to expect a numbered menu each turn'

# NORMALIZE/regex-extract are DELETED, not relocated -- grep the real script text for the specific
# code shapes that used to live in the per-turn loop (not just the word "NORMALIZE", which the
# deletion's own explanatory comment still legitimately uses).
$agentPlaytestRawText = Get-Content (Join-Path $toolsDir 'agent-playtest.ps1') -Raw
Check ($agentPlaytestRawText -notlike '*[regex]::Match($reply*') 'the old regex JSON-extract call ([regex]::Match($reply, ...)) must be gone from agent-playtest.ps1'
Check ($agentPlaytestRawText -notlike '*normalized "*') 'the old NORMALIZE Say(''normalized "..."'') message must be gone from agent-playtest.ps1'
# "the playtest learns to finish" wave, U1: the main act loop now resolves a MENU CHOICE, not a
# free-form reply -- Get-LegalCommandFromReply is RETAINED in model-call.ps1 (its own header explains
# why: Get-ResolvedPressCommandText is reused by the new path) but is no longer the call site here.
Check ($agentPlaytestRawText -like '*Get-LegalCommandFromMenuChoice*') 'agent-playtest.ps1 must call Get-LegalCommandFromMenuChoice in its per-turn attempts loop'
Check ($agentPlaytestRawText -like '*Build-ActMenu*') 'agent-playtest.ps1 must call Build-ActMenu to build each turn''s menu'
Check ($agentPlaytestRawText -like '*action-schema.json*') 'agent-playtest.ps1 must load action-schema.json and pass it as the act calls'' format'

# Honesty footer (W1): present as a pure, testable function so a scripted/live run is not the only
# way to prove its content -- the live -Scripted run gets its own end-to-end check on top of this.
$footerLines = Get-HonestyFooterLines
$footerText = $footerLines -join [Environment]::NewLine
Check ($footerText -like '*Game feel*') 'the honesty footer must name game feel by that term'
Check ($footerText -like '*Tone register*') 'the honesty footer must name tone register by that term'
Check ($footerText -like '*Emotional weight*') 'the honesty footer must name emotional weight by that term'
Check ($footerText -like '*cannot ask*') 'the honesty footer must say silence on these is not a clean bill'

# --- 11a. Menu-choice acting (U1, "the playtest learns to finish" wave) ---------------------------
# Owner finding 2026-08-11 + fable census: 58 of 58 model runs died on patience by day 3, ~1,190 of
# ~1,260 refusals were the 8B model emitting semantically EMPTY freeform commands. Build-ActMenu /
# Get-LegalCommandFromMenuChoice (model-call.ps1, already dot-sourced above) replace composing JSON
# with picking a number.

$menuStateNoMoveNoInteract = [pscustomobject]@{
    canMove        = $false
    interactPrompt = ''
    nearby         = @()
    controls       = @(
        [pscustomobject]@{ name = 'OpenShop'; label = 'Open Shop'; enabled = $true }
        [pscustomobject]@{ name = 'CloseLedger'; label = 'Close'; enabled = $true }
        [pscustomobject]@{ name = 'BuyMat_copper'; label = 'Buy'; enabled = $false }
    )
}
$menuBasic = Build-ActMenu -State $menuStateNoMoveNoInteract

# Item 0 is always advance.
Check ($menuBasic.Count -gt 0) 'Build-ActMenu must never return an empty menu'
Check ($menuBasic[0].Index -eq 0) 'menu item 0 must always exist'
Check ($menuBasic[0].Command.Action -eq 'advance') 'menu item 0 must always resolve to advance'
Check ($menuBasic[0].DisplayText -like '0.*advance*') 'menu item 0''s display text must name advance'

# Only ENABLED controls get a menu item -- the disabled BuyMat_copper must never appear.
$pressItems = @($menuBasic | Where-Object { $_.Command.Action -eq 'press' })
Check ($pressItems.Count -eq 2) ('exactly 2 enabled controls must produce exactly 2 press menu items, got ' + $pressItems.Count)
Check (@($pressItems | Where-Object { $_.Command.Target -eq 'BuyMat_copper' }).Count -eq 0) 'a DISABLED control must never appear in the menu at all'

# Label+name both shown -- CloseLedger/Close differ, so the descriptor must show both (reuses
# Format-ControlDescriptor, already proven correct by the eyes-learn-labels section above).
$closeItem = @($menuBasic | Where-Object { $_.Command.Target -eq 'CloseLedger' }) | Select-Object -First 1
Check ($null -ne $closeItem) 'the CloseLedger control must produce a menu item'
Check ($closeItem.DisplayText -like '*CloseLedger -- label: "Close"*') ('a differing label must show both name and label in the menu, got [' + $closeItem.DisplayText + ']')

# No move items when canMove is false; cancel is always present (no per-turn legality signal exists
# for it); interact is absent when neither interactPrompt nor any nearby.inRange is true.
Check (@($menuBasic | Where-Object { $_.Command.Action -eq 'move' }).Count -eq 0) 'canMove=false must produce zero move menu items'
Check (@($menuBasic | Where-Object { $_.Command.Action -eq 'key' -and $_.Command.Target -eq 'cancel' }).Count -eq 1) 'cancel must always appear exactly once, canMove or not'
Check (@($menuBasic | Where-Object { $_.Command.Action -eq 'key' -and $_.Command.Target -eq 'interact' }).Count -eq 0) 'interact must be ABSENT when neither interactPrompt nor any nearby.inRange is true'

# canMove=true adds exactly the 8 fixed directions, in $script:KnownMoveDirs order.
$menuStateCanMove = [pscustomobject]@{
    canMove        = $true
    interactPrompt = ''
    nearby         = @()
    controls       = @()
}
$menuWithMove = Build-ActMenu -State $menuStateCanMove
$moveItems = @($menuWithMove | Where-Object { $_.Command.Action -eq 'move' })
Check ($moveItems.Count -eq 8) ('canMove=true must add exactly 8 move menu items, got ' + $moveItems.Count)
Check ((@($moveItems | ForEach-Object { $_.Command.Dir }) -join ',') -eq ($script:KnownMoveDirs -join ',')) 'move menu items must appear in $script:KnownMoveDirs order'

# interactPrompt present -> interact legal; nearby.inRange present -> interact legal too (the
# "YOU ARE HERE, press interact" building case, a SEPARATE signal from interactPrompt).
$menuStateInteractPrompt = [pscustomobject]@{ canMove = $false; interactPrompt = 'Press E to open the chest'; nearby = @(); controls = @() }
Check (@((Build-ActMenu -State $menuStateInteractPrompt) | Where-Object { $_.Command.Action -eq 'key' -and $_.Command.Target -eq 'interact' }).Count -eq 1) 'a non-empty interactPrompt must make interact legal'

$menuStateNearbyInRange = [pscustomobject]@{ canMove = $true; interactPrompt = ''; nearby = @([pscustomobject]@{ inRange = $true }); controls = @() }
Check (@((Build-ActMenu -State $menuStateNearbyInRange) | Where-Object { $_.Command.Action -eq 'key' -and $_.Command.Target -eq 'interact' }).Count -eq 1) 'a nearby entry with inRange=true must also make interact legal'

# Deterministic ordering: the SAME inputs must number identically across two separate calls (proves
# stability across turns for the same enabled-control/canMove/interact set, not just "some order").
$menuAgain = Build-ActMenu -State $menuStateNoMoveNoInteract
Check ((@($menuBasic | ForEach-Object { $_.DisplayText }) -join '|') -eq (@($menuAgain | ForEach-Object { $_.DisplayText }) -join '|')) 'Build-ActMenu must number identically across two calls given the same inputs'

# Resolution: choice N must resolve to EXACTLY the command the OLD free-form path would have sent
# for that verb -- proven by comparing parsed fields, not raw text (key ORDER is an implementation
# detail; the VALUES are the contract).
$openShopItem = @($menuBasic | Where-Object { $_.Command.Target -eq 'OpenShop' }) | Select-Object -First 1
$menuPressReply = '{"choice":' + $openShopItem.Index + ',"why":"try the shop","note":"remember this"}'
$menuPressResult = Get-LegalCommandFromMenuChoice -Reply $menuPressReply -MenuItems $menuBasic
Check ($menuPressResult.Refused -eq $false) 'a menu choice for an enabled control must not be refused'
$menuPressParsed = $menuPressResult.Command | ConvertFrom-Json
$legacyPressResult = Get-LegalCommandFromReply -Reply '{"action":"press","target":"OpenShop","why":"try the shop","note":"remember this"}' -EnabledControls @('OpenShop')
$legacyPressParsed = $legacyPressResult.Command | ConvertFrom-Json
Check ($menuPressParsed.action -eq $legacyPressParsed.action -and $menuPressParsed.target -eq $legacyPressParsed.target -and $menuPressParsed.why -eq $legacyPressParsed.why -and $menuPressParsed.note -eq $legacyPressParsed.note) ('a menu-resolved press must match the legacy free-form command field-for-field, got menu=[' + $menuPressResult.Command + '] legacy=[' + $legacyPressResult.Command + ']')

$menuAdvanceReply = '{"choice":0,"why":"nothing else to do"}'
$menuAdvanceResult = Get-LegalCommandFromMenuChoice -Reply $menuAdvanceReply -MenuItems $menuBasic
$menuAdvanceParsed = $menuAdvanceResult.Command | ConvertFrom-Json
Check ($menuAdvanceParsed.action -eq 'advance' -and $menuAdvanceParsed.why -eq 'nothing else to do') ('choice 0 must resolve to advance with the model''s own why text, got [' + $menuAdvanceResult.Command + ']')

$moveItem = @($menuWithMove | Where-Object { $_.Command.Dir -eq 'right' }) | Select-Object -First 1
$menuMoveReply = '{"choice":' + $moveItem.Index + ',"why":"go right"}'
$menuMoveResult = Get-LegalCommandFromMenuChoice -Reply $menuMoveReply -MenuItems $menuWithMove
$menuMoveParsed = $menuMoveResult.Command | ConvertFrom-Json
Check ($menuMoveParsed.action -eq 'move' -and $menuMoveParsed.dir -eq 'right' -and $menuMoveParsed.frames -gt 0) ('choice resolving to move must produce action=move, dir=right, and a positive frames count, got [' + $menuMoveResult.Command + ']')

# Out-of-range and missing choice both refuse -- still signal (ruling 1), still hits patience.
$outOfRangeResult = Get-LegalCommandFromMenuChoice -Reply '{"choice":999,"why":"nope"}' -MenuItems $menuBasic
Check ($outOfRangeResult.Refused -eq $true) 'an out-of-range choice must be Refused=true'
Check ($outOfRangeResult.Reason -like '*out-of-range*') ('the reason must say out-of-range, got [' + $outOfRangeResult.Reason + ']')
Check ($null -eq $outOfRangeResult.Command) 'a refused choice must not also return a Command'

$missingChoiceResult = Get-LegalCommandFromMenuChoice -Reply '{"why":"no choice field at all"}' -MenuItems $menuBasic
Check ($missingChoiceResult.Refused -eq $true) 'a reply with no "choice" field at all must be Refused=true'

$emptyReplyMenuResult = Get-LegalCommandFromMenuChoice -Reply '' -MenuItems $menuBasic
Check ($emptyReplyMenuResult.Refused -eq $true -and $emptyReplyMenuResult.Reason -eq 'empty reply') 'an empty reply must be Refused=true with reason "empty reply"'

$nonIntegerChoiceResult = Get-LegalCommandFromMenuChoice -Reply '{"choice":"three","why":"nope"}' -MenuItems $menuBasic
Check ($nonIntegerChoiceResult.Refused -eq $true) 'a non-integer choice value must be Refused=true'

# Build-ActUserText: -MenuItems is OPTIONAL -- every pre-existing caller (every check above this
# point that never passed it) must keep rendering byte-identically (proven by the "Answer with one
# JSON object only." exact line surviving unchanged, and no "Choose ONE action" text appearing).
$noMenuText = Build-ActUserText -State $nearbyState -Turn 1 -Turns 40
Check ($noMenuText -like '*Answer with one JSON object only.*') 'with no -MenuItems passed, the old exact closing line must survive unchanged'
Check ($noMenuText -notlike '*Choose ONE action*') 'with no -MenuItems passed, the numbered-menu block must not appear at all'

$withMenuText = Build-ActUserText -State $nearbyState -Turn 1 -Turns 40 -MenuItems $menuBasic
Check ($withMenuText -like '*Choose ONE action by its number:*') 'with -MenuItems passed, the numbered-menu block must appear'
Check ($withMenuText -like '*0. advance*') 'the rendered menu must include item 0 (advance)'
Check ($withMenuText -like '*"choice"*') 'with -MenuItems passed, the closing line must name the "choice" field'

# --- 11b. Eyes/brain residency plan (U2, "the playtest learns to finish" wave) ---------------------
$singleModePlan = Get-ModelResidencyPlan -Model 'qwen3-vl:8b' -BrainModel '' -JudgeModel 'qwen3:14b'
Check ($singleModePlan.SplitMode -eq $false) 'an empty -BrainModel must resolve to single-model mode'
Check ($singleModePlan.ActModel -eq 'qwen3-vl:8b') 'single-model mode''s ActModel must be the vision model'
Check ($singleModePlan.ActUsesImage -eq $true) 'single-model mode must still attach an image every turn'
Check ($singleModePlan.JudgeModel -eq 'qwen3:14b') 'single-model mode''s JudgeModel must be the dedicated judge model'
Check (@($singleModePlan.UnloadBeforeJudge) -contains 'qwen3-vl:8b') 'single-model mode must unload the vision model before the judge call'
Check (@($singleModePlan.UnloadAfterRun) -contains 'qwen3:14b') 'single-model mode must unload the judge model after the run'

$splitModePlan = Get-ModelResidencyPlan -Model 'qwen3-vl:8b' -BrainModel 'qwen3:14b' -JudgeModel 'qwen3:14b'
Check ($splitModePlan.SplitMode -eq $true) 'a non-empty -BrainModel must resolve to split mode'
Check ($splitModePlan.ActModel -eq 'qwen3:14b') 'split mode''s ActModel must be the brain model'
Check ($splitModePlan.ActUsesImage -eq $false) 'split mode must NOT attach an image (frame narration is skipped, not swapped)'
Check ($splitModePlan.JudgeModel -eq 'qwen3:14b') 'split mode''s judge call must also go to the brain model'
Check (@($splitModePlan.UnloadBeforeJudge).Count -eq 0) 'split mode must unload NOTHING before the judge call -- the brain model is already resident and reused'
Check ((@($splitModePlan.UnloadAfterRun) -join ',') -eq 'qwen3:14b') 'split mode must unload exactly the brain model, once, after the run'

# Build-ActUserText: -NoImage is a plain switch, off by default -- every pre-existing caller keeps
# rendering with no "No screenshot" line at all.
$defaultImageText = Build-ActUserText -State $nearbyState -Turn 1 -Turns 40
Check ($defaultImageText -notlike '*No screenshot*') 'with -NoImage not passed, no "no screenshot" line must appear'
$noImageText = Build-ActUserText -State $nearbyState -Turn 1 -Turns 40 -NoImage
Check ($noImageText -like '*No screenshot this turn*') 'with -NoImage passed, the text-only note must appear'

# Driver wiring: agent-playtest.ps1 must actually compute and use the residency plan, not just
# import the function and never call it.
Check ($agentPlaytestRawText -like '*Get-ModelResidencyPlan*') 'agent-playtest.ps1 must call Get-ModelResidencyPlan'
Check ($agentPlaytestRawText -like '*BrainModel*') 'agent-playtest.ps1 must declare a -BrainModel parameter'
Check ($agentPlaytestRawText -like '*residency.ActModel*') 'agent-playtest.ps1 must use the residency plan''s ActModel for its act calls'
Check ($agentPlaytestRawText -like '*residency.JudgeModel*') 'agent-playtest.ps1 must use the residency plan''s JudgeModel for its judge call'

# --- 12. Dead-verb detector (W3, docs/plans/2026-08-10-002, ruling 7) -- "a mechanical check the
# sceptic persona used to only narrate in prose" ---------------------------------------------------
. (Join-Path $toolsDir 'agent-playtest\deadverb.ps1')

# Ruling 7's exclusion list must be EXACTLY {turn, lastOutcome} -- not a broader hand-typed set. This
# pins the LITERAL constant (the same $script: pattern personas.ps1 uses for $script:KnownPersonas),
# not just behavior that happens to match it today.
Check (($script:DeadVerbExcludedFields -join ',') -eq 'turn,lastOutcome') ('the exclusion list must be EXACTLY {turn, lastOutcome}, got [' + ($script:DeadVerbExcludedFields -join ',') + ']')

# A minimal but realistic state.json shape (AgentPlaytest.cs's StateDigest fields).
$dvStateA = [pscustomobject]@{
    turn                 = 1
    day                  = 1
    phase                = 'Morning'
    beat                 = 'None'
    actionSlotsRemaining = 5
    gold                 = 100
    location             = 'town'
    canMove              = $true
    screenText           = @('Welcome to the forge')
    controls             = @([pscustomobject]@{ name = 'OpenShop'; label = 'Open Shop'; enabled = $true })
    interactPrompt       = ''
    nearby               = @([pscustomobject]@{ key = 'forge'; label = 'Forge'; direction = 'up'; distance = 32; inRange = $true })
    lastOutcome          = '(run start)'
}
# Same in every field EXCEPT turn and lastOutcome -- the two fields ruling 7 excludes because they
# necessarily change on every single turn regardless of what the press did.
$dvStateB = [pscustomobject]@{
    turn                 = 2
    day                  = 1
    phase                = 'Morning'
    beat                 = 'None'
    actionSlotsRemaining = 5
    gold                 = 100
    location             = 'town'
    canMove              = $true
    screenText           = @('Welcome to the forge')
    controls             = @([pscustomobject]@{ name = 'OpenShop'; label = 'Open Shop'; enabled = $true })
    interactPrompt       = ''
    nearby               = @([pscustomobject]@{ key = 'forge'; label = 'Forge'; direction = 'up'; distance = 32; inRange = $true })
    lastOutcome          = 'pressed OpenShop -> gold 100 -> 100'
}
$dvFingerprintA = Get-StateFingerprint -State $dvStateA
$dvFingerprintB = Get-StateFingerprint -State $dvStateB
Check ($dvFingerprintA -eq $dvFingerprintB) 'the fingerprint must be identical when ONLY turn/lastOutcome differ (ruling 7 exclusion list)'
Check ($dvFingerprintA.Length -eq 64) ('the fingerprint must be a 64-char SHA256 hex digest, got [' + $dvFingerprintA + '] (' + $dvFingerprintA.Length + ' chars)')

# A NORMAL field changing (gold) must change the fingerprint -- proves this is not a function that
# always returns the same hash regardless of input.
$dvStateGoldChanged = [pscustomobject]@{
    turn = 1; day = 1; phase = 'Morning'; beat = 'None'; actionSlotsRemaining = 5; gold = 999
    location = 'town'; canMove = $true; screenText = @('Welcome to the forge')
    controls = @([pscustomobject]@{ name = 'OpenShop'; label = 'Open Shop'; enabled = $true })
    interactPrompt = ''
    nearby = @([pscustomobject]@{ key = 'forge'; label = 'Forge'; direction = 'up'; distance = 32; inRange = $true })
    lastOutcome = '(run start)'
}
Check ($dvFingerprintA -ne (Get-StateFingerprint -State $dvStateGoldChanged)) 'the fingerprint must change when a normal field (gold) changes'

# THE whole-state guarantee (the state-fingerprint lesson, cited by ruling 7 directly): an ENTIRELY
# NEW field neither this file nor a hand-typed inclusion list has ever heard of must still change the
# fingerprint. If this failed, the walk would secretly be an inclusion list wearing an exclusion
# list's name -- exactly the defect class CLAUDE.md rules 6-10 exist to catch.
$dvStateWithNewField = [pscustomobject]@{
    turn = 1; day = 1; phase = 'Morning'; beat = 'None'; actionSlotsRemaining = 5; gold = 100
    location = 'town'; canMove = $true; screenText = @('Welcome to the forge')
    controls = @([pscustomobject]@{ name = 'OpenShop'; label = 'Open Shop'; enabled = $true })
    interactPrompt = ''
    nearby = @([pscustomobject]@{ key = 'forge'; label = 'Forge'; direction = 'up'; distance = 32; inRange = $true })
    lastOutcome = '(run start)'
    brandNewSurfaceFieldNoOneHasSeenYet = 'x'
}
Check ($dvFingerprintA -ne (Get-StateFingerprint -State $dvStateWithNewField)) 'adding a state field the fingerprint code has never heard of must still change the hash (the whole-state guarantee, not a second hand-typed list)'

# Canonicalization: object key ORDER must not matter (two logically-identical objects built with keys
# in different insertion order must hash the same) but array ELEMENT order MUST matter (screenText/
# controls/nearby order is real game state, never a set to normalize away).
$dvOrderedOne = [pscustomobject][ordered]@{ gold = 100; day = 1; phase = 'Morning' }
$dvOrderedTwo = [pscustomobject][ordered]@{ phase = 'Morning'; gold = 100; day = 1 }
Check ((Get-StateFingerprint -State $dvOrderedOne) -eq (Get-StateFingerprint -State $dvOrderedTwo)) 'object key order must not affect the fingerprint (canonical: sorted keys)'

$dvArrayOrderOne = [pscustomobject]@{ screenText = @('first line', 'second line') }
$dvArrayOrderTwo = [pscustomobject]@{ screenText = @('second line', 'first line') }
Check ((Get-StateFingerprint -State $dvArrayOrderOne) -ne (Get-StateFingerprint -State $dvArrayOrderTwo)) 'array ELEMENT order must affect the fingerprint (screenText/controls/nearby order is real state, never normalized)'

# --- Get-BackendEventsForSlice (backend.ps1's own W3 extension -- "if U2's API is whole-log only,
# W3 extends it, never forks it") ---
$dvFixturePath = Join-Path $toolsDir 'agent-playtest\tests\deadverb-backend-fixture.jsonl'
Check (Test-Path $dvFixturePath) ('deadverb backend fixture must exist at ' + $dvFixturePath)
$dvFixtureRead = Read-BackendLogRows -LogPath $dvFixturePath
Check ($dvFixtureRead.Rows.Count -eq 5) ('deadverb fixture must have exactly 5 rows (1 session + 3 tick + 1 note), got ' + $dvFixtureRead.Rows.Count)

# RowCountBefore=1 (only the session row existed): the slice covers all 3 tick rows (events 0,5,0)
# plus the trailing note -- a LOUD slice, a sim event clearly fired.
$dvLoudSlice = Get-BackendEventsForSlice -AllRows $dvFixtureRead.Rows -RowCountBefore 1
Check ($dvLoudSlice.SliceRowCount -eq 4) ('loud slice must cover 4 rows (3 tick + 1 note), got ' + $dvLoudSlice.SliceRowCount)
Check ($dvLoudSlice.EventCount -eq 5) ('loud slice event count must be 0+5+0=5, got ' + $dvLoudSlice.EventCount)
Check ($dvLoudSlice.SawSimEvent -eq $true) 'a slice whose tick rows sum to a nonzero event count must report SawSimEvent=true'

# RowCountBefore=3 (session + first two tick rows already existed): the slice is just the LAST tick
# (events=0) plus the note -- a SILENT slice.
$dvSilentSlice = Get-BackendEventsForSlice -AllRows $dvFixtureRead.Rows -RowCountBefore 3
Check ($dvSilentSlice.SliceRowCount -eq 2) ('silent slice must cover 2 rows (1 tick + 1 note), got ' + $dvSilentSlice.SliceRowCount)
Check ($dvSilentSlice.EventCount -eq 0) ('silent slice event count must be 0, got ' + $dvSilentSlice.EventCount)
Check ($dvSilentSlice.SawSimEvent -eq $false) 'a slice whose tick rows sum to zero events must report SawSimEvent=false'

# RowCountBefore = total row count: nothing was appended since -- an EMPTY slice, still SawSimEvent
# =false and never a crash on an out-of-range index.
$dvEmptySlice = Get-BackendEventsForSlice -AllRows $dvFixtureRead.Rows -RowCountBefore ($dvFixtureRead.Rows.Count)
Check ($dvEmptySlice.SliceRowCount -eq 0) ('a RowCountBefore equal to the total row count must produce an empty slice, got ' + $dvEmptySlice.SliceRowCount)
Check ($dvEmptySlice.SawSimEvent -eq $false) 'an empty slice must report SawSimEvent=false, never throw'

# Note/action rows never contribute to EventCount even when present in the slice -- only "tick" rows
# carry a real eventCount (mirrors Get-BackendSummary's own EventsTotalAcrossTicks convention).
Check ($dvSilentSlice.TickRowCount -eq 1) ('silent slice must count exactly 1 tick row, got ' + $dvSilentSlice.TickRowCount)

# --- Get-DeadVerbVerdict -- the fusion of both signals, and the ONLY thing allowed to say CANDIDATE
$dvCandidateVerdict = Get-DeadVerbVerdict -FingerprintBefore $dvFingerprintA -FingerprintAfter $dvFingerprintB `
    -BackendSlice $dvSilentSlice -Turn 7 -Phase 'Morning' -ControlName 'OpenShop'
Check ($dvCandidateVerdict.IsCandidate -eq $true) 'identical fingerprint + backend-silent slice must produce a CANDIDATE (both signals agree)'
Check ($null -ne $dvCandidateVerdict.Line) 'a candidate verdict must carry a non-null findings.md line'
Check ($dvCandidateVerdict.Line -like '*CANDIDATE*') 'the candidate line must say CANDIDATE'
Check ($dvCandidateVerdict.Line -like '*law-3*') 'the candidate line must name law-3'
Check ($dvCandidateVerdict.Line -like '*turn 7*') 'the candidate line must name the turn number'
Check ($dvCandidateVerdict.Line -like '*Morning*') 'the candidate line must name the phase'
Check ($dvCandidateVerdict.Line -like '*OpenShop*') 'the candidate line must name the pressed control'
Check ($dvCandidateVerdict.Line -like '*human confirmation*') 'the candidate line must say it is for human confirmation, never asserted as a defect (ruling 7''s exact words)'
Check ($dvCandidateVerdict.Line -notlike '*is a defect*') 'the candidate line must never assert the press IS a defect'

# A CHANGED fingerprint suppresses the candidate even with a silent backend slice -- the press
# demonstrably did something, whatever the backend log did or did not separately record.
$dvChangedFingerprintVerdict = Get-DeadVerbVerdict -FingerprintBefore $dvFingerprintA `
    -FingerprintAfter (Get-StateFingerprint -State $dvStateGoldChanged) -BackendSlice $dvSilentSlice `
    -Turn 7 -Phase 'Morning' -ControlName 'OpenShop'
Check ($dvChangedFingerprintVerdict.IsCandidate -eq $false) 'a CHANGED fingerprint must suppress the candidate even when the backend slice is silent'
Check ($null -eq $dvChangedFingerprintVerdict.Line) 'a suppressed verdict must not carry a findings.md line'

# A LOGGED sim event suppresses the candidate even with an unchanged fingerprint -- something fired
# off-screen (a background hero tick, say), so the press is not provably inert.
$dvLoggedEventVerdict = Get-DeadVerbVerdict -FingerprintBefore $dvFingerprintA -FingerprintAfter $dvFingerprintB `
    -BackendSlice $dvLoudSlice -Turn 7 -Phase 'Morning' -ControlName 'OpenShop'
Check ($dvLoggedEventVerdict.IsCandidate -eq $false) 'a logged sim event must suppress the candidate even when the fingerprint is unchanged'

# An UNKNOWN backend slice ($null -- no backend log at all) must NEVER be treated as silence. Absence
# of evidence is not evidence of a dead verb; this file adds nothing it cannot support.
$dvUnknownBackendVerdict = Get-DeadVerbVerdict -FingerprintBefore $dvFingerprintA -FingerprintAfter $dvFingerprintB `
    -BackendSlice $null -Turn 7 -Phase 'Morning' -ControlName 'OpenShop'
Check ($dvUnknownBackendVerdict.IsCandidate -eq $false) 'an unavailable backend slice must never be treated as silence -- no candidate without positive evidence'

# --- Frame retention (Definition of Done: "keep that turn's frame regardless of -FrameEvery") ------
$dvFrameTempDir = Join-Path $env:TEMP ('deadverb-frame-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dvFrameTempDir -Force | Out-Null
try {
    $dvSourceFrame = Join-Path $dvFrameTempDir 'frame.png'
    Set-Content -Path $dvSourceFrame -Value 'fake-png-bytes' -Encoding utf8
    $dvStagingFrame = Join-Path $dvFrameTempDir 'deadverb-staging.png'
    $dvFinalFrame = Join-Path $dvFrameTempDir 'turn-007.png'

    $dvStagedOk = Save-ProvisionalDeadVerbFrame -SourcePath $dvSourceFrame -StagingPath $dvStagingFrame
    Check ($dvStagedOk -eq $true) 'staging an existing source frame must return true'
    Check (Test-Path $dvStagingFrame) 'staging must actually copy the file to the staging path'

    $dvMissingSourceStaged = Save-ProvisionalDeadVerbFrame -SourcePath (Join-Path $dvFrameTempDir 'no-such-frame.png') -StagingPath (Join-Path $dvFrameTempDir 'never-created.png')
    Check ($dvMissingSourceStaged -eq $false) 'staging a MISSING source frame must return false, not throw'
    Check (-not (Test-Path (Join-Path $dvFrameTempDir 'never-created.png'))) 'a failed staging attempt must not create a destination file'

    $dvResolvedKept = Resolve-ProvisionalDeadVerbFrame -StagingPath $dvStagingFrame -FinalPath $dvFinalFrame -IsCandidate $true
    Check ($dvResolvedKept -eq $true) 'resolving a CANDIDATE turn must promote the staged frame and return true'
    Check (Test-Path $dvFinalFrame) 'a promoted frame must land at its final turn-NNN.png path'
    Check (-not (Test-Path $dvStagingFrame)) 'a promoted frame must not leave the staging copy behind'

    # Re-stage, then resolve as NOT a candidate: the staged frame must be discarded, never promoted.
    Set-Content -Path $dvSourceFrame -Value 'fake-png-bytes-2' -Encoding utf8
    [void](Save-ProvisionalDeadVerbFrame -SourcePath $dvSourceFrame -StagingPath $dvStagingFrame)
    $dvFinalFrame2 = Join-Path $dvFrameTempDir 'turn-008.png'
    $dvResolvedDiscarded = Resolve-ProvisionalDeadVerbFrame -StagingPath $dvStagingFrame -FinalPath $dvFinalFrame2 -IsCandidate $false
    Check ($dvResolvedDiscarded -eq $false) 'resolving a NON-candidate turn must return false (nothing promoted)'
    Check (-not (Test-Path $dvFinalFrame2)) 'a non-candidate turn must never produce a final kept-frame file'
    Check (-not (Test-Path $dvStagingFrame)) 'a non-candidate turn''s staged frame must be deleted, not left as an orphan'

    # Resolving with no staged file at all (the common case -- most press turns never needed staging)
    # must be a quiet no-op, never a throw.
    $dvResolvedNothingStaged = $null
    $dvResolvedNothingStagedThrew = $false
    try { $dvResolvedNothingStaged = Resolve-ProvisionalDeadVerbFrame -StagingPath (Join-Path $dvFrameTempDir 'nothing-here.png') -FinalPath (Join-Path $dvFrameTempDir 'turn-009.png') -IsCandidate $true } catch { $dvResolvedNothingStagedThrew = $true }
    Check ($dvResolvedNothingStagedThrew -eq $false) 'resolving with no staged file present must not throw'
    Check ($dvResolvedNothingStaged -eq $false) 'resolving with no staged file present must return false'
} finally {
    Remove-Item -Path $dvFrameTempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Driver wiring: the detector must be reachable from agent-playtest.ps1 itself. Sceptic may still
# be NAMED in explanatory comments (why it was retired, ruling 6) -- what must be gone is it
# FUNCTIONING as a persona, which the census/list checks below assert precisely rather than banning
# the word outright (a blanket text-contains ban would also fail on this file's own honest "sceptic
# is RETIRED" doc comment).
Check ($agentPlaytestRawText -like '*Get-DeadVerbVerdict*') 'agent-playtest.ps1 must call Get-DeadVerbVerdict somewhere in its turn loop'
Check ($agentPlaytestRawText -like '*Get-StateFingerprint*') 'agent-playtest.ps1 must call Get-StateFingerprint somewhere in its turn loop'
Check ($agentPlaytestRawText -like '*deadverb.ps1*') 'agent-playtest.ps1 must dot-source deadverb.ps1'
Check ($script:KnownPersonas -notcontains 'sceptic') 'personas.ps1''s $script:KnownPersonas must not contain sceptic any more'
Check ($script:KnownPersonas.Count -eq 7) ('personas.ps1''s known-persona roster must be exactly 7 (W4 landed monkey and attached, S2 landed pilot), got ' + $script:KnownPersonas.Count + ': ' + ($script:KnownPersonas -join ', '))
Check ($script:KnownPersonas -contains 'monkey') 'personas.ps1''s $script:KnownPersonas must contain monkey (W4)'
Check ($script:KnownPersonas -contains 'attached') 'personas.ps1''s $script:KnownPersonas must contain attached (W4)'
Check ($script:KnownPersonas -contains 'pilot') 'personas.ps1''s $script:KnownPersonas must contain pilot (S2, scripted-deep-pilot lane)'
$sweepRawText = Get-Content (Join-Path $toolsDir 'playtest-sweep.ps1') -Raw
Check ($sweepRawText -notlike '*''sceptic''*') 'playtest-sweep.ps1''s default -Personas array must not contain the string literal ''sceptic'' any more (it would pass a rejected persona straight to the driver)'

# --- 13. Mechanical fun metrics (W2, docs/plans/2026-08-10-002 "the playtest becomes a player") -----
. (Join-Path $toolsDir 'agent-playtest\metrics.ps1')

# --- 12a. Per-day action entropy: hand-computed 2-day fixture ------------------------------------
# Day 1: TurnRecords contribute 2x "press" + 1x "advance" (3 items); playtest-log.jsonl's own
# "action" rows (BackendActionRows) contribute 1x "BuyMaterialAction" -- 4 items total, counts
# press=2/advance=1/BuyMaterialAction=1, probabilities 0.5/0.25/0.25.
#   H = -(0.5*log2(0.5) + 0.25*log2(0.25) + 0.25*log2(0.25)) = -(0.5*-1 + 0.25*-2 + 0.25*-2) = 1.5 bits
# Day 2: TurnRecords contribute 4x "advance", BackendActionRows contributes nothing for day 2 -- a
# single action type, so entropy is exactly 0 (log2(1) = 0). Both values are exact (no floating
# rounding involved: log2(0.5)=-1, log2(0.25)=-2, log2(1)=0 are all exact powers of two).
$entropyTurnRecords = @(
    [pscustomobject]@{ Day = 1; Action = 'press' }
    [pscustomobject]@{ Day = 1; Action = 'press' }
    [pscustomobject]@{ Day = 1; Action = 'advance' }
    [pscustomobject]@{ Day = 2; Action = 'advance' }
    [pscustomobject]@{ Day = 2; Action = 'advance' }
    [pscustomobject]@{ Day = 2; Action = 'advance' }
    [pscustomobject]@{ Day = 2; Action = 'advance' }
)
$entropyBackendRows = @(
    [pscustomobject]@{ day = 1; action = 'BuyMaterialAction' }
)
$entropyResult = Get-PerDayActionEntropy -TurnRecords $entropyTurnRecords -BackendActionRows $entropyBackendRows
Check ($entropyResult.Count -eq 2) ('entropy fixture must produce exactly 2 day rows, got ' + $entropyResult.Count)
$day1Entropy = $entropyResult | Where-Object { $_.Day -eq '1' } | Select-Object -First 1
$day2Entropy = $entropyResult | Where-Object { $_.Day -eq '2' } | Select-Object -First 1
Check ($null -ne $day1Entropy) 'entropy fixture must include a day 1 row'
Check ($null -ne $day2Entropy) 'entropy fixture must include a day 2 row'
if ($day1Entropy) {
    Check ($day1Entropy.TotalActions -eq 4) ('day 1 TotalActions must be 4 (3 TurnRecords + 1 backend action row), got ' + $day1Entropy.TotalActions)
    Check ($day1Entropy.DistinctActionTypes -eq 3) ('day 1 must have 3 distinct action types, got ' + $day1Entropy.DistinctActionTypes)
    Check ($day1Entropy.EntropyBits -eq 1.5) ('day 1 entropy must be EXACTLY 1.5 bits (hand-computed), got ' + $day1Entropy.EntropyBits)
}
if ($day2Entropy) {
    Check ($day2Entropy.TotalActions -eq 4) ('day 2 TotalActions must be 4 (all TurnRecords, no backend rows for day 2), got ' + $day2Entropy.TotalActions)
    Check ($day2Entropy.DistinctActionTypes -eq 1) ('day 2 must have exactly 1 distinct action type (all advance), got ' + $day2Entropy.DistinctActionTypes)
    Check ($day2Entropy.EntropyBits -eq 0) ('day 2 entropy must be EXACTLY 0 bits (a single repeated action has zero entropy), got ' + $day2Entropy.EntropyBits)
}

# Degenerate input: no TurnRecords and no BackendActionRows at all must not throw, and must produce
# zero day rows -- never a phantom day.
$emptyEntropy = Get-PerDayActionEntropy -TurnRecords @() -BackendActionRows @()
Check (@($emptyEntropy).Count -eq 0) ('entropy over zero input must produce zero day rows, got ' + (@($emptyEntropy).Count))

# --- 12b. LEGAL-vs-CHOSEN ratio per phase: exact counts ------------------------------------------
# Morning: two turns, both press, enabled controls {A,B,C} both times; turn 1 presses A, turn 2
# presses B -- legal=3 (A,B,C), chosen=2 (A,B), ratio exactly 2/3.
# Evening: one turn, enabled controls {D}, action=advance (no target) -- legal=1, chosen=0, ratio 0.
$ratioTurnRecords = @(
    [pscustomobject]@{ Phase = 'Morning'; Action = 'press'; Target = 'A'; EnabledControls = @('A', 'B', 'C') }
    [pscustomobject]@{ Phase = 'Morning'; Action = 'press'; Target = 'B'; EnabledControls = @('A', 'B', 'C') }
    [pscustomobject]@{ Phase = 'Evening'; Action = 'advance'; Target = $null; EnabledControls = @('D') }
)
$ratioResult = Get-LegalVsChosenByPhase -TurnRecords $ratioTurnRecords
Check ($ratioResult.Count -eq 2) ('ratio fixture must produce exactly 2 phase rows, got ' + $ratioResult.Count)
$morningRatio = $ratioResult | Where-Object { $_.Phase -eq 'Morning' } | Select-Object -First 1
$eveningRatio = $ratioResult | Where-Object { $_.Phase -eq 'Evening' } | Select-Object -First 1
if ($morningRatio) {
    Check ($morningRatio.LegalCount -eq 3) ('Morning LegalCount must be 3, got ' + $morningRatio.LegalCount)
    Check ($morningRatio.ChosenCount -eq 2) ('Morning ChosenCount must be 2, got ' + $morningRatio.ChosenCount)
    Check ([Math]::Abs($morningRatio.Ratio - (2.0 / 3.0)) -lt 0.0001) ('Morning Ratio must be exactly 2/3, got ' + $morningRatio.Ratio)
    Check ($morningRatio.RatioPct -eq 66.7) ('Morning RatioPct must be 66.7, got ' + $morningRatio.RatioPct)
}
if ($eveningRatio) {
    Check ($eveningRatio.LegalCount -eq 1) ('Evening LegalCount must be 1, got ' + $eveningRatio.LegalCount)
    Check ($eveningRatio.ChosenCount -eq 0) ('Evening ChosenCount must be 0, got ' + $eveningRatio.ChosenCount)
    Check ($eveningRatio.Ratio -eq 0) ('Evening Ratio must be exactly 0, got ' + $eveningRatio.Ratio)
}
# A phase with zero legal controls ever seen must report ratio 0, not divide-by-zero/crash.
$zeroLegalResult = Get-LegalVsChosenByPhase -TurnRecords @([pscustomobject]@{ Phase = 'Void'; Action = 'advance'; Target = $null; EnabledControls = @() })
Check ($zeroLegalResult[0].Ratio -eq 0) 'a phase with zero legal controls ever seen must report ratio 0, not throw'

# --- 12c. Refusal control name parsing (the Reason-string -> control-name bridge) ----------------
Check ((Get-RefusalControlFromReason 'disabled/absent control: NoSuchButton_xyz') -eq 'NoSuchButton_xyz') 'must extract the control name from a disabled/absent-control reason'
Check ((Get-RefusalControlFromReason 'illegal key target: "climb" (must be interact or cancel)') -eq 'climb') 'must extract the target from an illegal-key-target reason'
Check ((Get-RefusalControlFromReason 'illegal/missing move dir: "" (must be up/down/left/right or a "+"-joined composite)') -eq '(move: no/empty dir)') 'an empty captured dir must map to a NAMED fallback, not a blank map row'
Check ((Get-RefusalControlFromReason 'empty reply') -eq '(unspecified)') 'a reason this file does not recognize must map to (unspecified)'
Check ((Get-RefusalControlFromReason '') -eq '(unspecified)') 'an empty/absent reason must map to (unspecified), not throw'

# eyes-learn-labels wave (U1): two new Reason shapes Get-LegalCommandFromReply's press branch can now
# emit (label resolution failures) -- both must map to NAMED fallbacks, never (unspecified).
Check ((Get-RefusalControlFromReason 'empty press target -- enabled controls: Alpha, Bravo') -eq '(press: no/empty target)') 'an empty-press-target reason must map to the same named fallback as the pre-existing blank-name case'
Check ((Get-RefusalControlFromReason 'ambiguous label "Buy" matches 2 enabled controls: BuyOre_1_copper, BuyOre_3_copper') -eq '(ambiguous label: "Buy")') 'an ambiguous-label reason must map to a NAMED fallback carrying the attempted label, not (unspecified)'

# --- 12d. Refusals-by-control frustration map: exact counts, both sources combined ---------------
# NoSuchButton_xyz refused twice by the driver (pre-send); BuyMaterialAction rejected once by the
# kernel (backend); OtherBtn refused once by the driver. Ranked by total count descending, ties
# broken alphabetically (BuyMaterialAction and OtherBtn both total 1 -- "B" sorts before "O").
$frustrationPreRefusals = @(
    [pscustomobject]@{ Control = 'NoSuchButton_xyz'; Reason = 'disabled/absent control: NoSuchButton_xyz' }
    [pscustomobject]@{ Control = 'NoSuchButton_xyz'; Reason = 'disabled/absent control: NoSuchButton_xyz' }
    [pscustomobject]@{ Control = 'OtherBtn'; Reason = 'disabled/absent control: OtherBtn' }
)
$frustrationBackendRejections = @(
    [pscustomobject]@{ Day = 1; Phase = 'Morning'; Action = 'BuyMaterialAction'; Why = 'insufficient gold' }
)
$frustrationResult = Get-RefusalFrustrationMap -PreRefusals $frustrationPreRefusals -BackendRejections $frustrationBackendRejections
Check ($frustrationResult.Count -eq 3) ('frustration map must produce exactly 3 rows, got ' + $frustrationResult.Count)
Check ($frustrationResult[0].Control -eq 'NoSuchButton_xyz') ('the top-ranked row must be NoSuchButton_xyz (count 2), got ' + $frustrationResult[0].Control)
Check ($frustrationResult[0].TotalCount -eq 2) ('NoSuchButton_xyz total must be 2, got ' + $frustrationResult[0].TotalCount)
Check ($frustrationResult[0].PreRefusedCount -eq 2) ('NoSuchButton_xyz must be all pre-refused (driver-side), got ' + $frustrationResult[0].PreRefusedCount)
Check ($frustrationResult[1].Control -eq 'BuyMaterialAction') ('the tie-break must sort BuyMaterialAction before OtherBtn alphabetically, got ' + $frustrationResult[1].Control)
Check ($frustrationResult[1].BackendRejectedCount -eq 1) ('BuyMaterialAction must be backend-rejected once, got ' + $frustrationResult[1].BackendRejectedCount)
Check ($frustrationResult[2].Control -eq 'OtherBtn') ('the third row must be OtherBtn, got ' + $frustrationResult[2].Control)

# Degenerate: zero refusals of any kind must produce zero rows, not throw.
$emptyFrustration = Get-RefusalFrustrationMap -PreRefusals @() -BackendRejections @()
Check (@($emptyFrustration).Count -eq 0) ('zero refusals of any kind must produce zero frustration-map rows, got ' + (@($emptyFrustration).Count))

# --- 12e. Product-sentence counter: U2 (eyes-learn-labels wave) gates ProductSentenceFired on a
# REAL BACKEND NOTE HIT, never a screen-only regex match -- the exact defect the campaign found live:
# 33 of 34 runs read True from a keyword hit on RIVAL DIALOGUE ("signed...") while the backend note
# scan was 0-hits in every one of those runs. A screen-only hit is now WEAK, never a bare True. -----

# Backend HAS a real note hit, screen ALSO shows it -- the genuine positive case: True, CONFIRMED.
$bothHitBackendSummary = [pscustomobject]@{
    Available           = $true
    AttributionNoteHits = @('gossip: word of the MakersMark blade is spreading')
    AttributionCaveat   = 'a tick row records only a COUNT of events -- this log CANNOT directly prove an AttributionBeatEvent fired. 1 hit(s) above -- treat zero hits as "the log cannot tell you", not "nothing named the player''s work."'
}
$bothHitScreenText = @(
    'Welcome to the shop.',
    'Legend: Emberbite''s MakersMark blade turned the killing blow on floor 3. Torvald lives.'
)
$bothHitReport = Get-ProductSentenceReport -BackendSummary $bothHitBackendSummary -ScreenTextHistory $bothHitScreenText
Check ($bothHitReport.ProductSentenceFired -eq $true) 'a REAL backend note hit must fire the product-sentence counter'
Check ($bothHitReport.PlayerScreenShowedIt -eq $true) 'PlayerScreenShowedIt must be true when a screenText line ALSO matches'
Check ($bothHitReport.Verdict -eq 'CONFIRMED') ('a backend hit (with or without a screen hit) must verdict CONFIRMED, got [' + $bothHitReport.Verdict + ']')
Check (@($bothHitReport.ScreenTextHits).Count -eq 1) ('exactly 1 screenText hit expected, got ' + @($bothHitReport.ScreenTextHits).Count)
Check ($bothHitReport.ScreenTextHits[0] -like '*MakersMark*') 'the recorded hit must be the actual matching line, not a placeholder'

# Backend HAS a real note hit, screen shows NOTHING -- still True/CONFIRMED (the backend is the
# signal of record; the screen scan is a separate, best-effort observation, not a gate on top of it).
$backendOnlyReport = Get-ProductSentenceReport -BackendSummary $bothHitBackendSummary -ScreenTextHistory @('gold 100', 'welcome')
Check ($backendOnlyReport.ProductSentenceFired -eq $true) 'a backend hit alone (no screen hit) must still fire True'
Check ($backendOnlyReport.PlayerScreenShowedIt -eq $false) 'PlayerScreenShowedIt must be false when no screenText line matches'
Check ($backendOnlyReport.Verdict -eq 'CONFIRMED') 'a backend hit alone must still verdict CONFIRMED'

# 2026-08-11 (backend-log-sees-the-spine): Backend HAS an eventTypes hit but ZERO note hits -- the
# OR-gate this unit adds. Before this, only a note-scan hit could fire True; now the exact
# eventTypes signal (a tick row naming AttributionBeatEvent by type, not a text guess) must ALSO
# fire True/CONFIRMED on its own, with no note-scan hit needed at all.
$eventTypeOnlyBackendSummary = [pscustomobject]@{
    Available                = $true
    AttributionNoteHits      = @()
    AttributionEventTypeHits = @([pscustomobject]@{ T = 10.0; Day = 1; Phase = 'Evening' })
    AttributionCaveat        = 'a tick row now carries both a COUNT of events and the DISTINCT event TYPE NAMES that fired that tick -- so THIS log can directly prove an AttributionBeatEvent fired: 1 tick row(s) named it.'
}
$eventTypeOnlyReport = Get-ProductSentenceReport -BackendSummary $eventTypeOnlyBackendSummary -ScreenTextHistory @('gold 100', 'welcome')
Check ($eventTypeOnlyReport.ProductSentenceFired -eq $true) 'an eventTypes hit ALONE (zero note hits) must fire the product-sentence counter True'
Check ($eventTypeOnlyReport.AttributionBeatNamed -eq $true) 'an eventTypes hit alone must report AttributionBeatNamed=true'
Check ($eventTypeOnlyReport.Verdict -eq 'CONFIRMED') ('an eventTypes hit alone must verdict CONFIRMED, got [' + $eventTypeOnlyReport.Verdict + ']')
Check (@($eventTypeOnlyReport.AttributionEventTypeHits).Count -eq 1) ('the report must carry the eventTypes hit through, got ' + @($eventTypeOnlyReport.AttributionEventTypeHits).Count)
Check (@($eventTypeOnlyReport.AttributionNoteHits).Count -eq 0) 'the report must still show zero note hits -- the two signals stay independently visible'

# THE REGRESSION PIN: screen shows an attribution-shaped line, but the backend log is AVAILABLE and
# SILENT (zero note hits) -- this is exactly the 33/34-run false-positive shape. Must be False in
# metrics.json (ProductSentenceFired) and WEAK in findings.md (Verdict), never a bare True.
$screenOnlyBackendSummary = [pscustomobject]@{
    Available           = $true
    AttributionNoteHits = @()
    AttributionCaveat   = 'a tick row records only a COUNT of events -- this log CANNOT directly prove an AttributionBeatEvent fired. 0 hit(s) above -- treat zero hits as "the log cannot tell you", not "nothing named the player''s work."'
}
$screenOnlyReport = Get-ProductSentenceReport -BackendSummary $screenOnlyBackendSummary -ScreenTextHistory $bothHitScreenText
Check ($screenOnlyReport.ProductSentenceFired -eq $false) 'THE REGRESSION PIN: a screen-only hit with the backend SILENT must NOT fire (ProductSentenceFired=False)'
Check ($screenOnlyReport.PlayerScreenShowedIt -eq $true) 'PlayerScreenShowedIt must still be true -- the screen signal itself is not suppressed, only kept separate from Fired'
Check ($screenOnlyReport.Verdict -eq 'WEAK (screen text only, backend silent)') ('a screen-only hit with the backend silent must verdict exactly "WEAK (screen text only, backend silent)", got [' + $screenOnlyReport.Verdict + ']')
Check ($screenOnlyReport.ScreenTextCaveat -like '*WEAK*') 'the screenText caveat itself must also warn that a screen-only hit is weak'

# Zero hits on either side: False, NOT SEEN.
$zeroBackendSummary = [pscustomobject]@{
    Available           = $true
    AttributionNoteHits = @()
    AttributionCaveat   = 'a tick row records only a COUNT of events -- this log CANNOT directly prove an AttributionBeatEvent fired. 0 hit(s) above -- treat zero hits as "the log cannot tell you", not "nothing named the player''s work."'
}
$zeroReport = Get-ProductSentenceReport -BackendSummary $zeroBackendSummary -ScreenTextHistory @('gold 100', 'welcome')
Check ($zeroReport.ProductSentenceFired -eq $false) 'zero hits on either side must not fire the counter'
Check ($zeroReport.AttributionBeatNamed -eq $false) 'zero backend note hits must report AttributionBeatNamed=false'
Check ($zeroReport.Verdict -eq 'NOT SEEN') ('zero hits on either side must verdict NOT SEEN, got [' + $zeroReport.Verdict + ']')
Check ($zeroReport.AttributionCaveat -like '*the log cannot tell you*') ('on zero hits, the backend caveat must be carried through VERBATIM, including "the log cannot tell you" -- got [' + $zeroReport.AttributionCaveat + ']')
Check ($zeroReport.ScreenTextCaveat -like '*the log cannot tell you*') 'the screenText-side caveat must ALSO say "the log cannot tell you" on zero hits, not silently omit it'

# A missing/unavailable backend summary must not crash, and must say the beat is UNKNOWN, not "false".
$noBackendReport = Get-ProductSentenceReport -BackendSummary ([pscustomobject]@{ Available = $false }) -ScreenTextHistory @()
Check ($noBackendReport.AttributionBeatNamed -eq $false) 'an unavailable backend summary must report AttributionBeatNamed=false (not crash)'
Check ($noBackendReport.ProductSentenceFired -eq $false) 'an unavailable backend summary must never fire True'
Check ($noBackendReport.Verdict -eq 'NOT SEEN') 'an unavailable backend summary with no screen hit either must verdict NOT SEEN'
Check ($noBackendReport.AttributionCaveat -like '*UNKNOWN*') 'an unavailable backend summary caveat must say the attribution beat is UNKNOWN, not silently claim "no"'

# Screen hit + backend UNAVAILABLE (not silent -- genuinely unknown): still WEAK, still False, but a
# DIFFERENT wording than the "backend silent" case -- unknown must never be conflated with "checked
# and found nothing" (the same discipline AttributionCaveat's own UNKNOWN wording already requires).
$screenOnlyNoBackendReport = Get-ProductSentenceReport -BackendSummary ([pscustomobject]@{ Available = $false }) -ScreenTextHistory $bothHitScreenText
Check ($screenOnlyNoBackendReport.ProductSentenceFired -eq $false) 'a screen hit with no backend log at all must not fire True'
Check ($screenOnlyNoBackendReport.Verdict -eq 'WEAK (screen text only, backend unavailable)') ('a screen hit with an UNAVAILABLE backend must verdict differently than a SILENT one, got [' + $screenOnlyNoBackendReport.Verdict + ']')

# --- 12f. Get-MetricsSummary + Format-MetricsMarkdown: the combined caller-facing shape ----------
$combinedBackendSummary = [pscustomobject]@{
    Available           = $true
    ActionRows          = @([pscustomobject]@{ day = 1; action = 'BuyMaterialAction' })
    Rejections          = @([pscustomobject]@{ Day = 1; Phase = 'Morning'; Action = 'BuyMaterialAction'; Why = 'insufficient gold' })
    AttributionNoteHits = @()
    AttributionCaveat   = 'treat zero hits as "the log cannot tell you"'
}
$combinedTurnRecords = @(
    [pscustomobject]@{ Turn = 1; Day = 1; Phase = 'Morning'; Action = 'press'; Target = 'A'; Why = 'test'; Outcome = 'ok'; ScreenText = @('hello'); EnabledControls = @('A', 'B') }
)
$combinedPreRefusals = @([pscustomobject]@{ Control = 'NoSuchButton_xyz'; Reason = 'disabled/absent control: NoSuchButton_xyz' })
$combinedMetrics = Get-MetricsSummary -TurnRecords $combinedTurnRecords -PreRefusals $combinedPreRefusals -BackendSummary $combinedBackendSummary
Check ($combinedMetrics.PerDayEntropy.Count -eq 1) 'Get-MetricsSummary must combine TurnRecords + BackendSummary.ActionRows into per-day entropy'
Check ($combinedMetrics.RefusalFrustrationMap.Count -eq 2) 'Get-MetricsSummary must combine PreRefusals + BackendSummary.Rejections into the frustration map'
$combinedMarkdown = Format-MetricsMarkdown -Metrics $combinedMetrics
Check ($combinedMarkdown -like '*Mechanical fun metrics*') 'Format-MetricsMarkdown must produce the "Mechanical fun metrics" heading'
Check ($combinedMarkdown -like '*Product-sentence counter*') 'Format-MetricsMarkdown must include the product-sentence section'
Check ($combinedMarkdown -like '*Per-day action entropy*') 'Format-MetricsMarkdown must include the per-day entropy table'
Check ($combinedMarkdown -like '*LEGAL-vs-CHOSEN*') 'Format-MetricsMarkdown must include the legal-vs-chosen section'
Check ($combinedMarkdown -like '*frustration map*') 'Format-MetricsMarkdown must include the frustration map section'
Check ($combinedMarkdown -like '*VERDICT:*') 'Format-MetricsMarkdown must print the product-sentence VERDICT line (U2, eyes-learn-labels)'

# --- 12f-2. Format-DigestTurnLine: the Day-chip pairing fix (U2, eyes-learn-labels wave) ----------
# THE REGRESSION PIN: a real captured state.json's screenText begins ["Day", "2", ...] -- the HUD's
# Day chip renders as two adjacent Label nodes, always first. The old blind First-2-joined-with-';'
# slice quoted a phantom "Day; 2" no player ever saw; the fix pairs them with a space.
$dayChipTurn = [pscustomobject]@{
    Turn = 4; Phase = 'Morning'; Action = 'advance'; Target = $null; Why = 'tick'; Outcome = 'advanced'
    ScreenText = @('Day', '2', 'Gold', '100'); Refused = $false; RefusalReason = ''
}
$dayChipLine = Format-DigestTurnLine $dayChipTurn
Check ($dayChipLine -like '*Day 2*') ('THE REGRESSION PIN: the Day chip must render as "Day 2", got [' + $dayChipLine + ']')
Check ($dayChipLine -notlike '*Day; 2*') ('the Day chip must NEVER render as "Day; 2", got [' + $dayChipLine + ']')
Check ($dayChipLine -like '*Gold; 100*') 'entries beyond the Day-chip pair must keep the ordinary "; "-joined preview'

# A single-entry screenText (no pair to make) must not crash and must show the one entry as-is.
$oneEntryTurn = [pscustomobject]@{
    Turn = 1; Phase = 'Morning'; Action = 'advance'; Target = $null; Why = 't'; Outcome = 'ok'
    ScreenText = @('Welcome'); Refused = $false; RefusalReason = ''
}
$oneEntryLine = Format-DigestTurnLine $oneEntryTurn
Check ($oneEntryLine -like '*screen: Welcome*') ('a single-entry screenText must render as-is, got [' + $oneEntryLine + ']')

# Zero-entry screenText: no "screen:" segment at all (unchanged behavior).
$zeroEntryTurn = [pscustomobject]@{
    Turn = 1; Phase = 'Morning'; Action = 'advance'; Target = $null; Why = 't'; Outcome = 'ok'
    ScreenText = @(); Refused = $false; RefusalReason = ''
}
$zeroEntryLine = Format-DigestTurnLine $zeroEntryTurn
Check ($zeroEntryLine -notlike '*screen:*') 'zero screenText entries must produce no "screen:" segment at all'

# --- 12f-3. Get-FallbackCloseControl (U2, eyes-learn-labels wave) --------------------------------
# An overlay-owning close control (name starting "Close") among the enabled list must be found;
# absent one, the function must return nothing (never throw, never guess a non-Close control).
Check ((Get-FallbackCloseControl -EnabledControls @('AdvancePhase', 'CloseLedger', 'OpenShop')) -eq 'CloseLedger') 'a "Close"-prefixed enabled control must be found regardless of position in the list'
Check ($null -eq (Get-FallbackCloseControl -EnabledControls @('AdvancePhase', 'OpenShop'))) 'with no Close-prefixed control enabled, the function must return nothing (null), never guess'
Check ($null -eq (Get-FallbackCloseControl -EnabledControls @())) 'an empty enabled-control list must return nothing, never throw'
$twoCloseControls = Get-FallbackCloseControl -EnabledControls @('CloseLedger', 'CloseShop')
Check ($twoCloseControls -eq 'CloseLedger') 'with two Close-prefixed controls, the FIRST in the caller''s own array order must be chosen deterministically'

# agent-playtest.ps1 itself must actually call Get-FallbackCloseControl in its fallback path, and the
# unconditional advance-only fallback string must no longer be the ONLY fallback shape.
$agentPlaytestRawTextForFallback = Get-Content (Join-Path $toolsDir 'agent-playtest.ps1') -Raw
Check ($agentPlaytestRawTextForFallback -like '*Get-FallbackCloseControl*') 'agent-playtest.ps1 must call Get-FallbackCloseControl in its fallback path'
Check ($agentPlaytestRawTextForFallback -like '*an overlay owns the screen*') 'agent-playtest.ps1''s fallback path must press the close control (not advance) when an overlay owns the screen, logged as such'

# --- 12f-4. Setup-command "why" text gets the [setup] prefix in the judge digest (U2) -------------
# agent-playtest.ps1 must prefix a scenario Setup-replayed turn's Why text with "[setup] " before it
# ever reaches TurnRecords -- Format-DigestTurnLine puts .Why straight into the judge line with no
# other place to distinguish a QA comment ("safety margin 2") from a model's own stated reasoning.
# NOTE: -like's "[...]" is a character CLASS, not literal brackets -- .Contains() is used for these
# two checks instead, since the text being searched for genuinely contains literal square brackets.
Check ($agentPlaytestRawTextForFallback.Contains("'[setup] ' + ")) 'agent-playtest.ps1 must prefix a scenario-Setup turn''s Why text with "[setup] " before recording it'
$setupWhyLine = Format-DigestTurnLine ([pscustomobject]@{
    Turn = 8; Phase = 'Camp'; Action = 'advance'; Target = $null; Why = '[setup] safety margin 2'; Outcome = 'advanced'
    ScreenText = @(); Refused = $false; RefusalReason = ''
})
Check ($setupWhyLine.Contains('([setup] safety margin 2)')) ('a setup-prefixed why must render distinguishably in the judge digest line, got [' + $setupWhyLine + ']')

# --- 12g. Per-day judge digest: the front-trim regression pin ------------------------------------
# THE required proof (Verification Contract, docs/plans/2026-08-10-002): a per-day digest of a
# 3-day fixture contains ALL THREE days, even forced through aggressive thinning by an artificially
# tiny -MaxChars -- the exact regression the old $judgeCap tail-trim could never pass (trimming from
# the front means only the LAST day would ever survive a small enough cap).
$digestTurnRecords = New-Object System.Collections.ArrayList
$digestTurnCounter = 0
foreach ($digestDay in 1, 2, 3) {
    for ($i = 1; $i -le 12; $i++) {
        $digestTurnCounter++
        [void]$digestTurnRecords.Add([pscustomobject]@{
            Turn          = $digestTurnCounter
            Day           = $digestDay
            Phase         = 'Morning'
            Action        = 'advance'
            Target        = $null
            Why           = 'padding this line out so the fixture is big enough to force real thinning'
            Outcome       = 'advanced -> day ' + $digestDay
            ScreenText    = @('some screen text padding the digest size for the thinning test')
            Refused       = $false
            RefusalReason = ''
        })
    }
}
$tinyDigest = Build-PerDayJudgeDigest -TurnRecords @($digestTurnRecords) -MaxChars 800
Check ($tinyDigest.DayCount -eq 3) ('the 3-day fixture must report DayCount=3, got ' + $tinyDigest.DayCount)
Check ($tinyDigest.Thinned -eq $true) 'an 800-char budget against a 36-turn/3-day fixture must have triggered thinning'
Check ($tinyDigest.Text -like '*Day 1*') 'THE REGRESSION PIN: the digest must still contain Day 1 even after aggressive thinning'
Check ($tinyDigest.Text -like '*Day 2*') 'THE REGRESSION PIN: the digest must still contain Day 2 even after aggressive thinning'
Check ($tinyDigest.Text -like '*Day 3*') 'THE REGRESSION PIN: the digest must still contain Day 3 even after aggressive thinning'
Check ($tinyDigest.Text -like '*omitted for length*') 'a thinned day must say so explicitly, not silently drop turns with no trace'

# A budget large enough to hold everything must NOT thin at all -- proves the function does not thin
# unconditionally just because it CAN.
$roomyDigest = Build-PerDayJudgeDigest -TurnRecords @($digestTurnRecords) -MaxChars 1000000
Check ($roomyDigest.Thinned -eq $false) 'a generous budget must not trigger thinning'
Check ($roomyDigest.Text -like '*turn 36*') 'an unthinned digest must include the LAST turn (turn 36), not just early ones'
Check ($roomyDigest.Text -like '*turn 1 *') 'an unthinned digest must include the FIRST turn'

# Degenerate: zero turn records must not throw, and must say so rather than returning an empty string
# that would silently look like a working (if empty) run.
$emptyDigest = Build-PerDayJudgeDigest -TurnRecords @() -MaxChars 24000
Check ($emptyDigest.DayCount -eq 0) 'zero turn records must report DayCount=0'
Check ($emptyDigest.Text -like '*no turns recorded*') 'zero turn records must say so in the digest text, not return a blank string'

# agent-playtest.ps1 itself must actually call the new digest builder in place of the old tail-trim.
$agentPlaytestRawTextForDigest = Get-Content (Join-Path $toolsDir 'agent-playtest.ps1') -Raw
Check ($agentPlaytestRawTextForDigest -like '*Build-PerDayJudgeDigest*') 'agent-playtest.ps1 must call Build-PerDayJudgeDigest for the judge input'
# The old variable's NAME may still appear in a regression-note comment (this repo's own convention --
# see backend.ps1's AllowEmptyCollection notes for the same pattern); what must actually be gone is the
# ASSIGNMENT that made it a live tail-trim cap.
Check ($agentPlaytestRawTextForDigest -notmatch '\$judgeCap\s*=') 'the old $judgeCap tail-trim ASSIGNMENT must be gone from agent-playtest.ps1 (a comment mentioning the old name by way of explanation is fine)'

# --- 14. Temperament meter (W4, docs/plans/2026-08-10-002 "the playtest becomes a player") -------
. (Join-Path $toolsDir 'agent-playtest\temperament.ps1')

Check ($script:TemperamentVersion -and $script:TemperamentVersion.Length -gt 0) 'TemperamentVersion must be a non-empty string'

$freshMeter = New-TemperamentMeter
Check ($freshMeter.Value -eq $script:PatienceStart) ('a fresh meter must start at PatienceStart (' + $script:PatienceStart + '), got ' + $freshMeter.Value)
Check ($freshMeter.Max -eq $script:PatienceStart) 'a fresh meter''s Max must equal PatienceStart'
Check ($freshMeter.Depleted -eq $false) 'a fresh meter must not be Depleted'
Check ($freshMeter.Version -eq $script:TemperamentVersion) 'a fresh meter must stamp the current TemperamentVersion'
Check (@($freshMeter.DrainHistory).Count -eq 0) 'a fresh meter must have an empty drain history'

$scaledMeter = New-TemperamentMeter -StartMultiplier 2.0
Check ($scaledMeter.Max -eq ($script:PatienceStart * 2.0)) ('-StartMultiplier must scale ONLY the start value, got Max=' + $scaledMeter.Max)

# THE required proof: a stubbed refusal sequence drains to quit with the EXACT lead-finding line.
# Sized deliberately to match the plan's own worked example (6 refusals at 3 each = 18 = default
# PatienceStart) so this test's numbers are not invented on top of the brief's own illustration.
$quitMeter = New-TemperamentMeter
for ($i = 1; $i -le 6; $i++) {
    Add-TemperamentDrain -Meter $quitMeter -Cause 'refusal' -Amount $script:PatienceDrainRefusal `
        -Turn (8 + $i) -Day 2 -Phase 'Morning' -Detail 'BountiesPanel'
}
Check ($quitMeter.Depleted -eq $true) ('6 refusals at ' + $script:PatienceDrainRefusal + ' each must deplete a default-' + $script:PatienceStart + ' meter, got Value=' + $quitMeter.Value)
Check ($quitMeter.Value -le 0) ('a depleted meter''s Value must be <= 0, got ' + $quitMeter.Value)

$quitFinding = Get-TemperamentQuitFinding -Meter $quitMeter -Turn 14 -Day 2 -Phase 'Morning'
$expectedQuitHeadline = 'quit day 2 Morning after 6 refusal(s) at BountiesPanel (turn 14)'
Check ($quitFinding.Headline -eq $expectedQuitHeadline) ('the quit headline must match exactly, expected [' + $expectedQuitHeadline + '] got [' + $quitFinding.Headline + ']')
Check (@($quitFinding.DrainHistory).Count -eq 6) ('the quit finding''s drain history must list all 6 refusals, got ' + @($quitFinding.DrainHistory).Count)
Check ($quitFinding.TemperamentVersion -eq $script:TemperamentVersion) 'the quit finding must carry the current TemperamentVersion'

# Mixed-cause headline: a stuck repeat with NO detail, plus two dead-verb candidates with a detail,
# on the SAME meter -- proves the headline (a) groups counts by cause rather than listing every
# individual drain, (b) sorts causes deterministically, and (c) only appends " at X" using the LAST
# detail seen, never blanking out once a later drain happens to have none.
$mixedMeter = New-TemperamentMeter
Add-TemperamentDrain -Meter $mixedMeter -Cause 'stuck' -Amount $script:PatienceDrainStuckRepeat -Turn 3 -Day 1 -Phase 'Evening' -Detail ''
Add-TemperamentDrain -Meter $mixedMeter -Cause 'deadverb' -Amount $script:PatienceDrainDeadVerbCandidate -Turn 4 -Day 1 -Phase 'Evening' -Detail 'OpenShop'
Add-TemperamentDrain -Meter $mixedMeter -Cause 'deadverb' -Amount $script:PatienceDrainDeadVerbCandidate -Turn 5 -Day 1 -Phase 'Evening' -Detail 'OpenShop'
Check ($mixedMeter.Depleted -eq $false) ('one stuck (' + $script:PatienceDrainStuckRepeat + ') plus two dead-verb (' + (2 * $script:PatienceDrainDeadVerbCandidate) + ') drains must not deplete an ' + $script:PatienceStart + '-start meter')
$mixedFinding = Get-TemperamentQuitFinding -Meter $mixedMeter -Turn 5 -Day 1 -Phase 'Evening'
Check ($mixedFinding.Headline -eq 'quit day 1 Evening after 2 dead-verb candidate(s), 1 stuck repeat(s) at OpenShop (turn 5)') ('mixed-cause headline must group by cause (alphabetical: deadverb before stuck) and use the LAST detail seen, got [' + $mixedFinding.Headline + ']')

# Novelty RESETS the meter -- not increments it. Drain it partway, then reset, and Value must land
# EXACTLY on Max (a full second wind), and Depleted must clear if it had been set.
$resetMeter = New-TemperamentMeter
Add-TemperamentDrain -Meter $resetMeter -Cause 'refusal' -Amount $script:PatienceDrainRefusal -Turn 1 -Day 1 -Phase 'Morning' -Detail 'X'
Check ($resetMeter.Value -lt $resetMeter.Max) 'sanity: the meter must actually be below Max before reset'
Reset-TemperamentMeter -Meter $resetMeter -Turn 2 -Day 1 -Phase 'Morning' -Surface 'coverage +1'
Check ($resetMeter.Value -eq $resetMeter.Max) ('a reset must land EXACTLY on Max, not merely increase -- got ' + $resetMeter.Value + ' vs Max ' + $resetMeter.Max)
Check ($resetMeter.Depleted -eq $false) 'a reset must clear Depleted'
$lastHistoryEntry = $resetMeter.DrainHistory[$resetMeter.DrainHistory.Count - 1]
Check ($lastHistoryEntry.Cause -eq 'reset') 'the reset must be recorded in DrainHistory with Cause=reset'
Check ($lastHistoryEntry.Amount -eq 0.0) 'a reset''s recorded Amount must be 0 (it is not a drain)'

# A depleted meter that gets reset is genuinely un-depleted, and Get-TemperamentQuitFinding after a
# reset only ever explains drains SINCE that reset -- proving the "walk back to the last reset" logic.
$depletedThenResetMeter = New-TemperamentMeter
for ($i = 1; $i -le 6; $i++) {
    Add-TemperamentDrain -Meter $depletedThenResetMeter -Cause 'refusal' -Amount $script:PatienceDrainRefusal -Turn $i -Day 1 -Phase 'Morning' -Detail 'A'
}
Check ($depletedThenResetMeter.Depleted -eq $true) 'sanity: 6 refusals must deplete this meter too'
Reset-TemperamentMeter -Meter $depletedThenResetMeter -Turn 7 -Day 1 -Phase 'Morning' -Surface 'coverage +1'
Check ($depletedThenResetMeter.Depleted -eq $false) 'resetting a depleted meter must un-deplete it'
Add-TemperamentDrain -Meter $depletedThenResetMeter -Cause 'stuck' -Amount $script:PatienceDrainStuckRepeat -Turn 8 -Day 1 -Phase 'Morning' -Detail 'town/Morning'
$postResetFinding = Get-TemperamentQuitFinding -Meter $depletedThenResetMeter -Turn 8 -Day 1 -Phase 'Morning'
Check (@($postResetFinding.DrainHistory).Count -eq 1) ('the quit finding after a reset must only count drains SINCE the reset, got ' + @($postResetFinding.DrainHistory).Count)

# The other ending: a budget-reached run must say so unambiguously, distinct from an exhausted one.
$budgetMeter = New-TemperamentMeter
Add-TemperamentDrain -Meter $budgetMeter -Cause 'refusal' -Amount $script:PatienceDrainRefusal -Turn 1 -Day 1 -Phase 'Morning' -Detail 'X'
$budgetNote = Get-TemperamentBudgetEndNote -Meter $budgetMeter
Check ($budgetNote -like 'budget reached, patience remaining*') ('the budget-end note must say "budget reached, patience remaining N", got [' + $budgetNote + ']')
Check ($budgetNote -notlike '*exhausted*') 'the budget-end note must never use exhaustion language -- the two endings must never be conflated'

# Format-TemperamentMarkdown: smoke check that both branches render (the real content is asserted
# above via the pure functions it calls).
$quitMarkdown = Format-TemperamentMarkdown -Meter $quitMeter -QuitFinding $quitFinding
Check ($quitMarkdown -like '*## Patience*') 'Format-TemperamentMarkdown must render a Patience heading'
Check ($quitMarkdown -like '*BountiesPanel*') 'Format-TemperamentMarkdown must render the quit headline''s detail'
$budgetMarkdown = Format-TemperamentMarkdown -Meter $budgetMeter -QuitFinding $null
Check ($budgetMarkdown -like '*budget reached*') 'Format-TemperamentMarkdown must render the budget-end note when the meter was never depleted'

# Get-CoverageTrackerTouchedCount: the driver's own novelty-detection hook, proven against
# coverage.ps1's REAL tracker shape (already dot-sourced in section 8 above).
$noveltyTracker = New-CoverageTracker
Check ((Get-CoverageTrackerTouchedCount -Tracker $noveltyTracker) -eq 0) 'a fresh coverage tracker must report zero touched surfaces'
$noveltyState = [pscustomobject]@{ location = 'town'; phase = 'Morning'; nearby = @([pscustomobject]@{ key = 'forge'; inRange = $true }) }
Add-CoverageTouch -Tracker $noveltyTracker -State $noveltyState -Command ([pscustomobject]@{ action = 'advance' })
$noveltyCountAfter = Get-CoverageTrackerTouchedCount -Tracker $noveltyTracker
Check ($noveltyCountAfter -gt 0) 'touching a new surface must increase the tracker''s total touched count (the driver''s own reset trigger)'

# agent-playtest.ps1 wiring: temperament version must reach the findings.md header, and every drain
# site must actually call Add-TemperamentDrain / Reset-TemperamentMeter.
Check ($agentPlaytestRawText -like '*temperament version*') 'agent-playtest.ps1''s header must include a temperament version line'
Check ($agentPlaytestRawText -like '*Add-TemperamentDrain*') 'agent-playtest.ps1 must call Add-TemperamentDrain'
Check ($agentPlaytestRawText -like '*Reset-TemperamentMeter*') 'agent-playtest.ps1 must call Reset-TemperamentMeter'
Check ($agentPlaytestRawText -like '*Get-TemperamentQuitFinding*') 'agent-playtest.ps1 must call Get-TemperamentQuitFinding'
Check ($agentPlaytestRawText -like '*PATIENCE EXHAUSTED*') 'agent-playtest.ps1 must render a PATIENCE EXHAUSTED lead banner'
Check ($agentPlaytestRawText -like '*-not $isMonkey*') 'agent-playtest.ps1 must gate something on -not $isMonkey (the temperament meter, the GPU gate, and the act-prompt build all depend on this)'

# --- 14a. Sweep patience (U3, "the playtest learns to finish" wave) --------------------------------
# Owner finding 2026-08-11: 58 of 58 model runs died on patience by day 3 -- a sweep meant to measure
# the REST of a long campaign needs the frustration recorded as a finding, never a fatality.

# Get-WouldHaveQuitMarker reuses Get-TemperamentQuitFinding's own drain-history walk -- the marker's
# Trigger text must be the SAME headline a real quit would have produced from the identical meter
# state (proven directly, not just smoke-checked).
$sweepMeter = New-TemperamentMeter
for ($i = 1; $i -le 6; $i++) {
    Add-TemperamentDrain -Meter $sweepMeter -Cause 'refusal' -Amount $script:PatienceDrainRefusal -Turn $i -Day 2 -Phase 'Morning' -Detail 'BountiesPanel'
}
Check ($sweepMeter.Depleted -eq $true) 'sanity: 6 refusals at the sized drain amount must deplete the fixture meter (mirrors the quit-fixture math above)'
$wouldHaveQuitMarker = Get-WouldHaveQuitMarker -Meter $sweepMeter -Turn 6 -Day 2 -Phase 'Morning'
Check ($wouldHaveQuitMarker.Turn -eq 6 -and $wouldHaveQuitMarker.Day -eq 2 -and $wouldHaveQuitMarker.Phase -eq 'Morning') 'Get-WouldHaveQuitMarker must carry the exact Turn/Day/Phase it was given'
$equivalentQuitFinding = Get-TemperamentQuitFinding -Meter $sweepMeter -Turn 6 -Day 2 -Phase 'Morning'
Check ($wouldHaveQuitMarker.Trigger -eq $equivalentQuitFinding.Headline) ('a would-have-quit marker''s Trigger must equal the real quit finding''s Headline for the same meter state, got marker=[' + $wouldHaveQuitMarker.Trigger + '] quit=[' + $equivalentQuitFinding.Headline + ']')

# Reset behaviour: after logging a marker, the caller resets the meter (agent-playtest.ps1's own
# loop does this) -- a reset meter must be un-depleted and able to drain-then-deplete AGAIN,
# producing a SECOND, independent marker rather than never firing twice.
Reset-TemperamentMeter -Meter $sweepMeter -Turn 6 -Day 2 -Phase 'Morning' -Surface 'patience reset after a would-have-quit marker (Sweep mode)'
Check ($sweepMeter.Depleted -eq $false) 'resetting after a would-have-quit marker must un-deplete the meter'
for ($i = 7; $i -le 12; $i++) {
    Add-TemperamentDrain -Meter $sweepMeter -Cause 'refusal' -Amount $script:PatienceDrainRefusal -Turn $i -Day 2 -Phase 'Evening' -Detail 'BountiesPanel'
}
Check ($sweepMeter.Depleted -eq $true) 'the SAME meter must be able to deplete a second time after a reset'
$secondMarker = Get-WouldHaveQuitMarker -Meter $sweepMeter -Turn 12 -Day 2 -Phase 'Evening'
Check ($secondMarker.Trigger -ne $wouldHaveQuitMarker.Trigger) 'a second marker after a reset must describe the drains SINCE the reset, not repeat the first marker''s text'

# Format-TemperamentMarkdown: -WouldHaveQuitMarkers is OPTIONAL -- every pre-existing call (the
# $quitMarkdown/$budgetMarkdown checks above, which never pass it) must keep rendering with NO
# "Would-have-quit" text at all.
Check ($quitMarkdown -notlike '*Would-have-quit*') 'with no -WouldHaveQuitMarkers passed, no would-have-quit section must appear (byte-identical Quit-mode rendering)'
$sweepMarkdown = Format-TemperamentMarkdown -Meter $budgetMeter -QuitFinding $null -WouldHaveQuitMarkers @($wouldHaveQuitMarker, $secondMarker)
Check ($sweepMarkdown -like '*Would-have-quit marker(s)*') 'with -WouldHaveQuitMarkers passed, the section must appear'
Check ($sweepMarkdown -like '*BountiesPanel*') 'the rendered would-have-quit section must carry the marker''s own trigger text'
Check (([regex]::Matches($sweepMarkdown, 'turn \d+ day \d+')).Count -ge 2) 'the rendered section must list BOTH markers, not just the last one'

# Driver wiring: -PatienceMode must exist, default to Quit, and the Sweep branch must actually call
# Get-WouldHaveQuitMarker and Reset-TemperamentMeter (not just declare the parameter and ignore it).
Check ($agentPlaytestRawText -like '*PatienceMode*') 'agent-playtest.ps1 must declare a -PatienceMode parameter'
Check ($agentPlaytestRawText -like '*$PatienceMode = ''Quit''*') 'agent-playtest.ps1''s -PatienceMode must default to Quit (today''s exact behaviour)'
Check ($agentPlaytestRawText -like '*Get-WouldHaveQuitMarker*') 'agent-playtest.ps1 must call Get-WouldHaveQuitMarker in its Sweep-mode branch'
Check ($agentPlaytestRawText -like '*wouldHaveQuitMarkers*') 'agent-playtest.ps1 must collect would-have-quit markers into its own list'

# playtest-sweep.ps1 wiring: must pass -PatienceMode Sweep by default (feature-detected, same
# pattern as -Persona) and expose a WouldHaveQuitTurns SUMMARY.csv column.
$playtestSweepRawText = Get-Content (Join-Path $toolsDir 'playtest-sweep.ps1') -Raw
Check ($playtestSweepRawText -like '*$PatienceMode = ''Sweep''*') 'playtest-sweep.ps1''s own -PatienceMode must default to Sweep'
Check ($playtestSweepRawText -like '*patienceModeSupported*') 'playtest-sweep.ps1 must feature-detect -PatienceMode support on the driver, same idiom as -Persona'
Check ($playtestSweepRawText -like '*WouldHaveQuitTurns*') 'playtest-sweep.ps1 must expose a WouldHaveQuitTurns column'
# The stale comment fix: the OLD wrong claim ("the screenText check... not the backend log") must be
# gone, and a corrected note must be present in its place.
Check ($playtestSweepRawText -notlike '*ProductSentence.ProductSentenceFired (the screenText*') 'playtest-sweep.ps1 must not still claim ProductSentenceFired means the screenText check -- #457/#460 flipped the gate to a backend hit'
Check ($playtestSweepRawText -like '*STALE NOTE*CORRECTED*') 'playtest-sweep.ps1 must carry the stale-comment correction, not silently delete the history of the mistake'

# --- 15. Persona front-matter amendment (W4, joins table): "persona files have NO front-matter
# today" -- verified mechanically here, not just asserted in a comment -----------------------------
foreach ($existingPersonaFile in @('first-timer', 'veteran', 'speedrunner', 'completionist')) {
    $rawPersonaText = Get-Content (Join-Path $personasDir ($existingPersonaFile + '.md')) -Raw
    Check ($rawPersonaText.TrimStart() -notlike '---*') ($existingPersonaFile + '.md must have NO front-matter today (the join-table''s own precondition)')
    $splitResult = Split-PersonaFrontMatter -RawText $rawPersonaText
    Check ($splitResult.PatienceMultiplier -eq 1.0) ($existingPersonaFile + '.md with no front-matter must default PatienceMultiplier to 1.0 (no scaling)')
    Check ($splitResult.Body -eq $rawPersonaText.Trim()) ($existingPersonaFile + '.md with no front-matter must pass its whole text through as Body, unchanged')
    $multiplierViaHelper = Get-PersonaPatienceMultiplier -PersonaName $existingPersonaFile -PersonasDir $personasDir
    Check ($multiplierViaHelper -eq 1.0) ($existingPersonaFile + ' must resolve to a 1.0 patience multiplier via Get-PersonaPatienceMultiplier')
}

# A valid front-matter block: PatienceMultiplier parses, and the block is stripped from Body before
# it could ever reach the model.
$validFrontMatterText = "---`r`nPatienceMultiplier: 1.5`r`n---`r`n## Who you are`r`n`r`nSome persona text."
$validSplit = Split-PersonaFrontMatter -RawText $validFrontMatterText
Check ($validSplit.PatienceMultiplier -eq 1.5) ('a valid PatienceMultiplier front-matter value must parse, got ' + $validSplit.PatienceMultiplier)
Check ($validSplit.Body -notlike '*PatienceMultiplier*') 'the front-matter block must be stripped from Body -- it must never reach the model'
Check ($validSplit.Body -like '*Who you are*') 'the real persona text after the front-matter block must survive in Body'

# THE required proof: an unknown front-matter key fails loudly.
$unknownKeyText = "---`r`nSomeMadeUpKey: 3`r`n---`r`n## Who you are"
$unknownKeyThrew = $false
$unknownKeyMessage = ''
try { Split-PersonaFrontMatter -RawText $unknownKeyText | Out-Null } catch { $unknownKeyThrew = $true; $unknownKeyMessage = $_.Exception.Message }
Check ($unknownKeyThrew -eq $true) 'an unrecognized persona front-matter key must throw, not silently do nothing'
Check ($unknownKeyMessage -like '*unknown persona front-matter key*') ('the thrown message must say "unknown persona front-matter key", got [' + $unknownKeyMessage + ']')

# A non-numeric PatienceMultiplier value must also fail loudly, not silently coerce to something odd.
$badNumberText = "---`r`nPatienceMultiplier: not-a-number`r`n---`r`n## Who you are"
$badNumberThrew = $false
try { Split-PersonaFrontMatter -RawText $badNumberText | Out-Null } catch { $badNumberThrew = $true }
Check ($badNumberThrew -eq $true) 'a non-numeric PatienceMultiplier must throw, not silently default'

# --- 16. Monkey persona (W4, ruling 9): model-free, seeded uniform-random ------------------------
. (Join-Path $toolsDir 'agent-playtest\monkey.ps1')

$monkeyStateTwoEnabledOneDisabled = [pscustomobject]@{
    canMove = $true
    controls = @(
        [pscustomobject]@{ name = 'OpenShop'; enabled = $true }
        [pscustomobject]@{ name = 'OpenForge'; enabled = $true }
        [pscustomobject]@{ name = 'ClosedThing'; enabled = $false }
    )
}
$monkeyCandidates = Get-MonkeyCandidates -State $monkeyStateTwoEnabledOneDisabled
# 2 enabled presses + 8 move directions + 1 advance = 11.
Check ($monkeyCandidates.Count -eq 11) ('2 enabled + 8 moves + advance must be 11 candidates, got ' + $monkeyCandidates.Count)
$pressTargets = @($monkeyCandidates | Where-Object { $_.Action -eq 'press' } | ForEach-Object { $_.Target })
Check (($pressTargets -join ',') -eq 'OpenShop,OpenForge') ('only the two ENABLED controls may be press candidates, got [' + ($pressTargets -join ',') + ']')
Check ($pressTargets -notcontains 'ClosedThing') 'a disabled control must never be a monkey candidate'
Check (@($monkeyCandidates | Where-Object { $_.Action -eq 'advance' }).Count -eq 1) 'advance must always be exactly one candidate'
Check (@($monkeyCandidates | Where-Object { $_.Action -eq 'key' }).Count -eq 0) 'monkey must never consider "key" -- the plan''s own candidate set is enabled controls + legal moves + advance only'
Check (@($monkeyCandidates | Where-Object { $_.Action -eq 'stop' }).Count -eq 0) 'monkey must never consider "stop" -- ruling 9, it runs to budget regardless'

$monkeyStateCannotMove = [pscustomobject]@{ canMove = $false; controls = @([pscustomobject]@{ name = 'OpenShop'; enabled = $true }) }
$monkeyCandidatesNoMove = Get-MonkeyCandidates -State $monkeyStateCannotMove
Check ($monkeyCandidatesNoMove.Count -eq 2) ('canMove=false must drop all 8 move candidates (1 press + 1 advance = 2), got ' + $monkeyCandidatesNoMove.Count)

$monkeyStateNothingLegal = [pscustomobject]@{ canMove = $false; controls = @() }
$monkeyCandidatesNothing = Get-MonkeyCandidates -State $monkeyStateNothingLegal
Check ($monkeyCandidatesNothing.Count -eq 1) 'zero enabled controls and canMove=false must still leave exactly 1 candidate (advance) -- never an empty set'
Check ($monkeyCandidatesNothing[0].Action -eq 'advance') 'the one guaranteed candidate must be advance'

# THE required proof: same seed against the same state sequence produces a BYTE-IDENTICAL command
# sequence -- reproducibility of the COMMAND STREAM, never a sim-determinism claim (monkey.ps1's own
# header says so at length).
$monkeyStubStates = @(
    [pscustomobject]@{ canMove = $true; controls = @([pscustomobject]@{ name = 'A'; enabled = $true }, [pscustomobject]@{ name = 'B'; enabled = $false }) }
    [pscustomobject]@{ canMove = $false; controls = @([pscustomobject]@{ name = 'A'; enabled = $true }, [pscustomobject]@{ name = 'B'; enabled = $true }) }
    [pscustomobject]@{ canMove = $true; controls = @() }
    [pscustomobject]@{ canMove = $true; controls = @([pscustomobject]@{ name = 'C'; enabled = $true }) }
    [pscustomobject]@{ canMove = $false; controls = @() }
)
$monkeyRandomOne = New-Object System.Random(7)
$monkeyRandomTwo = New-Object System.Random(7)
$monkeySequenceOne = @($monkeyStubStates | ForEach-Object { Get-MonkeyCommand -State $_ -Random $monkeyRandomOne })
$monkeySequenceTwo = @($monkeyStubStates | ForEach-Object { Get-MonkeyCommand -State $_ -Random $monkeyRandomTwo })
Check ($monkeySequenceOne.Count -eq $monkeyStubStates.Count) 'sanity: one command must be produced per stubbed state'
$monkeySequencesMatch = $true
for ($i = 0; $i -lt $monkeySequenceOne.Count; $i++) {
    if ($monkeySequenceOne[$i] -ne $monkeySequenceTwo[$i]) { $monkeySequencesMatch = $false }
}
Check ($monkeySequencesMatch -eq $true) ('same seed (7) over the same state sequence must produce a byte-identical command sequence. Seq1: [' + ($monkeySequenceOne -join ' | ') + '] Seq2: [' + ($monkeySequenceTwo -join ' | ') + ']')

# Every produced command must itself be legal JSON with a recognized action -- Get-MonkeyCommand's
# own output feeds straight into command.json with no Get-LegalCommandFromReply re-check.
foreach ($cmdText in $monkeySequenceOne) {
    $parsedMonkeyCmd = $null
    try { $parsedMonkeyCmd = $cmdText | ConvertFrom-Json } catch { }
    Check ($null -ne $parsedMonkeyCmd) ('every monkey command must parse as JSON, got [' + $cmdText + ']')
    if ($parsedMonkeyCmd) {
        Check (@('press', 'move', 'advance') -contains $parsedMonkeyCmd.action) ('a monkey command''s action must be press/move/advance only, got [' + $parsedMonkeyCmd.action + ']')
    }
}

# Driver wiring: monkey must be reachable, and the GPU gate / act-prompt assembly must be SKIPPED
# for it -- proven by checking the guarding conditional actually mentions isMonkey right next to the
# gate, not just that the string "isMonkey" appears somewhere in the file.
Check ($agentPlaytestRawText -like '*Get-MonkeyCommand*') 'agent-playtest.ps1 must call Get-MonkeyCommand'
Check ($agentPlaytestRawText -like '*monkey.ps1*') 'agent-playtest.ps1 must dot-source monkey.ps1'
Check ($agentPlaytestRawText -like '*FrameEvery = 25*') 'agent-playtest.ps1 must default -FrameEvery to 25 for monkey (ruling 4)'

$gpuGateBlockMatch = [regex]::Match($agentPlaytestRawText, '(?s)GPU gate:.*?nvidia-smi')
Check ($gpuGateBlockMatch.Success -eq $true) 'sanity: the GPU gate comment block followed by an nvidia-smi call must exist'
if ($gpuGateBlockMatch.Success) {
    Check ($gpuGateBlockMatch.Value -like '*isMonkey*') 'the nvidia-smi GPU gate''s own guarding conditional must reference isMonkey -- monkey must skip the gate entirely, not just the model warm-up at the bottom of it'
}
Check ($agentPlaytestRawText -like '*skipping act-prompt*schema*judge-prompt assembly entirely*') 'agent-playtest.ps1 must have a dedicated monkey branch that skips act-prompt/schema/judge-prompt assembly outright, never building one just to ignore it'
$actPromptBlockMatch = [regex]::Match($agentPlaytestRawText, '(?s)\$actionSchemaJson = ''''\r?\nif \(\$isMonkey\) \{.*?\} elseif \(-not \$Scripted\) \{.*?Build-PersonaActPrompt')
Check ($actPromptBlockMatch.Success -eq $true) 'the act-prompt assembly block must be structurally gated by "if ($isMonkey) {...} elseif (-not $Scripted) {...Build-PersonaActPrompt...}", not merely mention isMonkey somewhere earlier in the file'

# --- 17. Attached persona (W4): hero tracking, death detection, and reusing metrics.ps1's own
# product-sentence matcher rather than duplicating it -----------------------------------------------
. (Join-Path $toolsDir 'agent-playtest\metrics.ps1')
. (Join-Path $toolsDir 'agent-playtest\attached.ps1')

Check ((Get-AttachedHeroNameFromNote -Note '  Torvald  ') -eq 'Torvald') 'a hero name must be trimmed of surrounding whitespace'
Check ($null -eq (Get-AttachedHeroNameFromNote -Note '')) 'an empty note must never be treated as a hero name'
Check ($null -eq (Get-AttachedHeroNameFromNote -Note '   ')) 'a whitespace-only note must never be treated as a hero name'
Check ($null -eq (Get-AttachedHeroNameFromNote -Note $null)) 'a missing note must never be treated as a hero name'

# THE required proof: fires on a fixture screenText that names the hero next to death vocabulary,
# and NOT otherwise (name alone, death words alone for a different hero, or no name at all).
$deathScreenText = @('The town square is quiet.', 'Torvald fell on floor 3 of the mine.')
Check ((Test-ScreenTextForHeroDeath -HeroName 'Torvald' -ScreenTextLines $deathScreenText) -eq $true) 'the hero''s name next to death vocabulary on the same line must fire'

$nameOnlyScreenText = @('Torvald bought a sword from the shelf.')
Check ((Test-ScreenTextForHeroDeath -HeroName 'Torvald' -ScreenTextLines $nameOnlyScreenText) -eq $false) 'the hero''s name WITHOUT death vocabulary must never fire'

$differentHeroDeathScreenText = @('Emberbite fell on floor 2.')
Check ((Test-ScreenTextForHeroDeath -HeroName 'Torvald' -ScreenTextLines $differentHeroDeathScreenText) -eq $false) 'death vocabulary next to a DIFFERENT hero''s name must never fire'

Check ((Test-ScreenTextForHeroDeath -HeroName '' -ScreenTextLines $deathScreenText) -eq $false) 'a blank/unset hero name must never fire, however the screen reads'
Check ((Test-ScreenTextForHeroDeath -HeroName 'Torvald' -ScreenTextLines @()) -eq $false) 'empty screenText must never fire'
Check ((Test-ScreenTextForHeroDeath -HeroName 'Torvald' -ScreenTextLines $null) -eq $false) 'null screenText must never throw or fire'

# Reuses metrics.ps1's OWN pattern -- proven by a positive AND a negative match, not just "it did not
# throw" (a silently-always-false stub would pass a throw-only check).
$attributionHitText = @('Legend: Emberbite''s MakersMark blade turned the killing blow on floor 3. Torvald lives.')
Check ((Test-ScreenTextForAttribution -ScreenTextLines $attributionHitText) -eq $true) 'an attribution-shaped line must be detected via the shared pattern'
$noAttributionText = @('gold 100', 'welcome to the shop')
Check ((Test-ScreenTextForAttribution -ScreenTextLines $noAttributionText) -eq $false) 'ordinary lines with no attribution-shaped language must not fire'

# attached.md itself: a real persona file, unlike monkey -- and it must know NOTHING about the
# vigil-specific vocabulary (it is a minimally-informed persona, same spirit as first-timer/
# speedrunner), reusing the SAME glossary denylist section 10 already built.
$attachedPersonaPath = Join-Path $personasDir 'attached.md'
Check (Test-Path $attachedPersonaPath) 'prompts/personas/attached.md must exist'
$attachedPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'attached' -PersonasDir $personasDir
Check ($attachedPrompt -notlike '*{{PERSONA}}*') 'the assembled attached prompt must not still contain the {{PERSONA}} marker'
Check ($attachedPrompt -like '*hero*') 'the assembled attached prompt must carry its own hero-tracking goal text'
$attachedHits = Test-TextForGameNouns -Text $attachedPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($attachedHits -notcontains 'Vigil') 'attached must not know the vigil by name -- it is deliberately minimally informed, same spirit as first-timer/speedrunner'

# Driver wiring: the death check, the injected line, the major patience hit, and the honesty
# footer's own attached-specific disclosure must all be reachable from agent-playtest.ps1.
Check ($agentPlaytestRawText -like '*Test-ScreenTextForHeroDeath*') 'agent-playtest.ps1 must call Test-ScreenTextForHeroDeath'
Check ($agentPlaytestRawText -like '*Get-AttachedHeroNameFromNote*') 'agent-playtest.ps1 must call Get-AttachedHeroNameFromNote'
Check ($agentPlaytestRawText -like '*Test-ScreenTextForAttribution*') 'agent-playtest.ps1 must call Test-ScreenTextForAttribution'
Check ($agentPlaytestRawText -like '*attached-death*') 'agent-playtest.ps1 must drain the meter with the attached-death cause'
Check ($agentPlaytestRawText -like '*PatienceDrainAttachedDeath*') 'agent-playtest.ps1 must apply the major attached-death patience hit'
Check ($agentPlaytestRawText -like '*attachedHeroName + '' is dead.''*') 'agent-playtest.ps1 must inject the "<name> is dead." line'
Check ($agentPlaytestRawText -like '*Get-HonestyFooterLines -ExtraLines*') 'agent-playtest.ps1 must pass -ExtraLines to Get-HonestyFooterLines (the attached-specific honesty disclosure)'
Check ($agentPlaytestRawText -like '*INJECTED by the harness*') 'the honesty footer''s attached disclosure must say the attachment was INJECTED, never formed by the model'

# footer.ps1 itself: -ExtraLines must be backward-compatible (every existing caller with no args
# still gets the identical static footer) AND must actually append when given lines.
$footerNoExtra = Get-HonestyFooterLines
$footerWithExtra = Get-HonestyFooterLines -ExtraLines @('- an extra attached-only line')
Check (($footerNoExtra -join [Environment]::NewLine) -notlike '*extra attached-only line*') 'calling Get-HonestyFooterLines with no -ExtraLines must produce the ORIGINAL static footer, unchanged'
Check (($footerWithExtra -join [Environment]::NewLine) -like '*extra attached-only line*') '-ExtraLines must actually be appended when supplied'

# --- 18. Scratchpad wipe-at-start (W4, ruling 2) --------------------------------------------------
# notes.md must be in the SAME stale-artifact sweep every other run-scoped file already goes through
# -- proven by checking $notesPath sits inside that exact foreach's own @(...) list, not merely that
# the string "notesPath" appears somewhere in the file.
$staleArrayMatch = [regex]::Match($agentPlaytestRawText, '(?s)foreach \(\$stale in @\((.*?)\)\)')
Check ($staleArrayMatch.Success -eq $true) 'sanity: the stale-artifact cleanup foreach must exist'
if ($staleArrayMatch.Success) {
    Check ($staleArrayMatch.Groups[1].Value -like '*notesPath*') 'notes.md''s path variable must be included in the stale-artifact wipe-at-start sweep'
}
Check ($agentPlaytestRawText -like "*'notes.md'*") 'agent-playtest.ps1 must define a notes.md path'
Check ($agentPlaytestRawText -like '*notesLines.Add*') 'agent-playtest.ps1 must accumulate notes into an in-memory list'
Check ($agentPlaytestRawText -like '*Add-Content -Path $notesPath*') 'agent-playtest.ps1 must append each note to notes.md on disk as it arrives'
Check ($agentPlaytestRawText -notlike '*RecentHistory*') 'agent-playtest.ps1 must not reference the old -RecentHistory parameter anywhere -- it was REPLACED, not extended'

# --- 19. Judge demoted to two pointers (W4, deferred from W2) -------------------------------------
$judgeMdText = Get-Content (Join-Path $toolsDir 'agent-playtest\prompts\judge.md') -Raw
$scoutJudgeMdText = Get-Content (Join-Path $toolsDir 'agent-playtest\prompts\scout-judge.md') -Raw

Check ($judgeMdText -like '*most wanted to keep playing*') 'judge.md must instruct the two-pointer closing format (keep-playing pointer)'
Check ($judgeMdText -like '*most wanted to stop*') 'judge.md must instruct the two-pointer closing format (stop pointer)'
Check ($judgeMdText -like '*exactly two quoted pointers*') 'judge.md must say the closing pointers are exactly two and quoted'

Check ($scoutJudgeMdText -like '*most wanted to keep playing*') 'scout-judge.md must ALSO instruct the two-pointer closing format (keep-playing pointer)'
Check ($scoutJudgeMdText -like '*most wanted to stop*') 'scout-judge.md must ALSO instruct the two-pointer closing format (stop pointer)'
# scout-judge.md must KEEP its own evidence questions -- the brief's own "keeps its evidence
# questions but gains" wording -- proven by checking its pre-existing headings survived this edit.
Check ($scoutJudgeMdText -like '*Decision that mattered*') 'scout-judge.md must keep its own "Decision that mattered" evidence question'
Check ($scoutJudgeMdText -like '*Named my work*') 'scout-judge.md must keep its own "Named my work" evidence question'
Check ($scoutJudgeMdText -like '*Day-11 check*') 'scout-judge.md must keep its own day-11 evidence question'

# --- 20. Scenario cards (W5, docs/plans/2026-08-10-002) -- "did this ONE named behaviour work" -----
. (Join-Path $toolsDir 'agent-playtest\scenario.ps1')

$scenarioFixturePath = Join-Path $toolsDir 'agent-playtest\tests\scenario-card-fixture.md'
Check (Test-Path $scenarioFixturePath) ('scenario card fixture must exist at ' + $scenarioFixturePath)

# Card parses into all four fields (Backend predicate included -- the fixture card carries one).
$scenarioCard = Read-ScenarioCard -Path $scenarioFixturePath
Check ($scenarioCard.Slug -eq 'scenario-card-fixture') ('card Slug must come from the filename, got [' + $scenarioCard.Slug + ']')
Check ($scenarioCard.Setup.Type -eq 'Fresh') ('fixture card Setup must parse as Fresh, got [' + $scenarioCard.Setup.Type + ']')
Check (@($scenarioCard.Setup.Commands).Count -eq 0) 'a Fresh Setup must carry zero commands'
Check ($scenarioCard.Brief -like '*price one item*') ('Brief text must round-trip, got [' + $scenarioCard.Brief + ']')
Check ($scenarioCard.ExpectedObservation -like '*XYZZY_EXPECTED_MARKER_NEVER_IN_ACT_PROMPT*') ('Expected observation text must round-trip, got [' + $scenarioCard.ExpectedObservation + ']')
Check ($null -ne $scenarioCard.BackendPredicate) 'fixture card carries a Backend predicate section and must not parse as null'
if ($scenarioCard.BackendPredicate) {
    Check ($scenarioCard.BackendPredicate.Kind -eq 'action') ('BackendPredicate.Kind must be "action", got [' + $scenarioCard.BackendPredicate.Kind + ']')
    Check ($scenarioCard.BackendPredicate.Field -eq 'action') ('BackendPredicate.Field must be "action", got [' + $scenarioCard.BackendPredicate.Field + ']')
    Check ($scenarioCard.BackendPredicate.Equals -eq 'SendSupplyAction') ('BackendPredicate.Equals must be "SendSupplyAction", got [' + $scenarioCard.BackendPredicate.Equals + ']')
}

# The real, shipped card (vigil-runner.md) must ALSO parse cleanly -- a regression pin on the one
# card W5 actually ships, not just the synthetic fixture above.
$vigilRunnerPath = Join-Path $toolsDir 'agent-playtest\scenarios\vigil-runner.md'
Check (Test-Path $vigilRunnerPath) ('the shipped vigil-runner card must exist at ' + $vigilRunnerPath)
$vigilRunnerCard = Read-ScenarioCard -Path $vigilRunnerPath
Check ($vigilRunnerCard.Slug -eq 'vigil-runner') ('vigil-runner card Slug must be "vigil-runner", got [' + $vigilRunnerCard.Slug + ']')
Check ($vigilRunnerCard.Setup.Type -eq 'Scripted') ('vigil-runner Setup must be a scripted command prefix, got [' + $vigilRunnerCard.Setup.Type + ']')

# U3 (eyes-learn-labels wave): the card now crafts a sendable field-salve BEFORE the advance-spam
# (CampHandlers.ApplySend needs a player-crafted consumable in hand -- see the card's own "## Setup"
# citations) -- 7 craft-prefix commands (enter forge, approach anvil, open panel, buy x2, craft,
# close panel) followed by the original 12 advances, 19 total. The old "every command is advance"
# pin is replaced by two narrower pins: the LAST 12 commands are still all "advance" (unchanged from
# before this unit), and the FIRST 7 are the specific non-advance craft-prefix shape.
$vigilRunnerCommands = @($vigilRunnerCard.Setup.Commands)
Check ($vigilRunnerCommands.Count -eq 19) ('vigil-runner Setup must carry exactly 19 scripted commands (7 craft-prefix + 12 advance), got ' + $vigilRunnerCommands.Count)

$vigilCraftPrefix = @($vigilRunnerCommands | Select-Object -First 7 | ForEach-Object { $_ | ConvertFrom-Json })
$vigilAdvanceTail = @($vigilRunnerCommands | Select-Object -Last 12 | ForEach-Object { $_ | ConvertFrom-Json })

Check ($vigilCraftPrefix.Count -eq 7) ('vigil-runner craft-prefix slice must have 7 parsed commands, got ' + $vigilCraftPrefix.Count)
foreach ($advCmd in $vigilAdvanceTail) {
    Check ($null -ne $advCmd -and $advCmd.action -eq 'advance') ('every one of the LAST 12 vigil-runner Setup commands must still parse as "advance", got [' + $advCmd + ']')
}

if ($vigilCraftPrefix.Count -eq 7) {
    Check ($vigilCraftPrefix[0].action -eq 'key' -and $vigilCraftPrefix[0].target -eq 'interact') 'vigil-runner craft-prefix command 1 must be key:interact (enter the forge -- player spawns at its door)'
    Check ($vigilCraftPrefix[1].action -eq 'move' -and $vigilCraftPrefix[1].dir -eq 'up') 'vigil-runner craft-prefix command 2 must be move:up (approach the anvil station)'
    Check ($vigilCraftPrefix[2].action -eq 'key' -and $vigilCraftPrefix[2].target -eq 'interact') 'vigil-runner craft-prefix command 3 must be key:interact (interact with the anvil -> opens the Forge panel)'
    Check ($vigilCraftPrefix[3].action -eq 'press' -and $vigilCraftPrefix[3].target -eq 'BuyMat_copper') 'vigil-runner craft-prefix command 4 must press BuyMat_copper (1st copper)'
    Check ($vigilCraftPrefix[4].action -eq 'press' -and $vigilCraftPrefix[4].target -eq 'BuyMat_copper') 'vigil-runner craft-prefix command 5 must press BuyMat_copper again (2nd copper -- field-salve needs 2)'
    Check ($vigilCraftPrefix[5].action -eq 'press' -and $vigilCraftPrefix[5].target -eq 'Craft_field-salve') 'vigil-runner craft-prefix command 6 must press Craft_field-salve (bare CraftAction, no minigame)'
    Check ($vigilCraftPrefix[6].action -eq 'key' -and $vigilCraftPrefix[6].target -eq 'cancel') 'vigil-runner craft-prefix command 7 must be key:cancel (close the Forge panel before the advance-spam)'
}

Check ($vigilRunnerCard.Brief -like '*camped in the mine*') 'vigil-runner Brief must describe the send-supply task'
Check ($vigilRunnerCard.ExpectedObservation -like '*send-supply verb*') 'vigil-runner Expected observation must name the send-supply verb'
Check ($null -ne $vigilRunnerCard.BackendPredicate) 'vigil-runner must carry a Backend predicate'
if ($vigilRunnerCard.BackendPredicate) {
    Check ($vigilRunnerCard.BackendPredicate.Equals -eq 'SendSupplyAction') ('vigil-runner BackendPredicate.Equals must be "SendSupplyAction", got [' + $vigilRunnerCard.BackendPredicate.Equals + ']')
}

# U3 regression pin: the KNOWN GAP note this unit was scoped to close must actually be gone from the
# shipped card, not just described as gone in a commit message (rule 8's own "git outranks docs", one
# level down -- the card's own text must agree with what the Setup now does).
$vigilRunnerRawText = Get-Content $vigilRunnerPath -Raw
Check ($vigilRunnerRawText -notlike '*KNOWN GAP*') 'vigil-runner.md must no longer carry a KNOWN GAP note -- U3 closed it, the craft-prefix above is the fix'

# Missing card: FAILS LOUDLY, never falls back to a plain run.
$missingCardThrew = $false
$missingCardMessage = ''
try { Read-ScenarioCard -Path (Join-Path $env:TEMP 'agent-playtest-no-such-scenario-card.md') } catch { $missingCardThrew = $true; $missingCardMessage = $_.Exception.Message }
Check ($missingCardThrew -eq $true) 'Read-ScenarioCard must THROW on a missing file, never silently return a default card'
Check ($missingCardMessage -like '*not found*') ('the missing-card message must say so explicitly, got [' + $missingCardMessage + ']')

# Malformed card (a REQUIRED section absent -- Brief, here): FAILS LOUDLY, naming the section.
$malformedCardPath = Join-Path $toolsDir 'agent-playtest\tests\scenario-card-missing-brief-fixture.md'
Check (Test-Path $malformedCardPath) ('malformed-card fixture must exist at ' + $malformedCardPath)
$malformedCardThrew = $false
$malformedCardMessage = ''
try { Read-ScenarioCard -Path $malformedCardPath } catch { $malformedCardThrew = $true; $malformedCardMessage = $_.Exception.Message }
Check ($malformedCardThrew -eq $true) 'Read-ScenarioCard must THROW on a card missing a required section'
Check ($malformedCardMessage -like '*Brief*') ('the malformed-card message must NAME the missing section (Brief), got [' + $malformedCardMessage + ']')

# Setup's three shapes, directly.
$freshSetup = ConvertTo-ScenarioSetup -RawSetupText '  Fresh  '
Check ($freshSetup.Type -eq 'Fresh') 'ConvertTo-ScenarioSetup must accept "fresh" case/whitespace-insensitively'
$continueSetup = ConvertTo-ScenarioSetup -RawSetupText 'continue'
Check ($continueSetup.Type -eq 'Continue') 'ConvertTo-ScenarioSetup must accept "continue"'
$scriptedSetupText = 'prose above the block' + [Environment]::NewLine + '```json' + [Environment]::NewLine +
    '["{\"action\":\"advance\",\"why\":\"t\"}", "{\"action\":\"key\",\"target\":\"cancel\",\"why\":\"t\"}"]' +
    [Environment]::NewLine + '```'
$scriptedSetup = ConvertTo-ScenarioSetup -RawSetupText $scriptedSetupText
Check ($scriptedSetup.Type -eq 'Scripted') 'ConvertTo-ScenarioSetup must recognize a fenced JSON command list as Scripted'
Check (@($scriptedSetup.Commands).Count -eq 2) ('a 2-command scripted Setup must parse to exactly 2 commands, got ' + @($scriptedSetup.Commands).Count)

$badSetupThrew = $false
try { ConvertTo-ScenarioSetup -RawSetupText 'not fresh, not continue, not json at all' } catch { $badSetupThrew = $true }
Check ($badSetupThrew -eq $true) 'ConvertTo-ScenarioSetup must THROW on text that is neither fresh/continue nor parseable JSON'

$nestedObjectSetupThrew = $false
try { ConvertTo-ScenarioSetup -RawSetupText '[{"action":"advance"}]' } catch { $nestedObjectSetupThrew = $true }
Check ($nestedObjectSetupThrew -eq $true) 'ConvertTo-ScenarioSetup must THROW when the list holds nested JSON objects instead of command STRINGS'

# De-contamination pin: build the REAL assembled act prompt (persona substitution + the scenario
# Brief append, in that order) and prove the Expected observation text is NOWHERE in it. This is the
# brief's own required proof, not a trust-the-doc-comment check.
$realActPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'first-timer' -PersonasDir $personasDir
$realActPromptWithScenario = $realActPrompt + [Environment]::NewLine + [Environment]::NewLine +
    (Get-ScenarioActPromptAddition -Brief $scenarioCard.Brief)
Check ($realActPromptWithScenario -like '*price one item*') 'sanity: the Brief itself must actually reach the assembled act prompt'
Check ($realActPromptWithScenario -notlike '*XYZZY_EXPECTED_MARKER_NEVER_IN_ACT_PROMPT*') 'the Expected observation text must NEVER appear in the assembled act prompt (de-contamination)'

# Judge-question assembly: Expected observation DOES belong in the judge's own input (never the act
# prompt above) -- the mirror-image proof of the de-contamination pin.
$judgeQuestionLines = Get-ScenarioJudgeQuestionText -ExpectedObservation $scenarioCard.ExpectedObservation
$judgeQuestionText = ($judgeQuestionLines -join [Environment]::NewLine)
Check ($judgeQuestionText -like '*XYZZY_EXPECTED_MARKER_NEVER_IN_ACT_PROMPT*') 'the judge-only question text must carry the Expected observation'
Check ($judgeQuestionText -like '*SCENARIO VERDICT:*') 'the judge-only question must instruct the exact "SCENARIO VERDICT:" reply line'

# Each of the three verdicts, parsed from a fixture judge reply.
$confirmedText = 'Some prose about the run.' + [Environment]::NewLine + 'SCENARIO VERDICT: CONFIRMED: the ledger reads "Runner delivered a potion to Torvald."'
$confirmedVerdict = Get-ScenarioVerdictFromJudgeText -JudgeText $confirmedText
Check ($confirmedVerdict.Verdict -eq 'CONFIRMED') ('CONFIRMED fixture must parse as CONFIRMED, got [' + $confirmedVerdict.Verdict + ']')
Check ($confirmedVerdict.Quote -like '*Runner delivered a potion*') ('CONFIRMED fixture must capture its quote, got [' + $confirmedVerdict.Quote + ']')

$notSeenText = 'The run never opened the camp card.' + [Environment]::NewLine + 'SCENARIO VERDICT: NOT SEEN: the log never shows a Camp phase at all.'
$notSeenVerdict = Get-ScenarioVerdictFromJudgeText -JudgeText $notSeenText
Check ($notSeenVerdict.Verdict -eq 'NOT SEEN') ('NOT SEEN fixture must parse as NOT SEEN, got [' + $notSeenVerdict.Verdict + ']')
Check ($notSeenVerdict.Quote -like '*never shows a Camp phase*') 'NOT SEEN fixture must capture its quote'

$contradictedText = 'SCENARIO VERDICT: CONTRADICTED: the runner reported "Nothing in your hands to send."'
$contradictedVerdict = Get-ScenarioVerdictFromJudgeText -JudgeText $contradictedText
Check ($contradictedVerdict.Verdict -eq 'CONTRADICTED') ('CONTRADICTED fixture must parse as CONTRADICTED, got [' + $contradictedVerdict.Verdict + ']')
Check ($contradictedVerdict.Quote -like '*Nothing in your hands to send*') 'CONTRADICTED fixture must capture its quote'

$noVerdictLineText = 'The judge just wrote ordinary prose findings with no verdict line at all.'
$unknownVerdict = Get-ScenarioVerdictFromJudgeText -JudgeText $noVerdictLineText
Check ($unknownVerdict.Verdict -eq 'UNKNOWN') ('a reply with no "SCENARIO VERDICT:" line must parse as UNKNOWN, got [' + $unknownVerdict.Verdict + ']')

# Format-ScenarioVerdictSection renders each of the three (plus UNKNOWN), with mechanical fact and
# model observation kept SEPARATE lines, never blended into one boolean.
$presentBackendResult = [pscustomobject]@{ Present = $true; MatchCount = 1; Detail = 'found 1 matching "action" row(s) where action contains "SendSupplyAction"' }
$confirmedSection = Format-ScenarioVerdictSection -Card $scenarioCard -JudgeVerdict $confirmedVerdict -BackendResult $presentBackendResult
Check ($confirmedSection -like '*## Scenario verdict*') 'the rendered section must carry its own heading'
Check ($confirmedSection -like '*Model observation: CONFIRMED*') 'CONFIRMED must render as the model observation line'
Check ($confirmedSection -like '*PRESENT*') 'a present backend predicate must render as PRESENT'
Check ($confirmedSection -like '*never blended*') 'the rendered section must say mechanical fact and model observation are never blended'

$absentBackendResult = [pscustomobject]@{ Present = $false; MatchCount = 0; Detail = 'no "action" row with action containing "SendSupplyAction" was found in 3 row(s)' }
$notSeenSection = Format-ScenarioVerdictSection -Card $scenarioCard -JudgeVerdict $notSeenVerdict -BackendResult $absentBackendResult
Check ($notSeenSection -like '*Model observation: NOT SEEN*') 'NOT SEEN must render as the model observation line'
Check ($notSeenSection -like '*ABSENT*') 'an absent backend predicate must render as ABSENT'

$contradictedSection = Format-ScenarioVerdictSection -Card $scenarioCard -JudgeVerdict $contradictedVerdict -BackendResult $null
Check ($contradictedSection -like '*Model observation: CONTRADICTED*') 'CONTRADICTED must render as the model observation line'
Check ($contradictedSection -like '*not evaluated*') 'a null BackendResult (no backend log available) must render as "not evaluated", never a silent PRESENT/ABSENT'

$unknownSection = Format-ScenarioVerdictSection -Card $scenarioCard -JudgeVerdict $unknownVerdict -BackendResult $null
Check ($unknownSection -like '*Model observation: UNKNOWN*') 'UNKNOWN must render as its own model observation line'
Check ($unknownSection -notlike '*Model observation: NOT SEEN*') 'UNKNOWN must never render as the NOT SEEN model-observation line -- that would fabricate a negative the judge never gave'

# Backend predicate against fixture JSONL rows -- present and absent, mechanically, no model.
$presentPredicate = [pscustomobject]@{ Kind = 'action'; Field = 'action'; Equals = 'SendSupplyAction' }

$supplyFixturePath = Join-Path $toolsDir 'agent-playtest\tests\scenario-supply-fixture.jsonl'
Check (Test-Path $supplyFixturePath) ('scenario supply fixture must exist at ' + $supplyFixturePath)
$supplyRows = @((Read-BackendLogRows -LogPath $supplyFixturePath).Rows)
$presentResult = Test-ScenarioBackendPredicate -Predicate $presentPredicate -Rows $supplyRows
Check ($presentResult.Present -eq $true) ('a fixture with a SendSupplyAction action row must report Present=true, got ' + $presentResult.Present)
Check ($presentResult.MatchCount -eq 1) ('exactly 1 row must match, got ' + $presentResult.MatchCount)

$absentRows = @((Read-BackendLogRows -LogPath $backendFixturePath).Rows)
$absentResult = Test-ScenarioBackendPredicate -Predicate $presentPredicate -Rows $absentRows
Check ($absentResult.Present -eq $false) ('backend-fixture.jsonl has no SendSupplyAction row and must report Present=false, got ' + $absentResult.Present)
Check ($absentResult.MatchCount -eq 0) ('zero rows must match, got ' + $absentResult.MatchCount)

# Case-insensitive substring match -- proven directly rather than trusted from the two cases above.
$caseInsensitivePredicate = [pscustomobject]@{ Kind = 'action'; Field = 'action'; Equals = 'sendsupplyaction' }
$caseInsensitiveResult = Test-ScenarioBackendPredicate -Predicate $caseInsensitivePredicate -Rows $supplyRows
Check ($caseInsensitiveResult.Present -eq $true) 'the predicate match must be case-insensitive'

# Empty row set must never throw (mirrors backend.ps1's own AllowEmptyCollection lesson).
$emptyRowsThrew = $false
try { $null = Test-ScenarioBackendPredicate -Predicate $presentPredicate -Rows @() } catch { $emptyRowsThrew = $true }
Check ($emptyRowsThrew -eq $false) 'Test-ScenarioBackendPredicate must not throw on an empty row set'

# Malformed Backend predicate (missing a required key): FAILS LOUDLY.
$badPredicateThrew = $false
try { ConvertTo-ScenarioBackendPredicate -RawPredicateText '{"kind":"action","field":"action"}' } catch { $badPredicateThrew = $true }
Check ($badPredicateThrew -eq $true) 'ConvertTo-ScenarioBackendPredicate must THROW when a required key ("equals") is missing'

# --- 21. Pilot persona (S2, scripted-deep-pilot lane): human-shaped, seeded, friction-capturing ----
. (Join-Path $toolsDir 'agent-playtest\pilot.ps1')

# Determinism: THE required proof, same shape as monkey's own -- same seed over the same state
# sequence must produce a byte-identical command sequence AND identical decision/friction logs.
$pilotStubStates = @(
    [pscustomobject]@{ turn = 1; day = 1; phase = 'Morning'; location = 'town'; canMove = $true; lastOutcome = '(run start)'; screenText = @('Welcome to town.'); controls = @([pscustomobject]@{ name = 'OpenCommissions'; enabled = $true }); nearby = @([pscustomobject]@{ key = 'shop'; label = 'Shop'; direction = 'right'; distance = 80; inRange = $false }) }
    [pscustomobject]@{ turn = 2; day = 1; phase = 'Morning'; location = 'panel:CommissionBoard'; canMove = $false; lastOutcome = 'pressed OpenCommissions'; screenText = @('No commissions today.'); controls = @(); nearby = @() }
    [pscustomobject]@{ turn = 3; day = 1; phase = 'Expedition'; location = 'town'; canMove = $true; lastOutcome = 'advanced'; screenText = @('The heroes are away.'); controls = @(); nearby = @([pscustomobject]@{ key = 'forge'; label = 'Forge'; direction = 'left'; distance = 40; inRange = $true }); actionSlotsRemaining = 1 }
)
$pilotRandomOne = New-Object System.Random(11)
$pilotRandomTwo = New-Object System.Random(11)
$pilotMemoryOne = New-PilotMemory
$pilotMemoryTwo = New-PilotMemory
$pilotSequenceOne = @($pilotStubStates | ForEach-Object { Get-PilotCommand -State $_ -Memory $pilotMemoryOne -Random $pilotRandomOne })
$pilotSequenceTwo = @($pilotStubStates | ForEach-Object { Get-PilotCommand -State $_ -Memory $pilotMemoryTwo -Random $pilotRandomTwo })
Check ($pilotSequenceOne.Count -eq $pilotStubStates.Count) 'sanity: one command must be produced per stubbed state'
$pilotSequencesMatch = $true
for ($i = 0; $i -lt $pilotSequenceOne.Count; $i++) {
    if ($pilotSequenceOne[$i] -ne $pilotSequenceTwo[$i]) { $pilotSequencesMatch = $false }
}
Check ($pilotSequencesMatch -eq $true) ('same seed (11) over the same state sequence must produce a byte-identical pilot command sequence. Seq1: [' + ($pilotSequenceOne -join ' | ') + '] Seq2: [' + ($pilotSequenceTwo -join ' | ') + ']')
Check ($pilotMemoryOne.SixDecisions.Count -eq $pilotMemoryTwo.SixDecisions.Count) 'same seed must log the same number of six-decision entries'
Check ($pilotMemoryOne.FrictionLog.Count -eq $pilotMemoryTwo.FrictionLog.Count) 'same seed must log the same number of friction entries'

# Every produced command must itself be legal JSON with a recognized action, and never "stop" --
# pilot must run to budget the same way monkey does (S2's own "day 11+ is the floor" requirement
# would be undermined by a policy that could voluntarily end its own run early).
foreach ($cmdText in $pilotSequenceOne) {
    $parsedPilotCmd = $null
    try { $parsedPilotCmd = $cmdText | ConvertFrom-Json } catch { }
    Check ($null -ne $parsedPilotCmd) ('every pilot command must parse as JSON, got [' + $cmdText + ']')
    if ($parsedPilotCmd) {
        Check (@('press', 'move', 'key', 'advance') -contains $parsedPilotCmd.action) ('a pilot command''s action must be press/move/key/advance only, never stop, got [' + $parsedPilotCmd.action + ']')
    }
}

# Turn 2's stubbed state reports a refused-shaped lastOutcome is NOT tested here (none of the fixture
# states above use "refused:") -- proven directly instead, isolated from the sequence above.
$pilotRefusalMemory = New-PilotMemory
$pilotRefusalMemory.PendingIntent = 'pilot: stock the crafted item'
$pilotRefusalState = [pscustomobject]@{
    turn = 5; day = 2; phase = 'Morning'; location = 'panel:Shop'; canMove = $false
    lastOutcome = 'refused: ''Stock_9'' is disabled -- (no reason on the tooltip)'
    screenText = @('Nothing to stock.'); controls = @(); nearby = @()
}
Get-PilotCommand -State $pilotRefusalState -Memory $pilotRefusalMemory -Random (New-Object System.Random(1)) | Out-Null
Check ($pilotRefusalMemory.FrictionLog.Count -eq 1) ('a refused: lastOutcome must produce exactly one friction entry, got ' + $pilotRefusalMemory.FrictionLog.Count)
if ($pilotRefusalMemory.FrictionLog.Count -eq 1) {
    $firstFriction = $pilotRefusalMemory.FrictionLog[0]
    Check ($firstFriction.Category -eq 'refused') ('the friction entry''s category must be "refused", got [' + $firstFriction.Category + ']')
    Check ($firstFriction.Detail -eq $pilotRefusalState.lastOutcome) 'the friction entry must quote lastOutcome VERBATIM, never paraphrased'
    Check ($firstFriction.Trying -eq 'pilot: stock the crafted item') 'the friction entry must carry what the pilot was trying to do (PendingIntent from the prior turn)'
}

# Six-decisions logging: a commission accept/decline must log to Get-PilotCommand's own ledger under
# the exact decision name CLAUDE.md uses, with a Choice value that is one of the two real sides --
# never silently always the same side (checked over many draws, not one).
$sawAccept = $false
$sawDecline = $false
for ($seedTry = 0; $seedTry -lt 40; $seedTry++) {
    $decisionMemory = New-PilotMemory
    $decisionState = [pscustomobject]@{
        turn = 1; day = 1; phase = 'Morning'; location = 'panel:CommissionBoard'; canMove = $false
        lastOutcome = '(run start)'; screenText = @('A commission awaits.')
        controls = @(
            [pscustomobject]@{ name = 'CommissionAccept_7'; enabled = $true }
            [pscustomobject]@{ name = 'CommissionDecline_7'; enabled = $true }
        )
        nearby = @()
    }
    Get-PilotCommand -State $decisionState -Memory $decisionMemory -Random (New-Object System.Random($seedTry)) | Out-Null
    $entry = $decisionMemory.SixDecisions | Where-Object { $_.Decision -eq 'answer the commission' } | Select-Object -First 1
    if ($entry -and $entry.Choice -eq 'accept') { $sawAccept = $true }
    if ($entry -and $entry.Choice -eq 'decline') { $sawDecline = $true }
}
Check ($sawAccept -eq $true) 'across 40 seeded draws, the commission decision must resolve to "accept" at least once'
Check ($sawDecline -eq $true) 'across 40 seeded draws, the commission decision must resolve to "decline" at least once -- a run where it always resolves the same way tested one player, not a person (owner steer)'

# Forge minigame reading: parses ForgeMinigame's/QuenchMinigame's own on-screen readout text, never
# an internal gauge value -- proven with the exact label shapes those two classes actually render.
$forgePumpingLowHeatState = [pscustomobject]@{ screenText = @('Strike 3/21 -- Heat 200 -- pumping') }
$forgeCmdLowHeat = Get-PilotForgeMinigameCommand -State $forgePumpingLowHeatState -Memory (New-PilotMemory) -Random (New-Object System.Random(1))
Check ($null -ne $forgeCmdLowHeat) 'a Strike/Heat readout must be recognized as an open Act 1 overlay'
if ($forgeCmdLowHeat) {
    $parsedForge = $forgeCmdLowHeat | ConvertFrom-Json
    Check ($parsedForge.action -eq 'key' -and $parsedForge.target -eq 'forge_strike') 'while pumping and heat is still low, the pilot must wait (tap forge_strike, a Quench-safe no-op here) rather than toggle bellows off early'
}
$forgeIdleHotState = [pscustomobject]@{ screenText = @('Strike 5/21 -- Heat 900 -- idle') }
$forgeCmdHot = Get-PilotForgeMinigameCommand -State $forgeIdleHotState -Memory (New-PilotMemory) -Random (New-Object System.Random(1))
if ($forgeCmdHot) {
    $parsedForgeHot = $forgeCmdHot | ConvertFrom-Json
    Check ($parsedForgeHot.action -eq 'key' -and $parsedForgeHot.target -eq 'forge_strike') 'idle with hot heat must strike'
}
$quenchPlungeNowState = [pscustomobject]@{ screenText = @('Heat 512 (target 500 +/-140) -- PLUNGE NOW') }
$quenchCmd = Get-PilotForgeMinigameCommand -State $quenchPlungeNowState -Memory (New-PilotMemory) -Random (New-Object System.Random(1))
Check ($null -ne $quenchCmd) 'a Heat/target readout must be recognized as an open Act 2 (quench) overlay'
if ($quenchCmd) {
    $parsedQuench = $quenchCmd | ConvertFrom-Json
    Check ($parsedQuench.action -eq 'key' -and $parsedQuench.target -eq 'plunge') 'PLUNGE NOW on screen must produce a plunge key press'
}
$quenchWaitState = [pscustomobject]@{ screenText = @('Heat 800 (target 500 +/-140) -- wait for it...') }
$quenchWaitCmd = Get-PilotForgeMinigameCommand -State $quenchWaitState -Memory (New-PilotMemory) -Random (New-Object System.Random(1))
if ($quenchWaitCmd) {
    $parsedQuenchWait = $quenchWaitCmd | ConvertFrom-Json
    Check ($parsedQuenchWait.target -ne 'plunge') 'wait for it... on screen must never plunge early'
}
$noOverlayState = [pscustomobject]@{ screenText = @('Just standing in town.') }
Check ($null -eq (Get-PilotForgeMinigameCommand -State $noOverlayState -Memory (New-PilotMemory) -Random (New-Object System.Random(1)))) 'ordinary screen text with neither readout must return null (caller falls through to its own next choice)'

# Driver wiring: pilot must be reachable, and the GPU gate / act-prompt assembly must be SKIPPED for
# it -- same structural proof style as monkey's own wiring checks above.
Check ($agentPlaytestRawText -like '*Get-PilotCommand*') 'agent-playtest.ps1 must call Get-PilotCommand'
Check ($agentPlaytestRawText -like '*pilot.ps1*') 'agent-playtest.ps1 must dot-source pilot.ps1'
Check ($agentPlaytestRawText -like '*isPilot*') 'agent-playtest.ps1 must reference $isPilot'
if ($gpuGateBlockMatch.Success) {
    Check ($gpuGateBlockMatch.Value -like '*isPilot*') 'the nvidia-smi GPU gate''s own guarding conditional must reference isPilot -- pilot must skip the gate entirely, the same way monkey does'
}
Check ($agentPlaytestRawText -like '*skipping act-prompt*schema*judge-prompt assembly entirely (S2*') 'agent-playtest.ps1 must have a dedicated pilot branch that skips act-prompt/schema/judge-prompt assembly outright, never building one just to ignore it'
Check ($agentPlaytestRawText -like '*Friction log*') 'agent-playtest.ps1 must write a Friction log section for pilot runs'
Check ($agentPlaytestRawText -like '*Six decisions this run took*') 'agent-playtest.ps1 must write a Six-decisions section for pilot runs'
Check ($agentPlaytestRawText -like '*FrictionLog*SixDecisions*' -or $agentPlaytestRawText -like '*SixDecisions*FrictionLog*') 'agent-playtest.ps1 must fold FrictionLog/SixDecisions into metrics.json for pilot runs'

# --- Summary -----------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host ('FAIL (' + $failures.Count + ' of ' + ($passes + $failures.Count) + '):')
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}

Write-Host ('PASS: agent-playtest Diff/Scout pure logic, ' + $passes + '/' + $passes + ' checks, no Godot/ollama/VRAM needed.')
exit 0
