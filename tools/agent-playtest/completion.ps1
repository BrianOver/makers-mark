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

# The THIRD honesty gauge, and the one whose absence cost this project a whole night of fake data.
#
# DEGRADED asks "were the turns real decisions?". INCOMPLETE asks "did enough turns happen?". Neither
# one asks the question a person watching the screen asks immediately: DID ANYTHING HAPPEN? A run can
# burn every budgeted turn, model-driven, zero fallbacks, and finish verdict=ok exit=0 while every
# single input was swallowed before it reached the game. That is not hypothetical -- it is the
# 2026-08-11 ten-rounds campaign, which reported "78 runs, zero crashes" over runs where
# AgentPlaytest.ApplyKey used Viewport.PushInput and therefore never updated the polled input state
# WorldInput2D reads, so EVERY 'interact' in EVERY run was a no-op. The owner found it by opening the
# game and watching it sit there. The harness had no opinion.
#
# INERT is that opinion. A turn is inert when the command the driver sent was an ACTING command (not
# 'advance', which is allowed to change nothing but the clock) and the screen digest afterwards is
# byte-identical to the digest before. One inert turn is ordinary -- a refused press, a walk into a
# wall. A run made mostly of them did not test the game, and must not be allowed to report findings
# as though it had.
#
# Deliberately NOT reused here: the existing STUCK detector fires on `$digestSeen[$digest] -eq 4`,
# which is an exact-equality trip -- it warns ONCE per distinct digest and never again, so a run
# frozen for 400 turns emits a single line and keeps going. It is a note, not a gauge. This is the
# gauge.
function Get-InertVerdict {
    param(
        [Parameter(Mandatory)][int]$InertTurns,
        [Parameter(Mandatory)][int]$ActingTurns,
        [switch]$Scripted,
        [double]$Floor = 0.5,
        [int]$MinActingTurns = 8
    )

    $ratio = 0.0
    if ($ActingTurns -gt 0) { $ratio = [double]$InertTurns / [double]$ActingTurns }

    # Two guards against firing on a run too small to judge. MinActingTurns: a 3-turn channel proof
    # that happens to refuse twice is not an inert run, it is a tiny sample. Scripted: same exemption
    # INCOMPLETE grants it, and for the same reason -- Scripted deliberately sends an illegal press to
    # prove the refusal path works, so a high inert ratio there is the mode succeeding.
    $inert = (-not $Scripted) -and ($ActingTurns -ge $MinActingTurns) -and ($ratio -ge $Floor)

    return [pscustomobject]@{
        Ratio       = $ratio
        PercentText = [math]::Round($ratio * 100, 1)
        Inert       = $inert
    }
}
