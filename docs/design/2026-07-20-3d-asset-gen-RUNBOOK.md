# 3D-asset-gen — setup RUNBOOK (run when you're awake + at the machine)

Companion to `2026-07-20-3d-asset-gen-plan.md`. The plan is the *why*; this is the exact *how*,
plus what an overnight agent session found and why the machine-modifying steps were left for you.

## What the overnight session confirmed (2026-07-20)

- ComfyUI **0.28.0**, embedded **Python 3.13.12**, **torch 2.13.0+cu130** (Blackwell-native — sm_120 fine).
- GPU **RTX 5080, 16.0 GB** total. **~8 GB was held by an EXTERNAL process** (ComfyUI's own cache cleared to no effect) → **find + close that GPU user before generating** (Task Manager → GPU, or `nvidia-smi`), you want ≥14 GB free.
- Disk **1.6 TB free** — plenty.
- **Blockers that require YOU (not automatable unattended):**
  1. **ComfyUI-Manager is not enabled** — its install API is unreachable, so nodes can't be installed programmatically. Start ComfyUI with `--enable-manager`, or install nodes by manual `git clone` (below).
  2. **`COMFYUI_PATH` unknown** to tooling — I don't have the portable's install dir. You need to `cd` there for manual installs.
  3. **Blender not installed** — the normalize step needs it.
  4. Custom-node builds (TRELLIS.2 isolated env) + VS Build Tools want **admin/UAC** and a **ComfyUI restart** — doing that unattended could leave your live ComfyUI broken, so it was deferred.

## Step 0 — prerequisites (once)

1. **Blender 4.x** — install from blender.org; confirm `blender --version` on PATH (or note the full exe path).
2. **VS Build Tools 2022** with "Desktop development with C++" (insurance for any source build). https://visualstudio.microsoft.com/downloads/
3. **Locate your ComfyUI portable** dir (the one serving `127.0.0.1:8188`). Note it as `%COMFY%` below. `custom_nodes/` and `python_embeded/` live under it.
4. **Free VRAM**: close the external ~8 GB GPU user; confirm `nvidia-smi` shows ≥14 GB free.
5. Launch ComfyUI **as admin** for the first node install (symlink/env-build permissions).

## Step 1 — TRELLIS.2 (PRIMARY)

Preferred (isolated env dodges your cp313/torch2.13 host — the whole point):
- Via ComfyUI-Manager (if enabled): Manager → Install Custom Nodes → search **"TRELLIS2"** → install **PozzettiAndrea/ComfyUI-TRELLIS2** → restart. It builds its own pixi/comfy-env on first load (long — downloads its own torch).
- Manual: `cd %COMFY%\custom_nodes` → `git clone https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2` → follow its README's env-build step → restart ComfyUI.
- Weights: auto-download on first run from HF `microsoft/TRELLIS.2-4B` (~10–12 GB) — or pre-`huggingface-cli download microsoft/TRELLIS.2-4B` into the path its README specifies.
- If the isolated build fights >2h: try the alt wrapper `visualbruno/ComfyUI-Trellis2` (ships a cp313 wheel matrix). If still stuck → skip to TripoSG (Step 3) so you have *something* working.

## Step 2 — first smoke test (SAFETY-GATED — do NOT skip the gate)

**Pre-flight (every session):** machine otherwise idle · one AI stack resident · ≥14 GB VRAM free · start a monitor in a spare terminal: `nvidia-smi -l 5` (watch temp + VRAM + power) · optional long-batch power cap (admin): `nvidia-smi -pl 250`.
**Rules:** ONE job at a time; abort if GPU > 85 °C or free VRAM too low; hard per-job timeout; checkpoint each GLB to disk. No unattended/overnight generation.

1. Generate ONE source image (image-first is the quality path): use your existing SDXL (or add Flux) with a **locked low-poly style** prompt — single object, 3/4 view, flat neutral bg, no ground shadow, whole object in frame. E.g. *"low-poly isometric medieval blacksmith forge, flat shading, single object, 3/4 view, plain background"*.
2. Feed it to TRELLIS.2 at **512³ first** (fast sanity), then 1024³ for a hero asset. Export GLB.
3. Watch the monitor. Stop if temp/VRAM breach.

## Step 3 — TripoSG (FALLBACK — "always runs")

Pure diffusers, no custom CUDA ops → runs on your host env directly.
- Node: `cd %COMFY%\custom_nodes` → `git clone https://github.com/tungnguyensipher/ComfyUI-TripoSG` → `%COMFY%\python_embeded\python.exe -m pip install -r ComfyUI-TripoSG\requirements.txt` → restart.
- Shape-only (texture separately / in Blender). Use `--faces 5000`-style cap for low-poly drafts.

## Step 4 — Hunyuan3D-2.1 (BACKUP — best textures, heaviest)

Do NOT force it into your ComfyUI (wheels are cp312/cu126, won't match). Use the standalone:
- `YanWenKun/Hunyuan3D-2-WinPortable` (v4-cu129, Blackwell-ready, zero compiling) — separate app beside ComfyUI. Paint stage ~21 GB → uses `--low_vram_mode` offload to your 64 GB RAM (slow: 5–15 min/asset). Reach for it only when TRELLIS.2 textures disappoint on a specific asset.

## Step 5 — Blender normalize (Godot-ready output)

Script is committed: `tools/blender/normalize_glb.py` (headless, no window, offline, deterministic).
```
blender -b -P tools/blender/normalize_glb.py -- \
    --in  raw/forge_raw.glb \
    --out godot/assets/models/gen/forge.glb \
    --tris 3000 --pivot base
```
It joins meshes → decimates to the tri budget → sets origin to feet (base) → applies scale/rotation →
exports Y-up GLB. **First run = smoke test**: eyeball the one asset in Godot before batching. Options:
`--tris 0` (no decimate), `--pivot center`, `--height <m>` (uniform rescale to N metres tall).

## Step 6 — Godot swap-in (one-file change)

`godot/scripts/town3d/TownAssets.cs` already indirects key→scene path. Point a building/hero key at the
new `res://assets/models/gen/<name>.glb`, LFS-track it (`.gitattributes` already covers `models/**/*.glb`),
headless-import (`GODOT_BIN --path godot --headless --import --quit`), run, verify feet-on-ground + scale.
Keep animated heroes on Kenney Mini Characters (no AI tool rigs).

## Open items for you

- Find/close the external ~8 GB GPU process.
- Decide TRELLIS.2 install route (enable Manager vs manual clone) — both need you present for the admin + restart + attended build-failure handling.
- Blender + VS Build Tools installs (admin).
- First generation is explicitly YOUR-gated ("machine is free") per the safety rules.
