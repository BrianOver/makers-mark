# play.ps1 — the ONLY sanctioned way to launch the playtest build.
# Guarantees you never test a stale checkout: rebuilds + reimports the CURRENT dev
# build and prints its provenance before launch. Root-cause fix for the 2026-07-21
# "interactive professions were invisible" incident (a forked play checkout was never synced).
# See docs/design/build-provenance-and-never-lost.md
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/play.ps1
param(
    [string]$GodotBin = "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe",
    [switch]$NoImport
)
$ErrorActionPreference = "Stop"
$repo = (git rev-parse --show-toplevel)
$godot = Join-Path $repo "godot"

# --- provenance stamp (what you are about to run) ---
$branch = (git rev-parse --abbrev-ref HEAD)
$sha    = (git rev-parse --short HEAD)
$date   = (git show -s --format=%ci HEAD)
$dirty  = if ((git status --porcelain)) { "DIRTY (uncommitted changes)" } else { "clean" }
$stamp  = "$branch @ $sha | $date | $dirty"
Write-Host "==== PLAYTEST BUILD ====" -ForegroundColor Cyan
Write-Host $stamp -ForegroundColor Yellow
# write it where the game reads it for the in-game corner stamp
Set-Content -Path (Join-Path $godot "assets\build_info.txt") -Value $stamp -Encoding utf8

# --- build ---
Write-Host "building..." -ForegroundColor Cyan
dotnet build (Join-Path $godot "GodotClient.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED — not launching." -ForegroundColor Red; exit 1 }

# --- import (first run / new assets) ---
if (-not $NoImport) {
    Write-Host "importing assets..." -ForegroundColor Cyan
    & $GodotBin --path $godot --headless --import --quit | Out-Null
}

# --- launch ---
Write-Host "launching: $stamp" -ForegroundColor Green
& $GodotBin --path $godot
