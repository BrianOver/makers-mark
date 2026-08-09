<#
.SYNOPSIS
    Pure logic: turns one turn's state.json digest into the user-text a local vision model reads.

.DESCRIPTION
    Extracted out of agent-playtest.ps1's main loop so it can be proven with a stubbed state
    object -- no Godot, no ollama, no VRAM -- the same reason scope-map.ps1 exists as its own file.
    See tools/test-agent-playtest-modes.ps1.

    This is also where the "Also do this" follow-up from the shell-around-the-game plan's A5 lands:
    AgentPlaytest.cs's StateDigest has carried a "beat" field (RaidConductor.Current) since A3, but
    the driver built $userText by hand-picking fields off $state and never once read $state.beat --
    so the model was never actually told a vigil was holding the world open, even though the act
    prompt's own rule 7 talks about "if beat reads VigilStop" as though it had been. Line 2 below
    (day/phase/beat/location) is the fix; it is one field added to an existing line, not new
    machinery.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??. Dot-sourced by agent-playtest.ps1, which
    has to survive Windows PowerShell 5.1's BOM and here-string traps -- keep this file plain too.
#>

# $State is whatever ConvertFrom-Json produced from state.json (see AgentPlaytest.cs's StateDigest
# for the field contract this depends on: day, phase, beat, location, canMove, gold,
# actionSlotsRemaining, lastOutcome, screenText, controls[], nearby[], interactPrompt).
function Build-ActUserText {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][int]$Turn,
        [Parameter(Mandatory)][int]$Turns,
        [string[]]$RecentHistory
    )

    if (-not $RecentHistory) { $RecentHistory = @() }

    # An in-range target must NOT be reported as a direction to walk -- see agent-playtest.ps1's own
    # note on this (the first agent run walked "down" into the forge's own footprint eight times
    # from 8px away). Preserved verbatim from the loop this was extracted from.
    $around = ''
    if ($State.nearby -and @($State.nearby).Count -gt 0) {
        $nearbyLines = @(@($State.nearby) | Select-Object -First 6 | ForEach-Object {
            if ($_.inRange) {
                '  ' + $_.key + ' [' + $_.label + '] YOU ARE HERE - press interact to use it (do not walk)'
            } else {
                '  ' + $_.key + ' [' + $_.label + '] ' + $_.direction + ' ' + $_.distance + 'px away'
            }
        })
        $around = 'Around you:' + [Environment]::NewLine + ($nearbyLines -join [Environment]::NewLine)
    }

    $prompt2d = ''
    if ($State.interactPrompt) { $prompt2d = 'Interact prompt on screen: ' + $State.interactPrompt }

    $recent = ''
    if ($RecentHistory.Count -gt 0) {
        $recent = 'Recent turns:' + [Environment]::NewLine + ($RecentHistory -join [Environment]::NewLine)
    }

    return (@(
        ('Turn ' + $Turn + ' of ' + $Turns + '.'),
        ('Day ' + $State.day + ', phase ' + $State.phase + ', beat ' + $State.beat + ', at ' +
            $State.location + '. canMove=' + $State.canMove + '. Gold ' + $State.gold +
            ', action slots left ' + $State.actionSlotsRemaining + '.'),
        ('Last outcome: ' + $State.lastOutcome),
        $prompt2d,
        '',
        'On screen:',
        (($State.screenText | ForEach-Object { '  ' + $_ }) -join [Environment]::NewLine),
        '',
        $around,
        '',
        'Controls:',
        (($State.controls | ForEach-Object { '  ' + $_.name + ' [' + $_.label + '] enabled=' + $_.enabled }) -join [Environment]::NewLine),
        '',
        $recent,
        '',
        'Answer with one JSON object only.'
    ) -join [Environment]::NewLine)
}
