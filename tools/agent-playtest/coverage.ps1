<#
.SYNOPSIS
    Pure logic for U3 (playtest-harness wave): "everything" gets a denominator.

.DESCRIPTION
    No run before this could say what it did NOT reach, so a findings.md claiming broad coverage was
    unfalsifiable -- there was no list of surfaces to check it against. This file derives that list
    FROM THE GAME'S OWN CODE (never hand-typed -- a hand-listed set is a known defect shape in this
    repo: it silently reads as complete and rots the moment a new panel/building/phase is added and
    nobody remembers to add it here too) and tracks, turn by turn, what a run actually touched.

    Registries and their source of truth, one function each below:
      - panel ids           <- MainUi.cs's Drawer.Register("Id", PanelInstance) calls
      - overlay ids         <- MainUi.cs's OverlaySurfaces() named list (Ledger/Camp/Mirror/Forecast/
                                Bestiary/Commissions/Legends/the system menu -- FullRect overlays that
                                deliberately bypass the drawer, so Get-PanelIdRegistry above is
                                structurally blind to them; see this file's own Get-OverlayRegistry)
      - town buildings      <- TownLayout2D.Venues (the outdoor building table)
      - interior stations   <- InteriorLayout2D.Rooms (market/tavern/minegate) UNION
                                WorkshopVocab.ByProfession (forge -- see its own caveat below)
      - DayPhase values     <- sim/GameSim/Contracts/Enums.cs's enum DayPhase
      - action types        <- AgentPlaytest.cs's AgentPlaytestBridge.Apply() switch

    HUD control names have NO central registry in this codebase -- button Name is set ad hoc, per
    file, as an object-initializer property or a helper call. Get-HudControlRegistry is a best-effort
    regex harvest (two dominant patterns observed across godot/scripts), not a formal source of truth,
    and says so in its own caveat rather than presenting a guess as ground truth.

    ARRAY-RETURN NOTE: every registry function below returns via `,@(...)` (a leading comma), not
    bare `@(...)`. Measured directly while building this file: a PowerShell function that does
    `return @($x)` has its array UNWRAPPED to a bare scalar by the pipeline when $x has exactly one
    element (`$caller = Get-Foo` then silently gets the single item, not a 1-element array), and a
    bare `return @()` with zero elements assigns $null, not an empty array. Both bit
    Get-BackendRejections during U2's own build (see backend.ps1's matching note) before the comma
    fix; every registry here uses it from the start so a denominator that happens to have exactly one
    or zero entries does not silently break `.Count` for every caller.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# --- Registries: derived from the real C# source, not hand-typed ---------------------------------

# Panel ids: MainUi.cs's own registration call, the SAME strings Location() emits as "panel:<id>" in
# StateDigest and the same ones OpenPanel(id) routes on -- one source, not a re-typed mirror of it.
function Get-PanelIdRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Join-Path $RepoRoot 'godot\scripts\MainUi.cs'
    if (-not (Test-Path $path)) { return ,@() }
    $text = Get-Content $path -Raw

    $ids = @()
    foreach ($m in [regex]::Matches($text, 'Drawer\.Register\(\s*"([A-Za-z0-9]+)"')) {
        $ids += $m.Groups[1].Value
    }
    return ,@($ids | Select-Object -Unique | Sort-Object)
}

# Overlay ids: MainUi.cs's own OverlaySurfaces() named list -- the SAME list AnOverlayOwnsTheScreen()
# and ActiveOverlayName() both fold over, so Location() (AgentPlaytest.cs) reports one of these names
# exactly when Get-PanelIdRegistry above could never see it. These are FullRect overlays (Ledger,
# Camp, the Scrying Mirror, the raid Forecast board, the Bestiary, the Commission board, the Legends
# wall, and the system menu) that deliberately bypass Drawer.Register -- MainUi.cs's own comments call
# them "FullRect overlays above the drawer". Before this registry existed, a full playthrough that
# opened the Ledger and the Camp panel every day produced byte-identical Panel coverage to a run that
# never opened either (2026-08-12 finding, reproduced against a real archived run).
function Get-OverlayRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Join-Path $RepoRoot 'godot\scripts\MainUi.cs'
    if (-not (Test-Path $path)) { return ,@() }
    $text = Get-Content $path -Raw

    $blockMatch = [regex]::Match($text, '(?s)OverlaySurfaces\(\)\s*=>\s*new[^\{]*\{(.*?)\};')
    if (-not $blockMatch.Success) { return ,@() }
    $block = $blockMatch.Groups[1].Value

    $ids = @()
    foreach ($m in [regex]::Matches($block, '\(\s*"([A-Za-z0-9]+)"')) {
        $ids += $m.Groups[1].Value
    }
    return ,@($ids | Select-Object -Unique | Sort-Object)
}

# Town buildings: TownLayout2D's own outdoor venue table, scoped to the "Venues = { ... };" array
# literal specifically (never the whole file) so an unrelated future target-typed `new(...)` cannot
# silently leak into this list.
function Get-TownBuildingRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Join-Path $RepoRoot 'godot\scripts\town2d\TownLayout2D.cs'
    if (-not (Test-Path $path)) { return ,@() }
    $text = Get-Content $path -Raw

    $blockMatch = [regex]::Match($text, '(?s)VenueLayout\[\]\s*Venues\s*=\s*\{(.*?)\};')
    if (-not $blockMatch.Success) { return ,@() }
    $block = $blockMatch.Groups[1].Value

    $keys = @()
    foreach ($m in [regex]::Matches($block, 'new\(\s*"([a-z0-9\-]+)"')) {
        $keys += $m.Groups[1].Value
    }
    return ,@($keys | Select-Object -Unique | Sort-Object)
}

# Interior stations: InteriorLayout2D.Rooms declares market/tavern/minegate's stations inline as
# `new StationSpec("id", ...)`; the "forge" row instead calls WorkshopVocab.StationsFor(...), so its
# stations live in WorkshopVocab.cs's own per-profession table, not here. Both are combined below.
#
# Venue attribution for the InteriorLayout2D half: each RoomSpec entry opens with a target-typed
# `new(` (C# 9 inferred-type construction, distinct from `new StationSpec(` -- "new(" has nothing
# between "new" and "(", "new StationSpec(" does), followed by its venue key as the first quoted
# arg. A station belongs to whichever venue-start match most recently precedes it in the file --
# text position, not indentation, is the join key, since this is regex over source text, not an AST.
function Get-InteriorStationRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $result = New-Object System.Collections.ArrayList

    $layoutPath = Join-Path $RepoRoot 'godot\scripts\town2d\InteriorLayout2D.cs'
    if (Test-Path $layoutPath) {
        $text = Get-Content $layoutPath -Raw
        $venueMatches = @([regex]::Matches($text, 'new\(\s*"([a-z0-9\-]+)"'))
        $stationMatches = @([regex]::Matches($text, 'new StationSpec\(\s*"([a-zA-Z0-9\-]+)"'))
        foreach ($sm in $stationMatches) {
            $venue = $null
            foreach ($vm in $venueMatches) {
                if ($vm.Index -lt $sm.Index) { $venue = $vm.Groups[1].Value } else { break }
            }
            if ($venue) {
                [void]$result.Add([pscustomobject]@{ Venue = $venue; StationId = $sm.Groups[1].Value })
            }
        }
    }

    # Every StationSpec built by WorkshopVocab feeds the shared "forge" room (KTD-3: one shell, never
    # per-profession buildings) -- so unlike the loop above, every match here is unconditionally forge.
    $vocabPath = Join-Path $RepoRoot 'godot\scripts\town2d\WorkshopVocab.cs'
    if (Test-Path $vocabPath) {
        $text = Get-Content $vocabPath -Raw
        foreach ($m in [regex]::Matches($text, 'InteriorLayout2D\.StationSpec\(\s*"([a-zA-Z0-9\-]+)"')) {
            [void]$result.Add([pscustomobject]@{ Venue = 'forge'; StationId = $m.Groups[1].Value })
        }
    }

    return ,@($result)
}

# DayPhase values: the enum itself, comments stripped first so a trailing "// = 3 -- decision window"
# annotation (Camp, ExpeditionDeep both carry one) cannot leak stray words in as bogus phase names.
function Get-DayPhaseRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Join-Path $RepoRoot 'sim\GameSim\Contracts\Enums.cs'
    if (-not (Test-Path $path)) { return ,@() }
    $text = Get-Content $path -Raw

    $blockMatch = [regex]::Match($text, '(?s)enum DayPhase\s*\{(.*?)\}')
    if (-not $blockMatch.Success) { return ,@() }
    $block = [regex]::Replace($blockMatch.Groups[1].Value, '//[^\r\n]*', '')

    $names = @()
    foreach ($m in [regex]::Matches($block, '[A-Za-z_][A-Za-z0-9_]*')) { $names += $m.Value }
    return ,@($names | Select-Object -Unique)
}

# Action types: AgentPlaytestBridge.Apply()'s own switch on command.Action -- the bridge's REAL
# vocabulary, not agent-playtest.ps1's separately-maintained $verbs array (which exists for a
# different reason: normalizing a model's malformed reply before it ever reaches the bridge).
function Get-ActionTypeRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Join-Path $RepoRoot 'godot\scripts\tools\AgentPlaytest.cs'
    if (-not (Test-Path $path)) { return ,@() }
    $text = Get-Content $path -Raw

    $blockMatch = [regex]::Match($text, '(?s)ToLowerInvariant\(\)\s*switch\s*\{(.*?)\};')
    if (-not $blockMatch.Success) { return ,@() }
    $block = $blockMatch.Groups[1].Value

    $actions = @()
    foreach ($m in [regex]::Matches($block, '"([a-z]+)"\s*=>')) { $actions += $m.Groups[1].Value }
    return ,@($actions | Select-Object -Unique)
}

# HUD control names: BEST EFFORT ONLY -- see this file's own header note. Two patterns cover the
# large majority of buttons observed across godot/scripts: an inline object initializer
# (`new Button { Name = "X", ... }`) and the MainUi tray-button helper (`TrayButton("X", ...)`). A
# button whose Name is assigned on ITS OWN LINE after construction (`_advance.Name = "X"` where
# `_advance` was declared elsewhere) is invisible to this scan -- named explicitly in the caveat this
# feeds into Get-CoverageRegistries, never silently presented as a complete registry.
function Get-HudControlRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $scriptsDir = Join-Path $RepoRoot 'godot\scripts'
    if (-not (Test-Path $scriptsDir)) { return ,@() }

    $names = New-Object System.Collections.Generic.HashSet[string]
    $files = Get-ChildItem -Path $scriptsDir -Recurse -Filter '*.cs' -File
    foreach ($f in $files) {
        $text = Get-Content $f.FullName -Raw
        foreach ($m in [regex]::Matches($text, 'new\s+Button\s*\{[^{}]*?Name\s*=\s*"([A-Za-z0-9_]+)"')) {
            [void]$names.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($text, 'TrayButton\(\s*"([A-Za-z0-9_]+)"')) {
            [void]$names.Add($m.Groups[1].Value)
        }
    }
    return ,@(@($names) | Sort-Object)
}

# Every registry plus the caveats a reader needs to interpret them honestly -- the "N surfaces could
# not be enumerated" line the brief asks for, rather than dropping the hard cases.
function Get-CoverageRegistries {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $panels = Get-PanelIdRegistry -RepoRoot $RepoRoot
    $overlays = Get-OverlayRegistry -RepoRoot $RepoRoot
    $buildings = Get-TownBuildingRegistry -RepoRoot $RepoRoot
    $stations = Get-InteriorStationRegistry -RepoRoot $RepoRoot
    $stationKeys = @($stations | ForEach-Object { $_.Venue + '/' + $_.StationId } | Sort-Object -Unique)
    $phases = Get-DayPhaseRegistry -RepoRoot $RepoRoot
    $actions = Get-ActionTypeRegistry -RepoRoot $RepoRoot
    $controls = Get-HudControlRegistry -RepoRoot $RepoRoot

    $caveats = @()
    $caveats += ('HUD control names: ' + (@($controls).Count) + ' name(s) found by best-effort regex ' +
        'over "new Button { Name = ... }" and TrayButton("...") call sites -- this repo has no single ' +
        'central control registry, so a button whose Name is assigned on a separate line after ' +
        'construction is not counted and can never show as "touched" even if it was pressed. Treat ' +
        'this category''s denominator as a floor, not an exact count.')
    $caveats += ('Interior stations (forge only): the forge room''s station set is the UNION of all ' +
        'four professions'' stations (WorkshopVocab.ByProfession), but a single campaign selects only ' +
        '1-2 of the 4 (ProfessionHandlers.MaxSelected). Stations belonging to a profession this run ' +
        'never picked are structurally UNREACHABLE this run and will always read untouched -- that is ' +
        'expected, not a coverage gap, and this run''s findings should say which profession(s) were ' +
        'active before reading the forge untouched list as a defect.')

    return [pscustomobject]@{
        Panel           = $panels
        Overlay         = $overlays
        TownBuilding    = $buildings
        InteriorStation = $stationKeys
        DayPhase        = $phases
        ActionType      = $actions
        HudControl      = $controls
        Caveats         = $caveats
    }
}

# --- Per-turn touch recording ----------------------------------------------------------------------

# A fresh accumulator: one set (hashtable used as a set) per category. Categories match
# Get-CoverageRegistries' own property names 1:1 so Get-CoverageReport can walk both by name.
function New-CoverageTracker {
    return [ordered]@{
        Panel           = @{}
        Overlay         = @{}
        TownBuilding    = @{}
        InteriorStation = @{}
        DayPhase        = @{}
        ActionType      = @{}
        HudControl      = @{}
    }
}

# Records what ONE turn's state (and the command chosen for it, if any) touched. $State is a
# StateDigest-shaped object (state.json, ConvertFrom-Json'd -- see turn-prompt.ps1's own note on the
# field contract) and $Command is the command object about to be sent (has .action / .target),
# or $null for an observation with no command yet (the tracker is still updated for what the STATE
# showed; only the ActionType/HudControl categories need a command at all).
#
# "Touched" for a building/station is INRANGE, not merely visible in the nearby list -- the same
# proximity gate WorldInput2D's own interact prompt uses (AgentPlaytest.cs's InRangeReportingPx),
# so "touched" means "stood close enough to use it," not "saw it exist from across the map."
function Add-CoverageTouch {
    param(
        [Parameter(Mandatory)]$Tracker,
        [Parameter(Mandatory)]$State,
        $Command
    )

    $location = ''
    if ($State.location) { $location = $State.location.ToString() }

    if ($location.StartsWith('panel:')) {
        $Tracker.Panel[$location.Substring(6)] = $true
    } elseif ($location.StartsWith('overlay:')) {
        $Tracker.Overlay[$location.Substring(8)] = $true
    } elseif ($location -eq 'town') {
        foreach ($n in @($State.nearby)) {
            if ($n.inRange) { $Tracker.TownBuilding[$n.key] = $true }
        }
    } elseif ($location.StartsWith('interior:')) {
        $venue = $location.Substring(9)
        foreach ($n in @($State.nearby)) {
            if ($n.inRange) { $Tracker.InteriorStation[$venue + '/' + $n.key] = $true }
        }
    }

    if ($State.phase) { $Tracker.DayPhase[$State.phase.ToString()] = $true }

    if ($Command -and $Command.action) {
        $action = $Command.action.ToString().ToLowerInvariant()
        $Tracker.ActionType[$action] = $true
        if ($action -eq 'press' -and $Command.target) {
            $Tracker.HudControl[$Command.target.ToString()] = $true
        }
    }
}

# --- Report ------------------------------------------------------------------------------------

# Touched/untouched/percentage per category plus an overall rollup, from a registry set and a
# tracker built from a real run. Untouched is returned IN FULL (never top-N truncated) -- the whole
# point of this unit is that the tail nobody looked at is the part that matters.
function Get-CoverageReport {
    param(
        [Parameter(Mandatory)]$Registries,
        [Parameter(Mandatory)]$Tracker
    )

    $categories = @('Panel', 'Overlay', 'TownBuilding', 'InteriorStation', 'DayPhase', 'ActionType', 'HudControl')
    $byCategory = @()
    $totalAll = 0
    $totalTouched = 0

    foreach ($cat in $categories) {
        $all = @($Registries.$cat)
        $touchedSet = $Tracker.$cat
        $touched = @($all | Where-Object { $touchedSet.ContainsKey($_) })
        $untouched = @($all | Where-Object { -not $touchedSet.ContainsKey($_) })
        $pct = 0.0
        if ($all.Count -gt 0) { $pct = [math]::Round((100.0 * $touched.Count / $all.Count), 1) }

        $totalAll += $all.Count
        $totalTouched += $touched.Count

        $byCategory += [pscustomobject]@{
            Category   = $cat
            Total      = $all.Count
            Touched    = @($touched | Sort-Object)
            Untouched  = @($untouched | Sort-Object)
            Percentage = $pct
        }
    }

    $overallPct = 0.0
    if ($totalAll -gt 0) { $overallPct = [math]::Round((100.0 * $totalTouched / $totalAll), 1) }

    return [pscustomobject]@{
        Categories        = $byCategory
        OverallTouched    = $totalTouched
        OverallTotal      = $totalAll
        OverallPercentage = $overallPct
        Caveats           = @($Registries.Caveats)
    }
}

# coverage.md's full text.
function Format-CoverageMarkdown {
    param([Parameter(Mandatory)]$Report)

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('# Coverage census')
    [void]$lines.Add('')

    # 2026-08-12 (coverage-can-see-the-overlays finding B): a registry that returns ZERO entries (a
    # source-format regression in the regex that builds it, not a real "nothing left to touch") used
    # to fall through the $totalAll -gt 0 guard below into 0/0 -- which every downstream reader sees
    # as a clean, fully-covered run. A zero denominator must never render as success.
    if ($Report.OverallTotal -eq 0) {
        [void]$lines.Add('Overall: registry empty -- coverage undefined (every category totaled zero ' +
            'entries; this is a broken registry-building regex, not a run that covered everything).')
    } else {
        [void]$lines.Add('Overall: ' + $Report.OverallTouched + ' of ' + $Report.OverallTotal +
            ' surfaces touched (' + $Report.OverallPercentage + '%).')
    }
    [void]$lines.Add('')

    $caveats = @($Report.Caveats)
    [void]$lines.Add($caveats.Count.ToString() + ' categor(y/ies) carry an enumeration caveat -- read before treating any untouched list below as a defect:')
    foreach ($c in $caveats) { [void]$lines.Add('- ' + $c) }
    [void]$lines.Add('')

    foreach ($cat in $Report.Categories) {
        if ($cat.Total -eq 0) {
            # Same zero-denominator guard, per category: Get-PanelIdRegistry (or any sibling registry
            # function) returning ,@() must read as "this census is blind here", never as "(none --
            # full coverage this run)" -- the literal text a genuinely empty Untouched list prints
            # below, which a $cat.Total -eq 0 case would otherwise hit too.
            [void]$lines.Add('## ' + $cat.Category + ' (registry empty -- coverage undefined)')
            [void]$lines.Add('')
            [void]$lines.Add('This category''s registry returned ZERO entries. Treat this as a broken ' +
                'source-format regex to fix, not as evidence the run touched everything.')
            [void]$lines.Add('')
            continue
        }

        [void]$lines.Add('## ' + $cat.Category + ' (' + $cat.Touched.Count + '/' + $cat.Total + ', ' + $cat.Percentage + '%)')
        [void]$lines.Add('')
        [void]$lines.Add('Touched:')
        if ($cat.Touched.Count -eq 0) { [void]$lines.Add('(none)') }
        foreach ($t in $cat.Touched) { [void]$lines.Add('- ' + $t) }
        [void]$lines.Add('')
        [void]$lines.Add('Untouched -- FULL list, never truncated:')
        if ($cat.Untouched.Count -eq 0) { [void]$lines.Add('(none -- full coverage this run)') }
        foreach ($u in $cat.Untouched) { [void]$lines.Add('- ' + $u) }
        [void]$lines.Add('')
    }

    return ($lines -join [Environment]::NewLine)
}
