<#
.SYNOPSIS
    Runs the Godot engine test suite the only safe way, and refuses to report a green it cannot
    justify.

.DESCRIPTION
    Never call `dotnet test godot/tests` by hand. Every trap below was hit for real, most of them
    more than once, and each one produced a confident wrong answer rather than an error:

      1. TWO CONCURRENT RUNS. gdUnit's runner cannot reach its Godot runtime when another run holds
         it, so it DROPS every [RequireGodotRuntime] test and prints "Passed!" for the handful that
         remain. Measured 2026-08-03: "Passed: 87 ... Duration: 579 ms" - which is not a fast suite,
         it is a runtime that never connected. That output was read as data and two conclusions were
         drawn from it. This script refuses to start while any Godot process is alive.

      2. PIPING TO tail/head. A bash pipeline returns the LAST command's exit code, so
         `dotnet test ... | tail` reports success for a run that failed. This script never pipes;
         it redirects to a log and reads $LASTEXITCODE itself.

      3. A TRUNCATED RUN LOOKS GREEN. When the runtime dies mid-session the suite still prints
         "Passed!" for whatever finished. CI's floor (ENGINE_MIN_PASSED=300) is far below the real
         suite size, so a run that silently drops half of itself clears it. This script fails when
         Total is under -MinTests, and fails on the runtime's own death signatures regardless of
         what the summary line claims.

      4. READING THE STALE SHARED ROOT. C:\Code\Game is a coordination checkout nobody updates; on
         2026-08-03 it was ~130 PRs behind main. Building or reading source there silently measures
         old code. This script refuses to run against it.

    On success it writes a receipt naming the commit it verified, so a push can be checked against
    a real local run instead of a memory of one. CI is a gate, not a test loop.

    NOTE ON STYLE: this file is deliberately ASCII-only and here-string-free. Windows PowerShell 5.1
    reads a BOM-less UTF-8 file as ANSI, which turns any dash or quote into mojibake, and an indented
    here-string terminator is a parse error. Both bit this script on its first run. Keep it plain.

.PARAMETER RepoRoot
    Worktree to test. Defaults to the repo containing this script.

.PARAMETER MinTests
    Minimum Total the run must report. Default 780 (suite was 803 on 2026-08-03). Raise it as the
    suite grows; never lower it to make a run pass.

.PARAMETER Filter
    Optional dotnet test --filter. Sets -MinTests to 0, since a filtered run legitimately runs few
    tests - and therefore CANNOT prove the suite is healthy. Use for diagnosis only, never as the
    evidence that work is done.

.EXAMPLE
    .\tools\engine-test.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [int]$MinTests = 780,
    [string]$Filter
)

$ErrorActionPreference = 'Stop'

function Fail([string[]]$Lines) {
    Write-Host ''
    Write-Host ('ENGINE-TEST REFUSED: ' + $Lines[0]) -ForegroundColor Red
    foreach ($line in $Lines[1..($Lines.Count - 1)]) {
        if ($null -ne $line) { Write-Host $line -ForegroundColor Red }
    }
    exit 1
}

# --- Resolve the worktree -----------------------------------------------------------------------
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
$RepoRoot = $RepoRoot.TrimEnd('\', '/')

# Trap 4: the stale coordination checkout.
if ($RepoRoot -ieq 'C:\Code\Game') {
    Fail @(
        'that is the SHARED COORDINATION ROOT.',
        'Nobody checks it out, so it is stale (~130 PRs behind main on 2026-08-03).',
        'Testing there measures old code and passes.',
        'Run from a worktree instead: C:\Code\Game\.claude\worktrees\<slug>'
    )
}

if (-not (Test-Path (Join-Path $RepoRoot 'godot\tests'))) {
    Fail @(("no godot\tests under '" + $RepoRoot + "' - is that a worktree of this repo?"))
}

# --- Trap 1: serialization ----------------------------------------------------------------------
$live = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*Godot*' })
if ($live.Count -gt 0) {
    $ids = ($live | ForEach-Object { $_.ProcessName + ' (pid ' + $_.Id + ')' }) -join ', '
    Fail @(
        ([string]$live.Count + ' Godot process(es) already running: ' + $ids),
        '',
        'Two gdUnit runs cannot share a runtime. The second one DROPS every [RequireGodotRuntime]',
        'test and still prints "Passed!", which is indistinguishable from a real pass unless you',
        'check the count. Wait for the other run, or stop those processes if they are orphans.'
    )
}

$sha = (& git -C $RepoRoot rev-parse HEAD).Trim()
$shortSha = $sha.Substring(0, 8)
$logDir = Join-Path $RepoRoot '.claude\engine-test'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$log = Join-Path $logDir 'last-run.log'

Write-Host ('engine-test: ' + $RepoRoot + ' @ ' + $shortSha) -ForegroundColor Cyan
if ($Filter) {
    Write-Host ("engine-test: FILTERED ('" + $Filter + "') - this run cannot prove suite health.") -ForegroundColor Yellow
    $MinTests = 0
}
Write-Host 'engine-test: running (expect several minutes; do not start another run)' -ForegroundColor Cyan

# Trap 2: no pipeline. Redirect, then read the exit code ourselves.
$settings = Join-Path $RepoRoot '.runsettings'
$testArgs = @('test', (Join-Path $RepoRoot 'godot\tests'), '--settings', $settings, '--nologo')
if ($Filter) { $testArgs += @('--filter', $Filter) }

& dotnet $testArgs > $log 2>&1
$testExit = $LASTEXITCODE

$text = Get-Content $log -Raw
if (-not $text) { $text = '' }

# --- Trap 3: the run may have died while claiming success ----------------------------------------
$deathSignatures = @(
    'Connection interrupted by cancellation requested',
    'Test host process crashed',
    'ends with exit code: 139',
    'ends with exit code: -1073741819',
    'Failed to connect',
    'Rebuild Godot Project ends with exit code: -1'
)
$deaths = @()
foreach ($sig in $deathSignatures) {
    if ($text -like ('*' + $sig + '*')) { $deaths += $sig }
}

$totals = @()
foreach ($m in [regex]::Matches($text, 'Total:\s+(\d+)')) { $totals += [int]$m.Groups[1].Value }
$suiteTotal = 0
if ($totals.Count -gt 0) { $suiteTotal = ($totals | Measure-Object -Maximum).Maximum }

Write-Host ''
Write-Host ('engine-test: total=' + $suiteTotal + ' exit=' + $testExit + ' log=' + $log)

if ($deaths.Count -gt 0) {
    Fail @(
        'the Godot runtime DIED during this run.',
        'The summary line is not trustworthy even if it says "Passed!".',
        ('Signatures found: ' + ($deaths -join '; ')),
        '',
        'Do not re-run hoping it clears. Start by reading the orphan-node warnings in the log:',
        'this has twice been resource pressure in the shared runtime, and once a viewport nobody',
        'had disabled. See .runsettings for the diagnosis and docs/debugging.md section 2a.'
    )
}

if ($suiteTotal -lt $MinTests) {
    Fail @(
        ('only ' + $suiteTotal + ' tests ran, expected at least ' + $MinTests + '.'),
        'A short run that says "Passed!" is the most expensive failure mode in this repo:',
        'it hides the tests it never ran.',
        '',
        'If the suite genuinely shrank, change -MinTests deliberately in the same commit that',
        'removed the tests, with the new number in the message. Never lower it to turn a run green.'
    )
}

if ($testExit -ne 0) {
    Write-Host ''
    Write-Host ('engine-test: ' + $suiteTotal + ' tests ran and REAL failures were reported:') -ForegroundColor Red
    foreach ($f in [regex]::Matches($text, '(?m)^\s*Failed\s+(\S+)')) {
        Write-Host ('  ' + $f.Groups[1].Value) -ForegroundColor Red
    }
    Write-Host ''
    Write-Host ('This is an honest failure, not a truncation - the suite ran. Full log: ' + $log)
    exit 1
}

# --- Receipt: what a push is allowed to rely on --------------------------------------------------
# The receipt must record whether the tree was DIRTY. Otherwise it names a commit while what actually
# ran was uncommitted work, and a later "the receipt says this commit is green" is false in exactly
# the way this whole script exists to prevent. Ignore .import churn: Godot rewrites ~200 of those on
# every run, so their presence says nothing about the code under test.
$dirty = @(& git -C $RepoRoot status --porcelain | Where-Object { $_ -notmatch '\.import$' })
$treeState = 'clean'
if ($dirty.Count -gt 0) { $treeState = 'DIRTY (' + $dirty.Count + ' uncommitted file(s))' }

$receipt = Join-Path $logDir 'receipt.txt'
Set-Content -Path $receipt -Encoding utf8 -Value @(
    ('commit=' + $sha),
    ('tree=' + $treeState),
    ('total=' + $suiteTotal),
    ('worktree=' + $RepoRoot)
)

Write-Host ''
Write-Host ('engine-test: PASS - ' + $suiteTotal + ' tests, runtime healthy.') -ForegroundColor Green
if ($dirty.Count -gt 0) {
    Write-Host ('engine-test: tree was ' + $treeState + ' - this receipt does NOT vouch for ' + $shortSha) -ForegroundColor Yellow
    Write-Host '             alone. Commit, then re-run before treating it as push evidence.' -ForegroundColor Yellow
} else {
    Write-Host ('engine-test: receipt written for ' + $shortSha + ' (clean tree). A push may rely on it;') -ForegroundColor Green
    Write-Host '             CI is a gate, not a test loop.' -ForegroundColor Green
}
exit 0
