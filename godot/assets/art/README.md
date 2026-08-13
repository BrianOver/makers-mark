# Generated art

PNGs here are produced by the local ComfyUI/SDXL pipeline (free, $0/image) driven by the
master art-Claude through the `comfyui-mcp` server — see
`docs/design/art-pipeline-architecture.md` (roles, lifecycle, lock gate) and
`docs/design/asset-style-spec.md` (two-track styles, palette, prompt composition).

- An asset's request-half lives in `art/specs/<module>/` (`AssetSpec`); its build-half
  (seed, model, sha256, provenance) lands in `art/build/<id>.build.json` when locked.
- Rendering binds by NAME via `IconRegistry.Art("<id>")` — null-tolerant, so scenes stay
  green while a texture is still ungenerated. The game runs fine with this directory empty.
- File layout: `<track>/<id>.png` + `<id>_n.png` (normal map, when the spec asks for one).

Review each image against `docs/style-bible.md` before committing. The retired Gemini
generator (`tools/AssetGen`, paid API) was removed 2026-07-17; its history is in git.

## Fresh-checkout render convention (R8)

A plain `git clone` has no `.godot/imported/*.ctex` cache, so `GD.Load` returns `null` for a
committed PNG even with its `.import` sidecar checked in — the cache, not the sidecar, is what
makes a texture loadable. The durable fix is **both**:

- **Every committed pair ships its `.png.import`.** Commit `<id>.png` + `<id>_n.png` (when the
  spec calls for a normal map) together with `<id>.png.import` + `<id>_n.png.import`, minted by
  the **pinned Godot 4.6.3-stable .NET** engine (hard rule #2 — a non-pinned editor silently
  rewrites uids/import metadata and breaks CI). This pins stable `uid://`s across machines.
- **`play.bat` runs a headless import pre-pass before the interactive launch** —
  `godot --headless --import --quit` — so a fresh checkout materializes the `.ctex` cache for
  every committed PNG regardless of sidecar state. This is the load-bearing fix; the sidecars
  make it deterministic on top.

Regenerate sidecars only on the pinned engine (`.godot-version` is the source of truth). If a
re-import rewrites uids/import metadata on unrelated assets, `git checkout --` those files before
committing so only the intended sidecars land in the diff.

## Two tracks: generated, and hand-authored (2026-07-29)

Not everything here comes from SDXL, and the split is deliberate rather than historical.

**Generated track** — backdrops, portraits, monsters, items, props, panel banners. Big enough that
a diffusion render survives, so it goes through the ComfyUI/SDXL chain above and lands a
provenance record in `art/build/<id>.build.json`.

**Hand-authored pixel track** — the 20×32 town character bodies (`town2d-hero-*.png`; 20×36 before
U6, docs/plans/2026-08-02-002 — resized so the cast reads as six drawn people while still staying
under the player's height, and so sentinel/skirmisher/occultist could get a real town body instead
of the roster-SVG fallback), authored as
explicit pixel grids in `tools/art/gen_town_sprites.py`. These are NOT generated, for the reason
`docs/plans/2026-07-28-001-feat-animation-motion-adventure.md` already recorded: at sprite scale a
diffusion render downsamples to mush, and it cannot hold identity or palette between a base frame
and its step frame. Confirmed again on 2026-07-29 — an SDXL pass at 768 returned a two-view
character turnaround in saturated purples, unusable before the downsample even began.

So they are authored one pixel at a time. That buys three things the generated track cannot:

- **The diff is the art.** A palette or silhouette change shows up as readable text in review.
- **Byte-identical on every machine**, with no GPU in the loop — regenerate with
  `python tools/art/gen_town_sprites.py`, drift-check with `--check`.
- **The base and step frames differ ONLY in the legs**, which is what makes the 2-frame walk read
  as a stride rather than a flicker. `godot/tests/TownSpriteArtTests.cs` pins that invariant, plus
  the pinned 20×32 size, a minimum distinct-colour count (so art cannot silently regress to a flat
  placeholder box), and the neutral-body contract `TownAssets2D.ForHero` depends on — bodies stay
  desaturated so `ClassColors.RoleColor` can multiply the class colour in without double-tinting.
  `godot/tests/CastProportionTests.cs` (U6) pins the taller invariant: every hero class's
  on-screen height stays under the player's, permanently.

Hand-authored assets get no `art/build/*.build.json`: that record's fields (seed, model, sampler,
AI disclosure) describe a generation run, and filling them for a hand-drawn grid would be a
fiction. The script itself, committed and diffable, IS the provenance.

**Still placeholder-tier**, listed honestly so it is not mistaken for finished work: the three
ground tiles (`town2d-tile-*`, 2-3 colours), `town2d-ground-atlas`, `town2d-prop-tree`,
`town2d-prop-crate`, `mine-gate`, and `ui-frame-wood`. All are flat programmer art from a
throwaway script. The hero bodies were done first because they are what the player watches move.

## Ids outside the `AssetSpec` grammar — deliberate, not a bug (U9)

`panel_banner_bounties/heroes/shop/tavern` and `player_smith`/`player_smith_step`/
`player_smith_walk2`/`player_smith_walk4` use `snake_case`, which `AssetSpecRules.IdGrammar`
rejects (every registered `AssetSpec.Id` must be kebab-case). That is why neither family has — or
will ever get — an `AssetSpec` entry: the registry and `AssetConformanceTests` cannot see them by
construction, not because someone forgot to register them. Do not "fix" the underscores: every
`UiKit.SceneBanner("panel_banner_*")` call site (`ShopPanel`, `TavernPanel`, `HeroPanel`,
`BountyPanel`) and every `PlayerController2D`/`TownAssets2D` `player_smith*` id names these exact
literals.

The two families are NOT in the same provenance state, though, and U9's own provenance sweep is
the reason that distinction is worth recording here:

- **`player_smith*` (4 files) is regenerable.** `tools/art/gen_town_sprites.py`'s `PLAYER_SPRITES`
  dict is merged into the same `all_sprites` map the hand-authored hero/townsfolk bodies come from
  and written by the same `main()` loop — `python tools/art/gen_town_sprites.py` reproduces all
  four byte-identically today. It belongs with the hand-authored pixel track above in every way
  except id grammar.
- **`panel_banner_*` (4 files) is not.** No script in `art/pipeline/` or `tools/art/` produces
  these; there is no seed, no generator, nothing to re-run. They are the same kind of gap as
  `market`/`tavern`/`mine-gate`/`forge`/`noticeboard` (pre-pipeline SDXL-era art with no surviving
  build-half) — see `art/build/{market,tavern,mine-gate}.build.json`'s `"Status":
  "unreproducible-legacy"` records for the pattern this family shares but, being outside the
  `AssetSpec` grammar, cannot register a matching spec for.
