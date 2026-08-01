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
#   - its own diff mode EXITS NON-ZERO ON A 0% PIXEL DIFF. A change that was supposed to
#     be visible and produced identical frames is exactly today's incident #1, so it is a
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
#                                          [-GodotBin <path>] [-TimeoutSec 60]
#     -> writes runs/receipts/<label>-<state>-<sha>.png per state, appends one JSONL row
#        per state to runs/receipts/index.jsonl (label, state, sha, timestamp, path).
#
#   Diff two PNGs (typically two receipts) and enforce the non-zero-diff rule:
#     powershell -File tools/receipt.ps1 -Diff -Before <before.png> -After <after.png>
#     -> prints diff_pct and the changed bounding box; EXIT CODE 1 when diff_pct is 0.
#
# -State accepts the same values as shoot.ps1 ("" | Forge | Shop | Tavern | Gate | ...,
# see shot_harness.gd for the full list) plus whatever states that harness adds over time.
#
# HONEST LIMIT: like shoot.ps1, this needs a real Windows desktop GPU session --
# `--headless` renders a blank frame, so this is a local gate, not a CI job.
#
# HONEST LIMIT #2 (found while verifying this script): two captures of the town state with
# ZERO code changes between them are NOT byte-identical -- ambient VFX (torch/lantern glow,
# particles) key off real elapsed time rather than the deterministic sim frame count, so a
# genuine no-op still shows a small nonzero diff (observed 0.04-0.25% on the town state
# across repeated same-code captures). The exact-0% check below still does its job -- it is
# a code path independently verified against two byte-identical PNGs -- but on an animated
# state, "some diff" is the expected floor, not proof a claimed change actually rendered.
# Sanity-check a receipt's bbox against where the change should visibly land, don't just
# read the percentage as pass/fail once it clears zero.
[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [Parameter(ParameterSetName = 'Capture', Mandatory = $true)]
    [string]$Label,

    [Parameter(ParameterSetName = 'Capture')]
    [string[]]$State = @(""),

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
    [string]$After
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

    $pct = ($lines | Where-Object { $_ -match '^diff_pct=' }) -replace '^diff_pct=', ''
    $bbox = ($lines | Where-Object { $_ -match '^bbox=' }) -replace '^bbox=', ''

    if ($pdExit -ne 0) {
        Write-Host ""
        Write-Host "RECEIPT FAILED -- 0% pixel diff between the two frames." -ForegroundColor Red
        Write-Host "A change claimed to be visible produced IDENTICAL frames. This is the exact" -ForegroundColor Red
        Write-Host "byte-identical-screenshot failure this tool exists to catch -- see the header" -ForegroundColor Red
        Write-Host "comment. Did you rebuild before capturing 'after'? Did the change touch" -ForegroundColor Red
        Write-Host "anything the captured state actually renders?" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "RECEIPT OK -- $pct% of pixels differ, changed region [$bbox]" -ForegroundColor Green
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
