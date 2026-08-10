<#
.SYNOPSIS
    Pure logic for W4's attached.md persona (docs/plans/2026-08-10-002 "the playtest becomes a
    player"): track one named hero across a run, notice their death, and check whether the death
    screen itself carried the player's work.

.DESCRIPTION
    The persona's own knowledge is deliberately thin -- "heroes exist and die permanently"; its goal
    is "keep one named hero alive" -- and everything this file does is mechanical support for that,
    never a second opinion on whether the model is playing it well:

      1. Get-AttachedHeroNameFromNote -- once, on whichever turn the model first writes a hero's name
         into the schema's optional "note" field (attached.md's own protocol asks it to do this the
         first time it sees one), the driver records that exact text as the hero to watch.
      2. Test-ScreenTextForHeroDeath -- every later turn, scans that turn's screenText for a line that
         BOTH names the recorded hero AND uses death vocabulary. Both conditions are required on
         purpose: the hero's name alone appears constantly (shop lines, ledger entries) and would fire
         on nearly every turn; death vocabulary alone could hit an unrelated hero's death.
      3. Test-ScreenTextForAttribution -- reuses metrics.ps1's OWN product-sentence keyword pattern
         ($script:ProductSentenceKeywordPattern) rather than a second copy, per the plan's own
         instruction ("reuse metrics.ps1's product-sentence matcher, do not duplicate it"). This file
         must be dot-sourced AFTER metrics.ps1 for that pattern to be in scope -- agent-playtest.ps1's
         own dot-source order enforces this (see its file list).

    The attachment here is INJECTED by the harness (the persona TELLS the model to name a hero and
    tells the driver to inject "<name> is dead." the turn a death is detected, per the plan) -- this
    file's own functions never claim to measure whether attachment "formed" on its own. What the
    resulting run measures is narrower and mechanical: did the game's own screen, at or after that
    death, ever say anything attribution-shaped. agent-playtest.ps1 carries that distinction forward
    into the honesty footer on every attached run (footer.ps1's own -ExtraLines parameter).

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# Death vocabulary kept narrow and literal -- not a broad "sad words" scan -- so this only fires on an
# actual death report next to the hero's own name, never on ordinary combat flavor text that happens
# to share a word with it.
$script:HeroDeathKeywordPattern = '(?i)(fell|died|memorial|lost)'

# The model's own words, verbatim and trimmed -- this file trusts what the model wrote into "note"
# rather than trying to parse a name out of freeform prose. An empty/whitespace-only note is treated
# as "no name given yet," not a hero literally named "".
function Get-AttachedHeroNameFromNote {
    param([string]$Note)

    if (-not $Note) { return $null }
    $trimmed = $Note.Trim()
    if (-not $trimmed) { return $null }
    return $trimmed
}

# BOTH the hero's own name AND death vocabulary must appear on the SAME screenText line -- see this
# file's own header for why either alone is not enough. $ScreenTextLines may be $null/empty (never
# throws); a blank/null $HeroName (no name recorded yet) always reports no death, never a false match
# against every line.
function Test-ScreenTextForHeroDeath {
    param(
        [string]$HeroName,
        [array]$ScreenTextLines
    )

    if (-not $HeroName) { return $false }
    foreach ($line in $ScreenTextLines) {
        if (-not $line) { continue }
        $text = [string]$line
        if ($text -notmatch [regex]::Escape($HeroName)) { continue }
        if ($text -match $script:HeroDeathKeywordPattern) { return $true }
    }
    return $false
}

# Reuses metrics.ps1's own $script:ProductSentenceKeywordPattern -- see this file's header for why
# duplicating that pattern here would be exactly the second-copy-silently-rots risk this repo already
# fixed once (personas.ps1's own Get-GameNounDenylist note).
function Test-ScreenTextForAttribution {
    param([array]$ScreenTextLines)

    foreach ($line in $ScreenTextLines) {
        if ($line -and ([string]$line -match $script:ProductSentenceKeywordPattern)) { return $true }
    }
    return $false
}
