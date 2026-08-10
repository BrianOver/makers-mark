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
    (Join-Path $toolsDir 'agent-playtest\completion.ps1')
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

# --- Summary -----------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host ('FAIL (' + $failures.Count + ' of ' + ($passes + $failures.Count) + '):')
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}

Write-Host ('PASS: agent-playtest Diff/Scout pure logic, ' + $passes + '/' + $passes + ' checks, no Godot/ollama/VRAM needed.')
exit 0
