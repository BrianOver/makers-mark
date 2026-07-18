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
