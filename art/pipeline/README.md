# Art pipeline — local runbook

The committed tool of record for turning an SDXL prompt into a Godot-ready
diffuse + normal-map pair for Maker's Mark. This is the generation stage of the
[art pipeline architecture](../../docs/design/art-pipeline-architecture.md) and
the recipe half of the [asset style spec](../../docs/design/asset-style-spec.md).

**Local tooling only.** Nothing here is wired into CI — the fast lane and the
engine tests never run generation. Model weights are **never committed**; they
live in the local Hugging Face / ComfyUI caches and are pinned by name + SHA
(`cutout.py` pins the BiRefNet revision; `models.lock.json` pins checkpoints
when it exists, orchestrator-owned).

Full stage flow:

```
ComfyUI (SDXL)  ->  cutout.py (BiRefNet)  ->  normalmap.py (Sobel)  ->  Godot import
   generate            <id>.png RGBA            <id>_n.png              Sprite2D + Light2D
      |                     |                       |                        |
  candidates/          candidates/             candidates/          godot/assets/art/ (LFS)
  (gitignored)                                                       curated pairs only
```

## 0. Prerequisites (one time)

- **ComfyUI portable** at `C:\Tools\ComfyUI_windows_portable`, served at
  **`http://127.0.0.1:8188`**. Always use the **IPv4 literal `127.0.0.1`**, never
  `localhost` — `localhost` can resolve to IPv6 and miss the server. It
  auto-starts at logon (`…\Startup\ComfyUI-MakersMark.vbs`); manual relaunch is
  `run_nvidia_gpu.bat`.
- **Base model** `sd_xl_base_1.0.safetensors` present in ComfyUI's
  `models/checkpoints`.
- **GPU:** RTX 5080 (Blackwell), CUDA 13 (cu130 PyTorch).
- **Pinned Godot** per `.godot-version` (never open `godot/` in any other editor
  version — hard rule 2) for the final import step.
- Optional: **Krita AI Diffusion** (Custom Server → the same `127.0.0.1:8188`)
  for hand-finish, and **Laigter** for a normal-map quality pass.

### Python venv for cutout + normalmap

`torch`/`torchvision` are GPU-build-specific and must come from the CUDA index
first; the rest come from `requirements.txt`:

```bash
python -m venv art/pipeline/.venv
art/pipeline/.venv/Scripts/activate          # Windows (Git Bash / PowerShell: .venv\Scripts\Activate.ps1)
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu130
pip install -r art/pipeline/requirements.txt
python -c "import torch; print(torch.cuda.is_available())"   # must print True
```

The `.venv/` lives under `bin/`-style ignores; keep it out of commits.

## 1. Generate candidates (ComfyUI)

Drive ComfyUI through the `comfyui-mcp` tools (`generate_image` /
`create_workflow`, then `view_image`) — the agent-driven path — or the ComfyUI
web UI at `http://127.0.0.1:8188`.

**SDXL settings (locked, from `ArtTrackProfiles.Active` — the single authoritative
source; `asset-style-spec.md` restates it for reference only):**

| Setting | Value |
|---|---|
| checkpoint | `sd_xl_base_1.0.safetensors` |
| size | `1024×1024` (buildings/scenes); per-spec override for others (e.g. item icons `512×512`) |
| steps | `28` |
| cfg | `6.5` |
| sampler | `dpmpp_2m` |
| scheduler | `karras` |
| seed | fixed per asset — first candidate = `AssetSeed.SeedFor(id)`; re-rolls = `seed+1`, `seed+2`, … (record the chosen offset in the build-half) |
| framing | 3/4 isometric, centered, **plain dark neutral background** (clean cutout) |

Prompt = the **`active`** track profile (master prefix + subject + neutral
background); negatives include `plain dark neutral background`-friendly excludes.
The single source of the master prompt/negative **and settings** is
`art/GameArt/ArtTrackProfiles.cs` — the style spec restates it for reference
only.

**Hyper-SDXL LoRA note.** `Hyper-SDXL-8steps-CFG-lora.safetensors` is present in
the local ComfyUI `models/loras` (health-checked 2026-07-18). It is an
**optional fast-draft aid only** — useful for rapid curation-time previews at
low step counts — and is **never** the ship setting. All committed, shipped art
renders at the frozen **28-step base** settings above with no LoRA attached.

Generate **8–16 candidates** per asset. Write them to the gitignored scratch
dir so they never enter history:

```
art/pipeline/candidates/          # gitignored (curation scratch)
```

Curate hard (expect 60–90% reject). Optional Krita AI hand-finish (inpaint /
touch-up) on the winner. The curated diffuse is the input to the next stage.

## 2. Cutout — background removal (`cutout.py`)

BiRefNet segmentation turns the neutral-background sprite into a transparent
RGBA cutout. `--trim` crops to the alpha bounding box so the sprite is
content-tight (V4b's runtime scaling assumes trimmed textures).

```bash
python art/pipeline/cutout.py \
    art/pipeline/candidates/town-forge-07.png \
    art/pipeline/candidates/town-forge.png \
    --trim
```

- First run downloads BiRefNet (~1 GB) to the HF cache. The model **revision is
  pinned** in the script (`trust_remote_code` executes repo code — supply-chain
  rule); bump it only after re-reviewing the repo at the new SHA.
- No GPU? `--device cpu` works but is slow.
- `python art/pipeline/cutout.py --help` for all flags.

## 3. Normal map — Sobel from luminance (`normalmap.py`)

Height-from-luminance → `np.gradient` → tangent-space normal, source alpha
preserved. Emits `<id>_n.png`.

```bash
python art/pipeline/normalmap.py \
    art/pipeline/candidates/town-forge.png \
    art/pipeline/candidates/town-forge_n.png \
    2.5
```

- `strength` (default `2.5`) is the bump gain — higher = deeper relief, too high
  reads tinny/embossed. The tavern shipped at `2.5`.
- **Godot +Y flip:** Godot normal maps are +Y **up** (OpenGL green-up). The
  script already emits the correct orientation, so **import the `_n.png` with
  "Normal Map → Flip Y" OFF**. Do not change the sign in the script — flipping it
  caves every bump in.
- **Optional Laigter pass:** where the Sobel map reads flat/embossed/tinny (the
  known low-relief risk), run the cutout through
  [Laigter](https://azagaya.itch.io/laigter) instead and export its normal map
  as `<id>_n.png`. Same downstream import.

### QA gate (before committing a pair)

Load the diffuse+normal pair in a throwaway pilot-style scene
(`Sprite2D` + `PointLight2D`, `Light2D.Height ≈ 30`, `CanvasModulate`) and sweep
the light across it with the arrow keys. **Bumps must brighten *toward* the
light.** If relief caves in, the normal Y is flipped — fix the import "Flip Y"
toggle, not the script.

## 4. Import into Godot + commit (curated pairs only)

On the **pinned engine** (`.godot-version`), copy the curated pair into the LFS
art tree and let Godot mint the `.import` sidecar:

```
godot/assets/art/<id>.png       # diffuse   (LFS)
godot/assets/art/<id>_n.png     # normal    (LFS)
godot/assets/art/<id>.png.import
godot/assets/art/<id>_n.png.import
```

Layout is **flat** (no `<track>/` subdirs) — the shipped registry
(`IconRegistry.Art` → `res://assets/art/<name>.png`) and the pilot are flat; the
null-tolerant *name* is the contract, not the path.

`godot/assets/art/**/*.png` is **git-LFS**-tracked. Only curated, committed pairs
go here — `art/pipeline/candidates/` stays gitignored. Then write the build-half
`art/build/<id>.build.json` (seed, model, sampler/scheduler, diffuse+normal
sha256, uid, provenance, `Status: locked`) per the architecture doc, and record
the asset in `seeds.generated.md`.

## Files

| File | What |
|---|---|
| `cutout.py` | BiRefNet background removal → RGBA cutout (`--trim`, `--revision`, `--device`) |
| `normalmap.py` | Height-from-luminance → tangent-space normal map (`strength` arg) |
| `requirements.txt` | Python deps (torch/torchvision installed separately from the cu130 index) |
| `candidates/` | gitignored generation/curation scratch — never committed |
| `seeds.generated.md` | generated audit log of committed assets (seed provenance) |
| `README.md` | this runbook |
