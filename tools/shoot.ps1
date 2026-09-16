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
#   -State: "" (town, default) | PhaseN (N=1..5, day-phase tint captures) | one of the states in the
#   BEGIN/END KNOWN_STATES block below.
#
#   P2-SCREEN-28: this line used to hand-list Forge | Shop | Tavern | Gate | Counter | Watch as the
#   valid values -- but the harness has never recognised Forge, Shop, Tavern, or Gate (only the more
#   specific ForgeAnvil, ShopPanel, TavernPanel, GateNight/GateHeldStreak, etc.), because a hand-copy
#   of shot_harness.gd's real state list rots the moment that list changes and nobody notices. It
#   cannot rot silently anymore: the block below is meant to be a verbatim copy of KNOWN_STATES, and
#   ShootScriptKnownStatesCensusTests (sim/GameSim.Tests/Hygiene) fails the fast lane the moment this
#   comment and that const disagree in either direction -- a state added to the harness without a
#   matching header update is a red build, not a stale doc.
#
# BEGIN KNOWN_STATES
#   BellTray, Bestiary, BrynGreedyRule, BrynRuleRevised, Camp, CommissionDilemma, Chronicle,
#   Counter, Demand, DepthsPanel, Docket, ForgeAnvil, ForgeAnvilEmpty, ForgeEcho, ForgeExit,
#   ForgeFlavor, ForgeLadder, ForgePanel, ForgeShelf, ForgeTrinket, GatedCounterEmptyShelf,
#   GateHeldStreak, GateNight, Graduation, HeroCandidateOpen, HeroCards, HeroErrand, HeroTrinket,
#   Ledger, LedgerProvenance, Lessons, Memorial, MemoryRow, MineGateFocus, Mirror, OccupancyCorner,
#   OreSlotGate, Primer, Provenance, ReturnAtNight, ReturnEmerge, ReturnQuestEmpty, SendOff, ShopPanel,
#   ShopTrinket, SplitLessons, Storied, StoriedCard, StoriedRefusal, SystemMenu, TavernPanel, TavernScene,
#   TavernSceneAtBar, Telling, TellingFall, TellingFork, TellingVerdict, TownOverview,
#   TutorialLookIn, TutorialOffCamera, Watch, WarrantFirstMorning
# END KNOWN_STATES
#
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

# ---- rebuild BEFORE anything renders ---------------------------------------------------------
# `godot/.godot/mono/temp/bin/**/GodotClient.dll` is gitignored (.gitignore:7-8) and NOTHING
# rebuilds it on a `git checkout`/`git fetch` -- only an explicit `dotnet build` or an editor
# build does. This script launches the Godot binary directly against the project path, so without
# this step it renders whatever assembly was compiled last, which may predate the commit stamped
# below by any number of changes.
#
# That is not hypothetical and it is not new. receipt.ps1's own header records a before/after pair
# that came back byte-identical because both shots ran the same stale DLL -- and it closed the hole
# by rebuilding inside ITS ceremony, leaving this script (documented and used standalone, not only
# as receipt.ps1's child) still able to fail exactly the same way. P2-SCREEN-02 then taught this
# script to stamp branch@sha into the rendered frame, which made the failure MODE WORSE rather than
# better: the watermark began vouching for a commit the running binary need not contain.
#
# Measured 2026-09-15: a capture of `main @ d8f401ef` carried "receipt: HEAD@d8f401ef | clean" in
# its own pixels while rendering pre-fix IL. The frame was read as proof that a merged fix had not
# worked, and cost a full investigation that ended in "both numbers are identical, there is no bug."
# The shared root's DLL at that moment was 31 commits behind the sha its captures were stamping.
#
# So the build happens HERE, before the stamp, and a build that does not compile refuses to capture.
# The stamp is then true by construction rather than by convention. There is deliberately no
# -SkipBuild escape hatch: an incremental no-op build costs about a second, and the entire point is
# that the unsafe door stops existing -- a safe wrapper beside an unsafe tool is what failed twice.
Write-Host "==== building godot/GodotClient.csproj (stale-DLL guard) ====" -ForegroundColor Cyan
dotnet build (Join-Path $godot "GodotClient.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "BUILD FAILED -- refusing to capture. A frame rendered from the last assembly that" -ForegroundColor Red
    Write-Host "happened to compile is not a picture of this commit, and the watermark stamped below" -ForegroundColor Red
    Write-Host "would claim it was. See this block's comment for the incident that bought this check." -ForegroundColor Red
    exit 1
}
# Report the assembly's own timestamp: if the build above was a no-op because nothing changed, this
# line is what tells a reader the binary is genuinely current rather than merely unrebuilt.
$clientDll = Join-Path $godot ".godot\mono\temp\bin\Debug\GodotClient.dll"
if (Test-Path $clientDll) {
    Write-Host ("client assembly: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Item $clientDll).LastWriteTime) -ForegroundColor DarkGray
}

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
# P2-MEMORY-22: the outdoor memorial wall's own lantern row needs real recorded deaths, which a
# fresh day-1 campaign has none of (MainUi.StageMemorialDeathsReceipt) -- 3 is enough to show a
# real, non-trivial row without implying a specific "how many is normal" count.
$env:SHOT_MEMORIAL_DEATHS = if ($State -eq "Memorial") { "3" } else { "" }
# P2-PEOPLE-01: TavernScene / TavernSceneAtBar need one FACT planted before the tavern is opened --
# a player-marked piece in Torvald's hands (MainUi.StageArcSceneReceipt). The scene engine then
# decides for itself whether to offer, so the capture still proves the real eligibility rule rather
# than a staged screen. Same seam and same never-in-real-play contract as SHOT_WATCH_FIGHT above.
$env:SHOT_ARC_SCENE = if ($State -eq "TavernScene" -or $State -eq "TavernSceneAtBar") { "1" } else { "" }
# M2b: the storied-gear states need FACTS planted before anything opens -- a marked blade with real
# recorded deeds in every hero's hands, and one marginally better blade shelved
# (MainUi.StageStoriedGearReceipt). The threshold, the wall row, the card line and the counter's
# refusal are then all decided by the sim itself. Same seam and same never-in-real-play contract as
# SHOT_WATCH_FIGHT / SHOT_ARC_SCENE above.
$env:SHOT_STORIED = if ($State -eq "Storied" -or $State -eq "StoriedCard" -or $State -eq "StoriedRefusal") { "1" } else { "" }
# P2-END-01 (§11.8.1, "say it out loud"): the gate-held-streak Ledger receipt -- same
# never-in-real-play staging contract as SHOT_WATCH_FIGHT/SHOT_ARC_SCENE/SHOT_STORIED above.
$env:SHOT_GATE_HELD_STREAK = if ($State -eq "GateHeldStreak") { "1" } else { "" }
# P2-HONEST-27: the ore row's own action-slot gate -- Torvald's mithril offer live on day 1's
# Evening with the day's action slots already spent (MainUi.StageOreZeroSlotEveningReceipt).
# Reaching a real 0-slot Evening honestly costs five real workshop actions a screenshot has no
# business driving; the Buy button's Disabled state and player-phrased reason are then decided
# entirely by LedgerModal's own gate, exactly as they would be in play. Never reads in real play.
$env:SHOT_ORE_SLOT_GATE = if ($State -eq "OreSlotGate") { "1" } else { "" }
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
