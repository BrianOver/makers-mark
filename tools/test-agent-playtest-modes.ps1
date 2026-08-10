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
    (Join-Path $toolsDir 'agent-playtest\footer.ps1')
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
$vigilText = Build-ActUserText -State $vigilState -Turn 5 -Turns 40 -RecentHistory @()
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
$nearbyText = Build-ActUserText -State $nearbyState -Turn 1 -Turns 40 -RecentHistory @()
Check ($nearbyText -like '*forge*YOU ARE HERE*') 'an in-range target must say YOU ARE HERE, not a walking direction'
Check ($nearbyText -like '*market*right*220px away*') 'an out-of-range target must give a direction and distance'

$historyText = Build-ActUserText -State $nearbyState -Turn 2 -Turns 40 -RecentHistory @('turn 1 @ town/Morning -> advance  (test) ; outcome: ok')
Check ($historyText -like '*Recent turns:*') 'recent history must be included when present'

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

# Attribution: the ONE "gossip: ..." note row matches the keyword scan; the caveat text must be
# present regardless, since the log genuinely cannot prove an attribution EVENT fired (only a count).
Check ($backendSummary.AttributionNoteHits.Count -eq 1) ('fixture must find exactly 1 attribution-shaped note, got ' + $backendSummary.AttributionNoteHits.Count)
Check ($backendSummary.AttributionCaveat -like '*CANNOT directly prove*') 'the attribution caveat must say the log cannot prove an event fired, not just report a count'

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

# --- 9. Personas (U4) -- "five players, not one player five times" ------------------------------
. (Join-Path $toolsDir 'agent-playtest\personas.ps1')

$personasDir = Join-Path $toolsDir 'agent-playtest\prompts\personas'
$actMdPath = Join-Path $toolsDir 'agent-playtest\prompts\act.md'
$actProtocolText = Get-Content $actMdPath -Raw

foreach ($p in @('first-timer', 'veteran', 'speedrunner', 'completionist', 'sceptic')) {
    $resolved = Resolve-PersonaChoice -Persona $p
    Check ($resolved -eq $p) ('a known persona name must resolve to itself, got [' + $resolved + '] for [' + $p + ']')
}

# "random" resolves via the injectable scriptblock (overridable so this is deterministic) to one of
# the five known names -- never a sixth value, never the literal string "random" itself.
$randomResolved = Resolve-PersonaChoice -Persona 'random' -Random { param($items) $items[2] }
Check (@('first-timer', 'veteran', 'speedrunner', 'completionist', 'sceptic') -contains $randomResolved) ('"random" must resolve to one of the five known personas, got [' + $randomResolved + ']')

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

# Two different personas must produce two different assembled prompts and two different hashes --
# "so two runs claiming to be different players can be checked" (the brief's own acceptance test).
$veteranPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'veteran' -PersonasDir $personasDir
$scepticPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'sceptic' -PersonasDir $personasDir
$firstTimerPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'first-timer' -PersonasDir $personasDir
$speedrunnerPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'speedrunner' -PersonasDir $personasDir
$completionistPrompt = Build-PersonaActPrompt -ActProtocolText $actProtocolText -PersonaName 'completionist' -PersonasDir $personasDir

Check ($veteranPrompt -ne $scepticPrompt) 'veteran and sceptic must assemble to DIFFERENT prompt text'
$veteranHash = Get-PromptHash -Text $veteranPrompt
$scepticHash = Get-PromptHash -Text $scepticPrompt
$firstTimerHash = Get-PromptHash -Text $firstTimerPrompt
Check ($veteranHash -ne $scepticHash) ('veteran and sceptic must hash differently, both got [' + $veteranHash + ']')
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
# veteran/completionist/sceptic are SUPPOSED to know the vigil by name (rule 7's relocated content).
$veteranHits = Test-TextForGameNouns -Text $veteranPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($veteranHits.Count -gt 0) 'veteran SHOULD legitimately trip the denylist (it is told about VigilStop on purpose) -- zero hits here would mean this test is vacuous'
Check ($veteranHits -contains 'Vigil') ('veteran''s hits should specifically include "Vigil", got [' + ($veteranHits -join ', ') + ']')
$completionistHits = Test-TextForGameNouns -Text $completionistPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($completionistHits -contains 'Vigil') 'completionist must also carry vigil-specific knowledge per the correction'
$scepticHits = Test-TextForGameNouns -Text $scepticPrompt -Denylist $gameNounDenylist -Allowlist $script:GameNounAllowlist
Check ($scepticHits -contains 'Vigil') 'sceptic must also carry vigil-specific knowledge per the correction'

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

if ($actionSchemaObj) {
    Check ($actionSchemaObj.type -eq 'object') 'action-schema.json must be a flat object schema'
    $actionEnum = @($actionSchemaObj.properties.action.enum)
    Check (($actionEnum | Sort-Object) -join ',' -eq 'advance,key,move,press,stop') ('action-schema.json''s action enum must be exactly the 5 bridge verbs, got [' + ($actionEnum -join ',') + ']')
    # Ruling 1 (docs/plans/2026-08-10-002): the schema must NOT enum the enabled controls -- an
    # illegal press IS signal. This is the mechanical proof that "target" stays an open string.
    $targetPropNames = @($actionSchemaObj.properties.target.PSObject.Properties.Name)
    Check ($targetPropNames -notcontains 'enum') 'action-schema.json''s "target" property must NOT be an enum (ruling 1: an illegal press must stay possible)'
    Check ($actionSchemaObj.properties.target.type -eq 'string') 'action-schema.json''s "target" must be a plain string'
    $dirEnum = @($actionSchemaObj.properties.dir.enum)
    Check (($dirEnum | Sort-Object) -join ',' -eq 'down,down+left,down+right,left,right,up,up+left,up+right') ('action-schema.json''s dir enum must be exactly the 8 cardinal/diagonal directions, got [' + ($dirEnum -join ',') + ']')
    Check ($null -ne $actionSchemaObj.properties.why) 'action-schema.json must have a "why" property'
    Check ($null -ne $actionSchemaObj.properties.note) 'action-schema.json must have an optional "note" property (W4 will use it; added now per the plan to avoid a second schema PR)'
    $requiredFields = @($actionSchemaObj.required)
    Check (($requiredFields | Sort-Object) -join ',' -eq 'action,why') ('action-schema.json must require exactly [action, why], got [' + ($requiredFields -join ',') + ']')
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
    Check (@($parsedBodyWithSchema.format.properties.action.enum) -contains 'press') 'request body''s format.properties.action.enum must contain press (the schema survived the splice as a live object)'
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
Check ($legalDiagonalMove.Refused -eq $false) 'a move with a legal diagonal dir (matching action-schema.json''s own enum) must not be refused'
$illegalDirWord = Get-LegalCommandFromReply -Reply '{"action":"move","dir":"north","why":"nope"}' -EnabledControls @()
Check ($illegalDirWord.Refused -eq $true) 'a move dir outside the known 8 (e.g. "north") must be refused'

# NORMALIZE/regex-extract are DELETED, not relocated -- grep the real script text for the specific
# code shapes that used to live in the per-turn loop (not just the word "NORMALIZE", which the
# deletion's own explanatory comment still legitimately uses).
$agentPlaytestRawText = Get-Content (Join-Path $toolsDir 'agent-playtest.ps1') -Raw
Check ($agentPlaytestRawText -notlike '*[regex]::Match($reply*') 'the old regex JSON-extract call ([regex]::Match($reply, ...)) must be gone from agent-playtest.ps1'
Check ($agentPlaytestRawText -notlike '*normalized "*') 'the old NORMALIZE Say(''normalized "..."'') message must be gone from agent-playtest.ps1'
Check ($agentPlaytestRawText -like '*Get-LegalCommandFromReply*') 'agent-playtest.ps1 must call the new Get-LegalCommandFromReply in its place'
Check ($agentPlaytestRawText -like '*action-schema.json*') 'agent-playtest.ps1 must load action-schema.json and pass it as the act calls'' format'

# Honesty footer (W1): present as a pure, testable function so a scripted/live run is not the only
# way to prove its content -- the live -Scripted run gets its own end-to-end check on top of this.
$footerLines = Get-HonestyFooterLines
$footerText = $footerLines -join [Environment]::NewLine
Check ($footerText -like '*Game feel*') 'the honesty footer must name game feel by that term'
Check ($footerText -like '*Tone register*') 'the honesty footer must name tone register by that term'
Check ($footerText -like '*Emotional weight*') 'the honesty footer must name emotional weight by that term'
Check ($footerText -like '*cannot ask*') 'the honesty footer must say silence on these is not a clean bill'

# --- Summary -----------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host ('FAIL (' + $failures.Count + ' of ' + ($passes + $failures.Count) + '):')
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}

Write-Host ('PASS: agent-playtest Diff/Scout pure logic, ' + $passes + '/' + $passes + ' checks, no Godot/ollama/VRAM needed.')
exit 0
