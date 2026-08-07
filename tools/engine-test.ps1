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
    [string]$Filter,

    # How long to WAIT for another run to finish before giving up. The machine is serialized (one
    # gdUnit runtime at a time), and callers used to be told "wait and retry" in prose -- which meant
    # agents sat idle for hours doing nothing while a run they could not see held the machine. The
    # waiting belongs here, bounded and visible, so a caller either gets the machine or gets told to
    # come back with a real deadline attached. 0 means fail immediately if the machine is busy.
    [int]$MaxWaitMinutes = 10,

    # Hard cap on the run itself. A healthy full run is ~4-7 minutes on this machine; a STALLED one
    # has been measured at ~9m48s before CI cancelled it. Without a cap, one stalled host holds the
    # machine indefinitely and every other caller's wait expires for no reason.
    [int]$RunTimeoutMinutes = 20
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

# --- Trap 1: serialization, with a bounded wait --------------------------------------------------
# Two gdUnit runs cannot share a runtime. The second one DROPS every [RequireGodotRuntime] test and
# still prints "Passed!", which is indistinguishable from a real pass unless you check the count.
#
# The wait is bounded and reported. Unbounded waiting is what actually cost hours: callers were told
# in prose to "wait and retry", so they idled with no deadline and no visibility into who held the
# machine. Now the script waits, says how long it has waited, and quits with a real answer.
function GodotProcesses {
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*Godot*' })
}

$waitDeadline = (Get-Date).AddMinutes($MaxWaitMinutes)
$announcedWait = $false
while ($true) {
    $live = GodotProcesses
    if ($live.Count -eq 0) { break }

    $ids = ($live | ForEach-Object { $_.ProcessName + ' (pid ' + $_.Id + ')' }) -join ', '
    if ((Get-Date) -ge $waitDeadline) {
        Fail @(
            ([string]$live.Count + ' Godot process(es) still running after waiting ' + $MaxWaitMinutes + ' minute(s): ' + $ids),
            '',
            'The machine is serialized: a second gdUnit run silently drops every runtime test.',
            'This is a real answer, not a hint to keep waiting -- do NOT sit in a retry loop.',
            'Either come back later, or if those processes are orphans from a dead run, stop them:',
            '  Get-Process Godot* | Stop-Process -Force'
        )
    }

    if (-not $announcedWait) {
        Write-Host ('engine-test: machine busy (' + $ids + '); waiting up to ' + $MaxWaitMinutes + ' min') -ForegroundColor Yellow
        $announcedWait = $true
    }
    Start-Sleep -Seconds 15
}
if ($announcedWait) {
    Write-Host 'engine-test: machine free, proceeding' -ForegroundColor Cyan
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

# TRAP 5, and this one made the whole script LIE on failure.
#
# The original line was `& dotnet $testArgs > $log 2>&1`. In Windows PowerShell 5.1, redirecting a
# native process's stderr inside PowerShell wraps each line in an ErrorRecord -- and with
# $ErrorActionPreference='Stop' set at the top of this script, the FIRST stderr line becomes a
# script-terminating NativeCommandError. A genuine test failure writes its assertion message to
# stderr, so the script died at this line with a 31-line log containing only build output, no
# summary, no failed-test names. The honest-failure reporting further down never ran.
#
# Diagnosed the hard way: two full runs of a branch with 7 real failures reported only
# "dotnet.exe : Expecting be equal:" and were misread as a stale test runner. Running
# `dotnet test` directly, outside PowerShell, produced the real summary immediately.
#
# Start-Process redirects at the OS level, so stderr never becomes a PowerShell error object. It also
# gives a killable handle, which is what makes RunTimeoutMinutes possible at all.
$errLog = Join-Path $logDir 'last-run.err.log'
$proc = Start-Process -FilePath 'dotnet' -ArgumentList $testArgs -NoNewWindow -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError $errLog

$timedOut = $false
if (-not $proc.WaitForExit($RunTimeoutMinutes * 60 * 1000)) {
    $timedOut = $true
    Write-Host ''
    Write-Host ('engine-test: run exceeded ' + $RunTimeoutMinutes + ' min - killing it so it stops holding the machine') -ForegroundColor Red
    try { $proc.Kill($true) } catch { try { $proc.Kill() } catch { } }
    $proc.WaitForExit(30000) | Out-Null
    # A stalled test host leaves Godot children behind; they would block every later run.
    foreach ($stray in GodotProcesses) {
        try { Stop-Process -Id $stray.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
}
# Read the exit code only after a full (argument-less) WaitForExit. WaitForExit(ms) returning true
# does NOT guarantee the process object has its ExitCode populated -- the first version of this
# printed "exit=" empty, and since empty -ne 0 is TRUE in PowerShell, a genuinely GREEN run would
# have been reported as a failure. Defaulting to a nonzero sentinel keeps the honest direction: if we
# cannot read the code, do not claim success.
$testExit = 99
if (-not $timedOut) { $proc.WaitForExit() }
try { $testExit = [int]$proc.ExitCode } catch { $testExit = 99 }

# stdout and stderr are separate files now, so the analysis below must read BOTH -- the failure
# messages this script reports live in stderr.
$text = ''
if (Test-Path $log) { $text = (Get-Content $log -Raw) }
if (-not $text) { $text = '' }
$errText = ''
if (Test-Path $errLog) { $errText = (Get-Content $errLog -Raw) }
if ($errText) {
    $text = $text + "`n" + $errText
    Add-Content -Path $log -Encoding utf8 -Value @('', '--- stderr ---', '', $errText)
}

if ($timedOut) {
    Fail @(
        ('the run was KILLED after ' + $RunTimeoutMinutes + ' minutes.'),
        'A healthy full run takes about 4-7 minutes here, so this was a stall, not slowness.',
        'Any Godot strays it left behind have been stopped, so the machine is free for the next run.',
        ('Read the log before re-running: ' + $log),
        'Start with the orphan-node warnings - a stall has twice been resource pressure in the',
        'shared runtime. See .runsettings and docs/debugging.md section 2a.'
    )
}
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

# --- Trap 4: the runner can report failures AND still exit 0 -------------------------------------
# Observed 2026-08-07 on a real wave: the summary line read "Failed: 5, Passed: 940, Total: 945"
# and the process exit code was 0. This script's ONLY failure check was `$testExit -ne 0`, so it
# printed "PASS - 945 tests, runtime healthy", wrote a receipt, and the failures were reported to
# the owner as green twice before CI caught them.
#
# That is the same mistake this file's own header warns about for Total: trusting one number the
# runner happens to emit instead of reading what actually happened. The count is authoritative and
# the exit code is advisory -- never the other way round. Sum every suite's Failed: (a run can emit
# more than one summary line) and refuse on any nonzero, whatever the process claimed.
$failedTotal = 0
foreach ($m in [regex]::Matches($text, 'Failed:\s+(\d+)')) { $failedTotal += [int]$m.Groups[1].Value }

if ($failedTotal -gt 0 -or $testExit -ne 0) {
    Write-Host ''
    Write-Host ('engine-test: ' + $suiteTotal + ' tests ran and REAL failures were reported (Failed: ' +
        $failedTotal + ', exit=' + $testExit + '):') -ForegroundColor Red
    foreach ($f in [regex]::Matches($text, '(?m)^\s*Failed\s+(\S+)')) {
        Write-Host ('  ' + $f.Groups[1].Value) -ForegroundColor Red
    }
    if ($failedTotal -gt 0 -and $testExit -eq 0) {
        Write-Host ''
        Write-Host ('NOTE: the runner exited 0 despite ' + $failedTotal + ' failure(s). The count is ' +
            'what counts; the exit code lied.') -ForegroundColor Yellow
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
