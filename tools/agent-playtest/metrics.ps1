<#
.SYNOPSIS
    Pure logic for W2 (docs/plans/2026-08-10-002 "the playtest becomes a player"): fun measured
    mechanically instead of narrated by a 7B, and the judge's own input built so it can actually see
    the whole run instead of a tail-trimmed slice of it.

.DESCRIPTION
    Four instruments, all derived from data the driver already holds in memory or already writes to
    playtest-log.jsonl -- none of them call ollama, none of them need Godot:

    1. Get-PerDayActionEntropy -- Shannon entropy (bits) of the action-type distribution WITHIN each
       in-game day, combined from two sources on purpose: the driver's own per-turn record of what
       verb the player actually chose (TurnRecords), and playtest-log.jsonl's "action" kind rows (the
       KERNEL's own action-type record -- see backend.ps1's Get-BackendSummary, .ActionRows). Either
       source alone is a partial picture: TurnRecords only ever sees the five bridge verbs
       (press/move/key/advance/stop), which flattens every day to looking the same; the backend log's
       action rows are richer (real kernel action types) but only exist for immediately-resolved
       actions. Falling entropy day over day, or entropy that never moves, is the day-11 instrument
       the plan asks for -- it answers "did the shape of what the player did change" with no model in
       the loop at all.
    2. Get-LegalVsChosenByPhase -- for each DayPhase, the union of controls that were ever ENABLED
       across every turn spent in that phase, against the union of controls actually PRESSED. A phase
       where five controls light up and one gets pressed, every run, is a phase the player never had a
       reason to explore -- this is a per-phase denominator the way coverage.ps1 is a per-surface one.
    3. Get-RefusalFrustrationMap -- every refusal, ranked by control, from BOTH sides that can refuse:
       the driver's own pre-send legality check (Get-LegalCommandFromReply, model-call.ps1 -- an
       illegal press/key/move the driver caught before ever sending it to the client) and the kernel's
       own rejects[] accumulator (backend.ps1's Get-BackendRejections -- the client sent it, the SIM
       said no). Two different refusal mechanisms, one ranked list, because a control that frustrates
       the player does not care which layer said no.
    4. Get-ProductSentenceReport -- the actual instrument on THE-GAME.md links 4-5. Two independent
       signals, kept separate and both reported: did the backend log corroborate an attribution beat
       (Get-BackendSummary's own AttributionNoteHits -- a best-effort text scan of free-text "note"
       rows -- OR AttributionEventTypeHits -- EXACT since 2026-08-11, a tick row whose eventTypes
       field names the sim's AttributionBeatEvent by type, PlaytestLog.Tick's own addition; either
       one is a real backend hit, and AttributionCaveat is carried forward verbatim describing
       whichever this run's log can and cannot prove), and did the PLAYER'S OWN SCREEN ever show
       attribution-shaped text (a best-effort scan of every turn's screenText, the only channel that
       answers what a human actually watched happen). "Fired" for sweep purposes means the FIRST one
       (a real backend hit, note or event type): a screen-only match is UI copy this pattern happens
       to catch, not proof the sim actually recorded the beat -- see the U2 regression this gate
       exists to close, in the function's own doc below.

    Plus Build-PerDayJudgeDigest, which replaces agent-playtest.ps1's $judgeCap tail-trim (W1's own
    interim 24000-char raise, still a front-trim). The live defect it kills: at any fixed character
    cap, trimming FROM THE FRONT means a long run's judge input is always "whatever happened most
    recently" -- a 57 KB log at a 6000-char cap showed the judge roughly the last 2-3 turns, so a
    question like "did day 11 change shape from day 2" was unanswerable in principle, not just in
    practice, because day 2 had already fallen off the front. This builds one block of text PER DAY
    (phase sequence, every turn's action/outcome/refusal/screenText) and, only if the total still
    exceeds the character budget, THINS EVERY DAY'S BLOCK DOWN TOWARD A FLOOR (first N / last N turns
    per day, never below 4) rather than ever dropping a day's block outright. A day that happened is
    always represented by at least a few lines, however long the run got.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by
    (agent-playtest.ps1's own note: Windows PowerShell 5.1 reads a BOM-less UTF-8 file as ANSI and
    treats an indented here-string terminator as a parse error).
#>

# Mirrors backend.ps1's own $script:AttributionKeywordPattern rather than importing it -- this file
# must stand alone and be dot-sourceable/testable with no other file loaded first (same reason
# model-call.ps1 duplicates coverage.ps1's five bridge verbs instead of dot-sourcing that file). Kept
# textually identical on purpose: MakersMark, signed, memorial, heirloom, gossip, legend are the sim
# event vocabulary (sim/GameSim/Contracts/Events.cs: AttributionBeatEvent, GossipEmitted, ItemSigned,
# MemorialHonored, HeirloomReforged) that carries a player's mark forward into the town's memory.
$script:ProductSentenceKeywordPattern = '(?i)(attribution|signed|memorial|heirloom|gossip|legend|makersmark|maker''?s mark)'

# --- 1. Per-day action entropy -------------------------------------------------------------------

# log2(p), hand-rolled rather than [Math]::Log($p, 2) purely so the ONE call site that needs a
# non-integer log base is named and commented once rather than repeated -- .NET's Math.Log(x, base) is
# used either way. This is a tools/ script, not sim/GameSim/ -- CLAUDE.md's transcendental-Math ban
# (rule 4, cross-OS float drift in the DETERMINISTIC kernel) does not reach this file; a playtest
# metric is not a replay-determinism surface.
function Get-Log2 {
    param([double]$Value)
    return [Math]::Log($Value, 2)
}

# TurnRecords: array of {Day; Action; ...} (extra fields ignored) -- the driver's own per-turn record
# of the verb it chose to send this turn (agent-playtest.ps1 builds one of these every turn, model or
# Scripted). BackendActionRows: array of {day; action} (lowercase -- playtest-log.jsonl's own field
# names, PlaytestLog.cs's "action" kind rows, exposed as Get-BackendSummary's .ActionRows). Either
# array may be empty or $null; this never throws on that, and a day present in only one source still
# gets an entropy value from whatever it has.
function Get-PerDayActionEntropy {
    param(
        [array]$TurnRecords,
        [array]$BackendActionRows
    )

    $byDay = @{}
    foreach ($t in $TurnRecords) {
        if ($null -eq $t.Day) { continue }
        $key = [string]$t.Day
        if (-not $byDay.ContainsKey($key)) { $byDay[$key] = New-Object System.Collections.ArrayList }
        $a = [string]$t.Action
        if (-not $a) { $a = '(none)' }
        [void]$byDay[$key].Add($a)
    }
    foreach ($r in $BackendActionRows) {
        if ($null -eq $r.day) { continue }
        $key = [string]$r.day
        if (-not $byDay.ContainsKey($key)) { $byDay[$key] = New-Object System.Collections.ArrayList }
        $a = [string]$r.action
        if (-not $a) { $a = '(none)' }
        [void]$byDay[$key].Add($a)
    }

    $result = @()
    foreach ($key in ($byDay.Keys | Sort-Object { [double]$_ })) {
        $actions = @($byDay[$key])
        $total = $actions.Count
        $counts = @{}
        foreach ($a in $actions) {
            if ($counts.ContainsKey($a)) { $counts[$a] = $counts[$a] + 1 } else { $counts[$a] = 1 }
        }
        $entropy = 0.0
        foreach ($k in $counts.Keys) {
            $p = [double]$counts[$k] / [double]$total
            $entropy = $entropy - ($p * (Get-Log2 $p))
        }
        $countRows = @()
        foreach ($k in ($counts.Keys | Sort-Object)) { $countRows += [pscustomobject]@{ Action = $k; Count = $counts[$k] } }
        $result += [pscustomobject]@{
            Day                 = $key
            TotalActions        = $total
            DistinctActionTypes = $counts.Keys.Count
            ActionCounts        = $countRows
            EntropyBits         = [math]::Round($entropy, 4)
        }
    }
    return ,@($result)
}

# --- 2. LEGAL-vs-CHOSEN ratio per phase -----------------------------------------------------------

# TurnRecords: array of {Phase; Action; Target; EnabledControls (array of names seen enabled THIS
# turn); ...}. LegalCount is the size of the UNION of enabled-control-names across every turn spent in
# that phase (a control enabled on turn 3 and gone by turn 9 still counts -- it was a real option at
# some point in the phase); ChosenCount is the union of press targets actually sent, intersected with
# the legal set so a target that was NEVER actually legal (should not happen, but this file trusts
# nothing) cannot inflate the numerator past the denominator.
function Get-LegalVsChosenByPhase {
    param([array]$TurnRecords)

    $byPhase = @{}
    $order = New-Object System.Collections.ArrayList
    foreach ($t in $TurnRecords) {
        $phase = [string]$t.Phase
        if (-not $phase) { $phase = '(unknown)' }
        if (-not $byPhase.ContainsKey($phase)) {
            $byPhase[$phase] = [pscustomobject]@{ Legal = @{}; Chosen = @{} }
            [void]$order.Add($phase)
        }
        foreach ($c in @($t.EnabledControls)) {
            if ($c) { $byPhase[$phase].Legal[$c] = $true }
        }
        if ($t.Action -eq 'press' -and $t.Target) { $byPhase[$phase].Chosen[[string]$t.Target] = $true }
    }

    $result = @()
    foreach ($phase in ($order | Select-Object -Unique | Sort-Object)) {
        $row = $byPhase[$phase]
        $legalControls = @($row.Legal.Keys | Sort-Object)
        $chosenControls = @($row.Chosen.Keys | Where-Object { $row.Legal.ContainsKey($_) } | Sort-Object)
        $legalCount = $legalControls.Count
        $chosenCount = $chosenControls.Count
        $ratio = 0.0
        if ($legalCount -gt 0) { $ratio = [double]$chosenCount / [double]$legalCount }
        $result += [pscustomobject]@{
            Phase          = $phase
            LegalCount     = $legalCount
            ChosenCount    = $chosenCount
            Ratio          = $ratio
            RatioPct       = [math]::Round($ratio * 100, 1)
            LegalControls  = $legalControls
            ChosenControls = $chosenControls
        }
    }
    return ,@($result)
}

# --- 3. Refusals-by-control frustration map -------------------------------------------------------

# model-call.ps1's Get-LegalCommandFromReply returns a Reason STRING, not a structured target -- this
# file is not in W2's own edit list (see metrics.ps1's header), so rather than change that return
# shape, this parses the fixed Reason prefixes it emits (its own source, unchanged): "disabled/
# absent control: <name>", 'illegal key target: "<name>" ...', 'illegal/missing move dir: "<name>" ...',
# plus U1 (eyes-learn-labels wave)'s two new press-specific shapes: "empty press target -- ..." and
# 'ambiguous label "<label>" matches N enabled controls: ...'. A captured name that is itself blank
# (the model sent an empty/missing target or dir -- the exact "key"/"move" gaps model-call.ps1's own
# header names as found-live defects) gets a NAMED fallback rather than a blank map row nobody could
# read; anything else this file does not recognize at all (an unrecognized-action or empty-reply
# refusal, which never names a control) maps to '(unspecified)'.
function Get-RefusalControlFromReason {
    param([string]$Reason)

    if (-not $Reason) { return '(unspecified)' }
    $m = [regex]::Match($Reason, '^disabled/absent control:\s*(.*)$')
    if ($m.Success) {
        $name = $m.Groups[1].Value.Trim()
        if ($name) { return $name }
        return '(press: no/empty target)'
    }
    $m = [regex]::Match($Reason, '^illegal key target:\s*"([^"]*)"')
    if ($m.Success) {
        $name = $m.Groups[1].Value.Trim()
        if ($name) { return $name }
        return '(key: no/empty target)'
    }
    $m = [regex]::Match($Reason, '^illegal/missing move dir:\s*"([^"]*)"')
    if ($m.Success) {
        $name = $m.Groups[1].Value.Trim()
        if ($name) { return $name }
        return '(move: no/empty dir)'
    }
    if ($Reason -like 'empty press target*') {
        return '(press: no/empty target)'
    }
    $m = [regex]::Match($Reason, '^ambiguous label "([^"]*)"')
    if ($m.Success) {
        return ('(ambiguous label: "' + $m.Groups[1].Value + '")')
    }
    return '(unspecified)'
}

# PreRefusals: array of {Control; Reason} -- the driver's own pre-send catches (agent-playtest.ps1's
# attempts loop, one entry per refused attempt, BEFORE anything reaches the client). BackendRejections:
# array of {Action; Why} -- backend.ps1's Get-BackendRejections output (Day/Phase/Action/Why; only
# Action/Why are used here). Ranked by total count descending, ties broken alphabetically by control
# name so the output is deterministic and fixture-testable.
function Get-RefusalFrustrationMap {
    param(
        [array]$PreRefusals,
        [array]$BackendRejections
    )

    $rows = @{}
    foreach ($p in $PreRefusals) {
        $c = [string]$p.Control
        if (-not $c) { $c = '(unspecified)' }
        if (-not $rows.ContainsKey($c)) {
            $rows[$c] = [pscustomobject]@{ Control = $c; PreRefusedCount = 0; BackendRejectedCount = 0; Reasons = New-Object System.Collections.ArrayList }
        }
        $rows[$c].PreRefusedCount = $rows[$c].PreRefusedCount + 1
        if ($p.Reason) { [void]$rows[$c].Reasons.Add([string]$p.Reason) }
    }
    foreach ($r in $BackendRejections) {
        $c = [string]$r.Action
        if (-not $c) { $c = '(unspecified)' }
        if (-not $rows.ContainsKey($c)) {
            $rows[$c] = [pscustomobject]@{ Control = $c; PreRefusedCount = 0; BackendRejectedCount = 0; Reasons = New-Object System.Collections.ArrayList }
        }
        $rows[$c].BackendRejectedCount = $rows[$c].BackendRejectedCount + 1
        if ($r.Why) { [void]$rows[$c].Reasons.Add([string]$r.Why) }
    }

    $result = @()
    foreach ($key in $rows.Keys) {
        $row = $rows[$key]
        $total = $row.PreRefusedCount + $row.BackendRejectedCount
        $result += [pscustomobject]@{
            Control              = $row.Control
            TotalCount           = $total
            PreRefusedCount      = $row.PreRefusedCount
            BackendRejectedCount = $row.BackendRejectedCount
            Reasons              = @($row.Reasons | Select-Object -Unique)
        }
    }
    return ,@($result | Sort-Object -Property @{ Expression = 'TotalCount'; Descending = $true }, @{ Expression = 'Control'; Descending = $false })
}

# --- 4. Product-sentence counter ------------------------------------------------------------------

# BackendSummary: Get-BackendSummary's own return object (backend.ps1) -- used for
# .AttributionNoteHits/.AttributionEventTypeHits/.AttributionCaveat/.Available, never re-derived here.
# ScreenTextHistory: a FLAT array of every screenText line seen across every turn of the run (the
# caller flattens TurnRecords[].ScreenText -- see agent-playtest.ps1's own wiring).
#
# U2 (eyes-learn-labels wave): "Fired" (ProductSentenceFired, the field metrics.json reports) used to
# be the SCREEN check alone -- found live as the exact defect this unit closes: 33 of 34 campaign
# runs read True purely from a regex hit on RIVAL DIALOGUE ("signed...") while the backend note-scan
# was 0-hits in every single one of those runs. A screen-text regex is a best-effort guess at UI copy
# (see its own caveat below); a real backend hit is the signal that is actually about the SIM having
# recorded an attribution event. ProductSentenceFired is now gated on a backend hit alone; a screen-
# only hit with the backend silent or unavailable is reported through Verdict as WEAK, never folded
# into a bare True.
#
# 2026-08-11 (backend-log-sees-the-spine): a "backend hit" is now EITHER of two independent signals,
# not just the note-scan -- AttributionEventTypeHits (a tick row whose eventTypes field names
# AttributionBeatEvent by type, PlaytestLog.Tick's own field, EXACT rather than a text guess) counts
# just as much as AttributionNoteHits (the pre-existing free-text scan). Before this, the sweep could
# only ever see the note-scan's best-effort proxy; it can now also see the sim's own event record.
function Get-ProductSentenceReport {
    param(
        $BackendSummary,
        [array]$ScreenTextHistory
    )

    $screenHits = New-Object System.Collections.ArrayList
    foreach ($line in $ScreenTextHistory) {
        if ($line -and ([string]$line -match $script:ProductSentenceKeywordPattern)) { [void]$screenHits.Add([string]$line) }
    }
    $screenHits = @($screenHits | Select-Object -Unique)
    $screenFired = ($screenHits.Count -gt 0)

    $attributionBeatNamed = $false
    $attributionNoteHits = @()
    $attributionEventTypeHits = @()
    $backendAvailable = $false
    $attributionCaveat = 'no backend log was available for this run -- whether an attribution beat ' +
        'fired at all is UNKNOWN, not "no" (see backend.ps1''s own Message for why the log is absent).'
    if ($BackendSummary -and $BackendSummary.Available) {
        $backendAvailable = $true
        # "| Where-Object { $null -ne $_ }" before the @() wrap, not after -- @($null) is a ONE-element
        # array holding a null (PowerShell's array subexpression operator wraps a scalar, and $null is
        # a scalar), so a BackendSummary that never set this property (an older/hand-built caller, not
        # today's Get-BackendSummary, which always sets both) would silently read as "1 hit" instead of
        # zero. Piping through Where-Object first empties a null pipeline input to genuinely nothing --
        # same idiom playtest-sweep.ps1 already uses for exactly this trap.
        $attributionNoteHits = @($BackendSummary.AttributionNoteHits | Where-Object { $null -ne $_ })
        $attributionEventTypeHits = @($BackendSummary.AttributionEventTypeHits | Where-Object { $null -ne $_ })
        $attributionBeatNamed = ($attributionNoteHits.Count -gt 0) -or ($attributionEventTypeHits.Count -gt 0)
        $attributionCaveat = $BackendSummary.AttributionCaveat
    }

    # This screenText scan is a SEPARATE best-effort text match from the backend note scan above (two
    # different logs, two different vocabularies) -- its own caveat is carried regardless of hit count,
    # per the brief: zero hits here means "the log cannot tell you", never "nothing named the player's
    # work," since a real attribution line could easily use wording this pattern does not catch.
    $screenTextCaveat = 'best-effort scan of every turn''s on-screen text for attribution-shaped ' +
        'language (the same keyword family as the backend note scan above: attribution/signed/' +
        'memorial/heirloom/gossip/legend/makersmark) -- not a parser of the game''s actual UI layout, ' +
        'and not proof the game never showed one just because this pattern found nothing. Treat zero ' +
        'hits as "the log cannot tell you", not "nothing named the player''s work." A screen hit ALONE ' +
        '(no matching backend note) is a WEAK signal, not proof -- rival dialogue and other UI copy can ' +
        'share this pattern''s keyword family without a real attribution event ever having fired.'

    # The one boolean metrics.json actually reports: True ONLY on a real backend hit (a note-scan hit
    # OR an eventTypes hit -- either one is the sim's own log corroborating it, not a screen guess). A
    # screen-only hit is real information (PlayerScreenShowedIt still reports it) but is not, by
    # itself, proof the product sentence fired -- that is exactly the false-positive this unit closes.
    $fired = $attributionBeatNamed

    $verdict = 'NOT SEEN'
    if ($attributionBeatNamed) {
        $verdict = 'CONFIRMED'
    } elseif ($screenFired -and $backendAvailable) {
        $verdict = 'WEAK (screen text only, backend silent)'
    } elseif ($screenFired) {
        $verdict = 'WEAK (screen text only, backend unavailable)'
    }

    return [pscustomobject]@{
        ProductSentenceFired     = $fired
        PlayerScreenShowedIt     = $screenFired
        ScreenTextHits           = $screenHits
        ScreenTextCaveat         = $screenTextCaveat
        AttributionBeatNamed     = $attributionBeatNamed
        AttributionNoteHits      = $attributionNoteHits
        AttributionEventTypeHits = $attributionEventTypeHits
        AttributionCaveat        = $attributionCaveat
        Verdict                  = $verdict
    }
}

# --- Combined summary + markdown -------------------------------------------------------------------

# One call assembles all four instruments from what agent-playtest.ps1 already has in hand after a
# run: TurnRecords (built during the loop), PreRefusals (built during the loop's attempts sub-loop),
# and $backendSummary (already computed by backend.ps1 before this runs). Always returns the same
# shape; every sub-report already degrades gracefully on empty/missing input (see each function's own
# doc), so this never needs a null-guard of its own.
function Get-MetricsSummary {
    param(
        [array]$TurnRecords,
        [array]$PreRefusals,
        $BackendSummary
    )

    $backendActionRows = @()
    $backendRejections = @()
    if ($BackendSummary -and $BackendSummary.Available) {
        $backendActionRows = @($BackendSummary.ActionRows)
        $backendRejections = @($BackendSummary.Rejections)
    }
    $screenTextHistory = @($TurnRecords | ForEach-Object { $_.ScreenText })

    return [pscustomobject]@{
        PerDayEntropy          = Get-PerDayActionEntropy -TurnRecords $TurnRecords -BackendActionRows $backendActionRows
        LegalVsChosenByPhase   = Get-LegalVsChosenByPhase -TurnRecords $TurnRecords
        RefusalFrustrationMap  = Get-RefusalFrustrationMap -PreRefusals $PreRefusals -BackendRejections $backendRejections
        ProductSentence        = Get-ProductSentenceReport -BackendSummary $BackendSummary -ScreenTextHistory $screenTextHistory
    }
}

# The "## Mechanical fun metrics" section for findings.md -- placed BELOW the Backend record and
# ABOVE the model's prose (the brief's own ordering: recorded facts first, then measured facts, then
# the model's account last). Pure text assembly, same shape as backend.ps1's Format-BackendMarkdown.
function Format-MetricsMarkdown {
    param([Parameter(Mandatory)]$Metrics)

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('## Mechanical fun metrics')
    [void]$lines.Add('')
    [void]$lines.Add('Measured, not narrated -- no model was asked any of this. See tools/agent-playtest/metrics.ps1.')
    [void]$lines.Add('')

    [void]$lines.Add('### Product-sentence counter')
    [void]$lines.Add('')
    $ps = $Metrics.ProductSentence
    [void]$lines.Add('- VERDICT: ' + $ps.Verdict + ' (metrics.json ProductSentenceFired=' + $ps.ProductSentenceFired + ' -- True requires a real backend hit (note scan or eventTypes); a screen-only hit reports WEAK, never a bare True)')
    [void]$lines.Add('- attribution beat named in the backend log: ' + $ps.AttributionBeatNamed + ' (' + @($ps.AttributionNoteHits).Count + ' note hit(s), ' + @($ps.AttributionEventTypeHits).Count + ' eventTypes hit(s) -- the latter is EXACT, PlaytestLog.Tick naming AttributionBeatEvent by type, not a text guess)')
    [void]$lines.Add('- the PLAYER''S SCREEN ever showed one: ' + $ps.PlayerScreenShowedIt + ' (' + @($ps.ScreenTextHits).Count + ' screenText hit(s))')
    [void]$lines.Add('CAVEAT (backend note scan): ' + $ps.AttributionCaveat)
    [void]$lines.Add('CAVEAT (screenText scan): ' + $ps.ScreenTextCaveat)
    if (@($ps.ScreenTextHits).Count -gt 0) {
        [void]$lines.Add('Screen hits:')
        foreach ($h in $ps.ScreenTextHits) { [void]$lines.Add('- ' + $h) }
    }
    [void]$lines.Add('')

    [void]$lines.Add('### Per-day action entropy (the day-11 instrument)')
    [void]$lines.Add('')
    if (@($Metrics.PerDayEntropy).Count -eq 0) {
        [void]$lines.Add('(no day-tagged actions recorded -- zero turns, or no day field on any of them)')
    } else {
        [void]$lines.Add('| Day | Total actions | Distinct types | Entropy (bits) |')
        [void]$lines.Add('|---|---|---|---|')
        foreach ($d in $Metrics.PerDayEntropy) {
            [void]$lines.Add('| ' + $d.Day + ' | ' + $d.TotalActions + ' | ' + $d.DistinctActionTypes + ' | ' + $d.EntropyBits + ' |')
        }
    }
    [void]$lines.Add('')

    [void]$lines.Add('### LEGAL-vs-CHOSEN ratio per phase')
    [void]$lines.Add('')
    if (@($Metrics.LegalVsChosenByPhase).Count -eq 0) {
        [void]$lines.Add('(no phase data recorded)')
    } else {
        [void]$lines.Add('| Phase | Legal controls | Chosen controls | Ratio |')
        [void]$lines.Add('|---|---|---|---|')
        foreach ($p in $Metrics.LegalVsChosenByPhase) {
            [void]$lines.Add('| ' + $p.Phase + ' | ' + $p.LegalCount + ' | ' + $p.ChosenCount + ' | ' + $p.RatioPct + '% |')
        }
    }
    [void]$lines.Add('')

    [void]$lines.Add('### Refusals-by-control frustration map')
    [void]$lines.Add('')
    if (@($Metrics.RefusalFrustrationMap).Count -eq 0) {
        [void]$lines.Add('(none -- zero pre-refused attempts and zero backend rejections)')
    } else {
        [void]$lines.Add('| Control | Total | Pre-refused (driver) | Rejected (kernel) |')
        [void]$lines.Add('|---|---|---|---|')
        foreach ($f in $Metrics.RefusalFrustrationMap) {
            [void]$lines.Add('| ' + $f.Control + ' | ' + $f.TotalCount + ' | ' + $f.PreRefusedCount + ' | ' + $f.BackendRejectedCount + ' |')
        }
    }

    return ($lines -join [Environment]::NewLine)
}

# --- Fallback close-control detection (U2, eyes-learn-labels wave) --------------------------------

# When the driver's own per-turn attempts loop exhausts itself with no legal command, the OLD
# fallback was unconditional "advance" -- even when an OVERLAY owns the screen (a modal/panel with a
# close control among this turn's enabled ones), where advancing the DAY does not get the player
# unstuck at all; the very next turn starts from the same stuck overlay having burned a day for
# nothing. Derived MECHANICALLY, never a hardcoded control list: any enabled control whose NAME
# starts with "Close" (this codebase's own naming convention -- CloseLedger, CloseShop, ... -- see
# LedgerModal.cs/ShopPanel.cs's own AddButton calls) is treated as the overlay's own close verb.
# Returns $null when no such control is enabled (the ordinary case -- nothing is holding the screen).
function Get-FallbackCloseControl {
    param([array]$EnabledControls)

    return (@($EnabledControls) | Where-Object { $_ -and ([string]$_).StartsWith('Close') } | Select-Object -First 1)
}

# --- Per-day judge digest --------------------------------------------------------------------------

# U2 (eyes-learn-labels wave): metrics.ps1's own Format-DigestTurnLine used to slice the first TWO
# raw ScreenText entries and join them with '; ' -- found live to be a phantom: the real HUD's Day
# chip renders as two adjacent Label nodes (label "Day", value "2"), so ScreenObservation.VisibleText
# (which walks each Label separately) ALWAYS puts them first, and a judge reading "Day; 2" quoted
# text no player ever saw (a player sees "Day 2", rendered with no visible separator at all). Pair
# them with a single space instead; anything beyond those first two entries is a genuinely separate
# screen item and keeps the '; ' join.
function Format-DigestTurnLine {
    param($Turn)

    $screenBit = ''
    $screenTextArr = @($Turn.ScreenText)
    if ($screenTextArr.Count -gt 0) {
        $previewParts = New-Object System.Collections.ArrayList
        if ($screenTextArr.Count -ge 2) {
            [void]$previewParts.Add(([string]$screenTextArr[0] + ' ' + [string]$screenTextArr[1]))
            foreach ($rest in @($screenTextArr | Select-Object -Skip 2 -First 2)) { [void]$previewParts.Add($rest) }
        } else {
            [void]$previewParts.Add([string]$screenTextArr[0])
        }
        $screenBit = ' | screen: ' + ($previewParts -join '; ')
    }
    $refusedBit = ''
    if ($Turn.Refused) { $refusedBit = ' | REFUSED: ' + $Turn.RefusalReason }
    return ('turn ' + $Turn.Turn + ' [' + $Turn.Phase + ']: ' + $Turn.Action + ' ' + $Turn.Target +
        ' (' + $Turn.Why + ') -> ' + $Turn.Outcome + $refusedBit + $screenBit)
}

# One day's block: a header naming the day and its phase sequence, then one line per turn -- or, once
# thinning is active ($MaxTurnsPerDay -gt 0 and this day has more turns than that), the first half and
# last half of the day's turns with an explicit "N turn(s) omitted" marker between them. $MaxTurnsPerDay
# of 0 means unlimited (no thinning at all).
function Format-DigestDayBlock {
    param(
        [Parameter(Mandatory)][string]$DayKey,
        [Parameter(Mandatory)][array]$TurnsThisDay,
        [Parameter(Mandatory)][int]$MaxTurnsPerDay
    )

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('=== Day ' + $DayKey + ' (' + $TurnsThisDay.Count + ' turn(s) recorded) ===')
    $phaseSeq = @($TurnsThisDay | ForEach-Object { $_.Phase } | Select-Object -Unique)
    [void]$lines.Add('Phases: ' + ($phaseSeq -join ' -> '))

    if ($MaxTurnsPerDay -le 0 -or $TurnsThisDay.Count -le $MaxTurnsPerDay) {
        foreach ($t in $TurnsThisDay) { [void]$lines.Add((Format-DigestTurnLine $t)) }
        return ($lines -join [Environment]::NewLine)
    }

    $half = [math]::Max(1, [math]::Floor($MaxTurnsPerDay / 2.0))
    $tailCount = [math]::Max(1, $MaxTurnsPerDay - $half)
    $head = @($TurnsThisDay | Select-Object -First $half)
    $tail = @($TurnsThisDay | Select-Object -Last $tailCount)
    $omitted = $TurnsThisDay.Count - ($head.Count + $tail.Count)

    foreach ($t in $head) { [void]$lines.Add((Format-DigestTurnLine $t)) }
    if ($omitted -gt 0) { [void]$lines.Add('  ... ' + $omitted + ' turn(s) omitted for length ...') }
    foreach ($t in $tail) { [void]$lines.Add((Format-DigestTurnLine $t)) }
    return ($lines -join [Environment]::NewLine)
}

# Replaces agent-playtest.ps1's $judgeCap tail-trim (W1's interim 24000-char raise -- see that file's
# own comment on the defect this still left: a FRONT trim on a long run means the judge never sees
# early days at all). TurnRecords must already be in chronological turn order (agent-playtest.ps1
# appends one per turn as the loop runs, so this holds by construction); day grouping preserves
# FIRST-SEEN order, which is day order as long as that holds.
#
# Bounding strategy: try unthinned first: if the whole thing already fits under $MaxChars, done. If
# not, thin every day down to a shrinking per-day turn cap (20, then halved each further pass down to
# a floor of 4 -- 2 head + 2 tail) until it fits or the floor is reached. The floor is a DELIBERATE
# stopping point, not a bug: a day is never dropped outright, even if the final result still runs a
# little over $MaxChars -- see this file's own header for why that is the whole point of this
# function existing (the regression it exists to pin: docs/plans/2026-08-10-002's Verification
# Contract, "per-day digest of a 3-day fixture contains all three days").
function Build-PerDayJudgeDigest {
    param(
        # AllowEmptyCollection: same crash backend.ps1's own Get-BackendRejections/
        # Get-BackendRejectionCountsByReason already document and fix -- a Mandatory [array] parameter
        # throws "Cannot bind argument ... because it is an empty collection" the moment a caller
        # passes a real, legal @() (measured live here while writing this test: a zero-turn run's own
        # $turnRecords is exactly that). Found before it ever reached a real run, but the same shape.
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$TurnRecords,
        [int]$MaxChars = 24000
    )

    if (@($TurnRecords).Count -eq 0) {
        return [pscustomobject]@{ Text = '(no turns recorded)'; DayCount = 0; Thinned = $false; Length = 15 }
    }

    $dayKeys = New-Object System.Collections.ArrayList
    $byDay = @{}
    foreach ($t in $TurnRecords) {
        $d = $t.Day
        if ($null -eq $d) { $d = '(unknown)' }
        $key = [string]$d
        if (-not $byDay.ContainsKey($key)) {
            $byDay[$key] = New-Object System.Collections.ArrayList
            [void]$dayKeys.Add($key)
        }
        [void]$byDay[$key].Add($t)
    }

    $maxTurnsPerDay = 0
    $joined = ''
    $guard = 0
    while ($true) {
        $blocks = @()
        foreach ($key in $dayKeys) { $blocks += (Format-DigestDayBlock -DayKey $key -TurnsThisDay @($byDay[$key]) -MaxTurnsPerDay $maxTurnsPerDay) }
        $joined = ($blocks -join ([Environment]::NewLine + [Environment]::NewLine))
        $guard++
        if ($joined.Length -le $MaxChars) { break }
        if ($maxTurnsPerDay -eq 0) {
            $maxTurnsPerDay = 20
        } elseif ($maxTurnsPerDay -gt 4) {
            $maxTurnsPerDay = [math]::Max(4, [int][math]::Floor($maxTurnsPerDay / 2.0))
        } else {
            break
        }
        if ($guard -gt 20) { break }
    }

    return [pscustomobject]@{
        Text     = $joined
        DayCount = $dayKeys.Count
        Thinned  = ($maxTurnsPerDay -gt 0)
        Length   = $joined.Length
    }
}
