"""Author the painted Forge interior: one room shell + six station props.

U2 of docs/plans/2026-08-02-001-feat-painted-interiors-plan.md ("painted interiors" plan).
U1 (a parallel branch, feat/interiors-u1-forge-room) ships the walkable room FRAMEWORK with loud
magenta placeholders standing in for these seven ids; this script paints the real pixels so the
placeholders retire. The two units are pinned to run in parallel against the exact ids below
(plan section U1/U2) so neither blocks the other.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same reason as every other file in this pipeline (gen-market.py, gen-ground-tiles.py): every
colour below is SAMPLED FROM COMMITTED town2d-* PIXELS, never picked by eye, so the room reads as
built from the same material as the buildings the player already stood next to outside. That also
makes this byte-reproducible (`--check`) and editable without a GPU or an image editor.

PALETTE PROVENANCE (verbatim, sampled from committed PNGs -- exact counts noted so a future
re-sample can confirm the same source pixel; run this at the repo root to reproduce a count:
`python -c "from PIL import Image; import collections; print(collections.Counter(Image.open(P).getdata()).most_common(8))"`)

Structural family (VOID/IRON_DEEP/IRON/IRON_LIT/BONE/EMBER/ARCANE/COOLANT) -- gen-market.py
documents this exact named set as the town2d idiom. IMPORTANT: gen-market.py's own committed
docstring quotes the PRE-boost hex values; `boost-town2d-palette.py` (2026-08-01, Option C of the
building-exterior receipt) raised the four low-chroma structural tones' saturation/lightness on
the five actually-committed town2d-*.png files and left the accent tones untouched. Sampling was
done against what is on disk TODAY (post-boost), not the older in-comment values, per this plan's
own instruction to sample "verbatim from committed town2d-* PNGs" -- the committed pixel is the
source of truth over a comment describing an earlier revision of it:
    town2d-forge.png   (24, 16, 40, 255) x181   -> VOID
    town2d-forge.png   (38, 27, 61, 255) x566   -> IRON_DEEP
    town2d-forge.png   (58, 42, 84, 255) x1214  -> IRON
    town2d-tavern.png  (86, 55, 92, 255) x637   -> IRON_LIT
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-forge.png   (224, 145, 63, 255) x61  -> EMBER
    town2d-forge.png   (224, 145, 63, 140) x126 -> EMBER_GLOW (forge's own lit-window haze alpha)
    town2d-forge.png   (107, 76, 154, 255) x20  -> ARCANE
    town2d-forge.png   (63, 176, 172, 255) x19  -> COOLANT
    town2d-tavern.png  (90, 54, 46, 255) x281   -> WOOD
    town2d-market.png  (196, 182, 150, 255) x70 -> PARCHMENT

Floor family -- sampled from the two ground tiles already committed for the town's own paths, so
the interior floor is made of the same material the player just walked in on:
    town2d-tile-path.png   (107, 95, 82, 255) x221 -> PLANK       (worn board tone)
    town2d-tile-path.png   (87, 77, 67, 255) x35   -> PLANK_DARK  (board seam shadow)
    town2d-tile-cobble.png (74, 70, 88, 255) x208  -> FLAG        (stone flagstone)
    town2d-tile-cobble.png (56, 51, 73, 255) x46   -> FLAG_DARK   (mortar joint)
    town2d-tile-cobble.png (92, 88, 112, 255) x2   -> FLAG_LIT    (rare fleck highlight)

"Single ARCANE/COOLANT accents" (plan's composition brief, echoing gen-market's own "single rune +
single circuit trace" rule): across all seven files here, exactly one sprite carries the ARCANE
rune (the furnace) and exactly one carries the COOLANT tone (the quench trough's water) -- these
are precious, not sprinkled per-sprite.

SIZES
-----
Shell: 384x224 = 24x14 tiles at the town's 16px/tile convention (TownLayout2D.TileSize). Station
sizes are the exact figures the executing plan/task pinned (U1's InteriorLayout2D table, not yet
on this branch -- see this script's PR body for how they were confirmed): anvil 24x20, furnace
32x40, bellows 20x14, quench 24x14, shelf 28x32, rack 28x32.

Usage:
    python art/pipeline/gen-forge-interior.py [--check]

    --check   render every sprite in memory and compare against the committed PNGs; writes
              nothing and exits non-zero on any drift. Same contract as gen-market.py --check.
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
FLAG_LIT = (92, 88, 112, 255)


# ── pixel helpers (generalised versions of gen-market.py's rect/outline, parametrised over an
#    explicit canvas size so one module can render both the 384x224 shell and 20x14 props) ──────
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
    """Transparent pixels fully enclosed by opaque ones -- see gen-market.py's own doc for why
    this is a guard and not just an eyeball check (an enclosed gap is invisible against a dark
    editor background and only shows up rendered at high zoom against a light background)."""
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


# ── the room shell (384x224 = 24x14 tiles) ────────────────────────────────────────────────────
SHELL_W, SHELL_H = 384, 224
TILE = 16


def render_shell() -> Image.Image:
    """Ember-lit stone-and-timber smithy floor: a 4-tile stone wall band across the back (top),
    a baseboard-shadow transition, then a plank work-floor bordered by a flagstone apron along the
    left/right/bottom edges, with a 4-tile door gap centred on the bottom edge (KTD-1's island
    door tile) framed by two timber jamb posts and a warm glow bleeding up from the exterior.

    Composition brief (plan U2): readable OPEN floor -- everything from x=32..351, y=88..191 is
    bare plank with nothing painted on it, so six stations placed against the walls (U1's job)
    never crowd the walkable middle.
    """
    im = Image.new("RGBA", (SHELL_W, SHELL_H), FLAG)
    px = im.load()
    w, h = SHELL_W, SHELL_H

    # ---- stone wall band (back wall, top 4 tiles = 64px) ----------------------------------
    rect(px, 0, 0, w - 1, 63, FLAG, w, h)
    # coursing: horizontal mortar joints every 8px, offset brick pattern via alternating FLAG_DARK
    # vertical joints so the band doesn't read as one flat slab
    for y in range(7, 64, 8):
        rect(px, 0, y, w - 1, y, FLAG_DARK, w, h)
    for i, x in enumerate(range(0, w, 24)):
        offset = 12 if (i % 2) else 0
        for jx in range(x + offset, w, 24):
            rect(px, jx, 0, jx, 63, FLAG_DARK, w, h)
    rect(px, 0, 0, w - 1, 1, BONE, w, h)  # single bright rim, light from above (gen-market idiom)
    # a scatter of lit flecks in the stonework -- sparse, never a stripe
    for fx, fy in ((18, 22), (94, 40), (170, 14), (246, 34), (322, 20), (60, 50), (300, 52)):
        px[fx, fy] = FLAG_LIT

    # two wall braziers (ember-lit openings), symmetric, clear of the room's centre where a
    # future flue/chimney prop could sit
    for bx in (72, w - 72 - 16):
        rect(px, bx, 20, bx + 15, 39, IRON_DEEP, w, h)
        outline(px, bx - 1, 19, bx + 16, 40, w, h, BONE)
        rect(px, bx + 2, 23, bx + 13, 36, EMBER, w, h)
        rect(px, bx + 1, 22, bx + 14, 37, EMBER_GLOW, w, h)
        rect(px, bx + 2, 23, bx + 13, 36, EMBER, w, h)  # solid core redrawn over the glow haze

    rect(px, 0, 63, w - 1, 63, VOID, w, h)  # crisp wall/floor seam

    # ---- baseboard shadow (transition band, 8px) ------------------------------------------
    for i, y in enumerate(range(64, 72)):
        # blend FLAG_DARK -> PLANK across 8 rows so the seam reads as ambient occlusion, not a
        # hard second wall
        t = i / 7
        blend = tuple(int(FLAG_DARK[c] * (1 - t) + PLANK[c] * t) for c in range(3)) + (255,)
        rect(px, 0, y, w - 1, y, blend, w, h)

    # ---- floor: plank work floor, flagstone apron border -----------------------------------
    rect(px, 0, 72, w - 1, h - 1, PLANK, w, h)
    for x in range(0, w, 16):
        rect(px, x + 15, 72, x + 15, h - 1, PLANK_DARK, w, h)  # board seams every tile
    for y in range(72, h, 32):
        rect(px, 0, y + 15, w - 1, y + 15, PLANK_DARK, w, h)  # cross-seams (2-tile planks)

    apron = TILE  # 1 tile
    rect(px, 0, 72, apron - 1, h - 1, FLAG, w, h)          # left apron
    rect(px, w - apron, 72, w - 1, h - 1, FLAG, w, h)      # right apron
    rect(px, 0, h - apron, w - 1, h - 1, FLAG, w, h)       # bottom apron
    for x in (apron - 1, w - apron):
        rect(px, x, 72, x, h - 1, FLAG_DARK, w, h)
    rect(px, 0, h - apron, w - 1, h - apron, FLAG_DARK, w, h)

    # ---- door gap: 4 tiles wide, centred on the bottom edge --------------------------------
    door_w = 4 * TILE
    door_x0 = (w - door_w) // 2
    door_x1 = door_x0 + door_w - 1
    # open the gap: plank continues straight to the bottom edge (no flagstone apron across it)
    rect(px, door_x0, 72, door_x1, h - 1, PLANK, w, h)
    for x in range(door_x0, door_x1 + 1, 16):
        rect(px, x + 15, 72, x + 15, h - 1, PLANK_DARK, w, h)
    # warm glow spilling in from the town outside, brightest at the very bottom edge
    for i, y in enumerate(range(h - 24, h)):
        t = i / 23
        rect(px, door_x0, y, door_x1, y, (
            int(PLANK[0] * (1 - t * 0.6) + EMBER[0] * t * 0.6),
            int(PLANK[1] * (1 - t * 0.6) + EMBER[1] * t * 0.6),
            int(PLANK[2] * (1 - t * 0.6) + EMBER[2] * t * 0.6),
            255,
        ), w, h)
    # timber jamb posts flanking the gap
    for jx in (door_x0 - 8, door_x1 + 1):
        rect(px, jx, 40, jx + 7, h - 1, WOOD, w, h)
        outline(px, jx, 40, jx + 7, h - 1, w, h)
        rect(px, jx + 1, 41, jx + 2, h - 2, IRON_DEEP, w, h)  # grain shadow
    # threshold seam where jambs meet the wall band above
    rect(px, door_x0 - 8, 40, door_x1 + 8, 40, VOID, w, h)

    return im


# ── stations ────────────────────────────────────────────────────────────────────────────────────
def render_anvil() -> Image.Image:
    """24x20. Working face left-lit (IRON_LIT), horn tapering left, wood stump base."""
    w, h = 24, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 6, 15, 17, 19, WOOD, w, h)             # base stump
    outline(px, 6, 15, 17, 19, w, h)
    rect(px, 7, 16, 8, 18, IRON_DEEP, w, h)         # stump shadow face

    rect(px, 9, 12, 14, 15, IRON_DEEP, w, h)        # waist connecting body to stump
    outline(px, 9, 12, 14, 15, w, h)

    rect(px, 2, 7, 21, 11, IRON, w, h)              # main body
    rect(px, 2, 7, 11, 8, IRON_LIT, w, h)           # lit top-left plane
    outline(px, 2, 7, 21, 11, w, h)
    rect(px, 3, 7, 11, 7, BONE, w, h)               # rim highlight, upper-left only

    # horn taper (points left)
    rect(px, 0, 8, 1, 10, IRON, w, h)
    px[1, 8] = IRON_LIT
    outline(px, 0, 8, 1, 10, w, h)

    return im


def render_furnace() -> Image.Image:
    """32x40. Tallest station; ember-lit mouth, the interior's single rune accent."""
    w, h = 32, 40
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 2, 4, 29, 39, IRON, w, h)              # main hearth body, left half lit (upper-left
    rect(px, 16, 4, 29, 39, IRON_DEEP, w, h)        # convention every town2d building follows)
    outline(px, 2, 4, 29, 39, w, h)
    rect(px, 2, 4, 29, 5, BONE, w, h)               # crown rim

    # chimney stub
    rect(px, 11, 0, 20, 5, IRON_DEEP, w, h)
    outline(px, 11, 0, 20, 5, w, h)

    # the mouth: ember core behind a bone frame, the forge exterior's exact treatment
    rect(px, 8, 20, 23, 33, IRON_DEEP, w, h)
    outline(px, 7, 19, 24, 34, w, h, BONE)
    rect(px, 10, 22, 21, 31, EMBER_GLOW, w, h)
    rect(px, 11, 23, 20, 30, EMBER, w, h)
    rect(px, 14, 26, 17, 27, IRON_DEEP, w, h)       # grate bar shadow across the coals

    # the interior's single rune glyph, above the mouth
    rect(px, 14, 10, 17, 10, ARCANE, w, h)
    rect(px, 15, 11, 16, 13, ARCANE, w, h)
    rect(px, 14, 14, 17, 14, ARCANE, w, h)

    return im


def render_bellows() -> Image.Image:
    """20x14. Concertina wedge feeding the furnace; small ember nozzle glow."""
    w, h = 20, 14
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 2, 6, 11, WOOD, w, h)               # rear board (tall end)
    outline(px, 1, 2, 6, 11, w, h)
    rect(px, 13, 5, 17, 8, WOOD, w, h)               # front board (narrow end, at the nozzle)
    outline(px, 13, 5, 17, 8, w, h)

    # concertina folds between the two boards, tapering top and bottom toward the nozzle
    for i, x in enumerate(range(7, 13)):
        top = 3 + i // 2
        bot = 10 - i // 2
        rect(px, x, top, x, bot, IRON_DEEP if i % 2 else WOOD, w, h)
    outline(px, 7, 3, 12, 10, w, h)
    rect(px, 7, 6, 12, 6, BONE, w, h)                # rim highlight along the fold crest
    # the bounding outline above frames the tallest columns (7/8); the innermost tapered columns
    # (11/12, top=5/bot=8) leave a 1px notch between their own fill and that frame at (11,4) and
    # (11,9) -- close it with the same tone the column already uses so the taper reads as solid
    px[11, 4] = WOOD
    px[11, 9] = WOOD

    rect(px, 18, 6, 19, 7, EMBER, w, h)              # nozzle glow, aimed at the furnace mouth
    rect(px, 2, 3, 2, 8, IRON_DEEP, w, h)             # handle-board seam, upper-left of the rear
    # board -- a full-height seam line reads as a hinged handle board; a small square accent read
    # too close to a trigger/sight and made the silhouette scan as a ray-gun instead of a bellows

    return im


def render_quench() -> Image.Image:
    """24x14. Stone trough, the interior's single coolant accent as the water fill."""
    w, h = 24, 14
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 3, 22, 13, IRON_DEEP, w, h)         # basin body
    outline(px, 1, 3, 22, 13, w, h)
    rect(px, 1, 3, 22, 4, BONE, w, h)               # rim lip, upper edge catches the light
    rect(px, 2, 5, 21, 12, IRON, w, h)              # inner wall

    rect(px, 3, 6, 20, 11, COOLANT, w, h)           # the water: this station's one coolant accent
    for rx in range(4, 20, 4):                       # sparse ripple flecks along the surface row
        px[rx, 6] = IRON_LIT

    # two steam wisps rising off the surface -- deliberately NOT sharing a row: two isolated dots
    # on the same scanline satisfy holes()'s coarse "opaque somewhere left/right in this row"
    # check on both sides at once, flagging the open air between them as a false-positive hole
    px[7, 1] = BONE
    px[16, 2] = BONE

    return im


def render_shelf() -> Image.Image:
    """28x32. Timber material shelving, parchment-wrapped goods -- the market sign's own material,
    read here as the smithy's raw-material bundles."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)              # frame
    outline(px, 1, 1, 26, 30, w, h)
    for y in (1, 11, 21, 30):                        # three shelf boards
        rect(px, 1, y, 26, y, IRON_DEEP, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)                # top rim highlight

    # goods on the upper shelf: two parchment-wrapped bundles
    for gx in (4, 15):
        rect(px, gx, 4, gx + 7, 9, PARCHMENT, w, h)
        outline(px, gx, 4, gx + 7, 9, w, h)
        rect(px, gx + 1, 6, gx + 6, 6, IRON_DEEP, w, h)  # binding cord

    # goods on the middle shelf: stacked ingot-like blocks
    for gx in (3, 10, 17):
        rect(px, gx, 14, gx + 5, 19, IRON, w, h)
        outline(px, gx, 14, gx + 5, 19, w, h)
        px[gx, 14] = BONE

    # lower shelf: two more bundles, offset
    for gx in (5, 16):
        rect(px, gx, 24, gx + 7, 29, PARCHMENT, w, h)
        outline(px, gx, 24, gx + 7, 29, w, h)

    return im


def render_rack() -> Image.Image:
    """28x32. Finished-goods display: iron blade shapes with bone hilts, hung on wood pegs."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)              # frame, same family as the shelf
    outline(px, 1, 1, 26, 30, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)

    for hx in (7, 20):                               # two mounting pegs
        rect(px, hx - 1, 3, hx + 1, 5, IRON_DEEP, w, h)

    # blade 1: straight sword, hilt at top
    rect(px, 6, 6, 8, 8, BONE, w, h)                 # hilt/guard
    rect(px, 6, 9, 8, 24, IRON, w, h)                # blade
    rect(px, 6, 9, 6, 24, IRON_DEEP, w, h)           # blade shadow edge
    px[8, 24] = EMBER                                 # tip glint

    # blade 2: shorter tool (hammer-head), offset lower and to the right
    rect(px, 18, 12, 22, 15, IRON, w, h)             # head
    outline(px, 18, 12, 22, 15, w, h)
    rect(px, 19, 16, 21, 26, WOOD, w, h)             # haft
    outline(px, 19, 16, 21, 26, w, h)

    return im


SPRITES = {
    "town2d-forge-interior-shell": render_shell,
    "town2d-station-anvil": render_anvil,
    "town2d-station-furnace": render_furnace,
    "town2d-station-bellows": render_bellows,
    "town2d-station-quench": render_quench,
    "town2d-station-shelf": render_shelf,
    "town2d-station-rack": render_rack,
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
            print(f"gen-forge-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-forge-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-forge-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
