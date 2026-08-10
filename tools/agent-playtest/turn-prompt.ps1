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

    W4 (docs/plans/2026-08-10-002, ruling 2): the "Recent turns:" 6-line history window is GONE, not
    extended -- the model's own scratchpad (the schema's optional "note" field, accumulated by the
    driver into notes.md and handed back in here as $NotesText) REPLACES it outright. The plan names
    this "the Pokemon lesson: removing complexity beat adding it" -- a fixed 6-line window of raw
    turn-by-turn mechanics ("turn 4 @ town/Morning -> press OpenShop") is a worse memory than letting
    the model write down, in its own words, the one or two things it actually wants to remember (a
    plan, a hero's name, a thing not yet tried) and reading THAT back. Get-EchoedNotesText below caps
    what gets echoed at ~2000 chars (oldest content dropped, an explicit trimmed-marker so a capped
    echo is never mistaken for the complete scratchpad) -- the untrimmed full text still lives in
    notes.md on disk; only what rides along in the next prompt is bounded.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??. Dot-sourced by agent-playtest.ps1, which
    has to survive Windows PowerShell 5.1's BOM and here-string traps -- keep this file plain too.
#>

# Caps the model's own accumulated scratchpad text to roughly $MaxChars for the NEXT prompt, oldest
# content dropped first (a model's most recent thought is the one most worth keeping), with an
# explicit marker so a caller can never mistake a trimmed echo for the whole thing. The FULL,
# untrimmed text still lives in notes.md on disk -- this only bounds what rides along in the prompt.
function Get-EchoedNotesText {
    param(
        [string]$FullNotesText,
        [int]$MaxChars = 2000
    )

    if (-not $FullNotesText) { return '' }
    if ($FullNotesText.Length -le $MaxChars) { return $FullNotesText }

    $marker = '(older notes trimmed)'
    $keepChars = $MaxChars - $marker.Length - [Environment]::NewLine.Length
    if ($keepChars -lt 0) { $keepChars = 0 }
    $tail = $FullNotesText.Substring($FullNotesText.Length - $keepChars)
    return ($marker + [Environment]::NewLine + $tail)
}

# $State is whatever ConvertFrom-Json produced from state.json (see AgentPlaytest.cs's StateDigest
# for the field contract this depends on: day, phase, beat, location, canMove, gold,
# actionSlotsRemaining, lastOutcome, screenText, controls[], nearby[], interactPrompt).
function Build-ActUserText {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][int]$Turn,
        [Parameter(Mandatory)][int]$Turns,
        [string]$NotesText = ''
    )

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

    $notesBlock = ''
    if ($NotesText) {
        $echoedNotes = Get-EchoedNotesText -FullNotesText $NotesText -MaxChars 2000
        $notesBlock = 'Your notes so far:' + [Environment]::NewLine + $echoedNotes
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
        $notesBlock,
        '',
        'Answer with one JSON object only.'
    ) -join [Environment]::NewLine)
}
