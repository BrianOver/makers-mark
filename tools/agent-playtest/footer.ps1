<#
.SYNOPSIS
    The honesty footer (W1, docs/plans/2026-08-10-002 "the playtest becomes a player"): a static
    block naming what this instrument structurally cannot see, appended to EVERY findings.md this
    harness ever writes -- zero-turn, Scripted, judge-failed, and a normal successful run alike, so
    silence on game feel, tone, or emotional weight is never mistaken for a clean bill.

.DESCRIPTION
    Unlike act.md's protocol text, this is never sent to a model and never read by one -- it is
    appended directly to the findings.md FILE by agent-playtest.ps1 after the judge (or the run
    itself) has already had its say. The noun-purity guard in personas.ps1/test-agent-playtest-modes
    ps1 governs what the MODEL is told; this footer is a note to the HUMAN reading the report
    afterward, so it is free to name the game by its real vocabulary.

    Extracted to its own file (rather than an inline array literal in agent-playtest.ps1) purely so
    tools/test-agent-playtest-modes.ps1 can assert its content without a live Godot/-Scripted run --
    the live run still gets its own end-to-end check that findings.md actually contains it.

    W4 (docs/plans/2026-08-10-002): -ExtraLines lets a caller append persona-specific caveats after
    the static three -- used exactly once today, for the attached persona's own required disclosure
    that its attachment to a hero was INJECTED by the harness, never formed by the model on its own
    (see attached.ps1's header). Optional and empty by default so every existing caller (every run
    that is not the attached persona) keeps the identical static footer this file has always produced.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

function Get-HonestyFooterLines {
    param([string[]]$ExtraLines = @())

    $lines = @(
        '',
        '---',
        '',
        '## What this instrument cannot see',
        '',
        ('This harness reads structured game state, a screenshot, and a local model''s narration of ' +
         'both. Three things it has no way to measure, so their absence from the findings above is ' +
         'never evidence they are fine:'),
        '',
        ('- **Game feel** -- whether performing an action (the kinetic forge acts, walking, the ' +
         'quench minigame''s timing) actually feels good under a human''s hands. A model narrates ' +
         'what happened; it cannot report how it felt to do.'),
        ('- **Tone register** -- whether the writing lands the way it is meant to (funny, grim, ' +
         'earnest) rather than merely being readable. A 7-14B local model grades prose for content, ' +
         'not for register.'),
        ('- **Emotional weight** -- whether a moment (a hero''s death, a legend line, a memorial ' +
         'entry) actually moves a human the way the design intends. Nothing here can measure that a ' +
         'human was moved.'),
        '',
        ('Silence on any of the three above is the instrument having nothing to say about a question ' +
         'it cannot ask -- not a clean bill. Only a human playtest answers them.')
    )

    if ($ExtraLines -and $ExtraLines.Count -gt 0) {
        $lines = $lines + @('') + $ExtraLines
    }
    return $lines
}
