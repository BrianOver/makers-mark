"""Author the three rival-shelf CATEGORY icons: item-rival-weapon/-shield/-armor.

U7 (docs/plans/2026-08-08-001-feat-proof-the-player-never-sees-plan.md): ShopPanel.BuildRivalSection
renders every RivalCatalog entry through the generic slot placeholder (UiKit.ArtRect's no-manifest-
hit fallback box) because AssetCatalog.ItemIconId(item.RecipeId) composes "item-rival-blade-1" etc,
and no such id has ever had committed art. Rival pricing is a core comparison and one of the four
starting buildings, so a player hits blank placeholder cards immediately and repeatedly (playtest
findings 2026-07-19 SS8, LayoutTests.ShopBody_RivalShelfLongItemNames_WrapAtWordBoundaries_
NotMidWord's own doc quotes the same fallback).

WHY CATEGORY ICONS, NOT ONE PER SYNTHETIC ID
---------------------------------------------
The plan's own instruction: "prefer a small icon set covering the catalogue's categories over one
icon per synthetic id -- the rival catalogue is generated, so per-id art would rot the moment the
generator changes." RivalCatalog.Entries carries exactly one line per (ItemSlot, Tier) pair today
(rival-blade-1/2, rival-shield-1/2, rival-armor-1/2 -- 6 ids), but RivalRestockSystem mints new
item INSTANCES from this table every restock, and a future tier-3 line or a fourth slot's entry
would need its own PNG under the old per-id scheme with nobody remembering to paint it -- exactly
the KnownPendingIds failure mode this same plan's Part 2 fix (art/pipeline/gen-engineering-
interior.py) already documents for the Workbench Hall stations. Binding art to ItemSlot instead
(IconRegistry.RivalCategoryArtId) means any future catalog entry resolves to real, already-painted
art the moment it exists, because every RivalCatalogEntry already carries a slot and there are
only three.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same discipline as art/pipeline/gen-engineering-interior.py and tools/art/gen_town_sprites.py:
every colour below is SAMPLED FROM COMMITTED town2d-* PIXELS, never picked by eye. Byte-
reproducible (`--check`), editable without a GPU or an image editor -- and deliberately NOT run
through the ComfyUI/SDXL chain the rest of godot/assets/art/item-*.png came from: these three
render into the same 56x56 ArtRect box (ShopPanel.ItemArtSize) an item-dagger.png etc already
does, but they are simple flat silhouettes, not a detailed weapon render, so hand-authored pixel
art at a modest canvas holds up exactly as well and avoids a GPU dependency for three icons.

DELIBERATELY MUTED, NOT PLAYER-VIVID
-------------------------------------
RivalCatalog's own doc: entries are "generic flat-quality," "all QualityGrade.Common," and "never
a maker's mark" (R5). The three icons below use ONLY the plain iron/bone structural tones (no
ARCANE, no COOLANT, no EMBER) -- a deliberate visual tell that rival goods carry no mark, so a
player's own vivid crafted item (real SDXL art, richer palette) reads as visibly different from
the rival stall's plain stock even before either card's stat chips are read.

PALETTE PROVENANCE -- the same structural family every town2d interior script already verified
against committed pixels (re-quoted verbatim, counts match gen-engineering-interior.py's own
re-sample):
    town2d-forge.png   (58, 42, 84, 255)   x1214 -> IRON
    town2d-forge.png   (38, 27, 61, 255)   x566  -> IRON_DEEP
    town2d-tavern.png  (86, 55, 92, 255)   x637  -> IRON_LIT
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-tavern.png  (90, 54, 46, 255)   x281  -> WOOD

SIZES
-----
All three: 48x48 -- larger than a town2d interior prop (this renders into a UI panel box, not a
walkable-room tile), but still a clean pixel-art canvas rather than the ~200-400px SDXL renders
the profession item icons use; ArtRect's KeepAspectCentered/IgnoreSize scaling makes the source
canvas size a free choice.

Usage:
    python art/pipeline/gen-rival-icons.py [--check]

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
WOOD = (90, 54, 46, 255)

W, H = 48, 48


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


def holes(im: Image.Image) -> list[tuple[int, int]]:
    """Transparent pixels fully enclosed by opaque ones."""
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


def render_weapon() -> Image.Image:
    """48x48. A plain straight longsword -- BONE hilt/guard, IRON blade with an IRON_LIT edge,
    no gem, no rune: the "generic, unmarked" tell RivalCatalog's own doc calls for."""
    w, h = W, H
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 20, 38, 27, 44, WOOD, w, h)                # grip
    outline(px, 20, 38, 27, 44, w, h)
    rect(px, 14, 34, 33, 37, BONE, w, h)                # crossguard
    outline(px, 14, 34, 33, 37, w, h)

    rect(px, 21, 4, 26, 34, IRON, w, h)                 # blade
    rect(px, 21, 4, 23, 34, IRON_LIT, w, h)             # lit edge (upper-left convention)
    outline(px, 21, 4, 26, 34, w, h)
    px[23, 4] = BONE                                     # tip glint

    return im


def render_shield() -> Image.Image:
    """48x48. A plain kite shield -- WOOD face, IRON rim band, BONE boss. No crest, no paint: the
    "generic" tell, mirroring the weapon icon's own restraint. The point is built by painting
    progressively narrower rows (never by clearing an already-opaque pixel back to transparent),
    the same "paint the taper, don't punch it" discipline gen-forge-interior.py's bellows uses for
    its own concertina taper -- clearing corners out of a filled rect risks bracketing a gap that
    holes() flags as enclosed; painting only forward avoids the question entirely."""
    w, h = W, H
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 6, 4, 41, 5, IRON, w, h)                   # top rim band
    outline(px, 6, 4, 41, 5, w, h)

    rect(px, 10, 6, 37, 26, WOOD, w, h)                 # main rectangular body
    rect(px, 10, 6, 22, 26, IRON_LIT, w, h)             # lit left half
    outline(px, 10, 6, 37, 26, w, h)

    for i in range(14):                                  # taper into a point, row by row
        y = 27 + i
        x0, x1 = 10 + i, 37 - i
        if x0 >= x1:
            px[x0, y] = WOOD
            break
        rect(px, x0, y, x1, y, WOOD, w, h)
        px[x0, y] = IRON_DEEP
        px[x1, y] = IRON_DEEP

    rect(px, 20, 14, 27, 21, BONE, w, h)                # central boss
    outline(px, 20, 14, 27, 21, w, h)

    return im


def render_armor() -> Image.Image:
    """48x48. A plain riveted breastplate -- IRON body, IRON_LIT chest highlight, BONE rivets. No
    engraving: the same "generic, unmarked" restraint as the weapon/shield icons."""
    w, h = W, H
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 12, 6, 35, 12, IRON, w, h)                 # shoulders/collar band
    outline(px, 12, 6, 35, 12, w, h)

    rect(px, 8, 12, 39, 40, IRON, w, h)                 # torso plate
    rect(px, 8, 12, 22, 40, IRON_LIT, w, h)             # lit left half
    outline(px, 8, 12, 39, 40, w, h)

    rect(px, 22, 12, 25, 40, IRON_DEEP, w, h)           # centre seam
    for ry in (16, 24, 32):                              # rivets down each side of the seam
        px[19, ry] = BONE
        px[28, ry] = BONE

    return im


SPRITES = {
    "item-rival-weapon": render_weapon,
    "item-rival-shield": render_shield,
    "item-rival-armor": render_armor,
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
            print(f"gen-rival-icons.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-rival-icons.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-rival-icons.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
