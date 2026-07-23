# play.ps1 — the ONLY sanctioned way to launch the playtest build.
#
# ROOT-CAUSE FIX for the recurring "I playtested a stale/wrong build" incidents
# (2026-07-21 invisible pro-professions; 2026-07-23 main was still the 2D town while the
# 3D game lived off-trunk). See docs/design/build-provenance-and-never-lost.md.
#
# It is a FRESHNESS GATE, not just a launcher. Before it ever builds, it:
#   1. Requires the played checkout to be ON the canonical trunk ($Trunk, default 'main').
#   2. Fetches and FAST-FORWARDS the checkout to the newest trunk tip (origin if ahead).
#   3. REFUSES TO LAUNCH if the build is stale (behind trunk) or diverged — no silent
#      stale playtests. -AllowStale is the only override, and it launches with a loud banner.
# Only then does it stamp provenance (branch @ sha | ahead/behind | date | dirty), build,
# import, and launch. If it launches, the build IS the trunk tip — that is the guarantee.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/play.ps1
#         ... -Trunk main         # canonical branch play must track (default 'main')
#         ... -AllowStale         # escape hatch: launch even if stale/diverged (banner shown)
#         ... -NoLaunch           # run the gate + build + import, skip the GUI (CI / verify)
#         ... -NoImport           # skip asset reimport (faster relaunch, no new assets)
param(
    [string]$GodotBin = "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe",
    [string]$Trunk = "main",
    [switch]$AllowStale,
    [switch]$NoLaunch,
    [switch]$NoImport
)
$ErrorActionPreference = "Stop"
$repo = (git rev-parse --show-toplevel)
$godot = Join-Path $repo "godot"

function Fail($msg, $fix) {
    Write-Host "REFUSING TO LAUNCH — $msg" -ForegroundColor Red
    if ($fix) { Write-Host "  Fix: $fix" -ForegroundColor Yellow }
    Write-Host "  (override with -AllowStale if you truly mean to playtest a non-latest build)" -ForegroundColor DarkGray
    exit 1
}

# ---- FRESHNESS GATE -----------------------------------------------------------------------
$branch = (git rev-parse --abbrev-ref HEAD)
$ahead = 0; $behind = 0; $staleNote = ""

if ($AllowStale) {
    Write-Host "==== -AllowStale: freshness gate BYPASSED — this may NOT be the latest build ====" -ForegroundColor Magenta
    $staleNote = "STALE-OVERRIDE"
}
else {
    # (1) play tracks the trunk only — it is a playtest surface, not a dev branch.
    if ($branch -ne $Trunk) {
        Fail "checkout is on '$branch', not the canonical trunk '$Trunk'." "git checkout $Trunk"
    }

    # play is read-only: uncommitted CODE edits mean you're testing something off-trunk.
    # (.import reimport churn is expected and ignored.)
    $codeDirty = (git status --porcelain -- '*.cs' '*.tscn' '*.gd' 'project.godot')
    if ($codeDirty) {
        Fail "uncommitted code changes in the play checkout — play must track '$Trunk' cleanly." "commit/stash them in a dev worktree, not here"
    }

    # (2) fast-forward to the newest trunk. Fetch is best-effort (offline is OK for a purely
    #     local trunk); if origin/$Trunk is ahead and we can ff, take it.
    try { git fetch --quiet origin $Trunk 2>$null } catch { Write-Host "  (offline — using local $Trunk)" -ForegroundColor DarkGray }

    $hasOrigin = $false
    try { git rev-parse --verify --quiet "origin/$Trunk" *> $null; $hasOrigin = $? } catch { $hasOrigin = $false }

    if ($hasOrigin) {
        $behind = [int](git rev-list --count "HEAD..origin/$Trunk")
        $ahead  = [int](git rev-list --count "origin/$Trunk..HEAD")

        if ($behind -gt 0 -and $ahead -eq 0) {
            Write-Host "fast-forwarding $Trunk to origin ($behind new commit(s))..." -ForegroundColor Cyan
            git merge --ff-only "origin/$Trunk" | Out-Null
            $behind = 0
        }
        elseif ($behind -gt 0 -and $ahead -gt 0) {
            # (3) genuinely diverged — cannot silently pick a side.
            Fail "'$Trunk' has diverged from origin/$Trunk (ahead $ahead, behind $behind)." "reconcile: git pull --rebase origin $Trunk (resolve, retest)"
        }

        if ($ahead -gt 0) {
            Write-Host "note: local $Trunk is $ahead commit(s) ahead of origin (UNPUSHED — push so it can't be lost)." -ForegroundColor Yellow
            $staleNote = "ahead $ahead (unpushed)"
        }
    }
    else {
        Write-Host "  (no origin/$Trunk — local-only trunk)" -ForegroundColor DarkGray
    }
}

# ---- provenance stamp (what you are about to run) -----------------------------------------
$sha    = (git rev-parse --short HEAD)
$date   = (git show -s --format=%ci HEAD)
$dirty  = if ((git status --porcelain)) { "dirty" } else { "clean" }
$freshBits = @()
if ($staleNote) { $freshBits += $staleNote }
$freshBits += $dirty
$stamp  = "$branch @ $sha | $($freshBits -join ' | ') | $date"
Write-Host "==== PLAYTEST BUILD ====" -ForegroundColor Cyan
Write-Host $stamp -ForegroundColor Yellow
# write it where the game reads it for the in-game corner stamp
Set-Content -Path (Join-Path $godot "assets\build_info.txt") -Value $stamp -Encoding utf8

# ---- build --------------------------------------------------------------------------------
Write-Host "building..." -ForegroundColor Cyan
dotnet build (Join-Path $godot "GodotClient.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED — not launching." -ForegroundColor Red; exit 1 }

# ---- import (first run / new assets) ------------------------------------------------------
if (-not $NoImport) {
    Write-Host "importing assets..." -ForegroundColor Cyan
    & $GodotBin --path $godot --headless --import --quit | Out-Null
}

# ---- launch -------------------------------------------------------------------------------
if ($NoLaunch) {
    Write-Host "gate + build + import OK (-NoLaunch): $stamp" -ForegroundColor Green
    exit 0
}
Write-Host "launching: $stamp" -ForegroundColor Green
& $GodotBin --path $godot
