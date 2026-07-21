---
title: Asset Manifest — placeholder / generic / borrowed models to replace
type: reference
status: living — update as units land
owner: any session touching visuals; keep truthful
related: docs/design/2026-07-21-active-professions-shop-design.md
---

# Asset Manifest

Running list of every place the game currently ships a **placeholder, generic, or borrowed
(CC0) model** instead of bespoke Maker's-Mark-themed art. This is the worklist for the later
art-generation pass. **Keep it truthful:** when you add a placeholder, log it here in the same
PR; when you replace one with final art, mark it Done (don't delete the row — history is useful).

Palette / style target (from plan U15): fantasy-witchy with a sci-fi tinge — dark purple/teal,
runes with faint circuitry, candle-glow rim light.

## Legend

- **Priority:** P1 = player looks at it constantly / core loop · P2 = frequent · P3 = ambient/edge.
- **Status:** `generic` (borrowed CC0) · `primitive` (box/capsule fallback) · `2d-placeholder` ·
  `needed` (nothing exists yet) · `done` (bespoke art shipped).

## 3D town — current generic (Kenney CC0)

The whole walkable town is stood up on Kenney kits. All bespoke-art debt.

| Slot | Now | Needed | Priority | Status |
|---|---|---|---|---|
| Hero characters (6 classes) | Kenney `mini-characters` GLBs (generic male/female a–f) | Per-class themed hero models matching the 6 combat classes | P1 | generic |
| Forge building | Kenney fantasy-town-kit cottage + chimney | Themed blacksmith forge exterior | P2 | generic |
| Market / shop building | Kenney town-kit stall (`stall-red`, `banner-red`) | Themed shopfront | P2 | generic |
| Tavern building | Kenney town-kit wood cottage + lantern | Themed tavern | P3 | generic |
| Mine entrance / gate | Kenney castle-kit `metal-gate` + `rock-large` | Themed Mine mouth | P2 | generic |
| Misc town props | Kenney town-kit (well, pillars, walls, banners, chimney, lantern) | Themed set dressing | P3 | generic |
| Building fallback | `PrimitiveMesh` box when GLB missing | n/a (should never ship) | P1 | primitive |
| Hero fallback | `CapsuleMesh` when character GLB missing | n/a (should never ship) | P1 | primitive |

> Note: every Kenney GLB ships white and gets a runtime colormap fallback (`TownAssets.ApplyColormapFallback`).
> Bespoke models should carry their own materials so that workaround can be retired.

## Blacksmith slice (Phase A) — new placeholders this phase will introduce

Log entries here as the slice builds; seed list of what the design implies:

| Slot | Now | Needed | Priority | Status |
|---|---|---|---|---|
| Forge station (interior focus) | — | Furnace, anvil, hammer, quench barrel, workbench for the 2.5D overlay | P1 | needed |
| Furnace heat FX | — | Glow / ember / heat-shimmer for the smelt beat | P1 | needed |
| Glowing stock / ingot | — | Hot-metal stock that visibly cools during the forge beat | P1 | needed |
| Ore & fuel props | — | Ore chunks (per material grade), coal/fuel | P2 | needed |
| Hammer strike FX | — | Sparks + impact feedback on the forge beat | P2 | needed |
| Shop counter station | — | Counter, display shelves/pedestals, signage for the service loop | P1 | needed |
| Item display models | 2D icons (`IconRegistry`) | 3D/2.5D display versions of shelved gear for the counter | P2 | needed |
| Craft-quality feedback | — | Visual tell for Poor→Masterwork result (spark burst, mark stamp) | P2 | needed |

## Phase B/C (not yet built — placeholder rows to expand later)

| Slot | Now | Needed | Priority | Status |
|---|---|---|---|---|
| Alchemist station | — | Cauldron / reagent bench for the puzzle minigame | P1 | needed |
| Enchanter station | — | Glyph table / rune surface for the pattern minigame | P1 | needed |

## How to use this

- Building visuals or minigames? Check this file first; add a row before you commit a placeholder.
- Art-gen pass: filter P1 `generic`/`needed`, generate against the style bible, flip rows to `done`.
