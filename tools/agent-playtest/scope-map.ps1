<#
.SYNOPSIS
    Pure logic for -Scope Diff (A4, "the shell around the game" plan): which real-world surface a
    changed file maps to, and the act-prompt section that names it.

.DESCRIPTION
    Split out of agent-playtest.ps1 for exactly one reason: this logic needs zero Godot, zero
    ollama, and zero VRAM to prove, and the harness needs all three. Dot-source this file to test
    it in isolation (see tools/test-agent-playtest-modes.ps1) the same way scope-map.ps1's caller
    cannot be exercised end to end on a machine with no spare client and no free VRAM.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??. Same reason as agent-playtest.ps1's own
    note -- this file is dot-sourced by it and must survive the same Windows PowerShell 5.1 traps.

.NOTES
    Scope boundary (A4): this is a prompt plus a file-to-surface map, nothing more. When a changed
    file matches no pattern below, the caller is told so and must fall back to the full sweep
    LOUDLY -- that is the whole lesson A1 exists to teach, applied to a second silent-success trap.
#>

# Every path is normalized to forward slashes before matching, since git always reports them that
# way but a caller on Windows could plausibly pass a backslash path from somewhere else.
function Get-ScopeMapSurface {
    param([Parameter(Mandatory)][string]$Path)

    $p = $Path -replace '\\', '/'

    # --- most specific first: named panels, minigames, town input -----------------------------
    if ($p -match '(?i)^godot/scripts/panels/CounterPanel\.cs$') {
        return 'the counter service flow (Present/Suggest/Close/Haggle), inside the shop building'
    }
    if ($p -match '(?i)^godot/scripts/panels/([A-Za-z]+)Panel\.cs$') {
        return ($Matches[1] + ' panel -- open it from the HUD/drawer and look')
    }
    if ($p -match '(?i)^godot/scenes/panels/([A-Za-z]+?)(Panel)?\.tscn$') {
        return ($Matches[1] + ' panel''s layout (scene file) -- open it and look for anything misplaced')
    }
    if ($p -match '(?i)^godot/scripts/minigames/ForgeMinigame\.cs$') {
        return 'the Forge minigame (bellows + strikes, inside the Forge panel)'
    }
    if ($p -match '(?i)^godot/scripts/minigames/QuenchMinigame\.cs$') {
        return 'the Quench minigame (the forge''s second act, the plunge)'
    }
    if ($p -match '(?i)^godot/scripts/minigames/AlchemyBrewPuzzle\.cs$') {
        return 'the Alchemy brew minigame'
    }
    if ($p -match '(?i)^godot/scripts/minigames/TanningFrame\.cs$') {
        return 'the Tanning minigame'
    }
    if ($p -match '(?i)^godot/scripts/minigames/EngineeringBench\.cs$') {
        return 'the Engineering minigame'
    }
    if ($p -match '(?i)^godot/scripts/minigames/') {
        return 'every profession minigame (shared input handling) -- open any craft panel and craft'
    }
    if ($p -match '(?i)^godot/scripts/town2d/PlayerController2D\.cs$') {
        return 'walking the town (movement itself)'
    }
    if ($p -match '(?i)^godot/scripts/town2d/WorldInput2D\.cs$') {
        return 'the interact prompt and walking into buildings'
    }
    if ($p -match '(?i)^godot/scripts/town2d/') {
        return 'the town (walking, buildings, interiors, ambient life)'
    }
    if ($p -match '(?i)^godot/scripts/audio/') {
        return 'audio (music/narrator/SFX) -- no single screen, listen across the whole run'
    }
    if ($p -match '(?i)^godot/scripts/ui/SettingsPanel\.cs$') {
        return 'the settings menu'
    }
    if ($p -match '(?i)^godot/scripts/ui/Tutorial') {
        return 'the day 1-3 tutorial overlay'
    }
    if ($p -match '(?i)^godot/scripts/ui/') {
        return 'shared UI chrome used by every panel -- check several panels, not just one'
    }
    if ($p -match '(?i)^godot/scripts/tools/') {
        return '(dev tool, not player-facing -- nothing to look at in a normal playthrough)'
    }

    # --- sim modules -> the screens that render them -------------------------------------------
    if ($p -match '(?i)^sim/GameSim/Crafting/') {
        return 'the Forge/Alchemy/Tanning/Engineering panels and their minigames'
    }
    if ($p -match '(?i)^sim/GameSim/Heroes/') {
        return 'the Heroes panel, hero cards, and the tavern'
    }
    if ($p -match '(?i)^sim/GameSim/Expedition/') {
        return 'the Camp (vigil) panel, the Depths panel, and the raid/Delve stage'
    }
    if ($p -match '(?i)^sim/GameSim/Economy/') {
        return 'the Shop panel, the Ledger, and the Demand panel'
    }
    if ($p -match '(?i)^sim/GameSim/Drama/') {
        return 'the Legends wall, the Chronicle scroll, and gossip on the night ticker'
    }
    if ($p -match '(?i)^sim/GameSim/Bounties/') {
        return 'the Bounties panel and the Commission board'
    }
    if ($p -match '(?i)^sim/GameSim/Counter/') {
        return 'the counter service flow (Present/Suggest/Close/Haggle)'
    }
    if ($p -match '(?i)^sim/GameSim/Arc/') {
        return 'the HUD arc chip and the Progression panel'
    }
    if ($p -match '(?i)^sim/GameSim/Advisor/') {
        return 'the advisor objective chip on the HUD'
    }
    if ($p -match '(?i)^sim/GameSim/Chronicle/') {
        return 'the Legends wall and the Chronicle scroll'
    }
    if ($p -match '(?i)^sim/GameSim/Progression/') {
        return 'the Progression panel and talent trees'
    }
    if ($p -match '(?i)^sim/GameSim/Factions/') {
        return 'the faction standing chips on the HUD and the night''s ore offers'
    }
    if ($p -match '(?i)^sim/GameSim/Materials/') {
        return 'the Shop panel (materials shelf) and the Foundry'
    }
    if ($p -match '(?i)^sim/GameSim/Narrative/') {
        return 'the night ledger, gossip lines, and the narrator'
    }
    if ($p -match '(?i)^sim/GameSim/Venues/') {
        return 'the three live venues -- which panels are reachable where'
    }
    if ($p -match '(?i)^sim/GameSim/Classes/') {
        return 'the Heroes panel and hero cards'
    }
    if ($p -match '(?i)^sim/GameSim/Professions/') {
        return 'the Progression panel (profession swap) and every craft panel'
    }
    if ($p -match '(?i)^sim/GameSim/Presentation/') {
        return 'whatever panel the changed presentation contract feeds -- check the Evening ledger first'
    }
    if ($p -match '(?i)^sim/GameSim/(Kernel|Contracts|Harness)/') {
        return 'no single surface -- this is core substrate everything downstream depends on; sweep broadly'
    }

    # --- resolved, but nothing on screen to look at ---------------------------------------------
    if ($p -match '(?i)^(docs|\.github|tools)/') {
        return '(non-player-facing: docs/CI/tooling -- nothing to look at)'
    }
    if ($p -match '(?i)^(sim/GameSim\.Tests|godot/tests)/') {
        return '(test-only change -- nothing new on screen)'
    }
    if ($p -match '(?i)\.md$') {
        return '(documentation -- nothing to look at)'
    }

    return $null
}

# Runs the actual git diff. Returns an empty array (never $null) on ANY failure -- git missing,
# origin/main not fetched, not a git repo -- so the caller's own "zero changed files" branch is the
# single place that has to reason about "we could not tell what changed."
function Get-ChangedFilesAgainstMain {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $raw = $null
    try {
        $raw = & git -C $RepoRoot diff --name-only 'origin/main...HEAD'
    } catch {
        return ,@()
    }
    if ($LASTEXITCODE -ne 0) {
        return ,@()
    }
    if (-not $raw) {
        return ,@()
    }
    return @($raw | Where-Object { $_ -and $_.Trim().Length -gt 0 })
}

# The text block to append to the act prompt, plus the honesty flags the caller needs to log and
# to put in findings.md. FellBack is true whenever the Diff scope cannot honestly narrow anything
# -- either there is nothing to point at, or the map does not understand part of what changed --
# and in both cases the run must say so loudly rather than quietly acting like a full sweep was a
# deliberate Diff-scoped result.
function Get-ScopeDiffSection {
    param([string[]]$ChangedFiles)

    if (-not $ChangedFiles) { $ChangedFiles = @() }

    $lines = New-Object System.Collections.ArrayList
    $unresolvedList = New-Object System.Collections.ArrayList

    foreach ($f in $ChangedFiles) {
        $surface = Get-ScopeMapSurface $f
        if ($null -eq $surface) {
            [void]$unresolvedList.Add($f)
            [void]$lines.Add('  - ' + $f + ' -> UNRESOLVED (no mapping)')
        } else {
            [void]$lines.Add('  - ' + $f + ' -> ' + $surface)
        }
    }

    $fellBack = ($ChangedFiles.Count -eq 0) -or ($unresolvedList.Count -gt 0)

    $text = ''
    if ($ChangedFiles.Count -eq 0) {
        $text = 'DIFF SCOPE FELL BACK TO FULL SWEEP: no files differ from origin/main (or the diff ' +
            'could not be read). There is nothing "just deployed" to point at here -- play the whole game.'
    } elseif ($unresolvedList.Count -gt 0) {
        $text = 'DIFF SCOPE PARTIALLY FELL BACK: ' + $unresolvedList.Count + ' of ' + $ChangedFiles.Count +
            ' changed file(s) have no known surface mapping. Sweep the WHOLE game, not just the list ' +
            'below -- an unmapped change could be anywhere.' + [Environment]::NewLine + [Environment]::NewLine +
            'What changed today (go look at this first, but do not stop there):' + [Environment]::NewLine +
            ($lines -join [Environment]::NewLine)
    } else {
        $text = 'WHAT CHANGED TODAY -- go look at this first, before anything else:' + [Environment]::NewLine +
            ($lines -join [Environment]::NewLine) + [Environment]::NewLine + [Environment]::NewLine +
            'Every changed file maps to a known surface above. Explore the rest of the game too if there ' +
            'is time, but these are the priority.'
    }

    return [pscustomobject]@{
        Text = $text
        ChangedCount = $ChangedFiles.Count
        UnresolvedCount = $unresolvedList.Count
        Unresolved = @($unresolvedList)
        FellBack = $fellBack
    }
}
