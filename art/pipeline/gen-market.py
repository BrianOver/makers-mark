"""Author town2d-market.png — the one building missing from the town2d pixel-art set.

WHY THIS EXISTS
---------------
`TownLayout2D.Venues` names five venue sprites. Four of them have a clean pixel-art asset in the
`town2d-*` set (town2d-forge, town2d-tavern, town2d-board, town2d-mine-gate); the market does not.
That gap is the only thing blocking the town from drawing the pixel set instead of the older
SDXL-era buildings, which is why the Forge currently renders with a magenta roof (#520051) that
appears in no `town2d-*` asset. See the PT27 task for the full trace.

WHY GENERATED AND NOT PAINTED
-----------------------------
Same reason as `gen-ground-tiles.py`: every colour here is SAMPLED from the committed sibling
buildings rather than picked by eye. A colour chosen by eye would be "close", and close is what
makes a building look bolted on next to its neighbours. Generating also makes the asset diffable,
editable without a GPU, and byte-reproducible on any machine — and it gives us `--check` as a drift
guard, exactly like the ground atlas has.

WHY NOT SDXL
------------
`gen_town_sprites.py` already recorded the finding: "at 20x36 a diffusion render downscales to
mush". The market is 64x64. Confirmed again before writing this — every asset in this size class is
hand-authored for that reason.

SIZE
----
64x64 = the 4x4-tile footprint `TownLayout2D` reserves for the market (x24-28, y8-12). The sibling
forge is 64x80 for its stated 4x5 rect, so 16px/tile is the convention this follows.

Usage:
    python art/pipeline/gen-market.py [--check]

    --check   render in memory and compare against the committed PNG; writes nothing and exits
              non-zero on any difference. Use as a drift guard in CI or after a palette edit.
"""
from __future__ import annotations

import argparse
import pathlib
import sys

from PIL import Image

OUT = pathlib.Path("godot/assets/art/town2d-market.png")
W = H = 64

# ── palette ────────────────────────────────────────────────────────────────────────────────────
# Sampled verbatim from the committed town2d-forge / town2d-tavern / town2d-board PNGs. The counts
# in those files show the idiom: a dark iron body, Bone for linework only, Ember for lit openings,
# and a single Arcane + single Coolant accent. Six to eight colours per building, never more.
CLEAR = (0, 0, 0, 0)
VOID = (20, 15, 31, 255)        # outline — town2d-forge/tavern both use this and nothing darker
IRON_DEEP = (30, 25, 42, 255)   # shadowed planes
IRON = (42, 36, 56, 255)        # body (forge's dominant colour, 1214px)
IRON_LIT = (61, 50, 66, 255)    # tavern's body tone, used here as the sunlit plane
WOOD = (90, 54, 46, 255)        # tavern's timber
BONE = (216, 207, 224, 255)     # linework / rim
EMBER = (224, 145, 63, 255)     # lantern + lit window
PARCHMENT = (196, 182, 150, 255)  # town2d-board's sign face
ARCANE = (107, 76, 154, 255)    # the one rune glyph
COOLANT = (63, 176, 172, 255)   # the one faint circuit trace


def rect(px, x0, y0, x1, y1, c):
    """Inclusive filled rectangle, clipped to the canvas."""
    for y in range(max(0, y0), min(H - 1, y1) + 1):
        for x in range(max(0, x0), min(W - 1, x1) + 1):
            px[x, y] = c


def outline(px, x0, y0, x1, y1, c=VOID):
    """Inclusive 1px border — the crisp edge the style bible asks for."""
    for x in range(max(0, x0), min(W - 1, x1) + 1):
        if 0 <= y0 < H:
            px[x, y0] = c
        if 0 <= y1 < H:
            px[x, y1] = c
    for y in range(max(0, y0), min(H - 1, y1) + 1):
        if 0 <= x0 < W:
            px[x0, y] = c
        if 0 <= x1 < W:
            px[x1, y] = c


def render() -> Image.Image:
    im = Image.new("RGBA", (W, H), CLEAR)
    px = im.load()

    # ── hanging sign (top) ─────────────────────────────────────────────────────────────────────
    # Reads as "shop" before any other detail does, and reuses town2d-board's parchment so the
    # noticeboard and the market sign are visibly the same material.
    rect(px, 24, 2, 39, 11, PARCHMENT)
    outline(px, 24, 2, 39, 11)
    rect(px, 26, 4, 37, 5, IRON_DEEP)        # two ruled lines = illegible "text", correct at 16px
    rect(px, 26, 7, 34, 8, IRON_DEEP)
    rect(px, 31, 12, 32, 14, WOOD)           # the post it hangs from

    # ── roof lip ───────────────────────────────────────────────────────────────────────────────
    rect(px, 3, 15, 60, 19, IRON_DEEP)
    rect(px, 3, 15, 60, 15, BONE)            # single bright rim = the light direction (upper-left)
    outline(px, 3, 15, 60, 19)

    # ── striped awning ─────────────────────────────────────────────────────────────────────────
    # Alternating timber/iron stripes, then a scalloped lower edge. Stripe phase is fixed (not
    # random) so a regeneration is byte-identical.
    for x in range(4, 60):
        band = WOOD if ((x - 4) // 4) % 2 == 0 else IRON
        rect(px, x, 20, x, 27, band)
    for x in range(4, 60):
        # Scallop: every 4px cell dips 2px in the middle, so the hem reads as cloth not a slab.
        #
        # The hem must reach the facade with no transparent row between them. The first version
        # filled only down to 27+dip and put the dark edge at 28+dip, which left y=29 EMPTY on every
        # non-dipping column — a dashed line of holes straight through the sprite. It was invisible
        # against a dark editor background and only showed up rendered at 6x against white. Filling
        # to 28+dip and edging at 29+dip makes the hem meet the facade's top outline (y=30) on the
        # shallow columns and overlap it by one on the deep ones, which is correct either way
        # because the awning hangs in FRONT of the wall.
        dip = 2 if 1 <= (x - 4) % 4 <= 2 else 0
        rect(px, x, 28, x, 28 + dip, WOOD if ((x - 4) // 4) % 2 == 0 else IRON)
        px[x, 29 + dip] = VOID
    outline(px, 3, 20, 60, 27)

    # ── facade ─────────────────────────────────────────────────────────────────────────────────
    rect(px, 5, 31, 58, 57, IRON)
    rect(px, 5, 31, 30, 57, IRON_LIT)        # left half catches the light
    outline(px, 4, 30, 59, 58)

    # lit windows — Ember behind a Bone frame, the sibling forge's exact treatment
    for wx in (9, 45):
        rect(px, wx, 34, wx + 9, 43, EMBER)
        outline(px, wx - 1, 33, wx + 10, 44, BONE)
        rect(px, wx + 4, 34, wx + 5, 43, IRON_DEEP)   # mullion
        rect(px, wx, 38, wx + 9, 39, IRON_DEEP)       # transom

    # ── open counter (the thing that makes it a market and not a house) ────────────────────────
    rect(px, 24, 34, 39, 52, IRON_DEEP)
    outline(px, 23, 33, 40, 53, BONE)
    rect(px, 22, 46, 41, 48, WOOD)           # counter slab, overhanging both jambs
    outline(px, 22, 46, 41, 48)
    rect(px, 23, 45, 40, 45, COOLANT)        # the one circuit trace, under the slab's lip

    # goods on the counter: three crates in silhouette, Bone top edge only
    for gx in (25, 30, 35):
        rect(px, gx, 41, gx + 3, 45, WOOD)
        px[gx, 41] = BONE
        px[gx + 3, 41] = BONE

    # the one rune glyph, centred on the counter's back wall
    rect(px, 30, 36, 33, 36, ARCANE)
    rect(px, 31, 37, 32, 39, ARCANE)
    rect(px, 30, 40, 33, 40, ARCANE)

    # ── base plinth + stacked crates ───────────────────────────────────────────────────────────
    rect(px, 4, 58, 59, 61, IRON_DEEP)
    outline(px, 4, 58, 59, 61)
    rect(px, 5, 58, 30, 58, IRON)            # lit face of the step, matching the facade split

    for cx, cy in ((6, 48), (14, 52)):
        rect(px, cx, cy, cx + 7, cy + 7, WOOD)
        outline(px, cx, cy, cx + 7, cy + 7)
        rect(px, cx + 1, cy + 3, cx + 6, cy + 4, IRON_DEEP)   # slat shadow
        px[cx + 1, cy + 1] = BONE                             # rim light, upper-left only

    # ── lantern on the right post ──────────────────────────────────────────────────────────────
    rect(px, 54, 47, 57, 52, IRON_DEEP)
    outline(px, 54, 47, 57, 52)
    rect(px, 55, 48, 56, 51, EMBER)

    return im


def holes(im: Image.Image) -> list[tuple[int, int]]:
    """Transparent pixels fully enclosed by opaque ones — gaps you can see straight through.

    Worth a guard rather than an eyeball: the awning hem originally left an empty row across the
    whole sprite, which is invisible against a dark background and only showed up rendered at 6x
    against white. A hole is any clear pixel that has opaque neighbours both above and below AND
    both left and right, so the silhouette's outside is never counted.
    """
    px = im.load()
    found = []
    for y in range(1, H - 1):
        for x in range(1, W - 1):
            if px[x, y][3] > 8:
                continue
            up = any(px[x, yy][3] > 8 for yy in range(0, y))
            down = any(px[x, yy][3] > 8 for yy in range(y + 1, H))
            left = any(px[xx, y][3] > 8 for xx in range(0, x))
            right = any(px[xx, y][3] > 8 for xx in range(x + 1, W))
            if up and down and left and right:
                found.append((x, y))
    return found


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="compare against the committed PNG instead of writing")
    args = ap.parse_args()

    fresh = render()

    gaps = holes(fresh)
    if gaps:
        print(f"FAIL {len(gaps)} enclosed transparent pixel(s), first at {gaps[:6]}",
              file=sys.stderr)
        return 1

    if args.check:
        if not OUT.exists():
            print(f"FAIL {OUT} does not exist", file=sys.stderr)
            return 1
        committed = Image.open(OUT).convert("RGBA")
        if committed.size != fresh.size:
            print(f"FAIL size drift: committed {committed.size} vs fresh {fresh.size}",
                  file=sys.stderr)
            return 1
        if list(committed.get_flattened_data()) != list(fresh.get_flattened_data()):
            print(f"FAIL {OUT} differs from a fresh render", file=sys.stderr)
            return 1
        print(f"ok {OUT} matches a fresh render")
        return 0

    OUT.parent.mkdir(parents=True, exist_ok=True)
    fresh.save(OUT)
    print(f"wrote {OUT} ({W}x{H})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
