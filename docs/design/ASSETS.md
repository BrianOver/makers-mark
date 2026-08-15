# Assets — the whole inventory

Every image, animation, sound and voice line in the game, where it comes from, and whether anything
actually draws or plays it. Written 2026-08-12 against `d03c3af`; **§5 through §8 rewritten
2026-08-13** after the completion wave (#485-#492) closed nine of the ten holes §6 opened.

**Read the counts as of that commit, not as gospel.** Every number here has a command beside it that
re-derives it. If a number and the command disagree, the command is right and this file is stale —
fix it or delete it (CLAUDE.md rule 8). The counts in §1 predate the wave's deletions: 121 files left
the repo, so re-run the commands before quoting any of them.

```bash
ls godot/assets/art/*.png | wc -l          # 355 after the 2026-08-14 variation pools (was 227)
ls godot/assets/icons/*.svg | wc -l        # 28 icons (9 glyphs + 19 ore)
ls godot/assets/audio/*.mp3 | wc -l        # 4 music tracks
find godot/assets -name "*.ogg" | wc -l    # 49 narrator lines
ls art/build/*.json | wc -l                # 92 provenance records: 84 SDXL + 8 backfilled by U9
```

The authoritative wiring check is an engine test, not this document:
`dotnet test godot/tests --settings .runsettings` runs `AssetResolutionCensusTests`, which fails
loudly if a referenced id stops resolving. It does **not** run in the fast lane.

---

## 1. Images — 389 files

*(The families that lost files are the deleted rows in §5 — sprites, faction crests, `town-*`,
`shop-interior`, `town2d-*` candidates — none of which appear here, because none of them were ever
drawn. Item-icon and town-body rows re-counted 2026-08-14; the rest still predate that wave.)*

| Family | Count | Wired by | Origin |
|---|---|---|---|
| Hero portraits | 6 + 6 normals | `AssetCatalog.HeroPortrait` → Heroes/Counter/Ledger/Tavern panels | SDXL |
| Monster portraits (4 venues) | 38 | `AssetCatalog.MonsterPortrait` → Bestiary, MineWatch, DelveStage | SDXL |
| Venue backdrops + entrances | 6 | `AssetCatalog.VenueBackdrop` / `VenueEntrance` | SDXL |
| Item icons | 48 (45 recipes + 3 rival category) | `ForgePanel`, shop surfaces | SDXL |
| Town2D hero + townsfolk bodies | 160 (8 characters x 5 variants x 4 frames) | `TownAssets2D.ForHero`, `HeroActor2D` | Hand-pixel Python |
| Town2D stations / shells / signs / props | ~42 | `WorkshopVocab`, `InteriorLayout2D`, `TownAssets2D.ForProp` | Hand-pixel Python |
| Player smith | 5 | `PlayerController2D` | Hand-pixel Python |
| Mine monster minis | 25 (5 kinds × 5 variants) | `DelveStage` | Hand-pixel Python |
| UI chrome (panel banners, frame) | 5 | `UiKit.SceneBanner`, `GameTheme` | SDXL + placeholder |
| Icons (glyph + ore SVG) | 28 | `MainUi`, `IconRegistry.Ore` | Hand-authored SVG |

The 19 `ore_*.svg` match `MaterialRegistry.PricedPool` exactly. The two registered-but-inert
materials (electrum, orichalcum) correctly have none.

### Variation pools (2026-08-14)

Owner direction: heroes, NPCs, enemies and crafted items should each look a little unique, drawn
from a committed collection rather than generated at runtime. The convention is one id namespace,
not a new one: **the base id is variant 1**, and siblings suffix a contiguous run from 2 —
`town2d-hero-vanguard`, `-v2`, `-v3`, … `GodotClient.ArtVariants.Pick(baseId, keyspace, simId)`
chooses among whatever is committed, hashing a stable sim id (FNV-1a, never `GetHashCode` — .NET
randomizes that per process, so the whole cast would be re-drawn on every launch and again after a
load). A base id with no siblings returns itself, so adding variation to any family is purely
additive and wiring a call site early costs nothing.

Live pools: the six hero town bodies and both civilian builds, 5 each (`gen_town_sprites.py` —
skin, hair and a garment dye-tint vary; **the class hue never does**, because it is the same colour
the class's panel chip and ledger row use and it has to stay readable as identity). Item icons are
wired through `IconRegistry.ItemArtId(recipeId, slot, itemId)` and inert until their pools exist —
`gen-item-variants.py` renders those.

The five mine monsters joined the pools on 2026-08-14 (§11.10 U4/U5). They previously had **no
generator at all** — `art/build/town2d-monster-*.build.json` recorded `unreproducible-legacy`, and
measurement showed why the "Hand-pixel Python" credit in the §1 table could not have been true: 972
to 5,755 distinct opaque colours each, at sizes from 60×41 to 84×99, which is a generated image
downscaled rather than authored pixel art. They are now genuinely procedural and that credit is
honest. Their pick keys on **floor + kind**, never kind alone (`DelveStage.MonsterBodyId`) — kind is
a catalogue key, so keying on it would draw every cave rat in the campaign identically.
`BestiaryPanel` keeps the base id on purpose: it is a reference catalogue, where one canonical
picture per kind is the right answer.

## 2. Animation

**Frame-based.** Nine characters have the full 4-frame gait (base / `_walk2` / `_step` / `_walk4`):
six heroes, two townsfolk builds, the player — and since the variation pools landed, so does every
one of their five committed bodies (41 gaits, 164 frames). Driven by `SpriteMotion`, consumed by
`HeroActor2D`, `PlayerController2D`, `TownsfolkNpc2D`, `MarketLife2D` and `MineWatch`.
`TownSpriteArtTests.EveryVariantBody_ObeysTheSameGaitInvariantsAsItsBase` holds every pool body to
the invariants the six base bodies always had — it was written because the first 128 variants shipped
`.import` sidecars Godot had defaulted to `fix_alpha_border=true`, and a full green engine run said
nothing, since the older guard iterates six hand-listed class ids.

**Twenty-two procedural animators, all wired, zero orphans** — the wave added the monster
idle-breathe (`DelveStage`, U6) and the forge ember glow (`ForgeEmberGlow2D`, U7). `SpriteMotion`, `TreeSway`,
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

**SFX** — 22 cues, **all synthesized in code** (`Synth.cs`). There are zero committed SFX files. Since
U2 every cue has at least one production call site, and a census test in `AudioTests` fails if one
stops having any — `Cue.Click` sat synthesized-and-unreferenced until then.

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
diffs against the committed file.** 219 assets (the 2026-08-14 variation pools added 128). This is
the strongest provenance tier in the repo.

**C. 3D — gone.** The TRELLIS/Blender tooling from before the 2.5D pivot was deleted on 2026-08-13
(U8), along with the 45MB of candidate output it produced (U5). Two pipelines remain, and that is the
whole list.

## 5. Orphans — cleared 2026-08-13

Committed, resolution-tested, and drawn by nothing. Every row this section listed is now either
**drawn** or **deleted**. Kept as a record because the disposition rule is the reusable part: *mount
finished art, delete superseded or dead-pipeline output, and never delete on size alone.*

**121 files, 52.11MB deleted** (U5, #490) — measured with `git cat-file -s` against every deleted
blob, not estimated.

| What | Files | Disposition |
|---|---|---|
| `godot/assets/sprites/2d/*.svg` | 42 | deleted — early 2.5D scaffold, superseded by `town2d-*.png` |
| `godot/assets/sprites/{forge,ground_tile,memorial_stone,mine_gate,shop,tavern}.svg` | 12 | deleted with `IconRegistry.Building()`, which they were the only reason to keep |
| `faction-*-emblem.png` | 4 | deleted — `MainUi` draws the faction's ore as its chip icon; the crests never appeared |
| `town-{forge,market,tavern,mine-gate}.png(+_n)` | 16 | deleted — the LitOverlay scene that drew them is long gone |
| `shop-interior.png(+_n)` | 4 | deleted — superseded by `town2d-market-interior-shell` |
| `town2d-{board,forge,market,…}.png` | 12 | deleted — early candidates; the exterior reverted to the SDXL ids |
| `art/gen-candidates/2026-07-21/*` | 24 | deleted — abandoned 3D pipeline output, 45MB |
| `godot/assets/candidates/heroes-r3/*` | 6 | deleted — human-review receipts, never shipped |
| **`props-*` (8 warm-hub props)** | 16 | **mounted, not deleted** (U4, #487) — 11.87MB of finished art now drawn in the town |

**The town props were the sharpest case, and the reason the wave exists.** Their sibling trio
(`gloomwood-mushroom-cluster`, `gloomwood-toll-booth`, `sunkencrypt-donation-plate`) had the identical
bug and was fixed months earlier — ids that resolved, tests that passed, and nothing that ever drew
them. The town set never got the equivalent. They are now in `TownLayout2D.Props`, resampled offline
to the size the town actually draws them at (11.87MB → 31KB; see §7 on why not a runtime scale).

**The count this section first published was wrong, and how it was wrong is worth keeping.** It said
"61 files, ~20.4MB." That is right for the first seven rows and wrong for the table — a scope error,
not an arithmetic one; the `gen-candidates` row alone is 45MB. The plan corrected it to ~90 files /
~64MB, and the real figure after U4 mounted the props rather than deleting them is 121 files /
52.11MB. Three separate numbers, each honest at the time, and only the last one was measured.

## 6. Holes

**Nine of the ten holes this section opened are closed** (PRs #485-#492, 2026-08-13). What each one
was, and what it is now, is below — kept rather than deleted, because the *shape* of each failure is
the reusable part.

### Missing art for live content — CLOSED (2026-08-14)
Six Tier 8-14 forward-ladder recipes were craftable with no icon — `item-gloomsteel-blade`,
`item-wardenweave-mail`, `item-moonresin-draught`, `item-cinderforge-blade`, `item-ashguild-plate`,
`item-emberglass-draught` (`RecipeTable.cs:94-128`) — and degraded to a generic slot glyph plus the
recipe name. All six are now committed, and the recipe→icon mapping is pinned in both lanes (§8).

The hardware block was real and cleared the moment the GPU was idle. What the batch measured is
worth more than the six PNGs:

- **The Active master negative does not stop SDXL drawing a design study.** The first batch returned
  8 of 8 concept sheets — multi-blade variation plates, inventory grids, framed plaques on parchment
  — despite the master negative already listing `sprite sheet`, `tiled`, `duplicated`, `frame` and
  `border`. `ItemSpecsLadder.SingleItemOnDark` is the escalation. Even hardened, usable yield was
  ~1-3 singles per 16 candidates, so budget curation time, not just GPU time.
- **A wrong silhouette costs more than a wrong background.** Armour drifted to "worn by a figure"
  and vessels to "on a carved plinth". BiRefNet deletes a background for free; it keeps a body and a
  plinth, because they read as part of the subject. Two subjects were rewritten to say the item
  stands alone before the yield became usable.
- **The 42 sibling icons ship 4-9× larger than they draw.** `ShopPanel.ItemArtSize` is 56 and
  `UiKit.ArtRect` uses `ExpandMode.IgnoreSize` + `KeepAspectCentered`, so a 200-500px texture is
  downscaled into a 56×56 box every frame. The six new icons ship at a 112px long edge — a 2×
  reserve — and weigh ~79KB together. Re-sizing the existing 42 is a cheap offline pass nobody has
  run; it needs no GPU and no regeneration.
- **Three shipped icons already fail the house rule** they were curated under: `item-longsword` kept
  an ornate frame and an opaque white card, and `item-kite-shield` and `item-bulwark` are each two
  items in one texture. Cosmetic, live today, and an owner call rather than an engineering one.

### Silent-fallback risks — CLOSED (U1)
- **`MineWatch` single point of failure.** A missing `"mine-backdrop"` used to hide the **entire**
  hero-march panel forever, every phase, with no warning. The census now pins that exact id, and both
  places that set `HasContent` emit through `EngineDistress.Warn` — so a runtime failure (partial
  checkout, corrupt import cache, a rename the census literal misses) becomes a reported anomaly
  rather than an empty panel.
- **`IconRegistry.Ore` had no fallback tier** — `GD.Load` with no existence guard, once worth 260
  native resource errors in a single playtest. It now degrades to a placeholder swatch and warns once
  per missing key.
- **`IconRegistry.Manifest()`** still degrades a corrupted `art-manifest.json` to "nothing present"
  with no warning — quieter than the audio equivalent, which warns. Not closed; not yet bitten.

### Never animated — CLOSED (U6, U7)
- **Monsters breathe.** All five Mine monsters carry a procedural idle — an eased swell-to-peak-then-
  release on a cached per-monster base scale, accumulated delta only, frozen by the same pause
  contract the feed uses. No new art: the owner chose procedural motion over authoring gait frames,
  because the five minis are five different ad-hoc canvases and `gen_town_sprites.py` is a fixed
  humanoid rig that never references a monster.
- **The forge is lit.** Furnace and anvil carry a warm additive pulse (0.35 Hz, phase-offset), which
  is a property of the object rather than the shared "you can click this" affordance halo
  `Building2D.Tell` puts on every station.
- Boss/venue creatures remain single-frame painterly portraits — lighting response only. Deliberately
  out of scope; U6 covered the five Mine monsters.

### Silent moments — CLOSED (U2)
Link 2 of the five-link chain says a craft reaches a hero through four channels. All four now make a
sound.

| Channel | Sound |
|---|---|
| Shelf | `Shelve` on stocking, `Coin` when a hero buys |
| Counter | `Click` on Present / Accept / Hold Firm / Counter; the sale's own `Coin` still comes from `MarketLife2D`, exactly once |
| Commission | `Click` on accept, `Rejected` on decline — deliberately different, so a refusal never sounds like a success |
| Vigil runner | `Coin` on a real delivery only, never on a refusal |

`Cue.Click` had zero call sites and a doc comment describing exactly this gap; it is wired, and a
census test now fails if any cue goes unreferenced.

### Unbuilt declarations — CLOSED (U9)
The seven dead `AssetSpec` entries (`forge-interior`, `tavern-interior`, `gate-interior`,
`town-ground-plaza`, `town-ground-plaza-worn`, `town-mine-strip`, `player-avatar`) are deleted from
`TownSpecsExtra.cs`, along with `AssetCatalog.PlayerAvatarId`. Eleven further specs remain registered
and describe-only — the intended null-tolerant gap, not a defect.

### Provenance gaps — CLOSED (U9)
Every one of the 196 manifest ids now has either a `build.json` or a documented exception, and
`AssetProvenanceTests` asserts it rather than this file claiming it.

- `market`, `tavern`, `mine-gate` and the five `town2d-monster-*` minis are **live and genuinely
  unreproducible** — recorded as `unreproducible-legacy` with null seed and model. No seed was
  invented for any of them; a fabricated provenance record is worse than an honest gap.
- The four `town-*` ids are gone entirely — U5 deleted the art, so U9's provenance for them would
  have been a record describing a file that is not there. A test now pins their absence in both
  directions.
- **Correction:** this section previously said `player_smith*` "can never be regenerated." That is
  false — `tools/art/gen_town_sprites.py` writes all four frames. What is true of `panel_banner_*`
  and `player_smith*` is narrower: their underscore ids sit outside `AssetSpecRules.IdGrammar`, so the
  registry and conformance tests cannot see them by construction. That is recorded in
  `godot/assets/art/README.md` so nobody "fixes" it.

### Dead tooling — CLOSED (U5, U8)
`tools/3dgen/` and `tools/blender/` are deleted, along with `UiTestSupport.WalkUntilArrived3D` — the
one dead helper typed against `Node3D`. (Nothing *renders* 3D; the earlier claim that no `Node3D`
existed anywhere was wrong, and that helper was the reason.) The 45MB of candidate output the
pipeline produced went with §5's orphan sweep.

## 7. Rejected approaches — do not re-litigate

- **SDXL at sprite scale.** Rejected three separate times with the same finding.
  `gen_town_sprites.py`: *"at sprite scale a diffusion render downscales to mush, and it cannot hold
  identity or palette between frames... an SDXL pass at 768 returned a two-view character turnaround
  in saturated purples — unusable at sprite scale even before the downsample."*
- **A bigger source PNG as free quality.** Under `Nearest` filtering with no mipmaps, scaling down is
  point-sample decimation, not a quality downsample — a bigger PNG lands proportionally bigger, not
  sharper at the same size. (This is the same mechanism as the decimation bug fixed in #471.)
- **Silkscreen for body text** (tried and rejected 2026-08-13, U10). Its lowercase glyphs are
  cap-height — inherited from the 2001 bitmap original — so every sentence of prose renders as visual
  ALL-CAPS: *"They've made camp above the deep floors…"* came out *"THEY'VE MADE CAMP ABOVE THE DEEP
  FLOORS…"*. Fine for headings and labels, where it ships; wrong for a game whose ledger, gossip and
  narration are long prose. A future pixel-body face needs true lowercase, not just a pixel grid.
- **Runtime downscale as a way to use oversized art** (U4). The eight warm-hub props were ~1000px
  mounted at `Scale ≈ 0.03`; it rendered correctly and was still wrong — a 25-30× runtime downscale of
  a 1MB texture shimmers as the camera pans (no mipmaps on these) and holds ~33MB of VRAM to draw
  thumbnails. Resampled offline once instead: same picture, 11.87MB → 31KB, no scale knob left to get
  wrong. Same lesson as the `CharacterSpriteScale` decimation fix below.
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

## 8. To-dos

**None.** The last row — six missing item icons for the live Tier 8-14 recipes — closed on
2026-08-14 once the owner freed the GPU (restarting ComfyUI dropped 8058MiB of resident VRAM to
1059MiB, clearing the ≥14GB floor). Every craftable recipe now has committed art, and two tests hold
it that way in both lanes: `ItemIconCoverageTests` (art) pins that every recipe has a spec and every
item spec names a real recipe; `AssetResolutionCensusTests.EveryRecipeItemIcon_ResolvesToCommittedArt`
(engine) pins that the pixels are committed and loadable. Both read `ProfessionRegistry.AllRecipes`
rather than a hand-copied list, so recipe #49 is covered the day its row lands.

The other nine shipped on 2026-08-13 as PRs #485-#492. Two things worth carrying forward more than
the list itself:

- **Every unit that changed what is on screen was rendered and looked at**, and looking caught defects
  the assertions passed over — two props reading as one cluttered blob, and a body font that turned
  every sentence into visual ALL-CAPS.
- **Three tests failed the first time they ran honestly, and none of the three was a product defect.**
  A cue test asserted silence where the app-wide rejection cue legitimately sounds; a sale test read
  "Coin played 0 times" because its advance loop exited before the customer had spawned; a pause test
  passed against removed wiring because its window happened to be exactly one breath cycle. A test
  that can pass with the feature deleted is the failure shape this repo keeps finding.

## Owner calls, not engineering ones

- `hero-vanguard.png` renders the knight and his greatsword as two disconnected floating subjects —
  a diffusion artifact. The other five portraits are single cohesive poses. Reroll or accept?
- Ground tiles, `ui-frame-wood`, and several props are honestly-disclosed flat-colour placeholders
  (130-770 bytes each). They are marked as such in `godot/assets/art/README.md`; the question is when
  they stop being acceptable.
- The six heroes standing in formation on the road reads as staged to at least one viewer. Design
  feel, not a defect.
