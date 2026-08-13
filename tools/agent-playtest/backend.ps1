<#
.SYNOPSIS
    Pure logic for U2 (playtest-harness wave): make playtest-log.jsonl -- written every run, read by
    nothing -- into evidence the findings actually cite.

.DESCRIPTION
    PlaytestLog.cs (godot/scripts/PlaytestLog.cs) writes one JSONL row per phase tick plus free-text
    "note"/"action" rows, gated on MM_PLAYTEST_LOG. agent-playtest.ps1 has set that env var and
    collected the file since #425, and until this unit nothing downstream ever opened it -- the
    per-turn turnlog.md and the model's own judge pass were the only record a reader ever saw, and
    neither one has any idea what the KERNEL actually did (a real cause tag, a raw rejection reason,
    a counterfactual attribution beat). This file reads that log and turns it into the specific
    claims the harness's own standing findings need evidence for.

    The row shapes below are DERIVED from PlaytestLog.cs as written, not invented -- see that file's
    Tick/Note/Action methods for the authoritative field list. Where the log genuinely cannot answer
    a question (see Get-BackendSummary's own note on event TYPES), this says so instead of guessing:

    "tick" -- {kind, t, day, phase, beat, cause, fromDay, fromPhase, gold, mats, shelf, items,
               heroesAlive, heroes, inFlight, bounties, act, slots, events, rejects:[{action,why}],
               eventTypes:[typeName,...]}   (eventTypes added 2026-08-11 -- absent on a row from an
               OLDER build, always present (possibly empty) on a current one; see the caveat below)
    "note" -- {kind, t, what}
    "action" -- {kind, t, day, phase, beat, action, immediate}
    "session" -- {kind, startedAt, provenance}   (header row, written once by PlaytestLog.Begin)

    A GAP THAT WAS GENUINE, found while building this -- CLOSED 2026-08-11 for one specific event:
    "events" on a tick row is still Adapter.LastEvents.Count, an INTEGER kept for existing readers,
    but PlaytestLog.Tick now ALSO writes "eventTypes", the DISTINCT event type names that fired that
    tick (e.g. ["ItemSold","AttributionBeatEvent"]). That means a log written by a build carrying the
    field CAN directly prove an AttributionBeatEvent fired (AttributionEventTypeHits below) -- it
    still cannot break out GossipEmitted/ItemSigned/MemorialHonored specifically unless eventTypes on
    that row happens to name one of THOSE too, and a log from BEFORE this field existed still only
    has the bare count. See AttributionCaveat below, which reports whichever of those is actually
    true for the run in hand rather than a heuristic that would read as more certain than it is.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# Free-text "note" rows whose "what" text is worth surfacing on its own -- narrator lines
# (AudioDirector.SpeakNarratorLine writes "VOICE: spoke <id>" / "VOICE: text-only (no audio) <id>")
# and anything that LOOKS like it is naming the player's work, checked against the actual sim event
# vocabulary (sim/GameSim/Contracts/Events.cs) rather than invented keywords: AttributionBeatEvent,
# GossipEmitted, ItemSigned, MemorialHonored, HeirloomReforged, CommissionFulfilled are the events
# that carry a MakersMark forward into something a hero or the town can react to.
$script:NarratorNotePrefix = 'VOICE:'
$script:AttributionKeywordPattern = '(?i)(attribution|signed|memorial|heirloom|gossip|legend|makersmark|maker''?s mark)'

# The sim's own type name for the one event link 4/5's sweep actually needs (sim/GameSim/Contracts/
# Events.cs: "AttributionBeatEvent", the exact string PlaytestLog.Tick's eventTypes field writes via
# GetType().Name -- the same vocabulary rejects[].action already uses for RejectedAction.Action). A
# named constant rather than inlining the literal below, for the same reason
# $script:AttributionKeywordPattern is: one place to change if the sim ever renames it.
$script:AttributionEventTypeName = 'AttributionBeatEvent'

# Reads and parses playtest-log.jsonl, returning one row per line as a PSCustomObject (ConvertFrom-Json
# output), tagging lines that fail to parse rather than dropping them silently. Never throws on a
# missing file -- the caller (Get-BackendSummary) is the one place that has to decide what "no log"
# means, same shape as scope-map.ps1's Get-ChangedFilesAgainstMain returning an empty array on error.
function Read-BackendLogRows {
    param([Parameter(Mandatory)][string]$LogPath)

    $rows = New-Object System.Collections.ArrayList
    $malformed = 0

    if (-not (Test-Path $LogPath)) {
        return [pscustomobject]@{ Rows = @(); MalformedCount = 0; Exists = $false; Empty = $true }
    }

    $raw = Get-Content $LogPath -Raw -ErrorAction SilentlyContinue
    if (-not $raw -or $raw.Trim().Length -eq 0) {
        return [pscustomobject]@{ Rows = @(); MalformedCount = 0; Exists = $true; Empty = $true }
    }

    foreach ($line in ($raw -split "`r?`n")) {
        if (-not $line -or $line.Trim().Length -eq 0) { continue }
        $parsed = $null
        try { $parsed = $line | ConvertFrom-Json -ErrorAction Stop } catch { $parsed = $null }
        if ($null -eq $parsed) {
            $malformed++
            continue
        }
        [void]$rows.Add($parsed)
    }

    return [pscustomobject]@{ Rows = @($rows); MalformedCount = $malformed; Exists = $true; Empty = ($rows.Count -eq 0) }
}

# Every REJECTED action across the whole log, deduplicated the same way MainUi.cs's own toast logic
# has to be (see MainUi's _rejectionsWarned / SimAdapter.LastRejections doc): a tick row's "rejects"
# array ACCUMULATES for the whole phase (OnPhaseCompleted re-reports the WHOLE list on every
# immediately-resolved action, not just the newest one), and resets only when a real AdvancePhase
# clears it. Counting every row's full array at face value would count the SAME rejection 2, 3, N
# times -- once per tick row emitted before the phase actually turns over. This walks the accumulator
# the same way the client's own code does: a shrink means a reset, only the NEW tail beyond the
# previous length is a genuinely new rejection.
function Get-BackendRejections {
    # AllowEmptyCollection: a clean run (zero backend-level rejections across the whole log, the
    # NORMAL case for a short scripted/veteran run that never touched an action the kernel would
    # refuse) passes an empty array here. Without this attribute, PowerShell's own parameter binder
    # treats a Mandatory array parameter receiving @() as an error and throws "Cannot bind argument
    # ... because it is an empty collection" -- found live (W1, docs/plans/2026-08-10-002) verifying
    # the honesty footer: a -Scripted run's real playtest-log.jsonl had a tick row but zero rejects,
    # and Get-BackendSummary crashed before findings.md could be written at all. The exact silent-zero
    # shape this file's own header note says it exists to avoid, just one level up (a CRASH instead of
    # a reported zero, which is worse: no findings.md, not even a wrong one).
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$TickRows)

    $events = New-Object System.Collections.ArrayList
    $prevCount = 0
    foreach ($row in $TickRows) {
        $rejects = @($row.rejects)
        $n = $rejects.Count
        if ($n -lt $prevCount) { $prevCount = 0 }
        if ($n -gt $prevCount) {
            for ($i = $prevCount; $i -lt $n; $i++) {
                $r = $rejects[$i]
                [void]$events.Add([pscustomobject]@{
                    Day    = $row.day
                    Phase  = $row.phase
                    Action = $r.action
                    Why    = $r.why
                })
            }
        }
        $prevCount = $n
    }
    # Leading comma: PowerShell UNWRAPS a single-element array back to a bare scalar when a function
    # returns it across the pipeline (`return @($x)` with one item behaves like `return $x[0]` to the
    # caller) -- measured directly here: a fixture with exactly one rejection came back with
    # $result.Rejections.Count silently blank instead of 1. `,@(...)` wraps the array ONE level
    # deeper so the pipeline's unwrap peels off the wrapper, not the array itself. Same precedent as
    # scope-map.ps1's own Get-ChangedFilesAgainstMain (its empty-array early returns use the same
    # comma; this file needed it for the one-element case that function's own tests never hit).
    return ,@($events)
}

# {Reason;Count} sorted by count descending -- "plus counts grouped by reason" from the brief,
# grouped on the WHY text (the RAW kernel reason string, e.g. "insufficient gold"), not the action
# type, since two different actions can fail for the same underlying reason and that is the
# interesting rollup.
function Get-BackendRejectionCountsByReason {
    # AllowEmptyCollection -- see Get-BackendRejections' own note just above; this is the second of
    # the two call sites that crashed a zero-rejection run before this file could ever explain WHY
    # it was zero.
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Rejections)

    $counts = @{}
    foreach ($r in $Rejections) {
        $key = $r.Why
        if (-not $key) { $key = '(no reason given)' }
        if ($counts.ContainsKey($key)) { $counts[$key] = $counts[$key] + 1 } else { $counts[$key] = 1 }
    }
    $result = @()
    foreach ($key in $counts.Keys) {
        $result += [pscustomobject]@{ Reason = $key; Count = $counts[$key] }
    }
    # Leading comma -- see Get-BackendRejections' own note on why a single-reason run needs it.
    return ,@($result | Sort-Object -Property Count -Descending)
}

# The full backend picture for one run's playtest-log.jsonl. Always returns the SAME shape whether
# or not a log was found -- Available/Message tell the caller which case it is, and every array/count
# field is populated (never $null) either way, so a renderer never needs a null-check per field.
function Get-BackendSummary {
    param([Parameter(Mandatory)][string]$LogPath)

    $read = Read-BackendLogRows -LogPath $LogPath

    $empty = [pscustomobject]@{
        Available                = $false
        Message                  = ''
        LogPath                  = $LogPath
        RowCount                 = 0
        MalformedLineCount       = 0
        Session                  = $null
        Timeline                 = @()
        Advances                 = @()
        AdvanceCauseCounts       = @()
        AutoAdvanceCount         = 0
        PressAdvanceCount        = 0
        UnattributedAdvanceCount = 0
        UnattributedAdvances     = @()
        Rejections               = @()
        RejectionCountsByReason  = @()
        EventsTotalAcrossTicks   = 0
        AttributionNoteHits      = @()
        AttributionEventTypeHits = @()
        AttributionCaveat        = ''
        NarratorLines            = @()
        NarratorVoicedCount      = 0
        NarratorTextOnlyCount    = 0
        AutosaveWriteCount       = 0
        AutosaveCaveat           = ''
    }

    # An absent or empty log is reported EXPLICITLY -- never a silent zero across every field, which
    # would read exactly like "a clean run: no rejections, no auto-advances, nothing to attribute."
    # That is the U2 brief's own standing requirement, and it is the same silent-success shape U1's
    # "no line about the frame" and A1's fallback-turn defect both already are in this same file tree.
    if (-not $read.Exists) {
        $empty.Message = 'no backend log: ' + $LogPath + ' does not exist. MM_PLAYTEST_LOG was ' +
            'probably unset for this run, or PlaytestLog.Begin never opened it (see its own fail-soft ' +
            'contract) -- everything below is UNKNOWN, not clean.'
        return $empty
    }
    if ($read.Empty) {
        $empty.Available = $true
        $empty.Message = 'backend log exists but has zero rows: ' + $LogPath + ' -- the client opened ' +
            'it but the run ended before a single tick completed (or every line failed to parse).'
        $empty.MalformedLineCount = $read.MalformedCount
        return $empty
    }

    $rows = $read.Rows
    $sessionRow = $rows | Where-Object { $_.kind -eq 'session' } | Select-Object -First 1
    $tickRows = @($rows | Where-Object { $_.kind -eq 'tick' })
    $noteRows = @($rows | Where-Object { $_.kind -eq 'note' })
    $actionRows = @($rows | Where-Object { $_.kind -eq 'action' })

    $session = $null
    if ($sessionRow) {
        $session = [pscustomobject]@{ Provenance = $sessionRow.provenance; StartedAt = $sessionRow.startedAt }
    }

    # Timeline: every tick, with the CAUSE tag PlaytestLog.Tick carries (press:*/auto:*/empty) and
    # whether it was a REAL transition (day or phase actually changed) versus an immediate in-phase
    # action re-reporting the same day/phase (SimAdapter.Queue's own doc: OnPhaseCompleted fires on
    # every immediately-resolved action, not only on a true AdvancePhase).
    $timeline = @()
    foreach ($row in $tickRows) {
        $isAdvance = ($row.day -ne $row.fromDay) -or ($row.phase -ne $row.fromPhase)
        $timeline += [pscustomobject]@{
            T          = $row.t
            Day        = $row.day
            Phase      = $row.phase
            FromDay    = $row.fromDay
            FromPhase  = $row.fromPhase
            Beat       = $row.beat
            Cause      = $row.cause
            IsAdvance  = $isAdvance
            Events     = $row.events
            # Where-Object null-filter BEFORE @() wraps it, not after -- @($null) is a ONE-element
            # array holding a null (the array subexpression operator wraps a bare scalar, and $null
            # counts), so an old-format row with no "eventTypes" key at all would otherwise read as
            # "1 (unknown) type" instead of the honest zero/absent this field means for that row.
            EventTypes = @($row.eventTypes | Where-Object { $null -ne $_ })
        }
    }
    $advances = @($timeline | Where-Object { $_.IsAdvance })

    $causeCounts = @{}
    $autoCount = 0
    $pressCount = 0
    $unattributed = @()
    foreach ($a in $advances) {
        $cause = $a.Cause
        if (-not $cause) { $cause = '(empty)' }
        if ($causeCounts.ContainsKey($cause)) { $causeCounts[$cause] = $causeCounts[$cause] + 1 } else { $causeCounts[$cause] = 1 }
        if ($a.Cause -like 'auto:*') { $autoCount++ }
        elseif ($a.Cause -like 'press:*') { $pressCount++ }
        elseif (-not $a.Cause) {
            # A REAL transition with an EMPTY cause is the bug's exact signature PlaytestLog.Tick's
            # own doc names: a day advancing with nothing on record that asked for it.
            $unattributed += $a
        }
    }
    $causeCountsArray = @()
    foreach ($k in $causeCounts.Keys) { $causeCountsArray += [pscustomobject]@{ Cause = $k; Count = $causeCounts[$k] } }
    $causeCountsArray = @($causeCountsArray | Sort-Object -Property Count -Descending)

    $rejections = Get-BackendRejections -TickRows $tickRows
    $rejectionCounts = Get-BackendRejectionCountsByReason -Rejections $rejections

    $eventsTotal = 0
    foreach ($row in $tickRows) { $eventsTotal += [int]$row.events }

    $attributionHits = @($noteRows | Where-Object { $_.what -match $script:AttributionKeywordPattern } |
        ForEach-Object { [pscustomobject]@{ T = $_.t; What = $_.what } })

    # eventTypes corroboration (2026-08-11): a tick row from a CURRENT build always carries the
    # "eventTypes" key, even when its value is an empty array -- that is what lets this tell "zero
    # events fired" apart from "this log predates the field" by key PRESENCE, not by count. Detected
    # across every tick row (not just the first) since a batch/telemetry run could in principle mix a
    # rebuilt client mid-session; one row carrying the key is enough to prove the build could report it.
    $tickRowsCarryEventTypes = $false
    foreach ($row in $tickRows) {
        if (@($row.PSObject.Properties.Name) -contains 'eventTypes') { $tickRowsCarryEventTypes = $true; break }
    }
    $attributionEventTypeHits = @($tickRows | Where-Object { @($_.eventTypes) -contains $script:AttributionEventTypeName } |
        ForEach-Object { [pscustomobject]@{ T = $_.t; Day = $_.day; Phase = $_.phase } })

    if ($tickRowsCarryEventTypes) {
        $attributionCaveatText = 'a tick row now carries both a COUNT of events (' + $eventsTotal +
            ' total across this run) and the DISTINCT event TYPE NAMES that fired that tick ' +
            '(PlaytestLog.Tick''s eventTypes field, added to close the exact gap this caveat used to ' +
            'describe) -- so THIS log can directly prove an AttributionBeatEvent fired: ' +
            (@($attributionEventTypeHits).Count) + ' tick row(s) named it. It still cannot prove ' +
            'GossipEmitted/ItemSigned/MemorialHonored/HeirloomReforged fired unless eventTypes on that ' +
            'row names one of THOSE specifically -- this only closes the gap for AttributionBeatEvent, ' +
            'the one link 4/5 sweep actually needs. The ' + (@($attributionHits).Count) + ' note-scan ' +
            'hit(s) below remain a separate, best-effort signal, not the proof.'
    } else {
        $attributionCaveatText = 'this run''s tick rows carry no "eventTypes" key at all -- a log ' +
            'written by a build before PlaytestLog.Tick gained that field. For THIS run, whether an ' +
            'AttributionBeatEvent fired is exactly as unprovable from event counts alone as it always ' +
            'was (only that SOME events fired that tick, never which). The ' +
            (@($attributionHits).Count) + ' hit(s) below are a best-effort scan of free-text "note" ' +
            'rows for attribution-shaped language, not a count of real attribution events -- treat ' +
            'zero hits as "the log cannot tell you", not "nothing named the player''s work."'
    }

    $narratorRows = @($noteRows | Where-Object { $_.what -like ($script:NarratorNotePrefix + '*') })
    $narratorLines = @($narratorRows | ForEach-Object {
        [pscustomobject]@{ T = $_.t; Text = $_.what; Voiced = ($_.what -notlike '*text-only*') }
    })
    $voicedCount = @($narratorLines | Where-Object { $_.Voiced }).Count
    $textOnlyCount = @($narratorLines | Where-Object { -not $_.Voiced }).Count

    # Autosave is CampaignSave.Save(state), called unconditionally from MainUi.cs the instant
    # completedPhase == DayPhase.Evening -- there is no separate PlaytestLog write for it, so this is
    # DERIVED from that same condition on the tick row already in hand, not a direct log entry. See
    # AutosaveCaveat.
    $autosaveCount = @($tickRows | Where-Object { $_.fromPhase -eq 'Evening' }).Count

    return [pscustomobject]@{
        Available                = $true
        Message                  = ''
        LogPath                  = $LogPath
        RowCount                 = $rows.Count
        MalformedLineCount       = $read.MalformedCount
        Session                  = $session
        Timeline                 = $timeline
        Advances                 = $advances
        AdvanceCauseCounts       = $causeCountsArray
        AutoAdvanceCount         = $autoCount
        PressAdvanceCount        = $pressCount
        UnattributedAdvanceCount = @($unattributed).Count
        UnattributedAdvances     = $unattributed
        Rejections               = $rejections
        RejectionCountsByReason  = $rejectionCounts
        EventsTotalAcrossTicks   = $eventsTotal
        AttributionNoteHits      = $attributionHits
        AttributionEventTypeHits = $attributionEventTypeHits
        AttributionCaveat        = $attributionCaveatText
        NarratorLines            = $narratorLines
        NarratorVoicedCount      = $voicedCount
        NarratorTextOnlyCount    = $textOnlyCount
        AutosaveWriteCount       = $autosaveCount
        AutosaveCaveat           = 'derived, not directly logged: every tick row whose fromPhase is ' +
            '"Evening" corresponds to one CampaignSave.Save(state) call (MainUi.cs OnPhaseCompleted, ' +
            'unconditional on that phase completing) -- there is no dedicated autosave log line.'
        ActionRows               = @($actionRows)
    }
}

# Auto-advances -- (b) from the brief: "any phase advance whose cause is auto: -- the game moving
# without the player." Self-contained from the summary alone.
function Get-AutoAdvanceContradictions {
    param([Parameter(Mandatory)]$Summary)

    # Leading comma on every array return in this function -- see Get-BackendRejections' own note.
    # An empty ",@()" also matters here (not just the 1-element case): a bare "return @()" with ZERO
    # objects emitted assigns $null to the caller's variable, not an empty array, which would make
    # every "@($x).Count -eq 0" check downstream true by luck rather than by an actual empty array.
    if (-not $Summary.Available) { return ,@() }

    $lines = @()
    foreach ($a in @($Summary.Advances | Where-Object { $_.Cause -like 'auto:*' })) {
        $lines += ('day ' + $a.FromDay + ' ' + $a.FromPhase + ' -> day ' + $a.Day + ' ' + $a.Phase +
            ' advanced by ' + $a.Cause + ', not a player press.')
    }
    if (@($Summary.UnattributedAdvances).Count -gt 0) {
        foreach ($a in $Summary.UnattributedAdvances) {
            $lines += ('day ' + $a.FromDay + ' ' + $a.FromPhase + ' -> day ' + $a.Day + ' ' + $a.Phase +
                ' advanced with NO cause recorded at all -- the exact unattributed-tick shape ' +
                'PlaytestLog.Tick''s own doc warns about.')
        }
    }
    return ,@($lines)
}

# (a) from the brief: "a turn the driver recorded as accepted while the backend logged a rejection."
# $DriverTurns is the driver's OWN per-turn record -- an array of {Day;Phase;Accepted}, one entry per
# turn, built from state.lastOutcome (Accepted = lastOutcome does NOT start with "refused:"). This
# is bucketed by (Day,Phase), not by single turn: the two logs have no shared per-action join key
# (the control name a player presses, e.g. "BuyMat_copper", is not the kernel action type name the
# backend log records, e.g. "MaterialPurchaseAction"), so a per-turn exact match cannot be made
# honestly. A (Day,Phase) bucket where the driver saw zero refusals but the backend logged 1+ NEW
# rejections is still real evidence -- the UI-visible outcome and the kernel's own record disagree
# for that window -- even though it cannot name which single turn caused it.
function Get-DriverBackendMismatches {
    param(
        [Parameter(Mandatory)]$Summary,
        [array]$DriverTurns
    )

    if (-not $Summary.Available) { return ,@() }
    if (-not $DriverTurns) { return ,@() }

    $driverRefusalBuckets = @{}
    foreach ($t in $DriverTurns) {
        $key = $t.Day.ToString() + '|' + $t.Phase
        if (-not $driverRefusalBuckets.ContainsKey($key)) { $driverRefusalBuckets[$key] = 0 }
        if (-not $t.Accepted) { $driverRefusalBuckets[$key] = $driverRefusalBuckets[$key] + 1 }
    }

    $backendRejectBuckets = @{}
    foreach ($r in $Summary.Rejections) {
        $key = $r.Day.ToString() + '|' + $r.Phase
        if ($backendRejectBuckets.ContainsKey($key)) { $backendRejectBuckets[$key] = $backendRejectBuckets[$key] + 1 } else { $backendRejectBuckets[$key] = 1 }
    }

    $lines = @()
    foreach ($key in $backendRejectBuckets.Keys) {
        $driverRefusals = 0
        if ($driverRefusalBuckets.ContainsKey($key)) { $driverRefusals = $driverRefusalBuckets[$key] }
        if ($driverRefusals -eq 0) {
            $parts = $key -split '\|', 2
            $lines += ('day ' + $parts[0] + ' ' + $parts[1] + ': backend logged ' +
                $backendRejectBuckets[$key] + ' new rejection(s), but the driver''s own turn log shows ' +
                'no refused outcome in that day/phase -- bucketed by day+phase, not a single-turn ' +
                'match (see this function''s own doc for why).')
        }
    }
    return ,@($lines | Sort-Object)
}

# --- Backend log liveness (2026-08-12, "the evidence channel says when it dies" -- finding A) -----
#
# PlaytestLog.Append (godot/scripts/PlaytestLog.cs) is fail-soft BY DESIGN: the first write that
# throws (a documented Windows IOException against a file this client writes) sets the class's static
# _path to null PERMANENTLY, and the only warning is GD.PrintErr/EngineDistress.Warn -- neither of
# which agent-playtest.ps1 ever sees, since it launches the client with no stdout/stderr redirection
# (Start-Process, no -RedirectStandardOutput/-RedirectStandardError). Left alone, EVERY dead-verb
# check for the rest of that run then reads "no sim event fired in the backend slice" -- the exact
# shape a genuinely dead button produces, with nothing in Get-DeadVerbVerdict's own inputs able to
# tell the two apart (an 8-lens adversarial audit built both slices by hand and got identical
# IsCandidate=True verdicts).
#
# The fix does not touch PlaytestLog's fail-soft contract -- a logging failure must never cost a real
# playtest session, and that design is correct. It gives the digest itself an honest voice instead:
# AgentPlaytest.cs's StateDigest now carries "backendLogActive" (PlaytestLog.Active, unchanged) every
# turn -- a DIRECT fact from the client's own bookkeeping, never an inference from row counts or
# timing. This tracker watches that field turn over turn and LATCHES the instant it reads false --
# once tripped it never un-latches for the rest of this run's report, even if a later scene reload
# somehow re-armed PlaytestLog (Begin's own re-entry guard only blocks a second call while _path is
# still set): a run that had a hole in its own evidence should say so for good, "say so, loudly"
# outliving a later recovery, never a quiet return to normal.
function New-BackendLogLivenessTracker {
    return [pscustomobject]@{
        Stalled             = $false
        StallDetectedAtTurn = $null
    }
}

# Call once per turn, in turn order, with THAT turn's own state.json "backendLogActive" value.
# $BackendLogActive is $null when reading an OLDER state.json shape that predates the field entirely
# (never guess in either direction -- the same "presence, not just value" caution backend.ps1 already
# takes for eventTypes) -- such a turn is skipped, neither proving nor disproving liveness. Mutates
# $Tracker in place and returns nothing, matching temperament.ps1's Add-TemperamentDrain/
# Reset-TemperamentMeter convention for a per-run tracker object.
function Update-BackendLogLivenessTracker {
    param(
        [Parameter(Mandatory)]$Tracker,
        [Parameter(Mandatory)][int]$Turn,
        $BackendLogActive
    )

    if ($Tracker.Stalled) { return }
    if ($null -eq $BackendLogActive) { return }
    if ($BackendLogActive -eq $false) {
        $Tracker.Stalled = $true
        $Tracker.StallDetectedAtTurn = $Turn
    }
}

# The loud, distinct callout (never folded into an ordinary dead-verb candidate line) -- $null when
# the tracker never tripped, so every call site can splice this in unconditionally.
function Get-BackendLogStallLine {
    param([Parameter(Mandatory)]$Tracker)

    if (-not $Tracker.Stalled) { return $null }
    return ('BACKEND LOG EVIDENCE CHANNEL DIED at turn ' + $Tracker.StallDetectedAtTurn + ': ' +
        'state.json''s own "backendLogActive" field (PlaytestLog.Active) read false -- ' +
        'PlaytestLog.Append failed and disabled itself PERMANENTLY for the rest of this process (see ' +
        'PlaytestLog.cs''s own fail-soft contract). Every dead-verb check from turn ' +
        $Tracker.StallDetectedAtTurn + ' onward reads "no sim event fired" because the CHANNEL is ' +
        'dead, not necessarily because the verb is -- see the UNRELIABLE candidates below and never ' +
        'read them as confirmed defects.')
}

# The "## Backend record" section for findings.md -- recorded facts, placed ABOVE the model's prose
# per the brief ("recorded facts first, the model's account second"). Pure text assembly; the caller
# still writes backend.json separately (ConvertTo-Json on the same $Summary is enough for that).
# -LivenessTracker (finding A, above) is optional so every existing caller keeps working unchanged;
# when it is stalled, the callout is inserted FIRST, before even the row/malformed-line count, so it
# is the first thing a reader of this section sees.
function Format-BackendMarkdown {
    param(
        [Parameter(Mandatory)]$Summary,
        [array]$Contradictions,
        $LivenessTracker = $null
    )

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('## Backend record')
    [void]$lines.Add('')
    if ($LivenessTracker -and $LivenessTracker.Stalled) {
        [void]$lines.Add('**' + (Get-BackendLogStallLine -Tracker $LivenessTracker) + '**')
        [void]$lines.Add('')
    }
    [void]$lines.Add('From ' + $Summary.LogPath + ' (PlaytestLog.cs''s JSONL trail) -- the kernel''s own')
    [void]$lines.Add('record, independent of what the model claims below.')
    [void]$lines.Add('')

    if (-not $Summary.Available) {
        [void]$lines.Add('**' + $Summary.Message + '**')
        return ($lines -join [Environment]::NewLine)
    }

    [void]$lines.Add($Summary.RowCount.ToString() + ' row(s), ' + $Summary.MalformedLineCount + ' malformed line(s).')
    if ($Summary.Session) {
        [void]$lines.Add('Session: provenance=' + $Summary.Session.Provenance + ', startedAt=' + $Summary.Session.StartedAt)
    }
    [void]$lines.Add('')

    [void]$lines.Add('### Phase timeline')
    [void]$lines.Add('')
    [void]$lines.Add(@($Summary.Advances).Count.ToString() + ' real phase advance(s) out of ' +
        @($Summary.Timeline).Count + ' tick row(s). By cause:')
    foreach ($c in $Summary.AdvanceCauseCounts) {
        [void]$lines.Add('- ' + $c.Cause + ': ' + $c.Count)
    }
    [void]$lines.Add('- auto-advances (game moved with no player press): ' + $Summary.AutoAdvanceCount)
    [void]$lines.Add('- press-advances: ' + $Summary.PressAdvanceCount)
    if ($Summary.UnattributedAdvanceCount -gt 0) {
        [void]$lines.Add('- UNATTRIBUTED advances (real transition, empty cause): ' + $Summary.UnattributedAdvanceCount)
    }
    [void]$lines.Add('')

    [void]$lines.Add('### Rejections')
    [void]$lines.Add('')
    [void]$lines.Add(@($Summary.Rejections).Count.ToString() + ' rejected action(s) (deduplicated across the accumulating rejects[] list). By reason:')
    foreach ($rc in $Summary.RejectionCountsByReason) {
        [void]$lines.Add('- ' + $rc.Reason + ': ' + $rc.Count)
    }
    if (@($Summary.RejectionCountsByReason).Count -eq 0) { [void]$lines.Add('(none)') }
    [void]$lines.Add('')

    [void]$lines.Add('### Sim events and attribution')
    [void]$lines.Add('')
    [void]$lines.Add($Summary.EventsTotalAcrossTicks.ToString() + ' event(s) total across every tick (a raw count only -- see caveat).')
    [void]$lines.Add('CAVEAT: ' + $Summary.AttributionCaveat)
    if (@($Summary.AttributionEventTypeHits).Count -gt 0) {
        [void]$lines.Add('AttributionBeatEvent named directly (PlaytestLog.Tick''s eventTypes field, not a text guess):')
        foreach ($h in $Summary.AttributionEventTypeHits) { [void]$lines.Add('- [t=' + $h.T + '] day ' + $h.Day + ' ' + $h.Phase) }
    }
    if (@($Summary.AttributionNoteHits).Count -gt 0) {
        [void]$lines.Add('Attribution-shaped note text found:')
        foreach ($h in $Summary.AttributionNoteHits) { [void]$lines.Add('- [t=' + $h.T + '] ' + $h.What) }
    }
    [void]$lines.Add('')

    [void]$lines.Add('### Narrator and autosave')
    [void]$lines.Add('')
    [void]$lines.Add($Summary.NarratorVoicedCount.ToString() + ' narrator line(s) voiced, ' + $Summary.NarratorTextOnlyCount + ' text-only (no recording).')
    [void]$lines.Add('Autosave writes: ' + $Summary.AutosaveWriteCount + ' (' + $Summary.AutosaveCaveat + ')')
    [void]$lines.Add('')

    [void]$lines.Add('### Contradiction checks')
    [void]$lines.Add('')
    if ($Contradictions -and @($Contradictions).Count -gt 0) {
        foreach ($c in $Contradictions) { [void]$lines.Add('- ' + $c) }
    } else {
        [void]$lines.Add('(none found)')
    }

    return ($lines -join [Environment]::NewLine)
}

# W3 (docs/plans/2026-08-10-002): the dead-verb detector (deadverb.ps1) needs a PER-TURN slice of
# this same log -- Get-BackendSummary above only ever answers for the WHOLE run. Extending that API
# here rather than forking a second reader, per the plan's own join-table instruction ("if U2's API
# is whole-log only, W3 extends it, never forks it").
#
# There is no turn number to join on: PlaytestLog rows are keyed by day/phase/elapsed-seconds "t",
# never a turn counter (see this file's own header), and day+phase is not a 1:1 turn key either (see
# Get-DriverBackendMismatches' own note on why no exact per-turn join exists between the driver and
# this log -- the control name a player presses is not the kernel action-type name this log records).
# What the driver DOES know precisely is a ROW COUNT: it can call Read-BackendLogRows once right
# before writing command.json for a press, and again once the next state.json (that press's own
# outcome) has arrived, and hand both counts here. PlaytestLog.Append is a single
# File.AppendAllText call per row and the log is never rewritten, so "the rows added since a count
# taken a moment ago" is an honest, race-free slice -- the same append-only contract
# Read-BackendLogRows already relies on elsewhere in this file.
function Get-BackendEventsForSlice {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$AllRows,
        [Parameter(Mandatory)][int]$RowCountBefore
    )

    $sliceRows = @()
    if ($AllRows.Count -gt $RowCountBefore) {
        $sliceRows = @($AllRows[$RowCountBefore..($AllRows.Count - 1)])
    }

    # Only "tick" rows carry an event count (PlaytestLog.Tick's own eventCount -- Adapter.LastEvents
    # .Count for whatever the tick just completed). "note"/"action" rows in the slice mean something
    # was SUBMITTED or narrated, never that the kernel's own event system fired, so they count toward
    # SliceRowCount but never toward EventCount -- the same convention Get-BackendSummary's own
    # EventsTotalAcrossTicks already uses for the whole-log case.
    $eventCount = 0
    $tickRowCount = 0
    foreach ($row in $sliceRows) {
        if ($row.kind -eq 'tick') {
            $tickRowCount++
            $eventCount += [int]$row.events
        }
    }

    return [pscustomobject]@{
        SliceRowCount = @($sliceRows).Count
        TickRowCount  = $tickRowCount
        EventCount    = $eventCount
        SawSimEvent   = ($eventCount -gt 0)
    }
}
