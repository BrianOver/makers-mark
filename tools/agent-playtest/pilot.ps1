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
$script:PilotMoveFrames               = 30

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
        FrictionLog        = New-Object System.Collections.ArrayList
        SixDecisions       = New-Object System.Collections.ArrayList
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
    $obj.why = $Why
    return (([pscustomobject]$obj) | ConvertTo-Json -Compress)
}

function Get-PilotEnabledControls {
    param($State, [Parameter(Mandatory)][string]$Pattern)
    return ,@($State.controls | Where-Object { $_ -and $_.enabled -and ([string]$_.name -match $Pattern) })
}

# Walk toward (then enter/interact with) the nearest $State.nearby entry whose label contains
# $Keyword, or the plain nearest one if nothing matches -- the same "Around you" reading a model is
# taught in act.md rule 8, just applied by regex instead of by an LLM reading prose. Returns $null
# when there is nothing to navigate toward at all (cannot move, or nothing nearby), so the caller can
# fall through to its own next choice instead of forcing a bad command.
function Get-PilotNavigateCommand {
    param($State, [string]$Keyword, [Parameter(Mandatory)]$Memory)

    if (-not $State.canMove) { return $null }
    $nearbyList = @($State.nearby)
    if ($nearbyList.Count -eq 0) { return $null }

    $target = $null
    if ($Keyword) {
        $target = $nearbyList | Where-Object { $_.label -and ([string]$_.label).ToLowerInvariant().Contains($Keyword) } | Select-Object -First 1
    }
    if (-not $target) { $target = $nearbyList[0] }

    if ($target.inRange) {
        $Memory.VisitedSurfaces[[string]$target.key] = $true
        return (Build-PilotCommandJson -Action 'key' -Target 'interact' -Why ('pilot: entering ' + $target.label))
    }
    return (Build-PilotCommandJson -Action 'move' -Dir $target.direction -Frames $script:PilotMoveFrames `
        -Why ('pilot: walking to ' + $target.label + ' (' + $target.distance + 'px ' + $target.direction + ')'))
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
            return (Build-PilotCommandJson -Action 'key' -Target 'forge_strike' -Why ('pilot: forge still pumping, waiting out heat ' + $heat))
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
        # QuenchMinigame does not bind forge_strike at all (its _GuiInput only checks plunge/Escape),
        # so this is a harmless real key-press that spends a few real frames while the quench's own
        # real-time heat-fall clock keeps running between file-channel turns -- never "cancel", which
        # would forfeit the whole craft (Act 2's own Cancel()).
        return (Build-PilotCommandJson -Action 'key' -Target 'forge_strike' -Why 'pilot: quench not in band yet, waiting')
    }

    return $null
}

# --- Phase policies ------------------------------------------------------------------------------

function Get-PilotMorningCommand {
    param($State, [Parameter(Mandatory)]$Memory, [Parameter(Mandatory)][System.Random]$Random)

    $accept = Get-PilotEnabledControls -State $State -Pattern '^CommissionAccept_'
    $decline = Get-PilotEnabledControls -State $State -Pattern '^CommissionDecline_'
    if ($accept.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotAcceptCommissionChance) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'answer the commission' -Choice 'accept' `
                -Why ('accepted ' + $accept[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $accept[0].name -Why 'pilot: accept the commission')
        }
        if ($decline.Count -gt 0) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'answer the commission' -Choice 'decline' `
                -Why ('declined ' + $decline[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $decline[0].name -Why 'pilot: decline the commission')
        }
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

    $nav = Get-PilotNavigateCommand -State $State -Keyword 'shop' -Memory $Memory
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

    $nav = Get-PilotNavigateCommand -State $State -Keyword 'forge' -Memory $Memory
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

    $honor = Get-PilotEnabledControls -State $State -Pattern '^Honor_'
    if ($honor.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotHonorMemorialChance) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'honor the memorial' -Choice 'honor' `
                -Why ('honored ' + $honor[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $honor[0].name -Why 'pilot: honor the memorial')
        }
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'honor the memorial' -Choice 'walk past' -Why 'skipped honoring tonight'
    }

    $openLegends = Get-PilotEnabledControls -State $State -Pattern '^OpenLegends$'
    if ($openLegends.Count -gt 0 -and -not $Memory.VisitedSurfaces.ContainsKey('OpenLegends-' + $State.day)) {
        $Memory.VisitedSurfaces['OpenLegends-' + $State.day] = $true
        return (Build-PilotCommandJson -Action 'press' -Target 'OpenLegends' -Why 'pilot: check the legends wall')
    }

    $buy = Get-PilotEnabledControls -State $State -Pattern '^BuyMat_'
    $buySupply = Get-PilotEnabledControls -State $State -Pattern '^BuySupply_'
    $buyable = $buy + $buySupply
    if ($buyable.Count -gt 0) {
        if (Test-PilotChance -Random $Random -Probability $script:PilotBuyMaterialChance) {
            Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'buy the ore or buy the goodwill' -Choice 'buy ore' `
                -Why ('bought via ' + $buyable[0].name)
            return (Build-PilotCommandJson -Action 'press' -Target $buyable[0].name -Why 'pilot: buy material')
        }
        Add-PilotDecision -Memory $Memory -Day $State.day -Decision 'buy the ore or buy the goodwill' -Choice 'conserve gold' `
            -Why 'held gold back instead of buying material'
    }

    $nav = Get-PilotNavigateCommand -State $State -Keyword 'forge' -Memory $Memory
    if ($nav) { return $nav }

    return $null
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
            if ($nav) { return $nav }
        }
    }

    # --- 4. Phase policy. ---
    $command = $null
    $phase = [string]$State.phase
    if ($phase -eq 'Morning') { $command = Get-PilotMorningCommand -State $State -Memory $Memory -Random $Random }
    elseif ($phase -eq 'Expedition') { $command = Get-PilotExpeditionCommand -State $State -Memory $Memory -Random $Random }
    elseif ($phase -eq 'Camp') { $command = Get-PilotCampCommand -State $State -Memory $Memory -Random $Random }
    elseif ($phase -eq 'Evening') { $command = Get-PilotEveningCommand -State $State -Memory $Memory -Random $Random }

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
                return $nav
            }
        }
        $Memory.PendingIntent = 'advance the day (nothing else legal/available)'
        return (Build-PilotCommandJson -Action 'advance' -Why 'pilot: nothing phase-specific available, advancing')
    }

    $parsedFinal = $command | ConvertFrom-Json
    $Memory.PendingIntent = [string]$parsedFinal.why
    return $command
}
