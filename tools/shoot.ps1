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
#   -State: "" (town, default) | Forge | Shop | Tavern | Gate
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$State = "",
    [string]$GodotBin = "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe",
    [int]$TimeoutSec = 60
)
$ErrorActionPreference = "Stop"
$repo = (git rev-parse --show-toplevel)
$godot = Join-Path $repo "godot"

$env:SHOT_OUT = $Out
$env:SHOT_STATE = $State
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
