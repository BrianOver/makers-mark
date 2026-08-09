<#
.SYNOPSIS
    -Scope Scout's mechanical half (A5): launches the two detectors that already exist and folds
    their own reports into the scout run's findings.md.

.DESCRIPTION
    Deliberately separate from the judgement half (the scout-judge.md prompt, scored against the
    model's own turn log). Nothing here asks a model anything, and nothing here is new detection
    logic -- FullPlaytest.cs's EngineLogAnomalies scan and MotionBurst freeze detection, and
    Playtest3dRecorder's 3D verb-reachability map, were built already and simply were never wired
    into the same report a scouting run produces. This file is the wiring, not the detector.

    Both stages launch a REAL process (a windowed Godot client, then a dotnet test run that itself
    launches Godot for gdUnit) and are run ONE AT A TIME, sequentially, after the act loop's own
    client has fully exited -- this machine's gdUnit runtime cannot be shared across two concurrent
    runs (see tools/engine-test.ps1's own trap 1), and FullPlaytest is exactly that same kind of
    real launch, so the same rule applies to it.

    Every failure mode (timeout, missing binary, no report written, a build failure) is reported as
    text in the returned section rather than thrown -- a broken mechanical half must never cost the
    judgement half's findings, and per rule 10 (raw output outranks any harness) this reports the
    runner's own counts, never a computed verdict.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, no 2>&1 on a native executable -- same
    traps as agent-playtest.ps1, which dot-sources this file.
#>

function Invoke-MechanicalDetectors {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)][string]$Godot,
        [int]$TimeoutMinutes = 15
    )

    try {
        return Invoke-MechanicalDetectorsCore -RepoRoot $RepoRoot -OutDir $OutDir -Godot $Godot -TimeoutMinutes $TimeoutMinutes
    } catch {
        return '## Mechanical detectors' + [Environment]::NewLine + [Environment]::NewLine +
            'CRASHED before either detector finished: ' + $_.Exception.Message + [Environment]::NewLine +
            'This is a wiring failure in this script, not a finding about the game -- the judgement ' +
            'section above (if present) is unaffected.'
    }
}

function Invoke-MechanicalDetectorsCore {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)][string]$Godot,
        [int]$TimeoutMinutes = 15
    )

    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('## Mechanical detectors')
    [void]$lines.Add('')
    [void]$lines.Add('Not model-driven. Two checks that already existed, launched here and folded in:')
    [void]$lines.Add('FullPlaytest (engine-log anomalies + frozen-world motion checks across 5 real')
    [void]$lines.Add('campaign launches) and Playtest3dRecorder (which on-theme verbs actually have a')
    [void]$lines.Add('3D button -- the dead-surface map).')
    [void]$lines.Add('')

    # --- FullPlaytest: res://fullplaytest.tscn --------------------------------------------------
    [void]$lines.Add('### FullPlaytest (engine anomalies + motion-freeze)')
    [void]$lines.Add('')
    $fpOut = Join-Path $OutDir 'mechanical-fullplaytest'
    if (Test-Path $fpOut) { Remove-Item $fpOut -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Path $fpOut -Force | Out-Null

    $prevPlaytestOut = $env:PLAYTEST_OUT
    try {
        $env:PLAYTEST_OUT = $fpOut
        Say 'scout: launching FullPlaytest (5 real campaign launches; this takes a while)'
        $fpProc = Start-Process -FilePath $Godot -ArgumentList @('--path', (Join-Path $RepoRoot 'godot'), 'res://fullplaytest.tscn') -PassThru
        $fpExited = $fpProc.WaitForExit($TimeoutMinutes * 60 * 1000)
        if (-not $fpExited) {
            Warn 'scout: FullPlaytest exceeded its timeout -- killing it'
            try { $fpProc.Kill($true) } catch { try { $fpProc.Kill() } catch { } }
            $fpProc.WaitForExit(30000) | Out-Null
            [void]$lines.Add('TIMED OUT after ' + $TimeoutMinutes + ' minute(s) -- killed. No report to fold in.')
        } else {
            $reportPath = Join-Path $fpOut 'REPORT.md'
            if (Test-Path $reportPath) {
                [void]$lines.Add('Exit code: ' + $fpProc.ExitCode + ' (FullPlaytest exits nonzero when it')
                [void]$lines.Add('found anomalies -- that is the report below working, not this script failing)')
                [void]$lines.Add('')
                [void]$lines.Add((Get-Content $reportPath -Raw))
            } else {
                [void]$lines.Add('FullPlaytest exited (' + $fpProc.ExitCode + ') but wrote no REPORT.md at ' +
                    $reportPath + ' -- treat this as a failed run, not a clean one.')
            }
        }
    } catch {
        [void]$lines.Add('FullPlaytest could not be launched: ' + $_.Exception.Message)
    } finally {
        $env:PLAYTEST_OUT = $prevPlaytestOut
    }
    [void]$lines.Add('')

    # --- Playtest3dRecorder: dotnet test godot/tests, filtered ----------------------------------
    [void]$lines.Add('### Playtest3dRecorder (3D verb-reachability / dead-surface map)')
    [void]$lines.Add('')
    $tdReport = Join-Path $OutDir 'mechanical-3d-surface.md'
    $tdActiveReport = Join-Path $OutDir 'mechanical-3d-surface-active.md'
    $tdLog = Join-Path $OutDir 'mechanical-3d-test.log'
    $tdErrLog = Join-Path $OutDir 'mechanical-3d-test.err.log'
    foreach ($stale in @($tdReport, $tdActiveReport, $tdLog, $tdErrLog)) {
        if (Test-Path $stale) { Remove-Item $stale -Force -ErrorAction SilentlyContinue }
    }

    $prevTdOut = $env:PLAYTEST_3D_OUT
    $prevTdActiveOut = $env:PLAYTEST_3D_ACTIVE_OUT
    try {
        $env:PLAYTEST_3D_OUT = $tdReport
        $env:PLAYTEST_3D_ACTIVE_OUT = $tdActiveReport
        Say 'scout: running Playtest3dRecorder (dotnet test godot/tests, filtered)'
        $settings = Join-Path $RepoRoot '.runsettings'
        $testArgs = @('test', (Join-Path $RepoRoot 'godot\tests'), '--settings', $settings, '--nologo',
            '--filter', 'FullyQualifiedName~Playtest3dRecorder')
        $tdProc = Start-Process -FilePath 'dotnet' -ArgumentList $testArgs -NoNewWindow -PassThru `
            -RedirectStandardOutput $tdLog -RedirectStandardError $tdErrLog
        $tdExited = $tdProc.WaitForExit($TimeoutMinutes * 60 * 1000)
        if (-not $tdExited) {
            Warn 'scout: Playtest3dRecorder run exceeded its timeout -- killing it'
            try { $tdProc.Kill($true) } catch { try { $tdProc.Kill() } catch { } }
            $tdProc.WaitForExit(30000) | Out-Null
            [void]$lines.Add('TIMED OUT after ' + $TimeoutMinutes + ' minute(s) -- killed. No report to fold in.')
        } else {
            $tdText = ''
            if (Test-Path $tdLog) { $tdText = Get-Content $tdLog -Raw }
            $tdErrText = ''
            if (Test-Path $tdErrLog) { $tdErrText = Get-Content $tdErrLog -Raw }
            $combined = $tdText + [Environment]::NewLine + $tdErrText

            # Rule 10: quote the runner's own counts, never a wrapper's verdict. Sum every Failed:
            # line the same way tools/engine-test.ps1 does (a run can emit more than one).
            $failedSum = 0
            foreach ($m in [regex]::Matches($combined, 'Failed:\s+(\d+)')) { $failedSum += [int]$m.Groups[1].Value }
            $totalNums = @([regex]::Matches($combined, 'Total:\s+(\d+)') | ForEach-Object { [int]$_.Groups[1].Value })
            if ($totalNums.Count -gt 0) {
                $totalMax = ($totalNums | Measure-Object -Maximum).Maximum
                [void]$lines.Add('Test runner: Failed: ' + $failedSum + ', Total: ' + $totalMax +
                    ' (raw counts from ' + $tdLog + '; process exit code ' + $tdProc.ExitCode + ')')
            } else {
                [void]$lines.Add('Test runner produced no Failed:/Total: line -- exit code ' +
                    $tdProc.ExitCode + '. See ' + $tdLog + ' and ' + $tdErrLog + '.')
            }
            [void]$lines.Add('')

            $any = $false
            if (Test-Path $tdReport) {
                [void]$lines.Add((Get-Content $tdReport -Raw))
                $any = $true
            }
            if (Test-Path $tdActiveReport) {
                [void]$lines.Add((Get-Content $tdActiveReport -Raw))
                $any = $true
            }
            if (-not $any) {
                [void]$lines.Add('No report written to ' + $tdReport + ' or ' + $tdActiveReport +
                    ' -- the env vars were set but the test wrote nothing, which means it did not reach ' +
                    'WriteReport (build failure or a thrown exception). Check ' + $tdLog + '.')
            }
        }
    } catch {
        [void]$lines.Add('Playtest3dRecorder could not be run: ' + $_.Exception.Message)
    } finally {
        $env:PLAYTEST_3D_OUT = $prevTdOut
        $env:PLAYTEST_3D_ACTIVE_OUT = $prevTdActiveOut
    }

    return ($lines -join [Environment]::NewLine)
}
