# shoot.ps1 (Track A, U1) -- capture one game state to a PNG via the GPU.
#
# Launches the Godot console exe NON-headless (windowed, minimized) running
# godot/tools/shot_harness.gd, which renders the requested state and saves a PNG.
# Windowed (not --headless) because the headless dummy driver cannot render a real
# frame; the viewport texture renders regardless of window visibility as long as
# this runs in a desktop session on the GPU. Wrapped in a timeout+Kill: the
# headless failure mode is an infinite hang, so we never wait forever.
#
# Usage: powershell -File tools/shoot.ps1 -Out C:\tmp\town.png [-State Tavern]
#   -State: "" (town, default) | Forge | Shop | Tavern | Gate | Counter | Watch
#   -State TavernScene / TavernSceneAtBar (P2-PEOPLE-01): the arc-scene row on a patron's card, and
#   the scene itself once pursued. Both set SHOT_ARC_SCENE below.
#   -State Watch (§11.14.7): a hand-built, already-resolved two-floor fight staged straight into
#   MineWatch (MainUi.StageWatchFightReceipt, gated on SHOT_WATCH_FIGHT below) -- the real day-cycle
#   route to a populated watch is unreliable to park a screenshot on (a fresh campaign's day-1 party
#   often resolves without ever staging, and the path there crosses the tutorial gate plus an
#   auto-opening Camp/Ledger modal), so this bypasses all of it.
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$State = "",
    [string]$GodotBin = "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe",
    [int]$TimeoutSec = 60
)
$ErrorActionPreference = "Stop"
$repo = (git rev-parse --show-toplevel)
$godot = Join-Path $repo "godot"

# P2-SCREEN-02: stamp godot/assets/build_info.txt with the running branch@sha BEFORE rendering,
# the same way receipt.ps1 does (shared code, tools/stamp-build-info.ps1 -- see its header). This
# script did not stamp at all before: a shoot.ps1-only capture (its own documented standalone
# usage, not only as receipt.ps1's child process) could carry a stale watermark naming whatever
# commit receipt.ps1 last happened to stamp in this worktree, not the one actually being
# rendered -- a measured, real wasted diagnosis.
. (Join-Path $repo "tools\stamp-build-info.ps1")
$stamp = Set-BuildInfoStamp -Repo $repo
Write-Host $stamp -ForegroundColor DarkGray

$env:SHOT_OUT = $Out
$env:SHOT_STATE = $State
$env:SHOT_WATCH_FIGHT = if ($State -eq "Watch") { "1" } else { "" }
# P2-PEOPLE-01: TavernScene / TavernSceneAtBar need one FACT planted before the tavern is opened --
# a player-marked piece in Torvald's hands (MainUi.StageArcSceneReceipt). The scene engine then
# decides for itself whether to offer, so the capture still proves the real eligibility rule rather
# than a staged screen. Same seam and same never-in-real-play contract as SHOT_WATCH_FIGHT above.
$env:SHOT_ARC_SCENE = if ($State -eq "TavernScene" -or $State -eq "TavernSceneAtBar") { "1" } else { "" }
if (Test-Path $Out) { Remove-Item $Out -Force }

Write-Host "capturing state='$State' -> $Out" -ForegroundColor Cyan
$p = Start-Process -FilePath $GodotBin `
    -ArgumentList '--path', $godot, '-s', 'tools/shot_harness.gd' `
    -WindowStyle Minimized -PassThru
if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    Write-Host "TIMEOUT after ${TimeoutSec}s -- killing (render hang?)" -ForegroundColor Red
    try { $p.Kill() } catch {}
    exit 1
}

if (-not (Test-Path $Out)) { Write-Host "NO PNG produced" -ForegroundColor Red; exit 1 }
$sz = (Get-Item $Out).Length
Write-Host "captured: $Out ($([int]($sz/1KB)) KB)" -ForegroundColor Green
if ($sz -lt 20KB) { Write-Host "WARNING: PNG suspiciously small -- possible black/empty frame (check desktop session)" -ForegroundColor Yellow }
