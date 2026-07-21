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

---

## OVERNIGHT STAGING STATUS (2026-07-20, done for you)

Staged under `C:\Tools\ComfyUI_windows_portable\staged_3d_nodes\` — **your live ComfyUI is untouched** (nothing installed into its Python, no restart):
- `pip-freeze-backup-2026-07-20.txt` — full 151-package snapshot of your working ComfyUI env (restore reference).
- `constraints.txt` — pins the versions that MUST NOT change: `torch==2.13.0+cu130`, `torchvision==0.28.0+cu130`, `torchaudio==2.11.0+cu130`, `numpy==2.5.1`, `transformers==5.13.1`, `trimesh==4.12.2`.
- `ComfyUI-TripoSG/` — cloned.
- `ComfyUI-TRELLIS2/` — cloned (PozzettiAndrea).
- `trellis2-weights-download.log` — background download of `microsoft/TRELLIS.2-4B` (~12–16 GB) into the HF cache (`C:\Users\Brian Over\.cache\huggingface`). Check the log tail for `DONE`; if interrupted, re-run `python_embeded\python.exe -c "from huggingface_hub import snapshot_download; snapshot_download('microsoft/TRELLIS.2-4B')"` (resumable).

### Why the final install was left for you (real reasons found, not caution-for-its-own-sake)
- **TripoSG's `requirements.txt` pins `numpy==1.22.3`** — installing it into your shared ComfyUI Python would downgrade numpy from 2.5.1 and **break your whole ComfyUI** (torch cu130 needs numpy 2.x). It also wants `diffusers`/`transformers`/`peft`/`diso`/`pymeshlab` that would churn versions your existing nodes (controlnet_aux, IPAdapter, RMBG) depend on. TripoSG is best run in a SEPARATE venv, not as a node in this ComfyUI.
- **TRELLIS.2 uses `comfy-env` (isolated environment)** via its `install.py`/`prestartup_script.py` — env-safe (won't touch your shared Python) but the isolated build is heavy and best watched. This is the recommended primary; the isolation is exactly why it's the safe one to actually run.
- **ComfyUI-Manager isn't enabled** on your instance, so node installs can't go through the Manager API — hence manual clone (done) + attended build.

### Exact attended steps (when you're up + GPU free)
1. Confirm the weight download finished: `type C:\Tools\ComfyUI_windows_portable\staged_3d_nodes\trellis2-weights-download.log` → look for `DONE`.
2. **TRELLIS.2 (primary):** move the node in and let comfy-env build it:
   `move C:\Tools\ComfyUI_windows_portable\staged_3d_nodes\ComfyUI-TRELLIS2 C:\Tools\ComfyUI_windows_portable\ComfyUI\custom_nodes\`
   then restart ComfyUI **as admin** (comfy-env needs symlink perms; also enables the HF-cache symlink reuse so it won't re-download the 12 GB). Watch its console — the first-load comfy-env build is long. If the build errors on the 5080/cu130, try the alt wrapper `visualbruno/ComfyUI-Trellis2` (cp313 wheels).
3. **Smoke test (SAFETY-GATED — see Step 2 of the main runbook):** nvidia-smi monitor running, ≥14 GB free (you have ~14 now), one job, 512³ first. Feed an SDXL low-poly prop render → GLB.
4. **TripoSG (only if you want it):** do NOT add it to this ComfyUI. Make a separate venv: `py -3.13 -m venv C:\Tools\triposg-venv` → activate → `pip install torch --index-url https://download.pytorch.org/whl/cu130` → `pip install -r staged_3d_nodes\ComfyUI-TripoSG\requirements.txt` (this venv can have numpy 1.22.3 without hurting ComfyUI) → run TripoSG standalone.
5. **Godot:** run generated GLBs through `tools/blender/normalize_glb.py`, drop into `res://assets/models/gen/`, point a `TownAssets` key at it.
