# ASSETS — asset ledger

**Rebuilt 2026-07-28.** The previous version tracked a 3D asset pipeline that no longer exists. The 2.5D pixel-art pivot (#244-#249) retired TRELLIS.2 generation along with the 3D render layer it fed, which orphaned the entire mesh tree — see §Orphans, the largest cleanup finding in the repo.

One row per asset id. Kind: image / icon / music / sfx. Status: none / placeholder / final. Canonical detail lives in `docs/design/asset-manifest.md` + `art-manifest.json` + the AssetSpec registry; this is the tracking view.

**Rule:** every asset binds to a `CONTENT.md` id. No orphan assets; no `final` without a file on disk. **There is no manifest test enforcing this** — see `SYSTEMS.md` §Drift note.

## Current state (2026-07-28)
| asset id | binds content | kind | status | source | notes |
|---|---|---|---|---|---|
| hero figures (6 classes) | vanguard…skirmisher | image | **final** | SDXL 2D | all six have diffuse + normal on disk and in `art-manifest.json`. The old row claimed 3 were missing — **wrong** |
| mine monster art (5) | mine monsters | image | final | SDXL 2D | diffuse + normal present |
| gloomwood monster art (4) | gloomwood monsters | image | **final** | SDXL 2D | bramble-boar / lantern-moth / old-mossjaw / wicker-shepherd, all with normals. **Had no row** |
| gloomwood venue art (3) | gloomwood venue | image | final | SDXL 2D | backdrop / entrance / toll-booth. **Had no row** |
| `town2d-*` sprite set | 2.5D town | image | final | SDXL + PIL | ground atlas, buildings, props, 5 pixel monsters, step frames |
| painted panel banners + interiors | shop / tavern / bounty / heroes / minigames | image | final | SDXL, pixel-quantized | #248, #254-#256 |
| palette families (house/hearth/gloomwood/crypt/den) | — (art system) | — | **built** | `art/GameArt/PaletteRegistry.cs` | all 5 implemented with prompt clauses + registry lookup. Old row said "not yet in code" — **wrong** |
| recipe icons | 4 professions' recipes | icon | partial | mixed | **recount owed**: recipes now total 39 (16 + 7 + 8 + 8); the old "~23" predates alchemy + engineering's full sets |
| ore / material icons | materials | icon | partial | mixed | **recount owed**: materials now total 21 (old row said ~7) |
| faction emblems | 5 factions | icon | on disk, unrowed | SDXL | present in `godot/assets/art/` with no ledger binding |
| music / sfx | — | music/sfx | none | — | future phase, deliberately not started |

**Untracked volume:** `godot/assets/art/` holds 157 PNGs; a large fraction (gloomwood art, faction emblems, engineering/alchemy item icons, `town2d-*` variants) has no `CONTENT.md` / `ASSETS.md` binding. Rowing all of them by hand is exactly the work the manifest test was supposed to make unnecessary.

## Orphans — flagged for cleanup
- **`godot/assets/models/` — ~1,627 files** (Kenney kits + TRELLIS gen GLBs). Its only source-code consumer is `godot/scripts/town3d/TownAssets.cs`, which is itself dead post-pivot. **This whole tree is dead weight** and should be deleted or archived alongside the U5 teardown in `docs/plans/2026-07-28-004-feat-close-the-open-work-plan.md`. Decide deliberately: it is large, it is in git history either way, and `MonsterView3D` is the one live 3D consumer left (it reads gen monster meshes for Bestiary and MineWatch).
- **The 12 gen'd GLBs from the 2026-07-21 batch** (`monster-*`, `mine-gate`, `well`, `ore-cart`, `anvil`, `barrel`, `tavern`, `bounty-board`, `forge`) were never wired into the town before the pivot made them moot. They are a sunk cost, not a backlog.
- Worktree litter seen during the audit: `_gen_town2d_*.py` scratch scripts and ~20 `*.import~RF*.TMP` files, untracked.

## The pipeline (current)
SDXL generation → curation (~60-90% reject) → pixel post-process: crop to canvas aspect → hard LANCZOS downscale → palette quantize (MEDIANCUT) → brightness dim. Hand-author with PIL when generation fails a subject outright (the noticeboard failed twice and was drawn by hand).

**Style cohesion is the binding constraint, not volume.** Painterly output clashes badly with pixel art, so existing good compositions get quantized rather than regenerated.

## Discipline
Author the silhouettes (~20%), generate the variants, proceduralize the rest. Curation is the budget — fan out generation, never curation. Ten distinct silhouettes beat a hundred near-identical ones. Assets bind by name (`IconRegistry` is null-tolerant) and are Godot-side only — they never touch sim determinism.

## Needs-final queue
The old queue was organised by roadmap phases that are now all closed. Current targets, in the order the game would benefit:
1. Tanning + engineering item icons and their two new craft overlays' props (rides U2/U3 of plan `-004`).
2. Sunken Crypt and Emberfall art — but **only if** those venues are activated; both are `built-inert` today, so generating for them now would be filler.
3. Trait-signalling hero cosmetics (needs a decision on whether traits should be visually legible at all).
4. Music / SFX — deliberately last.
