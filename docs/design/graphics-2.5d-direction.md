# 2.5D graphics direction (2026-07-16)

Refines the graphics lane in `fanout-strategy.md` (which assumed flat 2D). Two 2026 research
passes — 2.5D approaches + Godot 4.6 cost, and the free human-in-the-loop asset pipeline —
converge on one direction. This doc is the committed target.

## The recommendation (both passes agree)

**Normal-mapped 2D + `Light2D` + `CanvasModulate`** — "lit 2D," not perspective 3D. The atmosphere
a mostly-static town+panels+mine actually needs is *light, glow, colored ambient* — not a camera
angle. Torch flicker on the forge facade, a moon-rim on memorial stones, and the day/dusk/mine
**phase tints you already draw flatly become lit ambient**. It reads premium immediately, matches
witchy-scifi-cozy better than any perspective trick, keeps the calm 2D readability a management sim
wants, and — critically — **grows from the existing flat art** (add one normal map per illustration,
light them one at a time; nothing re-authored at a new angle). Optional cheap garnish: subtle
parallax / idle layer-drift (fog, distant silhouette, foreground foliage).

Rejected for now (both passes): **billboard-3D** (Don't-Starve look) and **pre-rendered-3D** are a
genre change, not a polish pass — full `Node3D` world-swap + raycast mouse-picking + FX rebuild, or a
whole Blender→spritesheet toolchain. Reserve as a later, separately-scoped "wow" pass. **Isometric**
is expensive on the art side (re-project everything). Parallax alone is subtle on a static town.

## The load-bearing grounding fact (corrects the "free" framing)

The town is **not a 2D game-world scene**: `godot/scenes/town/town_scene.tscn` is a bare `Control`,
and `TownScene.cs` builds ground/gate/facades/memorials/hero-layer from `Control` + `TextureRect` UI
widgets. There is **no `Camera2D`, `Node2D`/`CanvasItem` world, `Light2D`, or `Parallax`** anywhere in
`godot/`. `Light2D` + normal maps only work on `CanvasItem` (`Sprite2D`), not on `Control`/`TextureRect`.
So the cheap path is cheap but **not zero** — it needs a one-time render-layer migration first. Every
real 2.5D option costs at least that migration; this is the smallest one.

## Engine sequence (here — orchestrator/core; each step is a small unit)

1. **Migrate the town render layer** — `Control`/`TextureRect` illustrations → `Sprite2D` under a
   `Node2D`/`CanvasLayer` subtree. Management panels stay flat `Control` UI on top (unchanged). This
   is the one structural cost and the prerequisite for everything below.
2. **`CanvasModulate` driven from phase state** — the existing day/dusk/mine `phase tint` becomes lit
   ambient. Cheap, high impact, reuses state that's already there.
3. **`Light2D` placements** — forge glow, torch flicker, moon-rim on memorials. Raise each light's
   `Height` (~30px) or normal maps read flat (the #1 gotcha).
4. **Normal maps per illustration** — attach a `_n` map (or bundle diffuse+normal via `CanvasTexture`).
   Flat SVG icons + UI panels need none.
5. **Optional:** `Parallax2D` / scripted idle-drift layers for a touch of stacked depth.

Godot 4.6 note: 4.6 is a polish release with **no 2D-lighting/renderer changes** — `Light2D`,
normal maps, `CanvasModulate`, `Parallax2D` behave as in 4.3–4.5. `Parallax2D` may still be flagged
experimental (unverified in 4.6). `AnimatedSprite2D`/TileMap normal maps need a custom shader, not
the plain property.

## Asset pipeline (the graphics lane — free, agent-driven)

> **SUPERSEDED 2026-07-17 (generation stage only):** the "hand-drive Krita" generation model below
> was replaced once `comfyui-mcp` proved Claude can drive the local ComfyUI directly — automated,
> $0/image, full seed/LoRA control. The pipeline is now **sequential stages of one flow**:
> **ComfyUI/SDXL via MCP (generate, master art-Claude) → Krita AI Diffusion (hand-finish/inpaint,
> human) → Laigter (normal maps) → Godot (steps 1–5 below).** Contract, roles, and lifecycle live in
> `docs/design/art-pipeline-architecture.md`; styles/prompts in `docs/design/asset-style-spec.md`.
> Everything below about Laigter, licensing, LFS, and the pilot stays authoritative.

**Krita AI Diffusion (free, local, GPL) + Laigter (free normal/depth/AO maps).** One app for
LoRA/reference-image coherence → inpaint → **paint-finish**; Laigter turns any finished
PNG into the normal (+ specular/AO/depth) maps step 4 needs.
- **Pilot the cheapest thing first:** run the *existing flat art* (SVG→PNG) through Laigter → normal
  maps → do the engine steps 1–3 on ONE building, and judge whether "lit flat art" already hits the
  2.5D look before generating any new art. Caveat (Agent A): auto-normals off low-relief flat art can
  look embossed/tinny — expect per-asset hand-tuning or authored height maps.
- **Base model licensing:** SDXL or **Z-Image (Apache-2.0)** — commercially clean, no revenue cap.
  **Avoid FLUX *dev*-tier (non-commercial)** and hosted "free" tiers (Leonardo/OpenArt/etc. — trial /
  attribution / IP-reserved / publicly-visible landmines). Keep per-asset provenance (model, LoRA,
  seed, prompt) + hand-finish edits — the copyright-protectability trail.
- **Repo:** commit PNGs (diffuse + `_n`) as source of truth via **Git LFS**; commit `.import` files;
  keep prompts/spec in-repo. Do not regenerate-on-demand (cross-GPU drift).

## Fan-out fit

Per `fanout-strategy.md`: the **engine steps (1–5) stay here** (they touch the Godot scene/render layer
— core, not data). **Asset production fans out but curation does not** — one art-director tiller; I
author the spec, you generate+curate. The class-figure win still holds: gen a **neutral base**, tint
in-engine via P3's `ClassDefinition.ColorRgb`, so add-on classes get a lit, tinted figure for free.

## Sequencing

1. **Pilot** — migrate ONE building to `Sprite2D`, add `CanvasModulate` + one `Light2D` + a
   Laigter normal map from existing art. Confirm the look before committing to the full town.
2. If the look lands → roll the migration across the town + phase-driven ambient + key lights.
3. Stand up the Krita AI + Laigter pipeline; I author the style spec; generate richer art only where
   lit-flat-art falls short.
4. Reserve billboard-3D / pre-rendered-3D for a later, separately-scoped wow pass — not this one.
