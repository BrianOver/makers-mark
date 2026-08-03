"""Author the painted Tavern interior: one room shell + four station props.

U3 of docs/plans/2026-08-02-004-feat-world-and-interiors-plan.md ("world and interiors" plan).
U1 (PR #356, feat/world-u1-three-rooms) shipped the walkable room FRAMEWORK with loud magenta
placeholders standing in for the five ids below (RoomSpec "tavern" in InteriorLayout2D.cs); this
script paints the real pixels so those placeholders retire. Ids and tile positions are PINNED by
that framework unit and are used here VERBATIM -- this script does not invent or rename anything.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same reason as gen-forge-interior.py (the idiom this script follows exactly): every colour below
is SAMPLED FROM COMMITTED town2d-* PIXELS, never picked by eye, so the room reads as built from
the same material as the buildings the player already stood next to outside. Byte-reproducible
(`--check`), editable without a GPU or an image editor.

PALETTE PROVENANCE (independently re-sampled from committed PNGs TODAY -- not copied from
gen-forge-interior.py's own docstring, per this plan's own warning that a script's documented
hexes were once found stale. Reproduce with:
`python -c "from PIL import Image; import collections; print(collections.Counter(Image.open(P).get_flattened_data()).most_common(8))"`)

    town2d-forge.png   (24, 16, 40, 255) x181   -> VOID
    town2d-forge.png   (38, 27, 61, 255) x566   -> IRON_DEEP
    town2d-forge.png   (58, 42, 84, 255) x1214  -> IRON
    town2d-tavern.png  (86, 55, 92, 255) x637   -> IRON_LIT
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-forge.png   (224, 145, 63, 255) x61  -> EMBER
    town2d-forge.png   (224, 145, 63, 140) x126 -> EMBER_GLOW
    town2d-tavern.png  (90, 54, 46, 255) x281   -> WOOD
    town2d-market.png  (196, 182, 150, 255) x70 -> PARCHMENT
    town2d-tile-cobble.png (74, 70, 88, 255) x208  -> FLAG_DARK-adjacent (see note)
    town2d-tile-cobble.png (56, 51, 73, 255) x46   -> FLAG_DARK

These match gen-forge-interior.py's own already-current (post-boost) values exactly -- confirmed
by re-sampling the committed PNGs directly rather than trusting either script's comment, so no
further palette drift has happened since that script landed.

COMPOSITION BRIEF (plan text, U3): "hearth-lit, tables with clear seat tiles (U6 will seat patrons
there), a bar along one wall, and a story wall with pinned scraps. Warm-dark palette -- the tavern
reads dimmer than the market; the hearth carries the light." Concretely:
  - The back wall is dark half-timbered wood (IRON_DEEP base, WOOD beams) rather than forge's lit
    stone brazier wall -- no baked-in wall braziers here, so the freestanding hearth station is
    the room's ONE dominant light source, not one of three.
  - The floor uses WOOD (~64 luminance) as its board tone instead of forge's brighter PLANK
    (~97 luminance) -- a deliberately darker floor is most of this room's "dimmer" story, since
    floor is the majority of the canvas by area.
  - The flagstone apron uses the DARKER FLAG_DARK tone (not FLAG) for the same reason.
  - render_table() paints ONLY the bare table -- no stools/benches baked into the sprite. The
    tiles around table-a/table-b are U6's future seating anchors; pre-painting a seated
    silhouette there would double up with a patron sprite that unit adds later.

SIZES
-----
Shell: 352x208 = 22x13 tiles at the town's 16px/tile convention (TownLayout2D.TileSize) -- the
plan's own pinned figure, matching InteriorLayout2D.TavernRoomSizeTiles exactly. Station sizes are
chosen here (InteriorLayout2D.StationSpec has no separate declared-size field -- Building2D.Configure
derives collision straight from each PNG's own pixel size) and pinned by this script + the
AssetResolutionCensusTests.ForgeStationArt_MatchesItsKtd5DeclaredFootprint_NeverShiftsCollision
size-parity table so a future re-paint can never silently grow a sprite into its neighbour's tile:
    hearth 40x40 -> corrected to 40x32 (see render_hearth), bar 48x28, storywall 32x36,
    table 28x24 (shared by table-a and table-b).
Every size was checked against InteriorLayout2D's pinned Tile anchors (TownLayout2D.TileToWorld
centers each tile, i.e. tile*16+8) so no station's painted footprint reaches into a neighbouring
station's tile or outside the room's own perimeter wall.

Usage:
    python art/pipeline/gen-tavern-interior.py [--check]

    --check   render every sprite in memory and compare against the committed PNGs; writes
              nothing and exits non-zero on any drift. Same contract as gen-forge-interior.py.
"""
from __future__ import annotations

import argparse
import pathlib
import sys

from PIL import Image

OUT_DIR = pathlib.Path("godot/assets/art")

# ── palette (see provenance block above -- independently re-sampled, not copy-pasted) ─────────
CLEAR = (0, 0, 0, 0)
VOID = (24, 16, 40, 255)
IRON_DEEP = (38, 27, 61, 255)
IRON = (58, 42, 84, 255)
IRON_LIT = (86, 55, 92, 255)
BONE = (216, 207, 224, 255)
EMBER = (224, 145, 63, 255)
EMBER_GLOW = (224, 145, 63, 140)
WOOD = (90, 54, 46, 255)
PARCHMENT = (196, 182, 150, 255)
FLAG_DARK = (56, 51, 73, 255)


# ── pixel helpers (verbatim from gen-forge-interior.py -- same idiom, same contract) ───────────
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


# ── the room shell (352x208 = 22x13 tiles) ─────────────────────────────────────────────────────
SHELL_W, SHELL_H = 352, 208
TILE = 16


def render_shell() -> Image.Image:
    """Hearth-lit, dark half-timbered tavern floor: a 3-tile wood-and-beam wall band across the
    back (top, no baked-in braziers -- the hearth station alone carries the light), a baseboard
    shadow transition, a dark plank work-floor bordered by a darker flagstone apron, with a
    4-tile door gap centred on the bottom edge (TavernDoorTile) framed by timber jamb posts and a
    warm glow bleeding up from the exterior -- same structural grammar as the forge shell, but
    every dominant tone swapped for the darker anchor in its own family (WOOD not PLANK, FLAG_DARK
    not FLAG) so the room reads measurably dimmer overall (see PR body luminance comparison).
    """
    im = Image.new("RGBA", (SHELL_W, SHELL_H), IRON_DEEP)
    px = im.load()
    w, h = SHELL_W, SHELL_H

    # ---- back wall: dark half-timbered wood, top 3 tiles = 48px --------------------------
    rect(px, 0, 0, w - 1, 47, IRON_DEEP, w, h)
    for x in range(4, w, 32):
        rect(px, x, 0, x + 3, 47, WOOD, w, h)
    rect(px, 0, 0, w - 1, 1, BONE, w, h)  # single bright rim, light from above
    # sparse nail-head/knot flecks in the timber -- texture, never a stripe
    for fx, fy in ((30, 20), (94, 30), (158, 14), (222, 26), (286, 18), (60, 40), (320, 34)):
        px[fx, fy] = BONE
    rect(px, 0, 47, w - 1, 47, VOID, w, h)  # crisp wall/baseboard seam

    # ---- baseboard shadow (transition band, 8px) ------------------------------------------
    for i, y in enumerate(range(48, 56)):
        t = i / 7
        blend = tuple(int(IRON_DEEP[c] * (1 - t) + WOOD[c] * t) for c in range(3)) + (255,)
        rect(px, 0, y, w - 1, y, blend, w, h)

    # ---- floor: dark wood work floor, dark flagstone apron border -------------------------
    rect(px, 0, 56, w - 1, h - 1, WOOD, w, h)
    for x in range(0, w, 16):
        rect(px, x + 15, 56, x + 15, h - 1, IRON_DEEP, w, h)  # board seams every tile
    for y in range(56, h, 32):
        rect(px, 0, y + 15, w - 1, y + 15, IRON_DEEP, w, h)  # cross-seams (2-tile planks)

    apron = TILE  # 1 tile
    rect(px, 0, 56, apron - 1, h - 1, FLAG_DARK, w, h)          # left apron
    rect(px, w - apron, 56, w - 1, h - 1, FLAG_DARK, w, h)      # right apron
    rect(px, 0, h - apron, w - 1, h - 1, FLAG_DARK, w, h)       # bottom apron
    for x in (apron - 1, w - apron):
        rect(px, x, 56, x, h - 1, VOID, w, h)
    rect(px, 0, h - apron, w - 1, h - apron, VOID, w, h)

    # ---- door gap: 4 tiles wide, centred on the bottom edge --------------------------------
    door_w = 4 * TILE
    door_x0 = (w - door_w) // 2
    door_x1 = door_x0 + door_w - 1
    rect(px, door_x0, 56, door_x1, h - 1, WOOD, w, h)
    for x in range(door_x0, door_x1 + 1, 16):
        rect(px, x + 15, 56, x + 15, h - 1, IRON_DEEP, w, h)
    # warm glow spilling in from the town outside, brightest at the very bottom edge
    for i, y in enumerate(range(h - 24, h)):
        t = i / 23
        rect(px, door_x0, y, door_x1, y, (
            int(WOOD[0] * (1 - t * 0.6) + EMBER[0] * t * 0.6),
            int(WOOD[1] * (1 - t * 0.6) + EMBER[1] * t * 0.6),
            int(WOOD[2] * (1 - t * 0.6) + EMBER[2] * t * 0.6),
            255,
        ), w, h)
    # timber jamb posts flanking the gap
    for jx in (door_x0 - 8, door_x1 + 1):
        rect(px, jx, 32, jx + 7, h - 1, WOOD, w, h)
        outline(px, jx, 32, jx + 7, h - 1, w, h)
        rect(px, jx + 1, 33, jx + 2, h - 2, IRON_DEEP, w, h)  # grain shadow
    rect(px, door_x0 - 8, 32, door_x1 + 8, 32, VOID, w, h)  # threshold seam

    return im


# ── stations ────────────────────────────────────────────────────────────────────────────────────
def render_hearth() -> Image.Image:
    """40x32. Chimney-breast hearth built into the back wall -- the room's ONE deliberate light
    source (composition brief: "the hearth carries the light"). Anchored at tile (11, 2); bottom
    edge lands at world y=40 (TileToWorld), so height <=40 keeps it flush with or inside the
    48px wall band above -- 32 leaves 8px of visible wall above the mantel."""
    w, h = 40, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 2, 0, 37, 31, IRON_DEEP, w, h)          # chimney-breast stonework
    outline(px, 2, 0, 37, 31, w, h, BONE)
    rect(px, 2, 0, 37, 1, BONE, w, h)                 # mantel rim catches the light
    rect(px, 2, 10, 37, 11, IRON_DEEP, w, h)          # mantel-shelf underside shadow (redundant
    # fill, kept for clarity of intent -- same tone as the body, harmless overpaint)

    rect(px, 8, 14, 31, 29, IRON, w, h)               # firebox recess
    outline(px, 7, 13, 32, 30, w, h, BONE)
    rect(px, 10, 17, 29, 27, EMBER_GLOW, w, h)
    rect(px, 11, 18, 28, 26, EMBER, w, h)              # the room's one dominant light source
    rect(px, 15, 21, 24, 22, IRON_DEEP, w, h)          # grate bar shadow across the coals

    return im


def render_bar() -> Image.Image:
    """48x28. A long serving counter along the west wall -- warm wood top, dark kick base, a row
    of mugs/bottles catching a highlight."""
    w, h = 48, 28
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 0, 10, 47, 27, WOOD, w, h)               # counter body
    outline(px, 0, 10, 47, 27, w, h)
    rect(px, 0, 8, 47, 9, BONE, w, h)                  # top lip highlight, catches the light
    rect(px, 1, 13, 46, 26, IRON_DEEP, w, h)           # kick-base recess

    for bx in (6, 20, 34):                             # mugs/bottles on the counter
        rect(px, bx, 3, bx + 3, 9, IRON_LIT, w, h)
        outline(px, bx, 3, bx + 3, 9, w, h)
        px[bx + 1, 3] = BONE

    return im


def render_storywall() -> Image.Image:
    """32x36. A standing corkboard, pinned with parchment scraps and legend clippings -- deliberately
    uneven, a scrapbook, not a filing cabinet."""
    w, h = 32, 36
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 30, 34, WOOD, w, h)                 # board + frame
    outline(px, 1, 1, 30, 34, w, h)
    rect(px, 2, 2, 29, 2, BONE, w, h)                   # top rim highlight

    rect(px, 4, 5, 15, 13, PARCHMENT, w, h)
    outline(px, 4, 5, 15, 13, w, h)
    rect(px, 17, 4, 28, 10, PARCHMENT, w, h)
    outline(px, 17, 4, 28, 10, w, h)
    rect(px, 5, 16, 14, 24, PARCHMENT, w, h)
    outline(px, 5, 16, 14, 24, w, h)
    rect(px, 17, 14, 27, 22, PARCHMENT, w, h)
    outline(px, 17, 14, 27, 22, w, h)
    rect(px, 7, 26, 20, 32, PARCHMENT, w, h)
    outline(px, 7, 26, 20, 32, w, h)

    # a single wax-seal accent scrap -- the one legend that got the ember treatment
    rect(px, 22, 24, 29, 31, PARCHMENT, w, h)
    outline(px, 22, 24, 29, 31, w, h)
    rect(px, 24, 26, 27, 29, EMBER, w, h)

    for dx, dy in ((9, 6), (22, 5), (9, 17), (21, 15), (13, 27), (25, 25)):
        px[dx, dy] = BONE                               # pin dots

    return im


def render_table() -> Image.Image:
    """28x24. Bare patron table, shared by table-a and table-b -- deliberately NO stools baked in:
    the tiles around it are U6's future seating anchors, and pre-painting a seated silhouette
    here would double up with a patron sprite that unit adds later."""
    w, h = 28, 24
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 2, 14, 25, 20, IRON_DEEP, w, h)           # table legs/shadow, seen from above
    rect(px, 1, 4, 26, 15, WOOD, w, h)                  # tabletop
    outline(px, 1, 4, 26, 15, w, h)
    rect(px, 2, 5, 25, 5, BONE, w, h)                   # tabletop rim highlight

    rect(px, 6, 8, 11, 12, PARCHMENT, w, h)             # a plate left on the table
    outline(px, 6, 8, 11, 12, w, h)
    rect(px, 17, 7, 20, 12, IRON_LIT, w, h)             # a mug left on the table
    outline(px, 17, 7, 20, 12, w, h)
    px[18, 7] = BONE

    return im


SPRITES = {
    "town2d-tavern-interior-shell": render_shell,
    "town2d-station-tavern-hearth": render_hearth,
    "town2d-station-tavern-bar": render_bar,
    "town2d-station-tavern-storywall": render_storywall,
    "town2d-station-tavern-table": render_table,
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
            print(f"gen-tavern-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-tavern-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-tavern-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
