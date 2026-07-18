---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-brainstorm
origin: docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md
title: Art Pipeline & Wiring - Plan
date: 2026-07-18
---

# Art Pipeline & Wiring — Plan

> **Plan #2 of 4** in the next-phase wave. Product authority is the origin scope doc
> (`docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md`). This plan owns the **art-wiring**
> requirement band **R8, R9, R10** and shares the cross-cutting invariant **R14**. It sits between
> Plan #1 (Playable Core) and Plan #3 (UI Rethink): the rethink consumes the loadable-art seam this
> plan builds, so this lands after Plan #1 and before Plan #3.

---

## Goal Capsule

**Objective.** Make the authored art *real and reachable*: fix the fresh-checkout render gap so
committed art shows up without a manual step (R8), generate the high-value authored specs to pixels
and commit them (R9), and expose all art to the gameplay UI through a by-id catalog/manifest seam
the Godot layer consumes instead of hand-placing textures per scene (R10). All work is
presentation + tooling; the sim is never touched (R14).

**Product authority.** `docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md` (R8/R9/R10/R14,
KD-none-specific; resolves **OQ3**). This plan does not redefine scope.

**Open blockers.** **None.** OQ3 (is the local ComfyUI AssetSpec→prompt→PNG path healthy end-to-end?)
is **resolved GO** by the U1 verification performed during planning: ComfyUI 0.28.0 is up on
`http://127.0.0.1:8188` (IPv4), RTX 5080 / 15.9 GB VRAM, `sd_xl_base_1.0.safetensors` present, queue
empty, no recent errors; the committed `art/pipeline/` cutout + normal-map scripts are proven on the
tavern pilot; and a prior curated run already produced 7 committed pairs. U1 re-confirms this at
implementation time and freezes the go/no-go before any generation unit runs.

---

## Summary

The repo has **77 authored `AssetSpec` records** across 9 fan-out modules (`art/specs/**`), a pure
reflection-built `AssetRegistry`, a single-source prompt composer (`ArtTrackProfiles.ComposePrompt`),
and a proven local generation pipeline (ComfyUI SDXL → `cutout.py` BiRefNet → `normalmap.py` Sobel →
Godot import). Only **7 of those 77** specs have been generated to pixels (the town buildings + three
hero figures), and **6 of the 7 committed PNG pairs lack `.import` sidecars**, so `GD.Load` returns
`null` on a fresh checkout and the art is invisible in play unless a headless import is run first.
Meanwhile the gameplay panels render generic slot SVGs, not the authored per-item / per-monster /
per-venue art, and there is no by-id catalog the UI can query.

This plan: (U1) re-verifies pipeline health and reconciles the one settings drift; (U2) makes
committed art render from a clean checkout via a durable sidecar-commit + launcher auto-import
convention (R8); (U3) builds the by-id **AssetCatalog + generated manifest** seam that generalizes
`IconRegistry.Art/Lit` (R10); (U4–U6) generate + commit the high-value pixels — **39 item icons, the
5 Mine monster portraits, and the 4 extra-venue backdrop/entrance pieces** (R9); (U7) regenerates the
manifest over everything committed and proves end-to-end that gameplay-critical ids resolve. Nothing
here touches `sim/GameSim`, `GameState`, saves, or chronicles (R14).

---

## Problem Frame

From the origin doc's playtest findings, the art-wiring failures are:

- **Art invisible in play.** Generated PNGs are wired only into the Town-tab lit backdrop; the six
  management panels use placeholder programmatic controls; most authored specs were never generated;
  and most committed PNGs lack `.import` sidecars so they don't even render locally. CI masks the
  sidecar gap because the headless runner imports before testing.
- **No by-id consumption seam.** `IconRegistry.Art(name)` / `.Lit(id)` load a texture by literal file
  name from a fixed directory, and only `TownScene`/`LitTownOverlay` call them. There is no semantic
  resolver (recipe→icon, monster→portrait, venue→backdrop) and no manifest of *what art exists*, so
  every gameplay screen would have to hand-wire paths — exactly what Plan #3 must not do.
- **Most authored pixels don't exist.** 70 of 77 specs are still describe-only. The craft/shop/venue
  screens Plan #3 builds have nothing on-palette to show for items, creatures, or the extra venues.

Grounding facts verified this session (read-only):

| Fact | Evidence |
|---|---|
| 77 authored specs, 9 modules | `art/specs/**` (`items` 24+15, `heroes` 3, `monsters` 5, `gloomwood` 8, `sunkencrypt` 8, `props` 8, `town` 4, `factions` 2) |
| 7 committed to pixels | `godot/assets/art/`: town-forge/market/mine-gate/tavern + hero-vanguard/striker/mystic (each + `_n`) |
| 6/7 pairs lack sidecars | only `town-tavern.png.import` + `town-tavern_n.png.import` exist |
| Pipeline healthy | ComfyUI 0.28.0, RTX 5080, `sd_xl_base_1.0.safetensors`, queue empty (health_check) |
| Scripts proven | `art/pipeline/{cutout.py,normalmap.py}` (tavern pilot PR #32) |
| Settings drift | `ArtTrackProfiles.Active.Steps = 28` (authoritative) vs `art/pipeline/README.md` table `steps 25` |
| LFS scope correct | `.gitattributes`: `godot/assets/art/**/*.png filter=lfs` |

---

## Requirements

This plan **satisfies R8, R9, R10** in full and **upholds R14** as a hard guardrail.

- **R8** — Committed art renders from a fresh local checkout with no manual pre-import step (or a
  documented, automated import step the launcher runs). → **U1, U2, U7**.
- **R9** — High-value authored specs (item icons, venue backdrops, hero/monster portraits) are
  generated to pixels and committed. → **U4 (39 item icons), U5 (5 Mine monster portraits), U6 (4
  extra-venue backdrop/entrance pieces)**; gated by **U1**.
- **R10** — Generated art is loadable by the gameplay UI through a manifest/registry the Godot layer
  consumes, not hand-placed per scene. → **U3 (AssetCatalog + generated manifest)**, proved in **U7**.
- **R14** — All sim-purity/determinism invariants hold: zero Godot refs in `sim/GameSim`; no
  RNG/wall-clock/transcendental math added to sim; golden-replay + gold-conservation untouched;
  presentation/tooling never writes `GameState`, saves, or chronicles. → **every unit** (each states
  its surface below; **no unit touches `sim/GameSim`**).

Requirements owned by sibling plans and explicitly **out of scope here**: R1–R7 (Plan #1
playability), R11–R12 (Plan #3 UI theme + screens), R13 (Plan #4 FlavorForge), R15 (test-coverage
posture — this plan adds engine coverage for its own seams but the overall posture is Plan #1/#3).

---

## Key Technical Decisions

- **KTD-A — Verify-first, generation contingent (resolves OQ3).** U1 is a gate, not a formality: a
  health re-check + a tiny throwaway smoke generation confirm the AssetSpec→`ComposePrompt`→ComfyUI→
  cutout→normalmap→PNG path end-to-end *before* U4–U6 commit any GPU time. If health regresses at
  implementation time, generation scope shrinks to what can be produced and the manifest/seam units
  (U2, U3, U7) still land — they are pipeline-independent. Rationale: the origin doc makes generation
  scope explicitly contingent on pipeline health.
- **KTD-B — Launcher auto-import is the load-bearing R8 fix; committed sidecars pin the uids.** A
  fresh checkout without the `.godot/imported/*.ctex` cache cannot `GD.Load` a texture even with a
  committed `.import`, so the durable guarantee is a `godot --headless --import --quit` pass in
  `play.bat` (and documented for CI/manual runs). Committing the `.import` sidecars on top makes
  `uid://`s stable and deterministic across machines so name/uid references never churn. We do
  **both** (the origin doc's "AND/OR"): auto-import for correctness, sidecars for stability.
- **KTD-C — R10 seam = typed by-id resolvers over the existing null-tolerant loader + a generated
  presence manifest.** Rather than a new dotnet project (which would force a deny-listed `Game.sln`
  edit), R10 is delivered as (1) an `AssetCatalog` of typed, id-composing resolvers
  (`ItemIcon(recipeId)`, `MonsterPortrait(kind)`, `VenueBackdrop(venueId)`, `HeroPortrait(classId)`,
  `VenueEntrance(venueId)`) that compose the documented id convention (`item-<recipeId>`,
  `monster-<slug>`, `<venue>-backdrop`, `hero-<classId>`) and call the *existing* null-tolerant
  `IconRegistry.Art/Lit`; plus (2) a committed generated JSON manifest (`godot/assets/art/art-manifest.json`)
  listing every committed asset id and whether it has diffuse/normal pixels. The id-composition *is*
  the mapping; the manifest is the authoritative "what exists" list for presence checks + coverage
  tests + Plan #3 enumeration. Rationale: reuses the verified deadlock-avoidance keystone
  (name-bound, null-tolerant — architecture doc §5), adds the semantic layer the UI needs, and avoids
  touching deny-listed infra.
- **KTD-D — Generate the legibility-critical triad now; defer the long tail.** U4–U6 target the 48
  assets that most directly make the craft/shop/venue loop legible (item icons for the storefront +
  craft cards, Mine creatures for the default venue, the two extra-venue establishing pieces for the
  venue-map hub). Venue floor monsters (9), venue/town props (11), and faction crests (2) are the
  long tail — same proven pattern, lower ROI — deferred to follow-up. Rationale: right-sizes the
  single-tiller GPU lane and front-loads what Plan #3 renders first.
- **KTD-E — Committed-PNG-as-source-of-truth; provenance recorded, never regenerated on demand.**
  Per the architecture doc §5, the reproducibility guarantee is the committed PNG + its sha256, not
  the seed (SDXL is not byte-reproducible across GPUs). Each generation unit records seed/model/
  sampler/sha in `art/build/<id>.build.json` and `seeds.generated.md`. This mirrors golden-replay
  for art. Reference: origin R14 (determinism family) and `docs/design/art-pipeline-architecture.md`.

Settings of record (frozen inputs for U4–U6, from `ArtTrackProfiles`): SDXL
`sd_xl_base_1.0.safetensors`, **steps 28** (Active) / 32 (Painterly — not used this plan), **cfg 6.5**,
**dpmpp_2m / karras**, first candidate seed = `AssetSeed.SeedFor(id)`, re-rolls `seed+n`.

---

## High-Level Technical Design

The pipeline is a linear, single-tiller producer feeding a fan-out, null-tolerant consumer. The two
seams this plan adds are the **launcher import pass** (R8) and the **AssetCatalog + manifest** (R10).

```mermaid
flowchart LR
  subgraph authored["art/ (describe half — already exists)"]
    S["AssetSpec (art/specs/**)"] --> CP["ArtTrackProfiles.ComposePrompt<br/>+ AssetSeed.SeedFor(id)"]
  end
  subgraph gen["Generation (single-tiller, U4-U6) — local, never in CI"]
    CP --> CU["ComfyUI SDXL<br/>28 steps / cfg 6.5 / dpmpp_2m karras"]
    CU --> CO["cutout.py (BiRefNet)"]
    CO --> NM["normalmap.py (Sobel, if NormalMap)"]
    NM --> IMP["Godot import on pinned 4.6.3<br/>-> .png + _n.png + .png.import (LFS)"]
    IMP --> BJ["art/build/&lt;id&gt;.build.json<br/>seed/model/sha/uid (provenance)"]
  end
  subgraph consume["godot/ (adapter — R8 + R10 seams)"]
    IMP --> MAN["art-manifest.json (generated, U3)"]
    MAN --> AC["AssetCatalog: ItemIcon / MonsterPortrait /<br/>VenueBackdrop / HeroPortrait (U3)"]
    AC --> UI["gameplay panels & Plan #3 screens<br/>load by id, null-tolerant"]
    LB["play.bat --headless --import (U2)"] -.materializes .ctex.-> UI
  end
  style gen fill:#2a1a1a,stroke:#a55
  style consume fill:#1a2436,stroke:#57a
```

**Surface map (R14 guardrail, per band):** `art/specs/**` + `art/build/**` + `art/pipeline/**` +
`godot/assets/art/**` + `godot/scripts/**` + `godot/tests/**` + `play.bat` + `docs/**` only.
**No unit reads or writes `sim/GameSim`, `GameState`, saves, or chronicles.** Art is presentation
data; the golden-replay and gold-conservation tests are regression guards, not targets.

---

## Implementation Units

Dependency order: **U1 → U2 → U3 → (U4 ∥ U5 ∥ U6) → U7**. U2 and U3 are pipeline-independent and may
proceed even if U1 down-scopes generation. U4–U6 share the single GPU (serial in practice) and each
depends on U1 (health), U2 (import convention), and U3 (manifest tool). U7 closes the loop.

---

### U1. Pipeline health verification + settings reconciliation (resolve OQ3)

**Goal.** Prove the AssetSpec→prompt→PNG path is healthy end-to-end and freeze the generation go/no-go;
reconcile the one documented settings drift so U4–U6 render at the true ship settings.

**Requirements.** R8 (unblocks the generation half), R9 (gate), R14.

**Dependencies.** None (entry point).

**Surface.** `art/pipeline/` + `docs/` only — **dev-tooling + docs, zero sim, zero runtime.**

**Files.**
- modify `art/pipeline/README.md` — reconcile the SDXL settings table to **steps 28** (match
  `ArtTrackProfiles.Active`, the single authoritative source); note the locally-present
  `Hyper-SDXL-8steps-CFG-lora.safetensors` is an **optional fast-draft aid, not the ship setting**
  (shipped art is 28-step base, no LoRA, per the committed profile + prior curated pairs); also fix
  the phantom `models.lock.json` reference (README line ~11) — no such file exists in
  `art/pipeline/`; either drop the line or create the pin file as part of this unit.
- create `docs/design/art-pipeline-health-2026-07-18.md` — a short verification record: ComfyUI
  version/GPU/VRAM/queue, checkpoint presence, script proof, the smoke-gen result, and the explicit
  **OQ3 = GO / GO-with-reduced-scope / NO-GO** verdict feeding U4–U6.

**Approach.** Run `comfyui health_check` (verify 0.28.0 / RTX 5080 / `sd_xl_base_1.0.safetensors` /
empty queue / no errors). Pick one representative spec (e.g. `item-longsword`), compute
`ArtTrackProfiles.ComposePrompt(spec)` + `ComposeNegative` + `AssetSeed.SeedFor(id)`, generate **one**
throwaway candidate via the ComfyUI MCP at the frozen settings, run it through `cutout.py` (+
`normalmap.py` for a normal-mapped spec) to confirm the full chain produces a valid RGBA + `_n` pair.
Discard the smoke output (candidates are gitignored). Record the verdict. If NO-GO, U4–U6 are
descoped to whatever renders and the seam units still proceed.

**Execution note.** The smoke generation is *verification only* — its bytes are never committed. The
committed deliverable is the health record + the README reconciliation, not pixels. **Provenance
reality check (verified 2026-07-18): `art/build/` is EMPTY — no `build.json` exists for ANY of the 7
committed assets, and `seeds.generated.md` has only 6 rows (town-tavern missing), every row
"(pending build-half)".** U1 must therefore *define* the `build.json` schema in the health doc (id,
track, seed, model, sampler, scheduler, DiffuseSha256, NormalSha256?, uid, status) so U4–U6 mint it
consistently; U7 backfills the 7 legacy assets. Also: health probe showed only ~8.1 of 15.9 GB VRAM
free — run `clear_vram`/restart before each 1024² generation batch, and use a checkpoint-loader-only
workflow for the smoke gen (the instance's vae/text_encoder/diffusion_model dirs are empty; separate
loader nodes will fail).

**Patterns to follow.** `docs/design/art-pipeline-architecture.md` (doc voice + verification-record
style); `art/pipeline/README.md` §1 (the settings table being corrected).

**Test scenarios.** Test expectation: none — dev-tooling/docs verification unit with no committed
behavioral surface. The "test" is the recorded end-to-end smoke pass captured in the health doc.

**Verification.** `comfyui health_check` returns healthy; the throwaway spec renders through the full
chain to a valid diffuse+normal pair; `art/pipeline/README.md` states steps 28; the health doc records
an explicit OQ3 verdict. No sim/engine tests are affected (docs-only commit).

---

### U2. Import-sidecar convention + launcher auto-import (R8)

**Goal.** Committed art renders from a clean checkout with no manual step: pin `.import` sidecars for
the existing committed pairs and add a durable, automated headless-import pass to the launcher.

**Requirements.** R8, R14.

**Dependencies.** None (pipeline-independent; may land in parallel with U1).

**Surface.** `godot/assets/art/**` (import sidecars) + `play.bat` + `docs/` + `godot/tests/**` —
**adapter + tooling, zero sim.**

**Files.**
- create `godot/assets/art/{town-forge,town-market,town-mine-gate}.png.import` +
  `{...}_n.png.import` and `{hero-vanguard,hero-striker,hero-mystic}.png.import` + `{...}_n.png.import`
  — the 12 missing sidecars, minted by the **pinned Godot 4.6.3** engine (mirror the shape of the
  existing `town-tavern.png.import`).
- modify `play.bat` — insert a `"%GODOT%" --path "%~dp0godot" --headless --import --quit` pass
  **before** the interactive launch, so a fresh checkout materializes `.godot/imported/*.ctex` for
  every committed PNG regardless of sidecar state.
- modify `godot/assets/art/README.md` — document the durable convention: "every committed pair ships
  its `.png.import`; the launcher runs a headless import pre-pass; regenerate sidecars only on the
  pinned engine."
- create `godot/tests/ArtRenderFreshCheckoutTests.cs` — assert the shipped, committed art ids load
  (see scenarios) so the fresh-checkout render contract is enforced, not assumed.

**Approach.** On the pinned engine, run the headless import once to mint the 12 sidecars; commit them
(LFS unaffected — sidecars are text). Add the pre-pass to `play.bat`. The new engine test locks the
contract that the committed ids resolve to non-null textures. Note the **`.import`/`.uid` churn
gotcha**: if a re-import rewrites uids on unrelated assets, `git checkout -- "godot/assets/art/*.import"`
before committing so only the intended 12 sidecars land.

**Execution note.** Sidecars must be generated on Godot 4.6.3-stable mono only (hard rule #2) — a
non-pinned editor silently rewrites import metadata and breaks CI. Stage exactly the 12 new sidecars
+ `play.bat` + the README; no `git add .`.

**Patterns to follow.** `godot/assets/art/town-tavern.png.import` (the sidecar template);
`godot/tests/IconRegistryTests.cs` (`Lit("town-tavern")` non-null assertion is the exact shape to
extend); `play.bat` (launcher structure).

**Test scenarios.**
- *Happy:* `ArtRenderFreshCheckoutTests` — for each committed id in
  `{town-forge, town-market, town-mine-gate, town-tavern, hero-vanguard, hero-striker, hero-mystic}`,
  `IconRegistry.Art(id)` and `IconRegistry.Lit(id)` return non-null; `_n` siblings load.
- *Edge:* a diffuse present with its `_n` sibling absent → `IconRegistry.Lit(id)` still returns a
  diffuse-only `CanvasTexture` (lights read flat), never null/crash (existing contract, re-asserted).
- *Error:* an unregistered id (`"does_not_exist_yet"`) → `Art`/`Lit` return null (graceful degrade —
  already covered by `IconRegistryTests`; keep green).
- *Integration:* engine-test suite green under `.runsettings` with the import pre-pass, proving the
  contract holds in the headless harness.

**Verification.** `dotnet test godot/tests --settings .runsettings` green (needs Godot); a manual
`play.bat` on a freshly-cloned worktree shows town buildings + hero figures rendered without any hand
import; the 12 sidecars are present and uid-stable.

---

### U3. AssetCatalog + generated manifest seam (R10)

**Goal.** Give the Godot UI a by-id way to load art — typed semantic resolvers over the null-tolerant
loader, backed by a generated manifest of what art exists — so gameplay screens (Plan #3) never
hand-wire texture paths.

**Requirements.** R10, R14.

**Dependencies.** U2 (so end-to-end load tests run against importable art).

**Surface.** `godot/scripts/**` + `godot/assets/art/**` (generated manifest JSON) + `art/pipeline/**`
(manifest generator script) + `godot/tests/**` — **adapter + dev-tooling, zero sim.**

**Files.**
- create `godot/scripts/AssetCatalog.cs` — typed resolvers that compose the id convention and delegate
  to `IconRegistry.Art/Lit`: `ItemIcon(string recipeId)` → `item-<recipeId>`;
  `MonsterPortrait(string kind)` → `monster-<slug>` (and venue-prefixed variants);
  `VenueBackdrop(string venueId)` → `<venue>-backdrop`; `VenueEntrance(string venueId)` →
  `<venue>-entrance`; `HeroPortrait(string classId)` → `hero-<classId>`. Each returns a
  `Texture2D?`/`CanvasTexture?` (null-tolerant) plus a `Has(id)` presence check backed by the manifest.
- modify `godot/scripts/IconRegistry.cs` — factor the id→path load so `AssetCatalog` reuses it; add a
  manifest-backed `Has(id)` fast-path (avoid scattered `ResourceLoader.Exists`). Keep existing
  `Art`/`Lit`/`Building`/`Sprite`/`Ore`/`Glyph`/`Slot` signatures intact (no consumer churn).
- create `art/pipeline/gen-manifest.ps1` (+ a `--check` mode) — a dev-only script that scans
  `godot/assets/art/*.png`, derives ids (strip `_n`, strip `.png`), and emits
  `godot/assets/art/art-manifest.json` = `{ "<id>": { "diffuse": true, "normal": <bool> }, ... }`,
  ordinal-sorted for deterministic diffs. `--check` fails (non-zero) if the on-disk set differs from
  the committed manifest (drift guard for CI/pre-commit).
- create `godot/assets/art/art-manifest.json` — the generated manifest for the 7 currently-committed
  ids (regenerated by U7 after generation).
- create `godot/tests/AssetCatalogTests.cs` — cover the resolver id-composition + null-tolerance +
  manifest-presence contract.

**Approach.** The id-composition convention *is* the AssetId→file mapping (documented in the spec
module headers: `item-<recipeId>`, `monster-<kind>`, `<venue>-backdrop/-entrance`, `hero-<classId>`).
`AssetCatalog` turns a sim concept into an id string and defers to the existing null-tolerant loader,
so a screen asks `AssetCatalog.ItemIcon(recipe.Id)` and gets the icon or a graceful null. The manifest
is the authoritative "what exists" list: `Has(id)` reads it (cheap, no filesystem probe per call) and
Plan #3 / coverage tests enumerate it. The manifest is a **presentation artifact** — generated from
committed pixels, never from `GameState`.

**Execution note.** `AssetCatalog` must not reference `GameSim` types it does not already have access
to; compose ids from primitive strings the caller passes (recipe id, class id, venue id, monster kind
slug), keeping the art lane decoupled from sim registries (architecture doc §7 — the same
`ClassFigure` decoupling rule). Caution on the `IconRegistry` refactor: only `Art`/`Lit` are
null-tolerant — the SVG path (`Load` behind `Building`/`Sprite`/`Ore`/`Glyph`/`Slot`) is a raw
`GD.Load` by contract. Keep the two load paths distinct; do not route the SVG accessors through the
null-tolerant path or their existing non-null test contracts change.

**Patterns to follow.** `godot/scripts/IconRegistry.cs` (`Art`/`Lit` null-tolerant load + the
"bind by name, null-tolerant" keystone); `art/pipeline/normalmap.py` (committed-dev-script voice, arg
handling, `_die` error style — mirror for the PowerShell generator); `godot/tests/IconRegistryTests.cs`
(theory-over-ids test shape).

**Test scenarios.**
- *Happy:* `AssetCatalog.ItemIcon("longsword")` composes `item-longsword` and returns the committed
  texture once U4 lands (pre-U4: returns null, asserted below); `HeroPortrait("vanguard")` returns the
  committed `hero-vanguard`; `VenueBackdrop`/`VenueEntrance` compose the documented ids.
- *Edge:* `Has("item-longsword")` reflects the manifest exactly (true iff the manifest lists it),
  independent of any live filesystem probe; a diffuse-only entry reports `normal:false`.
- *Error:* an unknown concept (`ItemIcon("no-such-recipe")`) → composed id absent from manifest →
  `Has` false and the loader returns null (graceful degrade, no throw).
- *Integration:* `gen-manifest.ps1 --check` passes against the committed manifest for the 7 seeded ids;
  the resolver id strings round-trip through `IconRegistry.Art` for every committed id.

**Verification.** `dotnet test godot/tests --settings .runsettings` green; `gen-manifest.ps1 --check`
exits 0 on a clean tree; `AssetCatalog` resolves every currently-committed id and gracefully nulls the
not-yet-generated ones. No sim tests affected (`dotnet test ... Category!=Balance` stays green as a
regression guard).

---

### U4. Generate + commit item icons (R9)

**Goal.** Turn the 39 authored item-icon specs into committed on-palette pixels so the storefront,
craft cards, and inventory read true per recipe.

**Requirements.** R9, R14.

**Dependencies.** U1 (health GO), U2 (import convention), U3 (manifest tool).

**Surface.** `godot/assets/art/**` (LFS PNGs + sidecars) + `art/build/**` + `art/pipeline/seeds.generated.md`
+ `godot/assets/art/art-manifest.json` — **art assets + provenance + tooling, zero sim.**

**Files.**
- create `godot/assets/art/item-*.png` (39 diffuse; `ItemSpecs` 24 + `ItemSpecsExtra` 15) — Active
  track, 512×512, **no normal map** (flat menu icons), LFS.
- create `godot/assets/art/item-*.png.import` (39 sidecars, pinned engine).
- create `art/build/item-*.build.json` (39 provenance records: seed/model/sampler/scheduler/
  diffuse sha256/uid/status:locked).
- modify `art/pipeline/seeds.generated.md` — append the 39 rows (id, track, seed+offset, hand-finish,
  build-half link).
- modify `godot/assets/art/art-manifest.json` — regenerate via `gen-manifest.ps1` to include the 39
  ids (`normal:false`).

**Approach.** For each `item-*` spec: `ComposePrompt`/`ComposeNegative` + `SeedFor(id)` first
candidate → generate 8–16 via ComfyUI at the frozen Active settings (28/6.5/dpmpp_2m/karras, 512²) →
palette-clamp → curate hard (60–90% reject; icons want a clean centered subject on plain dark
background) → `cutout.py --trim` for a content-tight RGBA (no `normalmap.py` — icons are flat) → import
on the pinned engine → write build-half + seeds row → regenerate manifest. Ids are
`item-<recipeId>` for real `ProfessionRegistry.AllRecipes` keys, so `AssetCatalog.ItemIcon(recipeId)`
resolves them directly.

**Execution note.** Single-tiller GPU — this runs serially with U5/U6. Batch by profession family to
keep palette coherent (`house` for metal/mechanical/arcane, `hearth` for leather/consumables, per the
spec headers). **`.import`/`.uid` churn:** run `git checkout -- "godot/assets/art/*.import"` for
untouched assets before committing so only this batch's sidecars land. Candidates stay under the
gitignored `art/pipeline/candidates/`.

**Patterns to follow.** `art/pipeline/README.md` (the §1–§4 runbook); `art/pipeline/cutout.py`
(`--trim`); the committed `hero-*`/`town-*` pairs + `seeds.generated.md` rows (provenance format);
`art/specs/items/ItemSpecs.cs` (the id↔subject source of truth).

**Test scenarios.** Test expectation: none behavioral — art bytes are curated assets, not
unit-testable logic. Coverage is (a) the U3 `gen-manifest.ps1 --check` drift guard, (b) the lock/QA
gate below, and (c) U7's end-to-end resolve test. The pipeline scripts are already covered by their
own `--help`/proven-math contracts.

**Verification.** All 39 `item-*` diffuse PNGs + sidecars committed via LFS; each has a
`build.json` with a non-zero seed and a diffuse sha256 that matches on-disk bytes; `seeds.generated.md`
lists all 39; `art-manifest.json` regenerated and `--check`-clean; **visual QA** — each icon reads as
its recipe's material/tier on a plain dark background at 512²; `AssetCatalog.ItemIcon(recipeId)`
resolves for a spot-check set. `dotnet test godot/tests --settings .runsettings` green.

---

### U5. Generate + commit Mine monster portraits (R9)

**Goal.** Generate the 5 Mine floor-boss creatures (the default venue's monsters) as lit world
sprites so the venue/combat surfaces show the real enemy, not a placeholder.

**Requirements.** R9, R14.

**Dependencies.** U1 (health GO), U2 (import convention), U3 (manifest tool).

**Surface.** `godot/assets/art/**` (LFS diffuse+normal + sidecars) + `art/build/**` +
`art/pipeline/seeds.generated.md` + `godot/assets/art/art-manifest.json` — **art assets + provenance,
zero sim.**

**Files.**
- create `godot/assets/art/monster-{cave-rat,tunnel-spider,deep-ghoul,ore-golem,forgeworm}.png` +
  `_n.png` (5 diffuse + 5 normal; Active/Monster/1024, **normal map** for the 2.5D Light2D path), LFS.
- create the 10 matching `.png.import` sidecars (pinned engine).
- create `art/build/monster-*.build.json` (5 provenance records incl. `NormalSha256`).
- modify `art/pipeline/seeds.generated.md` — append the 5 rows.
- modify `godot/assets/art/art-manifest.json` — regenerate to include the 5 ids (`normal:true`).

**Approach.** Same runbook as U4 but with the full **cutout → normalmap** chain (these are lit
creatures): generate 8–16 at 1024² Active settings → clamp → curate for a single centered creature
with a clean readable silhouette → `cutout.py --trim` → `normalmap.py <id>.png <id>_n.png 2.5` → **QA
the normal** (bumps must brighten toward the light; if relief caves, fix the Godot import "Flip Y"
toggle, not the script) → import → build-half + seeds → regenerate manifest. Ids `monster-<kind-slug>`
map to `VenueFloor.MonsterKind` display names so `AssetCatalog.MonsterPortrait(kind)` resolves them.

**Execution note.** Serial with U4/U6 on the one GPU. Normal-map QA is the extra gate here — reuse the
throwaway `Sprite2D + PointLight2D` pilot check from the README §3. `.import`/`.uid` churn handled as
in U4 (`git checkout -- "*.import"` for untouched assets before commit).

**Patterns to follow.** `art/pipeline/README.md` §3 (normal-map + Flip-Y QA); `art/pipeline/normalmap.py`;
the committed `hero-*` pairs (the closest existing normal-mapped, bottom-anchored world sprites);
`art/specs/monsters/MonsterSpecs.cs`.

**Test scenarios.** Test expectation: none behavioral (curated art). Coverage via `gen-manifest.ps1
--check`, the lock/QA gate, and U7's resolve test.

**Verification.** 5 diffuse + 5 normal PNGs + 10 sidecars committed (LFS); build-halves carry matching
diffuse+normal sha256; **normal-map QA passes** (relief stands out under a swept light); manifest
regenerated + `--check`-clean; `AssetCatalog.MonsterPortrait("cave-rat")` etc. resolve. Engine tests
green.

---

### U6. Generate + commit extra-venue backdrops + entrances (R9)

**Goal.** Generate the establishing art for the two non-Mine venues — the Gloomwood and Sunken Crypt
backdrop + entrance pieces — so the venue-map hub (Plan #3) shows real, palette-distinct locations.

**Requirements.** R9, R14.

**Dependencies.** U1 (health GO), U2 (import convention), U3 (manifest tool).

**Surface.** `godot/assets/art/**` (LFS) + `art/build/**` + `art/pipeline/seeds.generated.md` +
`godot/assets/art/art-manifest.json` — **art assets + provenance, zero sim.**

**Files.**
- create `godot/assets/art/gloomwood-backdrop.png` (Backdrop, **no normal**),
  `gloomwood-entrance.png` + `_n.png` (Building, normal), `sunkencrypt-backdrop.png` (no normal),
  `sunkencrypt-entrance.png` + `_n.png` (Building, normal) — LFS. (4 specs → 6 PNGs.)
- create the matching `.png.import` sidecars (pinned engine).
- create `art/build/{gloomwood,sunkencrypt}-{backdrop,entrance}.build.json` (4 provenance records).
- modify `art/pipeline/seeds.generated.md` — append the 4 rows.
- modify `godot/assets/art/art-manifest.json` — regenerate to include the 4 ids.

**Approach.** Same runbook; the **backdrops** use the family palette (`gloomwood`, `crypt`) and are
wide atmospheric planes → generate → clamp → curate for depth/readability → `cutout.py` **without
`--trim`** (backdrops fill the plane; skip normalmap — flat far plane per the spec headers). The
**entrances** are lit foreground buildings → full cutout→normalmap chain with Flip-Y QA. Ids
`<venue>-backdrop` / `<venue>-entrance` resolve via `AssetCatalog.VenueBackdrop(venueId)` /
`VenueEntrance(venueId)`.

**Execution note.** Serial with U4/U5. Two palette families in one batch — keep each venue's pieces
together so the `gloomwood` (moss/verdigris) and `crypt` (bone/cyan) families read distinct.
`.import`/`.uid` churn handled as in U4/U5.

**Patterns to follow.** `art/specs/gloomwood/GloomwoodSpecs.cs` + `art/specs/sunkencrypt/SunkenCryptSpecs.cs`
(ids, palette, Kind — backdrop no-normal vs entrance normal); `art/pipeline/README.md`;
`godot/assets/art/README.md`.

**Test scenarios.** Test expectation: none behavioral (curated art). Coverage via `gen-manifest.ps1
--check`, the lock/QA gate, and U7's resolve test.

**Verification.** 6 PNGs (2 backdrops flat + 2 entrance diffuse + 2 entrance normal) + sidecars
committed (LFS); entrance normals pass Flip-Y QA; backdrops read atmospheric and on-family-palette;
build-halves + seeds rows complete; manifest regenerated + `--check`-clean; venue resolvers return the
committed textures. Engine tests green.

---

### U7. Manifest reconciliation + end-to-end wiring verification (R9, R10, R8)

**Goal.** Close the loop: regenerate the manifest over everything committed by U2/U4/U5/U6, and prove
the full R8+R10 contract — gameplay-critical ids render from a fresh checkout through the AssetCatalog.

**Requirements.** R8, R9, R10, R14.

**Dependencies.** U3 (seam), U4, U5, U6 (the committed pixels).

**Surface.** `godot/assets/art/art-manifest.json` + `godot/tests/**` + `docs/` — **adapter + tests +
docs, zero sim.**

**Files.**
- modify `godot/assets/art/art-manifest.json` — final regeneration over the full committed set (7
  seeded + 39 items + 5 monsters + 4 venue = 55 ids).
- create `godot/tests/ArtWiringCoverageTests.cs` — assert the by-id seam resolves the
  gameplay-critical committed set and that `Has(id)` matches the manifest (see scenarios).
- modify `docs/design/art-pipeline-health-2026-07-18.md` (or a short companion) — record the final
  committed inventory + what was deferred, so any session sees coverage at a glance.

**Approach.** Run `gen-manifest.ps1` once more; commit the reconciled manifest. **Backfill
provenance for the 7 legacy committed assets**: mint their `art/build/<id>.build.json` records per
the U1 schema (seed from `seeds.generated.md` where recorded, sha256 from on-disk bytes) and add the
missing `town-tavern` row to `seeds.generated.md`, replacing the six "(pending build-half)" cells —
otherwise the "final committed inventory with full provenance" claim is unachievable. Add a coverage test
that walks a curated list of gameplay-critical ids (a representative recipe icon per profession, each
Mine monster, each committed venue backdrop/entrance, each hero) and asserts `AssetCatalog` resolves a
non-null texture and `Has(id)` agrees with the manifest — the executable proof that "the UI can load
art by id from a fresh checkout" (R8 + R10 together). This is the seam Plan #3 builds on.

**Execution note.** Keep the coverage list data-driven (theory over an id array) so adding future
generated assets (the deferred long tail) extends coverage by one array entry, not new test code.

**Patterns to follow.** `godot/tests/TownSceneTests.cs` (the `IconRegistry.Lit(id).IsNotNull()`
assertions over building/hero id arrays — the exact shape to generalize); `godot/tests/IconRegistryTests.cs`.

**Test scenarios.**
- *Happy:* `ArtWiringCoverageTests` — for each gameplay-critical committed id, `AssetCatalog` returns
  non-null and `Has(id)` is true.
- *Edge:* a manifest entry with `normal:false` (item icon, backdrop) → `Lit(id)` still yields a
  diffuse-only `CanvasTexture`; a `normal:true` entry (monster, entrance, hero) → `Lit(id)` carries a
  normal texture.
- *Error:* a deferred/ungenerated id (e.g. a `props-*` or `factions-*` id) → absent from manifest,
  `Has` false, resolver null — graceful degrade proven, not a regression.
- *Integration:* `gen-manifest.ps1 --check` clean against the committed manifest; full engine suite
  green under `.runsettings`; a manual `play.bat` on a fresh clone shows the new item/monster/venue
  art in the relevant surfaces without a hand import.

**Verification.** `dotnet test godot/tests --settings .runsettings` green including the new coverage
test; `gen-manifest.ps1 --check` exits 0; the health/inventory doc lists the final committed set and
the deferred tail; `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`
green (regression guard — no sim change). R8 + R9 + R10 demonstrably satisfied end-to-end.

---

## Verification Contract

**Gate commands (must pass before any unit is reportable as done):**

```bash
# Sim fast lane — regression guard only (this plan touches NO sim; must stay green — R14)
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance

# Engine tests — the real target for U2/U3/U7 (needs Godot 4.6.3-stable mono via .runsettings)
dotnet test godot/tests --settings .runsettings

# Manifest drift guard (dev-only; U3+)
pwsh art/pipeline/gen-manifest.ps1 --check
```

**Per-band evidence:**
- **R8** — `play.bat` on a fresh clone renders committed art with no manual import; the 12 new
  sidecars committed; `ArtRenderFreshCheckoutTests` green.
- **R9** — 48 assets committed via LFS (39 item icons + 5 Mine monsters + 4 venue pieces = 55 PNGs
  incl. normals), each with a `build.json` (non-zero seed + matching sha256) and a `seeds.generated.md`
  row; visual + normal-map QA passed.
- **R10** — `AssetCatalog` typed resolvers + `art-manifest.json` present and consumed; `--check` clean;
  `ArtWiringCoverageTests` green.
- **R14** — no diff under `sim/GameSim/**`; golden-replay + gold-conservation untouched; no writes to
  `GameState`/saves/chronicles anywhere in this plan's diff.

## Definition of Done

- OQ3 resolved and recorded (U1); settings reconciled to steps 28.
- Committed art renders from a clean checkout via sidecars + launcher auto-import (U2).
- `AssetCatalog` + generated manifest seam in place and covered (U3, U7).
- The 48 high-value assets generated, curated, and committed with full provenance (U4–U6).
- Both gate suites green; manifest `--check` clean; a manual fresh-clone `play.bat` shows the new art.
- No `sim/GameSim` change; determinism/gold-conservation guards green.
- `docs/` records the committed inventory + deferred tail; `.claude/tasks/` claim released.

---

## Scope Boundaries

**In scope:** pipeline health verification (U1), the fresh-checkout render fix (U2, R8), the by-id
AssetCatalog + manifest seam (U3, R10), and generation of the legibility-critical triad — 39 item
icons, 5 Mine monster portraits, 4 extra-venue backdrop/entrance pieces (U4–U6, R9) — plus the
end-to-end wiring proof (U7).

**Out of scope (owned by sibling plans):** the day-clock/craft-loop playability (Plan #1, R1–R7); the
shared Godot Theme and the rebuilt storefront/roster/craft/venue-map screens that *consume* this seam
(Plan #3, R11–R12); the dev-time FlavorForge generator (Plan #4, R13). This plan delivers the
loadable-art seam and the pixels; Plan #3 arranges them into screens.

### Deferred to Follow-Up Work

- **Art long tail (R9 remainder).** The lower-ROI specs on the identical proven pattern: venue floor
  monsters (Gloomwood 4 + Sunken Crypt 5 = 9), venue props (Gloomwood 2 + Sunken Crypt 1 = 3), town
  props (`props` module, 8), and faction crests (`factions`, 2) — ~22 specs. Each is a `gen → cutout
  → normalmap → import → build.json → manifest-regen` add-on; the U7 coverage list extends by one
  array entry per asset. Fold into a later art wave once Plan #3 shows which surfaces need them.
- **`TownLayoutRegistry` (data-driven placement).** The architecture doc §8 open decision #2 — a
  data record `id → anchor/z/scale` consumed by `TownScene.cs` so *placement* also fans out. Not
  required for R8–R10; a fast-follow that removes the last orchestrator-serial placement edit.
- **`AssetConformanceTests` / lock-gate CI job.** The architecture doc §6 lock gate (on-disk sha256 ==
  build-half, palette-clean, uid-unique) as an enforced CI step. This plan records provenance and runs
  the manifest `--check`; wiring a full lock-gate job into `.github/` is deny-listed
  (orchestrator-only) and deferred to an infra PR.
- **Hyper-SDXL fast-draft LoRA path.** The locally-present `Hyper-SDXL-8steps-CFG-lora` could speed
  curation drafts; the ship settings remain 28-step base. Evaluate as a curation-time optimization
  later, never as the committed provenance.
- **A committed dotnet manifest tool.** If `art-manifest.json` later needs to cross-reference
  `AssetRegistry.All` (e.g. to flag *authored-but-ungenerated* specs), promote the PowerShell
  generator to a `tools/ArtManifest` project — which requires an orchestrator-authored `Game.sln`
  micro-PR (deny-listed). The script suffices for R10 now.

---

## Notes

- **Determinism (R14), restated per surface:** every unit lives in `art/**`, `godot/**` (adapter,
  scripts, tests, assets), `play.bat`, or `docs/**`. **No unit modifies `sim/GameSim`, `GameState`,
  saves, or chronicles.** The art manifest is generated from committed pixels, never from sim state;
  the pipeline scripts are local-only and never enter CI or the runtime.
- **Deny-list awareness (multi-agent rules):** this plan writes `godot/assets/art/**` and
  `art/build/**` (the art lane's owned output), `godot/scripts/**`, `godot/tests/**`, `play.bat`, and
  `art/pipeline/gen-manifest.ps1`. It does **not** edit `Game.sln`, `.github/`, `.godot-version`,
  `Directory.Build.props`, `project.godot`, or `.gitattributes` (LFS line already correct). The art
  `.import`/binary commits are single-tiller by physical GPU/engine ownership (one pinned editor
  session), so they never land on two branches at once.
- **Known gotcha to bake into every generation unit:** `git checkout -- "godot/assets/art/*.import"`
  for untouched assets before committing a batch, so stray uid/import churn doesn't pollute the diff.
