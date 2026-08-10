<#
.SYNOPSIS
    Pure logic for the completion floor -- "a run that dies early reports itself healthy" (found in
    a 2026-08-09 overnight sweep).

.DESCRIPTION
    Split out of agent-playtest.ps1 for the same reason scope-map.ps1 and turn-prompt.ps1 are: this
    needs zero Godot, zero ollama, zero VRAM to prove, and agent-playtest.ps1 needs all three. Dot
    source this file to test it in isolation (tools/test-agent-playtest-modes.ps1) instead of
    running the real script end to end.

    DEGRADED (agent-playtest.ps1's own $degradeFloor) measures FIDELITY: of the turns that
    happened, how many were the model actually driving versus the harness pressing "advance" for
    it. This measures QUANTITY instead: of the turns that were BUDGETED, how many happened at all.
    A run whose client died, hung, or was talked into quitting after turn 1 has a perfect fallback
    ratio (0 fallbacks of 1 turn) and reads as pristine under DEGRADED alone -- the exact shape of
    self-flattery DEGRADED already exists to catch, in a new place. A 2026-08-09 overnight sweep
    measured four such runs, all reporting verdict=ok, exit=0: Scout-5 (1 of 80 turns), Scout-10
    (9 of 80), Full-1 (25 of 80), Full-5 (36 of 80).

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# Whether a run that stopped at $Turn of a $Turns budget counts as INCOMPLETE, independent of WHY
# it stopped -- a client timeout, a model that quit after repeated refusals, or anything else all
# cost the same thing: a run meant to observe most of a campaign that instead observed a sliver of
# it. The specific cause still belongs in $stopReason; this is only the quantity verdict.
function Get-CompletionVerdict {
    param(
        [Parameter(Mandatory)][int]$Turn,
        [Parameter(Mandatory)][int]$Turns,
        [switch]$Scripted,
        [double]$Floor = 0.5
    )

    $ratio = 1.0
    if ($Turns -gt 0) { $ratio = [double]$Turn / [double]$Turns }

    # Scripted is exempt on purpose: it is a fixed ~5-command channel proof that always stops
    # around turn 5 regardless of -Turns (see agent-playtest.ps1's own .PARAMETER Scripted doc) --
    # the floor would flag every scripted run as INCOMPLETE for doing exactly what it is for.
    $incomplete = (-not $Scripted) -and ($Turn -lt $Turns) -and ($ratio -lt $Floor)

    return [pscustomobject]@{
        Ratio       = $ratio
        PercentText = [math]::Round($ratio * 100, 1)
        Incomplete  = $incomplete
    }
}
