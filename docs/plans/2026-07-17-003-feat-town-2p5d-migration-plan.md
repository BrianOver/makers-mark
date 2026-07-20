---
title: "feat: town 2.5D migration — lit Node2D world, hero figures, 5-phase ambience"
date: 2026-07-17
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
origin: docs/design/graphics-2.5d-direction.md (pilot APPROVED, PR #32) + lane-B recon
execution: code+assets — VISUALS-lane Claude (V1, V2, V3-gen, V4a, V4b, V5a, V5b); ENGINE lane authors O1 + V0 (orchestrator merges); addon swarm authors V3-specs
related: docs/plans/2026-07-17-002-feat-staged-resolution-plan.md (U2 gate on V5a; V5b after U3), docs/design/lane-operating-model.md (charters, gates), docs/design/art-pipeline-architecture.md, docs/design/asset-style-spec.md
---

> **STATUS (2026-07-20, U25 cleanup): V4b/V5b's deferred town-wide migration is now DONE** —
> executed on Godot 4.6.3 (the 4.7.1 gate below was moot: 4.6.3 supports Y-sort/`CharacterBody2D`/
> `Area2D`/`TileMapLayer` in full) by `docs/plans/2026-07-19-002-feat-world-rework-plan.md`'s U3
> (de-collage triage) + U14 (TownWorld promotion — the Y-sort unification this plan's V4b
> describes). `lit_tavern_pilot.tscn`/`LitTavernPilot.cs` (the pilot this plan was built to
> generalize) are deleted as part of that same cleanup (U25) — their job is done; every building
> now ships the promoted, input-bearing, Y-sorted world directly. This plan's Phase 1 (art/cutout
> pipeline) and pre-V4b groundwork remain historically accurate; V4b/V5b's own sections describe
> the approach later executed under the 2026-07-19-002 plan's own KTD1/KTD6/U3/U14, not verbatim
> (that plan is the current source of truth for the town scene's actual shape).

## Goal Capsule

Take the approved lit-2D pilot (`godot/scenes/town/lit_tavern_pilot.tscn` + `LitTavernPilot.cs`: `CanvasTexture` diffuse+normal `Sprite2D` + `PointLight2D` + `CanvasModulate`) town-wide: complete the four-building asset set through the proven ComfyUI→cutout→normal-map pipeline, migrate `TownScene` from `Control`/`TextureRect` widgets to a lit `Node2D` world inside a `SubViewport`, give heroes neutral-base generated figures tinted by `ClassDefinition.ColorRgb`, and drive the ambient tint from the real `DayPhase` — including the 5-phase day when staged resolution lands. Every unit lands green on the existing CI gates. **Role note:** the VISUALS lane **is** the master art-Claude of `art-pipeline-architecture.md` §2 — sole writer of pixels, `.import` sidecars, and build-half JSON; that role grant authorizes the `godot/assets/art/**` writes below.

## Grounding facts (verified this session — re-verify if stale)

| Fact | Where |
|---|---|
| Town is Control-built: ground/gate/facades/memorials/tint all `Control`/`TextureRect`/`ColorRect`, built in code; `town_scene.tscn` is bare | `godot/scripts/town/TownScene.cs:237-365` |
| Tint is an alpha **overlay** `ColorRect` (`TintFor` returns alpha colors); pilot uses **multiply** `CanvasModulate` | `TownScene.cs:221-227` vs `LitTavernPilot.cs:15-23` |
| `TownScene.OnPhaseCompleted` switch has `case DayPhase.Evening: default:` → any **new** phase value snaps heroes home mid-day (visible-dead-hero bug when the 5-phase kernel lands) | `TownScene.cs:131-139` |
| `DayPhase` is still 3 values; Camp=3/ExpeditionDeep=4 land via the orchestrator's contracts micro-PR (staged plan U1) | `sim/GameSim/Contracts/Enums.cs:4-9` |
| `HeroSprite` is a `Control` (64×34) with children `Sprite` (TextureRect), `Marker` (ColorRect), `NameLabel`; tint via `Modulate` = `ClassDefinition.ColorRgb` | `godot/scripts/town/HeroSprite.cs:78,90-124` |
| Tests bind to node names/types: `Find<TextureRect>(sprite,"Sprite")`, `Find<ColorRect>(sprite,"Marker")`, `Find<Control>(ui.Town,"Building_Forge")`, `Click(Control)`, `CurrentTint == TintFor(phase)`, 3-tick day loops | `godot/tests/TownSceneTests.cs:34-41,54,67,87,246,263-270`, `UiTestSupport.cs:44-47` |
| **20 `AdvancePhase()` call sites** hard-coding day math: TownSceneTests **8** / MainUiTests **9** / SimAdapterTests **3** (grep this session) | `godot/tests/` |
| `IconRegistry.Art(name)` is null-tolerant PNG lookup in flat `res://assets/art/` (no track subdirs on disk — pilot committed flat) | `godot/scripts/IconRegistry.cs:45-49`, `godot/assets/art/` |
| 4 building request-half specs already exist and merge green: `town-forge`, `town-tavern`, `town-market`, `town-mine-gate`, all `Active` + `NormalMap: true` | `art/specs/town/TownSpecs.cs` |
| `art/build/` does **not** exist yet — even the shipped tavern has no build-half JSON | `art/` listing |
| Measured pilot asset weight: `town-tavern.png` 1,250,565 B + `town-tavern_n.png` 404,243 B ≈ **1.58 MiB/pair**; `.gitattributes` is `* text=auto` + `*.sh eol=lf` only (no LFS) | `godot/assets/art/`, `.gitattributes` |
| Engine pin 4.6.3-stable; CI pin at `ci.yml:48` (`chickensoft-games/setup-godot@v2`); local pin in `.runsettings` `GODOT_BIN`; CI runs engine tests on **every** PR (no path filters, `ci.yml:4-6,42`); import cache keyed on `project.godot` hash (`:57`); silent-skip guard (`:75-78`) | `.godot-version`, `.github/workflows/ci.yml`, `.runsettings` |
| gdUnit4Net pinned: `gdUnit4.api 5.0.0` + `gdUnit4.test.adapter 3.0.0`; csproj comment: adapter 3.1.x requires api `5.1.0-rc5` (prerelease — why we're stuck) | `godot/tests/GodotClient.Tests.csproj:29-35` |
| `PhaseClock.DurationOf` already has a safe default arm; `MainUi` only special-cases Evening | `godot/scripts/PhaseClock.cs:40-46`, `godot/scripts/MainUi.cs:110-134` |
| Cutout + normal-map scripts exist only in session scratchpad, validated on the tavern | scratchpad `cutout.py`, `normalmap.py` → committed as `art/pipeline/` in V1 |
| `ScriptedSession` `(day, phase)` tuple patterns are append-tolerant — unmatched Camp/Deep tuples yield no actions | `godot/tests/ScriptedSession.cs:56-65` |
| `AssetSpec` already carries every V3 field: `ClassFigure` kind, `NeutralBaseTint`, `ClassId` hint-string ("not resolved against ClassRegistry"), Width/Height | `art/GameArt/AssetSpec.cs:14-77` |

## Unit map & ordering

Unit namespace **V** (visuals) + **O** (orchestrator-merged infra). Claim files `.claude/tasks/V<N>-<slug>.md` / `O1-lfs-art.md` per `.claude/tasks/README.md` + operating-model §5. One unit = one branch = one PR. **Ownership per the lane operating model:** O1 and V0 are **ENGINE-lane-authored, orchestrator-merged**; V3-specs is an **addon-swarm packet**; everything else is VISUALS.

| Unit | Branch | Author lane | Blocked on | Engine |
|---|---|---|---|---|
| **V5a** 5-phase tolerance hardening | `feat/v5a-phase-tolerance` | VISUALS | — (**land FIRST — BOARD gate G2, blocks staged-plan U2**) | 4.6.3 OK |
| **O1** LFS + pipeline infra | `ci/lfs-art` | ENGINE (orchestrator merges) | — | any |
| **V1** pipeline scripts commit | `feat/v1-art-pipeline-scripts` | VISUALS | — | 4.6.3 OK |
| **V2** building assets: forge/market/mine-gate + build-half backfill | `feat/v2-town-buildings` | VISUALS | O1 | 4.6.3 OK |
| **V3-specs** hero figure specs | `feat/addon-art-heroes` | **addon swarm** (packet `addon-art-heroes`) | — | 4.6.3 OK |
| **V3-gen** hero figures generation | `feat/v3-hero-figures-gen` | VISUALS | O1 + V3-specs | 4.6.3 OK |
| **V4a** `IconRegistry.Lit` helper + tests | `feat/v4a-icon-lit` | VISUALS | — (four-id asserts after O1+V2) | 4.6.3 OK |
| **V0** Godot 4.7.1 upgrade gate | `ci/godot-4.7.1` | ENGINE authors, VISUALS verifies, orchestrator merges | gdUnit4Net upstream | — |
| **V4b** town scene migration | `feat/v4b-town-2p5d` | VISUALS | V0 + V2 + V4a + V5a | **4.7.1 only** |
| **V5b** real 5-phase ambience + choreography | `feat/v5b-phase-ambience` | VISUALS | V4b + staged-plan U3 | 4.7.1 |

Parallelizable now on 4.6.3: **V5a (first), V1, V4a** immediately; **V2, V3-gen** after O1; V3-specs anytime (swarm). **V0** whenever upstream ships. **V4b → V5b** serial after the gate.

---

## Phase 0a — V5a: 5-phase tolerance (**the plan's only hard inter-lane deadline — land before staged-plan U2**)

Verified hazard: `TownScene.OnPhaseCompleted` ends in `case DayPhase.Evening: default:` (`TownScene.cs:131-139`). When the 5-phase kernel (staged-plan U2) lands, MainUi fires OnPhaseCompleted for Camp/ExpeditionDeep completions → the default arm `SnapHome()`s everyone **mid-expedition**, making away/dead heroes pop visible before the Evening reveal — and every engine test that drives "3 ticks = 1 day" (the 20 `AdvancePhase` call sites above) breaks on day math. Because CI runs engine tests on every PR, U2 is unmergeable until this unit is on main.

Contents (all on the current Control-based town — independent of V0/V4b):
1. `OnPhaseCompleted`: make Evening explicit; unknown phases → no-op `return` (never snap on a phase we don't know).
2. `TintFor`: default arm already exists at `:226` — keep; `PhaseClock.DurationOf` default already safe (`:45`) — no change.
3. **`UiTestSupport.AdvanceDay(MainUi ui)` — loop-until-Morning form (normative):** tick `AdvancePhase()` until the sim reports `Phase == DayPhase.Morning` again, with a hard cap (e.g. 8 ticks) that fails the test on overrun. **NOT `Enum.GetValues<DayPhase>().Length` ticks** — that form breaks in the window between the contracts micro-PR (enum → 5 values) and the kernel PR (day still 3 ticks), turning the contracts PR red on the engine lane and defeating this unit's purpose. Loop-until-Morning is green in all three states (3-value/3-tick, 5-value/3-tick, 5-value/5-tick). Replace the hardcoded 3-tick day loops at all 20 call sites across `TownSceneTests`, `MainUiTests`, **and `SimAdapterTests`**.
4. Test: simulated unknown-phase completion — cast a **beyond-max** value (e.g. `(DayPhase)99`; `(DayPhase)3` stops being unknown the moment the contracts PR lands, when 3 = Camp) — leaves all sprite states/visibility untouched.

**Definition of done:** full engine suite green with the `AdvanceDay` helper; unknown-phase no-op test green; BOARD gate G2 flipped — **the AI-NPC lane is unblocked for U2 with zero `godot/` edits of their own.**

## Phase 0b — V0: Godot 4.7.1 upgrade gate

**Decided rule (do not relitigate):** stay on 4.6.3 until gdUnit4Net ships **stable** 4.7 support; then one isolated 4.7.1 infra PR; the town-wide migration (V4b) lands **only on 4.7.1**. Pilot-style additive scenes may proceed on 4.6.3.

**Ownership (unified with the lane operating model):** the **ENGINE lane** runs the upstream watch and authors the PR (it owns `.runsettings`/`.github/**` and holds author-not-merge grants on `.godot-version`/`project.godot`/`GodotClient.Tests.csproj`); the **VISUALS lane** performs the local scene-semantics verification (reviews the `.import` diff, runs the engine suite twice on 4.7.1); the **orchestrator merges** (deny-listed files) and updates CLAUDE.md rule 2.

### Watch procedure (ENGINE lane, weekly or when pinged)

Watch **`github.com/MikeSchulze/gdUnit4Net`** (Releases + README compatibility matrix) plus the NuGet listings for **`gdUnit4.api`** and **`gdUnit4.test.adapter`**. Ship condition (all three): (1) a **stable, non-prerelease** `gdUnit4.api` ≥ 5.1.0 final (today's blocker: adapter 3.1.x depends on `5.1.0-rc5`, per the pin comment at `GodotClient.Tests.csproj:30-31`); (2) an adapter whose NuGet dependency range accepts that stable api; (3) release notes explicitly listing **Godot 4.7.x**. Check via `dotnet package search`; then **local proof on a scratch branch before any repo change**: install `Godot_v4.7.1-stable_mono_win64` to `C:\Tools\Godot\`, export `GODOT_BIN` (env overrides `.runsettings`), bump the two `PackageReference`s, run `dotnet test godot/tests --settings .runsettings` twice (import flake allowance). Green twice = verified; hand evidence to the orchestrator.

### V0 PR contents — exact files

| File | Change |
|---|---|
| `.godot-version` | `4.6.3-stable` → `4.7.1-stable` (source of truth per CLAUDE.md rule 2) |
| `.github/workflows/ci.yml:48` | `version: 4.6.3` → `4.7.1` (keep the sync comment) |
| `.runsettings` | `GODOT_BIN` → the 4.7.1 console exe |
| `godot/tests/GodotClient.Tests.csproj` | bump both gdUnit4 packages to the verified stable pair; update the pin-rationale comment |
| `godot/project.godot` | **re-save protocol** below |
| `CLAUDE.md` rule 2 | "4.6.3-stable" → "4.7.1-stable" (orchestrator-only edit) |

**`project.godot` re-save protocol:** exactly one deliberate open-and-quit in the pinned **4.7.1** editor (rewrites `config/features` from `("4.6","C#")`; update the do-not-open warning text); then `godot --headless --path godot --import --quit-after 200` to re-mint every `.import`; commit **all** rewritten `.import`/scene metadata in this same PR; then verify `godot/GodotClient.csproj` kept its explicit `net10.0` TFM (hard rule 3 — the import step is exactly when Godot injects `net8.0`); `git grep net8.0` must return nothing. CI's import cache self-invalidates (key hashes `project.godot`). After merge, BOARD.md announces **no session may open `godot/` with 4.6.3 again**.

**V0 acceptance:** engine suite green twice locally on 4.7.1; all three CI lanes green; `.import` diff reviewed by VISUALS for scene-semantic changes (should be metadata only); no `net8.0` anywhere.

### Fallback if the adapter lags

Everything except V4b/V5b proceeds on 4.6.3. For de-risking while blocked, build **additive pilot-style scenes** (`lit_forge_pilot.tscn` etc.) to tune per-building light placement — additive `.tscn`, no `town_scene.tscn` edit, throwaway like the original pilot. Do **not** start V4b on 4.6.3 "to be ready" — the 4.7.1 re-import would churn every scene it touches. Blocked > ~6 weeks → escalate to Brian (dropping the adapter for a direct runner scene is an orchestrator-level infra decision).

---

## Phase 1 — asset completion (O1, V1, V2)

### O1 — LFS + pipeline infra (**ENGINE lane authors, orchestrator merges**; must precede V2's PNG commits)

**LFS decision — DECIDED NOW: adopt.** Math: measured pair = 1.58 MiB (tavern); projected this plan = 4 building pairs ≈ 6.3 MiB + 3 hero pairs ≈ 2–3 MiB ≈ **8–15 MiB near-term**. Plain git tolerates that — but every curation re-roll of a locked asset re-commits ~1.6 MiB into permanent history, the master catalog implies monsters/portraits/backdrops next, and retrofitting LFS later means either a history rewrite (hostile to the multi-session model — every agent re-clones) or permanently split storage. Both authority docs already commit to LFS ("must land before the first generated asset" — PR #32 shipped the tavern without it; stop the bleeding at two files).

Contents:
1. `.gitattributes`: append `godot/assets/art/**/*.png filter=lfs diff=lfs merge=lfs -text` (exactly this pattern — `art/**` would LFS-ify C# spec source and miss the PNG tree; pre-authorized in `art-pipeline-architecture.md` §4). Existing lines (`* text=auto`, `*.sh eol=lf`) untouched.
2. Renormalize the two existing tavern PNGs (`git add --renormalize`) so they become LFS pointers going forward. **No history rewrite** — the two old blobs (~1.6 MiB) stay as accepted dead weight.
3. `ci.yml` engine-tests job: `actions/checkout@v4` gains `lfs: true`; add an `actions/cache` step for `.git/lfs` keyed on the LFS file-list hash (protects the GitHub free-tier 1 GiB/month LFS bandwidth). sim-tests/balance-sim jobs stay LFS-less (pointer files are fine; they never read PNGs — `AssetConformanceTests` is pure no-IO by design).
4. `.gitignore`: add `art/pipeline/candidates/` (gitignored candidate images, architecture §5).

### V1 — commit the pipeline scripts (`art/pipeline/`)

Move the session-scratchpad scripts into the repo as the committed tool of record (currently orphaned tribal knowledge — violates the docs rule). **Ownership note: `art/pipeline/**` (scripts, requirements, README, `seeds.generated.md`) is VISUALS-owned per the operating model §1; `models.lock.json`, when it exists, stays orchestrator-owned.**

- **`art/pipeline/cutout.py`** — BiRefNet cutout (gradient-bg sprite → RGBA PNG): `AutoModelForImageSegmentation.from_pretrained("ZhengPeng7/BiRefNet", trust_remote_code=True)`, CUDA half-precision, 1024² mask resize, alpha from sigmoid mask. **Two required hardening edits on commit:** (a) pin `revision="<sha>"` in `from_pretrained` — `trust_remote_code` executes repo code from HF, an unpinned revision is a supply-chain hole (Fornida security rule); (b) add optional `--trim` flag (`out.crop(out.getbbox())`) so committed sprites are content-tight — V4b's runtime scaling assumes trimmed textures.
- **`art/pipeline/normalmap.py`** — height-from-luminance (Gaussian-blurred) → Sobel gradients → tangent-space `_n.png`, strength arg (default 2.5), green-channel flip for Godot, alpha preserved. Validated on the tavern in PR #32 — keep the math byte-identical apart from a `--strength` doc note.
- **`art/pipeline/requirements.txt`** — `torch` (cu130 wheel, local RTX 5080 per asset-style-spec), `torchvision`, `transformers`, `pillow`, `numpy`.
- **`art/pipeline/README.md`** — venv setup, per-stage usage (ComfyUI gen → cutout → normalmap → optional Laigter quality pass → Godot import on the pinned engine), and the explicit note: **local tooling only, never wired into CI; model weights never committed** (`models.lock.json` pins by sha when it exists).

### V2 — generate + commit forge / market / mine-gate; backfill build-halves

Specs already exist and are conformance-green (`art/specs/town/TownSpecs.cs`) — pure master-art-Claude lifecycle (architecture §6), one serial PR:

1. **Contract seeds:** first candidate = `AssetSeed.SeedFor(id)` (`art/GameArt/AssetSeed.cs` — FNV-1a+SplitMix64 of the id). **Curation re-rolls = seed+1, seed+2, …** — record the chosen offset seed in the build-half; conformance never asserts seed == SeedFor(id) (provenance, not reproducibility — the committed PNG + sha256 is the guarantee).
2. Generate 8–16 candidates per building via ComfyUI MCP with the `Active`-track profile (asset-style-spec: 1024², steps 25, cfg 6.5, dpmpp_2m/karras, 3/4-iso, plain dark neutral background); candidates go to gitignored `art/pipeline/candidates/`.
3. Curate (expect 60–90% reject) → optional Krita hand-finish → `cutout.py --trim` → `normalmap.py` → **optional Laigter quality pass** where the Sobel map reads embossed/tinny (the known low-relief risk from graphics-2.5d-direction).
4. **QA gate before commit:** load each pair in a throwaway pilot-style scene and sweep the light with arrows — bumps must brighten **toward** the light (catches normal-map Y-flip errors).
5. On the **pinned engine** (whichever `.godot-version` says at the time): import, commit `godot/assets/art/{town-forge,town-market,town-mine-gate}.png` + `_n.png` + `.import`. **Layout decision — DECIDED: stay flat** in `godot/assets/art/` (no `<track>/` subdirs): the shipped registry (`IconRegistry.Art` → `res://assets/art/{name}.png`) and the pilot are flat; the null-tolerant *name* is the contract, not the path. Record the deviation from architecture-doc §4's `<track>/` sketch in that doc (one-line note; docs are open).
6. **Build-half JSONs** — create `art/build/` and write `town-forge.build.json`, `town-market.build.json`, `town-mine-gate.build.json` **and backfill `town-tavern.build.json`** (currently missing — the pilot shipped without its provenance record). Fields per architecture §3: seed, model (`sd_xl_base_1.0.safetensors`), steps/cfgMilli/sampler/scheduler resolved, diffuse+normal sha256 (compute from committed bytes), uid, `HandFinished`, `Status: locked`, provenance (drafts count, paintover note, AI disclosure). For the tavern: recover the real seed from the PR #32 session record if possible; if unrecoverable, record `SeedFor("town-tavern")` with provenance note `"pilot asset; generation seed not preserved"` — never fabricate.

---

## Phase 2 — scene migration (V4a, V4b)

### V4a — `IconRegistry.Lit` (small, 4.6.3-safe, unblocks V4b review)

Add to `godot/scripts/IconRegistry.cs` (per lane-B recon):

```csharp
/// <summary>Diffuse+normal CanvasTexture for a generated art id (2.5D path). Null-tolerant:
/// null when the diffuse is absent (caller falls back to the SVG placeholder); a missing
/// _n sibling yields a diffuse-only CanvasTexture (lights work, normals just read flat).</summary>
public static CanvasTexture? Lit(string id)
{
    var diffuse = Art(id);
    return diffuse is null ? null
        : new CanvasTexture { DiffuseTexture = diffuse, NormalTexture = Art(id + "_n") };
}
```

Tests (append to `IconRegistryTests.cs` — `GeneratedArt…ReturnsNull` precedent at `:63-68`): `Lit("town-tavern")` returns CanvasTexture with non-null diffuse **and** normal; `Lit("does_not_exist_yet")` is null; after V2 merges, extend coverage to all four town ids (+ `_n`). Note: these assertions make engine-tests require materialized LFS content — which O1's `lfs: true` provides; sequence the *four-id* assertions after both O1 and V2.

### V4b — the town migration proper (4.7.1 only)

**Structure — the SubViewport trap (lane-B recon fact):** `CanvasModulate` tints its entire canvas, which for a naive in-tab `Node2D` includes the whole TabContainer UI. Therefore the town world lives inside its own canvas:

```
TownScene (SimPanel/Control — STAYS the TabContainer child; Bind/Refresh/events unchanged)
└── SubViewportContainer "TownViewportContainer"  (full-rect, stretch = true)
    └── SubViewport "TownViewport"                (size driven by container stretch)
        └── Node2D "TownWorld"
            ├── Sprite2D "Ground"          (ground_tile SVG, region+repeat, Centered=false)
            ├── Sprite2D "ForgeSprite" + Control "Building_Forge"   (invisible hit-rect)
            ├── Sprite2D "ShopSprite"  + Control "Building_Shop"
            ├── Sprite2D "TavernSprite"+ Control "Building_Tavern"
            ├── Sprite2D "GateSprite"  + Label "GATE"
            ├── Node2D  "MemorialPlot" (stones = Sprite2D SVG + Label, as today)
            ├── Node2D  "HeroLayer"    (HeroSprite children, now Node2D)
            ├── PointLight2D ×4 (below)
            └── CanvasModulate "Ambient"
```

`MainUi.cs` is untouched: `InstantiatePanel<TownScene>`, `Tabs.GetTabIdxFromControl(Town)`, `HeroClicked`/`BuildingClicked` routing all survive because `TownScene` remains a `SimPanel` Control with the same public surface.

**Element map (positions preserved exactly — `GatePosition (900,300)`, buildings at y=90, `BuildingSize (96,72)`, `MemorialPlotOrigin (20,60)`, homes formula unchanged):**

| Element | Today (`TownScene.cs`) | Target | Texture | Fallback |
|---|---|---|---|---|
| Ground | `TextureRect` Tile (:247) | `Sprite2D` region-repeat | `Building("ground_tile")` SVG (unchanged v1) | — |
| Forge | Control+TextureRect (:309) | `Sprite2D` + invisible hit-rect Control | `Lit("town-forge")` | `Building("forge")` |
| Shop | ditto | ditto | `Lit("town-market")` ← note the key≠id mapping | `Building("shop")` |
| Tavern | ditto | ditto | `Lit("town-tavern")` | `Building("tavern")` |
| Mine gate | Control+TextureRect (:282) | `Sprite2D` (no click today — keep) | `Lit("town-mine-gate")` | `Building("mine_gate")` |
| Memorial stones | TextureRect (:194) | `Sprite2D` SVG, no normal | `Building("memorial_stone")` | — |
| DayNightTint | overlay `ColorRect` (:272) | **`CanvasModulate` "Ambient"** | multiply table below | — |
| HeroLayer | Control (:263) | `Node2D` | — | — |

**Gotchas pinned as requirements:**
- `Sprite2D.Centered = false` on every migrated sprite — Sprite2D positions are center-origin by default; the old Control coordinates are top-left. Silent half-size offsets otherwise.
- Runtime scale: generated PNGs are ~1024² trimmed cutouts; `sprite.Scale = uniform min(BuildingSize.X/tex.Width, BuildingSize.Y/tex.Height)` — never stretch non-uniformly.
- Every `PointLight2D.Height` ≈ 30 (pilot value) — height 0 makes normals read flat (**the** #1 gotcha, now test-asserted).
- Click handling: **keep invisible `Control` hit-rects** (recon: `UiTestSupport.Click` emits `GuiInput` and breaks under `Area2D`). Hit-rects keep today's exact names (`Building_Forge` etc.), sizes, and `GuiInput` wiring; they live inside the SubViewport (SubViewportContainer forwards mouse input; Controls work under Node2D parents with manual positions). They draw nothing, so the CanvasModulate multiply is invisible on them.
- Labels stay `Label`s (RenderedText's Collect only reads Label/Button/ItemList — `UiTestSupport.cs:57-74`); they now get ambience-multiplied. QA readability at Evening (×0.45); if unreadable, the escape hatch is a `CanvasLayer` inside the SubViewport (separate canvas = unmodulated) — option, not default.

**`TintFor` — alpha table → multiply table** (pilot-proven values, `LitTavernPilot.cs:17-23`):

```csharp
public static Color TintFor(DayPhase phase) => phase switch
{
    DayPhase.Morning    => new Color(1.00f, 0.92f, 0.78f),
    DayPhase.Expedition => new Color(1.00f, 1.00f, 1.00f),
    DayPhase.Evening    => new Color(0.45f, 0.45f, 0.70f),
    _                   => new Color(1f, 1f, 1f),           // unknown/future phases: neutral (Camp/Deep rows land in V5b)
};
```
`CurrentTint` now reads `_ambient.Color` — same property, same `TintFor` comparison, tests pass mechanically.

**PointLight2D placements (all Height ≈ 30, radial `GradientTexture2D` per the pilot):**

| Light | Position | Color | Energy | Behavior |
|---|---|---|---|---|
| ForgeEmber | forge mouth (~450, 140) | (1, 0.75, 0.45) | 1.2 base | pilot flicker: `1.2 + 0.15·sin(9t)·sin(2.3t)` — deterministic sin of town time, **no RNG** (KTD2 hygiene even though presentation-only) |
| TavernWindow | (~720, 130) | (1, 0.80, 0.50) | 1.0 | steady |
| GateArcane | (~905, 300) | arcane `#6b4c9a` → (0.42, 0.30, 0.60) | 0.6 | steady |
| MemorialMoon | (~70, 80) | bone-pale (0.85, 0.81, 0.90) | 0.5 | steady (phase-modulation is V5b polish) |

**`HeroSprite` Control → Node2D:** re-parent class to `Node2D`. Children: `Sprite2D "Sprite"` (texture = `SpriteFor(classId)` fallback chain from V3, `Modulate = RoleColor(classId)` — unchanged P3 pattern reading `ClassDefinition.ColorRgb`), `ColorRect "Marker"` (kept — the pinned role→color contract and its test), `Label "NameLabel"` (kept), plus new invisible `Control "HitRect"` (64×34 = the old Control size, `MouseFilter.Stop`, exposed as `public Control HitRect`), with `TownScene.ReconcileSprites` moving the `GuiInput` subscription onto it. State machine, `WalkSpeed`, lissajous `WanderOffset`, `Advance`, `SetAway/SnapHome/BeginDeparture/BeginReturn` are untouched — `Position`/`Visible` have identical semantics on Node2D.

**TownSceneTests bridge strategy** (same-PR mechanical updates only — public API preserved so most tests don't change):

| Test binding | Fate |
|---|---|
| `Sprites` dict, `State`, `Visible`, counts, `Animate(10)` walk math, `MemorialStoneCount`, wipe-day, Return-Ritual | **unchanged** |
| `Find<ColorRect>(sprite, "Marker")` (:54) | **unchanged** (Marker kept as ColorRect) |
| `Find<TextureRect>(sprite, "Sprite")` (:87) | → `Find<Sprite2D>(sprite, "Sprite")`; `.Modulate`/`.Texture` asserts identical |
| `Click(ui.Town.Sprites[...])` (:246) | add `UiTestSupport.Click(HeroSprite s) => Click(s.HitRect)` overload — call sites unchanged |
| `Find<Control>(ui.Town, "Building_Forge")` + `Click` (:263-270) | **unchanged** (hit-rects keep names + GuiInput) |
| `CurrentTint == TintFor(phase)` (:67) | **unchanged** (values differ, comparison identical) |
| `RenderedText` memorial-name asserts (:61-64) | **unchanged** (Labels kept) |
| New asserts | town world root is `SubViewport`; exactly one `CanvasModulate` under it, `Color == TintFor(state.Phase)`; each building Sprite2D's texture is `CanvasTexture` with non-null `NormalTexture` (art present) **or** the SVG fallback (art absent); every `PointLight2D.Height > 0` |

Also in V4b: delete `lit_tavern_pilot.tscn` + `LitTavernPilot.cs` (throwaway by design, now superseded — no orphans) and update `graphics-2.5d-direction.md` engine-steps status.

---

## Phase 3 — hero figures (V3): neutral base + ColorRgb tint

Two PRs per the describe/generate lifecycle. **Ownership split (per the operating model):** the **describe** PR is an **addon-swarm packet** (`addon-art-heroes` — spec modules are swarm territory, `art/specs/**` is denied to VISUALS); the **generate** PR is VISUALS (master art-Claude, single generation tiller).

**V3-specs (swarm packet `addon-art-heroes`; merges green immediately, no pixels):** new module file `art/specs/heroes/HeroSpecs.cs` (`IAssetModule` — reflection registry picks it up, no shared edit; claim `addon-art-heroes` in `.claude/tasks/`). Three `Active`-track specs, ids `hero-vanguard`, `hero-striker`, `hero-mystic`:

```
Kind: ClassFigure, NeutralBaseTint: true, ClassId: "<id>" (plain hint string — never resolved
against ClassRegistry at conformance time), NormalMap: true, Width: 512, Height: 768
Subject (vanguard): "a single armored warrior figure standing, tower shield and blade,
  full body, front three-quarter view, clear readable silhouette"
Subject (striker):  "... lean duelist figure, twin daggers, poised stance ..."
Subject (mystic):   "... robed spellcaster figure, staff, hood ..."
PromptExtra (all):  "desaturated pale bone-grey clothing and armor, neutral monochrome
  figure, no colored accents"   // Modulate MULTIPLIES — a light neutral base is what
                                 // makes ColorRgb (69,130,181 / 219,20,61 / 138,43,227) read true
```

Gate: `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` (id grammar, uniqueness, track bounds — the describe-PR definition of done, already in CI).

**V3-gen (VISUALS, after O1 + V3-specs; master-art-Claude lifecycle as V2):** generate (seed = `SeedFor(id)`, rerolls +n), curate for **silhouette consistency across the trio** (they stand side-by-side in town), cutout `--trim`, normal maps, commit pairs + build-halves. Then extend `IconRegistry`:

```csharp
/// <summary>Lit generated class figure with SVG fallback: Lit("hero-{classId}") when the
/// PNG exists, else the hand-authored SVG (new add-on classes keep working art-less).</summary>
public static Texture2D SpriteFor(string classId) =>
    (Texture2D?)Lit($"hero-{classId}") ?? Sprite(classId);
```
`HeroSprite.Setup` switches to `SpriteFor`. Tests: `SpriteFor` returns CanvasTexture for the three built-ins, returns SVG for an unknown-art class id; existing `HeroSprite_HasRoleTintedFigureTexture` (Modulate == RoleColor) unchanged. The fan-out win stays intact: an add-on class with no PNG gets SVG + tint for free; with a PNG named `hero-<classId>` it gets a lit figure with zero code.

---

## Phase 4 — V5b: real 5-phase ambience + choreography (after V4b **and** staged-plan U3)

**Coordination contract with the AI-NPC lane:** `DayPhase` gains `Camp = 3`, `ExpeditionDeep = 4` in the orchestrator's contracts micro-PR (staged plan U1); the kernel goes 5-tick in U2 (gated on this plan's V5a); results park in-flight at Camp starting U3. The VISUALS lane **never edits `Contracts/`** — it consumes those exact enum names.

1. `TintFor` gains the committed rows: `Camp => (0.85, 0.80, 0.95)` (lavender lull), `ExpeditionDeep => (0.60, 0.60, 0.85)`.
2. **Choreography fix:** under staged resolution, survivors surface after **Deep**, not Expedition (`PendingExpeditions` is only final then — at Expedition-complete the run is parked `InFlight`, so the current query at `TownScene.cs:118-130` would strand staged parties' sprites Away until Evening). Move the walk-back arm from `case DayPhase.Expedition` to `case DayPhase.ExpeditionDeep`; Camp completion → no movement (optionally dim ForgeEmber energy — the town holds its breath during the camp window; attribution-pride thesis says sell the decision moment).
3. `PhaseClock.DurationOf`: explicit Camp/Deep durations (Camp long enough to actually use the decision window at 1×; propose CampSeconds ≈ Morning, tune with Brian).
4. Tests: full 5-tick day walk — Morning-complete departs, Expedition-complete does **not** return anyone (parked in-flight), Deep-complete walks exactly the final survivors in, Evening-complete snaps + reveals; ambient color asserted per phase against `TintFor`; wipe-day still can't hang the Ledger.

---

## Test scenarios per unit (definition of done)

| Unit | Must pass |
|---|---|
| V5a | full engine suite green with the loop-until-Morning `AdvanceDay` at all 20 call sites (TownSceneTests 8 / MainUiTests 9 / SimAdapterTests 3); beyond-max unknown-phase no-op test green; BOARD gate G2 flipped — **staged-plan U2 unblocked with zero `godot/` edits by the AI-NPC lane** |
| O1 | all 3 CI lanes green; engine-tests job materializes LFS PNGs (`git lfs ls-files` shows the tavern pair); sim/balance lanes unaffected |
| V1 | scripts run end-to-end locally on `town-tavern.png` (documented smoke in README); no CI wiring; HF revision pinned |
| V2 | fast lane + `GameArt.Tests` green; `IconRegistryTests` extended: `Art`/`Lit` non-null for all 4 ids + `_n`s; moving-light QA screenshots attached to PR; 4 build-half JSONs present incl. tavern backfill, sha256s match committed bytes |
| V3-specs | `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` green (that alone) |
| V3-gen | `SpriteFor` chain tests + existing tint test green |
| V4a | `Lit` null-tolerance tests green on 4.6.3 |
| V0 | engine suite green ×2 on 4.7.1 local + CI; no `net8.0` anywhere; `.import` diff reviewed by VISUALS |
| V4b | entire `TownSceneTests` suite green with only the mechanical updates in the bridge table; new SubViewport/CanvasModulate/normal-texture/light-height asserts green; silent-skip guard (`ci.yml:75-78`) still passes; pilot files deleted |
| V5b | 5-tick choreography test green; balance/golden untouched (no sim edits by definition) |

## Deny-list touches = handoffs (exhaustive)

| Touch | Unit | Author → Merger | File(s) |
|---|---|---|---|
| LFS attributes + CI checkout | O1 | ENGINE → orchestrator | `.gitattributes`, `.github/workflows/ci.yml` |
| Engine pin flip | V0 | ENGINE (VISUALS verifies) → orchestrator | `.godot-version`, `ci.yml:48`, `.runsettings`, `godot/project.godot`, `GodotClient.Tests.csproj`, `CLAUDE.md` rule 2 |
| TownScene placement ownership | V4b | VISUALS (explicit grant in the V4b claim file — `art-pipeline-architecture.md` §7 amendment per operating model §9) | `godot/scripts/town/TownScene.cs`, `godot/scenes/town/town_scene.tscn` |
| Hero spec module | V3-specs | addon swarm → auto-merge (own dir, conformance-green) | `art/specs/heroes/HeroSpecs.cs` |
| (none) | — | — | `sim/GameSim/Contracts/` — the DayPhase append is the orchestrator's staged-plan U1; this plan only consumes it |

## What the VISUALS-lane Claude may NOT touch — ever, under this plan

`sim/GameSim/**` (any file — no exceptions; KTD2), `sim/GameSim.Tests/**`, `Game.sln`, `.github/**`, `godot/project.godot`, `CLAUDE.md`, `global.json`, `Directory.Build.props`, `.godot-version`, `.gitattributes` (ENGINE authors, orchestrator merges), `art/GameArt/**` + `art/GameArt.Tests/**` (schema/conformance — orchestrator; nothing in this plan needs a schema change, `AssetSpec` already carries every field used), `art/palettes/**`, `art/specs/**` (swarm-owned — including the V3-specs packet), `art/pipeline/models.lock.json` (orchestrator, when it exists), `godot/scripts/panels/**` + `godot/scenes/panels/**` (U11 surface — untouched by design; `MainUi.cs` likewise), and any other unit's claimed directory. Never open `godot/` in any editor version other than the current `.godot-version` pin (hard rule 2 — the single most expensive mistake available in this lane).

## Kill risks

1. **gdUnit4Net never ships stable 4.7 support on a useful timeline** → V4b indefinitely gated. Mitigation: fallback pilot-scenes de-risk everything except the migration itself; escalate to Brian if blocked > ~6 weeks — dropping the adapter for a direct runner scene is an orchestrator-level infra decision, not this lane's call.
2. **Sobel normals read embossed/tinny on the three new buildings** (known low-relief risk). Mitigation built into V2 step 4 (moving-light QA) + the optional Laigter pass; if a building still fails, hand-author its height map in Krita before commit — never ship a flat-reading normal.
3. **Evening multiply (×0.45) makes in-world labels unreadable.** Escape hatch documented (CanvasLayer inside the SubViewport); decide at V4b QA, not before.
4. **Cross-lane timing:** if staged-plan U2 tries to land before V5a, engine tests go red on its PR (and the ordering rule says it must not open until BOARD G2 flips). Mitigation: V5a is deliberately tiny and unblocked — it is this plan's first action and only hard inter-lane deadline.
