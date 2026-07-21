# Local AI 3D-Model Generation — Setup + Operations Plan

**Date:** 2026-07-20 · **Status:** PLAN ONLY (nothing installed or run yet)
**Goal:** Generate low-poly 3D town assets (buildings, props, later hero variants) locally on this machine, normalize them in Blender, and swap them into the Godot 4.6.3 3D town via `TownAssets.cs`.

**Priority order (fixed):**

| Rank | Tool | Role | Why |
|---|---|---|---|
| 1 | **TRELLIS.2** (microsoft/TRELLIS.2-4B) | PRIMARY | Best current quality, PBR output, ComfyUI wrapper with isolated env |
| 2 | **Hunyuan3D-2.1** | BACKUP | Mature Windows portable with Blackwell build; strong shape + PBR texture |
| 3 | **TripoSG** | LAST RESORT | Pure-diffusers, no custom CUDA ops — runs anywhere torch runs; shape-only |

TripoSR is excluded (obsolete). Personal US hobby project — license concerns out of scope by explicit decision.

---

## 1. Machine / compatibility facts (verified 2026-07-20)

- **GPU:** NVIDIA RTX 5080, **16 GB VRAM** (Blackwell, sm_120). ~**7 GB VRAM is currently held** by something at idle — must be reclaimed before any run (see Operating Rules).
- **CPU/RAM:** Ryzen 9 7950X (16c/32t), 64 GB RAM — comfortably above every tool's RAM floor (Hunyuan portable asks ≥24 GB RAM).
- **OS:** Windows 11 Home.
- **ComfyUI:** 0.28.0 portable already installed — **embedded Python 3.13.12, torch 2.13.0+cu130** (Blackwell-native).
- **The one real compat risk:** the 5080 itself is fine on cu130. The hazard is **prebuilt custom-CUDA-extension wheels** (nvdiffrast-style rasterizers, voxel ops, flash-attn) published for cp312 / torch 2.6–2.10 / cu126–128 — they will not import into cp313/torch2.13/cu130. Mitigation baked into this plan:
  - TRELLIS.2 → use **PozzettiAndrea/ComfyUI-TRELLIS2**, which builds an **isolated pixi env** (own interpreter + conda CUDA packages + prebuilt wheels) via `comfy-env`, so the host cp313 env is never asked to load foreign wheels. ([repo](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2), [comfy-env](https://github.com/PozzettiAndrea/comfy-env))
  - Hunyuan3D-2.1 → use **YanWenKun/Hunyuan3D-2-WinPortable**, a fully standalone portable (v4-cu129: Python 3.12.11, CUDA 12.9, PyTorch 2.8.0, precompiled wheels, explicit Blackwell/RTX-50 support, no compile step). ([releases](https://github.com/YanWenKun/Hunyuan3D-2-WinPortable/releases), [repo](https://github.com/YanWenKun/Hunyuan3D-2-WinPortable))
  - TripoSG → **pure diffusers, no custom CUDA ops** → runs directly on a stock torch env; safest fallback. ([repo](https://github.com/VAST-AI-Research/TripoSG))
- **Isolation consequence:** we will have up to three parallel Python stacks on disk. That is intentional — never try to unify them into the ComfyUI embedded env.

### Shared prerequisites (do once, before any tool)

1. **Disk:** budget **~80 GB free** on the drive holding ComfyUI/models: ~10–12 GB weights per 3D tool, ~7–17 GB for a Flux/SDXL image checkpoint if not already present, plus pixi/conda env overhead (multi-GB) and output GLBs.
2. **VS Build Tools 2022 (C++ workload):** install as insurance. The isolated envs *should* ship prebuilt wheels, but any source fallback (pip building an extension) hard-fails without MSVC. One-time, harmless.
3. **Separate system CUDA toolkit: NOT required.** Pixi pulls CUDA conda packages into the TRELLIS2 env; the Hunyuan portable ships its own; TripoSG needs only the torch runtime. Do not install a global CUDA toolkit unless a build error explicitly demands `nvcc`.
4. **Run-as-admin note:** ComfyUI portable does *not* need admin for normal use — avoid running it elevated (files created as admin cause permission grief later). Admin **is** required for one optional command: `nvidia-smi -pl <watts>` (power limit, see Operating Rules).
5. **Reclaim the idle ~7 GB VRAM** before first smoke test: identify the holder via `nvidia-smi` (per-process memory table); usual suspects are a resident ComfyUI with models loaded (use the ComfyUI "unload models"/free memory API or restart it), browser hardware acceleration, or a game launcher. Target: **≥14 GB free** before any 3D generation run.

---

## 2. PRIMARY — TRELLIS.2 (image → 3D, PBR, GLB)

**Model:** [microsoft/TRELLIS.2](https://github.com/microsoft/TRELLIS.2) / weights [microsoft/TRELLIS.2-4B on HF](https://huggingface.co/microsoft/TRELLIS.2-4B) (4B-param flow transformer, O-Voxel sparse representation).
**Wrapper:** [PozzettiAndrea/ComfyUI-TRELLIS2](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2).

**VRAM reality on 16 GB:** upstream states ~8 GB min for 256³ generation, ~12 GB for 512³, 16–24 GB for 1024³+ ([HF card](https://huggingface.co/microsoft/TRELLIS.2-4B)). On this card: **256³ is the smoke-test tier, 512³ is the working tier; treat 1024³ as experimental** (only with everything else unloaded, low-VRAM mode on).

### 2.1 Prerequisites & prep
- Shared prereqs above (disk, VS Build Tools, VRAM cleared).
- Expect the wrapper's first launch to download **pixi** and build its isolated env (multi-GB, tens of minutes) — do this attended, not overnight.
- Known Windows friction to anticipate (don't be surprised): Manager-install failures on some Desktop/Windows setups ([issue #99](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2/issues/99)), `comfy_env` import mismatches ([#79](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2/issues/79)), isolated-worker connect timeouts on first model load ([#85](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2/issues/85) — usually just slow first-time env spin-up; retry once before debugging).

### 2.2 Install steps
1. Update ComfyUI Manager itself first (stale Manager is behind several of the reported install failures).
2. Install via **ComfyUI Manager → search "TRELLIS2" → install latest version** (repo's recommended path). Fallback: Manager → "Install via Git URL" → `https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2.git`. Last resort: manual clone into `custom_nodes` + `pip install -r requirements.txt --upgrade` + `python install.py` — **run those with the embedded python** (`python_embeded\python.exe -m pip ...`), never system pip.
3. Restart ComfyUI; watch the console for the pixi env build. Do not queue anything until the node pack reports imported cleanly.
4. **Weights:** the wrapper auto-downloads `microsoft/TRELLIS.2-4B` from HF on first `LoadTrellis2Models`; alternatively pre-download with `hf download microsoft/TRELLIS.2-4B` into the ComfyUI models dir the node expects (check the node README at install time — layout has shifted between versions). Budget ~10–12 GB.
5. First model load is the slowest, highest-risk step — run it attended with the monitoring loop (Section 7) visible.

### 2.3 First smoke test (image → GLB, low res)
- Use the example workflow shipped in the repo's `example_workflows/` (single image → mesh + PBR → GLB export).
- Input: one pre-made building concept image (simple, centered object, plain background).
- Settings: **lowest resolution tier (256³)**, low-VRAM option ON if the node exposes one, default steps.
- Pass criteria: a `.glb` lands in ComfyUI `output/`, opens in Blender, VRAM peak stayed ≤14 GB, GPU temp stayed under ceiling.
- Only after a green 256³ run, try 512³ on the same image and record peak VRAM + wall time as the baseline for the working tier.

---

## 3. BACKUP — Hunyuan3D-2.1 (image → shape + PBR texture)

**Model:** [Tencent-Hunyuan/Hunyuan3D-2.1](https://github.com/tencent-hunyuan/hunyuan3d-2.1) — official figures: ~10 GB VRAM shape-only, ~21 GB texture, ~29 GB combined; a low-VRAM/offload path brings texture down to roughly the 6 GB class ([VRAM issue #15](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/issues/15)).
**Wrapper:** [YanWenKun/Hunyuan3D-2-WinPortable](https://github.com/YanWenKun/Hunyuan3D-2-WinPortable) — standalone Windows portable, **completely separate from the ComfyUI install** (its own Python 3.12.11 + CUDA 12.9 + torch 2.8.0 stack; the safe pattern for this machine). v4-cu129 release line explicitly supports Blackwell/RTX-50 with no compilation, and its optimizations claim ≥3 GB VRAM for 2.1 geometry / ≥6 GB for texture, ≥24 GB system RAM ([releases](https://github.com/YanWenKun/Hunyuan3D-2-WinPortable/releases)).

### 3.1 Prerequisites & prep
- Same shared prereqs. No VS Build Tools strictly needed (no compile step), but keep them anyway.
- Disk: the portable 7z + models ≈ 15–25 GB; put it on the same fast SSD, **outside** the ComfyUI tree (e.g. `C:\Tools\Hunyuan3D-WinPortable\`).

### 3.2 Install steps
1. Download the latest **cu129 (RTX 50-class)** release pair `.7z.001` + `.7z.002` from the releases page; extract the `.001` (7-Zip handles the split automatically).
2. Run the bundled model-download script / GUI launcher to fetch Hunyuan3D-2.1 weights from HF ([tencent/Hunyuan3D-2.1](https://huggingface.co/tencent/Hunyuan3D-2.1), ~10–12 GB). Do not run elevated.
3. Launch via the portable's launcher (Gradio web UI on localhost).

### 3.3 First smoke test
- Same single concept image → **shape-only** generation first (lowest octree/resolution setting) → export GLB.
- Then one shape+texture run with the portable's max-offload/low-VRAM option enabled. On 16 GB the offload path is mandatory for texturing — never run the unoffloaded 21 GB texture pipeline.
- Pass criteria identical to TRELLIS.2 (GLB opens in Blender, VRAM/temp within limits).

---

## 4. LAST RESORT — TripoSG (image → shape only)

**Model + code:** [VAST-AI-Research/TripoSG](https://github.com/VAST-AI-Research/TripoSG), weights auto-download from [VAST-AI/TripoSG on HF](https://huggingface.co/VAST-AI/TripoSG) (plus RMBG background-removal weights). Pure diffusers/torch — **no custom CUDA extensions**, so cp313/torch2.13/cu130 is a non-issue. Roughly the 6–8 GB VRAM class. Shape-only (no textures) — output is an untextured mesh, acceptable for silhouette-driven low-poly town pieces with flat-color materials applied in Blender.

### 4.1 Install steps
1. Create a **dedicated plain venv** (do not pollute ComfyUI's embedded env): install a standard CPython (3.12 or 3.13) via winget/python.org, then `python -m venv C:\Tools\triposg-env`, `pip install torch --index-url https://download.pytorch.org/whl/cu130`, then `pip install -r requirements.txt` from a clone of the repo. (Running it inside the ComfyUI embedded env would *work* since it's pure-python, but keeps failure domains separate.)
2. Smoke test: `python -m scripts.inference_triposg --image-input <img> --output-path out.glb` (per repo README; GLB out of the box). First run auto-downloads weights.
3. Skip the optional `torch-cluster` VAE-encoder extra — that one *is* a compiled wheel and is not needed for generation.

---

## 5. Recommended production workflow (image-first)

Text→3D direct is weaker than image→3D in all three tools. Standard asset path:

1. **Text → image (ComfyUI, existing install):** Flux or SDXL checkpoint with a **locked low-poly style prompt** kept in one reusable workflow JSON — e.g. "isometric low-poly game asset, {building}, single object centered, flat colors, neutral gray background, no ground plane, soft even lighting". Fix the seed policy per asset family for coherent style. Match the art direction in `docs/design/asset-style-spec.md` and `docs/design/graphics-2.5d-direction.md`.
2. **Image cleanup:** background removal (ComfyUI RMBG node or the tool's built-in preprocessor); object centered with margin.
3. **Image → 3D:** TRELLIS.2 at 512³ working tier (256³ for drafts) → GLB with PBR.
4. **Blender normalize** (Section 6) → Godot-ready GLB.
5. **Godot swap-in** (Section 7).

One asset = one image = one gen = one normalize pass. Keep the concept image next to the GLB in the output folder so regens are reproducible.

---

## 6. Blender post-processing normalize step (spec — script to be written later)

A single headless script, run per asset: `blender -b -P normalize_asset.py -- --in raw.glb --out godot.glb --budget 8000 --height 4.0`. Blender 4.x LTS, CLI only, no UI. The script must:

1. **Import** the GLB; join loose parts into one mesh object (keep material slots); delete cameras/lights/empties the generator may emit.
2. **Clean:** remove doubles/merge-by-distance at a small epsilon; delete loose vertices/edges; recalc normals outside.
3. **Decimate to a poly budget** (Decimate modifier, collapse mode) with per-class budgets — proposal: buildings ≤ 8k tris, props ≤ 2k, hero-scale characters ≤ 4k — passed as `--budget`; skip if already under budget.
4. **Normalize transform:** apply all transforms (scale/rotation), then uniformly scale so the bounding-box height equals `--height` in meters (per-class world scale — cross-check `docs/design/world-scale.md`), then **set origin to bottom-center** ("feet"/base): origin at bbox center in X/Z, bbox min in Y, so the model sits on y=0 when placed at a Godot origin.
5. **Material sanity:** clamp to the imported PBR textures (no procedural nodes glTF can't export); optionally note (not implement yet) an atlas/bake pass if draw calls become a problem.
6. **Re-export glTF Binary** (`bpy.ops.export_scene.gltf`, format GLB, +Y up — the exporter default, which is what Godot expects), apply modifiers on export, export only the mesh.
7. **Log a one-line report** per asset (tri count in/out, final dimensions, output path) so batch runs are auditable, and exit non-zero on any failure so a batch driver can stop.

---

## 7. Godot import + `TownAssets` swap-in

The indirection already exists: `godot/scripts/town3d/TownAssets.cs` maps abstract keys (`market`, `tavern`, `forge`, `minegate`, `noticeboard`, hero variants 0–11) to GLB paths, guarded by `ResourceLoader.Exists` with primitive-placeholder fallback. Swap-in is deliberately a **one-file change**:

1. Copy normalized GLBs to a new dir, e.g. `godot/assets/models/gen/town/` (keep Kenney assets untouched as fallback).
2. **Engine pin rule applies (CLAUDE.md hard rule #2):** import by opening the project in **Godot 4.6.3-stable .NET only** — never any other editor version — so the editor writes `.import` metadata; commit the GLB + `.import` files together.
3. Edit `TownAssets.cs` only: point the `BuildingPaths` entries (and later `HeroVariantFiles`) at the new paths. Because of the `LoadIfExists` guard, a bad path degrades to a placeholder instead of crashing — safe to swap incrementally, one building at a time.
4. Verify scale/origin in the 3D town scene (buildings sit on the ground plane at their marker; no floating/sunken bases) and run the engine tests (`dotnet test godot/tests --settings .runsettings`).

---

## 8. OPERATING RULES — computer-safety (MANDATORY for every session)

These are hard rules, not suggestions. Generation happens **only when the machine is otherwise inactive**, one job at a time, attended.

### 8.1 Concurrency
- **ONE generation job at a time. Ever.** No parallel gens, no gen while a game/stream/video-encode/other GPU load is running, no text→image and image→3D at the same time.
- Only one AI stack resident at a time: shut down ComfyUI before launching the Hunyuan portable, and vice versa.

### 8.2 VRAM headroom
- **Before every run:** free VRAM ≥ **14 GB** (of 16). If the idle ~7 GB holder is present, find and kill it first — never generate on top of it.
- Unload/clear models between the text→image stage and the image→3D stage (ComfyUI free-memory / unload-models; or restart the app between stages — restarts are cheap, OOMs are not).
- Always run the 3D tools in their low-VRAM/offload mode on this card (TRELLIS2 low-VRAM option; Hunyuan max-offload; ComfyUI `--lowvram` if the host ever gets tight). Goal: graceful slow-down, never a hard OOM or a WDDM device reset.
- **Abort rule:** if dedicated VRAM usage exceeds ~15 GB (spill into shared/system memory) or the run hits a CUDA OOM once, stop, drop one resolution tier, and only then retry. Never retry the same settings after an OOM.

### 8.3 Thermal / power
- Keep a monitor loop running in a second terminal for the whole session:
  `nvidia-smi --query-gpu=temperature.gpu,power.draw,memory.used,memory.free,utilization.gpu --format=csv -l 5`
- **Temp ceiling: 83 °C GPU core.** Soft rule: if it holds >80 °C for more than a couple of minutes, plan to pause after the current item. Hard rule: >85 °C → stop the job now (cancel in UI, or kill the process) and investigate airflow before resuming.
- **Optional power limit for long/batch sessions (recommended):** in an **admin** terminal, `nvidia-smi -pl 250` caps the 5080 (~360 W stock) to ~250 W for a large heat cut at a modest speed cost; resets on reboot (or restore with `nvidia-smi -pl 360`). This is the single best "don't cook the box" lever.
- Physical: case panels on, intakes unblocked, room not hot; if fan curves are conservative, set a more aggressive GPU fan curve for gen sessions.
- **Cool-down breaks in batches:** ≥60 s idle between items (script the pause); after ~30–45 min of sustained load, take a several-minute break.

### 8.4 Time / runaway protection
- **Hard per-job timeout:** set an expected wall time per tier from the smoke tests (e.g. 256³ ≈ minutes); kill any job exceeding **2× the expected time** — wrap CLI runs in a watchdog (`Start-Process` + `Wait-Process -Timeout` + `Stop-Process` in PowerShell), and for ComfyUI use the queue-cancel + free-memory controls.
- **Kill switch (know it before you need it):** ComfyUI → cancel job / clear queue, then unload models; if unresponsive, kill the python process. Killing a generation process is always safe for the machine — the only loss is that one item.
- **No unattended overnight runs.** Batches run only while the user is present or checking in at short intervals; any batch driver must stop-on-first-failure, not retry-loop.
- **Device-idle gate:** a session starts only when the user explicitly says the machine is free (not gaming, not on a call, no other heavy work). Claude never starts a generation on its own schedule.
- **Checkpoint as you go:** every completed item is written to disk (GLB + its concept image + settings) before the next item starts — a crash loses at most one item. Never hold a batch's outputs in memory.

### 8.5 Session hygiene
- Start monitoring **before** the first job; watch the first item of every session end-to-end; record peak VRAM/temp/time per tier in a session log next to the outputs.
- After the session: unload models, close the tool, confirm VRAM returns to idle baseline, restore the power limit if changed.

---

## 9. Pre-flight checklist (run before ANY generation session)

1. [ ] User has confirmed the machine is idle/free (device-idle gate).
2. [ ] No other GPU workloads: check `nvidia-smi` process table; close games, extra browsers, capture/overlay software.
3. [ ] Free VRAM ≥ 14 GB (`nvidia-smi --query-gpu=memory.free --format=csv`). Hunt down the ~7 GB idle holder if present.
4. [ ] Disk free ≥ 20 GB on the output/models drive.
5. [ ] Monitor loop running in its own terminal: `nvidia-smi --query-gpu=temperature.gpu,power.draw,memory.used,memory.free,utilization.gpu --format=csv -l 5`.
6. [ ] (Long/batch session) power limit applied: admin `nvidia-smi -pl 250`.
7. [ ] Only ONE stack launched (ComfyUI **or** Hunyuan portable), not elevated, low-VRAM/offload mode confirmed in the workflow settings.
8. [ ] Timeout/watchdog value set for the tier being run; kill-switch commands known.
9. [ ] Output directory exists and is writable; session log file open.
10. [ ] First job of the session = one known-good smoke item before any new/experimental settings.

---

## 10. Open questions

1. **ComfyUI-TRELLIS2 on this exact stack (cp313/torch2.13/cu130 host + pixi isolated env on Win11):** the isolation should make the host env irrelevant, but Windows issues ([#99](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2/issues/99), [#79](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2/issues/79), [#85](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2/issues/85)) mean the first install is an experiment. Decision point: if install doesn't come clean within ~1 focused hour, fall through to Hunyuan portable rather than yak-shaving.
2. **What holds the idle ~7 GB VRAM?** Must be identified before the first session; if it's a resident ComfyUI with cached models, the fix is procedural (unload/restart), if it's something else it changes the pre-flight.
3. **512³ vs 1024³ working tier on 16 GB:** decide after measuring peak VRAM at 512³; 1024³ may be viable with offload but is not assumed.
4. **Poly budgets + world scale:** proposed budgets (8k/4k/2k) and `--height` values need a sanity pass against `docs/design/world-scale.md` and actual Kenney-asset footprints before the Blender script is written.
5. **Texture handling for TripoSG fallback:** if we land on TripoSG, decide flat-color material policy in the Blender step (vertex colors vs single palette texture).
6. **Blender version pin:** pick and record one Blender 4.x LTS for the normalize script so exports are reproducible across sessions.

---

## Sources

- TRELLIS.2: https://github.com/microsoft/TRELLIS.2 · https://huggingface.co/microsoft/TRELLIS.2-4B
- ComfyUI-TRELLIS2 wrapper: https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2 · https://github.com/PozzettiAndrea/comfy-env
- Hunyuan3D-2.1: https://github.com/tencent-hunyuan/hunyuan3d-2.1 · https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/issues/15
- Hunyuan Windows portable: https://github.com/YanWenKun/Hunyuan3D-2-WinPortable · https://github.com/YanWenKun/Hunyuan3D-2-WinPortable/releases
- TripoSG: https://github.com/VAST-AI-Research/TripoSG · https://huggingface.co/VAST-AI/TripoSG
- TRELLIS2 low-VRAM guidance (secondary source): https://trellis2.app/blog/trellis-2-low-vram · https://trellis2.app/blog/trellis-2-comfyui
