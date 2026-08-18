"""Author the painted Market interior: one room shell + four station props.

U2 of docs/plans/2026-08-02-004-feat-world-and-interiors-plan.md ("world and interiors" plan).
U1 (PR #356) ships the walkable room FRAMEWORK with loud magenta placeholders standing in for
these five ids, pinning every sprite id and tile position in InteriorLayout2D.cs so this script
can be authored in parallel against ids that never change. This script paints the real pixels so
the placeholders retire (the five ids leave AssetResolutionCensusTests.KnownPendingIds in the same
commit).

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same reason as every other file in this pipeline (gen-market.py, gen-forge-interior.py): every
colour below is SAMPLED FROM COMMITTED town2d-* PIXELS, never picked by eye, so the room reads as
built from the same material as the building the player just stood outside. That also makes this
byte-reproducible (`--check`) and editable without a GPU or an image editor.

PALETTE PROVENANCE (verbatim, sampled from committed PNGs -- exact counts noted so a future
re-sample can confirm the same source pixel; run this at the repo root to reproduce a count:
`python -c "from PIL import Image; import collections; print(collections.Counter(Image.open(P).getdata()).most_common(8))"`)

IMPORTANT: gen-market.py's own committed docstring quotes PRE-boost hex values (before
boost-town2d-palette.py, 2026-08-01, raised the four low-chroma structural tones' saturation/
lightness on the five committed town2d-*.png files). Sampling here was done against what is on
disk TODAY, the same discipline gen-forge-interior.py's own provenance block already documents,
re-verified independently against town2d-market.png/town2d-forge.png/town2d-tavern.png/
town2d-board.png rather than trusted from either script's comment:
    town2d-market.png (24, 16, 40, 255)   x640  -> VOID
    town2d-market.png (38, 27, 61, 255)   x584  -> IRON_DEEP
    town2d-market.png (58, 42, 84, 255)   x603  -> IRON
    town2d-market.png (90, 54, 46, 255)   x334  -> WOOD
    town2d-tavern.png (86, 55, 92, 255)   x637  -> IRON_LIT
    town2d-market.png (216, 207, 224, 255) x162 -> BONE
    town2d-market.png (224, 145, 63, 255) x136  -> EMBER
    town2d-forge.png  (224, 145, 63, 140) x126  -> EMBER_GLOW
    town2d-market.png (196, 182, 150, 255) x70  -> PARCHMENT
    town2d-market.png (107, 76, 154, 255)  x14  -> ARCANE
    town2d-forge.png  (63, 176, 172, 255)  x19  -> COOLANT (not in market's own top-10, but the
                                                    same committed accent gen-forge-interior.py
                                                    already verified; sampled from its own file)

Floor family -- identical to gen-forge-interior.py's own sampling (same two ground tiles the
town's paths already use, so the market's floor is made of the same material the player just
walked in on):
    town2d-tile-path.png   (107, 95, 82, 255) x221 -> PLANK       (worn board tone)
    town2d-tile-path.png   (87, 77, 67, 255) x35   -> PLANK_DARK  (board seam shadow)
    town2d-tile-cobble.png (74, 70, 88, 255) x208  -> FLAG        (stone flagstone)
    town2d-tile-cobble.png (56, 51, 73, 255) x46   -> FLAG_DARK   (mortar joint)

SIGNATURE ACCENT -- one ARCANE rune + one COOLANT trace, on the counter, deliberately echoing
town2d-market.png's OWN exterior counter (rune above, coolant trace under the slab's lip) so the
inside of the shop reads as the same counter the player saw painted on the front of the building --
not a coincidence of shared palette, an intentional callback.

SIZES
-----
Shell: 320x192 = 20x12 tiles at the town's 16px/tile convention (TownLayout2D.TileSize) -- pinned
by InteriorLayout2D.MarketRoomSizeTiles, not chosen here. Station sizes are authored fresh for this
unit (no prior plan text pinned them the way KTD-5 pinned the forge's) and pinned in this script's
own PR body / AssetResolutionCensusTests.ForgeStationArt_MatchesItsKtd5DeclaredFootprint_
NeverShiftsCollision, which this PR extends with market rows: counter 40x24, shelf 28x32,
ledger 24x20, crates 24x20.

Usage:
    python art/pipeline/gen-market-interior.py [--check]

    --check   render every sprite in memory and compare against the committed PNGs; writes
              nothing and exits non-zero on any drift. Same contract as gen-forge-interior.py
              --check.
"""
from __future__ import annotations

import argparse
import pathlib
import sys

from PIL import Image

OUT_DIR = pathlib.Path("godot/assets/art")

# ── palette (see provenance block above) ──────────────────────────────────────────────────────
CLEAR = (0, 0, 0, 0)
VOID = (24, 16, 40, 255)
IRON_DEEP = (38, 27, 61, 255)
IRON = (58, 42, 84, 255)
IRON_LIT = (86, 55, 92, 255)
BONE = (216, 207, 224, 255)
EMBER = (224, 145, 63, 255)
EMBER_GLOW = (224, 145, 63, 140)
ARCANE = (107, 76, 154, 255)
COOLANT = (63, 176, 172, 255)
WOOD = (90, 54, 46, 255)
PARCHMENT = (196, 182, 150, 255)

PLANK = (107, 95, 82, 255)
PLANK_DARK = (87, 77, 67, 255)
FLAG = (74, 70, 88, 255)
FLAG_DARK = (56, 51, 73, 255)


# ── pixel helpers (verbatim from gen-forge-interior.py -- same canvas-size-parametrised rect/
#    outline so one module can render both the 320x192 shell and the small props) ────────────────
def rect(px, x0, y0, x1, y1, c, w, h):
    """Inclusive filled rectangle, clipped to a w x h canvas."""
    for y in range(max(0, y0), min(h - 1, y1) + 1):
        for x in range(max(0, x0), min(w - 1, x1) + 1):
            px[x, y] = c


def outline(px, x0, y0, x1, y1, w, h, c=VOID):
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
    """Transparent pixels fully enclosed by opaque ones -- ported verbatim from
    gen-forge-interior.py (itself ported from gen-market.py): an enclosed gap is invisible against
    a dark editor background and only shows up rendered at high zoom against a light one, so this
    is a guard, not an eyeball check."""
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


# ── the room shell (320x192 = 20x12 tiles) ────────────────────────────────────────────────────
SHELL_W, SHELL_H = 320, 192
TILE = 16


# The room SHELL is no longer generated here. Register #146 replaced all four painted-plate
# backdrops with rendered art (PRs #587/#588): the six-to-eight-colour town2d idiom is correct
# for a 20x36 sprite and wrong for a room the camera fills, which is what made these shells
# measure 0.020-0.033 bytes/px against 1.688 for the forge exterior. This module keeps the
# STATION props -- they are sprite-scale and the idiom still holds for them. Leaving render_shell
# here would have been a loaded gun: running this script, or its own --check drift guard, would
# have silently reverted the shipped plate or reported drift against art that is deliberately
# no longer its output.

def render_counter() -> Image.Image:
    """40x24. Sales counter -- deliberately echoes town2d-market.png's own exterior counter
    (rune-stamped back panel, coolant trace under the slab's lip, crate goods on top) so the room
    the player just walked out of the front of is recognisably the same counter, seen from inside."""
    w, h = 40, 24
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    # back riser (the wall-mounted board behind the working surface)
    rect(px, 1, 0, 38, 5, IRON_DEEP, w, h)
    outline(px, 1, 0, 38, 5, w, h)
    # the one rune glyph, centred -- this station's single ARCANE accent
    rect(px, 18, 1, 21, 1, ARCANE, w, h)
    rect(px, 19, 2, 20, 3, ARCANE, w, h)
    rect(px, 18, 4, 21, 4, ARCANE, w, h)
    # three small crates on the back ledge, either side of the rune (goods on display)
    for gx in (4, 30):
        rect(px, gx, 1, gx + 5, 4, WOOD, w, h)
        px[gx, 1] = BONE
        px[gx + 5, 1] = BONE

    rect(px, 0, 6, 39, 6, BONE, w, h)                 # slab lip, upper rim highlight
    rect(px, 0, 7, 39, 7, COOLANT, w, h)              # the one coolant trace, under the slab's lip

    # slab front (working surface panel)
    rect(px, 1, 8, 38, 17, WOOD, w, h)
    outline(px, 1, 8, 38, 17, w, h)
    rect(px, 1, 8, 20, 9, IRON_LIT, w, h)             # upper-left lit plane (town2d convention)

    # kick / base, inset from the slab
    rect(px, 3, 18, 36, 23, IRON_DEEP, w, h)
    outline(px, 3, 18, 36, 23, w, h)

    return im


def render_shelf() -> Image.Image:
    """28x32. General-store display shelving -- baskets, jars, parchment bundles (deliberately a
    different silhouette from the forge's ingot-and-parchment shelf even though it shares the same
    wood-frame family, per the town's material idiom)."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)               # frame
    outline(px, 1, 1, 26, 30, w, h)
    for y in (1, 11, 21, 30):                         # three shelf boards
        rect(px, 1, y, 26, y, IRON_DEEP, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)                 # top rim highlight

    # top shelf: two round baskets
    for gx in (4, 16):
        rect(px, gx, 4, gx + 7, 9, WOOD, w, h)
        outline(px, gx, 4, gx + 7, 9, w, h)
        rect(px, gx + 1, 4, gx + 6, 4, BONE, w, h)    # basket rim highlight

    # middle shelf: three small jars
    for gx in (3, 11, 19):
        rect(px, gx, 14, gx + 4, 19, IRON_LIT, w, h)
        outline(px, gx, 14, gx + 4, 19, w, h)
        rect(px, gx + 1, 13, gx + 3, 13, BONE, w, h)  # jar lid

    # bottom shelf: two parchment-wrapped bundles
    for gx in (5, 16):
        rect(px, gx, 24, gx + 7, 29, PARCHMENT, w, h)
        outline(px, gx, 24, gx + 7, 29, w, h)
        rect(px, gx + 1, 26, gx + 6, 26, IRON_DEEP, w, h)  # binding cord

    return im


def render_ledger() -> Image.Image:
    """24x20. A standing ledger lectern -- honest flavor (no verb): an open parchment book on a
    slanted wood top, a thin quill line."""
    w, h = 24, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 9, 12, 14, 19, WOOD, w, h)               # base stand
    outline(px, 9, 12, 14, 19, w, h)
    rect(px, 6, 8, 17, 12, WOOD, w, h)                # slanted top plate
    outline(px, 6, 8, 17, 12, w, h)
    rect(px, 6, 8, 17, 8, BONE, w, h)                 # top rim highlight

    rect(px, 7, 4, 16, 9, PARCHMENT, w, h)             # the open book, propped on the plate
    outline(px, 7, 4, 16, 9, w, h)
    rect(px, 11, 4, 12, 9, IRON_DEEP, w, h)            # the book's centre spine/gutter
    rect(px, 8, 6, 10, 6, IRON_DEEP, w, h)             # ruled "text" lines, left page
    rect(px, 13, 6, 15, 6, IRON_DEEP, w, h)            # ruled "text" lines, right page

    rect(px, 17, 3, 19, 3, BONE, w, h)                 # quill, resting across the book's edge
    px[19, 2] = BONE
    px[20, 1] = BONE

    return im


def render_crates() -> Image.Image:
    """24x20. Stock crates -- honest flavor (no verb): a small stacked pile of unopened stock."""
    w, h = 24, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    # rear (taller) crate, left
    rect(px, 1, 3, 11, 19, WOOD, w, h)
    outline(px, 1, 3, 11, 19, w, h)
    rect(px, 2, 6, 10, 7, IRON_DEEP, w, h)             # slat shadow band
    rect(px, 2, 13, 10, 14, IRON_DEEP, w, h)
    px[1, 3] = BONE                                     # rim light, upper-left only

    # front (shorter) crate, right, overlapping the rear one slightly
    rect(px, 10, 9, 22, 19, WOOD, w, h)
    outline(px, 10, 9, 22, 19, w, h)
    rect(px, 11, 12, 21, 13, IRON_DEEP, w, h)
    px[10, 9] = BONE

    return im


SPRITES = {
    "town2d-station-market-counter": render_counter,
    "town2d-station-market-shelf": render_shelf,
    "town2d-station-market-ledger": render_ledger,
    "town2d-station-market-crates": render_crates,
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
            print(f"gen-market-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-market-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-market-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
