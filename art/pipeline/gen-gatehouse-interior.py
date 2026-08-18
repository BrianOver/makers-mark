"""Author the painted Gatehouse interior: one room shell + four station props.

U4 of docs/plans/2026-08-02-004-feat-world-and-interiors-plan.md ("world and interiors" plan).
U1 (feat/world-u1-three-rooms, PR #356) shipped the walkable room FRAMEWORK with loud magenta
placeholders standing in for these five ids and PINNED every sprite id + tile position in
InteriorLayout2D.cs's "minegate" row; this script paints the real pixels so the placeholders
retire. U2 (market) and U3 (tavern) are the sibling art units painting the other two new rooms in
parallel against the same U1 table -- disjoint files, same idiom.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same reason as gen-forge-interior.py (the idiom this script follows exactly) and every other file
in this pipeline: every colour below is SAMPLED FROM COMMITTED town2d-* PIXELS, never picked by
eye, so the gatehouse reads as built from the same material as the buildings the player already
stood next to outside. Byte-reproducible (`--check`), editable without a GPU or an image editor.

PALETTE PROVENANCE (verbatim, sampled from committed PNGs -- exact counts noted so a future
re-sample can confirm the same source pixel; run this at the repo root to reproduce a count:
`python -c "from PIL import Image; import collections; print(collections.Counter(Image.open(P).getdata()).most_common(10))"`)

Structural family (VOID/IRON_DEEP/IRON/IRON_LIT/BONE/EMBER/EMBER_GLOW) -- the same named set
gen-forge-interior.py documents, re-verified against what is on disk TODAY (2026-08-02) rather
than trusted from that script's own docstring -- gen-forge-interior.py's header explicitly warns
that a committed pixel is the source of truth over a comment describing an earlier revision of
it, and this script was built by re-running the sample, not by copying the other script's prose:
    town2d-forge.png   (58, 42, 84, 255) x1214 -> IRON
    town2d-forge.png   (38, 27, 61, 255) x566  -> IRON_DEEP
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-forge.png   (24, 16, 40, 255) x181  -> VOID
    town2d-forge.png   (224, 145, 63, 140) x126 -> EMBER_GLOW (forge's own lit-window haze alpha)
    town2d-forge.png   (224, 145, 63, 255) x61  -> EMBER
    town2d-tavern.png  (86, 55, 92, 255) x637   -> IRON_LIT
    town2d-tavern.png  (90, 54, 46, 255) x281   -> WOOD
    town2d-market.png  (196, 182, 150, 255) x70 -> PARCHMENT

Floor family -- the same two ground tiles gen-forge-interior.py samples, so this room's stone
floor is made of the exact material the player just walked in on:
    town2d-tile-cobble.png (74, 70, 88, 255) x208  -> FLAG       (this room's primary floor/wall
                                                                   material -- a gatehouse is
                                                                   ALL stone, unlike the forge's
                                                                   plank-floor smithy)
    town2d-tile-cobble.png (56, 51, 73, 255) x46   -> FLAG_DARK  (mortar joint)
    town2d-tile-cobble.png (92, 88, 112, 255) x2   -> FLAG_LIT   (rare fleck highlight)
    town2d-tile-path.png   (107, 95, 82, 255) x221 -> PLANK      (door-threshold boards only --
                                                                   a wood ramp across the stone
                                                                   sill, mirroring the forge's
                                                                   own door treatment)
    town2d-tile-path.png   (87, 77, 67, 255) x35   -> PLANK_DARK (board seam shadow)

MINE_DARK family -- sampled from `town2d-mine-backdrop.png`, the EXACT 160x160 backdrop
`MineWatch`/`DelveStage` render behind the animated delve (KTD-4). Composition brief for this
unit: "the overlook window on the north wall showing darkness below (it is the diegetic home of
the watch)" -- so the window's fill is not an invented dark colour, it is sampled from the same
image the player will later watch the delve happen against, tying the window's palette directly
to the thing it depicts:
    town2d-mine-backdrop.png (14, 13, 17, 255) x2173  -> MINE_DARK  (dominant depth tone)
    town2d-mine-backdrop.png (21, 15, 20, 255) x1344  -> MINE_DARK2 (secondary depth tone, used
                                                                      for the nearer band so the
                                                                      opening reads as a shaft,
                                                                      not a flat panel)
    town2d-mine-backdrop.png (120, 59, 58, 255) x1154 -> MINE_EMBER (a warm ember-red fleck --
                                                                      this room's one "precious"
                                                                      accent, a distant glow far
                                                                      below, echoing EMBER_GLOW's
                                                                      forge-window haze tone)

"Single precious accent" (gen-forge-interior.py's own "single ARCANE/COOLANT accents" rule,
re-applied here to the one thing this room's composition brief actually asks for): the ARCANE
rune and COOLANT water do not belong in a stone gatehouse -- this room's own precious accent is
MINE_EMBER, used ONLY inside the overlook window, sparingly (three flecks), never sprinkled
elsewhere. Everything else in the room is stone/iron/wood/bone.

SIZES
-----
Shell: 288x176 = 18x11 tiles at the town's 16px/tile convention (TownLayout2D.TileSize) -- pinned
by U1's InteriorLayout2D.GatehouseRoomSizeTiles. Station sizes are THIS unit's own authored choice
(unlike the forge set, U1's minegate row does not pre-declare station pixel dimensions -- only
tile position + sprite id): overlook 40x20, muster 28x40, bounty 26x36, winch 28x44.

Each height was chosen against TWO bottom-anchored constraints, not one (Building2D.Configure
anchors a sprite's BOTTOM edge on its tile position; Building2D.BuildLabel then stacks the
station's nametag ABOVE the sprite at world-space `-size.Y - 10`): the sprite itself must not
render above the room's own top edge (y=0), AND the nametag above the sprite must not either, or
the room's own camera clamp (limited to the room rect) crops it invisibly. `overlook` at tile
(9, 2) = y=32 is the tight case -- a label-safe height needs `32 - height - 10 + 8 >= 0`, i.e.
height <= 30 for the label's BOTTOM edge to clear zero (20 was chosen for real margin, not the
bare minimum); the other three stations sit at y=80/80/112, where even their generous heights
(40/36/44) clear both constraints with room to spare. This was caught by an actual receipt.ps1
capture at a first 40x32 draft (see render_overlook's own doc), not by eye -- exactly the reason
this repo's own tooling insists on judging art at play scale, in-room.

Usage:
    python art/pipeline/gen-gatehouse-interior.py [--check]

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
WOOD = (90, 54, 46, 255)
PARCHMENT = (196, 182, 150, 255)

FLAG = (74, 70, 88, 255)
FLAG_DARK = (56, 51, 73, 255)
FLAG_LIT = (92, 88, 112, 255)
PLANK = (107, 95, 82, 255)
PLANK_DARK = (87, 77, 67, 255)

MINE_DARK = (14, 13, 17, 255)
MINE_DARK2 = (21, 15, 20, 255)
MINE_EMBER = (120, 59, 58, 255)


# ── pixel helpers (identical to gen-forge-interior.py's own, so the two scripts stay
#    trivially comparable side by side) ───────────────────────────────────────────────────────
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
    """Transparent pixels fully enclosed by opaque ones -- see gen-forge-interior.py's own doc
    for why this is a guard and not just an eyeball check."""
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


# ── the room shell (288x176 = 18x11 tiles) ────────────────────────────────────────────────────
SHELL_W, SHELL_H = 288, 176
TILE = 16
WALL_BAND_H = 3 * TILE  # 48px -- the north (stone) wall band


# The room SHELL is no longer generated here. Register #146 replaced all four painted-plate
# backdrops with rendered art (PRs #587/#588): the six-to-eight-colour town2d idiom is correct
# for a 20x36 sprite and wrong for a room the camera fills, which is what made these shells
# measure 0.020-0.033 bytes/px against 1.688 for the forge exterior. This module keeps the
# STATION props -- they are sprite-scale and the idiom still holds for them. Leaving render_shell
# here would have been a loaded gun: running this script, or its own --check drift guard, would
# have silently reverted the shipped plate or reported drift against art that is deliberately
# no longer its output.

def render_overlook() -> Image.Image:
    """40x20 (NOT 40x32 -- see the size note below). The Overlook: a barred stone slit-window
    into the mine shaft -- the diegetic home of the watch (PR #355 re-hosts the animated delve
    renderer onto the Mirror; this station is the room's own promise that pressing it looks down
    into exactly that). Filled with MINE_DARK/MINE_DARK2 sampled from the actual MineWatch/
    DelveStage backdrop, barred (never open air -- reads as looking THROUGH something, not at a
    flat picture), with three sparse MINE_EMBER flecks standing in for distant torchlight below.

    Height is capped at 20, not the first-drafted 32: Building2D.Configure anchors a station's
    sprite by its BOTTOM edge on the tile position (U1 pins `overlook` at tile (9, 2), y=32), and
    stacks its nametag ABOVE the sprite at `-size.Y - 10` (Building2D.BuildLabel) -- world space,
    not screen space, so the room's own camera clamp (Town2D.EnterInterior, limited to the room
    rect) can crop it. A first 40x32 draft put the label's bottom edge at y=32-32-10+8=-2: entirely
    above the room's own top edge, silently unreadable in play (caught by an actual receipt.ps1
    capture, not by eye -- the exact failure mode `docs/solutions` warns "render the game and
    look" exists to catch). 20 tall keeps the label's bottom edge at y=32-20-10+8=10, safely
    inside the room."""
    w, h = 40, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, w - 2, h - 2, IRON_DEEP, w, h)   # stone frame
    outline(px, 1, 1, w - 2, h - 2, w, h, BONE)
    rect(px, 1, 1, w - 2, 2, BONE, w, h)             # top rim catches the light

    rect(px, 5, 5, w - 6, h - 6, MINE_DARK, w, h)    # the shaft opening -- receding darkness
    outline(px, 4, 4, w - 5, h - 5, w, h, IRON)
    rect(px, 5, h - 8, w - 6, h - 6, MINE_DARK2, w, h)  # nearer band, so it reads as a shaft
    # distant embers far below -- this room's one precious accent, never repeated elsewhere
    for ex, ey in ((14, h - 7), (21, h - 6), (28, h - 7)):
        px[ex, ey] = MINE_EMBER
    px[21, h - 5] = EMBER_GLOW  # faint haze beneath the brightest ember

    # iron bars -- the watch looks through them, never through open air
    for bx in (12, 20, 28):
        rect(px, bx, 5, bx, h - 6, IRON, w, h)
        px[bx, 5] = BONE  # bar-top rivet glint

    return im


def render_muster() -> Image.Image:
    """28x40. Muster Board: a wood notice board on an iron-shod post, a parchment call-to-arms
    pinned to it with a wax-seal glint -- the station that opens Depths."""
    w, h = 28, 40
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 11, 30, 16, h - 1, WOOD, w, h)          # floor-mounted post
    outline(px, 11, 30, 16, h - 1, w, h)
    rect(px, 12, 31, 13, h - 2, IRON_DEEP, w, h)      # post shadow face

    rect(px, 1, 2, w - 2, 29, WOOD, w, h)             # board frame
    outline(px, 1, 2, w - 2, 29, w, h)
    rect(px, 2, 3, w - 3, 3, BONE, w, h)               # top rim highlight

    rect(px, 4, 6, w - 5, 25, PARCHMENT, w, h)        # pinned notice
    outline(px, 4, 6, w - 5, 25, w, h)
    for ly in (10, 14, 18, 22):
        rect(px, 7, ly, w - 8, ly, IRON_DEEP, w, h)   # ink lines
    px[w - 9, 22] = EMBER                              # wax-seal glint
    for cx2, cy2 in ((5, 7), (w - 6, 7), (5, 24), (w - 6, 24)):
        px[cx2, cy2] = IRON                             # iron tacks pinning the corners

    return im


def render_bounty() -> Image.Image:
    """26x36. Bounty Ledger: a sloped reading desk with an open two-page ledger and a small
    iron lockbox for bounty coin -- the station that opens Bounties."""
    w, h = 26, 36
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 3, 20, w - 4, h - 1, WOOD, w, h)          # desk body
    outline(px, 3, 20, w - 4, h - 1, w, h)
    rect(px, 4, 21, 11, 22, IRON_DEEP, w, h)            # lower-left shadow face

    rect(px, 2, 12, w - 3, 20, WOOD, w, h)              # sloped reading surface
    outline(px, 2, 12, w - 3, 20, w, h)
    rect(px, 3, 13, 10, 13, BONE, w, h)                 # top rim highlight

    rect(px, 6, 10, w - 7, 17, PARCHMENT, w, h)         # open ledger
    outline(px, 6, 10, w - 7, 17, w, h)
    rect(px, 12, 10, 12, 17, IRON_DEEP, w, h)           # spine crease, two pages
    for ly in (12, 14, 16):
        rect(px, 7, ly, 11, ly, IRON, w, h)             # ink lines, left page
        rect(px, 13, ly, w - 8, ly, IRON, w, h)         # ink lines, right page

    rect(px, 3, 26, 9, h - 3, IRON, w, h)               # iron lockbox for bounty coin
    outline(px, 3, 26, 9, h - 3, w, h)
    rect(px, 4, 27, 8, 28, BONE, w, h)                  # lid rim highlight
    px[6, 30] = EMBER                                    # coin glint through the lock slot

    return im


def render_winch() -> Image.Image:
    """28x44. Gate Winch: the honest flavor station -- an iron drum on a timber A-frame, chain
    wound and hanging, a hook at its end. The room's "tackle" -- what actually raises the
    portcullis, per its own FlavorLine."""
    w, h = 28, 44
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    for lx in (2, w - 7):
        rect(px, lx, 10, lx + 4, h - 1, WOOD, w, h)     # A-frame legs
        outline(px, lx, 10, lx + 4, h - 1, w, h)
    rect(px, 2, 9, w - 3, 13, WOOD, w, h)                # crossbeam joining the legs
    outline(px, 2, 9, w - 3, 13, w, h)

    rect(px, 7, 14, 20, 27, IRON, w, h)                  # the drum itself
    outline(px, 7, 14, 20, 27, w, h)
    rect(px, 7, 14, 13, 20, IRON_LIT, w, h)              # lit upper-left plane
    rect(px, 8, 15, 19, 15, BONE, w, h)                  # rim highlight

    rect(px, 20, 17, w - 3, 19, IRON_DEEP, w, h)         # crank handle

    # Chain, wound then hanging: a SOLID strip (no transparent gap rows between links) banded by
    # alternating tone to read as links -- a transparent gap here would be fully bounded left/
    # right by the two A-frame legs, an enclosed hole by construction, not an open one (the same
    # lesson as the hook fix above, at the strip level instead of the endpoint).
    rect(px, 12, 28, 15, 40, IRON, w, h)
    for i, y in enumerate(range(28, 41, 2)):
        rect(px, 12, y, 15, y, IRON_DEEP, w, h)
    px[13, 30] = BONE                                     # link glint

    # Hook width matches the chain exactly (12..15) rather than the wider drum/legs -- a wider
    # hook would leave a 1px-wide vertical gap at x=11/16 fully bounded by leg-or-chain (left/
    # right) and drum-or-hook (up/down): an enclosed hole, not an open gap (holes() lesson from
    # gen-forge-interior.py's bellows notch, re-learned here the same way).
    rect(px, 12, h - 3, 15, h - 1, IRON, w, h)           # hoisting hook at the chain's end
    outline(px, 12, h - 3, 15, h - 1, w, h)

    return im


SPRITES = {
    "town2d-station-gate-overlook": render_overlook,
    "town2d-station-gate-muster": render_muster,
    "town2d-station-gate-bounty": render_bounty,
    "town2d-station-gate-winch": render_winch,
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
            print(f"gen-gatehouse-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-gatehouse-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-gatehouse-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
