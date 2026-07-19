# Maker's Mark — asset style spec (SDXL / ComfyUI pipeline)

The house look + the exact recipe for generating coherent 2.5D game assets on the local
free stack. Any Fornida session or task-Claude can reproduce assets from this file alone.

## Pipeline (all local, $0/image)

- **Backend:** local ComfyUI portable — `C:\Tools\ComfyUI_windows_portable`, served at
  `http://127.0.0.1:8188` (IPv4 only; use the IP, never `localhost` → IPv6 miss).
  Auto-starts at logon via `…\Startup\ComfyUI-MakersMark.vbs`. GPU: RTX 5080 (Blackwell,
  cu130 PyTorch). Manual relaunch: `run_nvidia_gpu.bat`.
- **Agent-driven gen:** `comfyui-mcp` (artokun) via `.mcp.json` — Claude calls
  `generate_image` / `create_workflow`, then `view_image`. This is the primary path.
- **Manual finishing:** Krita AI Diffusion (Custom Server → the same :8188). Inpaint,
  upscale, IP-Adapter reference, hand touch-up. Not the generator.
- **Base model:** `sd_xl_base_1.0.safetensors` (OpenRAIL-M, commercial-OK). A trained
  style-LoRA is the next coherence upgrade (deferred until the look is settled).

## Palette (from `docs/style-bible.md`)

| Role | Hex | Use |
|------|-----|-----|
| void | `#140f1f` | deepest background, negative space |
| iron | `#2a2438` | structures, stone, metal base |
| arcane | `#6b4c9a` | magic accents, violet rim light |
| coolant | `#3fb0ac` | teal highlights, alchemy/fluid |
| ember | `#e0913f` | forge/fire glow, warm key light |
| bone | `#d8cfe0` | light text, pale highlights |
| blood | `#b5462f` | danger, wounds, warning |

Dominant mood: deep desaturated **void-purple + iron-grey**, warm **ember-orange** key
glow, subtle **arcane-violet** accents. Low saturation base, glow does the color work.

## Palette families (2026-07-18 amendment — variety-tone direction §2)

Purple is the NIGHT/DEEP anchor, not the default. Five families — `house` (void-purple+ember,
night/arcane/deep-mine), `hearth` (honey-amber warm town), `gloomwood` (moss+verdigris forest),
`crypt` (bone+cold-cyan), `den` (rust+charcoal) — authoritative ids + clauses live in
`art/GameArt/PaletteRegistry.cs`; specs select via `AssetSpec.PaletteId`. Tint-multiply
legibility rules: warm families (R>G>B) always survive the phase tints; green identity needs
G >= 1.6x B pre-tint; teal is accent/emissive only; cool families ride the B channel; never
carry identity on yellow alone. The master negative no longer bans bright/cheerful (tone
directive) — warmth is a legal register; see docs/design/tone-register.md.

## Master prompt (prepend to every asset)

> dark fantasy painterly game art, stylized, cohesive game asset, moody volumetric
> torchlight, rich shadows, deep desaturated void-purple and iron-grey tones with
> ember-orange glow and subtle arcane-violet accents, hand-painted texture, clean rendering

## Negative (standard)

> photo, photorealistic, 3d render, blurry, low quality, jpeg artifacts, text, watermark,
> signature, ui, hud, frame, border, cluttered, oversaturated, neon, cartoon, cel shaded,
> flat lighting

## SDXL settings (locked)

- size `1024×1024` (buildings/scenes) · steps `28` · cfg `6.5` · sampler `dpmpp_2m` · scheduler `karras`
- **seed: fixed per asset** — reproducible iteration + matches the sim's determinism ethos.
  Record the seed with each kept asset.
- 2.5D framing: buildings/props at **3/4 isometric view**, centered, on a **plain dark
  neutral background** for easy cutout → normal-map (Laigter) → Godot Sprite2D + Light2D.

## Two-track styles (decided 2026-07-17)

Two locked visual tracks. Every generated asset is tagged with the track it belongs to.

**`active`** — in-game / gameplay / moving assets (town buildings, props, sprites). Clean,
cutout-ready, on-palette. The production workhorse; feeds the normal-map → Godot path.
- Style clause: `crisp clean stylized game asset, single <subject>, one structure centered,
  3/4 isometric view, hand-painted diffuse, clear readable silhouette, dark fantasy, low-key
  moody lighting, deep desaturated void-purple shadows, iron-grey, warm ember-orange key glow,
  subtle arcane-violet rim, muted somber palette, plain dark neutral background`
- Negative adds: `text, letters, logo, multiple buildings, sprite sheet, tiled, snow, trees, forest background`
- Reference: seed 1003 ("V3+darker" forge).

**`painterly`** — cutscenes / static art / key art. Soft oil chiaroscuro, atmospheric; full
scene OK. NOT for sprites (cutout is manual).
- Style clause: `dark fantasy concept art, <subject>, loose painterly brushwork, dramatic
  chiaroscuro, oil-painting texture, moody atmospheric, deep purples and iron greys with
  ember-orange glow, arcane-violet accents`
- Reference: seed 1002 (painterly forge).

## Per-asset template

```
<master prompt>, <subject + 3/4 iso view + material + light source>, centered, plain dark neutral background
```

## Asset seeds log

| Asset | Track | Seed | Notes |
|-------|-------|------|-------|
| forge (baseline sheet) | active | 7777 | first end-to-end test; SDXL laid out a multi-object sheet |
| forge (grimdark scene) | painterly | 1001 | atmospheric scene w/ bg trees |
| forge (painterly) | painterly | 1002 | **reference** for painterly track |
| forge (crisp sheet) | active | 1003 | clean multi-angle sheet (bright) |
| forge (V3+darker) | active | 1003 | **reference** for active track; single centered, dark prompt |
| forge (semi-real) | — | 1004 | rejected — reads as 3D render |
