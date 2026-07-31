"""Extend town2d-ground-atlas.png with two extra grass-detail tiles.

Stage 1b of the Maker's Mark art pipeline: hand-authored pixel tiles, generated rather than
painted so the palette is guaranteed to match the four tiles already in the atlas exactly
(sampled from them, never re-picked by eye) and so a regeneration is byte-reproducible.

The atlas is one row of 16x16 tiles read by Town2D.BuildTileSet:

    0 base | 1 blades | 2 flecks | 3 cobble | 4 clover | 5 pebbles

Deterministic: every placement comes from the same integer hash Town2D.GrassVariantFor uses,
never from `random`, so two runs on two machines emit identical bytes.

Usage:  python art/pipeline/gen-ground-tiles.py [--check]
        --check   do not write; exit non-zero if the committed atlas differs from a fresh render.
"""
import sys
import pathlib
from PIL import Image

TILE = 16
ATLAS = pathlib.Path("godot/assets/art/town2d-ground-atlas.png")

# Sampled verbatim from the committed atlas's own base/blade tiles — the whole reason this is a
# script and not a hand-painted PNG. A colour picked by eye would be "close", and close is what
# makes a tileset look patched together.
GRASS = [(68, 102, 52), (74, 110, 58), (78, 114, 60), (82, 120, 64)]
GRASS_LIT = (94, 132, 72)
GRASS_DARK = (58, 88, 46)
CLOVER = (108, 148, 84)
FLOWER = (214, 206, 132)   # pale straw — reads at 3x without shouting
STONE = (112, 106, 99)
STONE_DARK = (72, 68, 63)


def h(x: int, y: int, salt: int) -> int:
    """The spatial hash Town2D uses, so scatter here agrees with scatter there."""
    v = (x * 73856093) ^ (y * 19349663) ^ (salt * 83492791)
    return (v % 1000 + 1000) % 1000


def base_ground(img: Image.Image, ox: int, salt: int) -> None:
    """The mottled four-green ground every tile is built on top of."""
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, y), GRASS[h(x, y, salt) % len(GRASS)])


def clover_tile(img: Image.Image, ox: int) -> None:
    """A sparse clover patch with two flower heads — the 'this field is alive' tile."""
    base_ground(img, ox, salt=11)
    for y in range(TILE):
        for x in range(TILE):
            r = h(x, y, 21)
            if r < 90:
                img.putpixel((ox + x, y), CLOVER)
            elif r < 120:
                img.putpixel((ox + x, y), GRASS_LIT)
    # Two flowers, placed (not scattered) so they read as objects rather than noise.
    for fx, fy in ((4, 5), (11, 10)):
        img.putpixel((ox + fx, fy), FLOWER)
        img.putpixel((ox + fx, fy + 1), GRASS_DARK)


def pebble_tile(img: Image.Image, ox: int) -> None:
    """Worn ground: a few stones pushing through the turf, for paths' ragged edges."""
    base_ground(img, ox, salt=31)
    for cx, cy in ((3, 4), (9, 7), (12, 12), (6, 11)):
        img.putpixel((ox + cx, cy), STONE)
        img.putpixel((ox + cx + 1, cy), STONE)
        img.putpixel((ox + cx, cy + 1), STONE_DARK)
        img.putpixel((ox + cx + 1, cy + 1), STONE_DARK)


def render() -> Image.Image:
    existing = Image.open(ATLAS).convert("RGBA")
    kept = min(existing.width // TILE, 4)  # the four originals, never regenerated here
    out = Image.new("RGBA", ((kept + 2) * TILE, TILE))
    out.paste(existing.crop((0, 0, kept * TILE, TILE)), (0, 0))
    clover_tile(out, kept * TILE)
    pebble_tile(out, (kept + 1) * TILE)
    return out


def main() -> int:
    check = "--check" in sys.argv[1:]
    fresh = render()

    if check:
        current = Image.open(ATLAS).convert("RGBA")
        if current.tobytes() != fresh.tobytes() or current.size != fresh.size:
            print("gen-ground-tiles.py: error: town2d-ground-atlas.png differs from a fresh render.",
                  file=sys.stderr)
            return 1
        print(f"gen-ground-tiles.py: check OK -- {fresh.width // TILE} tiles match {ATLAS}")
        return 0

    fresh.save(ATLAS)
    print(f"gen-ground-tiles.py: wrote {ATLAS} ({fresh.width // TILE} tiles)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
