<#
.SYNOPSIS
    S2 (scripted deep pilot lane): a model-free, human-SHAPED player -- not a solver. Given a turn's
    state, decides one command the exact way Get-MonkeyCommand does (same file-channel contract,
    same "legal by construction" guarantee, same byte-identical-given-seed determinism), but the
    CHOICE is a deliberately imperfect, habit-forming, curious policy instead of uniform-random.

.DESCRIPTION
    Owner steer (2026-08-11, arrived mid-build): the deep pilot exists to replicate what a human
    would do and to surface FINDINGS, not to maximize day count. A robot that plays perfectly finds
    nothing, because no human plays that way. This file is built around that steer, not around the
    original "competent, repeatable" framing alone:

      1. READS BEFORE ACTING. The first time this run sees a given phase+location combination, it
         prefers an INFORMATIONAL move (walk toward something unvisited, open a board to look) over
         the transactional default -- see $Memory.VisitedSurfaces and the day<=2 curiosity gate in
         Get-PilotCommand. A "read" turn is a real, legal turn (never a no-op the game would refuse).

      2. IMPERFECT ON PURPOSE. Every one of CLAUDE.md's six named decisions (sell-vs-hold,
         fill-vs-upgrade, spend-vs-bank the slot, ore-vs-goodwill, send-vs-trust the runner) gets
         resolved by a SEEDED coin flip against a documented probability, never always the same way,
         and every resolution is logged to $Memory.SixDecisions with which side it took and why. The
         seventh CLAUDE.md decision ("price for the sale or the relationship") is structurally
         unreachable by this harness -- see the PRICING FRICTION note below -- so it cannot be
         exercised at all, which is itself logged as friction rather than silently skipped.

      3. HABIT, THEN DEVIATION. Early turns (day <= 2) lean curious; from day 5 a phase whose chosen
         action-prefix has repeated 3 times running is marked "locked" ($Memory.HabitLocked), and a
         later turn in that phase choosing something ELSE is logged as a rhythm-deviation friction
         entry -- the day-11 boredom question needs a "the routine changed HERE" marker, not just a
         raw action list.

      4. FRICTION IS THE DELIVERABLE. $Memory.FrictionLog accumulates, per entry: Turn/Day/Phase, a
         Category (refused / idle-stretch / no-route / harness-limit), a Detail that QUOTES the
         on-screen text or refusal copy verbatim, and Trying (what the pilot was attempting). The
         driver script folds this into findings.md's own "## Friction log" section and into
         metrics.json verbatim -- see agent-playtest.ps1's own pilot branch.

      5. NO CHEATING ON KNOWLEDGE. Every function in this file reads ONLY $State's own fields --
         screenText, controls[].enabled, nearby[].label/direction/distance/inRange, phase/day/beat/
         gold/lastOutcome -- the exact digest a human reading the screen would have. Nothing here
         ever reads sim/GameSim state directly (that would be cheating on the decision; the ENGINE
         TEST in godot/tests is a different artifact and is explicit about verifying outcomes from
         internal state AFTER the fact, never using it to decide).

    PRICING FRICTION (found while designing this, not a bug to fix here): ShopPanel's StockPrice_*
    control is a SpinBox, not a Button -- ScreenObservation.ObservedControls only lists Button
    subclasses (godot/scripts/tools/ScreenObservation.cs), and AgentPlaytestBridge's action vocabulary
    (press/move/key/advance/stop) has no "set a numeric value" or "scroll" verb. So neither this pilot
    nor the vision-model personas can ever adjust a listed price away from SuggestedPrice.For(item) --
    every Stock_ press sells at the auto-suggested price, always. A live human at a real keyboard/mouse
    CAN drag or scroll-wheel the SpinBox (PriceTag.cs's own _GuiInput does), so this is a gap in the
    HARNESS's action vocabulary, not in the game -- logged once per run as a 'harness-limit' friction
    entry rather than silently working around it by inventing a verb this harness's own contract does
    not have.

    FORGE MINIGAME, PLAYED FOR REAL: ForgeMinigame's readout label prints "Strike X/Y -- Heat Z --
    pumping|idle" and QuenchMinigame's prints "Heat Z (target T +/-B) -- PLUNGE NOW|wait for it..." --
    both plain Label text, both already inside $State.screenText. MinigameInput.cs registers "bellows"/
    "forge_strike"/"plunge" as real InputMap actions (a held-key tap-to-toggle escape hatch exists for
    "bellows" specifically -- ForgeMinigame.cs's own BellowsTapMaxHoldSeconds comment), so the driver's
    generic `{"action":"key","target":"bellows"}` (AgentPlaytestBridge.ApplyKey: press, 3 frames,
    release) toggles the bellows on a real, running client exactly the way a human's brief Shift-tap
    would. This file drives Act 1 (bellows/strike) and Act 2 (plunge) purely by regex-reading that
    readout text -- never by reading ForgeMinigame.HeatYPermille directly, which only the in-process
    engine test (godot/tests, same honesty rule as HumanPlayer/ForgePlayer) is allowed to do.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# --- Tunable probabilities for the six decisions (imperfect-on-purpose, ruling: named numbers, not
# hidden ones) ---------------------------------------------------------------------------------------
$script:PilotAcceptCommissionChance   = 0.75  # accept vs decline a commission
$script:PilotStockChance              = 0.80  # sell the good one (stock it) vs hold it back
$script:PilotWorkForgeChance          = 0.85  # work the forge (minigame) vs take the flat auto-craft
$script:PilotCraftThisWindowChance    = 0.85  # spend this expedition's action slot on a craft vs bank it
$script:PilotBuyMaterialChance        = 0.75  # buy the ore vs conserve gold (goodwill)
$script:PilotSendRunnerChance         = 0.70  # send the runner vs trust their own judgment
$script:PilotHonorMemorialChance      = 0.85  # honor the memorial vs walk past it
$script:PilotCuriosityChance          = 0.40  # (day <= 2 only) detour to something unvisited
$script:PilotCuriosityDayCeiling      = 2
$script:PilotHabitDayFloor            = 5
$script:PilotHabitStreakToLock        = 3
$script:PilotStuckFrictionThreshold   = 4     # matches the driver's own STUCK detector (4 turns)
# fix/pilot-finds-its-way: the general no-progress detector Get-PilotNavigateCommand runs against
# EVERY target it walks or interacts toward -- consecutive turns aimed at the same target with an
# unchanged (location, distance, inRange, screenText) reading means the approach is not working
# (a collision, a swallowed interact, a drawer that will not close), never that a person just needs
# one more try. Small and bounded per the owner's own requirement, not a number the run outlives.
#
# fix/the-pilot-goes-around: bumped 3 -> 4 to make room for what a person actually does when a
# straight walk hits a wall -- they do NOT keep pressing the same direction (that was the whole
# defect this branch exists to fix: `move` reporting an unchanged `pos (x,y) from (x,y)` turn after
# turn while the pilot kept sending the identical bearing). Get-PilotSidestepDirection maps stuck-
# count 1/2 to a perpendicular nudge (both signs, biased toward the target's own OTHER axis when the
# bearing string reveals it) and stuck-count 3 back to the plain bearing (re-approach, now that the
# sidestep may have cleared whatever was in the way) -- THEN, only if stuck-count reaches this
# threshold with still no change, the existing give-up-for-the-day blacklist below fires exactly as
# it always has. Every one of those attempts is still a real move command, so the existing
# no-response-press friction check (Get-PilotCommand's own step 1) still fires on each one -- this
# changes WHICH direction is attempted, never whether a blocked attempt gets reported.
$script:PilotNavStuckThreshold        = 4
# fix/the-pilot-goes-around (real-run finding, seed 1, live 220-turn run): the EXACT-equality
# snapshot above never once matched while the pilot crept toward Material Shelf/Anvil -- turnlog.md's
# own "moved DIR 30f -> pos (X,Y) from (X2,Y2)" outcomes showed deltas of ONE OR TWO PIXELS per move
# (2202,136)->(2200,138), turn after turn, for 130+ px of a diagonal approach -- so $target.distance
# always ticked down by 1-2 and the whole-snapshot string was never byte-identical, never triggering
# a sidestep. PlayerController2D.Speed is 90px/sec (PlayerController2D.cs), so a clean, unobstructed
# $script:PilotMoveFrames (30-frame, 0.5s) hold should cover roughly 45px -- 1-2px is the player
# scraping along a wall almost fully blocked, not a legitimately slow walk. A person does not read
# "I moved one pixel that time" as progress and keep going; they notice they are barely moving and
# try something else. This is the MINIMUM pixels $target.distance must close by, per move attempt,
# to count as real progress -- comfortably above the measured stuck-creep (1-3px) and comfortably
# below a clean move's own expected ~45px, so a genuinely slow-but-working approach (nearing the
# interact threshold, where a clean step could plausibly close only a modest amount) still reads as
# progress while a soft geometry slide does not.
$script:PilotNavMinProgressPx         = 8
# Hard ceiling on the file's own global idle-stretch counter (Get-PilotCommand, digest-based) --
# once a run has produced $script:PilotStuckFrictionThreshold consecutive identical turns the ONE
# friction entry already fires; if it reaches DOUBLE that, something outside navigation itself is
# looping (a phase policy re-pressing a dead verb Get-PilotNavigateCommand never touches), and the
# only honest answer left is to force real progress rather than ask the same policy again for what
# will provably be the same answer.
$script:PilotGlobalStuckForceAdvance  = 8
# fix/the-pilot-goes-around (real-run finding, seed 1): 30 was calibrated against "frames == physics
# ticks", which is false the instant AgentPlaytest.ps1 launches with --disable-vsync (required --
# see that launch line's own doc -- an unattended session with no compositor otherwise stalls
# Settle() forever). AgentPlaytestBridge.Settle() awaits SceneTree's process_frame (idle/render)
# signal, not physics_frame; with vsync off the render loop can run far faster than the fixed 60Hz
# physics tick rate, so most render frames land BETWEEN physics ticks. Measured directly on a live
# 220-turn run: PlayerController2D.Speed is 90px/sec (45px expected over a clean 0.5s/30-tick hold),
# but turnlog.md's own "moved DIR 30f -> pos (X,Y) from (X2,Y2)" outcomes showed 1-3px REGARDLESS OF
# DIRECTION, turn after turn -- not one wall in one direction, but every direction moving at ~5-10%
# of the expected rate, which no amount of sidestepping in pilot.ps1 can out-walk (a perfect
# sidestep into a direction that is ALSO frame-starved still only gains 1-3px). AgentPlaytestBridge.
# ApplyMove clamps Frames to at most 300 (Math.Clamp(command.Frames ?? 20, 1, 300)) -- raised to that
# ceiling here rather than guessing a smaller number, since this file cannot fix the underlying
# render/physics decoupling (that lives in the production C# bridge, out of this unit's scope) and
# more headroom costs nothing extra in TURN budget (still one command per turn either way).
$script:PilotMoveFrames               = 300
# Tighter than AgentPlaytestBridge's own 96px InRangeReportingPx -- see Get-PilotNavigateCommand's
# own note on why trusting the reported inRange alone let a swallowed interact go unnoticed.
$script:PilotInteractDistancePx       = 24
# Owner steer (2026-08-11): DEAD STRETCH -- "consecutive turns where the only legal thing is advance."
# 3 running is enough to call it a stretch rather than one ordinary quiet beat (Deep Vigil ticks, an
# empty Camp stop) -- see Complete-PilotTurn's own doc.
$script:PilotDeadStretchThreshold     = 3

# A fresh, empty memory -- one per run, passed to every Get-PilotCommand call and mutated in place
# (a PSCustomObject's properties are settable through the same reference -- same idiom
# temperament.ps1's New-TemperamentMeter/Add-TemperamentDrain already use for a run-lifetime object).
function New-PilotMemory {
    return [pscustomobject]@{
        VisitedSurfaces    = @{}
        LastDigest         = ''
        StuckCount         = 0
        RoutinePhaseChoice = @{}
        RoutineStreak      = @{}
        HabitLocked        = @{}
        PendingIntent      = '(run start)'
        PricingFrictionLogged = $false
        # fix/pilot-finds-its-way: the general no-progress detector (Get-PilotNavigateCommand) --
        # NavDigest is the last (location, distance, inRange, screenText) snapshot recorded while
        # aiming at a given nearby target key; NavStuckCount is how many consecutive turns that
        # snapshot has read IDENTICAL despite issuing a real move/interact toward it every time.
        # NavBlocked is the resulting blacklist, keyed by target key -> the DAY it was blocked (NOT a
        # bare bool -- a real-run finding on seed 4242 measured a permanent-forever blacklist turning
        # one early stuck episode into zero Stock_/BuyMat_/WorkForge_ presses for the rest of a
        # 22-day run, which is exactly the "papering over the wall" the owner's steer warns against).
        # A block only holds for the REST of the day it was set; Get-PilotBestReachableTarget clears
        # it the instant $State.day moves on, so a target gets a fresh try every day rather than being
        # exiled for the run. See that function's own doc for why a target-scoped digest catches BOTH
        # a swallowed interact (screen never changes) and a blocked move (distance never drops), the
        # two shapes the owner's steer named by name.
        NavDigest          = @{}
        NavStuckCount      = @{}
        NavBlocked         = @{}
        # fix/the-pilot-goes-around (real-run finding, seed 1): keyed by the KEYWORD a caller searched
        # for (e.g. "shelf", "anvil" -- Get-PilotEnterInteriorCommand's own $StationKeyword), value is
        # the DAY every reachable candidate for that keyword last proved stuck (Get-PilotNavigateCommand
        # sets this at the same moment it would answer "every reachable target here is stuck, leaving").
        # Distinct from NavBlocked (keyed by a target's own internal `.key`, which is only knowable
        # once already standing inside the room): this lets Get-PilotEnterInteriorCommand refuse to
        # re-ENTER a building at all -- by walking OR by QuickTravel -- when today's attempt on that
        # station already failed, rather than walking/travelling all the way back in only to
        # immediately find the same blacklisted station and cancel back out. Day-scoped exactly like
        # NavBlocked, for the same reason: tomorrow is a fresh try, never a permanent exile.
        StationGivenUpToday = @{}
        # 2026-08-11 real-run finding: pressing CommissionAccept_1/CommissionDecline_1 does NOT
        # remove that row from the board or disable either button -- the dead-verb detector
        # confirmed a byte-identical whole-state fingerprint before/after the press, repeatedly,
        # against a real running client. Whatever the underlying mechanic (a same-panel selection
        # that only resolves on close, or a genuine dead verb), re-pressing the SAME commission
        # forever is not a person's behavior -- a human decides once and moves on. ActedOn tracks
        # "decision-kind:id" pairs (e.g. "commission-1", "honor-7") so each candidate is acted on
        # AT MOST ONCE per run, then the policy closes the panel instead of re-opening the loop.
        ActedOn            = @{}
        FrictionLog        = New-Object System.Collections.ArrayList
        SixDecisions       = New-Object System.Collections.ArrayList
        # Owner steer (2026-08-11, "the test must try to follow as a human would... pay attention to
        # no interaction, lack of response etc"): PendingAction is what the LAST turn's command was
        # (action/target/label/why/kind) plus a snapshot of $State AS SEEN when it was chosen --
        # compared, at the top of the NEXT call, against $State AS SEEN now, to answer "did anything
        # happen" for EVERY verb (press/move/key/advance), not just the nav-specific case
        # Get-PilotNavigateCommand's own NavDigest already covers. See Test-PilotAcknowledgement's doc.
        PendingAction      = $null
        MinigameActive     = $false
        # DeadStretchRun: consecutive turns whose ONLY legal option was the "nothing phase-specific
        # available" advance-fallback (Get-PilotCommand's own catch-all) -- a real "the day-11 boredom
        # question, measured instead of guessed" per the owner's steer. Holds the FIRST turn/day/phase
        # of the current streak, or $null when the streak is not running.
        DeadStretchRun     = $null
    }
}

function Add-PilotFriction {
    param(
        [Parameter(Mandatory)]$Memory,
        [Parameter(Mandatory)]$Turn,
        $Day,
        [string]$Phase,
        [Parameter(Mandatory)][string]$Category,
        [Parameter(Mandatory)][string]$Detail,
        [string]$Trying = ''
    )
    [void]$Memory.FrictionLog.Add([pscustomobject]@{
        Turn     = $Turn
        Day      = $Day
        Phase    = $Phase
        Category = $Category
        Detail   = $Detail
        Trying   = $Trying
    })
}

function Add-PilotDecision {
    param(
        [Parameter(Mandatory)]$Memory,
        $Day,
        [Parameter(Mandatory)][string]$Decision,
        [Parameter(Mandatory)][string]$Choice,
        [string]$Why = ''
    )
    [void]$Memory.SixDecisions.Add([pscustomobject]@{
        Day      = $Day
        Decision = $Decision
        Choice   = $Choice
        Why      = $Why
    })
}

# Owner steer (2026-08-11): "pay attention to no interaction, lack of response etc." Everything a
# human would notice as "I did that and nothing happened" reduces to comparing a snapshot of the
# whole visible screen taken right before a command was chosen against one taken right after it
# resolved -- the SAME shape Get-PilotNavigateCommand's own NavDigest already uses for the nav-only
# case, generalized here to cover every verb (press/move/key/advance).
function Get-PilotFullSnapshot {
    param($State)
    return ([string]$State.location + '|' + [string]$State.gold + '|' + [string]$State.day + '|' +
        [string]$State.phase + '|' + [string]$State.beat + '|' + [string]$State.canMove + '|' +
        [string]$State.actionSlotsRemaining + '|' + (([string[]]@($State.screenText)) -join ';'))
}

# The visible LABEL a person reads for $Name (e.g. "Buy 1", "Work the forge"), or $Name itself when
# it names no on-screen control at all (move/key/advance targets, or a control that scrolled off
# between turns) -- friction entries should quote what a human would have read, not an internal id.
function Get-PilotControlLabel {
    param($State, [string]$Name)
    if (-not $Name) { return '' }
    $match = @($State.controls | Where-Object { $_ -and [string]$_.name -eq $Name }) | Select-Object -First 1
    if ($match -and $match.label) { return [string]$match.label }
    return $Name
}

# Owner steer, kind 4 (UNREADABLE REFUSAL): "the on-screen reason would not tell a human what to do
# differently (empty, generic, or naming a control/term not on screen)." AgentPlaytest.ApplyPress
# already surfaces the GAME's own tooltip text for a disabled control ("refused: 'X' is disabled --
# {reason}") -- that reason is genuinely player-facing copy (ForgePanel's own Gate() WhyNot strings,
# etc.) and is treated as readable by default; only the HARNESS's own generic fallbacks (a target that
# does not exist on screen, a nonsense move direction, or copy pointing back at itself with nothing a
# person could act on) are flagged. Candidate-shaped: this names what LOOKS unreadable, not a verdict.
function Test-PilotRefusalReadable {
    param([Parameter(Mandatory)][string]$Outcome)
    if (-not $Outcome.StartsWith('refused:')) { return $true }
    $reason = $Outcome.Substring(8).Trim()
    if ([string]::IsNullOrWhiteSpace($reason)) { return $false }
    if ($reason.ToLowerInvariant().Contains('(no reason on the tooltip)')) { return $false }
    $unhelpful = @('unknown action', "unknown move dir")
    foreach ($phrase in $unhelpful) {
        if ($reason.ToLowerInvariant().Contains($phrase)) { return $false }
    }
    return $true
}

# Seeded coin flip against $Probability. $Random is the SAME System.Random instance the caller reuses
# for the whole run (own by the driver, exactly like monkey's), so "same seed, same state sequence"
# still means a byte-identical command stream -- NextDouble() advances the same underlying stream
# Next(int) does.
function Test-PilotChance {
    param([Parameter(Mandatory)][System.Random]$Random, [Parameter(Mandatory)][double]$Probability)
    return ($Random.NextDouble() -lt $Probability)
}

function Build-PilotCommandJson {
    param(
        [Parameter(Mandatory)][string]$Action,
        [string]$Target = $null,
        [string]$Dir = $null,
        [int]$Frames = 0,
        [Parameter(Mandatory)][string]$Why
    )

    $obj = [ordered]@{}
    $obj.action = $Action
    if ($Target) { $obj.target = $Target }
    if ($Dir) {
        $obj.dir = $Dir
        $obj.frames = $Frames
    }
    elseif ($Action -eq 'wait' -and $Frames -gt 0) {
        # 'wait' carries a frame count with no direction -- the bridge's ApplyWait spends them and
        # presses nothing. See its doc for why every "harmless key" this replaced turned out live.
        $obj.frames = $Frames
    }
    $obj.why = $Why
    return (([pscustomobject]$obj) | ConvertTo-Json -Compress)
}

function Get-PilotEnabledControls {
    param($State, [Parameter(Mandatory)][string]$Pattern)
    return ,@($State.controls | Where-Object { $_ -and $_.enabled -and ([string]$_.name -match $Pattern) })
}

# The trailing "_<id>" a per-row control name carries (CommissionAccept_7 -> "7"), or the whole
# name unchanged if there is no such suffix. Used to key $Memory.ActedOn per underlying row rather
# than per exact control name, so accepting OR declining commission 7 both retire the SAME id.
function Get-PilotControlId {
    param([Parameter(Mandatory)][string]$Name)
    $m = [regex]::Match($Name, '_(\d+)$')
    if ($m.Success) { return $m.Groups[1].Value }
    return $Name
}

# $Candidates filtered down to rows this run has not already acted on under $Kind (see ActedOn's
# own doc in New-PilotMemory) -- the fix for the real-run finding that pressing the SAME
# CommissionAccept_1 over and over is a dead verb, not a decision.
function Get-PilotUnactedControls {
    param($Candidates, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][string]$Kind)
    return ,@($Candidates | Where-Object { -not $Memory.ActedOn.ContainsKey($Kind + '-' + (Get-PilotControlId $_.name)) })
}

# The first of $Candidates (in the order given -- $State.nearby already arrives nearest-first,
# AgentPlaytestBridge.cs's own Surroundings().OrderBy(distance)) whose key is not CURRENTLY on
# $Memory's no-progress blacklist, or $null when every candidate here has already proven stuck today.
# See Get-PilotNavigateCommand's own doc for what "stuck" means and why a per-key blacklist is the fix.
#
# fix/pilot-finds-its-way (real-run finding, seed 4242): a blacklist entry that never expired turned
# one early stuck episode into a permanent exile -- measured on a full 220-turn/22-day run, a single
# early failure to reach the Forge building blacklisted it on day 1 or 2, and the run never pressed a
# single Stock_/BuyMat_/WorkForge_ button again for the remaining ~20 days (0 of each, confirmed
# against the driver's own turn-by-turn log). That is "papering over the wall" in exactly the way the
# owner's steer warns against -- it stopped the infinite loop but also stopped the pilot from ever
# trying again, which a real person would not do (a doorway that was blocked yesterday is not
# permanently blocked). $Memory.NavBlocked now stores the DAY a key was blocked, and a block only
# holds for the REST of that same day -- a new day is a fresh chance, tied to the game's own
# meaningful boundary (world state, party positions, and the day's own obstacles all move) rather than
# an arbitrary turn-count cooldown.
function Get-PilotBestReachableTarget {
    param($Candidates, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)]$Day)
    return ($Candidates | Where-Object {
        $_.key -and -not ($Memory.NavBlocked.ContainsKey([string]$_.key) -and $Memory.NavBlocked[[string]$_.key] -eq [int]$Day)
    } | Select-Object -First 1)
}

# fix/the-pilot-goes-around: what a person actually does at a wall -- not keep walking into it. Given
# the target's own bearing string (AgentPlaytest.cs's Bearing(): a single axis word like "right", or a
# diagonal "right+down" when the offset is genuinely two-axis) and which sidestep try this is (1 =
# first, 2 = the opposite sign), returns the perpendicular direction to move instead of the bearing.
#
# BIASED TOWARD THE TARGET'S OTHER AXIS: a diagonal bearing already names both axes, dominant first
# (Bearing()'s own ordering -- "right+down" means rightward is the bigger offset, downward the
# smaller one) -- the secondary word IS the target's other axis, so attempt 1 uses it directly and
# attempt 2 tries its opposite. A single-axis bearing ("right") carries no such information at all --
# Bearing() only collapses to one word when the OTHER axis is small or nonexistent, so there is
# nothing to bias toward and both signs are tried in a fixed, deterministic order instead (still
# "both signs", just not a biased pick).
#
# Returns $null for "here" (already effectively at the target -- InRange should already be true by
# then, so this path should not normally be reached) or an unrecognized word.
function Get-PilotSidestepDirection {
    param([Parameter(Mandatory)][string]$Direction, [Parameter(Mandatory)][int]$Attempt)

    $opposite = @{ 'left' = 'right'; 'right' = 'left'; 'up' = 'down'; 'down' = 'up' }
    $parts = @($Direction.Split('+'))

    if ($parts.Count -ge 2 -and $opposite.ContainsKey($parts[1])) {
        $secondary = $parts[1]
        if ($Attempt -eq 1) { return $secondary }
        return $opposite[$secondary]
    }

    $primary = $parts[0]
    if ($primary -eq 'left' -or $primary -eq 'right') {
        if ($Attempt -eq 1) { return 'up' }
        return 'down'
    }
    if ($primary -eq 'up' -or $primary -eq 'down') {
        if ($Attempt -eq 1) { return 'right' }
        return 'left'
    }
    return $null
}

# Walk toward (then enter/interact with) the nearest reachable $State.nearby entry whose label
# contains $Keyword, or the nearest reachable one at all if no keyword was given -- the same
# "Around you" reading a model is taught in act.md rule 8, just applied by regex instead of by an
# LLM reading prose. Returns $null when there is nothing left to navigate toward (cannot move,
# nothing nearby, a keyword was wanted and not found, OR every candidate is blacklisted and this is
# not an interior to escape from), so the caller falls through to its own next choice.
#
# 2026-08-11 real-run finding: once inside an interior, $State.nearby lists ONLY that room's own
# stations (AgentPlaytestBridge.cs's Surroundings() — a town building's nearby becomes a DIFFERENT
# list the moment InteriorActive flips), with no "exit" entry at all. The first version of this
# function fell back to the NEAREST nearby entry whenever the keyword did not match -- so asking for
# "shop" while standing inside the FORGE fell back to whichever forge station happened to be
# closest, and kept interacting with forge stations forever, never leaving. Fixed: a keyword miss
# while $State.location already reads "interior:..." backs OUT (key: cancel) instead of wandering
# the wrong building; a keyword miss OUTDOORS still falls back to nearest (every town building is
# already in that list, so a genuine miss there means the building does not exist this run).
#
# fix/pilot-finds-its-way: the ORIGINAL wall this whole file exists to fix. The pilot walked toward
# "Stock Crates"/"Material Shelf" and never moved again -- this function had no way to notice its own
# move/interact was going nowhere, so it issued the SAME command forever. Mechanical, bounded no-
# progress detection now runs on every target this function ever picks: a (location, distance,
# inRange, screenText) snapshot is recorded per target key, and if that snapshot reads IDENTICAL for
# $script:PilotNavStuckThreshold turns running -- despite a real move or interact having been sent
# each time -- the target is blacklisted ($Memory.NavBlocked) and a friction entry quotes the frozen
# screen. This one mechanism covers BOTH shapes the owner's steer named: a blocked MOVE (distance
# never drops -- a collision) and a swallowed INTERACT (screen never changes -- the exact "376-turn
# entering Anvil" loop that produced zero crafts, since that loop is this same function issuing
# "key: interact" against the anvil turn after turn). Once a target is blocked this call immediately
# retries with the NEXT reachable candidate (recursion, bounded by $nearbyList's own finite size,
# never a wasted turn) -- another station, or (Get-PilotBestReachableTarget returning $null for every
# remaining candidate) a real "key: cancel" to leave the room, now that AgentPlaytest.ApplyKey
# dispatches a genuine input event a room's own Escape ladder can actually see (see that file's own
# fix comment) rather than the ActionPress/ActionRelease pair that could not.
function Get-PilotNavigateCommand {
    param($State, [string]$Keyword, [Parameter(Mandatory)]$Memory)

    if (-not $State.canMove) { return $null }
    $nearbyList = @($State.nearby)
    $inInterior = ([string]$State.location).StartsWith('interior:')
    if ($nearbyList.Count -eq 0) {
        if ($inInterior) {
            return (Build-PilotCommandJson -Action 'key' -Target 'cancel' -Why 'pilot: nothing here to reach, leaving')
        }
        return $null
    }

    $candidates = $nearbyList
    if ($Keyword) {
        $candidates = @($nearbyList | Where-Object { $_.label -and ([string]$_.label).ToLowerInvariant().Contains($Keyword) })
        if ($candidates.Count -eq 0) {
            # A wanted keyword matching nothing HERE is a miss, not "go anywhere nearby" -- the
            # first version of this function fell back to $nearbyList[0] regardless, which inside
            # an interior means the nearest station of whatever room we already happen to be in.
            # Back out first if that is where we are; outdoors, every town building is already in
            # this list, so a miss there means the building genuinely does not exist and there is
            # nothing sensible to fall back to either.
            if ($inInterior) {
                return (Build-PilotCommandJson -Action 'key' -Target 'cancel' -Why ('pilot: ' + $Keyword + ' is not in here, leaving'))
            }
            return $null
        }
    }

    $target = Get-PilotBestReachableTarget -Candidates $candidates -Memory $Memory -Day $State.day
    if (-not $target) {
        # Every candidate this call could have picked has already proven stuck. Escaping the room
        # (now that key:cancel genuinely works) is the only useful move left; outdoors there is
        # nothing sensible to fall back to and the caller's own next choice takes over.
        #
        # fix/the-pilot-goes-around (real-run finding, seed 1): recording the give-up here, keyed by
        # $Keyword and scoped to today, is what stops Get-PilotEnterInteriorCommand's own QuickTravel
        # branch (or an ordinary walk-in) from re-entering THIS SAME building five turns later only to
        # immediately hit the identical blacklisted station and cancel back out. Measured directly: a
        # live run ping-ponged QuickTravel_Forge <-> cancel for 100+ straight turns (turns ~15-220,
        # never advancing past day 1) because entering cost nothing and nothing remembered that
        # "shelf" was already given up on for today. A person who fails to reach the shelf does not
        # walk straight back in five seconds later hoping it changed -- they try again tomorrow.
        if ($Keyword) {
            $Memory.StationGivenUpToday[$Keyword] = [int]$State.day
        }
        if ($inInterior) {
            return (Build-PilotCommandJson -Action 'key' -Target 'cancel' -Why 'pilot: every reachable target here is stuck, leaving')
        }
        return $null
    }

    # No-progress detection: a snapshot of everything that SHOULD change if a move/interact toward
    # this exact target actually worked. Comparing against the SAME target's own last snapshot (never
    # a different target's) means switching targets never falsely counts as "stuck".
    #
    # fix/the-pilot-goes-around (real-run finding, seed 1): this used to be ONE exact-equality string
    # compare over (location, distance, inRange, screenText) -- which never fires while distance is
    # ticking down by even one pixel a turn. A live run measured EXACTLY that: 1-2px real position
    # deltas per 30-frame move, turn after turn, scraping along a wall for well over a hundred pixels
    # of "approach" that never once produced a byte-identical snapshot. Split into two independent
    # signals instead: CONTEXT (location + screenText -- unrelated to this target's own distance;
    # a real change here, e.g. a message appearing or the room changing, is unambiguous progress on
    # its own) and DISTANCE (compared numerically against the SAME target's own last reading, not
    # string-equality -- see $script:PilotNavMinProgressPx's own doc for why a small nonzero
    # improvement still counts as stuck). $target.inRange is redundant with distance (AgentPlaytest.cs
    # derives it FROM distance) and is dropped rather than kept as a second copy of the same fact.
    $navKey = [string]$target.key
    $navContext = ([string]$State.location + '|' + (([string[]]@($State.screenText)) -join ';'))
    $madeProgress = $true
    if ($Memory.NavDigest.ContainsKey($navKey)) {
        $lastSnapshot = $Memory.NavDigest[$navKey]
        if ($lastSnapshot.Context -eq $navContext) {
            $madeProgress = (([int]$lastSnapshot.Distance - [int]$target.distance) -ge $script:PilotNavMinProgressPx)
        }
    }
    if ($madeProgress) {
        $Memory.NavStuckCount[$navKey] = 0
    } else {
        $streak = 1
        if ($Memory.NavStuckCount.ContainsKey($navKey)) { $streak = $Memory.NavStuckCount[$navKey] + 1 }
        $Memory.NavStuckCount[$navKey] = $streak
    }
    $Memory.NavDigest[$navKey] = [pscustomobject]@{ Context = $navContext; Distance = [int]$target.distance }

    if ($Memory.NavStuckCount[$navKey] -ge $script:PilotNavStuckThreshold) {
        Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
            -Category 'no-route' -Trying ('reach ' + $target.label) `
            -Detail (($Memory.NavStuckCount[$navKey] + 1).ToString() + ' attempts at ' + $target.label +
                ' (sidestep A, sidestep B, resume) with no meaningful progress -- last reading ' + $target.distance +
                'px, screen: "' + (([string[]]@($State.screenText)) -join ' | ') + '"')
        $Memory.NavBlocked[$navKey] = [int]$State.day
        $Memory.NavStuckCount[$navKey] = 0
        # Retry NOW with the next reachable candidate rather than spending a whole turn on a target
        # already proven dead -- safe: NavBlocked shrinks the candidate set by exactly one key each
        # time, so this recurses at most $nearbyList.Count deep before Get-PilotBestReachableTarget
        # returns $null and the branch above ends it.
        return (Get-PilotNavigateCommand -State $State -Keyword $Keyword -Memory $Memory)
    }

    # 2026-08-11 real-run finding: a target reported inRange=true (AgentPlaytestBridge's own
    # InRangeReportingPx is 96px, documented as a REPORTING threshold "for the model's benefit
    # only" -- the real gate is WorldInput2D's own tighter Area2D overlap) had its "interact"
    # silently swallowed -- byte-identical whole-state fingerprint, confirmed against the same
    # target across several turns. Closing to a tighter distance than the report threshold before
    # ever pressing interact costs a couple of extra "move" turns per station but a swallowed press
    # marked VisitedSurfaces and never got tried again, which is worse.
    if ($target.inRange -and $target.distance -le $script:PilotInteractDistancePx) {
        $Memory.VisitedSurfaces[[string]$target.key] = $true
        return (Build-PilotCommandJson -Action 'key' -Target 'interact' -Why ('pilot: entering ' + $target.label))
    }

    # fix/the-pilot-goes-around: stuck count 1 or 2 -> sidestep (a person who walks into a wall does
    # not keep pressing the same direction); stuck count 0 (fresh, or just made real progress) or 3
    # (both sidestep signs already tried with no change -- re-approach once more before the threshold
    # check above gives up) -> the plain bearing. See $script:PilotNavStuckThreshold's own doc for the
    # full 4-stage shape this produces (sidestep A, sidestep B, resume, then give up).
    $stuckNow = $Memory.NavStuckCount[$navKey]
    if ($stuckNow -eq 1 -or $stuckNow -eq 2) {
        $sidestep = Get-PilotSidestepDirection -Direction ([string]$target.direction) -Attempt $stuckNow
        if ($sidestep) {
            return (Build-PilotCommandJson -Action 'move' -Dir $sidestep -Frames $script:PilotMoveFrames `
                -Why ('pilot: ' + $target.label + ' is not budging straight-on (' + $target.distance + 'px ' +
                    $target.direction + ') -- sidestepping ' + $sidestep + ' around it'))
        }
    }

    return (Build-PilotCommandJson -Action 'move' -Dir $target.direction -Frames $script:PilotMoveFrames `
        -Why ('pilot: walking to ' + $target.label + ' (' + $target.distance + 'px ' + $target.direction + ')'))
}

# Reach a WORKING STATION inside a specific building -- one level deeper than
# Get-PilotNavigateCommand's own building-keyword search. Real-run findings folded in here
# (2026-08-11, against the live client, not a stub):
#
#   1. A room's own stations are named after their FUNCTION, never the building (WorkshopVocab.cs's
#      own table -- the forge room holds "Anvil"/"Bellows"/"Furnace"/"Material Shelf", none of which
#      contain the substring "forge"), so re-using the building's own keyword once already standing
#      inside it can never match. Already inside the target interior -> the caller's own
#      $StationKeyword (may be empty -- nearest REACHABLE station here IS progress; a station with
#      Action:null, like the forge's own "Quench Trough", is real, if wasted, human behavior --
#      poking the wrong thing once is not a bug here). See finding 5 below for why an empty
#      $StationKeyword is no longer always the right default.
#
#   2. Interacting with a station OPENS A DRAWER on top of the room (location becomes
#      "panel:<PanelId>", MainUi's own priority rule -- a drawer panel outranks the room underneath
#      it), and that drawer does NOT close itself. "key:cancel" DOES close a DRAWER (ModalEscape.
#      TryClose, the same mechanism Commissions/Legends already use via their own named Close
#      buttons). So: "panel:$PanelId" (we are exactly where this call wanted) returns null so the
#      caller's own controls check (WorkForge_/Stock_/BuyMat_/etc.) takes over; any OTHER "panel:*"
#      closes via cancel before anything else is tried.
#
#   3. Inside a DIFFERENT walkable interior (no drawer) -> falls through to the ordinary
#      keyword search below, which is what actually fixes this case: $BuildingKeyword (e.g. "forge")
#      will not match ANY of the wrong room's own station labels, and Get-PilotNavigateCommand's own
#      keyword-miss-while-interior branch already answers a miss with a real "key: cancel" -- FIXED
#      2026-08-11 (fix/pilot-finds-its-way): earlier builds of this file believed cancel was dead
#      here (a grep for the "cancel" ACTION found only WorldInput2D's own unused CancelRequested
#      event, missing MainUi's separate raw-Escape-key Input handler entirely) and treated this as an
#      unfixable harness-limit, logging it once and returning null forever after. It was not a harness
#      limit -- AgentPlaytestBridge.ApplyKey drove "cancel" via Input.ActionPress/ActionRelease, which
#      Godot's own docs say never calls a node's _Input at all ("If you want to simulate _input, use
#      Input.ParseInputEvent instead"), so MainUi's Escape ladder (which DOES exit the room, per
#      InteriorEntryExitTests.Escape_WithNoDrawerOpen_ExitsTheRoom) never saw it. ApplyKey now
#      dispatches a real InputEventKey the same way HumanPlayer.PushKey already does for every engine
#      test in this suite (AgentPlaytestBridgeTests.KeyCancel_InsideAWalkableRoom_ExitsIt pins it) --
#      cancel reaches the room's real exit path now, so this no longer needs (or gets) a bespoke
#      dead-end branch of its own. SUPERSEDED same day: that fix was Viewport.PushInput, which turned
#      out to be only half a real key press -- see AgentPlaytest.ApplyKey's own doc for the second
#      half (Input.ParseInputEvent). Cancel still worked either way; "interact" against a town
#      building's door did not, until the second fix (this is what finally let a live pilot walk
#      into the forge at all, below).
#
#   4. Outdoors (location "town") -> the ordinary building-keyword search.
#
#   5. STATION-BLIND once inside (found 2026-08-11, the SAME live run that finally got past finding
#      3): an empty $StationKeyword lands on whatever station is nearest the room's own entry point,
#      not whatever the CALLER actually needs -- and ForgePanel.FocusSection (station-split plan,
#      2026-08) hides the OTHER half of the panel entirely depending on which station opened it
#      ("anvil"/"bellows" -> Focus:"craft", craft cards only, vendor rows hidden; "furnace"/"shelf" ->
#      Focus:"materials", vendor rows only, craft cards hidden). A 220-turn live run entered
#      interior:forge cleanly (finding 3's fix working) and then interacted with "Anvil" 43 times
#      across 9 in-game days -- Anvil happened to be nearest the door -- and NEVER once saw a BuyMat_
#      row, because Anvil's craft-only view hides it by design. Buying material and working the forge
#      are two DIFFERENT stations' jobs now, so the caller must say which one it wants:
#      $StationKeyword 'shelf' (Material Shelf, Focus:"materials") for Morning's buy decision,
#      'anvil' (Focus:"craft") for Expedition's craft decision -- see both call sites below.
#
#   6. QUICK TRAVEL (fix/the-pilot-goes-around): the game itself ships an on-screen shortcut for
#      exactly this problem -- TutorialFlow's own QuickTravelRow (buttons named "QuickTravel_Forge"/
#      "QuickTravel_Shop"/...) jumps straight into a building's interior (MainUi.QuickTravel ->
#      OnTownBuildingClicked, the SAME destination a walked arrival reaches), unlocked once the
#      tutorial chain completes (TutorialFlow.QuickTravelUnlocked). A human who has already unlocked
#      a venue-jump row uses it instead of walking across town every single morning -- that is not
#      this harness inventing a shortcut, it is the designed affordance the game puts on screen for a
#      player to click. Tried ONLY when outdoors ($location -eq 'town'): <see>Town2D.EnterInterior</see>
#      is a no-op while ALREADY inside a different room (InteriorActive stays true, guard returns
#      immediately), so pressing it from inside the wrong building would silently do nothing -- the
#      existing "different interior" keyword-miss/cancel path below already gets the pilot back
#      outdoors first, and quick travel is offered again the very next call from there.
function Get-PilotEnterInteriorCommand {
    param(
        $State,
        [Parameter(Mandatory)][string]$InteriorPrefix,
        [Parameter(Mandatory)][string]$BuildingKeyword,
        [string]$PanelId = '',
        [string]$StationKeyword = '',
        [string]$QuickTravelBuilding = '',
        [Parameter(Mandatory)]$Memory
    )

    $location = [string]$State.location

    if ($PanelId -and $location -eq ('panel:' + $PanelId)) {
        return $null # arrived -- let the caller's own control checks (WorkForge_/Stock_/BuyMat_/...) run
    }
    if ($location.StartsWith('panel:')) {
        # CORRECTED 2026-08-11 (fix/pilot-finds-its-way): this comment used to say "key:cancel does
        # NOT close a Drawer-hosted panel -- confirmed live... DrawerHost never wires Escape to
        # Close() at all". That was true only of the OLD, broken AgentPlaytestBridge.ApplyKey (see
        # Get-PilotNavigateCommand's own doc) -- DrawerHost._Input (godot/scripts/ui/DrawerHost.cs)
        # DOES close on a real Escape key event, it just never reached one before ApplyKey's fix.
        # Pressing the visible "Close" button (UiKit.DrawerHeader's own generic `Name = "Close"`,
        # shared across every Drawer-hosted panel -- Forge/Shop/Tavern/...) is kept anyway: it is the
        # more human-plausible action (a person reads the button, not the keyboard shortcut) and
        # needs no change now that either path would work.
        $close = Get-PilotEnabledControls -State $State -Pattern '^Close$'
        if ($close.Count -gt 0) {
            return (Build-PilotCommandJson -Action 'press' -Target 'Close' -Why ('pilot: closing an unrelated panel (' + $location + ') to get to ' + $BuildingKeyword))
        }
        # No visible/enabled "Close" button this turn (CommissionBoard/LegendsWall use their own
        # panel-specific names -- CommissionClose/LegendsWallClose -- not this generic one) -- cancel
        # is a real, working escape now (see above), so use it rather than spinning on a guess.
        return (Build-PilotCommandJson -Action 'key' -Target 'cancel' -Why ('pilot: closing an unrelated panel (' + $location + ') to get to ' + $BuildingKeyword))
    }
    if ($location.StartsWith($InteriorPrefix)) {
        return (Get-PilotNavigateCommand -State $State -Keyword $StationKeyword -Memory $Memory)
    }

    # Finding 7 (fix/the-pilot-goes-around, real-run finding, seed 1): NOT already inside, and today's
    # attempt on THIS station already gave up (Get-PilotNavigateCommand's own StationGivenUpToday,
    # set the moment every reachable candidate for this keyword proved stuck). Re-entering the
    # building now -- walking OR QuickTravel-ing in -- can only end in the exact same blacklisted
    # station and an immediate cancel back out. QuickTravel made this MUCH worse than it already was:
    # entering used to cost a long walk (which incidentally rate-limited how often the round trip
    # could repeat); QuickTravel makes it free, and a live run spent turns 15-220 of a 220-turn budget
    # ping-ponging QuickTravel_Forge <-> cancel, never once advancing past day 1, because nothing
    # remembered "shelf" was already given up on for today. A person who failed to reach the shelf
    # does not walk straight back in five seconds later hoping it changed -- they leave it for
    # tomorrow and get on with something else. Returning null here (never a command) lets the
    # caller's own next task run instead.
    if ($StationKeyword -and $Memory.StationGivenUpToday.ContainsKey($StationKeyword) -and
        [int]$Memory.StationGivenUpToday[$StationKeyword] -eq [int]$State.day) {
        return $null
    }

    # Finding 6 above: outdoors, with a quick-travel target named, prefer the on-screen shortcut over
    # walking -- checked BEFORE the ordinary keyword search, never after (a human who knows the
    # shortcut does not walk the long way first and only jump on failure).
    if ($QuickTravelBuilding -and $location -eq 'town') {
        $quickTravel = Get-PilotEnabledControls -State $State -Pattern ('^QuickTravel_' + [regex]::Escape($QuickTravelBuilding) + '$')
        if ($quickTravel.Count -gt 0) {
            return (Build-PilotCommandJson -Action 'press' -Target $quickTravel[0].name `
                -Why ('pilot: quick-travel to ' + $QuickTravelBuilding + ' instead of walking the town'))
        }
    }

    # Inside a DIFFERENT walkable interior (finding 3 above), OR outdoors with quick travel unavailable
    # (finding 4): both fall through to the ordinary keyword search. Inside the wrong room,
    # $BuildingKeyword cannot match any of ITS stations, so Get-PilotNavigateCommand's own
    # keyword-miss-while-interior branch answers with a real "key: cancel" -- no bespoke dead-end
    # branch needed here any more (see this function's own doc, finding 3, for what used to live here
    # and why it was wrong).
    return (Get-PilotNavigateCommand -State $State -Keyword $BuildingKeyword -Memory $Memory)
}

# Any nearby surface this run has never visited (VisitedSurfaces keyed by the stable `key` field, not
# distance -- distance changes every step, key does not). Used only for the day<=2 curiosity gate.
function Get-PilotUnvisitedNearby {
    param($State, [Parameter(Mandatory)]$Memory)
    return @($State.nearby) | Where-Object { $_.key -and -not $Memory.VisitedSurfaces.ContainsKey([string]$_.key) } | Select-Object -First 1
}

# --- The forge minigame loop -------------------------------------------------------------------------
# Reads ForgeMinigame's/QuenchMinigame's own readout Label text (see this file's header) -- never
# internal gauge values. Returns $null when the screen shows neither readout (caller is not looking
# at an open forge/quench overlay right now).
function Get-PilotForgeMinigameCommand {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][System.Random]$Random)

    $screen = [string](@($State.screenText) -join ' | ')

    $act1 = [regex]::Match($screen, 'Strike\s+(\d+)/(\d+).*?Heat\s+(\d+).*?(pumping|idle)')
    if ($act1.Success) {
        $heat = [int]$act1.Groups[3].Value
        $pumping = ($act1.Groups[4].Value -eq 'pumping')
        # Per-run jitter (seeded, not per-call) would need extra state to stay stable across a whole
        # craft attempt; a fixed skilled-but-not-perfect threshold pair is honest enough here -- the
        # in-process engine test is where a true per-run Skill curve (ForgePlayer.Skill) belongs.
        $pumpUntil = 780
        $strikeAbove = 340
        if ($pumping) {
            if ($heat -ge $pumpUntil) {
                return (Build-PilotCommandJson -Action 'key' -Target 'bellows' -Why ('pilot: forge heat ' + $heat + ' is hot enough, stop pumping'))
            }
            # Waiting here is a WAIT, not a key. Two earlier versions of this line each sent a key
            # believed to be inert and each was wrong:
            #   'forge_strike' -- was a real no-op while pumping, until the owner ruled STRIKE
            #     IMPLIES RELEASE and the early-return went away. It then stopped the pump and
            #     landed a strike on a lukewarm billet every turn, which reads as a balance finding
            #     rather than a driver bug.
            #   'plunge' -- shares its physical key with 'forge_strike' (both bind Space;
            #     MinigameInput.cs:45,47), and IsActionPressed matches the KEY, not the action name
            #     the sender resolved it from.
            # 'confirm' was correct at the time, by the coincidence that it shares no key with
            # forge_strike or bellows -- a property of the current binding table, not of the intent,
            # and revoked silently by the next binding change. The bridge's 'wait' verb presses
            # nothing at all, so it cannot be wrong about what a key does.
            return (Build-PilotCommandJson -Action 'wait' -Frames 6 -Why ('pilot: forge still pumping, waiting out heat ' + $heat))
        }
        if ($heat -lt $strikeAbove) {
            return (Build-PilotCommandJson -Action 'key' -Target 'bellows' -Why ('pilot: forge heat ' + $heat + ' too low, start pumping'))
        }
        return (Build-PilotCommandJson -Action 'key' -Target 'forge_strike' -Why ('pilot: forge heat ' + $heat + ' good, strike'))
    }

    $act2 = [regex]::Match($screen, 'Heat\s+(\d+)\s*\(target\s+(\d+)')
    if ($act2.Success) {
        if ($screen.Contains('PLUNGE NOW')) {
            return (Build-PilotCommandJson -Action 'key' -Target 'plunge' -Why 'pilot: quench reads PLUNGE NOW')
        }
        # Register #168, and it was NOT harmless. This sent 'forge_strike' on the reasoning that
        # QuenchMinigame._GuiInput only reads 'plunge' -- true, and irrelevant: 'plunge' binds
        # Space/Enter/KpEnter, 'forge_strike' binds Space, and IsActionPressed matches the incoming
        # KEY against the action's own bound set. Every "waiting" turn was therefore a plunge, and
        # the quench never once waited for its band -- so Act 2's whole recorded grade distribution
        # was the distribution of plunging immediately. The 'wait' verb spends the same real frames
        # against the quench's real-time heat-fall clock and presses nothing. Never 'cancel', which
        # would forfeit the craft (Act 2's own Cancel()).
        return (Build-PilotCommandJson -Action 'wait' -Frames 6 -Why 'pilot: quench not in band yet, waiting')
    }

    return $null
}

# --- Phase policies ------------------------------------------------------------------------------

function Get-PilotMorningCommand {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][System.Random]$Random)

    $accept = Get-PilotUnactedControls -Memory $Memory -Kind 'commission' `
        -Candidates (Get-PilotEnabledControls -State $State -Pattern '^CommissionAccept_')
    $decline = Get-PilotUnactedControls -Memory $Memory -Kind 'commission' `
        -Candidates (Get-PilotEnabledControls -State $State -Pattern '^CommissionDecline_')
    if ($accept.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotAcceptCommissionChance) {
            $Memory.ActedOn['commission-' + (Get-PilotControlId $accept[0].name)] = $true
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'answer the commission' -Choice 'accept' `
                -Why ('accepted ' + $accept[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $accept[0].name -Why 'pilot: accept the commission')
        }
        if ($decline.Count -gt 0) {
            $Memory.ActedOn['commission-' + (Get-PilotControlId $decline[0].name)] = $true
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'answer the commission' -Choice 'decline' `
                -Why ('declined ' + $decline[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $decline[0].name -Why 'pilot: decline the commission')
        }
    }

    # Every commission on the board this morning has now been decided once (or there were none to
    # begin with) -- close the board rather than let a still-enabled Accept/Decline (2026-08-11
    # real-run finding: neither disables nor disappears after being pressed -- a dead-verb candidate
    # against a live client) pull the policy into pressing the same row forever.
    $closeCommissions = Get-PilotEnabledControls -State $State -Pattern '^CommissionClose$'
    if ($closeCommissions.Count -gt 0) {
        return (Build-PilotCommandJson -Action 'press' -Target 'CommissionClose' -Why 'pilot: done with the commission board, closing it')
    }

    $stock = Get-PilotEnabledControls -State $State -Pattern '^Stock_'
    if ($stock.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotStockChance) {
            if (-not $Memory.PricingFrictionLogged) {
                $Memory.PricingFrictionLogged = $true
                Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
                    -Category 'harness-limit' -Trying 'price this item for the sale, not just accept the suggestion' `
                    -Detail ('StockPrice_* is a SpinBox, not a Button -- this harness''s press/move/key/advance ' +
                        'vocabulary has no way to change it, so every Stock_ press sells at the auto-suggested ' +
                        'price. A real player could drag or scroll-wheel it. See pilot.ps1''s own header note.')
            }
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'sell the good one or hold it' -Choice 'sell' `
                -Why ('stocked ' + $stock[0].name + ' at the suggested price (see pricing friction note)')
            return (Build-PilotCommandJson -Action 'press' -Target $stock[0].name -Why 'pilot: stock the crafted item')
        }
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'sell the good one or hold it' -Choice 'hold' `
            -Why ('left ' + $stock[0].name + ' off the shelf this morning')
    }

    $openCommissions = Get-PilotEnabledControls -State $State -Pattern '^OpenCommissions$'
    if ($openCommissions.Count -gt 0 -and -not $Memory.VisitedSurfaces.ContainsKey('OpenCommissions-' + $State.day)) {
        $Memory.VisitedSurfaces['OpenCommissions-' + $State.day] = $true
        return (Build-PilotCommandJson -Action 'press' -Target 'OpenCommissions' -Why 'pilot: check the commission board')
    }

    # fix/pilot-finds-its-way: BuyMaterialAction/BuyForgeSupplyAction are Morning-only in the sim
    # (MaterialVendorHandlers.cs:46, ForgeSupplyHandlers.cs:45 -- both CanHandle gate on
    # DayPhase.Morning), but this decision used to live in Get-PilotEveningCommand, where BuyMat_/
    # BuySupply_ can NEVER be enabled -- confirmed with DeepPilotPlayTests (godot/tests): moving this
    # block to Evening's OWN phase check produced ten straight days of "no BuyMat_ button" with
    # materials never rising off zero. "Buy the ore or buy the goodwill" (CLAUDE.md's own decision 5)
    # was therefore never actually decidable by any live pilot run before this fix -- the button was
    # dead on arrival every single evening, every single day, forever.
    $buy = Get-PilotEnabledControls -State $State -Pattern '^BuyMat_'
    $buySupply = Get-PilotEnabledControls -State $State -Pattern '^BuySupply_'
    $buyable = $buy + $buySupply
    if ($buyable.Count -gt 0) {
        # Seen at least one buyable row -- the Forge vendor is in front of us, so this morning's
        # material decision is resolvable here. Marking it resolved (bought OR conserved, either is a
        # real decision) is what lets the nav step below move on to the market instead of camping the
        # forge for the rest of the morning.
        $Memory.VisitedSurfaces['MorningMaterialResolved-' + $State.day] = $true
        if (Test-PilotChance -Random $Random -Probability $script:PilotBuyMaterialChance) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'buy the ore or buy the goodwill' -Choice 'buy ore' `
                -Why ('bought via ' + $buyable[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $buyable[0].name -Why 'pilot: buy material')
        }
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'buy the ore or buy the goodwill' -Choice 'conserve gold' `
            -Why 'held gold back instead of buying material'
    }

    # Visit the Forge FIRST each morning (to resolve the material decision above) before heading to
    # the market -- only one building can be walked toward per turn, and once
    # Get-PilotEnterInteriorCommand reports "arrived" (panel:Forge, or nothing left to do there) it
    # returns null and this falls straight through to the market nav on the SAME turn, so a morning
    # with nothing to buy costs no extra turns over the pre-fix behavior.
    #
    # -StationKeyword 'shelf' (finding 5, Get-PilotEnterInteriorCommand's own doc): Material Shelf is
    # the ONLY station whose FocusSection view shows BuyMat_ rows. Anvil (Focus:"craft") is nearer the
    # door and hides them entirely -- a live run proved this by walking straight into Anvil 43 times
    # across 9 days and never once seeing a material to buy.
    if (-not $Memory.VisitedSurfaces.ContainsKey('MorningMaterialResolved-' + $State.day)) {
        $navForge = Get-PilotEnterInteriorCommand -State $State -InteriorPrefix 'interior:forge' -BuildingKeyword 'forge' -PanelId 'Forge' -StationKeyword 'shelf' -QuickTravelBuilding 'Forge' -Memory $Memory
        if ($navForge) { return $navForge }
    }

    # Venue key is "market" (InteriorLayout2D.cs: "market" or "Shop" => "market"), NOT "shop" --
    # the building's own on-screen label is still "Shop", which is what a keyword search outdoors
    # needs to match. QuickTravelBuilding stays "Shop" though -- QuickTravelVenues/OnTownBuildingClicked
    # both key their BUILDING vocabulary off the legacy capitalized names, not the venue key.
    $nav = Get-PilotEnterInteriorCommand -State $State -InteriorPrefix 'interior:market' -BuildingKeyword 'shop' -PanelId 'Shop' -QuickTravelBuilding 'Shop' -Memory $Memory
    if ($nav) { return $nav }

    return $null
}

function Get-PilotExpeditionCommand {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][System.Random]$Random)

    $forgeCmd = Get-PilotForgeMinigameCommand -State $State -Memory $Memory -Random $Random
    if ($forgeCmd) { return $forgeCmd }

    if ($State.actionSlotsRemaining -le 0) { return $null }

    $work = Get-PilotEnabledControls -State $State -Pattern '^WorkForge_'
    $autoCraft = Get-PilotEnabledControls -State $State -Pattern '^Craft_'
    if ($work.Count -gt 0 -or $autoCraft.Count -gt 0) {
        if (-not (Test-PilotChance -Random $Random -Probability $script:PilotCraftThisWindowChance)) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'spend the slot or bank it' -Choice 'bank' `
                -Why 'skipped an available craft window this expedition'
            return $null
        }
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'spend the slot or bank it' -Choice 'spend' -Why 'took the open craft window'

        if ($work.Count -gt 0 -and (Test-PilotChance -Random $Random -Probability $script:PilotWorkForgeChance)) {
            # Highest-tier/last-listed recipe by default (fills the gap a stronger recipe implies),
            # but the seeded flip below sometimes picks a different enabled one instead -- the
            # fill-vs-upgrade decision is exactly this "which recipe" choice.
            $chosen = $work[$work.Count - 1]
            if ($work.Count -gt 1 -and (Test-PilotChance -Random $Random -Probability 0.25)) {
                $chosen = $work[$Random.Next(0, $work.Count)]
            }
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'fill the empty slot or upgrade the full one' `
                -Choice $chosen.name -Why 'worked the forge (full two-act minigame)'
            return (Build-PilotCommandJson -Action 'press' -Target $chosen.name -Why 'pilot: work the forge')
        }
        if ($autoCraft.Count -gt 0) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'fill the empty slot or upgrade the full one' `
                -Choice $autoCraft[0].name -Why 'took the flat auto-craft instead of working the forge'
            return (Build-PilotCommandJson -Action 'press' -Target $autoCraft[0].name -Why 'pilot: auto-craft (skip the minigame)')
        }
    }

    # -StationKeyword 'anvil' (finding 5, Get-PilotEnterInteriorCommand's own doc): Anvil is the
    # station whose FocusSection view shows WorkForge_/Craft_ -- explicit rather than relying on it
    # already being the nearest-to-the-door default, which is true today but not a contract.
    $nav = Get-PilotEnterInteriorCommand -State $State -InteriorPrefix 'interior:forge' -BuildingKeyword 'forge' -PanelId 'Forge' -StationKeyword 'anvil' -QuickTravelBuilding 'Forge' -Memory $Memory
    if ($nav) { return $nav }

    return $null
}

function Get-PilotCampCommand {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][System.Random]$Random)

    $send = Get-PilotEnabledControls -State $State -Pattern '^CampSend_'
    if ($send.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotSendRunnerChance) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'send the runner or trust their judgment' `
                -Choice 'send' -Why ('sent ' + $send[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $send[0].name -Why 'pilot: send the runner')
        }
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'send the runner or trust their judgment' `
            -Choice 'trust' -Why ('left ' + $send[0].name + ' unsent, trusting the party''s own judgment')
    }
    return $null
}

function Get-PilotEveningCommand {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][System.Random]$Random)

    # Same ActedOn-once-then-close fix as commissions (Get-PilotMorningCommand's own note): decide
    # each honor-eligible hero at most once, then close the wall instead of re-rolling the SAME
    # still-enabled Honor_ button forever.
    $honor = Get-PilotUnactedControls -Memory $Memory -Kind 'honor' `
        -Candidates (Get-PilotEnabledControls -State $State -Pattern '^Honor_')
    if ($honor.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotHonorMemorialChance) {
            $Memory.ActedOn['honor-' + (Get-PilotControlId $honor[0].name)] = $true
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'honor the memorial' -Choice 'honor' `
                -Why ('honored ' + $honor[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $honor[0].name -Why 'pilot: honor the memorial')
        }
        $Memory.ActedOn['honor-' + (Get-PilotControlId $honor[0].name)] = $true
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'honor the memorial' -Choice 'walk past' -Why 'skipped honoring tonight'
        # Falls through (no return) to the rest of this function -- "walk past" decided something,
        # it did not end the turn, and there may be another hero on the wall or material to buy.
    }

    $openLegends = Get-PilotEnabledControls -State $State -Pattern '^OpenLegends$'
    if ($openLegends.Count -gt 0 -and -not $Memory.VisitedSurfaces.ContainsKey('OpenLegends-' + $State.day)) {
        $Memory.VisitedSurfaces['OpenLegends-' + $State.day] = $true
        return (Build-PilotCommandJson -Action 'press' -Target 'OpenLegends' -Why 'pilot: check the legends wall')
    }

    $closeLegends = Get-PilotEnabledControls -State $State -Pattern '^LegendsWallClose$'
    if ($closeLegends.Count -gt 0 -and $Memory.VisitedSurfaces.ContainsKey('OpenLegends-' + $State.day)) {
        return (Build-PilotCommandJson -Action 'press' -Target 'LegendsWallClose' -Why 'pilot: done with the legends wall, closing it')
    }

    # fix/pilot-finds-its-way: the material-buying decision (BuyMat_/BuySupply_) used to live here.
    # Moved to Get-PilotMorningCommand -- BuyMaterialAction/BuyForgeSupplyAction are Morning-only in
    # the sim (MaterialVendorHandlers.cs:46, ForgeSupplyHandlers.cs:45), so those buttons could never
    # once be enabled at this phase; the Forge-navigation call that used to close this function existed
    # ONLY to reach them and has no other purpose now that they are gone (Honor_/OpenLegends are HUD
    # tray + LegendsWall controls, reachable from anywhere -- see this function's own checks above,
    # none of which need a particular building). Falling through to $null lets the caller's own
    # advance-fallback (Get-PilotCommand) end the phase instead of wandering the forge for nothing.
    return $null
}

# The ONE place every command Get-PilotCommand returns actually leaves the function -- owner steer
# (2026-08-11), kinds 1-3: flushes/extends the DEAD STRETCH streak (kind 3) and records what is about
# to be tried plus a snapshot of the screen right now (kinds 1+2, read back next call in Get-
# PilotCommand's own step 1). $IsDeadStretch marks ONLY the "nothing phase-specific available"
# fallback itself -- every other command, including a real one chosen the SAME turn a streak was
# running, ends and logs the streak here.
function Complete-PilotTurn {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][string]$CommandJson, [switch]$IsDeadStretch)

    if ($IsDeadStretch) {
        if (-not $Memory.DeadStretchRun) {
            $Memory.DeadStretchRun = [pscustomobject]@{ StartTurn = $State.turn; StartDay = $State.day; StartPhase = [string]$State.phase; Length = 0 }
        }
        $Memory.DeadStretchRun.Length++
    } elseif ($Memory.DeadStretchRun) {
        if ($Memory.DeadStretchRun.Length -ge $script:PilotDeadStretchThreshold) {
            Add-PilotFriction -Memory $Memory -Turn $Memory.DeadStretchRun.StartTurn -Day $Memory.DeadStretchRun.StartDay -Phase $Memory.DeadStretchRun.StartPhase `
                -Category 'dead-stretch' -Trying 'nothing legal but advance' `
                -Detail ($Memory.DeadStretchRun.Length.ToString() + ' consecutive turns (turn ' + $Memory.DeadStretchRun.StartTurn + ' to turn ' +
                    ($State.turn - 1) + ') with nothing to decide but advance, starting day ' + $Memory.DeadStretchRun.StartDay + ' ' + $Memory.DeadStretchRun.StartPhase)
        }
        $Memory.DeadStretchRun = $null
    }

    $parsed = $CommandJson | ConvertFrom-Json
    $target = if ($parsed.target) { [string]$parsed.target } else { '' }
    $kind = 'generic'
    if ($target -match '^(BuyMat_|BuySupply_)') { $kind = 'material-purchase' }
    $Memory.PendingAction = [pscustomobject]@{
        Action      = [string]$parsed.action
        Target      = $target
        Label       = (Get-PilotControlLabel -State $State -Name $target)
        Why         = [string]$parsed.why
        Kind        = $kind
        PreSnapshot = (Get-PilotFullSnapshot -State $State)
    }
    return $CommandJson
}

# The one entry point agent-playtest.ps1 calls, mirroring Get-MonkeyCommand's own signature shape
# (State, Random) plus the memory this policy needs to carry habit/curiosity/friction across turns.
function Get-PilotCommand {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)]$Memory,
        [Parameter(Mandatory)][System.Random]$Random
    )

    # --- 1. Friction from the LAST turn's outcome, observed now (state.lastOutcome always reports
    # the PRECEDING command's result -- same convention the driver's own $driverTurns uses). ---
    if (([string]$State.lastOutcome).StartsWith('refused:')) {
        Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
            -Category 'refused' -Trying $Memory.PendingIntent -Detail ([string]$State.lastOutcome)
        # Owner steer, kind 4 (UNREADABLE REFUSAL) -- a SEPARATE, filtered view of the same event:
        # would the quoted reason actually tell a human what to do differently.
        if (-not (Test-PilotRefusalReadable -Outcome ([string]$State.lastOutcome))) {
            Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
                -Category 'unreadable-refusal' -Trying $Memory.PendingIntent `
                -Detail ('refused with no player-actionable reason -- verbatim: "' + [string]$State.lastOutcome + '"')
        }
    } elseif ($Memory.PendingAction.Action -eq 'move') {
        # "move" is a SPECIAL case for kind 1: the thing that changes (player pixel position) is not
        # in $State at all -- location/gold/day/phase/beat/canMove/slots/screenText do not depend on
        # WHERE inside a room the player stands, so Get-PilotFullSnapshot reads "identical" for every
        # ordinary step deeper into a big room, which is real progress, not silence. Measured directly
        # (2026-08-11 live run): naively reusing the whole-screen digest here mislabeled 132 of 220
        # turns "no-response-press" while the player was, in fact, slowly closing on Anvil the entire
        # time (a genuinely slow crawl, a DIFFERENT and real finding on its own -- see pilot.ps1's own
        # header/Get-PilotEnterInteriorCommand doc -- but not silence). The move outcome text itself
        # already reports ground truth ("moved DIR Nf -> pos (X,Y) from (X2,Y2)") -- a REAL zero-pixel
        # move (blocked solid in the attempted direction) is what this checks instead.
        $moveMatch = [regex]::Match([string]$State.lastOutcome, 'pos \(([\d.-]+),\s*([\d.-]+)\) from \(([\d.-]+),\s*([\d.-]+)\)')
        if ($moveMatch.Success -and $moveMatch.Groups[1].Value -eq $moveMatch.Groups[3].Value -and $moveMatch.Groups[2].Value -eq $moveMatch.Groups[4].Value) {
            Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
                -Category 'no-response-press' -Trying $Memory.PendingAction.Why `
                -Detail ('move ' + $Memory.PendingAction.Target + ' reported "' + [string]$State.lastOutcome +
                    '" -- position did not change at all, likely blocked solid in that direction')
        }
        $Memory.PendingAction = $null
    } elseif ($Memory.PendingAction) {
        # Owner steer, kinds 1+2 (NO-RESPONSE PRESS / NO ACKNOWLEDGEMENT) -- the outcome CLAIMED
        # success (not a refusal), so compare the whole-screen snapshot taken when that command was
        # chosen against the one $State carries now. Generalizes Get-PilotNavigateCommand's own
        # per-target NavDigest to every verb, not just movement/interact toward a nearby station.
        $postSnapshot = Get-PilotFullSnapshot -State $State
        if ($postSnapshot -eq $Memory.PendingAction.PreSnapshot) {
            Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
                -Category 'no-response-press' -Trying $Memory.PendingAction.Why `
                -Detail ($Memory.PendingAction.Action + ' ' + $Memory.PendingAction.Target + ' ("' + $Memory.PendingAction.Label +
                    '") reported "' + [string]$State.lastOutcome + '" but the screen read identical before and after -- "' +
                    (([string[]]@($State.screenText)) -join ' | ') + '"')
        } elseif ($Memory.PendingAction.Kind -eq 'material-purchase') {
            # Objective, not textual: $State.gold is read off the real sim state every turn, never off
            # prose, so this cannot be fooled by a ticker line that never mentions the purchase.
            $goldMatch = [regex]::Match([string]$State.lastOutcome, 'gold (\d+) -> (\d+)')
            if ($goldMatch.Success -and [int]$goldMatch.Groups[1].Value -eq [int]$goldMatch.Groups[2].Value) {
                Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
                    -Category 'no-acknowledgement' -Trying $Memory.PendingAction.Why `
                    -Detail ('bought via ' + $Memory.PendingAction.Target + ' ("' + $Memory.PendingAction.Label +
                        '") -- the press was not refused and the screen changed, but gold read unchanged (' +
                        $goldMatch.Groups[1].Value + ' -> ' + $goldMatch.Groups[2].Value +
                        ') -- candidate for a queued/delayed apply or a silent purchase, not confirmed either way')
            }
        }
        $Memory.PendingAction = $null
    }

    # --- 2. Idle-stretch detection (same digest shape the driver's own STUCK detector uses). ---
    $digest = ([string]$State.phase + '|' + [string]$State.location + '|' + (([string[]]@($State.screenText)) -join ';'))
    if ($digest -eq $Memory.LastDigest) {
        $Memory.StuckCount++
        if ($Memory.StuckCount -eq $script:PilotStuckFrictionThreshold) {
            Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
                -Category 'idle-stretch' -Trying $Memory.PendingIntent `
                -Detail ('screen unchanged for ' + ($Memory.StuckCount + 1) + ' turns at ' + $State.location + '/' + $State.phase)
        }
    } else {
        $Memory.StuckCount = 0
    }
    $Memory.LastDigest = $digest

    # --- 3. Curiosity gate (day <= 2 only): sometimes look at something unvisited instead of the
    # transactional default, even when a real task is available -- "wonders what is there." ---
    if ([int]$State.day -le $script:PilotCuriosityDayCeiling -and $State.canMove) {
        $unvisited = Get-PilotUnvisitedNearby -State $State -Memory $Memory
        if ($unvisited -and (Test-PilotChance -Random $Random -Probability $script:PilotCuriosityChance)) {
            $Memory.PendingIntent = 'curiosity: look at ' + $unvisited.label
            $nav = Get-PilotNavigateCommand -State $State -Keyword ([string]$unvisited.label).ToLowerInvariant() -Memory $Memory
            if ($nav) { return (Complete-PilotTurn -State $State -Memory $Memory -CommandJson $nav) }
        }
    }

    # --- 3b. World-blocking overlay recovery (real-run finding, seed 4242). canMove false with no
    # phase-specific control in front of us means some code-built modal (Ledger, Forecast, ...) is
    # covering the whole world, and its own close button's programmatic NAME does not follow the
    # "<Name>Close" convention every OTHER modal here happens to use -- LedgerModal.cs names its
    # button "CloseLedger" (Close-PREFIX, not suffix), which the panel-branch's exact "^Close$" match
    # (Get-PilotEnterInteriorCommand) can never see. Measured on a full 220-turn/22-day live run: the
    # Evening Ledger opened once on day 1 and never closed again for the rest of the run -- "advance"
    # kept legally ticking the day forward the whole time (so the run never LOOKED stuck by the
    # idle-stretch/no-progress detectors above), but canMove stayed false for ~20 straight days,
    # silently blocking every Stock_/BuyMat_/WorkForge_ press behind it: 0 of each over the whole run.
    # Matching on the LABEL a person actually reads ("Close") rather than the inconsistent internal
    # name survives whichever naming convention a FUTURE modal happens to pick too.
    if (-not $State.canMove) {
        $closeLabeled = @($State.controls | Where-Object { $_ -and $_.enabled -and $_.label -and ([string]$_.label).Trim() -ieq 'Close' })
        if ($closeLabeled.Count -gt 0) {
            $Memory.PendingIntent = 'pilot: close the overlay blocking the world (' + $closeLabeled[0].name + ')'
            return (Complete-PilotTurn -State $State -Memory $Memory -CommandJson (Build-PilotCommandJson -Action 'press' -Target $closeLabeled[0].name -Why $Memory.PendingIntent))
        }
    }

    # --- 4. Phase policy. ---
    $command = $null
    $phase = [string]$State.phase
    if ($phase -eq 'Morning') { $command = Get-PilotMorningCommand -State $State -Memory $Memory -Random $Random }
    elseif ($phase -eq 'Expedition') { $command = Get-PilotExpeditionCommand -State $State -Memory $Memory -Random $Random }
    elseif ($phase -eq 'Camp') { $command = Get-PilotCampCommand -State $State -Memory $Memory -Random $Random }
    elseif ($phase -eq 'Evening') { $command = Get-PilotEveningCommand -State $State -Memory $Memory -Random $Random }

    # --- 4b. Global stuck circuit breaker. Get-PilotNavigateCommand's own per-target detector (see
    # its doc) catches a bad move/interact toward a KNOWN nearby target, but a phase policy re-pressing
    # a dead verb OUTSIDE navigation (a button that looks legal but the kernel silently rejects, a
    # panel that never closes) never goes through that function at all -- this is the general,
    # last-resort answer to the same requirement, never repeat the same no-op indefinitely, for
    # whatever the per-target detector cannot see. Once the WHOLE turn (phase, location, screen) has
    # read identical for $script:PilotGlobalStuckForceAdvance turns running -- double the threshold
    # that already logged ONE friction entry above -- asking the same phase policy for what will
    # provably be the same answer stops being useful; force real progress instead. advance is legal in
    # every phase (act.md's own rule 2) so this branch itself can never be refused.
    if ($Memory.StuckCount -ge $script:PilotGlobalStuckForceAdvance) {
        Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $State.phase `
            -Category 'idle-stretch' -Trying $Memory.PendingIntent `
            -Detail ('forced advance after ' + ($Memory.StuckCount + 1) + ' identical turns at ' + $State.location + '/' + $State.phase +
                ' -- screen stayed: "' + (([string[]]@($State.screenText)) -join ' | ') + '"')
        $Memory.StuckCount = 0
        $Memory.PendingIntent = 'forced advance (global stuck ceiling)'
        return (Complete-PilotTurn -State $State -Memory $Memory -CommandJson (Build-PilotCommandJson -Action 'advance' -Why 'pilot: stuck too long, forcing the day forward'))
    }

    # --- 5. Habit tracking: which action-prefix did this phase resolve to this time. ---
    $chosenPrefix = 'advance'
    if ($command) {
        $parsed = $command | ConvertFrom-Json
        $chosenPrefix = [string]$parsed.action
        if ($parsed.target) { $chosenPrefix = $chosenPrefix + ':' + ([string]$parsed.target -replace '_\d+$', '_N') }
    }
    if ([int]$State.day -ge $script:PilotHabitDayFloor) {
        $previous = $Memory.RoutinePhaseChoice[$phase]
        if ($previous -eq $chosenPrefix) {
            $streak = 1
            if ($Memory.RoutineStreak.ContainsKey($phase)) { $streak = $Memory.RoutineStreak[$phase] + 1 }
            $Memory.RoutineStreak[$phase] = $streak
            if ($streak -ge $script:PilotHabitStreakToLock) { $Memory.HabitLocked[$phase] = $true }
        } else {
            if ($Memory.HabitLocked.ContainsKey($phase) -and $Memory.HabitLocked[$phase]) {
                Add-PilotFriction -Memory $Memory -Turn $State.turn -Day $State.day -Phase $phase `
                    -Category 'rhythm-deviation' -Trying $chosenPrefix `
                    -Detail ('routine for ' + $phase + ' had locked on "' + $previous + '" but this turn chose "' + $chosenPrefix + '" instead')
            }
            $Memory.RoutineStreak[$phase] = 1
            $Memory.HabitLocked[$phase] = $false
        }
        $Memory.RoutinePhaseChoice[$phase] = $chosenPrefix
    }

    if (-not $command) {
        # Nothing phase-specific was available. Try one more unvisited curiosity target regardless
        # of day (a settled-routine run should still notice a genuinely new surface), else advance --
        # always legal, per act.md's own rule 2 ("if everything useful is disabled... advance").
        $unvisitedAnyDay = Get-PilotUnvisitedNearby -State $State -Memory $Memory
        if ($unvisitedAnyDay) {
            $nav = Get-PilotNavigateCommand -State $State -Keyword ([string]$unvisitedAnyDay.label).ToLowerInvariant() -Memory $Memory
            if ($nav) {
                $Memory.PendingIntent = 'explore ' + $unvisitedAnyDay.label
                return (Complete-PilotTurn -State $State -Memory $Memory -CommandJson $nav)
            }
        }
        $Memory.PendingIntent = 'advance the day (nothing else legal/available)'
        return (Complete-PilotTurn -State $State -Memory $Memory -IsDeadStretch -CommandJson (Build-PilotCommandJson -Action 'advance' -Why 'pilot: nothing phase-specific available, advancing'))
    }

    $parsedFinal = $command | ConvertFrom-Json
    $Memory.PendingIntent = [string]$parsedFinal.why
    return (Complete-PilotTurn -State $State -Memory $Memory -CommandJson $command)
}
