# Assets — the whole inventory

Every image, animation, sound and voice line in the game, where it comes from, and whether anything
actually draws or plays it. Written 2026-08-12 against `d03c3af`.

**Read the counts as of that commit, not as gospel.** Every number here has a command beside it that
re-derives it. If a number and the command disagree, the command is right and this file is stale —
fix it or delete it (CLAUDE.md rule 8).

```bash
ls godot/assets/art/*.png | wc -l          # 239 committed PNGs (196 diffuse + 43 normal maps)
ls godot/assets/icons/*.svg | wc -l        # 28 icons (9 glyphs + 19 ore)
ls godot/assets/audio/*.mp3 | wc -l        # 4 music tracks
find godot/assets -name "*.ogg" | wc -l    # 49 narrator lines
ls art/build/*.json | wc -l                # 84 assets with full SDXL provenance
```

The authoritative wiring check is an engine test, not this document:
`dotnet test godot/tests --settings .runsettings` runs `AssetResolutionCensusTests`, which fails
loudly if a referenced id stops resolving. It does **not** run in the fast lane.

---

## 1. Images — 239 files

| Family | Count | Wired by | Origin |
|---|---|---|---|
| Hero portraits | 6 + 6 normals | `AssetCatalog.HeroPortrait` → Heroes/Counter/Ledger/Tavern panels | SDXL |
| Monster portraits (4 venues) | 38 | `AssetCatalog.MonsterPortrait` → Bestiary, MineWatch, DelveStage | SDXL |
| Venue backdrops + entrances | 6 | `AssetCatalog.VenueBackdrop` / `VenueEntrance` | SDXL |
| Item icons | 39 | `ForgePanel`, shop surfaces | SDXL |
| Town2D hero + townsfolk bodies | 32 (8 characters x 4 frames) | `TownAssets2D.ForHero`, `HeroActor2D` | Hand-pixel Python |
| Town2D stations / shells / signs / props | ~42 | `WorkshopVocab`, `InteriorLayout2D`, `TownAssets2D.ForProp` | Hand-pixel Python |
| Player smith | 5 | `PlayerController2D` | Hand-pixel Python |
| Mine monster minis | 5 | `DelveStage` | Hand-pixel Python |
| UI chrome (panel banners, frame) | 5 | `UiKit.SceneBanner`, `GameTheme` | SDXL + placeholder |
| Icons (glyph + ore SVG) | 28 | `MainUi`, `IconRegistry.Ore` | Hand-authored SVG |

The 19 `ore_*.svg` match `MaterialRegistry.PricedPool` exactly. The two registered-but-inert
materials (electrum, orichalcum) correctly have none.

## 2. Animation

**Frame-based.** Nine characters have the full 4-frame gait (base / `_walk2` / `_step` / `_walk4`):
six heroes, two townsfolk builds, the player. Driven by `SpriteMotion`, consumed by `HeroActor2D`,
`PlayerController2D`, `TownsfolkNpc2D`, `MarketLife2D` and `MineWatch`.

**Twenty procedural animators, all wired, zero orphans.** `SpriteMotion`, `TreeSway`,
`AmbientLife2D`, `MarketLife2D`, `TavernLife2D`, `DayPhaseTint`, `MineWatch`, `DelveStage`,
`TabFade`, `DrawerHost`, `PipDock`, `ObjectiveTracker`, `Building2D`, `BestiaryPanel`,
`ChronicleScroll`, `AdventureTicker`, `AudioDirector` crossfade, the gold-chip pop, the UiKit hover
swap, and the ForgePanel focus flash.

**House rules, verified by sweep rather than assumed:**
- **No engine `Tween` anywhere.** Every animator uses accumulated frame delta with sine/lerp, for
  determinism and headless testability. Comments that mention Tween do so only to say it is avoided.
- **No shaders.** Zero `.gdshader` files exist. Every "material" read (steel vs cloth) is baked into
  the pixel art by the generator. There is no shader plumbing to build on.
- **`CpuParticles2D` only**, in `AmbientLife2D` (smoke, fireflies, gate dust) and `MineWatch` (embers).

## 3. Audio

**Music** — 4 committed MP3s, one per phase, crossfading over 2.5s. Bed level `-22dB` plus a
per-track trim. Missing files degrade to a synthesized `MusicBed` with a loud warning, never silence.

| Phase | Track | Trim |
|---|---|---|
| Morning | `day-first-light.mp3` | -6.9dB |
| Evening | `town-dusk.mp3` | -3.8dB |
| Camp | `night-still.mp3` | 0dB |
| Expedition (+ Deep) | `quest-wait.mp3` | -7.5dB |

No track carries a positive trim, and `AudioTests.NoComposedTrack_EverCarriesAPositiveTrimDb` makes
that permanent. See §7 for why that test exists.

**SFX** — 22 cues, **all synthesized in code** (`Synth.cs`). There are zero committed SFX files.

**Narrator** — 49 lines across 7 triggers (`VigilOpening`, `DeathEpitaph`, `ProvenSave`,
`KillingBlow`, `ActAdvanced`, `ClimaxReached`, `CampaignEnding`), all pre-baked to `.ogg` so no model
runs at play time. Voice is Chatterbox TTS cloning **VCTK Corpus speaker p254 — CC BY 4.0, the only
third-party asset in the game, and it carries an attribution obligation.** A line arriving while the
narrator is speaking is dropped rather than queued; `SelectForNight` emits at most one per night.

## 4. Pipelines

Three exist. Two are alive.

**A. SDXL / ComfyUI** — `art/specs/<module>/*Specs.cs` declares an `AssetSpec`; ComfyUI (SDXL base
1.0, settings frozen in `ArtTrackProfiles.cs`) renders candidates; `cutout.py --trim` segments;
`normalmap.py` derives the `_n` map; provenance lands in `art/build/<id>.build.json` (seed, model,
sampler, sha256). 84 assets carry that provenance. **Not byte-reproducible across GPUs** — the
guarantee is the committed PNG plus its hash, not re-derivable pixels.

**B. Procedural pixel art** — pure deterministic Python in `art/pipeline/gen-*.py` and
`tools/art/gen_town_sprites.py`. No GPU, no model. Colours are sampled from committed sibling PNGs
rather than picked by eye, and **every script has a `--check` flag that re-renders in memory and
diffs against the committed file.** 91 assets. This is the strongest provenance tier in the repo.

**C. 3D (dead)** — TRELLIS/Blender tooling from before the 2.5D pivot. See §6.

## 5. Orphans — 61 files, ~20.4MB, ~30% of the art directory

Committed, resolution-tested, and drawn by nothing. Verified by tracing each id from
`IconRegistry`/`AssetCatalog` to a *production* caller, not a test.

| What | Files | Size | Why it is orphaned |
|---|---|---|---|
| `godot/assets/sprites/2d/*.svg` | 21 | small | Early 2.5D scaffold, superseded by `town2d-*.png` |
| `sprites/{forge,ground_tile,memorial_stone,mine_gate,shop,tavern}.svg` | 6 | small | `IconRegistry.Building()`'s only caller is its own test |
| `art/faction-*-emblem.png` | 2 | 583KB | `MainUi` draws the faction's ore as its chip icon instead; the crests never appear |
| `art/town-{forge,market,tavern,mine-gate}.png(+_n)` | 8 | 7.27MB | The "LitOverlay" town scene that consumed them was deleted; the test that cites it no longer exists |
| `art/shop-interior.png(+_n)` | 2 | 389KB | Superseded by `town2d-market-interior-shell` |
| `art/props-*.png(+_n)` (8 warm-hub town props) | 16 | 11.87MB | Spec'd and tested, never mounted |
| `art/town2d-{board,forge,market,...}.png` | 6 | 244KB | Early candidates; exterior reverted to the SDXL ids in 2026-08-01 |
| `art/gen-candidates/2026-07-21/*` | 25 | ~44MB | Abandoned 3D pipeline output |
| review contact sheets / candidates | 4 | 80KB | Human-review receipts, never shipped |

**The town props are the sharpest case.** The sibling trio (`gloomwood-mushroom-cluster`,
`gloomwood-toll-booth`, `sunkencrypt-donation-plate`) had the identical bug and was fixed —
`MineWatch.cs:165-178` documents it verbatim: *"three generated, normal-mapped props that
resolved... but nothing ever drew them until this table."* The eight town props never got the
equivalent fix. `props-forge-salamander.png` is finished, characterful art sitting unused.

## 6. Holes

### Missing art for live content
Six Tier 8-14 forward-ladder recipes are craftable **today** with no icon —
`item-gloomsteel-blade`, `item-wardenweave-mail`, `item-moonresin-draught`, `item-cinderforge-blade`,
`item-ashguild-plate`, `item-emberglass-draught` (`RecipeTable.cs:94-128`). They degrade to a generic
slot glyph plus the recipe name. Not broken; generic.

### Silent-fallback risks
- **`MineWatch` single point of failure.** If the `"mine-backdrop"` id stops resolving, `HasContent`
  goes false and the **entire** hero-march panel hides forever, every phase, no warning. No test pins
  that specific id.
- **`IconRegistry.Ore` has no fallback tier.** It calls `GD.Load` with no existence guard, unlike
  `Art()`. One past incident produced 260 native resource errors in a single playtest. Green today
  across all 19 ores; reopens the moment an ore ships without an icon.
- **`IconRegistry.Manifest()`** degrades a corrupted `art-manifest.json` to "nothing present" with no
  warning — quieter than the audio equivalent, which does warn.
- `TownAssets2D.Placeholder` is the one fallback that announces itself (`GD.PrintErr` +
  `PlaytestLog.Note`), but a human sees only a magenta box.

### Never animated
- **Monsters have no gait.** All five Mine monsters are single-frame; in combat they slide, recoil
  and flash but never breathe or walk. Structural: the sprite generator only authors hero/player/
  townsfolk bodies, and the SDXL chain cannot hold identity across frames at sprite scale (§7).
- Boss/venue creatures are single-frame painterly portraits with normal maps — lighting response
  only, no motion anywhere.
- **Every station and prop is inert** — anvil, bellows, furnace, crates, lanterns. No anvil sparks,
  no furnace pulse. Only the tree got a sway pass.

### Silent moments — three of the four honest channels
Link 2 of the five-link chain says a craft reaches a hero through four channels. Only one makes a
sound.

| Channel | Sound |
|---|---|
| Shelf | `Shelve` on stocking, `Coin` when a hero buys |
| **Counter** | none — `CounterPanel` has zero `Cue.Play`, not even on the sale landing |
| **Commission** | none — `CommissionBoard` accept/decline are silent |
| **Vigil runner** | none — `CampPanel.OnSend` is silent |

The campaign chronicle (`ChronicleScroll`) is also silent, while the Legends Wall beside it chimes.
`Cue.Click` is synthesized and has **zero call sites** anywhere.

### Unbuilt declarations
Seven `AssetSpec` entries have no art and never did: `forge-interior`, `tavern-interior`,
`gate-interior`, `town-ground-plaza`, `town-ground-plaza-worn`, `town-mine-strip`, `player-avatar`.
The first six were superseded by differently-named `town2d-*` pixel art; the specs were never
deleted. `player-avatar` has had no live caller since U4.

Eleven further specs are registered and describe-only — the intended null-tolerant gap, not a defect.

### Provenance gaps
Fourteen files can never be regenerated:
- `market`, `tavern`, `mine-gate`, `town-tavern` — live-referenced, no seed, no build.json, no script.
- `panel_banner_*` (4) and `player_smith*` (4, counting frames) — outside the AssetSpec contract
  entirely; their underscore ids would be rejected by `AssetSpecRules.IdGrammar`, so the registry and
  conformance tests cannot see them by construction.
- `town-forge`, `town-market`, `town-mine-gate` — seed logged in `seeds.generated.md`, build-half
  never written.

### Dead tooling
`tools/3dgen/` and `tools/blender/` target the retired 3D town. Twelve GLBs sit committed at
`art/gen-candidates/2026-07-21/glb/` with no consumer. `gpu_guard.sh` guards a path that no longer
runs. **`tools/blender/normalize_glb.py` has never executed once** — its own header still says
Blender is not installed and to treat the first run as a smoke test.

## 7. Rejected approaches — do not re-litigate

- **SDXL at sprite scale.** Rejected three separate times with the same finding.
  `gen_town_sprites.py`: *"at sprite scale a diffusion render downscales to mush, and it cannot hold
  identity or palette between frames... an SDXL pass at 768 returned a two-view character turnaround
  in saturated purples — unusable at sprite scale even before the downsample."*
- **A bigger source PNG as free quality.** Under `Nearest` filtering with no mipmaps, scaling down is
  point-sample decimation, not a quality downsample — a bigger PNG lands proportionally bigger, not
  sharper at the same size. (This is the same mechanism as the decimation bug fixed in #471.)
- **Self-referential recolour.** `recolor-forge-roof.py`'s first version re-read its own output,
  assuming the hue shift was idempotent. It failed `--check` immediately: the target terracotta and
  the magenta defect sit only ~57° apart on the hue wheel.
- **A linear hue range for that gate** clipped the roof's brightest stripe, which wraps past 360°.
  Replaced with a circular hue-distance test.
- **Ground desaturation as the palette fix** was tried first and moved the needle far less than the
  saturation boost that shipped.
- **Swapping the whole exterior asset family to fix one bad roof** (PR #316) — owner's verdict was
  "buildings look WORSE, we only asked for interior changes." Rescoped to the roof hue alone.
- **A `night-still-long.mp3` Camp ambience** measured -27.15 LUFS and needed +5.45dB to reach
  parity — but sat at a near-constant -63 dBFS floor for nearly its whole length. That is hiss, not
  music, and the boost amplified it. Heard as "loud static randomly at night." Reverted, then deleted.

## 8. To-dos, ranked by whether a player would notice

1. **Sound the three silent channels** — counter, commission, vigil runner. Link 2 of the spine is
   three-quarters mute.
2. **Six missing item icons** for live Tier 8-14 recipes.
3. **Pin `"mine-backdrop"`** in the resolution census so one typo cannot delete the whole MineWatch
   panel.
4. **Monster idle/gait frames** — the things the heroes fight never move. Needs a decision on
   approach first, since neither existing pipeline produces monster gait.
5. **Mount or delete the eight town props** — 11.87MB of finished art drawn by nothing, with a
   documented sibling fix to copy.
6. **Give `IconRegistry.Ore` a fallback tier** before the next ore ships.
7. **Station idle cues** — anvil sparks, furnace pulse. The workshop is inert.
8. **Delete the dead 3D tooling** and its 44MB of candidate output, or state why it is kept.
9. **Backfill provenance** for the four live town buildings; decide whether the seven dead specs and
   `player-avatar` get deleted.
10. **Pixel font** — `GameTheme.cs:120` still has `TODO(font)`; every screen renders in the engine
    default body face rather than the intended pixel one.

## Owner calls, not engineering ones

- `hero-vanguard.png` renders the knight and his greatsword as two disconnected floating subjects —
  a diffusion artifact. The other five portraits are single cohesive poses. Reroll or accept?
- Ground tiles, `ui-frame-wood`, and several props are honestly-disclosed flat-colour placeholders
  (130-770 bytes each). They are marked as such in `godot/assets/art/README.md`; the question is when
  they stop being acceptable.
- The six heroes standing in formation on the road reads as staged to at least one viewer. Design
  feel, not a defect.
