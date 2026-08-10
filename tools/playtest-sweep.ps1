<#
.SYNOPSIS
    Turns N nights of hand-typed sweep scripts into one repo tool: launches a matrix of
    tools/agent-playtest.ps1 runs (scope x persona x repeat) and aggregates them into a CSV and
    a detail report.

.DESCRIPTION
    Why this exists: every overnight sweep so far has been an ad-hoc PowerShell script in a
    scratch directory, rewritten from memory each night. The 2026-08-09 overnight sweep (30
    runs) produced a SUMMARY that was six columns wide -- run, scope, exit, verdict,
    model-driven turns, last day -- and every one of those is a COUNT. The owner's own words on
    that result: "the runs should be capturing all details not just run count ... the playtests
    should cover EVERYTHING/EVERY aspect using the three primary playtest modes ... The idea was
    to launch the playtests with different claudes who have variying undertstanding, knowledge,
    goals etc." See docs/plans/2026-08-10-001-feat-the-playtest-keeps-what-it-saw-plan.md (this
    tool is that plan's U5; it is written against U1-U4/U6, which land separately -- see the
    DEFENSIVE READING note below).

    This script does NOT replace tools/agent-playtest.ps1 -- it drives it, repeatedly, and reads
    whatever it wrote. It never modifies that file or tools/agent-playtest/prompts/.

    DEFENSIVE READING (important, and why several fields below can read empty). The driver
    checked into this worktree as of 2026-08-10 has NO -Persona parameter, no
    tools/agent-playtest/prompts/personas/ directory, and writes neither coverage.json nor
    backend.json -- those are U2/U3/U4 of the same plan, being built in parallel by other agents
    and not yet landed here. Rather than block on that or fake the numbers, every reader in this
    file is written to degrade honestly: a field the driver has not started emitting yet becomes
    an EMPTY cell plus a note in SUMMARY.csv's Notes column and in REPORT.md's own caveats
    section -- never a silent zero and never a crash. A silent zero here would read as "clean,"
    which is the exact defect class this repo's playtest harness has already been fixed for
    three times (A1/#419, the completion floor/#436, and the state-fingerprint incident this
    same lesson traces back to). Re-run this sweep once U1-U4/U6 land and the empty cells should
    start filling in on their own, with no changes needed here except widening the property-name
    guesses in Get-CoverageData / Get-BackendData / Get-FindingsFields if the real schema differs
    from the guess.

    PERSONA IS A LABEL TODAY, NOT YET A DIFFERENT PLAYER. -Personas is accepted and drives the
    run matrix and every output's naming, exactly as the plan asks -- but until
    tools/agent-playtest.ps1 grows a real -Persona parameter (U4), passing a persona name through
    to it selects nothing different in the actual run: every persona in a sweep plays the SAME
    act.md. Invoke-SweepRun feature-detects the parameter at call time (Get-Command's own
    metadata, not a version string) so the day U4 lands, real per-persona runs start happening
    with no edit needed here. Until then, this is recorded plainly in run-meta.json
    (personaPassedToDriver: false) and surfaced in REPORT.md's per-persona section rather than
    left for a reader to discover the hard way.

    SERIAL BY CONSTRUCTION -- do not parallelize this loop. Each run launches its own real Godot
    client (agentplaytest.tscn) and holds the local ollama vision model resident on the one GPU
    this harness is scoped to (see tools/agent-playtest.ps1's own -MinFreeGb note). Two runs at
    once fight over both: the second Godot launch either fails outright or corrupts the first
    run's frame captures, and ollama does not usefully serve two concurrent generations on one
    consumer GPU -- it queues them, which is slower than serial and produces confusing per-run
    timing on top of it. This is the same constraint tools/engine-test.ps1 already documents for
    gdUnit ("this machine's gdUnit runtime cannot be shared across two concurrent runs"). A
    30-run night is meant to run overnight unattended; it is not meant to run faster.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every other file in
    tools/agent-playtest/ (Windows PowerShell 5.1 reads a BOM-less UTF-8 file as ANSI/mojibake,
    and mis-indented here-string terminators are a parse error -- see agent-playtest.ps1's own
    note; this file is not dot-sourced by it, but runs under the same PowerShell 5.1).

.PARAMETER Runs
    Repeats per (scope, persona) pair. Total runs launched = Runs * Scopes.Count * Personas.Count.

.PARAMETER Scopes
    Which of tools/agent-playtest.ps1's three modes to sweep. Default is all three, because
    "cover EVERY aspect using the three primary playtest modes" is the owner's own ask.

.PARAMETER Personas
    Persona labels, one run-matrix axis per entry. Default is the five named in
    docs/plans/2026-08-10-001-feat-the-playtest-keeps-what-it-saw-plan.md's U4 (first-timer,
    veteran, speedrunner, completionist, sceptic) so this tool's default vocabulary matches the
    persona files that unit will add. See the DESCRIPTION's "PERSONA IS A LABEL TODAY" note --
    until U4 lands, every persona here plays an identical act.md.

.PARAMETER Turns
    Turn budget passed to every run in the sweep (tools/agent-playtest.ps1's own -Turns).
    Measured rate is roughly 11-16 turns per in-game day (see the U5 plan doc's own scope
    boundary) -- 40 (the driver's default) reaches at most a handful of in-game days. A sweep
    meant to answer the day-11 boredom question needs roughly 110-160 turns per run; REPORT.md
    says this explicitly if no run in the sweep gets there.

.PARAMETER OutDir
    Base directory for sweep output. A timestamped subdirectory is created inside it
    (yyyy-MM-dd_HHmmss) so two sweeps never collide. Default: <repo>\runs\playtest.

.PARAMETER AggregateFrom
    Skip launching anything and re-run SUMMARY.csv/REPORT.md generation over an existing sweep
    directory (or any directory whose immediate subdirectories look like agent-playtest output).
    Useful after manually inspecting a sweep, after killing a partial overnight run, or -- as
    used by this file's own tests -- against synthetic fixture directories with no Godot or
    ollama involved at all.

.PARAMETER Model
    Passed through to every run (tools/agent-playtest.ps1's own -Model). Kept constant across a
    sweep on purpose: comparing personas/scopes is confounded if the vision model also changes
    mid-sweep.

.PARAMETER RepoRoot
    Override for testing / non-standard checkouts. Defaults to this script's own parent
    directory's parent (i.e. the repo root), same convention as tools/agent-playtest.ps1.

.PARAMETER DryRun
    Print the exact run matrix (every scope x persona x index, with its turn budget and an
    estimated wall clock) and exit. Launches nothing, creates nothing on disk. A 30-run night
    must be checkable in one second before it is spent -- that is this switch's whole job.

.EXAMPLE
    tools/playtest-sweep.ps1 -DryRun -Runs 2 -Scopes Full,Scout -Personas first-timer,veteran

.EXAMPLE
    tools/playtest-sweep.ps1 -Runs 1 -Turns 150

.EXAMPLE
    tools/playtest-sweep.ps1 -AggregateFrom runs/playtest/2026-08-10_020000
#>
[CmdletBinding()]
param(
    [int]$Runs = 1,
    [string[]]$Scopes = @('Full', 'Diff', 'Scout'),
    [string[]]$Personas = @('first-timer', 'veteran', 'speedrunner', 'completionist', 'sceptic'),
    [int]$Turns = 40,
    [string]$OutDir,
    [string]$AggregateFrom,
    [string]$Model = 'llava:7b',
    [string]$RepoRoot,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Say($text) { Write-Host ('playtest-sweep: ' + $text) -ForegroundColor Cyan }
function Warn($text) { Write-Host ('playtest-sweep: ' + $text) -ForegroundColor Yellow }
function Die($lines) {
    Write-Host ''
    Write-Host ('PLAYTEST-SWEEP REFUSED: ' + $lines[0]) -ForegroundColor Red
    if ($lines.Count -gt 1) {
        foreach ($line in $lines[1..($lines.Count - 1)]) { Write-Host $line -ForegroundColor Red }
    }
    exit 1
}

# Split any comma-joined element and trim whitespace. MEASURED (2026-08-10): invoking this script
# via `powershell -File tools\playtest-sweep.ps1 -Scopes Full,Scout` -- exactly the syntax this
# file's own .EXAMPLE and the calling brief both use -- arrives as ONE string element
# "Full,Scout", not two array elements; PowerShell's -File argument binding does not split
# comma-joined values the way typing the same thing at an interactive prompt would. A
# [ValidateSet] on -Scopes would reject that single mangled string outright before this script's
# body ever runs, which is why -Scopes has no ValidateSet attribute above -- validation happens
# below, AFTER splitting, so it can fail on the real per-scope value instead of on the whole
# unsplit string. Also tolerates the "real array" case (calling this script in-process via `&`
# with an actual multi-element array) as a no-op, since splitting a string with no comma in it
# returns the string unchanged.
function Split-CommaList {
    param([string[]]$Values)
    $result = New-Object System.Collections.ArrayList
    foreach ($v in $Values) {
        if (-not $v) { continue }
        foreach ($piece in ($v -split ',')) {
            $t = $piece.Trim()
            if ($t) { [void]$result.Add($t) }
        }
    }
    return @($result)
}

# =================================================================================================
# PURE LOGIC -- everything below this line and above "MAIN" needs no Godot, no ollama, no VRAM.
# Proven by tools/agent-playtest/tests/test-playtest-sweep.ps1 against this exact file.
# =================================================================================================

# --- run matrix -----------------------------------------------------------------------------

function Get-RunPlan {
    param(
        [Parameter(Mandatory)][int]$Runs,
        [Parameter(Mandatory)][string[]]$Scopes,
        [Parameter(Mandatory)][string[]]$Personas,
        [Parameter(Mandatory)][int]$Turns
    )
    $plan = New-Object System.Collections.ArrayList
    foreach ($scope in $Scopes) {
        foreach ($persona in $Personas) {
            for ($i = 1; $i -le $Runs; $i++) {
                $tag = $scope + '-' + $persona + '-' + $i
                [void]$plan.Add([pscustomobject]@{
                    Tag     = $tag
                    Scope   = $scope
                    Persona = $persona
                    Index   = $i
                    Turns   = $Turns
                })
            }
        }
    }
    return @($plan)
}

# ASSUMED constants, clearly labeled everywhere they are printed -- never presented as measured.
# No prior sweep in this repo recorded per-turn wall-clock timestamps (checked: run-meta.json is
# new in this file; nothing before it timed a run). These exist only so a dry run can print SOME
# number for planning, not zero. Update once Invoke-SweepRun's own run-meta.json timestamps give
# this repo a real baseline to replace them with.
function Get-EstimatedRunMinutes {
    param(
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][int]$Turns
    )
    $secondsPerTurnAssumed = 8.0
    $startupMinutesAssumed = 1.0
    $scoutMechanicalMinutesAssumed = 10.0

    $minutes = $startupMinutesAssumed + (($Turns * $secondsPerTurnAssumed) / 60.0)
    if ($Scope -eq 'Scout') { $minutes = $minutes + $scoutMechanicalMinutesAssumed }
    return [math]::Round($minutes, 1)
}

function Format-RunMatrix {
    param([Parameter(Mandatory)]$Plan)

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('playtest-sweep: DRY RUN -- ' + $Plan.Count + ' run(s) would be launched, nothing launched, nothing created on disk.')
    [void]$lines.Add('Serial by construction: each run holds its own Godot client and the local vision model,')
    [void]$lines.Add('so total wall clock below is a SUM, not a max -- see this script''s own header for why.')
    [void]$lines.Add('')
    [void]$lines.Add('  #  tag                              scope  persona          turns  est.min(*)')

    $totalMinutes = 0.0
    $i = 0
    foreach ($entry in $Plan) {
        $i++
        $est = Get-EstimatedRunMinutes -Scope $entry.Scope -Turns $entry.Turns
        $totalMinutes = $totalMinutes + $est
        $row = '  ' + $i.ToString().PadLeft(2) + '  ' + $entry.Tag.PadRight(32) + ' ' +
            $entry.Scope.PadRight(6) + ' ' + $entry.Persona.PadRight(16) + ' ' +
            $entry.Turns.ToString().PadLeft(5) + '  ' + $est.ToString('0.0').PadLeft(8)
        [void]$lines.Add($row)
    }

    [void]$lines.Add('')
    [void]$lines.Add('Total estimated wall clock: ' + [math]::Round($totalMinutes, 1) + ' minute(s) (' +
        [math]::Round($totalMinutes / 60.0, 2) + ' hour(s)), serial.')
    [void]$lines.Add('')
    [void]$lines.Add('(*) ASSUMED, not measured -- no prior sweep in this repo recorded per-turn wall clock.')
    [void]$lines.Add('    Placeholder: 8s/turn + 1 min startup, +10 min for Scope Scout''s mechanical')
    [void]$lines.Add('    detectors (FullPlaytest + Playtest3dRecorder). See Get-EstimatedRunMinutes.')

    return @($lines)
}

# --- findings.md header parsing (defensive: every field can be absent) ------------------------

# Known metadata bullet-line prefixes tools/agent-playtest.ps1 writes into findings.md's header,
# plus the sentence-shaped banners (DEGRADED:/INCOMPLETE:) and the terminal-state one-liners. Used
# both to parse fields out and, in Get-CandidateFindingLines, to EXCLUDE these from the recurring-
# findings scan so restating "- imageless turns: 0" on thirty clean runs cannot masquerade as a
# recurring finding.
$script:FindingsHeaderLinePatterns = @(
    '^-\s*scope:',
    '^-\s*model:',
    '^-\s*turns:',
    '^-\s*completion:',
    '^-\s*model-driven turns:',
    '^-\s*fallback turns:',
    '^-\s*imageless turns:',
    '^-\s*artifacts:',
    '^-\s*playtest log',
    '^-\s*diff scope:',
    '^-\s*persona:',
    '^-\s*(act )?prompt hash:',
    '^DEGRADED:',
    '^INCOMPLETE:',
    '^Scripted run',
    '^JUDGE FAILED',
    '^NOTHING WAS PLAYED'
)

# Reads findings.md's own header, never inventing a value the driver did not print and never
# throwing on a header shape it does not recognize. A field this cannot find becomes $null (an
# empty CSV cell plus a Notes entry), NEVER a coerced zero -- see this file's own header note on
# why a silent zero here would read as "clean" and hide the exact thing being measured.
function Get-FindingsFields {
    param([Parameter(Mandatory)][string]$RunDir)

    $notes = New-Object System.Collections.ArrayList
    $findingsPath = Join-Path $RunDir 'findings.md'
    if (-not (Test-Path $findingsPath)) {
        [void]$notes.Add('findings.md missing')
        return [pscustomobject]@{
            Missing              = $true
            Text                 = ''
            HeaderScope          = $null
            Model                = $null
            TurnsRan             = $null
            TurnsBudget          = $null
            CompletionPct        = $null
            ModelDrivenTurns     = $null
            FallbackTurns        = $null
            FallbackPct          = $null
            Degraded             = $false
            DegradedSentence     = $null
            Incomplete           = $false
            IncompleteSentence   = $null
            DiffFellBack         = $false
            DiffFellBackSentence = $null
            Persona              = $null
            PromptHash           = $null
            Notes                = @($notes)
        }
    }

    $text = Get-Content $findingsPath -Raw
    if (-not $text) { $text = '' }

    $headerScope = $null
    $m = [regex]::Match($text, '(?im)^-\s*scope:\s*(\S+)\s*$')
    if ($m.Success) { $headerScope = $m.Groups[1].Value } else { [void]$notes.Add('scope: line not found in header') }

    $model = $null
    $m = [regex]::Match($text, '(?im)^-\s*model:\s*(\S+)\s*$')
    if ($m.Success) { $model = $m.Groups[1].Value }

    $turnsRan = $null
    $m = [regex]::Match($text, '(?im)^-\s*turns:\s*(\d+)\s*\(stopped:')
    if ($m.Success) { $turnsRan = [int]$m.Groups[1].Value } else { [void]$notes.Add('turns: line not found') }

    $turnsBudget = $null
    $completionPct = $null
    $m = [regex]::Match($text, '(?im)^-\s*completion:\s*\d+\s*of\s*(\d+)\s*budgeted turns\s*\(([\d.]+)%\)')
    if ($m.Success) {
        $turnsBudget = [int]$m.Groups[1].Value
        $completionPct = [double]$m.Groups[2].Value
    } else { [void]$notes.Add('completion: line not found') }

    $modelDrivenTurns = $null
    $m = [regex]::Match($text, '(?im)^-\s*model-driven turns:\s*(\d+)')
    if ($m.Success) { $modelDrivenTurns = [int]$m.Groups[1].Value } else { [void]$notes.Add('model-driven turns: line not found') }

    $fallbackTurns = $null
    $fallbackPct = $null
    $m = [regex]::Match($text, '(?im)^-\s*fallback turns:\s*(\d+)\s*\(([\d.]+)%')
    if ($m.Success) {
        $fallbackTurns = [int]$m.Groups[1].Value
        $fallbackPct = [double]$m.Groups[2].Value
    } else { [void]$notes.Add('fallback turns: line not found') }

    $degraded = $false
    $degradedSentence = $null
    $m = [regex]::Match($text, '(?m)^(DEGRADED:.*)$')
    if ($m.Success) { $degraded = $true; $degradedSentence = $m.Groups[1].Value.Trim() }

    $incomplete = $false
    $incompleteSentence = $null
    $m = [regex]::Match($text, '(?m)^(INCOMPLETE:.*)$')
    if ($m.Success) { $incomplete = $true; $incompleteSentence = $m.Groups[1].Value.Trim() }

    $diffFellBack = $false
    $diffFellBackSentence = $null
    $m = [regex]::Match($text, '(?im)^-\s*diff scope:\s*(.*)$')
    if ($m.Success) {
        $diffFellBackSentence = $m.Groups[1].Value.Trim()
        if ($diffFellBackSentence -match '(?i)FELL BACK') { $diffFellBack = $true }
    }

    # SPECULATIVE (U4, not landed here as of 2026-08-10): persona + a short prompt hash are
    # planned to go in findings.md's header, but no build of agent-playtest.ps1 in this checkout
    # writes them -- verified directly: no -Persona parameter, no
    # tools/agent-playtest/prompts/personas/ directory. Try the label the plan doc itself uses; if
    # it is not there, say so plainly rather than guessing a hash that was never computed.
    $persona = $null
    $m = [regex]::Match($text, '(?im)^-\s*persona:\s*(.+?)\s*$')
    if ($m.Success) { $persona = $m.Groups[1].Value } else { [void]$notes.Add('persona: not reported by this build of agent-playtest.ps1 (needs the persona unit)') }

    $promptHash = $null
    $m = [regex]::Match($text, '(?im)^-\s*(?:act )?prompt hash:\s*([0-9a-fA-F]+)')
    if ($m.Success) { $promptHash = $m.Groups[1].Value } else { [void]$notes.Add('prompt hash: not reported by this build of agent-playtest.ps1 (needs the persona unit)') }

    return [pscustomobject]@{
        Missing              = $false
        Text                 = $text
        HeaderScope          = $headerScope
        Model                = $model
        TurnsRan             = $turnsRan
        TurnsBudget          = $turnsBudget
        CompletionPct        = $completionPct
        ModelDrivenTurns     = $modelDrivenTurns
        FallbackTurns        = $fallbackTurns
        FallbackPct          = $fallbackPct
        Degraded             = $degraded
        DegradedSentence     = $degradedSentence
        Incomplete           = $incomplete
        IncompleteSentence   = $incompleteSentence
        DiffFellBack         = $diffFellBack
        DiffFellBackSentence = $diffFellBackSentence
        Persona              = $persona
        PromptHash           = $promptHash
        Notes                = @($notes)
    }
}

# turnlog.md is written by the GODOT CLIENT (AgentPlaytest.cs), one "- day N phase P beat B
# location L ..." line per turn. Taking the max across all such lines is how "last in-game day"
# is derived without needing any new field from the driver -- this line has existed since the
# harness's first commit (#380).
function Get-LastDayFromTurnlog {
    param([Parameter(Mandatory)][string]$RunDir)

    $path = Join-Path $RunDir 'turnlog.md'
    if (-not (Test-Path $path)) { return $null }
    $text = Get-Content $path -Raw
    if (-not $text) { return $null }

    $found = [regex]::Matches($text, '(?im)^-\s*day\s+(\d+)\s+phase\s')
    if ($found.Count -eq 0) { return $null }
    $days = @($found | ForEach-Object { [int]$_.Groups[1].Value })
    return ($days | Measure-Object -Maximum).Maximum
}

# --- coverage.json / backend.json (U2/U3 -- speculative schema, see this file's own header) ----

function Get-CoverageData {
    param([Parameter(Mandatory)][string]$RunDir)

    $path = Join-Path $RunDir 'coverage.json'
    if (-not (Test-Path $path)) {
        return [pscustomobject]@{
            Available  = $false
            Percentage = $null
            Touched    = @()
            Untouched  = @()
            Note       = 'coverage.json not present (needs the coverage census unit, not yet landed in this driver build)'
        }
    }
    try {
        $j = Get-Content $path -Raw | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{
            Available  = $false
            Percentage = $null
            Touched    = @()
            Untouched  = @()
            Note       = 'coverage.json present but could not be parsed as JSON: ' + $_.Exception.Message
        }
    }

    # SPECULATIVE SCHEMA: tries the property names the U5 plan doc's own vocabulary implies
    # (touched/untouched/percentage). Update these three lines once U3 lands with its real shape.
    $touched = @()
    $untouched = @()
    $pct = $null
    if ($j.PSObject.Properties.Name -contains 'touched') { $touched = @($j.touched) }
    if ($j.PSObject.Properties.Name -contains 'untouched') { $untouched = @($j.untouched) }
    if ($j.PSObject.Properties.Name -contains 'percentage') { $pct = $j.percentage }

    $note = $null
    if ((@($touched)).Count -eq 0 -and (@($untouched)).Count -eq 0) {
        $note = 'coverage.json present but none of the expected fields (touched/untouched/percentage) were found -- schema may differ from what this reader expects'
    }
    return [pscustomobject]@{ Available = $true; Percentage = $pct; Touched = $touched; Untouched = $untouched; Note = $note }
}

function Get-BackendData {
    param([Parameter(Mandatory)][string]$RunDir)

    $path = Join-Path $RunDir 'backend.json'
    if (-not (Test-Path $path)) {
        return [pscustomobject]@{
            Available          = $false
            ContradictionCount = $null
            Note               = 'backend.json not present (needs the backend-log unit, not yet landed in this driver build)'
        }
    }
    try {
        $j = Get-Content $path -Raw | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{
            Available          = $false
            ContradictionCount = $null
            Note               = 'backend.json present but could not be parsed as JSON: ' + $_.Exception.Message
        }
    }

    # SPECULATIVE SCHEMA: same caveat as Get-CoverageData. Tries a scalar count field first, then
    # falls back to counting an array of contradiction records.
    $count = $null
    $note = $null
    if ($j.PSObject.Properties.Name -contains 'contradictionCount') {
        $count = $j.contradictionCount
    } elseif ($j.PSObject.Properties.Name -contains 'contradictions') {
        $count = (@($j.contradictions)).Count
    } else {
        $note = 'backend.json present but no recognised contradiction field (contradictionCount / contradictions) -- schema may differ from what this reader expects'
    }
    return [pscustomobject]@{ Available = $true; ContradictionCount = $count; Note = $note }
}

# --- recurring findings: honest normalised-line matching, not semantic clustering --------------

function Get-NormalizedLine {
    param([string]$Line)
    $t = $Line.Trim()
    $t = $t -replace '^[-*]\s*', ''
    $t = $t -replace '^\d+[\.\)]\s*', ''
    $t = $t.ToLowerInvariant()
    $t = $t -replace '\s+', ' '
    return $t
}

# Candidate "finding" lines from ONE run's findings.md: everything before "## Turn log" (the raw
# log tail), minus the known metadata header lines/banners, minus markdown headings and the '---'
# separator, minus anything shorter than 8 characters (noise). A coarse filter, not a parser --
# see Build-RecurrenceGroups's own note on why that tradeoff is named rather than hidden.
function Get-CandidateFindingLines {
    param([string]$Text)

    if (-not $Text) { return @() }
    $marker = '## Turn log'
    $idx = $Text.IndexOf($marker)
    $head = $Text
    if ($idx -ge 0) { $head = $Text.Substring(0, $idx) }

    $result = New-Object System.Collections.ArrayList
    $lines = $head -split "`r?`n"
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if (-not $trimmed) { continue }
        if ($trimmed.StartsWith('#')) { continue }
        if ($trimmed -eq '---') { continue }
        $isHeaderLine = $false
        foreach ($pat in $script:FindingsHeaderLinePatterns) {
            if ($trimmed -match $pat) { $isHeaderLine = $true; break }
        }
        if ($isHeaderLine) { continue }
        if ($trimmed.Length -lt 8) { continue }
        [void]$result.Add($trimmed)
    }
    return @($result)
}

# Findings that recur across DIFFERENT runs. Deliberately a normalised-LINE match -- trim,
# lowercase, collapse whitespace, strip one leading bullet/number marker -- NOT semantic
# clustering. Two runs describing the same bug in different words will not group here; read each
# run's own findings.md for anything phrased differently. Each run counts at most ONCE per
# distinct normalised line, so a model repeating itself five times inside one run's own prose
# cannot masquerade as "five runs agree" -- that distinction (one run's opinion vs. three
# personas independently landing on the same line) is the entire point the owner's ask names.
function Build-RecurrenceGroups {
    param([Parameter(Mandatory)]$RunLines)  # array of @{ RunTag; Lines }

    $groups = @{}
    foreach ($row in $RunLines) {
        $seenInRun = @{}
        foreach ($line in $row.Lines) {
            $norm = Get-NormalizedLine $line
            if (-not $norm) { continue }
            if ($seenInRun.ContainsKey($norm)) { continue }
            $seenInRun[$norm] = $true
            if (-not $groups.ContainsKey($norm)) {
                $groups[$norm] = [pscustomobject]@{
                    Normalized = $norm
                    Example    = $line
                    RunTags    = New-Object System.Collections.Generic.List[string]
                }
            }
            $groups[$norm].RunTags.Add($row.RunTag)
        }
    }

    $result = @()
    foreach ($g in $groups.Values) {
        if ($g.RunTags.Count -ge 2) { $result += $g }
    }
    return @($result | Sort-Object -Property @{ Expression = { $_.RunTags.Count }; Descending = $true })
}

# --- per-run row assembly -----------------------------------------------------------------------

# One row = one run directory's worth of evidence, from every source this file knows how to read.
# Every field that could not be established is $null, never a guessed value -- see the file
# header's DEFENSIVE READING note.
function Get-RunSummaryRow {
    param([Parameter(Mandatory)][string]$RunDir)

    $tag = Split-Path -Leaf $RunDir
    $notes = New-Object System.Collections.ArrayList

    # run-meta.json is written by THIS file's own Invoke-SweepRun. Absent on any directory this
    # tool did not launch itself (a hand-copied run, or this file's own test fixtures) -- exit
    # code and "was persona really passed to the driver" become unknown, not guessed.
    $meta = $null
    $metaPath = Join-Path $RunDir 'run-meta.json'
    if (Test-Path $metaPath) {
        try { $meta = Get-Content $metaPath -Raw | ConvertFrom-Json } catch { [void]$notes.Add('run-meta.json present but unparsable') }
    } else {
        [void]$notes.Add('run-meta.json missing (exit code unknown; scope/persona recovered from the directory name if possible)')
    }

    $scope = $null
    $persona = $null
    if ($meta -and $meta.scope) { $scope = $meta.scope }
    if ($meta -and $meta.persona) { $persona = $meta.persona }
    if ((-not $scope) -or (-not $persona)) {
        $m = [regex]::Match($tag, '^(Full|Diff|Scout)-(.+)-(\d+)$')
        if ($m.Success) {
            if (-not $scope) { $scope = $m.Groups[1].Value }
            if (-not $persona) { $persona = $m.Groups[2].Value }
        } else {
            [void]$notes.Add('scope/persona could not be recovered from run-meta.json or the directory name')
        }
    }

    $findings = Get-FindingsFields -RunDir $RunDir
    foreach ($n in $findings.Notes) { [void]$notes.Add($n) }

    $lastDay = Get-LastDayFromTurnlog -RunDir $RunDir
    if ($null -eq $lastDay) { [void]$notes.Add('turnlog.md missing or has no readable day line (last in-game day unknown)') }

    $coverage = Get-CoverageData -RunDir $RunDir
    if ($coverage.Note) { [void]$notes.Add($coverage.Note) }
    $untouchedCount = $null
    if ($coverage.Available) { $untouchedCount = (@($coverage.Untouched)).Count }

    $backend = Get-BackendData -RunDir $RunDir
    if ($backend.Note) { [void]$notes.Add($backend.Note) }

    $exitCode = $null
    if ($meta -and ($meta.PSObject.Properties.Name -contains 'exitCode')) { $exitCode = $meta.exitCode }

    $verdictParts = @()
    if ($findings.Missing) {
        $verdictParts += 'MISSING'
    } else {
        if ($findings.Degraded) { $verdictParts += 'DEGRADED' }
        if ($findings.Incomplete) { $verdictParts += 'INCOMPLETE' }
        if ($findings.DiffFellBack) { $verdictParts += 'FELL BACK' }
    }
    $verdict = 'CLEAN'
    if ($verdictParts.Count -gt 0) { $verdict = ($verdictParts -join ' + ') }

    $completionRatioText = $null
    if ($null -ne $findings.CompletionPct) { $completionRatioText = ($findings.CompletionPct.ToString() + '%') }

    return [pscustomobject]@{
        RunTag                    = $tag
        Scope                     = $scope
        Persona                   = $persona
        PromptHash                = $findings.PromptHash
        ExitCode                  = $exitCode
        Verdict                   = $verdict
        ModelDrivenTurns          = $findings.ModelDrivenTurns
        FallbackTurns             = $findings.FallbackTurns
        CompletionRatio           = $completionRatioText
        LastInGameDay             = $lastDay
        CoveragePercentage        = $coverage.Percentage
        UntouchedSurfaceCount     = $untouchedCount
        BackendContradictionCount = $backend.ContradictionCount
        Notes                     = ($notes -join ' | ')
        # Not CSV columns -- kept on the row for REPORT.md's section builders below.
        FindingsFields            = $findings
        CoverageData              = $coverage
    }
}

# --- report sections -----------------------------------------------------------------------------

function Get-CoverageUnion {
    param([Parameter(Mandatory)]$Rows)

    $anyAvailable = $false
    $totalSurfaces = @{}
    $touchedSurfaces = @{}
    foreach ($row in $Rows) {
        $cov = $row.CoverageData
        if (-not $cov -or -not $cov.Available) { continue }
        $anyAvailable = $true
        foreach ($s in @($cov.Touched)) { $touchedSurfaces[$s] = $true; $totalSurfaces[$s] = $true }
        foreach ($s in @($cov.Untouched)) { $totalSurfaces[$s] = $true }
    }

    if (-not $anyAvailable) {
        return [pscustomobject]@{
            Available    = $false
            TotalCount   = $null
            TouchedCount = $null
            NeverTouched = @()
            Note         = 'no run in this sweep reported coverage.json -- the coverage census unit has not landed in the driver build used here, so "what was never touched" cannot be answered by this sweep'
        }
    }

    $neverTouched = @($totalSurfaces.Keys | Where-Object { -not $touchedSurfaces.ContainsKey($_) } | Sort-Object)
    return [pscustomobject]@{
        Available    = $true
        TotalCount   = $totalSurfaces.Keys.Count
        TouchedCount = $touchedSurfaces.Keys.Count
        NeverTouched = $neverTouched
        Note         = $null
    }
}

function Get-NamedBadRuns {
    param([Parameter(Mandatory)]$Rows)

    $result = New-Object System.Collections.ArrayList
    foreach ($row in $Rows) {
        $f = $row.FindingsFields
        if (-not $f) { continue }
        if ($f.Missing) {
            [void]$result.Add(($row.RunTag + ': MISSING -- findings.md was never written for this run (see its Notes)'))
            continue
        }
        if ($f.Degraded) { [void]$result.Add(($row.RunTag + ': ' + $f.DegradedSentence)) }
        if ($f.Incomplete) { [void]$result.Add(($row.RunTag + ': ' + $f.IncompleteSentence)) }
        if ($f.DiffFellBack) { [void]$result.Add(($row.RunTag + ': ' + $f.DiffFellBackSentence)) }
    }
    return @($result)
}

function Get-DayElevenAnswer {
    param([Parameter(Mandatory)]$Rows)

    $days = @($Rows | ForEach-Object { $_.LastInGameDay } | Where-Object { $null -ne $_ })
    if ($days.Count -eq 0) {
        return 'No run in this sweep produced a readable turnlog.md day. The deepest in-game day ' +
            'reached is UNKNOWN, so the day-11 boredom question was NOT answered by this sweep. ' +
            'Measured rate is roughly 11-16 turns per in-game day (see ' +
            'docs/plans/2026-08-10-001-feat-the-playtest-keeps-what-it-saw-plan.md), so reaching ' +
            'day 10 needs roughly 110-160 turns per run -- check the -Turns budget used here.'
    }

    $maxDay = ($days | Measure-Object -Maximum).Maximum
    if ($maxDay -lt 9) {
        return 'Deepest in-game day reached by any run in this sweep: ' + $maxDay + '. That falls ' +
            'short of the day-10/11 wall, so the day-11 boredom question was NOT answered by this ' +
            'sweep. Measured rate is roughly 11-16 turns per in-game day, so reaching day 10 needs ' +
            'roughly 110-160 turns per run -- raise -Turns for a sweep meant to answer it.'
    }
    return 'Deepest in-game day reached by any run in this sweep: ' + $maxDay + '. This sweep reached ' +
        'day 10-or-later territory -- the day-11 boredom question is in scope for these runs; read ' +
        'their findings.md for whether the loop actually changed shape at that point.'
}

function Get-PersonaDifferences {
    param([Parameter(Mandatory)]$Rows)

    $lines = New-Object System.Collections.ArrayList
    $byPersona = @($Rows | Where-Object { $_.Persona } | Group-Object -Property Persona)
    if ($byPersona.Count -eq 0) {
        [void]$lines.Add('No run recovered a persona label at all (see Notes) -- per-persona comparison is empty.')
        return @($lines)
    }

    # Whether the driver actually varied the prompt per persona, or every persona ran the same
    # act.md verbatim (today's reality until U4 lands -- see Get-FindingsFields's own note).
    # Checked once so every line below is honest about what it can and cannot claim.
    $hashes = @($Rows | ForEach-Object { $_.PromptHash } | Where-Object { $_ } | Select-Object -Unique)
    if ($hashes.Count -le 1) {
        [void]$lines.Add('CAVEAT: no run in this sweep reported a distinct prompt hash per persona, so it ' +
            'cannot be confirmed the driver actually played each persona differently. The "persona" below ' +
            'is only the label this sweep REQUESTED, not a verified distinct prompt -- see run-meta.json''s ' +
            'personaPassedToDriver field. This needs the persona unit (U4) to land in agent-playtest.ps1.')
        [void]$lines.Add('')
    }

    # Coverage-based "what did only this persona touch" is computed only when every persona group
    # has coverage data -- never faked when some runs have it and others do not.
    $touchedByPersona = @{}
    $coverageUsable = $true
    foreach ($g in $byPersona) {
        $set = @{}
        foreach ($row in $g.Group) {
            if ($row.CoverageData -and $row.CoverageData.Available) {
                foreach ($s in @($row.CoverageData.Touched)) { $set[$s] = $true }
            } else {
                $coverageUsable = $false
            }
        }
        $touchedByPersona[$g.Name] = $set
    }

    foreach ($g in $byPersona) {
        $daysHere = @($g.Group | ForEach-Object { $_.LastInGameDay } | Where-Object { $null -ne $_ })
        $maxDayText = 'unknown'
        if ($daysHere.Count -gt 0) { $maxDayText = (($daysHere | Measure-Object -Maximum).Maximum).ToString() }

        $line = '- ' + $g.Name + ' (' + $g.Count + ' run(s)): deepest day reached ' + $maxDayText
        if ($coverageUsable) {
            $others = @{}
            foreach ($g2 in $byPersona) {
                if ($g2.Name -eq $g.Name) { continue }
                foreach ($k in $touchedByPersona[$g2.Name].Keys) { $others[$k] = $true }
            }
            $unique = @($touchedByPersona[$g.Name].Keys | Where-Object { -not $others.ContainsKey($_) } | Sort-Object)
            if ($unique.Count -gt 0) {
                $line = $line + '; touched, and no other persona touched: ' + ($unique -join ', ')
            } else {
                $line = $line + '; nothing touched exclusively by this persona'
            }
        } else {
            $line = $line + ' (coverage data unavailable for at least one persona -- exclusivity not computed)'
        }
        [void]$lines.Add($line)
    }
    return @($lines)
}

# --- writers ---------------------------------------------------------------------------------

function Write-SweepSummaryCsv {
    param([Parameter(Mandatory)]$Rows, [Parameter(Mandatory)][string]$Path)

    $csvRows = foreach ($r in $Rows) {
        [pscustomobject]@{
            RunTag                    = $r.RunTag
            Scope                     = $r.Scope
            Persona                   = $r.Persona
            PromptHash                = $r.PromptHash
            ExitCode                  = $r.ExitCode
            Verdict                   = $r.Verdict
            ModelDrivenTurns          = $r.ModelDrivenTurns
            FallbackTurns             = $r.FallbackTurns
            CompletionRatio           = $r.CompletionRatio
            LastInGameDay             = $r.LastInGameDay
            CoveragePercentage        = $r.CoveragePercentage
            UntouchedSurfaceCount     = $r.UntouchedSurfaceCount
            BackendContradictionCount = $r.BackendContradictionCount
            Notes                     = $r.Notes
        }
    }
    $csvRows | Export-Csv -NoTypeInformation -Encoding utf8 -Path $Path
}

function Write-SweepReportMd {
    param(
        [Parameter(Mandatory)]$Rows,
        [Parameter(Mandatory)][string]$RunsRoot,
        [Parameter(Mandatory)][string]$Path
    )

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('# Playtest sweep report')
    [void]$lines.Add('')
    [void]$lines.Add('Sweep directory: ' + $RunsRoot)
    [void]$lines.Add('Generated: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    [void]$lines.Add('Runs aggregated: ' + $Rows.Count)
    [void]$lines.Add('')
    [void]$lines.Add('This report reads whatever tools/agent-playtest.ps1 actually wrote for each run.')
    [void]$lines.Add('Fields the driver build used for this sweep does not yet emit (persona, prompt hash,')
    [void]$lines.Add('coverage.json, backend.json) are named explicitly below rather than guessed at -- see')
    [void]$lines.Add('each run''s own Notes column in SUMMARY.csv, and the Data completeness section below.')
    [void]$lines.Add('')

    [void]$lines.Add('## Deepest day reached')
    [void]$lines.Add('')
    [void]$lines.Add((Get-DayElevenAnswer -Rows $Rows))
    [void]$lines.Add('')

    [void]$lines.Add('## Coverage union across the sweep')
    [void]$lines.Add('')
    $cov = Get-CoverageUnion -Rows $Rows
    if (-not $cov.Available) {
        [void]$lines.Add($cov.Note)
    } else {
        [void]$lines.Add('Total known surfaces: ' + $cov.TotalCount)
        [void]$lines.Add('Touched by at least one run: ' + $cov.TouchedCount)
        [void]$lines.Add('')
        [void]$lines.Add('Never touched by ANY run in this sweep (' + $cov.NeverTouched.Count + '), in full, never truncated:')
        [void]$lines.Add('')
        if ($cov.NeverTouched.Count -eq 0) {
            [void]$lines.Add('(none -- every known surface was touched by at least one run)')
        } else {
            foreach ($s in $cov.NeverTouched) { [void]$lines.Add('- ' + $s) }
        }
    }
    [void]$lines.Add('')

    [void]$lines.Add('## Findings that recur across runs')
    [void]$lines.Add('')
    [void]$lines.Add('Simple normalised-line match (trim, lowercase, collapse whitespace, strip one leading')
    [void]$lines.Add('bullet/number marker) across each run''s findings prose -- NOT semantic clustering. The')
    [void]$lines.Add('same complaint phrased two different ways will show up as two unrelated lines here;')
    [void]$lines.Add('read a run''s own findings.md for anything phrased differently.')
    [void]$lines.Add('')
    $runLineSets = @()
    foreach ($r in $Rows) {
        $f = $r.FindingsFields
        $txt = ''
        if ($f -and -not $f.Missing) { $txt = $f.Text }
        $runLineSets += [pscustomobject]@{ RunTag = $r.RunTag; Lines = (Get-CandidateFindingLines -Text $txt) }
    }
    $recur = Build-RecurrenceGroups -RunLines $runLineSets
    if ($recur.Count -eq 0) {
        [void]$lines.Add('No line-for-line match recurred across two or more distinct runs.')
    } else {
        foreach ($g in $recur) {
            $uniqueTags = @($g.RunTags | Select-Object -Unique)
            [void]$lines.Add('- (' + $uniqueTags.Count + ' runs: ' + ($uniqueTags -join ', ') + ') ' + $g.Example)
        }
    }
    [void]$lines.Add('')

    [void]$lines.Add('## Per-persona differences')
    [void]$lines.Add('')
    foreach ($l in (Get-PersonaDifferences -Rows $Rows)) { [void]$lines.Add($l) }
    [void]$lines.Add('')

    [void]$lines.Add('## INCOMPLETE / DEGRADED / FELL BACK / MISSING runs, named with cause')
    [void]$lines.Add('')
    $bad = Get-NamedBadRuns -Rows $Rows
    if ($bad.Count -eq 0) {
        [void]$lines.Add('None. Every run in this sweep completed clean.')
    } else {
        foreach ($b in $bad) { [void]$lines.Add('- ' + $b) }
    }
    [void]$lines.Add('')

    [void]$lines.Add('## Data completeness caveats')
    [void]$lines.Add('')
    [void]$lines.Add('Per-row detail is in SUMMARY.csv''s Notes column. Sweep-wide:')
    $missingCoverage = @($Rows | Where-Object { -not $_.CoverageData.Available }).Count
    $missingBackend = @($Rows | Where-Object { $null -eq $_.BackendContradictionCount }).Count
    $missingPersonaHash = @($Rows | Where-Object { -not $_.PromptHash }).Count
    [void]$lines.Add('- coverage percentage / untouched-surface count: unavailable for ' + $missingCoverage + ' of ' + $Rows.Count + ' run(s)')
    [void]$lines.Add('- backend contradiction count: unavailable for ' + $missingBackend + ' of ' + $Rows.Count + ' run(s)')
    [void]$lines.Add('- prompt hash (persona proof): unavailable for ' + $missingPersonaHash + ' of ' + $Rows.Count + ' run(s)')
    [void]$lines.Add('')

    Set-Content -Path $Path -Value ($lines -join [Environment]::NewLine) -Encoding utf8
}

function Invoke-SweepAggregation {
    param([Parameter(Mandatory)][string]$RunsRoot)

    $runDirs = @(Get-ChildItem -Path $RunsRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($runDirs.Count -eq 0) {
        Warn ('no run subdirectories found under ' + $RunsRoot + ' -- nothing to aggregate')
        return
    }
    $rows = @($runDirs | ForEach-Object { Get-RunSummaryRow -RunDir $_.FullName })

    $summaryPath = Join-Path $RunsRoot 'SUMMARY.csv'
    $reportPath = Join-Path $RunsRoot 'REPORT.md'
    Write-SweepSummaryCsv -Rows $rows -Path $summaryPath
    Write-SweepReportMd -Rows $rows -RunsRoot $RunsRoot -Path $reportPath

    $missingCount = @($rows | Where-Object { $_.Verdict -eq 'MISSING' }).Count
    $badCount = @($rows | Where-Object { $_.Verdict -ne 'CLEAN' -and $_.Verdict -ne 'MISSING' }).Count
    $cleanCount = $rows.Count - $missingCount - $badCount
    Say ('aggregated ' + $rows.Count + ' run(s): ' + $cleanCount + ' clean, ' + $badCount +
        ' DEGRADED/INCOMPLETE/FELL BACK, ' + $missingCount + ' missing findings.md')
    Say ('SUMMARY.csv: ' + $summaryPath)
    Say ('REPORT.md: ' + $reportPath)
}

# =================================================================================================
# LIVE LAUNCH -- needs Godot + ollama + VRAM, same preconditions as tools/agent-playtest.ps1
# itself. NOT exercised by tools/agent-playtest/tests/test-playtest-sweep.ps1 -- see that file's
# own header for exactly what it covers instead.
# =================================================================================================

function Invoke-SweepRun {
    param(
        [Parameter(Mandatory)]$PlanEntry,
        [Parameter(Mandatory)][string]$DriverPath,
        [Parameter(Mandatory)][string]$RunDir,
        [Parameter(Mandatory)][string]$RepoRootForRun,
        [string]$Model = 'llava:7b'
    )

    New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

    # Feature-detect -Persona via the driver's own parameter metadata (Get-Command, no execution)
    # rather than assuming it exists -- U4 is landing in parallel and may not be in this checkout
    # yet. See this file's header ("PERSONA IS A LABEL TODAY") for what happens when it is absent.
    $personaSupported = $false
    try {
        $cmd = Get-Command -Name $DriverPath -CommandType ExternalScript -ErrorAction Stop
        $personaSupported = $cmd.Parameters.ContainsKey('Persona')
    } catch { }

    $argList = New-Object System.Collections.ArrayList
    [void]$argList.Add('-NoProfile')
    [void]$argList.Add('-NonInteractive')
    [void]$argList.Add('-File')
    [void]$argList.Add($DriverPath)
    [void]$argList.Add('-Scope')
    [void]$argList.Add($PlanEntry.Scope)
    [void]$argList.Add('-Turns')
    [void]$argList.Add([string]$PlanEntry.Turns)
    [void]$argList.Add('-OutDir')
    [void]$argList.Add($RunDir)
    [void]$argList.Add('-Model')
    [void]$argList.Add($Model)
    [void]$argList.Add('-RepoRoot')
    [void]$argList.Add($RepoRootForRun)
    if ($personaSupported) {
        [void]$argList.Add('-Persona')
        [void]$argList.Add($PlanEntry.Persona)
    }

    $startedAt = Get-Date
    $proc = Start-Process -FilePath 'powershell' -ArgumentList $argList -NoNewWindow -PassThru -Wait
    $endedAt = Get-Date
    $exitCode = $proc.ExitCode

    $meta = [pscustomobject]@{
        tag                    = $PlanEntry.Tag
        scope                  = $PlanEntry.Scope
        persona                = $PlanEntry.Persona
        personaPassedToDriver  = $personaSupported
        turnsRequested         = $PlanEntry.Turns
        model                  = $Model
        exitCode               = $exitCode
        startedAt              = $startedAt.ToString('o')
        endedAt                = $endedAt.ToString('o')
        wallClockSeconds       = [math]::Round(($endedAt - $startedAt).TotalSeconds, 1)
    }
    ($meta | ConvertTo-Json) | Set-Content -Path (Join-Path $RunDir 'run-meta.json') -Encoding utf8
    return $meta
}

# =================================================================================================
# MAIN
# =================================================================================================

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
$RepoRoot = $RepoRoot.TrimEnd('\', '/')

# Same trap tools/agent-playtest.ps1 and tools/engine-test.ps1 both guard: the shared coordination
# root is stale (nobody checks it out to play), so a sweep launched there would burn a whole night
# measuring old code. Checked unconditionally, including -DryRun/-AggregateFrom -- cheap, and a
# dry run that validated a path it would never actually use is not a useful rehearsal.
if ($RepoRoot -ieq 'C:\Code\Game') {
    Die @(
        'that is the SHARED COORDINATION ROOT, which is stale and never the one to playtest.',
        'Use a worktree or C:\Code\Game\play, same rule tools/agent-playtest.ps1 enforces on itself.'
    )
}

$driverPath = Join-Path $RepoRoot 'tools\agent-playtest.ps1'
if (-not $AggregateFrom -and -not (Test-Path $driverPath)) {
    Die @(('driver not found at ' + $driverPath + ' -- run this from a real checkout.'))
}

if ($AggregateFrom) {
    if (-not (Test-Path $AggregateFrom)) { Die @(('aggregate target does not exist: ' + $AggregateFrom)) }
    $runsRoot = (Resolve-Path $AggregateFrom).Path
    Say ('aggregating existing runs under ' + $runsRoot + ' -- no new runs launched (Godot/ollama untouched)')
    Invoke-SweepAggregation -RunsRoot $runsRoot
    exit 0
}

$Scopes = Split-CommaList -Values $Scopes
$Personas = Split-CommaList -Values $Personas

$knownScopes = @('Full', 'Diff', 'Scout')
$badScopes = @($Scopes | Where-Object { $knownScopes -notcontains $_ })
if ($badScopes.Count -gt 0) {
    Die @(
        ('unknown -Scopes value(s): ' + ($badScopes -join ', ') + '. Valid: ' + ($knownScopes -join ', ') + '.'),
        'Failing loudly here rather than silently dropping or defaulting -- same principle the driver''s',
        'own -Persona validation (U4) is built on: an unknown value is a mistake to report, not to guess past.'
    )
}
if ($Scopes.Count -eq 0) { Die @('no -Scopes left after parsing -- nothing to sweep.') }
if ($Personas.Count -eq 0) { Die @('no -Personas left after parsing -- nothing to sweep.') }

$plan = Get-RunPlan -Runs $Runs -Scopes $Scopes -Personas $Personas -Turns $Turns

if ($DryRun) {
    # Plain pipeline output (not Write-Host) so a caller capturing this process's stdout -- exactly
    # what tools/agent-playtest/tests/test-playtest-sweep.ps1 does -- gets it reliably. Nothing
    # after this point runs: no directory is created, no process is launched.
    Format-RunMatrix -Plan $plan
    exit 0
}

if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'runs\playtest' }
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$stampDir = Join-Path $OutDir $stamp
New-Item -ItemType Directory -Path $stampDir -Force | Out-Null
Say ('sweep output: ' + $stampDir + ' (' + $plan.Count + ' run(s) planned)')

# SERIAL BY CONSTRUCTION. Do not parallelize this loop -- see this file's own header for why (one
# Godot client, one resident vision model, one GPU). A future reader who wants a faster sweep
# should ask for more GPUs, not a runspace pool here.
foreach ($entry in $plan) {
    $runDir = Join-Path $stampDir $entry.Tag
    Say ('run ' + $entry.Tag + ' (' + $entry.Scope + ' / ' + $entry.Persona + ') -- turn budget ' + $entry.Turns)
    Invoke-SweepRun -PlanEntry $entry -DriverPath $driverPath -RunDir $runDir -RepoRootForRun $RepoRoot -Model $Model | Out-Null
}

Invoke-SweepAggregation -RunsRoot $stampDir
Say ('sweep complete: ' + $stampDir)
exit 0
