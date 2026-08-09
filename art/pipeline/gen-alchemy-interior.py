"""Author the five Alchemy (Apothecary) station props.

Bug fix, 2026-08-09: WorkshopVocab.ByProfession[AlchemyProfession.Id] (godot/scripts/town2d/
WorkshopVocab.cs) has pinned these five station ids since U7 (world-and-interiors plan,
docs/plans/2026-08-02-004) -- "cauldron"/"still"/"reagent-shelf"/"potion-rack"/"herb-bundles",
sprite ids town2d-station-alch-cauldron/-alch-still/-alch-shelf/-alch-rack/-alch-herbs -- but U8
(the unit meant to paint them) never ran, same gap art/pipeline/gen-engineering-interior.py's own
docstring documents for Engineering's four stations. The five ids sat in
AssetResolutionCensusTests.KnownPendingIds with no PNG and no manifest entry, so any player who
selected Alchemy saw TownAssets2D.ForStation's loud magenta placeholder for all five stations. This
script paints the real pixels; the fix's own PR removes the five ids from KnownPendingIds in the
same commit AND adds a no-escape-hatch census case (mirroring
EngineeringStationArtIds_ResolveToCommittedArt_NeverAPlaceholder) so these five specifically can
never again resolve to nothing without a red build.

The shared workshop shell needs no repainting here either, same reason as Engineering: KTD-3
already made the workshop ONE shell (town2d-forge-interior-shell) reused by every profession.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same discipline as every sibling script in this pipeline (gen-forge-interior.py, gen-market-
interior.py, gen-tavern-interior.py, gen-gatehouse-interior.py, gen-engineering-interior.py): every
colour below is SAMPLED FROM COMMITTED town2d-* PIXELS, never picked by eye, so the Apothecary
reads as built from the same material as the Forge the player already stood in. Byte-reproducible
(`--check`), editable without a GPU or an image editor -- no GPU/diffusion model is used here, per
the same reasoning tools/art/gen_town_sprites.py's own header documents: at sprite scale a
diffusion render downscales to mush.

PALETTE PROVENANCE -- the structural family every interior script above already verified against
committed pixels (re-quoted here verbatim rather than re-run, per gen-gatehouse-interior.py's own
precedent; counts match gen-engineering-interior.py's own re-sample):
    town2d-forge.png   (58, 42, 84, 255)   x1214 -> IRON
    town2d-forge.png   (38, 27, 61, 255)   x566  -> IRON_DEEP
    town2d-tavern.png  (86, 55, 92, 255)   x637  -> IRON_LIT
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-forge.png   (107, 76, 154, 255)  x20  -> ARCANE  (this room's brewing-liquid accent --
                                                              used more than once here on purpose,
                                                              since alchemy's whole craft IS the
                                                              arcane brew, unlike engineering's
                                                              single precious cog)
    town2d-forge.png   (63, 176, 172, 255)  x19  -> COOLANT (a second brew-liquid tone, so the
                                                              cauldron/still/rack don't all show the
                                                              exact same potion colour)
    town2d-tavern.png  (90, 54, 46, 255)   x281  -> WOOD

New for this room -- sampled fresh, not reused from a sibling script, since no prior interior
needed a leaf-green or a light-tan glass tone:
    town2d-tile-grass.png (79, 106, 82, 255) x12  -> HERB_GREEN (the town's own grass tile -- the
                                                                  apothecary's dried herbs match the
                                                                  green already growing outside its
                                                                  door, the same "built from the
                                                                  town's own material" discipline
                                                                  every sibling script follows)

Usage:
    python art/pipeline/gen-alchemy-interior.py [--check]

    --check   render every sprite in memory and compare against the committed PNGs; writes
              nothing and exits non-zero on any drift. Same contract as every sibling script.
"""
from __future__ import annotations

import argparse
import pathlib
import sys

from PIL import Image

OUT_DIR = pathlib.Path("godot/assets/art")

# ── palette (see provenance block above) ──────────────────────────────────────────────────────
CLEAR = (0, 0, 0, 0)
IRON_DEEP = (38, 27, 61, 255)
IRON = (58, 42, 84, 255)
IRON_LIT = (86, 55, 92, 255)
BONE = (216, 207, 224, 255)
ARCANE = (107, 76, 154, 255)
COOLANT = (63, 176, 172, 255)
WOOD = (90, 54, 46, 255)
HERB_GREEN = (79, 106, 82, 255)


# ── pixel helpers (verbatim from gen-engineering-interior.py) ─────────────────────────────────
def rect(px, x0, y0, x1, y1, c, w, h):
    """Inclusive filled rectangle, clipped to a w x h canvas."""
    for y in range(max(0, y0), min(h - 1, y1) + 1):
        for x in range(max(0, x0), min(w - 1, x1) + 1):
            px[x, y] = c


def outline(px, x0, y0, x1, y1, w, h, c=IRON_DEEP):
    """Inclusive 1px border, clipped to a w x h canvas."""
    for x in range(max(0, x0), min(w - 1, x1) + 1):
        if 0 <= y0 < h:
            px[x, y0] = c
        if 0 <= y1 < h:
            px[x, y1] = c
    for y in range(max(0, y0), min(h - 1, y1) + 1):
        if 0 <= x0 < w:
            px[x0, y] = c
        if 0 <= x1 < w:
            px[x1, y] = c


def circle(px, cx, cy, r, c, w, h):
    """Filled circle via squared-distance test -- integer-only, no transcendental math."""
    for y in range(max(0, cy - r), min(h - 1, cy + r) + 1):
        for x in range(max(0, cx - r), min(w - 1, cx + r) + 1):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                px[x, y] = c


def holes(im: Image.Image) -> list[tuple[int, int]]:
    """Transparent pixels fully enclosed by opaque ones -- see gen-forge-interior.py's own doc for
    why this is a guard and not just an eyeball check."""
    w, h = im.size
    px = im.load()
    found = []
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            if px[x, y][3] > 8:
                continue
            up = any(px[x, yy][3] > 8 for yy in range(0, y))
            down = any(px[x, yy][3] > 8 for yy in range(y + 1, h))
            left = any(px[xx, y][3] > 8 for xx in range(0, x))
            right = any(px[xx, y][3] > 8 for xx in range(x + 1, w))
            if up and down and left and right:
                found.append((x, y))
    return found


# ── stations ────────────────────────────────────────────────────────────────────────────────────
def render_cauldron() -> Image.Image:
    """24x24. Round iron cauldron on three squat legs; ARCANE brew simmering just under the rim
    with a single COOLANT bubble breaking the surface -- the room's primary craft focus, so it
    carries the heavier accent (mirrors the furnace carrying gen-forge-interior.py's one rune)."""
    w, h = 24, 24
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    circle(px, 12, 13, 8, IRON, w, h)                  # pot body
    for y in range(4, 22):                              # upper-left lit face (town2d convention)
        for x in range(3, 13):
            if (x - 12) ** 2 + (y - 13) ** 2 <= 64:
                px[x, y] = IRON_LIT
    rect(px, 4, 12, 20, 13, IRON_DEEP, w, h)           # waistline shadow band
    rect(px, 3, 8, 21, 9, BONE, w, h)                  # rim lip catches the light
    outline(px, 3, 8, 21, 9, w, h)

    rect(px, 5, 9, 19, 9, ARCANE, w, h)                # brewing liquid, just under the rim
    px[9, 9] = COOLANT                                  # a bubble breaking the surface
    px[15, 9] = BONE                                    # a steam glint

    for lx in (5, 12, 19):                              # three squat legs
        rect(px, lx - 1, 21, lx + 1, 23, IRON_DEEP, w, h)

    return im


def render_still() -> Image.Image:
    """20x32. A tall condenser still: squat boiler drum, tapering column, small collection flask.
    This room's second ARCANE accent sits on the condenser cap where the vapour turns."""
    w, h = 20, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 3, 22, 16, 30, IRON, w, h)                # boiler drum
    rect(px, 3, 22, 9, 30, IRON_LIT, w, h)             # lit left face
    outline(px, 3, 22, 16, 30, w, h)
    rect(px, 4, 23, 15, 23, BONE, w, h)                # rim highlight

    rect(px, 7, 10, 12, 22, IRON_DEEP, w, h)           # tapering column
    outline(px, 7, 10, 12, 22, w, h)
    rect(px, 8, 11, 9, 21, IRON, w, h)                 # column lit stripe

    rect(px, 6, 5, 13, 10, IRON, w, h)                 # condenser cap
    outline(px, 6, 5, 13, 10, w, h)
    px[7, 5] = ARCANE                                   # the vapour-turn accent

    rect(px, 1, 27, 4, 31, IRON_DEEP, w, h)            # small collection flask beside the drum
    outline(px, 1, 27, 4, 31, w, h)
    rect(px, 2, 28, 3, 30, COOLANT, w, h)              # a trace of distillate

    return im


def render_reagent_shelf() -> Image.Image:
    """28x32. Same timber shelf frame as the Forge/Market/Workbench sets, holding reagent jars and
    drying herb sprigs instead of ingots -- the room's HERB_GREEN accent, sampled from the town's
    own grass tile."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)                 # frame
    outline(px, 1, 1, 26, 30, w, h)
    for y in (1, 11, 21, 30):                            # three shelf boards
        rect(px, 1, y, 26, y, IRON_DEEP, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)                  # top rim highlight

    for gx, fill in ((4, ARCANE), (15, COOLANT)):       # upper shelf: two glass reagent jars
        rect(px, gx, 4, gx + 6, 9, BONE, w, h)
        outline(px, gx, 4, gx + 6, 9, w, h)
        rect(px, gx + 1, 6, gx + 5, 8, fill, w, h)

    for gx in (3, 9, 15, 21):                            # middle shelf: drying herb sprigs
        rect(px, gx, 15, gx + 3, 19, HERB_GREEN, w, h)
        px[gx, 15] = BONE

    for gx, fill in ((5, HERB_GREEN), (16, ARCANE)):    # lower shelf: two more reagent jars
        rect(px, gx, 24, gx + 6, 29, BONE, w, h)
        outline(px, gx, 24, gx + 6, 29, w, h)
        rect(px, gx + 1, 26, gx + 5, 28, fill, w, h)

    return im


def render_potion_rack() -> Image.Image:
    """28x32. Same timber rack frame, displaying corked potion bottles ready to sell -- three rows
    of small glass bottles cycling this room's own brew colours (no new hues)."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)
    outline(px, 1, 1, 26, 30, w, h)
    for y in (1, 11, 21, 30):
        rect(px, 1, y, 26, y, IRON_DEEP, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)

    fills = (ARCANE, COOLANT, HERB_GREEN, ARCANE)
    for row_index, row_y in enumerate((4, 14, 24)):
        for i, gx in enumerate((3, 9, 15, 21)):
            fill = fills[(i + row_index) % len(fills)]
            rect(px, gx, row_y, gx + 3, row_y + 5, BONE, w, h)           # bottle glass
            outline(px, gx, row_y, gx + 3, row_y + 5, w, h)
            rect(px, gx + 1, row_y + 2, gx + 2, row_y + 4, fill, w, h)   # potion liquid
            px[gx + 1, row_y] = IRON_DEEP                                # cork

    return im


def render_herb_bundles() -> Image.Image:
    """20x20. Honest flavor (Action: null): three herb bundles hanging flush under a wall rail --
    HERB_GREEN sprigs, nothing to craft directly from the bundle. Bundles are drawn CONTIGUOUS
    (each touching its neighbour) rather than as separate floating shapes, the same "no isolated
    third shape" caution gen-engineering-interior.py's own crate doc explains -- three separate
    hanging blobs bracket a gap between them that holes() coarsely flags as an enclosed hole."""
    w, h = 20, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 0, 0, 19, 2, WOOD, w, h)                  # wall rail
    outline(px, 0, 0, 19, 2, w, h)

    for cx in (4, 10, 16):
        rect(px, cx - 3, 2, cx + 3, 15, HERB_GREEN, w, h)   # bundle, flush under the rail
        outline(px, cx - 3, 2, cx + 3, 15, w, h)
        rect(px, cx - 3, 5, cx + 3, 6, WOOD, w, h)           # binding cord band
        px[cx, 15] = BONE                                     # a lighter sprig tip

    return im


SPRITES = {
    "town2d-station-alch-cauldron": render_cauldron,
    "town2d-station-alch-still": render_still,
    "town2d-station-alch-shelf": render_reagent_shelf,
    "town2d-station-alch-rack": render_potion_rack,
    "town2d-station-alch-herbs": render_herb_bundles,
}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true",
                    help="render in memory and compare against committed PNGs; writes nothing")
    args = ap.parse_args()

    drift = []
    for name, render_fn in SPRITES.items():
        fresh = render_fn()

        gaps = holes(fresh)
        if gaps:
            print(f"gen-alchemy-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
                  f"pixel(s), first at {gaps[:6]}", file=sys.stderr)
            return 1

        path = OUT_DIR / f"{name}.png"

        if args.check:
            if not path.exists():
                drift.append(f"{name}: no committed PNG at {path}")
                continue
            committed = Image.open(path).convert("RGBA")
            if committed.size != fresh.size:
                drift.append(f"{name}: size drift, committed {committed.size} vs fresh {fresh.size}")
                continue
            if list(committed.get_flattened_data()) != list(fresh.get_flattened_data()):
                drift.append(f"{name}: differs from a fresh render")
            continue

        path.parent.mkdir(parents=True, exist_ok=True)
        fresh.save(path)
        print(f"wrote {path} ({fresh.width}x{fresh.height})")

    if args.check:
        if drift:
            for line in drift:
                print(f"gen-alchemy-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-alchemy-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
