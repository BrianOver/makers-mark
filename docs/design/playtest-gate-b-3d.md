# Gate B — 3D town playtest sheet (Brian's run-and-score instrument)

> **RUN STATUS.** Play the CURRENT build only: run `tools/play.ps1` from `C:\Code\Game\play`.
> It is a FRESHNESS GATE -- it fetches, fast-forwards `play` to the latest `main`, refuses to launch
> anything stale/diverged, then builds + launches. The in-game corner **build stamp**
> (`branch @ sha | freshness | date`) tells you exactly what you are running -- screenshot it with any
> finding. There is no `play-3d` branch anymore: **`main` IS the game.**
>
> **Pre-flight green bar (2026-07-23, main @ fba5bb2):** fast lane 981, Balance 25, Godot build clean.
>
> **WHAT IS ACTUALLY IN THIS BUILD (scope -- read before scoring; do not test for what is not here):**
> - YES 3D town HUB: real 3D buildings (AI-gen + kit), click-to-move navmesh, 3D hero actors, camera rig.
> - YES Blacksmith ACTIVE craft: staged forge minigame (smelt/forge/quench) with G1 world VFX + result
>   ceremony (grade / quality stars / 3 sub-score pips) + in-game build stamp.
> - YES Scarcity (G3): per-day action slots, guild rent, rival market share.
> - YES Raid forecast (G4): CLI `forecast`/`telegraph` command only -- NO in-game board yet.
> - NO -- NOT the "full 3D game": building INTERIORS are still 2D painted backdrops; the mine/expedition
>   is a 2D spectate feed. 3D is the town HUB only.
> - NO -- NOT a profession overhaul: only the BLACKSMITH is interactive. Alchemy/Tanning/Engineering
>   exist as recipes/items/talents but craft PASSIVELY (auto-craft menu, no distinct minigame). The
>   per-profession verb overhaul is Phase B (docs/plans/2026-07-21-006-phaseB-living-heroes.md), NOT built.
>
> **This is the thing you open and fill in.** The *why* lives in
> `2026-07-21-3d-playtest-redesign.md` + `-open-questions-research.md`; this is the *do*.
> Gate A (CLI naive-persona comprehension) is **PASSED and carried** -- do not re-run it for a
> render change. Gate B rev.2 alone gates the 3D slice.
>
> **Decisions baked in (confirmed 2026-07-21):** motion sickness = auto-P0, fps target 60 / floor
> 45 (sustained <45 = P0) on the dev box (RTX 5080 / 7950X / 64 GB) via Godot's **built-in F3
> overlay**, strict discoverability bar, golden-image tests deferred, Cards 2 & 4 want a *fresh*
> tester on major reruns (prior exposure destroys cold-cognition; you keep verdict authority).

---

## 0. Pre-flight — MUST be green before you spend any human time

Run these; if any fails, fix first — no point scoring a broken build.

```bash
# fast lane (no Godot)
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter "Category!=Balance"
# 3D engine tests (render-free) — Godot 4.6.3 via .runsettings
dotnet test godot/tests/GodotClient.Tests.csproj --settings .runsettings \
  --filter "FullyQualifiedName~Town3DSceneTests|FullyQualifiedName~Building3DInteractionTests|FullyQualifiedName~GenAssetTests"
```

Optional visual baseline (real GPU, saves a PNG of the town + HUD, then quits):

```powershell
# type this with a leading ! in the Claude prompt, or run in PowerShell yourself
$env:TOWN_SHOT="C:\temp\townshot.png"; & "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe" --path godot
```

## 1. Launch for the live session

```powershell
powershell -ExecutionPolicy Bypass -File tools/play.ps1
```

Always launch via the gate (never the raw Godot exe) so you can never score a stale build; it writes
the corner build stamp and refuses stale/diverged checkouts. Press **F3** in-game for the fps/frame-time overlay (watch it during Card 5). Play **cold** — do not
pre-plan routes. Seed 2026 for reproducibility if a finding needs a repro. Budget ~45–60 min.

---

## 2. Score sheet — five cards, five axes (PASS / PARTIAL / FAIL)

Same trichotomy as Gate A so both gates read side-by-side. Fill the verdict + notes; log any
PARTIAL/FAIL as a numbered finding in §4.

### Card 1 — First 90 seconds (Embodiment)
New game, no instructions. WASD walk, then click-to-move. *PASS:* control immediate + predictable,
avatar faces travel direction, camera comfortable. *FAIL:* laggy/floaty input, sliding without
intent, click-move misfires.
- **Verdict:** ☐ PASS ☐ PARTIAL ☐ FAIL — notes: ____________________

### Card 2 — "Go to the Forge, craft something" (Navigation + interaction discoverability) — *fresh tester ideal*
*PASS:* each target found <~30 s, the highlight + `E · <label>` prompt is noticed and understood
without a hint. *FAIL:* any building unreachable/unfindable, prompt never noticed, stuck states.
- **Verdict:** ☐ PASS ☐ PARTIAL ☐ FAIL — notes: ____________________

### Card 3 — The seam (drawer/interior over 3D)
Open Forge panel → craft → close; enter an interior → exit. *PASS:* world input is dead while any
drawer/interior/modal is open; exit returns avatar sanely. *FAIL:* click-through moves avatar behind
a drawer, interior exit strands the avatar, input double-fires.
- **Verdict:** ☐ PASS ☐ PARTIAL ☐ FAIL — notes: ____________________

### Card 4 — Read the town (Spatial comprehension — the Gate-A bridge) — *fresh tester ideal*
Without opening any panel, does the town *layout alone* communicate the loop geography
(forge → market/shelf → notice board → mine gate) and make the game feel like a place, not a menu?
*FAIL here = the "3D hurt comprehension" alarm → design conversation, not just a bug.*
- **Verdict:** ☐ PASS ☐ PARTIAL ☐ FAIL — notes: ____________________

### Card 5 — Comfort + performance soak (Performance-comfort)
10 continuous minutes of normal play, F3 overlay on. *PASS:* sustained **≥60 fps**, no judder/swim,
no discomfort. *FAIL:* sustained **<45 fps** (P0), or ANY nausea/discomfort (**auto-P0**).
- fps observed (min / typical): ______ / ______
- **Verdict:** ☐ PASS ☐ PARTIAL ☐ FAIL — notes: ____________________

---

## 3. Severity (3D anchors)

- **P0** — core loop unreachable/misleading, a stuck/strand state, sustained <45 fps, or ANY motion
  discomfort. Blocks the gate.
- **P1** — real defect hurting play (prompt easy to miss, a building hard to find, camera annoyance).
- **P2** — polish (label legibility at distance, minor pop-in). Never gates.

## 4. Findings (this run)

Log each PARTIAL/FAIL. Copy the block per finding.

```
[P?] Card N / axis — one-line symptom
  Repro (seed 2026 + steps):
  Expected vs actual:
```

## 5. Verdict + carry-forward

- Per-axis: C1 __ · C2 __ · C3 __ · C4 __ · C5 __
- **Overall:** ☐ PASS ☐ CONDITIONAL FAIL ☐ FAIL
- **Gate B rev.2 passes when all five axes PASS and no P0/P1 open.** P2s never gate.
- Save this filled sheet as `docs/design/playtest-findings-<date>-gate-b-3d.md` (mirrors the Gate-A
  findings-doc convention). Acceptance of the 3D slice = **Gate A PASS (done)** + this sheet PASS.
