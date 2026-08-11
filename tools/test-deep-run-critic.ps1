<#
.SYNOPSIS
    Proves tools/deep-run-critic.ps1's (S3) pure logic without ollama, VRAM, or a real completed run.

.DESCRIPTION
    Same shape as tools/test-agent-playtest-modes.ps1: dot-source the real logic
    (tools/agent-playtest/critic.ps1), feed it synthetic fixture text/files standing in for a
    completed run's own artifacts, assert on the real output, print PASS/FAIL. No mocking framework,
    no Pester -- this repo does not use one.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it tests.

.EXAMPLE
    powershell -File tools/test-deep-run-critic.ps1
#>

$toolsDir = $PSScriptRoot

$failures = New-Object System.Collections.ArrayList
$passes = 0

function Check {
    param([bool]$Condition, [string]$Description)
    if ($Condition) {
        $script:passes++
    } else {
        [void]$script:failures.Add($Description)
    }
}

# --- 1. AST parse check -----------------------------------------------------------------------
$parseTargets = @(
    (Join-Path $toolsDir 'deep-run-critic.ps1'),
    (Join-Path $toolsDir 'agent-playtest\critic.ps1')
)
foreach ($target in $parseTargets) {
    $tokens = $null
    $parseErrors = $null
    if (-not (Test-Path $target)) {
        Check $false ('parse: ' + $target + ' does not exist')
        continue
    }
    [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors) | Out-Null
    Check ($parseErrors.Count -eq 0) ('parse: ' + $target + ' has ' + $parseErrors.Count + ' syntax error(s): ' +
        (($parseErrors | ForEach-Object { $_.Message }) -join ' | '))
}

. (Join-Path $toolsDir 'agent-playtest\critic.ps1')

# --- 2. Get-DeepRunDigest: under budget is returned whole, untouched -----------------------------
$smallLog = "# Agent playtest turn log`r`n`r`n## Turn 1`r`n- day 1 phase Morning beat Idle`r`n`r`n## Turn 2`r`n- day 1 phase Evening beat Idle`r`n"
$smallDigest = Get-DeepRunDigest -RawTurnLog $smallLog -MaxChars 100000
Check ($smallDigest.Thinned -eq $false) 'a turn log under the char budget must not be thinned'
Check ($smallDigest.Text -eq $smallLog) 'an untouched digest must equal the raw log verbatim (byte for byte)'
Check ($smallDigest.DayCount -eq 2) ('sanity: the small fixture spans 2 days (preamble + day 1), got ' + $smallDigest.DayCount)

# --- 3. Get-DeepRunDigest: over budget thins a long day's middle, keeps every day ----------------
$bigLogBuilder = New-Object System.Text.StringBuilder
[void]$bigLogBuilder.Append("# Agent playtest turn log`r`n`r`n")
for ($day = 1; $day -le 3; $day++) {
    for ($t = 1; $t -le 30; $t++) {
        $turnNum = (($day - 1) * 30) + $t
        [void]$bigLogBuilder.Append('## Turn ' + $turnNum + "`r`n")
        [void]$bigLogBuilder.Append('- day ' + $day + ' phase Morning beat Idle -- padding text to grow this fixture past a small char budget so the thinning path actually triggers, over and over' + "`r`n`r`n")
    }
}
$bigLog = $bigLogBuilder.ToString()
$bigDigest = Get-DeepRunDigest -RawTurnLog $bigLog -MaxChars 3000 -MinBlocksKeptPerDay 4
Check ($bigDigest.Thinned -eq $true) 'a turn log over the char budget must report Thinned=true'
Check ($bigDigest.DayCount -eq 4) ('every real day (3) plus the preamble''s own bucket must be represented, got ' + $bigDigest.DayCount)
Check ($bigDigest.Text.Length -lt $bigLog.Length) 'a thinned digest must be shorter than the raw log'
Check ($bigDigest.Text -like '*Turn 1*') 'day 1''s FIRST turn must survive thinning'
Check ($bigDigest.Text -like '*Turn 30*') 'day 1''s LAST turn must survive thinning'
Check ($bigDigest.Text -like '*Turn 90*') 'day 3''s LAST turn must survive thinning (never drop the last day)'
Check ($bigDigest.Text -like '*omitted for length*') 'a thinned day must say how many turns were omitted'

# --- 4. Format-CriticFrictionLines / Format-CriticDecisionLines ---------------------------------
Check (((Format-CriticFrictionLines -Entries @()) -join '') -like '*no friction entries*') 'an empty friction list must say so plainly, not print nothing'
$frictionFixture = @(
    [pscustomobject]@{ Turn = 5; Day = 2; Phase = 'Morning'; Category = 'refused'; Trying = 'stock the item'; Detail = "refused: 'Stock_9' is disabled" }
)
$frictionLines = Format-CriticFrictionLines -Entries $frictionFixture
Check (($frictionLines -join ' ') -like '*turn 5*day 2*Morning*refused*stock the item*Stock_9*') ('every friction field must appear in the formatted line, got: ' + ($frictionLines -join ' | '))

Check (((Format-CriticDecisionLines -Entries @()) -join '') -like '*no six-decisions entries*') 'an empty decision list must say so plainly'
$decisionFixture = @(
    [pscustomobject]@{ Day = 1; Decision = 'sell the good one or hold it'; Choice = 'sell'; Why = 'stocked it' }
    [pscustomobject]@{ Day = 2; Decision = 'sell the good one or hold it'; Choice = 'hold'; Why = 'held it back' }
    [pscustomobject]@{ Day = 1; Decision = 'send the runner or trust their judgment'; Choice = 'send'; Why = 'sent them' }
)
$decisionLines = Format-CriticDecisionLines -Entries $decisionFixture
Check (@($decisionLines).Count -eq 2) ('two distinct decision NAMES must produce two lines, got ' + @($decisionLines).Count)
$sellLine = $decisionLines | Where-Object { $_ -like '*sell the good one or hold it*' }
Check ($null -ne $sellLine -and $sellLine -like '*sell x1*' -and $sellLine -like '*hold x1*') ('a decision resolved BOTH ways must show both choices with their own counts, got [' + $sellLine + ']')

# --- 5. Get-CriticUnsupportedTokens: the fabrication guard ---------------------------------------
# Deliberately SCREAMING_CASE examples, matching the regex agent-playtest.ps1's own guard uses
# (mixed-case control names like "CommissionAccept_7" are NOT what this narrow check catches --
# same scope as the live driver's own guard, reused as-is rather than widened here).
$haystack = 'the screen showed OFFERED thirteen times and PLUNGE NOW was on screen'
$cleanVerdict = 'I saw OFFERED on screen and it said PLUNGE.'
$cleanUnsupported = Get-CriticUnsupportedTokens -VerdictText $cleanVerdict -Haystack $haystack
Check ($cleanUnsupported.Count -eq 0) ('tokens that DO appear in the haystack must never be flagged, got [' + ($cleanUnsupported -join ',') + ']')

$fabricatedVerdict = 'The screen said OFFRED, which should read OFFERED, and also showed QUENCHFAILED.'
$unsupported = Get-CriticUnsupportedTokens -VerdictText $fabricatedVerdict -Haystack $haystack
Check ($unsupported -contains 'OFFRED') ('a token NOT in the haystack must be flagged, got [' + ($unsupported -join ',') + ']')
Check ($unsupported -contains 'QUENCHFAILED') ('a fabricated SCREAMING_CASE token must be flagged, got [' + ($unsupported -join ',') + ']')
Check ($unsupported -notcontains 'OFFERED') 'a token that DOES appear (case-insensitively) in the haystack must not be flagged'

# --- 6. Read-CriticRunArtifacts: a real (temp) completed-run directory ---------------------------
$fixtureDir = Join-Path ([System.IO.Path]::GetTempPath()) ('critic-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDir -Force | Out-Null
try {
    Set-Content -Path (Join-Path $fixtureDir 'turnlog.md') -Value "# Agent playtest turn log`r`n`r`n## Turn 1`r`n- day 1 phase Morning beat Idle`r`n" -Encoding utf8
    $metricsFixture = @{
        PerDayEntropy = @()
        FrictionLog   = @(@{ Turn = 1; Day = 1; Phase = 'Morning'; Category = 'refused'; Trying = 'x'; Detail = 'y' })
        SixDecisions  = @(@{ Day = 1; Decision = 'honor the memorial'; Choice = 'honor'; Why = 'z' })
    }
    ($metricsFixture | ConvertTo-Json -Depth 8) | Set-Content -Path (Join-Path $fixtureDir 'metrics.json') -Encoding utf8
    New-Item -ItemType Directory -Path (Join-Path $fixtureDir 'frames') -Force | Out-Null
    Set-Content -Path (Join-Path $fixtureDir 'frames\turn0001.png') -Value 'not a real png, just a fixture' -Encoding utf8

    $artifacts = Read-CriticRunArtifacts -RunDir $fixtureDir
    Check ($artifacts.FullLog -like '*Turn 1*') 'Read-CriticRunArtifacts must read the real turnlog.md text'
    Check (@($artifacts.FrictionEntries).Count -eq 1) ('FrictionLog must round-trip from metrics.json, got ' + @($artifacts.FrictionEntries).Count)
    Check (@($artifacts.DecisionEntries).Count -eq 1) ('SixDecisions must round-trip from metrics.json, got ' + @($artifacts.DecisionEntries).Count)
    Check ($artifacts.FrameCount -eq 1) ('frame count must be read off the frames/ directory, got ' + $artifacts.FrameCount)

    # A run with no metrics.json at all (a non-pilot persona, or an older run) must degrade to empty
    # friction/decisions rather than throwing -- the turnlog alone is still a usable artifact.
    $noMetricsDir = Join-Path ([System.IO.Path]::GetTempPath()) ('critic-fixture-nometrics-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $noMetricsDir -Force | Out-Null
    try {
        Set-Content -Path (Join-Path $noMetricsDir 'turnlog.md') -Value "# Agent playtest turn log`r`n`r`n## Turn 1`r`n- day 1 phase Morning`r`n" -Encoding utf8
        $noMetricsArtifacts = Read-CriticRunArtifacts -RunDir $noMetricsDir
        Check (@($noMetricsArtifacts.FrictionEntries).Count -eq 0) 'a run with no metrics.json must degrade to zero friction entries, not throw'
        Check (@($noMetricsArtifacts.DecisionEntries).Count -eq 0) 'a run with no metrics.json must degrade to zero decision entries, not throw'
    } finally {
        Remove-Item -Path $noMetricsDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # A directory with no turnlog.md at all must THROW loudly -- never silently judge nothing.
    $emptyDir = Join-Path ([System.IO.Path]::GetTempPath()) ('critic-fixture-empty-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $emptyDir -Force | Out-Null
    try {
        $threw = $false
        try { Read-CriticRunArtifacts -RunDir $emptyDir | Out-Null } catch { $threw = $true }
        Check ($threw -eq $true) 'a directory with no turnlog.md must throw, never silently produce empty artifacts'
    } finally {
        Remove-Item -Path $emptyDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} finally {
    Remove-Item -Path $fixtureDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- 7. critic.md prompt file exists and asks the three required questions -----------------------
$criticPromptPath = Join-Path $toolsDir 'agent-playtest\prompts\critic.md'
Check (Test-Path $criticPromptPath) 'tools/agent-playtest/prompts/critic.md must exist'
if (Test-Path $criticPromptPath) {
    $criticPromptText = Get-Content $criticPromptPath -Raw
    Check ($criticPromptText -like '*Is this fun*') 'critic.md must ask whether the game is fun'
    Check ($criticPromptText -like '*match the game idea*') 'critic.md must ask whether the run matches the game idea'
    Check ($criticPromptText -like '*dead stretches*' -or $criticPromptText -like '*Dead stretches*') 'critic.md must ask about dead stretches'
    Check ($criticPromptText -like '*THE-GAME.md*') 'critic.md must be seeded from docs/design/THE-GAME.md, not invented from scratch'
    Check ($criticPromptText -like '*quoted*' -or $criticPromptText -like '*quote*') 'critic.md must carry the quote-everything discipline (fabrication guard depends on it)'
}

# --- Summary -------------------------------------------------------------------------------------
if ($failures.Count -gt 0) {
    Write-Host ('FAIL (' + $failures.Count + ' of ' + ($passes + $failures.Count) + '):')
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}
Write-Host ('PASS: deep-run-critic pure logic, ' + $passes + '/' + $passes + ' checks, no ollama/VRAM/completed-run needed.')
exit 0
