# 3D asset-gen — PROVEN working pipeline (2026-07-21)

First end-to-end success: SDXL source image → BiRefNet cutout → TRELLIS.2 → textured game-ready GLB (ore-golem: 8.8k verts / 11.9k faces, textured, ~1-unit bounds). This doc is the **turnkey recipe** — supersedes the guesswork in `2026-07-20-3d-asset-gen-RUNBOOK.md` (that was the *setup*; this is the *proven run*). Reusable assets in `tools/3dgen/`.

## Pipeline (4 stages, all local, $0)

```
SDXL text→image  →  BiRefNet bg-removal  →  TRELLIS.2 image→mesh  →  Godot import
(source image)      (RGBA cutout)           (textured GLB)          (res://.../gen/)
```

Everything runs through the **live ComfyUI** at 127.0.0.1:8188 (portable at `C:\Tools\ComfyUI_windows_portable\`). TRELLIS2 node (`ComfyUI-TRELLIS2`) is installed and working. Driven via the `comfyui` MCP tools.

## Stage 1 — SDXL source images

- Tool: `mcp__comfyui__generate_image`, checkpoint `sd_xl_base_1.0.safetensors`, 1024×1024, steps 26, cfg 7, euler/normal.
- **Prompt template** (single clean object = good TRELLIS input): `low-poly 3D game asset, ONE single <subject>, isolated object centered, three-quarter view, flat cel shading, clean neutral light-grey background, no ground, whole object in frame, stylized fantasy, game-ready`
- **Negative (anti-grid is mandatory):** `turnaround sheet, grid, multiple objects, four views, photorealistic, photo, text, watermark, busy background, cropped`
- **Use `batch_size: 1`.** batch_size 2 frequently produced turnaround/grid sheets (multiple mini-objects in one frame) — unusable for TRELLIS. One clean object per image.
- Buildings tolerate a small stone/diorama base; props & monsters want pure isolated object. Monsters: "rounded shapes, big eyes" (Cult-of-Lamb charm rule).

## Stage 2 — Background removal (REQUIRED)

Grey-bg images corrupt the mesh (TRELLIS treats the bg as geometry). Must produce an alpha cutout.
- Upload source: `mcp__comfyui__upload_image` (or `cp` into `ComfyUI/input/`).
- `mcp__comfyui__remove_background(image, filename_prefix)` → BiRefNet_toonout → RGBA PNG in `ComfyUI/output/`.
- **Stage the cutout back into `ComfyUI/input/`** (TRELLIS LoadImage reads from input): `cp output/<cut>_00001_.png input/<name>.png`.

## Stage 3 — TRELLIS.2 image→mesh

- Workflow template: `tools/3dgen/trellis_image_to_glb.json` (API format). Replace `__IMAGE__` (input filename) and `__PREFIX__` (output name).
- Submit with `mcp__comfyui__enqueue_workflow(workflow, disable_random_seed=true)`.
- **⚠ THE GOTCHA that cost an hour:** without `disable_random_seed: true`, the MCP randomizes every `seed` field to a value **> int32 max** → TRELLIS rejects it → generic `400 Bad Request`. Always pass `disable_random_seed: true` (seeds are pinned to 42 in the template for determinism anyway).
- Proven settings (in template): `LoadTrellis2Models` resolution **512**; `Trellis2ProcessMesh` remesh **on**, `target_face_count` **12000** (game-ready low-poly directly — **skips the Blender decimate step entirely**, which is why we don't need Blender for a usable mesh); `Trellis2RasterizePBR` `texture_size` **1024** (not 2048 — lower VRAM, plenty for stylized).
- Output: `ComfyUI/output/<prefix>_<timestamp>.glb`, ~2–4 min/asset. VRAM stayed ~3 GB (TRELLIS RAM-pressure-caches to system RAM — see limits).

## Stage 4 — Verify + Godot import

- **Verify visually** (matplotlib is too crude — showed the golem as a blob; use a textured render): `tools/3dgen/render_glb.py <glb> <out.png>` → pyglet-rendered front/side/back. Needs `pip install "pyglet<2"` in the embedded python (isolated, does NOT touch torch).
- Stats sanity: ~8–12k faces, `textured=True`, bounds ≈ ±0.5 (unit cube, centered). Good.
- **Normalize before town use (Blender IS installed):** raw TRELLIS GLBs are unit-cube *centered* — wire them in as-is and they float/mis-scale. Run `blender -b -P tools/blender/normalize_glb.py -- --in <raw>.glb --out godot/assets/models/gen/<key>.glb --tris 6000 --pivot base --height <metres>` (Blender 4.2.9 at `C:\Tools\blender-4.2.9-windows-x64\blender.exe`; headless, no GPU). Sets feet-pivot + scale so it sits on the ground at town scale.
- Godot: LFS-tracked (`.gitattributes` covers `models/**/*.glb`), point `TownAssets`/`BuildingKit` key at it (`TownAssets.InstantiateGen`, Kenney fallback), headless-import, verify in-engine. **TOWN_SHOT needs an interactive GPU desktop — it FAILS in a background/headless agent session; Brian runs it.** Kenney mini-characters stay for animated heroes (no AI rigs).

## HARD SAFETY LIMITS (updated 2026-07-21 — machine health outranks any asset)

Monitor EVERY job; abort/pause on breach. Cancelling a gen is always safe (lose one item, never the machine).
- **VRAM (16 GB card):** require free ≥ 14 GB before a heavy job; **abort if used > 14 GB**. Trust `nvidia-smi`, not ComfyUI `/system_stats`. TRELLIS in practice stays ~3 GB (RAM-cached) — the real constraint is RAM.
- **RAM (NEW — 64 GB, TRELLIS offloads the 4B model to system RAM):** **keep ≥ 5 GB free at all times.** Check with PowerShell `(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory`. Idle ~20 GB free; a TRELLIS job climbs — if free approaches 5 GB, pause the queue and `clear_vram` between jobs.
- **Temp:** hard-abort > 83 °C, soft-pause > 80 °C. Observed low (30–40 °C).
- **One job at a time** (ComfyUI queue enforces). Batch = enqueue sequentially, monitor RAM/VRAM/temp between.
- **Attended for the first job of a session**; long batches OK once the per-job envelope is measured and RAM headroom is confirmed.

## OPTIMIZATION — use VRAM, not RAM (root cause found 2026-07-21)

Observed: during TRELLIS jobs VRAM idles ~3 GB while system RAM climbs (the 4B model lives in RAM). Root cause: the stages-based nodes (`nodes/stages.py`) use ComfyUI `model_management` **per-stage offload** — each stage (image-cond → shape → texture → VAE-decode) is moved to GPU, run, then pushed back to CPU/RAM (`load_models_gpu` + explicit offload back to `unet_offload_device()`, stages.py:252/310/327). Deliberate low-VRAM design; wrong for a 16 GB card doing graphics.

**Fix: launch the TRELLIS worker's ComfyUI with `--highvram`** → models stay resident in VRAM (the ~8 GB fp16 model fits with headroom), eliminating per-stage PCIe shuffling (faster) AND freeing the system RAM. The main ComfyUI is launched `python main.py --windows-standalone-build` (auto vram, no flag); the TRELLIS worker is an isolated pixi subprocess (`trellis2-nodes` env) spawned by it.

**Apply (attended — it kills/relaunches the live ComfyUI + fragile TRELLIS pixi env):**
1. Stop ComfyUI. 2. Relaunch adding `--highvram` (edit the standalone `run_nvidia_gpu.bat` or launch `main.py --windows-standalone-build --highvram`). 3. Run ONE TRELLIS job; confirm `nvidia-smi` now shows ~10–12 GB resident and RAM stays high-free; measure time delta. 4. If the pixi worker fails to re-init → roll back (relaunch without `--highvram`). Not done unattended (recoverable-but-disruptive to the user's live tool). Measure before trusting the speedup — per-asset overhead saved is ~15–30 s; the bigger win is RAM relief.

## Batch procedure (many assets)

1. SDXL-gen all source images (`batch_size 1`), curate (~60–90% keep; reject grids/multi-object).
2. `cp` winners into `ComfyUI/input/` as `mm_<name>.png`.
3. `remove_background` each → cutouts; `cp` cutouts back into `input/`.
4. For each cutout: fill the TRELLIS template, `enqueue_workflow(disable_random_seed=true)`. Queue drains one-at-a-time.
5. Between jobs: check RAM ≥ 5 GB free, VRAM, temp. `clear_vram` if RAM tightens.
6. `render_glb.py` each output; keep the good ones; copy to `godot/assets/models/gen/`.

## Known-good this session
`monster-ore-golem.glb` shipped to `godot/assets/models/gen/`. 11 more winners (tavern, market, bounty-board, mine-gate, well, ore-cart, anvil, barrel, cave-rat, spider, ghoul) in flight. Source PNGs + GLBs staged in `art/gen-candidates/2026-07-21/`.
