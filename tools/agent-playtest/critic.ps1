<#
.SYNOPSIS
    S3 (scripted-deep-pilot lane): pure logic for judging a COMPLETED deep run's artifacts, split out
    of tools/deep-run-critic.ps1 for the same reason scope-map.ps1/completion.ps1/metrics.ps1 are --
    this needs zero Godot, zero ollama, zero VRAM to prove (tools/test-agent-playtest-modes.ps1 tests
    it from fixture text), and the top-level script needs all three.

.DESCRIPTION
    The critic pass is a DIFFERENT shape from every judge call agent-playtest.ps1 makes live: those
    read a $turnRecords array built DURING a live loop; this reads whatever a COMPLETED run left on
    disk (turnlog.md's raw markdown, metrics.json's already-computed FrictionLog/SixDecisions for a
    pilot run). Build-PerDayJudgeDigest (metrics.ps1) cannot be reused as-is -- it needs structured
    records, not raw text -- so Get-DeepRunDigest below re-derives the same "keep every day, thin the
    middle of a long one" idea by parsing turnlog.md's own "## Turn N" / "- day X phase Y ..." lines
    back into day-grouped blocks.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# Reads whichever of a completed run's artifacts exist, tolerating any that do not (a run this
# script is pointed at might predate FrictionLog/SixDecisions, or might not be a pilot run at all --
# every other persona's metrics.json simply lacks those two properties, which is a valid, expected
# shape, not an error).
function Read-CriticRunArtifacts {
    param([Parameter(Mandatory)][string]$RunDir)

    $turnlogPath = Join-Path $RunDir 'turnlog.md'
    $metricsJsonPath = Join-Path $RunDir 'metrics.json'
    $findingsPath = Join-Path $RunDir 'findings.md'
    $framesDir = Join-Path $RunDir 'frames'

    if (-not (Test-Path $turnlogPath)) {
        throw ('no turnlog.md at ' + $turnlogPath + ' -- this does not look like a completed agent-playtest run.')
    }

    $fullLog = Get-Content $turnlogPath -Raw

    $frictionEntries = @()
    $decisionEntries = @()
    if (Test-Path $metricsJsonPath) {
        try {
            $metrics = Get-Content $metricsJsonPath -Raw | ConvertFrom-Json
            if ($metrics.PSObject.Properties.Name -contains 'FrictionLog') { $frictionEntries = @($metrics.FrictionLog) }
            if ($metrics.PSObject.Properties.Name -contains 'SixDecisions') { $decisionEntries = @($metrics.SixDecisions) }
        } catch {
            # A malformed metrics.json degrades to "no friction/decision evidence" rather than
            # aborting the whole critic pass -- the turnlog alone is still a real, usable artifact.
        }
    }

    $frameCount = 0
    if (Test-Path $framesDir) {
        $frameCount = @(Get-ChildItem -Path $framesDir -Filter '*.png' -ErrorAction SilentlyContinue).Count
    }

    $findingsHeader = @()
    if (Test-Path $findingsPath) {
        $findingsHeader = @(Get-Content -Path $findingsPath -TotalCount 20)
    }

    return [pscustomobject]@{
        FullLog         = $fullLog
        FrictionEntries = $frictionEntries
        DecisionEntries = $decisionEntries
        FrameCount      = $frameCount
        FindingsHeader  = $findingsHeader
    }
}

# Splits turnlog.md's raw text on its own "## Turn N" headers and reads each block's own
# "day X phase Y" line to group by day -- the digest keeps EVERY day's first and last block in full
# and thins a long day's middle toward a floor (never drops a day outright), same spirit as
# Build-PerDayJudgeDigest (metrics.ps1) without needing that function's structured $TurnRecords.
function Get-DeepRunDigest {
    param(
        [Parameter(Mandatory)][string]$RawTurnLog,
        [int]$MaxChars = 32000,
        [int]$MinBlocksKeptPerDay = 4
    )

    $blocks = [regex]::Split($RawTurnLog, '(?=^## Turn \d+)', 'Multiline') | Where-Object { $_.Trim().Length -gt 0 }
    if ($blocks.Count -eq 0) {
        return [pscustomobject]@{ Text = $RawTurnLog; DayCount = 0; Thinned = $false }
    }

    # Keys are STRINGS ("1", "2", ...), never bare ints: an [ordered] dictionary resolves an Int32
    # key to its OWN int-indexed positional accessor instead of a hashtable-style key lookup (a
    # documented PowerShell/.NET OrderedDictionary trap), which threw ArgumentOutOfRangeException
    # the moment a day number did not already exist as a valid position. A string key never hits
    # that overload.
    $byDay = [ordered]@{}
    foreach ($block in $blocks) {
        $dayMatch = [regex]::Match($block, 'day\s+(\d+)')
        $dayNum = if ($dayMatch.Success) { [int]$dayMatch.Groups[1].Value } else { -1 }
        $dayKey = [string]$dayNum
        if (-not $byDay.Contains($dayKey)) { $byDay[$dayKey] = New-Object System.Collections.ArrayList }
        [void]$byDay[$dayKey].Add($block)
    }

    $fullText = ($blocks -join '')
    if ($fullText.Length -le $MaxChars) {
        return [pscustomobject]@{ Text = $fullText; DayCount = $byDay.Count; Thinned = $false }
    }

    $sb = New-Object System.Text.StringBuilder
    foreach ($day in $byDay.Keys) {
        $dayBlocks = $byDay[$day]
        if ($dayBlocks.Count -le $MinBlocksKeptPerDay) {
            foreach ($b in $dayBlocks) { [void]$sb.Append($b) }
            continue
        }

        $half = [math]::Max(1, [math]::Floor($MinBlocksKeptPerDay / 2))
        $head = $dayBlocks | Select-Object -First $half
        $tail = $dayBlocks | Select-Object -Last $half
        $omitted = $dayBlocks.Count - ($head.Count + $tail.Count)
        foreach ($b in $head) { [void]$sb.Append($b) }
        [void]$sb.Append([Environment]::NewLine + '(' + $omitted + ' turn(s) omitted for length)' + [Environment]::NewLine)
        foreach ($b in $tail) { [void]$sb.Append($b) }
    }

    return [pscustomobject]@{ Text = $sb.ToString(); DayCount = $byDay.Count; Thinned = $true }
}

# Plain, readable lines for the judge's own user text -- one entry per friction/decision record,
# quoting whatever the pilot's own Detail/Why text already carries (never re-summarized, so the
# fabrication guard's haystack check stays meaningful against the SAME text the model is shown).
function Format-CriticFrictionLines {
    param([array]$Entries)

    if (-not $Entries -or $Entries.Count -eq 0) {
        return @('(no friction entries recorded for this run)')
    }

    return @($Entries | ForEach-Object {
        '- turn ' + $_.Turn + ', day ' + $_.Day + ', ' + $_.Phase + ', [' + $_.Category + '] trying: ' +
            $_.Trying + ' -- ' + $_.Detail
    })
}

function Format-CriticDecisionLines {
    param([array]$Entries)

    if (-not $Entries -or $Entries.Count -eq 0) {
        return @('(no six-decisions entries recorded for this run)')
    }

    $byDecision = $Entries | Group-Object -Property Decision
    return @($byDecision | ForEach-Object {
        $choices = $_.Group | Group-Object -Property Choice | ForEach-Object { $_.Name + ' x' + $_.Count }
        '- ' + $_.Name + ': ' + ($choices -join ', ') + ' (' + $_.Count + ' total)'
    })
}

# Same fabrication-guard shape agent-playtest.ps1's own end-of-run check uses (SCREAMING_CASE /
# control-name-shaped tokens are what a judge quotes verbatim and what can be mechanically verified;
# ordinary prose is left alone on purpose, same reasoning as the live driver's own guard).
function Get-CriticUnsupportedTokens {
    param(
        [Parameter(Mandatory)][string]$VerdictText,
        [Parameter(Mandatory)][string]$Haystack
    )

    $upperHaystack = $Haystack.ToUpperInvariant()
    $unsupported = New-Object System.Collections.Generic.List[string]
    $quoted = [regex]::Matches($VerdictText, '(?<![A-Za-z0-9_])[A-Z][A-Z0-9_]{3,}(?![A-Za-z0-9_])')
    foreach ($m in $quoted) {
        $token = $m.Value
        if ($upperHaystack.Contains($token)) { continue }
        if ($unsupported.Contains($token)) { continue }
        [void]$unsupported.Add($token)
    }

    return ,@($unsupported)
}
