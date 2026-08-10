<#
.SYNOPSIS
    Pure logic for W4 (docs/plans/2026-08-10-002 "the playtest becomes a player"): a patience meter
    the model cannot fake but a counter can -- drained by frustration, reset by novelty, and honest
    about which of the two ways a run can end.

.DESCRIPTION
    Ruling 8 is the whole shape of this file: "one temperament clock, one constant set." Patience
    already encodes time pressure (a run has a finite turn budget); a second, per-persona set of
    drain/reset numbers would just be eight sets of invented weights measured at N<=2 runs each, so a
    sweep's quit-day clustering would tell you about constant CHURN, not about the game. Every number
    below is therefore GLOBAL and PINNED in this one file, versioned by $script:TemperamentVersion so
    a findings.md header can say exactly which constant set produced its quit (or its "budget reached"
    survival). Persona front-matter (personas.ps1's own Split-PersonaFrontMatter) may scale the START
    value only -- never these weights -- see personas.ps1's header for that format.

    Three things drain the meter (agent-playtest.ps1 calls Add-TemperamentDrain at each site, hooking
    the SAME call sites the existing driver already has rather than inventing new ones):
      - a refused/pre-refused action (the existing $preRefusals.Add call site)
      - a stuck-digest repeat (the existing $stuckFindings.Add call site, digestSeen[$digest] -eq 4)
      - a dead-verb candidate fire (W3's Get-DeadVerbVerdict resolution site -- OPTIONAL: a fixture or
        an early W4 lander with no W3 landed yet simply never calls this, which this file requires
        nothing special for -- it is just a drain source that may or may not fire)
    One thing RESETS it (not increments -- ruling 8's neighbor requirement, a full second wind rather
    than a partial refund): first-touch of a coverage surface this run had not touched before,
    detected by the driver comparing Get-CoverageTrackerTouchedCount before and after its own existing
    Add-CoverageTouch call (coverage.ps1) -- the same call site, not a parallel tracker.

    An EMPTY meter (Value <= 0) ends the run, and ruling 8's other neighbor requirement -- "the quit
    reason is the run's lead finding" -- is Get-TemperamentQuitFinding's whole job: it walks the drain
    history back to the last reset (or the run's start) and produces a plain-language headline
    ("quit day 2 Morning after 6 refusal(s) at BountiesPanel (turn 14)") plus the full drain list, so
    findings.md can lead with WHY, not just report a number hit zero. A run that spends its whole turn
    budget without emptying the meter gets the opposite, unambiguous message via
    Get-TemperamentBudgetEndNote ("budget reached, patience remaining N") specifically so the two
    endings are never conflated -- a reader must never have to guess which one happened.

    Monkey (this same wave's model-free persona, monkey.ps1) does NOT use this file at all: "it cannot
    get frustrated" (the plan's own words) -- agent-playtest.ps1 never constructs a meter when the
    resolved persona is monkey, and Scripted mode (no persona in the loop at all) does not either.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# --- The one constant set (ruling 8) ---------------------------------------------------------------
# Bumped whenever any number below changes, so a sweep's findings.md headers can say which constant
# set measured its quits -- the brief's own required proof that a sweep is never silently comparing
# quit-day clustering across two different rulesets.
$script:TemperamentVersion = 'temperament-v1'

# Sized so the plan's OWN worked example -- "quit day 2 Morning after 6 refusals at the Bounties
# panel" -- lands on exactly zero: 6 * PatienceDrainRefusal = PatienceStart. A round, human-checkable
# failure mode beats a number picked to look precise.
$script:PatienceStart = 18.0

# A single refused/pre-refused press: the model tried something that read as reasonable and the game
# said no. The baseline unit every other drain below is sized relative to.
$script:PatienceDrainRefusal = 3.0

# Worse than one refusal on purpose: the SAME screen for 4 turns straight (the existing stuck
# detector's own threshold) means nothing distinguishable happened at all across several turns, not
# just one wrong guess corrected on the next one.
$script:PatienceDrainStuckRepeat = 6.0

# Weighted equal to a refusal, not lighter: a press that was ACCEPTED but provably changed nothing
# (law-3's own dead-verb candidate) is exactly as wasted as an explicit refusal from the player's
# seat -- it is just quieter about it, which is why the detector exists at all.
$script:PatienceDrainDeadVerbCandidate = 3.0

# A major, mostly-fatal hit -- two-thirds of the whole meter in one event -- because this is not "the
# game said no to a button," it is "the one named hero this persona was told to protect is gone."
# Sized to end most runs within a turn or two afterward without making it impossible to ever recover
# (a full-meter run that had just reset on a fresh surface can still limp on a little further).
$script:PatienceDrainAttachedDeath = 12.0

# --- The meter --------------------------------------------------------------------------------------

# A fresh meter. -StartMultiplier comes from a persona's own front-matter (personas.ps1's
# Get-PersonaPatienceMultiplier) -- it scales ONLY this starting value, never the drain amounts above
# (ruling 8: per-persona DRAIN weights would be the exact invented-numbers problem the ruling forbids;
# a persona being generally more or less patient than another is a defensible, single, named number).
function New-TemperamentMeter {
    param([double]$StartMultiplier = 1.0)

    $start = $script:PatienceStart * $StartMultiplier
    return [pscustomobject]@{
        Value        = $start
        Max          = $start
        Version      = $script:TemperamentVersion
        DrainHistory = New-Object System.Collections.ArrayList
        Depleted     = $false
    }
}

# Drains the meter by $Amount and records why -- Turn/Day/Phase locate the drain in the run,
# Detail names the control/location responsible (nullable -- a stuck repeat's "detail" is the
# location+phase text, a refusal's is the control name, a dead-verb candidate's is the pressed
# control). Mutates $Meter in place (a PSCustomObject's properties are settable through the same
# reference) rather than returning a new one -- the caller already holds the one meter for the whole
# run and every call site needs to see the SAME object update, not a copy.
function Add-TemperamentDrain {
    param(
        [Parameter(Mandatory)]$Meter,
        [Parameter(Mandatory)][string]$Cause,
        [Parameter(Mandatory)][double]$Amount,
        [Parameter(Mandatory)][int]$Turn,
        $Day,
        [string]$Phase,
        [string]$Detail
    )

    $Meter.Value = $Meter.Value - $Amount
    [void]$Meter.DrainHistory.Add([pscustomobject]@{
        Turn       = $Turn
        Day        = $Day
        Phase      = $Phase
        Cause      = $Cause
        Detail     = $Detail
        Amount     = $Amount
        ValueAfter = $Meter.Value
    })
    if ($Meter.Value -le 0) { $Meter.Depleted = $true }
}

# A full reset (NOT an increment -- ruling 8's own words) to $Meter.Max, recorded in the same
# DrainHistory list with Cause='reset' and Amount=0 so Get-TemperamentQuitFinding's own "walk back to
# the last reset" logic has a marker to stop at. A depleted meter that gets reset is un-depleted --
# the whole point of novelty being a second wind, not a consolation prize on a run already over.
function Reset-TemperamentMeter {
    param(
        [Parameter(Mandatory)]$Meter,
        [Parameter(Mandatory)][int]$Turn,
        $Day,
        [string]$Phase,
        [string]$Surface
    )

    $Meter.Value = $Meter.Max
    $Meter.Depleted = $false
    [void]$Meter.DrainHistory.Add([pscustomobject]@{
        Turn       = $Turn
        Day        = $Day
        Phase      = $Phase
        Cause      = 'reset'
        Detail     = $Surface
        Amount     = 0.0
        ValueAfter = $Meter.Value
    })
}

# --- Coverage-tracker novelty check (hooks coverage.ps1's own New-CoverageTracker shape, never a
# parallel tracker of its own) -------------------------------------------------------------------
#
# coverage.ps1's tracker (New-CoverageTracker) is an ordered hashtable of category name -> hashtable
# used as a set (keys are the touched surface names, values unused). This sums every category's key
# count -- the SAME live object Add-CoverageTouch already mutates -- so the driver only needs to call
# this once before and once after its own existing Add-CoverageTouch call to know whether that one
# turn touched something genuinely new. Depends on coverage.ps1's tracker shape without needing to
# modify or duplicate that file.
function Get-CoverageTrackerTouchedCount {
    param([Parameter(Mandatory)]$Tracker)

    $total = 0
    foreach ($key in $Tracker.Keys) { $total += @($Tracker[$key].Keys).Count }
    return $total
}

# --- Reporting --------------------------------------------------------------------------------------

function Format-TemperamentDrainCauseLabel {
    param([string]$Cause)

    if ($Cause -eq 'refusal') { return 'refusal(s)' }
    if ($Cause -eq 'stuck') { return 'stuck repeat(s)' }
    if ($Cause -eq 'deadverb') { return 'dead-verb candidate(s)' }
    if ($Cause -eq 'attached-death') { return 'the named hero''s death' }
    return ($Cause + '(s)')
}

# The run's LEAD finding (ruling 8 / the plan's own worked example) -- walks $Meter.DrainHistory
# backward to the most recent 'reset' entry (or the run's start, if there was never one) so the
# headline only ever explains drains that happened SINCE the meter was last full, which is the
# honest "why now" story: a reset means the player got a second wind, so only what happened after it
# is why the meter is empty again.
function Get-TemperamentQuitFinding {
    param(
        [Parameter(Mandatory)]$Meter,
        [Parameter(Mandatory)][int]$Turn,
        $Day,
        [string]$Phase
    )

    $sinceReset = New-Object System.Collections.ArrayList
    for ($i = $Meter.DrainHistory.Count - 1; $i -ge 0; $i--) {
        $entry = $Meter.DrainHistory[$i]
        if ($entry.Cause -eq 'reset') { break }
        [void]$sinceReset.Insert(0, $entry)
    }

    $counts = @{}
    $lastDetail = ''
    foreach ($e in $sinceReset) {
        if (-not $counts.ContainsKey($e.Cause)) { $counts[$e.Cause] = 0 }
        $counts[$e.Cause] = $counts[$e.Cause] + 1
        if ($e.Detail) { $lastDetail = $e.Detail }
    }

    $parts = @()
    foreach ($cause in ($counts.Keys | Sort-Object)) {
        $parts += ($counts[$cause].ToString() + ' ' + (Format-TemperamentDrainCauseLabel $cause))
    }
    $causesText = ($parts -join ', ')
    if (-not $causesText) { $causesText = 'a drained patience meter' }

    $atText = ''
    if ($lastDetail) { $atText = ' at ' + $lastDetail }

    $headline = 'quit day ' + $Day + ' ' + $Phase + ' after ' + $causesText + $atText + ' (turn ' + $Turn + ')'

    return [pscustomobject]@{
        Headline           = $headline
        DrainHistory       = @($sinceReset)
        TemperamentVersion = $Meter.Version
    }
}

# The OTHER ending -- a run that spent its whole turn budget without ever emptying the meter. Kept as
# its own function (not just an inline string at the call site) so the two endings' text lives next
# to each other in one file and cannot silently drift into looking alike.
function Get-TemperamentBudgetEndNote {
    param([Parameter(Mandatory)]$Meter)

    return ('budget reached, patience remaining ' + [math]::Round($Meter.Value, 1))
}

# findings.md's own "## Patience" section -- the full drain history behind whichever headline applies,
# placed alongside backend/metrics/dead-verb as its own build-once-use-everywhere block (same
# convention as $backendSection/$metricsSection/$deadVerbSection in agent-playtest.ps1).
function Format-TemperamentMarkdown {
    param(
        [Parameter(Mandatory)]$Meter,
        $QuitFinding
    )

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('## Patience (temperament ' + $Meter.Version + ')')
    [void]$lines.Add('')

    if ($Meter.Depleted -and $QuitFinding) {
        [void]$lines.Add($QuitFinding.Headline + '.')
        [void]$lines.Add('')
        [void]$lines.Add('Drain history since the last reset (or the run''s start):')
        foreach ($e in $QuitFinding.DrainHistory) {
            $detailText = ''
            if ($e.Detail) { $detailText = ' (' + $e.Detail + ')' }
            [void]$lines.Add('- turn ' + $e.Turn + ' day ' + $e.Day + ' ' + $e.Phase + ': ' + $e.Cause +
                $detailText + ', -' + $e.Amount + ' -> ' + $e.ValueAfter)
        }
    } else {
        [void]$lines.Add((Get-TemperamentBudgetEndNote -Meter $Meter) + '.')
    }

    return ($lines -join [Environment]::NewLine)
}
