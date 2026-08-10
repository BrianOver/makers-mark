<#
.SYNOPSIS
    Pure logic for W5 (docs/plans/2026-08-10-002 "the playtest becomes a player"): scenario cards --
    "did this ONE named behaviour work", answered with quotes, instead of "what did the model notice
    this run" (the existing Full/Diff/Scout scopes).

.DESCRIPTION
    A card is a markdown file at tools/agent-playtest/scenarios/<slug>.md with four sections:

      ## Setup                 -- "fresh" | "continue" | a fenced JSON array of command strings
                                   (the SAME shape as agent-playtest.ps1's own $scriptedPlan --
                                   each element is itself a JSON-encoded command object) replayed
                                   BLIND through that existing scripted-mode plumbing before the
                                   model ever takes a turn. Setup may be blind; play may not
                                   (ruling 3, docs/plans/2026-08-10-002) -- see
                                   Get-ScenarioSetupCommandForTurn's own note on why the driver still
                                   watches state.beat during replay rather than trusting the count.
      ## Brief                 -- player words describing the task. Appended to the ALREADY-
                                   assembled, ALREADY-persona-substituted act prompt (a scenario is a
                                   task layered on a player, never a fifth persona) -- see
                                   Get-ScenarioActPromptAddition.
      ## Expected observation  -- JUDGE-ONLY. Handed to the judge pass alone (see
                                   Get-ScenarioJudgeQuestionText); it must NEVER reach the act prompt
                                   an actor told what to expect reports it, not discovers it. This is
                                   the de-contamination requirement and tools/test-agent-playtest-modes
                                   .ps1 pins it directly: build the REAL assembled act prompt (persona
                                   substitution + the Brief append) and assert the expected-observation
                                   text is nowhere in it.
      ## Backend predicate     -- OPTIONAL. A fenced JSON object {"kind":...,"field":...,"equals":...}
                                   checked against playtest-log.jsonl's own parsed rows with NO model
                                   (Test-ScenarioBackendPredicate) -- a mechanical fact, reported
                                   separately from the judge's own model observation and never blended
                                   into one verdict (the brief's own standing requirement).

    A missing card file, or a card missing any of its three REQUIRED sections (Setup/Brief/Expected
    observation), THROWS -- naming the exact section -- rather than falling back to a plain run. The
    caller (agent-playtest.ps1) turns that into the same loud Die() every other configuration error in
    this file tree already uses (persona resolution, action-schema.json, the GPU gate).

    NO DSL (W5's own binding scope): the backend predicate's {"kind","field","equals"} object is not a
    query language -- it is one fixed, three-key shape, ConvertFrom-Json'd directly, with no operators
    or boolean composition. Same for Setup's command list: a JSON array of pre-built command strings,
    not a mini-language describing how to reach a state.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# Extracts a markdown H2 section's raw body text ("## Heading" through the next "## " heading or end
# of file), or $null if the heading is not present at all. $null (never an empty string) is the
# caller's signal that the section is ABSENT -- an empty string would be indistinguishable from "the
# heading exists but the author left it blank", which is a different (and still malformed) case.
function Get-ScenarioSection {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Heading
    )

    $pattern = '(?mis)^##\s*' + [regex]::Escape($Heading) + '\s*\r?\n(.*?)(?=\r?\n##\s|\z)'
    $m = [regex]::Match($Text, $pattern)
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value.Trim()
}

# A fenced code block's own content (```<any-lang-tag>\n ... ```), trimmed -- or $null if the section
# text carries no fenced block at all. Used by both Setup (its command list) and Backend predicate
# (its {"kind","field","equals"} object) so a card can carry human-readable prose ABOVE the machine
# part (see vigil-runner.md for why that matters: reaching a scripted state is worth explaining).
function Get-ScenarioFencedBlock {
    param([Parameter(Mandatory)][string]$Text)

    $m = [regex]::Match($Text, '(?s)```[a-zA-Z0-9]*\r?\n(.*?)```')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return $null
}

# Setup's own three shapes. "fresh"/"continue" are recognized case-insensitively as the WHOLE
# (trimmed) section text with no fenced block at all; anything else must resolve (directly, or via a
# fenced block) to a JSON array of command STRINGS -- each one itself re-validated as parseable JSON
# here, at card-load time, rather than surfacing as a confusing mid-run ConvertFrom-Json failure on
# the first setup turn.
function ConvertTo-ScenarioSetup {
    param([Parameter(Mandatory)][string]$RawSetupText)

    $trimmed = $RawSetupText.Trim()
    if ($trimmed -ieq 'fresh') {
        return [pscustomobject]@{ Type = 'Fresh'; Commands = @() }
    }
    if ($trimmed -ieq 'continue') {
        return [pscustomobject]@{ Type = 'Continue'; Commands = @() }
    }

    $jsonText = Get-ScenarioFencedBlock -Text $RawSetupText
    if (-not $jsonText) { $jsonText = $trimmed }

    $parsed = $null
    try { $parsed = $jsonText | ConvertFrom-Json -ErrorAction Stop } catch {
        throw ('Setup is not "fresh", "continue", or a parseable JSON command list -- ' + $_.Exception.Message)
    }
    $commands = @($parsed)
    if ($commands.Count -eq 0) {
        throw 'Setup parsed as JSON but produced zero commands -- a scripted prefix must name at least one.'
    }
    foreach ($c in $commands) {
        if ($c -isnot [string]) {
            throw ('Setup''s JSON list must contain command STRINGS (the same shape as ' +
                'agent-playtest.ps1''s own $scriptedPlan -- each element is itself a JSON-encoded ' +
                'command object as TEXT), not a nested JSON object.')
        }
        try { $null = $c | ConvertFrom-Json -ErrorAction Stop } catch {
            throw ('Setup contains a command string that does not itself parse as JSON: ' + $c)
        }
    }
    return [pscustomobject]@{ Type = 'Scripted'; Commands = $commands }
}

# The optional Backend predicate: a fixed three-key shape, never a query language (W5's own "NO DSL"
# scope boundary) -- {"kind":"action","field":"action","equals":"SendSupplyAction"} reads as "does any
# row of kind=action have a field named action whose value CONTAINS SendSupplyAction (case-
# insensitive substring, so an exact field match and a free-text keyword scan both work through the
# same check -- see Test-ScenarioBackendPredicate).
function ConvertTo-ScenarioBackendPredicate {
    param([Parameter(Mandatory)][string]$RawPredicateText)

    $jsonText = Get-ScenarioFencedBlock -Text $RawPredicateText
    if (-not $jsonText) { $jsonText = $RawPredicateText.Trim() }
    if (-not $jsonText) {
        throw ('Backend predicate section is present but empty -- delete the section entirely if ' +
            'this card has none (it is OPTIONAL), or give it a fenced {"kind":...,"field":...,' +
            '"equals":...} block.')
    }

    $parsed = $null
    try { $parsed = $jsonText | ConvertFrom-Json -ErrorAction Stop } catch {
        throw ('Backend predicate does not parse as JSON: ' + $_.Exception.Message)
    }
    foreach ($required in @('kind', 'field', 'equals')) {
        if ($parsed.PSObject.Properties.Name -notcontains $required) {
            throw ('Backend predicate JSON is missing required key "' + $required +
                '" -- need {"kind":"...","field":"...","equals":"..."}')
        }
    }
    return [pscustomobject]@{
        Kind   = [string]$parsed.kind
        Field  = [string]$parsed.field
        Equals = [string]$parsed.equals
    }
}

# The whole card, parsed and validated. Slug is derived from the filename (not stored inside the
# card itself) so a renamed file and its own -Scenario argument can never silently disagree.
#
# THROWS on: a missing file, an empty file, any of the three REQUIRED sections (Setup/Brief/Expected
# observation) absent or blank, or a present-but-malformed Setup/Backend predicate -- every message
# names the file and the section, per this task's own "fails loudly, never falls back to a plain run"
# requirement. Backend predicate is the one OPTIONAL section: absent is a valid, ordinary card.
function Read-ScenarioCard {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path $Path)) {
        throw ('scenario card not found: ' + $Path)
    }
    $raw = Get-Content $Path -Raw
    if (-not $raw -or $raw.Trim().Length -eq 0) {
        throw ('scenario card is empty: ' + $Path)
    }

    $slug = [System.IO.Path]::GetFileNameWithoutExtension($Path)

    $setupRaw = Get-ScenarioSection -Text $raw -Heading 'Setup'
    if (-not $setupRaw) { throw ('scenario card ' + $Path + ' is missing (or has an empty) "## Setup" section') }
    $briefRaw = Get-ScenarioSection -Text $raw -Heading 'Brief'
    if (-not $briefRaw) { throw ('scenario card ' + $Path + ' is missing (or has an empty) "## Brief" section') }
    $expectedRaw = Get-ScenarioSection -Text $raw -Heading 'Expected observation'
    if (-not $expectedRaw) { throw ('scenario card ' + $Path + ' is missing (or has an empty) "## Expected observation" section') }

    $setup = $null
    try { $setup = ConvertTo-ScenarioSetup -RawSetupText $setupRaw } catch {
        throw ('scenario card ' + $Path + ' has a malformed "## Setup" section: ' + $_.Exception.Message)
    }

    $backendPredicate = $null
    $predicateRaw = Get-ScenarioSection -Text $raw -Heading 'Backend predicate'
    if ($null -ne $predicateRaw) {
        try { $backendPredicate = ConvertTo-ScenarioBackendPredicate -RawPredicateText $predicateRaw } catch {
            throw ('scenario card ' + $Path + ' has a malformed "## Backend predicate" section: ' + $_.Exception.Message)
        }
    }

    return [pscustomobject]@{
        Slug                = $slug
        Path                = $Path
        Setup               = $setup
        Brief               = $briefRaw
        ExpectedObservation = $expectedRaw
        BackendPredicate    = $backendPredicate
    }
}

# The act-prompt addition -- appended AFTER Build-PersonaActPrompt's own substitution (a scenario is
# a task layered on a chosen player, never a fifth persona). Contains the Brief ONLY: the Expected
# observation must never reach this function or anything it feeds, by construction (the
# de-contamination pin in tools/test-agent-playtest-modes.ps1 proves exactly that -- it builds the
# real assembled prompt via Build-PersonaActPrompt plus this function and asserts the expected-
# observation text is absent from the result).
function Get-ScenarioActPromptAddition {
    param([Parameter(Mandatory)][string]$Brief)

    return (@(
        '## Scenario task',
        '',
        'On top of everything above, this run has one specific thing to try:',
        '',
        $Brief
    ) -join [Environment]::NewLine)
}

# The judge-only question -- Expected observation lives HERE, never in the act prompt. Appended to
# the judge's own input (agent-playtest.ps1's $judgeInput), alongside the per-day digest, so the judge
# answers from the same log the model actually produced rather than from a description of what a
# player was supposed to try.
function Get-ScenarioJudgeQuestionText {
    param([Parameter(Mandatory)][string]$ExpectedObservation)

    return @(
        'This run also tests one specific scenario. Do not assume the expected observation happened',
        '-- check only what the log above actually shows.',
        '',
        ('Expected observation: ' + $ExpectedObservation),
        '',
        'As the very last line of your entire reply, output EXACTLY one line in this form (nothing',
        'else on that line):',
        '',
        'SCENARIO VERDICT: <CONFIRMED|NOT SEEN|CONTRADICTED>: <a short quote from the log above>'
    )
}

# Parses the judge's raw reply text for the "SCENARIO VERDICT: ..." line Get-ScenarioJudgeQuestionText
# asked for. Verdict is one of CONFIRMED/NOT SEEN/CONTRADICTED, or UNKNOWN when the judge never
# rendered a parseable line at all -- UNKNOWN is reported as its own state (see
# Format-ScenarioVerdictSection), never silently folded into NOT SEEN, which would fabricate a
# negative the judge never actually gave.
function Get-ScenarioVerdictFromJudgeText {
    param([Parameter(Mandatory)][string]$JudgeText)

    $m = [regex]::Match($JudgeText, '(?im)SCENARIO VERDICT\s*:\s*(CONFIRMED|NOT SEEN|CONTRADICTED)\s*:?\s*(.*)')
    if (-not $m.Success) {
        return [pscustomobject]@{ Verdict = 'UNKNOWN'; Quote = '' }
    }
    return [pscustomobject]@{
        Verdict = $m.Groups[1].Value.ToUpperInvariant()
        Quote   = $m.Groups[2].Value.Trim()
    }
}

# The mechanical half -- no model, checked directly against playtest-log.jsonl's own parsed rows
# (agent-playtest\backend.ps1's Read-BackendLogRows output, or an equivalent fixture array in tests).
# "Contains" (case-insensitive substring), not exact-equality: the SendSupplyAction case needs an
# exact field match and a free-text "note" scan needs a substring match, and a single Contains check
# serves both without the card format needing to say which kind of match it wants (further NO-DSL
# simplification -- one predicate shape, one comparison rule).
function Test-ScenarioBackendPredicate {
    param(
        [Parameter(Mandatory)]$Predicate,
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$Rows
    )

    $needle = $Predicate.Equals.ToLowerInvariant()
    $matched = @($Rows | Where-Object {
        ($_.kind -eq $Predicate.Kind) -and
        ($_.PSObject.Properties.Name -contains $Predicate.Field) -and
        ([string]$_.($Predicate.Field)).ToLowerInvariant().Contains($needle)
    })

    $detail = ''
    if ($matched.Count -gt 0) {
        $detail = 'found ' + $matched.Count + ' matching "' + $Predicate.Kind + '" row(s) where ' +
            $Predicate.Field + ' contains "' + $Predicate.Equals + '"'
    } else {
        $detail = 'no "' + $Predicate.Kind + '" row with ' + $Predicate.Field + ' containing "' +
            $Predicate.Equals + '" was found in ' + $Rows.Count + ' row(s)'
    }

    return [pscustomobject]@{
        Present    = ($matched.Count -gt 0)
        MatchCount = $matched.Count
        Detail     = $detail
    }
}

# The "## Scenario verdict" section -- written ABOVE the model's own prose in findings.md (the same
# "recorded/measured facts first, the model's account second" ordering backend.ps1/metrics.ps1 already
# use). The model observation and the backend predicate are printed as two SEPARATE facts and never
# combined into one boolean -- "mechanical fact vs model observation, never blended" is this unit's
# own standing requirement, not a style preference.
function Format-ScenarioVerdictSection {
    param(
        [Parameter(Mandatory)]$Card,
        [Parameter(Mandatory)]$JudgeVerdict,
        $BackendResult
    )

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('## Scenario verdict')
    [void]$lines.Add('')
    [void]$lines.Add('Scenario: ' + $Card.Slug)
    [void]$lines.Add('Brief given to the actor: ' + $Card.Brief)
    [void]$lines.Add('Expected observation (JUDGE-ONLY -- never shown to the actor): ' + $Card.ExpectedObservation)
    [void]$lines.Add('')
    [void]$lines.Add('Model observation: ' + $JudgeVerdict.Verdict)
    if ($JudgeVerdict.Verdict -eq 'UNKNOWN') {
        [void]$lines.Add('(the judge did not render a parseable "SCENARIO VERDICT:" line -- treat this as inconclusive, never as NOT SEEN)')
    } elseif ($JudgeVerdict.Quote) {
        [void]$lines.Add('Quote: ' + $JudgeVerdict.Quote)
    }
    [void]$lines.Add('')
    if ($null -eq $Card.BackendPredicate) {
        [void]$lines.Add('Backend predicate: none defined for this card.')
    } elseif ($null -eq $BackendResult) {
        [void]$lines.Add('Backend predicate: defined but not evaluated (no backend log available this run).')
    } else {
        $presentText = 'ABSENT'
        if ($BackendResult.Present) { $presentText = 'PRESENT' }
        [void]$lines.Add('Backend predicate (mechanical fact, independent of the model): ' + $presentText)
        [void]$lines.Add('Detail: ' + $BackendResult.Detail)
    }
    [void]$lines.Add('')
    [void]$lines.Add('Mechanical fact and model observation are reported separately above -- never blended into one verdict.')

    return ($lines -join [Environment]::NewLine)
}
