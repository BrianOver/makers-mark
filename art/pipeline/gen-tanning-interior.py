"""Author the four Tanning (Tannery) station props.

Bug fix, 2026-08-09: WorkshopVocab.ByProfession[TanningProfession.Id] (godot/scripts/town2d/
WorkshopVocab.cs) has pinned these four station ids since U7 (world-and-interiors plan,
docs/plans/2026-08-02-004) -- "scrape-frame"/"hide-rack"/"goods-rack"/"vats", sprite ids
town2d-station-tan-frame/-tan-hides/-tan-rack/-tan-vats -- but U8 (the unit meant to paint them)
never ran, the same gap art/pipeline/gen-engineering-interior.py's own docstring documents for
Engineering's four stations. The four ids sat in AssetResolutionCensusTests.KnownPendingIds with no
PNG and no manifest entry, so any player who selected Tanning saw TownAssets2D.ForStation's loud
magenta placeholder for all four stations. This script paints the real pixels; the fix's own PR
removes the four ids from KnownPendingIds in the same commit AND adds a no-escape-hatch census
case (mirroring EngineeringStationArtIds_ResolveToCommittedArt_NeverAPlaceholder) so these four
specifically can never again resolve to nothing without a red build.

The shared workshop shell needs no repainting here either, same reason as Engineering/Alchemy:
KTD-3 already made the workshop ONE shell (town2d-forge-interior-shell) reused by every profession.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same discipline as every sibling script in this pipeline: every colour below is SAMPLED FROM
COMMITTED town2d-* PIXELS, never picked by eye, so the Tannery reads as built from the same
material as the Forge the player already stood in. Byte-reproducible (`--check`), editable
without a GPU or an image editor -- no GPU/diffusion model is used here, per the same reasoning
tools/art/gen_town_sprites.py's own header documents: at sprite scale a diffusion render
downscales to mush.

PALETTE PROVENANCE -- the structural family every interior script already verified against
committed pixels (re-quoted here verbatim rather than re-run, per gen-gatehouse-interior.py's own
precedent; counts match gen-engineering-interior.py's own re-sample):
    town2d-forge.png   (58, 42, 84, 255)   x1214 -> IRON
    town2d-forge.png   (38, 27, 61, 255)   x566  -> IRON_DEEP
    town2d-tavern.png  (90, 54, 46, 255)   x281  -> WOOD
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE

New for this room -- sampled fresh from the town's own committed crate art, since no prior interior
needed a leather/hide tone (every WOOD/PLANK tone already claimed belongs to structural timber, not
tanned hide, and this room's whole craft is leatherwork):
    town2d-prop-crate.png (120, 82, 48, 255) x72 -> HIDE       (raw leather, mid tone)
    town2d-prop-crate.png (140, 98, 58, 255) x37 -> HIDE_LIT   (lit face)
    town2d-prop-crate.png (70, 46, 26, 255)  x41 -> HIDE_DARK  (shadow/outline)

Usage:
    python art/pipeline/gen-tanning-interior.py [--check]

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
BONE = (216, 207, 224, 255)
WOOD = (90, 54, 46, 255)
HIDE = (120, 82, 48, 255)
HIDE_LIT = (140, 98, 58, 255)
HIDE_DARK = (70, 46, 26, 255)


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
def render_scrape_frame() -> Image.Image:
    """32x20. A hide stretched taut across a wood frame, mid-scrape -- HIDE/HIDE_LIT/HIDE_DARK,
    this room's leather palette, sampled from the town's own crate art."""
    w, h = 32, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 30, 18, WOOD, w, h)                 # frame
    outline(px, 1, 1, 30, 18, w, h)
    rect(px, 4, 4, 27, 15, HIDE, w, h)                 # stretched hide
    rect(px, 4, 4, 15, 15, HIDE_LIT, w, h)             # lit half (town2d upper-left convention)
    outline(px, 4, 4, 27, 15, w, h, HIDE_DARK)
    for lx in range(4, 28, 4):                          # lacing pegs along the frame's top/bottom
        px[lx, 3] = IRON_DEEP
        px[lx, 16] = IRON_DEEP
    rect(px, 14, 8, 18, 9, HIDE_DARK, w, h)            # a worked-thin scraped patch

    return im


def render_hide_rack() -> Image.Image:
    """28x32. Same timber rack frame as the Forge/Market/Workbench/Apothecary sets, stacked raw
    hides instead of ingots -- HIDE/HIDE_LIT, no new hues."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)
    outline(px, 1, 1, 26, 30, w, h)
    for y in (1, 11, 21, 30):
        rect(px, 1, y, 26, y, IRON_DEEP, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)

    for gy in (4, 14, 24):                               # three shelves of stacked hides
        for gx, tone in ((3, HIDE), (13, HIDE_LIT)):
            rect(px, gx, gy, gx + 10, gy + 5, tone, w, h)
            outline(px, gx, gy, gx + 10, gy + 5, w, h, HIDE_DARK)

    return im


def render_goods_rack() -> Image.Image:
    """28x32. Finished leatherwork on display: a hanging belt strap and a satchel -- the same
    "hang finished goods on the frame" idiom gen-forge-interior.py's own rack uses for blades."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)
    outline(px, 1, 1, 26, 30, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)

    for hx in (7, 20):                                   # two mounting pegs
        rect(px, hx - 1, 3, hx + 1, 5, IRON_DEEP, w, h)

    rect(px, 5, 6, 9, 24, HIDE, w, h)                   # a hanging belt strap
    outline(px, 5, 6, 9, 24, w, h, HIDE_DARK)
    for by in range(9, 22, 4):
        px[7, by] = IRON_DEEP                             # buckle holes

    rect(px, 17, 12, 24, 22, HIDE_LIT, w, h)            # a satchel/pouch
    outline(px, 17, 12, 24, 22, w, h, HIDE_DARK)
    rect(px, 18, 12, 23, 13, HIDE_DARK, w, h)            # flap shadow

    return im


def render_vats() -> Image.Image:
    """28x20. Honest flavor (Action: null): two squat tanning vats -- the scrape frame does the
    real work. Kept as two SEPARATE shapes with an open gap above and below (never bridged, so the
    gap stays open to both canvas edges and holes() cannot flag it -- see gen-forge-interior.py's
    own quench doc for why an open-both-ends gap between isolated shapes is safe)."""
    w, h = 28, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    for vx in (2, 16):
        rect(px, vx, 6, vx + 9, 19, IRON_DEEP, w, h)     # vat body (iron-banded barrel)
        outline(px, vx, 6, vx + 9, 19, w, h)
        rect(px, vx + 1, 7, vx + 8, 7, BONE, w, h)        # rim
        rect(px, vx + 1, 9, vx + 8, 17, HIDE_DARK, w, h)  # tanning liquor
        rect(px, vx, 13, vx + 9, 13, IRON, w, h)          # iron hoop band

    return im


SPRITES = {
    "town2d-station-tan-frame": render_scrape_frame,
    "town2d-station-tan-hides": render_hide_rack,
    "town2d-station-tan-rack": render_goods_rack,
    "town2d-station-tan-vats": render_vats,
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
            print(f"gen-tanning-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-tanning-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-tanning-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
