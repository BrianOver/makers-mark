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
    (Join-Path $toolsDir 'agent-playtest\mechanical.ps1')
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

# --- Summary -----------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host ('FAIL (' + $failures.Count + ' of ' + ($passes + $failures.Count) + '):')
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}

Write-Host ('PASS: agent-playtest Diff/Scout pure logic, ' + $passes + '/' + $passes + ' checks, no Godot/ollama/VRAM needed.')
exit 0
