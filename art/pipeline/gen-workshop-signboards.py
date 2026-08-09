"""Author the four workshop exterior signboard overlays -- one per profession.

Bug fix, 2026-08-09: WorkshopVocab.ByProfession (godot/scripts/town2d/WorkshopVocab.cs) has pinned
town2d-sign-blacksmith/-alchemy/-engineering/-tanning since U7 (world-and-interiors plan,
docs/plans/2026-08-02-004) -- Town2D.MountWorkshopSignboard hangs one above the workshop's nametag,
swapped per selected profession -- but U8 (the unit meant to paint them) never ran. Unlike the
station props, these are NEW art in this unit rather than a repaint of anything committed: no
signboard, blacksmith included, ever existed before this script. All four ids sat in
AssetResolutionCensusTests.KnownPendingIds with no PNG and no manifest entry, so the workshop
building rendered with no signboard overlay at all regardless of which profession was primary.
This script paints the real pixels; the fix's own PR removes all four ids from KnownPendingIds in
the same commit AND adds a no-escape-hatch census case (mirroring
EngineeringStationArtIds_ResolveToCommittedArt_NeverAPlaceholder) so these four specifically can
never again resolve to nothing without a red build.

Cosmetic overlay only (Town2D.MountWorkshopSignboard's own doc): the signboard carries no
collision and is not one of InteriorLayout2D's station footprints, so there is no KTD-5-style
pinned-size collision risk here the way there is for an interior station.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same discipline as every sibling script in this pipeline: every colour below is SAMPLED FROM
COMMITTED town2d-* PIXELS, never picked by eye. Byte-reproducible (`--check`), editable without a
GPU or an image editor -- no GPU/diffusion model is used here, per the same reasoning
tools/art/gen_town_sprites.py's own header documents: at sprite scale a diffusion render downscales
to mush, and four tiny 20x16 hanging signs are exactly that scale.

PALETTE PROVENANCE -- re-quoted verbatim from gen-engineering-interior.py's own re-sample (same
structural family every interior script in this pipeline already verified):
    town2d-forge.png   (58, 42, 84, 255)   x1214 -> IRON
    town2d-forge.png   (38, 27, 61, 255)   x566  -> IRON_DEEP
    town2d-tavern.png  (86, 55, 92, 255)   x637  -> IRON_LIT
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-forge.png   (107, 76, 154, 255)  x20  -> ARCANE
    town2d-tavern.png  (90, 54, 46, 255)   x281  -> WOOD

New for this unit -- same HERB_GREEN/HIDE tones gen-alchemy-interior.py / gen-tanning-interior.py
sample fresh from the town's own committed grass tile and crate art, reused here rather than
re-sampled a third time, so a blacksmith/alchemy/engineering/tanning sign set reads as one family:
    town2d-tile-grass.png (79, 106, 82, 255) x12 -> HERB_GREEN (alchemy sign's flask contents)
    town2d-prop-crate.png (120, 82, 48, 255) x72 -> HIDE       (tanning sign's hide icon)

SIZES
-----
All four: 20x16 -- a small hanging board, deliberately smaller than any interior station (this is
an EXTERIOR cosmetic overlay hung above the nametag, not something a player stands next to). Same
canvas for every profession so the four read as one signboard family, differing only in the
icon painted on the board face.

Usage:
    python art/pipeline/gen-workshop-signboards.py [--check]

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
WOOD = (90, 54, 46, 255)
HERB_GREEN = (79, 106, 82, 255)
HIDE = (120, 82, 48, 255)

W, H = 20, 16


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


def _board() -> Image.Image:
    """The shared board shell every signboard starts from: a bracket, two ropes, and a wood
    board with a rim highlight -- only the icon painted over the board face differs per
    profession."""
    im = Image.new("RGBA", (W, H), CLEAR)
    px = im.load()

    rect(px, 8, 0, 11, 1, IRON_DEEP, W, H)             # mounting bracket
    px[8, 0] = IRON
    px[11, 0] = IRON
    rect(px, 9, 1, 10, 2, IRON_DEEP, W, H)             # short chain down to the board

    rect(px, 1, 3, 18, 14, WOOD, W, H)                 # board
    outline(px, 1, 3, 18, 14, W, H)
    rect(px, 2, 4, 17, 4, BONE, W, H)                  # top rim highlight

    return im


def render_sign_blacksmith() -> Image.Image:
    """20x16. A small anvil silhouette -- IRON body, IRON_LIT top-left face, horn taper left."""
    im = _board()
    px = im.load()

    rect(px, 7, 8, 13, 11, IRON, W, H)
    rect(px, 7, 8, 10, 9, IRON_LIT, W, H)
    outline(px, 7, 8, 13, 11, W, H)
    rect(px, 5, 9, 7, 10, IRON, W, H)                  # horn taper
    outline(px, 5, 9, 7, 10, W, H)

    return im


def render_sign_alchemy() -> Image.Image:
    """20x16. A small corked flask silhouette with ARCANE brew inside."""
    im = _board()
    px = im.load()

    rect(px, 8, 6, 11, 7, IRON_DEEP, W, H)             # neck/cork
    rect(px, 7, 8, 12, 12, BONE, W, H)                 # flask glass
    outline(px, 7, 8, 12, 12, W, H)
    rect(px, 8, 9, 11, 11, ARCANE, W, H)               # brew

    return im


def render_sign_engineering() -> Image.Image:
    """20x16. A small cog silhouette -- IRON disc, four teeth, IRON_DEEP hole."""
    im = _board()
    px = im.load()

    circle(px, 9, 9, 3, IRON, W, H)
    for dx, dy in ((4, 0), (-4, 0), (0, 4), (0, -4)):
        tx, ty = 9 + dx, 9 + dy
        if 0 <= tx < W and 0 <= ty < H:
            px[tx, ty] = IRON_LIT
    px[9, 9] = IRON_DEEP

    return im


def render_sign_tanning() -> Image.Image:
    """20x16. A small stretched-hide silhouette -- HIDE body, IRON_DEEP outline."""
    im = _board()
    px = im.load()

    rect(px, 6, 7, 13, 12, HIDE, W, H)
    outline(px, 6, 7, 13, 12, W, H)
    rect(px, 6, 7, 9, 9, HERB_GREEN, W, H)             # a drying-rack accent corner, distinct
    # from hide-rack's own pure-leather tone so the sign reads as "tannery" not "leather goods"
    rect(px, 6, 7, 6, 12, IRON_DEEP, W, H)             # frame edge, stretched-taut cue

    return im


SPRITES = {
    "town2d-sign-blacksmith": render_sign_blacksmith,
    "town2d-sign-alchemy": render_sign_alchemy,
    "town2d-sign-engineering": render_sign_engineering,
    "town2d-sign-tanning": render_sign_tanning,
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
            print(f"gen-workshop-signboards.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-workshop-signboards.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-workshop-signboards.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
