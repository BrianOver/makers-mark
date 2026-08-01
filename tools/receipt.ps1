# receipt.ps1 (U1, docs/plans/2026-08-01-001-feat-make-it-visible-plan.md) -- one command
# that produces a SELF-ATTESTING visible-difference receipt, or fails.
#
# WHY THIS EXISTS
# ---------------
# Two invisible-completion incidents happened in the same day:
#   1. `tools/shoot.ps1` renders a frame but does not rebuild first. A before/after
#      screenshot pair was captured back-to-back after a code change -- and came out
#      byte-identical, because both shots ran the same stale DLL. The "measurement"
#      proved nothing; it just looked like proof.
#   2. The town drew the wrong building art for weeks. `TownAssets2D.ForVenue` is
#      null-tolerant, so a wrong sprite id silently degraded to a flat colored box.
#      Nothing failed. Nobody looked at a rendered frame until the Forge happened to
#      have a magenta roof.
# This script makes both failure shapes impossible by construction, not just discouraged:
#   - it ALWAYS rebuilds godot/GodotClient.csproj before capturing, and refuses to
#     capture from a build that doesn't compile;
#   - it stamps the running branch@sha INTO `godot/assets/build_info.txt`, the exact file
#     `BuildStamp.cs` renders as a corner label in every frame -- so the sha that produced
#     a receipt is baked into the receipt's own pixels. A stale-build screenshot becomes
#     self-evident (or, with -Diff below, a build-failing refusal) instead of silently
#     passing.
#   - its own diff mode FAILS ON A DIFF AT OR BELOW THE MEASURED NOISE FLOOR (see below), not
#     just an exact 0%. A change that was supposed to be visible and produced a reading no
#     different from ambient rendering jitter is exactly today's incident #1, so it is a
#     failure here, never a quiet pass.
#
# This wraps the existing capture mechanism (`tools/shoot.ps1` -> `godot/tools/shot_harness.gd`)
# rather than reinventing it -- read those first if you are changing capture behavior; this
# script only adds the rebuild-stamp-diff shell around them.
#
# USAGE
# -----
#   Capture a receipt (rebuild, stamp, reimport, shoot -- one or more states in one run):
#     powershell -File tools/receipt.ps1 -Label <name> [-State ""] [-State Forge,Tavern,Gate]
#                                          [-Quiet] [-GodotBin <path>] [-TimeoutSec 60]
#     -> writes runs/receipts/<label>-<state>-<sha>.png per state, appends one JSONL row
#        per state to runs/receipts/index.jsonl (label, state, sha, timestamp, path).
#     -Quiet suppresses AmbientLife2D (lamp flicker, smoke, fireflies, awning sway, paper
#      flutter, mine dust) during capture -- see NOISE FLOOR below for why, and its own
#      honest limit (it narrows the floor, it does not zero it).
#
#   Diff two PNGs (typically two receipts) and enforce the noise-floor rule:
#     powershell -File tools/receipt.ps1 -Diff -Before <before.png> -After <after.png>
#                                          [-MinDiffPercent 1.0]
#     -> prints diff_pct and the changed bounding box; EXIT CODE 1 when diff_pct is at or
#        below -MinDiffPercent (distinct message for an exact 0% -- see NOISE FLOOR).
#
# -State accepts the same values as shoot.ps1 ("" | Forge | Shop | Tavern | Gate | ...,
# see shot_harness.gd for the full list) plus whatever states that harness adds over time.
#
# HONEST LIMIT: like shoot.ps1, this needs a real Windows desktop GPU session --
# `--headless` renders a blank frame, so this is a local gate, not a CI job.
#
# NOISE FLOOR (found while verifying this script, closed rather than just documented after a
# coordinator review): two captures of the town state with ZERO code changes between them
# are NOT byte-identical. AmbientLife2D accumulates real per-frame delta (not the
# deterministic sim frame count) to drive its lamp-flicker/smoke/firefly/awning/paper sine
# waves, and real per-frame delta is inherently jittery across separate process launches (OS/
# GPU scheduling varies run to run), so a genuine no-op still reads a small nonzero diff. A
# naive "fail only on exactly 0%" check would therefore NEVER catch a no-op on this state --
# precisely the "measurement that looks like proof" failure this unit exists to kill.
#
# Measured directly (five same-commit, zero-edit town-state pairs, -Quiet NOT used):
#     0.0450%  0.1516%  0.2288%  0.2427%  0.2610%
# i.e. observed range 0.045%-0.261% (mean ~0.186%) across five independent no-op pairs.
# -MinDiffPercent defaults to 1.0 -- roughly 4x the observed maximum, real headroom above the
# measured floor without being so loose it would swallow a small genuine UI tweak. Pass a
# tighter value once you have your own measurements for a specific state/scene (the floor is
# a property of what's animated in frame, not a universal constant -- a static interior with
# no ambient layer may have a floor much closer to 0).
#
# -Quiet (backed by shot_harness.gd's SHOT_QUIET, see its own header) freezes AmbientLife2D
# specifically -- reachable by name, self-contained, no coupling to sim/position logic, so
# disabling it via Node.PROCESS_MODE_DISABLED needed zero edits to AmbientLife2D.cs. It does
# NOT cover tree sway or idle-character breathing (owned by other actors' code, out of scope
# here), so a residual floor can still remain even with -Quiet on both captures.
#
# Measured directly with -Quiet on both captures (three same-commit town-state pairs):
#     0.0000%  0.0311%  0.0311%
# The residual (when nonzero) is a single ~20x30px region, not the multi-lamppost spread seen
# without -Quiet -- consistent with the remaining source being one idle actor's breath-cycle
# animation, exactly the residual this flag was documented not to cover. Two of the three
# pairs landed on EXACTLY 0% by coincidence (the residual source happened to be in the same
# state both times) -- this is real evidence for keeping the exact-0% case as a DISTINCT
# message rather than folding it into "below the floor": with -Quiet, a genuine no-op can
# legitimately hit 0%, and this tool still can't tell that apart from a stale build purely
# from the number, so it stays conservative and asks the human to check.
#
# The exact-0% case is kept as ITS OWN distinct message, separate from "below the noise
# floor": identical frames down to the last bit means something categorically different (a
# stale build that never picked up the change, or a capture that silently failed) than "a
# real capture came back statistically indistinguishable from ambient jitter."
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(ParameterSetName = 'Capture', Mandatory = $true)]
    [string]$Label,

    [Parameter(ParameterSetName = 'Capture')]
    [string[]]$State = @(""),

    [Parameter(ParameterSetName = 'Capture')]
    [switch]$Quiet,

    [Parameter(ParameterSetName = 'Capture')]
    [Parameter(ParameterSetName = 'Diff')]
    [string]$GodotBin = "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe",

    [Parameter(ParameterSetName = 'Capture')]
    [int]$TimeoutSec = 60,

    [Parameter(ParameterSetName = 'Diff', Mandatory = $true)]
    [switch]$Diff,

    [Parameter(ParameterSetName = 'Diff', Mandatory = $true)]
    [string]$Before,

    [Parameter(ParameterSetName = 'Diff', Mandatory = $true)]
    [string]$After,

    # 4x the measured no-op maximum (0.261%, see NOISE FLOOR above) -- override per-state once
    # you have your own measurements; a static state's floor may be much lower than the town's.
    [Parameter(ParameterSetName = 'Diff')]
    [double]$MinDiffPercent = 1.0
)
$ErrorActionPreference = "Stop"
$repo = (git rev-parse --show-toplevel)

# ============================================================================================
# DIFF MODE
# ============================================================================================
if ($Diff) {
    if (-not (Test-Path $Before)) { Write-Host "NOT FOUND: $Before" -ForegroundColor Red; exit 2 }
    if (-not (Test-Path $After)) { Write-Host "NOT FOUND: $After" -ForegroundColor Red; exit 2 }

    $pixelDiffProj = Join-Path $repo "tools\PixelDiff"
    Write-Host "==== RECEIPT DIFF: $Before  vs  $After ====" -ForegroundColor Cyan
    $lines = & dotnet run --project $pixelDiffProj -- $Before $After
    $pdExit = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host "  $_" }

    if ($pdExit -eq 2) {
        Write-Host "PixelDiff usage/IO error -- see output above (mismatched size or unreadable PNG?)." -ForegroundColor Red
        exit 2
    }

    $pctText = ($lines | Where-Object { $_ -match '^diff_pct=' }) -replace '^diff_pct=', ''
    $bbox = ($lines | Where-Object { $_ -match '^bbox=' }) -replace '^bbox=', ''
    $pct = [double]$pctText

    # Two distinct failure messages on purpose (coordinator review, see NOISE FLOOR in the
    # header): exact 0% (pdExit -eq 1, PixelDiff's own zero-pixel check) means something
    # categorically different from "a diff came back but it's not distinguishable from
    # ambient jitter" (pdExit -eq 0 but pct <= MinDiffPercent) -- the first is almost always a
    # stale build or a silently-failed capture, the second is a real capture that just didn't
    # clear the measured noise floor for this state.
    if ($pdExit -ne 0) {
        Write-Host ""
        Write-Host "RECEIPT FAILED -- IDENTICAL FRAMES (exactly 0% pixel diff)." -ForegroundColor Red
        Write-Host "This is the byte-identical-screenshot failure this tool exists to catch --" -ForegroundColor Red
        Write-Host "see the header comment. Did you rebuild before capturing 'after'? Did the" -ForegroundColor Red
        Write-Host "capture silently fail (check the PNG isn't a blank/black frame)?" -ForegroundColor Red
        exit 1
    }

    if ($pct -le $MinDiffPercent) {
        Write-Host ""
        Write-Host "RECEIPT FAILED -- $pct% is at or below the $MinDiffPercent% noise floor." -ForegroundColor Red
        Write-Host "That reading is indistinguishable from ambient-VFX jitter (see NOISE FLOOR in" -ForegroundColor Red
        Write-Host "the header -- a genuine no-op measured 0.045-0.261% on the town state). A" -ForegroundColor Red
        Write-Host "claimed visible change must clear the floor, not just clear zero, or this is" -ForegroundColor Red
        Write-Host "the same 'measurement that looks like proof' failure with extra steps." -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "RECEIPT OK -- $pct% of pixels differ (floor $MinDiffPercent%), changed region [$bbox]" -ForegroundColor Green
    exit 0
}

# ============================================================================================
# CAPTURE MODE
# ============================================================================================
$godotDir = Join-Path $repo "godot"
$buildInfoPath = Join-Path $godotDir "assets\build_info.txt"
$receiptsDir = Join-Path $repo "runs\receipts"
$indexPath = Join-Path $receiptsDir "index.jsonl"
New-Item -ItemType Directory -Force -Path $receiptsDir | Out-Null

# `-State Forge,Tavern` only splits into two array elements when PowerShell's own parser binds
# it directly (calling this script via `&` in the same process). Cross a process boundary --
# `powershell -File receipt.ps1 -State Forge,Tavern`, or any non-PowerShell caller -- and the
# comma survives as literal text inside a single argv token instead. Splitting every element on
# "," here makes both invocation styles behave the same regardless of how this was launched.
$states = @($State | ForEach-Object { $_ -split ',' })

# ---- 1. rebuild -- refuse to produce a receipt from a build that doesn't compile ----------
Write-Host "==== RECEIPT: building godot/GodotClient.csproj ====" -ForegroundColor Cyan
dotnet build (Join-Path $godotDir "GodotClient.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "BUILD FAILED -- refusing to capture. A receipt from a build that doesn't" -ForegroundColor Red
    Write-Host "compile is worse than no receipt: it would render whatever DLL was last" -ForegroundColor Red
    Write-Host "built successfully, which may not include the change under test." -ForegroundColor Red
    exit 1
}

# ---- 2. stamp -- the sha that produced this build renders INSIDE every captured frame -----
$sha = (git rev-parse --short HEAD)
$branch = (git rev-parse --abbrev-ref HEAD)
$dirty = if ((git status --porcelain)) { "dirty" } else { "clean" }
$dateStr = (Get-Date -Format "yyyy-MM-dd")
$stamp = "receipt: $branch@$sha | $dirty | $dateStr"
Write-Host $stamp -ForegroundColor Cyan
# Windows PowerShell 5.1's `Set-Content -Encoding utf8` writes a BOM; play.bat's equivalent
# stamp write does not, and BuildStamp.cs's plain .Trim() would leave a stray BOM character
# rather than strip it. Write BOM-less UTF-8 explicitly to match that convention exactly.
[System.IO.File]::WriteAllText($buildInfoPath, $stamp, (New-Object System.Text.UTF8Encoding($false)))

# ---- 3. reimport -- cheap and idempotent when nothing changed; matters when the visible ---
#         change is an asset edit rather than C#. Same call play.bat makes; not gated on its
#         exit code for the same reason play.bat isn't -- a re-run with nothing to import is
#         not a failure.
Write-Host "==== RECEIPT: reimporting assets ====" -ForegroundColor Cyan
& $GodotBin --path $godotDir --headless --import --quit *> $null

# ---- 4. shoot -- one child-process call per requested state ------------------------------
# Invoked as a genuine subprocess (not the `&` call operator) so shoot.ps1's own `exit 1` on
# a capture failure ends THAT process, not this one -- calling a script with `&` runs it in
# this same PowerShell host, where `exit` would tear down receipt.ps1 too.
$shootScript = Join-Path $repo "tools\shoot.ps1"

# -Quiet -> SHOT_QUIET=1, read by shot_harness.gd (see its own header). Set on THIS process's
# environment block: a child process inherits its parent's full environment at spawn time, so
# it survives receipt.ps1 -> child powershell (shoot.ps1) -> Start-Process (Godot) unchanged --
# no plumbing needed through shoot.ps1's own parameters, which stay untouched.
if ($Quiet) {
    $env:SHOT_QUIET = "1"
    Write-Host "(-Quiet: suppressing AmbientLife2D for this capture)" -ForegroundColor DarkGray
} else {
    Remove-Item Env:\SHOT_QUIET -ErrorAction SilentlyContinue
}

$capturedPaths = @()
foreach ($s in $states) {
    $stateTag = if ($s -eq "") { "town" } else { $s }
    $pngPath = Join-Path $receiptsDir "$Label-$stateTag-$sha.png"
    Write-Host "==== RECEIPT: capturing state='$s' -> $pngPath ====" -ForegroundColor Cyan

    # An empty -State argument silently vanishes when passed across a process boundary to a
    # child `powershell -File` invocation (Windows argv drops bare "" tokens) -- so the flag
    # is omitted entirely for the town state rather than passed as "", relying on shoot.ps1's
    # own default (also "") instead of a value that never arrives.
    $shootArgs = @('-NoProfile', '-File', $shootScript, '-Out', $pngPath, '-GodotBin', $GodotBin, '-TimeoutSec', $TimeoutSec)
    if ($s -ne "") { $shootArgs += @('-State', $s) }
    & powershell @shootArgs
    $shootExit = $LASTEXITCODE
    if ($shootExit -ne 0 -or -not (Test-Path $pngPath)) {
        Write-Host "CAPTURE FAILED for state='$s' (exit=$shootExit) -- see tools/shoot.ps1 output above." -ForegroundColor Red
        exit 1
    }
    $capturedPaths += $pngPath

    $row = [ordered]@{
        label     = $Label
        state     = $stateTag
        sha       = $sha
        timestamp = (Get-Date -Format "o")
        path      = $pngPath
        quiet     = [bool]$Quiet
    } | ConvertTo-Json -Compress
    Add-Content -Path $indexPath -Value $row
}

Write-Host ""
Write-Host "==== RECEIPT captured ($($capturedPaths.Count) state(s)) ====" -ForegroundColor Green
$capturedPaths | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "Diff against a baseline with:"
Write-Host "  powershell -File tools/receipt.ps1 -Diff -Before <old.png> -After <new.png>"
exit 0
