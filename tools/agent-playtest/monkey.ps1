<#
.SYNOPSIS
    Pure logic for W4's -Persona monkey (docs/plans/2026-08-10-002 "the playtest becomes a player"):
    a model-free, seeded uniform-random player -- the null baseline plus a crash/soft-lock census.

.DESCRIPTION
    Ruling 9: "monkey runs skip the judge and the GPU gate entirely -- an essay about uniform-random
    input is noise by construction." This file is the one piece of monkey that IS worth proving: given
    a turn's state, pick uniformly at random among this turn's actually-legal moves and produce the
    exact command.json text agent-playtest.ps1 would write. No ollama, no Godot, no VRAM -- just a
    System.Random the caller seeds once (via the driver's own -Seed parameter) and reuses across the
    whole run, so "same seed twice" means "identical System.Random draw sequence against an identical
    state sequence," never sim determinism (the plan's own required disclosure: this is reproducibility
    of the COMMAND STREAM given identical states, not a determinism claim about the game itself).

    The candidate set is deliberately narrow -- "enabled controls + legal moves + advance," per the
    plan's own wording, not the full five-verb vocabulary: no "key" (interact/cancel targets an
    InputMap action, not a control this file can discover as legal purely from $State.controls/
    $State.nearby without re-deriving the same in-range logic coverage.ps1 already owns) and no "stop"
    (monkey never voluntarily ends its own run -- ruling 9 and the plan's own words, "it runs to
    budget" -- ending early would defeat the crash/soft-lock census this persona exists to produce).
    "advance" is always a candidate, so the set is never empty even with canMove=false and zero enabled
    controls -- there is always at least one legal thing to do.

    Legality here does not need Get-LegalCommandFromReply's own re-check (model-call.ps1): every
    candidate is constructed FROM this turn's own $State.controls[].enabled / $State.canMove, so it is
    legal by construction, the same way the driver's OWN Scripted-mode illegal press is deliberately
    the only place in this codebase where an illegal command is ever produced on purpose.

    Output field order is FIXED (action, then target/dir+frames if present, then why) precisely so two
    runs with the same seed against the same state sequence produce BYTE-IDENTICAL JSON text, not just
    equivalent objects -- ConvertTo-Json's own property order follows an ordered hashtable's insertion
    order, which this file controls explicitly rather than leaving to a PSCustomObject's own field
    declaration order.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

$script:MonkeyMoveDirs = @('up', 'down', 'left', 'right', 'up+left', 'up+right', 'down+left', 'down+right')

# Every legal candidate this turn, in a stable order (enabled controls in $State.controls' own order,
# then the 8 move directions if canMove, then advance last) -- the ORDER candidates are built in does
# not affect which one gets picked (that is $Random's job), but a stable build order means a given
# $Random.Next(count) index always lands on the same candidate for the same state, which is part of
# what makes two same-seed runs byte-identical.
function Get-MonkeyCandidates {
    param([Parameter(Mandatory)]$State)

    $candidates = New-Object System.Collections.ArrayList
    foreach ($c in @($State.controls)) {
        if ($c -and $c.enabled) {
            [void]$candidates.Add([pscustomobject]@{ Action = 'press'; Target = [string]$c.name; Dir = $null })
        }
    }
    if ($State.canMove) {
        foreach ($d in $script:MonkeyMoveDirs) {
            [void]$candidates.Add([pscustomobject]@{ Action = 'move'; Target = $null; Dir = $d })
        }
    }
    [void]$candidates.Add([pscustomobject]@{ Action = 'advance'; Target = $null; Dir = $null })
    return ,@($candidates)
}

# Picks one candidate uniformly via $Random.Next (mutates $Random's internal state -- the caller must
# reuse the SAME System.Random instance across every turn of one run for the sequence to be a real
# seeded stream rather than N independent single draws) and renders it as the exact command.json text
# agent-playtest.ps1 writes to disk. $Random is a parameter (never `Get-Random`, which cannot be
# seeded per-call the way this needs) so a test can hand in two freshly-seeded `System.Random` objects
# and assert their output sequences match exactly.
function Get-MonkeyCommand {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][System.Random]$Random
    )

    $candidates = Get-MonkeyCandidates -State $State
    $index = $Random.Next(0, $candidates.Count)
    $pick = $candidates[$index]

    $obj = [ordered]@{}
    $obj.action = $pick.Action
    if ($pick.Target) { $obj.target = $pick.Target }
    if ($pick.Dir) {
        $obj.dir = $pick.Dir
        $obj.frames = 20
    }
    $obj.why = ('monkey: seeded uniform-random pick, 1 of ' + $candidates.Count + ' legal candidate(s) this turn')

    return (([pscustomobject]$obj) | ConvertTo-Json -Compress)
}
