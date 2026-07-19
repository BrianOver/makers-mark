# Art Pipeline Health Verification — 2026-07-18

> U1 of `docs/plans/2026-07-18-006-feat-art-pipeline-wiring-plan.md` (P006). Resolves **OQ3**:
> is the local ComfyUI `AssetSpec → ComposePrompt → PNG` path healthy end-to-end? Verified at
> implementation time, not just at planning time. This is a verification record — no pixels from
> this session are committed (smoke-gen bytes live under gitignored `art/pipeline/candidates/`).

## 1. Health check (`comfyui health_check`)

| Fact | Value |
|---|---|
| ComfyUI | `0.28.0` |
| Python / PyTorch | `3.13.12` / `2.13.0+cu130` |
| GPU | `cuda:0 NVIDIA GeForce RTX 5080` (Blackwell, `cudaMallocAsync`) |
| VRAM | 8.1 / 15.9 GB free at check time |
| System RAM free | 34.0 GB |
| Queue | 0 running, 0 pending |
| Checkpoint | `sd_xl_base_1.0.safetensors` present (1 of 1 in `checkpoints`) |
| LoRA | `Hyper-SDXL-8steps-CFG-lora.safetensors` present (fast-draft aid only — see §4) |
| `diffusion_models` / `vae` / `text_encoders` / `controlnet` | empty — **expected**: the SDXL base checkpoint bundles UNet+CLIP+VAE; this pipeline never uses the split-format loaders that populate those categories |
| Recent errors (`/internal/logs`) | none |

**Verdict:** server up, correct GPU, checkpoint present, queue clear, no errors. No regression
since the planning-time check recorded in the plan's Open Blockers section.

## 2. Smoke generation — `item-longsword`

Prompt/negative/seed were computed by **running the real `art/GameArt` code** (a throwaway
console app referencing `GameArt.csproj` — not hand-transcribed), so this is exactly what U4
would compose for this id, not an approximation:

- **Spec:** `art/specs/items/ItemSpecs.cs` → `item-longsword` (Track `Active`, Kind `Item`, `PaletteId` = `house` default, `Width`/`Height` override = 512).
- **Seed:** `AssetSeed.SeedFor("item-longsword")` = `992983946`.
- **Settings:** `sd_xl_base_1.0.safetensors`, `512×512` (per-spec icon override), steps `28`, cfg `6.5`, `dpmpp_2m`/`karras` — the frozen `ArtTrackProfiles.Active` settings.
- **Composed prompt:** `crisp clean stylized game asset, single subject, one structure centered, 3/4 isometric view, hand-painted diffuse texture, clear readable silhouette, dark fantasy, low-key moody lighting, plain dark neutral background, deep desaturated void-purple shadows, iron-grey, warm ember-orange key glow, subtle arcane-violet rim accents, muted somber palette, a single iron longsword, straight cruciform blade, wrapped hilt and pommel`
- **Composed negative:** `text, letters, logo, words, title, caption, signature, watermark, multiple buildings, sprite sheet, tiled, duplicated, photo, photorealistic, 3d render, blurry, low quality, ui, hud, frame, border, oversaturated, neon, flat lighting, people, snow, trees, forest background`

### Result and one diagnostic detour

The **first** candidate at the exact frozen seed/settings (`992983946`, 512²) rendered a
garbled, tiled/repeating pattern — not a usable sword. To rule out a real pipeline break before
recording a verdict, two isolating draws followed:

1. Same prompt/seed at **1024×1024** → clean, on-palette, legible (a multi-sword concept sheet).
2. Same prompt at **512×512** with **`seed+1`** (`992983947`) → clean, on-palette, legible single
   dagger/sword silhouette.

Conclusion: the pipeline itself is healthy; the first draw was simply a bad roll at the exact
seed — the kind of draw the runbook's own curation step already budgets for ("generate 8–16,
curate hard, expect 60–90% reject", `art/pipeline/README.md` §1). This is not a regression and
not specific to 512² generation; it is normal SDXL variance. **No process change needed** — U4's
per-asset multi-candidate curation loop already absorbs this.

### Full chain proof (on the clean `seed+1` draw)

```
ComfyUI SDXL (512x512, 28/6.5/dpmpp_2m/karras)
  -> cutout.py --trim         -> item-longsword.png    RGBA 480x487
  -> normalmap.py 2.5         -> item-longsword_n.png  RGBA 480x487
```

- `cutout.py --trim` produced a valid trimmed RGBA PNG (verified `Image.open(...).mode == "RGBA"`).
- `normalmap.py` (chain-proof only — item icons are flat menu icons and do **not** ship a normal
  per `AssetSpec.NormalMap = false`; this run exists solely to prove the script works end to end)
  produced a valid tangent-space RGBA normal map at matching dimensions.
- Both ran on ComfyUI's embedded Python (`C:\Tools\ComfyUI_windows_portable\python_embeded`),
  which already carries the cu130 torch/torchvision/transformers/timm/einops stack — no separate
  `art/pipeline/.venv` was needed for this smoke pass.
- All bytes (3 candidate draws + cutout + normal) live under gitignored
  `art/pipeline/candidates/` only. **Nothing from this smoke pass is committed.**

## 3. Settings reconciliation

`art/pipeline/README.md` §1's settings table said `steps 25`; the single authoritative source,
`ArtTrackProfiles.Active.Steps` (`art/GameArt/ArtTrackProfiles.cs`), is `28`. Reconciled in this
PR: the table now reads `steps 28` and points at `ArtTrackProfiles.Active` as the source of
truth, with a note that per-spec size overrides exist (item icons render at 512² per
`ItemSpecs.IconSize`, not the 1024² building/scene default).

**Residual drift (not fixed here, out of scope for U1's file list):** `docs/design/asset-style-spec.md`
§ (settings line) also still says `steps 25`. `art/pipeline/README.md` already notes the style
spec is "restated for reference only" and `ArtTrackProfiles.cs` is the one authoritative source,
so this doesn't block U4–U6, but it should get the same one-line fix in a follow-up doc pass.

## 4. Hyper-SDXL LoRA

`Hyper-SDXL-8steps-CFG-lora.safetensors` is present locally. It is documented (this PR,
`art/pipeline/README.md`) as an **optional fast-draft curation aid only** — useful for quick
low-step previews while iterating on a prompt — and explicitly **not** a ship setting. All
committed, shipped art is produced at the frozen 28-step base settings with no LoRA attached.

## 5. Verdict

**OQ3 = GO.**

The `AssetSpec → ArtTrackProfiles.ComposePrompt/ComposeNegative → AssetSeed.SeedFor → ComfyUI
SDXL → cutout.py → normalmap.py → Godot import` path is healthy end-to-end. Server, GPU,
checkpoint, and queue are all in the expected state; the one settings drift (steps 25→28) is
reconciled; the full generation→cutout→normalmap chain produces valid RGBA diffuse+normal pairs.
U4–U6 may proceed at full scope (39 item icons, 5 Mine monster portraits, 4 extra-venue
backdrop/entrance pieces) at the frozen settings recorded in the plan's Key Technical Decisions.

No change to `sim/GameSim` in this unit (R14 — verified by the regression-guard test run below).

## 6. U6 + U7 close-out — final committed inventory (2026-07-18)

U4 (39 item icons) and U5 (5 Mine monster portraits) landed after this record was first written
(#93 merged the U5 batch at manifest count 51). U6 generated the last of this plan's R9 scope — the
Gloomwood + Sunken Crypt backdrop/entrance pairs — and U7 closed the loop: final
`gen-manifest.ps1` regeneration, the `ArtWiringCoverageTests` end-to-end resolve proof, and this
inventory note.

### Final committed set — 55 ids

| Band | Count | Ids |
|---|---|---|
| Town buildings (pre-existing) | 4 | `town-forge`, `town-market`, `town-mine-gate`, `town-tavern` |
| Hero portraits (pre-existing) | 3 | `hero-vanguard`, `hero-striker`, `hero-mystic` |
| Item icons (U4) | 39 | `item-<recipeId>` — blacksmith (no prefix) + `tanning-`/`engineering-`/`alchemy-` prefixed, all professions/slots/tiers represented |
| Mine monster portraits (U5) | 5 | `monster-cave-rat`, `monster-tunnel-spider`, `monster-deep-ghoul`, `monster-ore-golem`, `monster-forgeworm` |
| Venue backdrops/entrances (U6) | 4 | `gloomwood-backdrop`, `gloomwood-entrance`, `sunkencrypt-backdrop`, `sunkencrypt-entrance` |

`gen-manifest.ps1 --check` is clean against this set (55 ids). `ArtWiringCoverageTests.cs`
(`godot/tests/`) proves the R8+R10 contract end-to-end: a curated, data-driven id list spanning
every band above resolves through `AssetCatalog` to a non-null texture and agrees with `Has`/
`HasNormal`, including the diffuse-only vs. diffuse+normal contract split (item icons and
backdrops are flat; monsters, hero portraits, entrances, and town buildings carry a normal map).

### U6 execution notes (for future backdrop/entrance generation)

- **Backdrops skip `cutout.py`.** Verified empirically (alpha histogram on the first gloomwood
  draw): BiRefNet's salient-object segmentation discards ~97% of a full-bleed atmospheric scene as
  "background" — it isolates only the single most-salient object, which is correct for an icon/
  portrait on a neutral background but wrong for a backdrop that IS the whole scene. Both
  `gloomwood-backdrop` and `sunkencrypt-backdrop` were instead format-converted straight from the
  raw SDXL output to a fully-opaque RGBA (no BiRefNet, no normal map — matches `NormalMap: false`
  on both specs). This deviates from the plan's approach text ("`cutout.py` without `--trim` —
  backdrops fill the plane"); flagging here as a runbook correction for the deferred long-tail
  backdrops/scenes: **skip `cutout.py` entirely for `AssetKind.Backdrop`**, don't just drop `--trim`.
- **`SunkenCryptSpecs.cs` `PaletteId` gap fixed for the 2 in-scope specs.** The file's own
  doc-comment claims "All `crypt` palette family," but no spec in it set `PaletteId` (silently
  defaulting to `house`, which is byte-identical to the Mine's own default palette — i.e. Sunken
  Crypt would have rendered visually indistinguishable from the Mine, failing the family-
  distinctness requirement). Added `PaletteId: "crypt"` to `sunkencrypt-backdrop` and
  `sunkencrypt-entrance` only (the 2 specs U6 actually generated); the other 6 Sunken Crypt specs
  (5 monsters + 1 prop, deferred long tail) still default to `house` and should get the same
  one-line fix when that tail is picked up.
- Entrance normal-map QA used the standard windowed `Sprite2D` + textured `PointLight2D` pilot
  scene (mirrors `LitTavernPilot`) — critically, the `PointLight2D` needs an explicit
  `GradientTexture2D` assigned to `texture`; without one it emits no visible light and every sweep
  screenshot looks identical (a dead end hit once before finding the tavern pilot's convention).
  Both `gloomwood-entrance` and `sunkencrypt-entrance` passed (highlights track the light position,
  no caved-in/inverted relief) on the existing Flip-Y-OFF import convention.

### Deferred to follow-up (R9 long tail, ~22 specs — unchanged from the plan)

Not generated by this plan; same proven pattern, lower ROI, left for a later art wave:

- **Venue floor monsters (9):** Gloomwood 4 (`gloomwood-bramble-boar`, `-lantern-moth`,
  `-wicker-shepherd`, `-old-mossjaw`) + Sunken Crypt 5 (`sunkencrypt-crypt-crab`, `-bog-wight`,
  `-choir-of-teeth`, `-reliquary-mimic`, `-undertow`).
- **Props (11):** venue props — Gloomwood 2 (`gloomwood-mushroom-cluster`, `-toll-booth`) + Sunken
  Crypt 1 (`sunkencrypt-donation-plate`) — plus town/world props, 8 (`art/specs/props/`, e.g.
  `props-noticeboard`, `props-town-well`, `props-ore-cart`).
- **Faction crests (2):** `art/specs/factions/` (`faction-deepvein-emblem`,
  `faction-crownsguard-emblem`).

`ArtWiringCoverageTests.DeferredLongTailIds_AbsentFromManifest_ResolversNullNoThrow` pins the
graceful-degrade contract against two of these real (but ungenerated) ids, so picking up this tail
later is additive: generate → cutout/normalmap → import → `build.json` → `gen-manifest.ps1` →
extend the coverage test's id arrays by one entry each.

### CI note — known engine-tests flake (unrelated to this plan)

`engine-tests` in CI has an observed flake shape: the job completes all tests ("Failed: 0, Passed:
N") and then the process SIGABRTs on shutdown (exit 134) *after* results are already reported. This
is a known Godot/mono runtime teardown issue, not a real test failure — the fix is
`gh run rerun <run-id> --repo BrianOver/makers-mark --failed` (cap 2 reruns), watch for a clean
pass. Any failure shape OTHER than this (a real `Failed: >0`, or SIGABRT before results print) is a
genuine regression and must be fixed, not rerun.
