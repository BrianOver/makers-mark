# ASSETS — asset ledger

One row per asset id. Kind: mesh / image / icon / music / sfx. Status: none / placeholder (Kenney-CC0 / primitive / gen-draft) / final. Overnight-gen (roadmap §4) consumes the `placeholder`+`needs-final` rows. Canonical detail lives in `docs/design/asset-manifest.md` + the AssetSpec registry; this is the tracking view.

**Rule:** every asset is bound to a `CONTENT.md` id. No orphan assets; no `final` without an LFS file. (Manifest test enforces once wired.)

## Current state (2026-07-21)
| asset id | binds content | kind | status | source | notes |
|---|---|---|---|---|---|
| town-forge | (town) forge building | mesh | placeholder | TRELLIS.2 gen | first AI-gen 3D asset; PBR-textured (#167/#168) — the one real gen'd mesh |
| town-tavern / town-market / town-mine-gate | town buildings | mesh | placeholder | Kenney / primitive | 2D specs exist; 3D placeholders |
| hero figures (6 classes) | vanguard…skirmisher | image | placeholder | SDXL 2D | 3 lit figures (occultist/sentinel/skirmisher) still missing |
| recipe icons (~23) | blacksmith/tanning recipes | icon | placeholder | partial | long tail deferred |
| ore icons (~7) | Mine ores | icon | placeholder | partial | |
| monster art (5 Mine) | mine monsters | image | placeholder | partial | Cult-of-Lamb charm rule (rounded, big eyes) |
| palette families (house/hearth/gloomwood/crypt/den) | — (art system) | — | planned | PaletteRegistry | 5 families; not yet in code |
| music / sfx | — | music/sfx | none | — | future phase; like art pipeline (much later) |

## Gen-candidate batches
- **2026-07-21 — 3D MESHES PRODUCED.** Full pipeline ran end-to-end (SDXL → BiRefNet → TRELLIS.2 → GLB). **12 textured game-ready GLBs** (~8–12k faces each) staged in `godot/assets/models/gen/` + `art/gen-candidates/2026-07-21/glb/`: `monster-ore-golem, monster-cave-rat, monster-spider, monster-ghoul, mine-gate, well, ore-cart, anvil, barrel, tavern, bounty-board` (+ `forge` from #167). **10 excellent, bounty-board usable-plain, market-stall regenerated clean** (original source had duplicate stalls → TRELLIS rebuilt clutter). Not yet wired into the town — `TownAssets`/`BuildingKit` still point at Kenney meshes; swapping keys → gen GLBs is the next step (attended, verify via TOWN_SHOT). Source PNGs + 3-view render thumbnails in the candidates dir. Recipe: `docs/design/2026-07-21-3d-gen-pipeline-PROVEN.md`. Safety held all run: VRAM ≤5 GB, temp ≤53°C, RAM ≥17 GB free (guard-monitored).
- Source images (2D, neutral-bg 3/4 view) double as placeholders + TRELLIS inputs; `batch_size 1` (batch 2 makes grids).

## Needs-final queue (overnight-gen targets, by phase)
- **Phase A:** memorial props, gravestones, trophy/engraving meshes (bind: memorial wall, heirloom).
- **Phase B:** trait-signaling hero cosmetics; bark/portrait variants.
- **Phase C:** 2nd-venue tileset modules (shared trim sheets), that venue's monster meshes, station meshes for modifier-layer craft.
- **Phase D:** era-variant town states, prestige cosmetics.
- **Ongoing T1:** replace every `placeholder` above with `final`; then music/sfx pass.

## Discipline (roadmap §4)
Author silhouettes/archetypes (~20%), gen variants (material/recolor/inscription), proceduralize the rest. Curation = gen budget (~60-90% reject). 10 distinct silhouettes > 100 near-identical meshes. Assets bind by name (IconRegistry null-tolerant) — never touch sim determinism.
