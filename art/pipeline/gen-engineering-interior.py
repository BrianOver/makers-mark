"""Author the four Engineering (Workbench Hall) station props.

Bug fix, 2026-08-09: WorkshopVocab.ByProfession[EngineeringProfession.Id] (godot/scripts/town2d/
WorkshopVocab.cs) has pinned these four station ids since U7 (world-and-interiors plan,
docs/plans/2026-08-02-004) -- "bench"/"gear-rack"/"parts-crate"/"flywheel", sprite ids
town2d-station-eng-bench/-eng-gears/-eng-crate/-eng-flywheel -- but U8 (the unit meant to paint
them, per that plan's own text: "U8 paints them and removes these 17 lines") never ran. The four
ids sat in AssetResolutionCensusTests.KnownPendingIds as "pending" for a full week with no PNG,
no manifest entry, and no test failure, so every player who selected Engineering saw
TownAssets2D.ForStation's loud magenta placeholder box for all four stations -- exactly the
"random unloaded/non generated assets" the owner reported. This script paints the real pixels;
the fix's own PR removes the four ids from KnownPendingIds in the same commit AND adds a
no-escape-hatch census case (mirroring ForgeInteriorArtIds_ResolveToCommittedArt_NeverAPlaceholder)
so these four specifically can never again resolve to nothing without a red build.

Only the shared workshop shell needed no repainting: KTD-3 already made the workshop ONE shell
(town2d-forge-interior-shell) reused by every profession -- InteriorLayout2D.WorkshopRoomFor keeps
the "forge" row's shell/size/door for every profession selection, swapping only the station table.
That is why the room, floor, door and player sprite all rendered fine in the reported screenshot
while just the four station icons were magenta: the shell was always real art; the stations never
were.

WHY GENERATED, NOT PAINTED BY HAND IN AN IMAGE EDITOR
------------------------------------------------------
Same discipline as every sibling script in this pipeline (gen-forge-interior.py, gen-market-
interior.py, gen-tavern-interior.py, gen-gatehouse-interior.py): every colour below is SAMPLED
FROM COMMITTED town2d-* PIXELS, never picked by eye, so the Workbench Hall reads as built from the
same material as the Forge the player already stood in. Byte-reproducible (`--check`), editable
without a GPU or an image editor -- and per this repo's rule, no GPU/diffusion model is used here.

PALETTE PROVENANCE -- the same structural family every interior script above already verified
against committed pixels (re-quoted here verbatim rather than re-run, per gen-gatehouse-
interior.py's own precedent of re-stating the set instead of silently trusting a sibling's
docstring; the values match gen-forge-interior.py's own re-sample, which remains the source
of truth):
    town2d-forge.png   (58, 42, 84, 255)   x1214 -> IRON
    town2d-forge.png   (38, 27, 61, 255)   x566  -> IRON_DEEP
    town2d-tavern.png  (86, 55, 92, 255)   x637  -> IRON_LIT
    town2d-forge.png   (216, 207, 224, 255) x385 -> BONE
    town2d-forge.png   (107, 76, 154, 255)  x20  -> ARCANE  (this room's one arcane accent -- the
                                                              gear rack's rune-etched cog, mirroring
                                                              the furnace's rune / the shelf's own
                                                              "one precious item" idiom)
    town2d-forge.png   (63, 176, 172, 255)  x19  -> COOLANT (this room's one coolant accent -- a
                                                              coolant flask on the workbench)
    town2d-tavern.png  (90, 54, 46, 255)   x281  -> WOOD
    town2d-tile-path.png (87, 77, 67, 255)  x35  -> PLANK_DARK (seam shadow, tabletop plank seams)
    town2d-tile-path.png (107, 95, 82, 255) x221 -> PLANK      (workbench tabletop surface --
                                                                  the same board tone the floor
                                                                  outside is made of)

SIZES -- authored fresh for this fix (WorkshopVocab pins tile position only, not pixel size, the
same way the market/tavern/gatehouse stations were free to choose their own): bench 32x20,
gear-rack 28x32 (matches the forge/market shelf convention for a browse-focus station), parts-crate
24x20 (matches town2d-station-market-crates' own footprint for the same station shape), flywheel
24x24. All four sit on WorkshopVocab's row y=11 (world y=176px) with stations 80px apart on center
(5 tiles) -- every width below is well clear of that spacing, and y=176 is far enough from the
room's own top edge that InteriorLayout2D's label-clearance constraint (see gen-gatehouse-
interior.py's own doc for the formula) never binds here.

Usage:
    python art/pipeline/gen-engineering-interior.py [--check]

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
PLANK = (107, 95, 82, 255)
PLANK_DARK = (87, 77, 67, 255)


# ── pixel helpers (verbatim from gen-forge-interior.py -- same canvas-size-parametrised rect/
#    outline/holes idiom every interior script in this pipeline shares) ──────────────────────────
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
    """Filled circle via squared-distance test -- integer-only, no transcendental math, so this
    is byte-identical on every machine (the same determinism discipline the sim itself requires,
    even though this script runs offline at authoring time, not inside the sim)."""
    for y in range(max(0, cy - r), min(h - 1, cy + r) + 1):
        for x in range(max(0, cx - r), min(w - 1, cx + r) + 1):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                px[x, y] = c


def gear(px, cx, cy, r, w, h, arcane=False):
    """A small cog: filled disc, four 1px teeth poking past the rim at the cardinal points, and a
    centre hole. `arcane=True` marks this room's single precious accent (the rune-etched cog on
    the gear rack's middle shelf) -- every other gear uses a plain iron hole."""
    circle(px, cx, cy, r, IRON, w, h)
    for dx, dy in ((r + 1, 0), (-r - 1, 0), (0, r + 1), (0, -r - 1)):
        tx, ty = cx + dx, cy + dy
        if 0 <= tx < w and 0 <= ty < h:
            px[tx, ty] = IRON_LIT
    if 0 <= cx < w and 0 <= cy < h:
        px[cx, cy] = ARCANE if arcane else IRON_DEEP


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
def render_bench() -> Image.Image:
    """32x20. A wood-framed workbench: plank tabletop, two legs, a vise clamped to the right end,
    a tool laid across the surface, and this room's one COOLANT accent (a flask of coolant resting
    on the bench)."""
    w, h = 32, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 2, 14, 5, 19, WOOD, w, h)               # left leg
    outline(px, 2, 14, 5, 19, w, h)
    rect(px, 26, 14, 29, 19, WOOD, w, h)              # right leg
    outline(px, 26, 14, 29, 19, w, h)

    rect(px, 1, 11, 30, 13, WOOD, w, h)               # apron connecting the legs under the top
    outline(px, 1, 11, 30, 13, w, h)

    rect(px, 0, 4, 31, 10, PLANK, w, h)               # tabletop surface
    outline(px, 0, 4, 31, 10, w, h)
    rect(px, 1, 4, 30, 4, BONE, w, h)                 # top rim catches the light
    rect(px, 0, 7, 31, 7, PLANK_DARK, w, h)           # plank seam across the middle

    rect(px, 24, 0, 29, 4, IRON, w, h)                # vise body, mounted at the bench's right end
    outline(px, 24, 0, 29, 4, w, h)
    rect(px, 26, 0, 27, 1, IRON_DEEP, w, h)           # jaw gap
    px[25, 1] = IRON_LIT

    rect(px, 10, 1, 17, 3, IRON_LIT, w, h)            # a tool laid across the surface
    outline(px, 10, 1, 17, 3, w, h)

    rect(px, 3, 1, 6, 3, COOLANT, w, h)               # the room's one coolant accent: a flask
    outline(px, 3, 1, 6, 3, w, h)
    px[3, 1] = BONE                                    # glass highlight

    return im


def render_gears() -> Image.Image:
    """28x32. Same wood-shelf frame as the Forge/Market shelves, holding cogs of varying size
    instead of bundles/ingots. The middle shelf's larger cog carries this room's one ARCANE
    accent -- a rune-etched centre hole, echoing the shelf's own "one precious item" idiom."""
    w, h = 28, 32
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 1, 1, 26, 30, WOOD, w, h)                # frame
    outline(px, 1, 1, 26, 30, w, h)
    for y in (1, 11, 21, 30):                          # three shelf boards
        rect(px, 1, y, 26, y, IRON_DEEP, w, h)
    rect(px, 2, 2, 25, 2, BONE, w, h)                 # top rim highlight

    gear(px, 8, 6, 3, w, h)                            # upper shelf: two plain cogs
    gear(px, 19, 6, 3, w, h)

    gear(px, 13, 16, 4, w, h, arcane=True)             # middle shelf: the rune-etched cog
    gear(px, 22, 17, 2, w, h)                          # a small spare cog beside it

    gear(px, 7, 26, 3, w, h)                           # lower shelf: two more plain cogs
    gear(px, 19, 26, 3, w, h)

    return im


def render_crate() -> Image.Image:
    """24x20. A reinforced wooden parts crate -- iron corner braces, slat seams, and a couple of
    loose parts (a bolt, a rod) spilling over the open top. Deliberately just two isolated shapes
    up there, not three -- gen-forge-interior.py's own quench doc explains why: holes()'s coarse
    "opaque somewhere in every direction" check false-positives the moment a THIRD isolated shape
    brackets a gap between the other two (confirmed here: a spare cog centred between the bolt and
    rod tripped exactly that false positive and was dropped rather than worked around)."""
    w, h = 24, 20
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 2, 6, 21, 19, WOOD, w, h)                 # crate body
    outline(px, 2, 6, 21, 19, w, h)
    rect(px, 3, 6, 20, 6, BONE, w, h)                  # open-top rim catches the light
    rect(px, 2, 10, 21, 10, PLANK_DARK, w, h)          # slat seam
    rect(px, 2, 14, 21, 14, PLANK_DARK, w, h)          # slat seam

    for cx in (2, 19):                                  # iron corner braces, top and bottom
        rect(px, cx, 6, cx + 2, 8, IRON, w, h)
        rect(px, cx, 17, cx + 2, 19, IRON, w, h)

    rect(px, 5, 1, 7, 5, IRON, w, h)                   # a bolt sticking out of the open top
    outline(px, 5, 1, 7, 5, w, h)
    px[6, 1] = BONE                                     # bolt-head highlight

    rect(px, 15, 0, 16, 5, IRON_LIT, w, h)             # a loose rod leaning against the far side

    return im


def render_flywheel() -> Image.Image:
    """24x24. An idle flywheel on a squat iron pedestal -- honest flavor (Action: null): a
    curiosity, nothing to work here. Four spokes and a rim highlight so the disc doesn't read as a
    flat blob at this scale."""
    w, h = 24, 24
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()

    rect(px, 9, 19, 14, 22, IRON_DEEP, w, h)           # pedestal base
    outline(px, 9, 19, 14, 22, w, h)

    circle(px, 11, 10, 9, IRON, w, h)                   # the wheel body

    # rim highlight ring: redraw the outermost shell lit, left side only (upper-left convention
    # every town2d building/station in this pipeline follows)
    for y in range(1, 20):
        for x in range(2, 21):
            dist2 = (x - 11) ** 2 + (y - 10) ** 2
            if 56 <= dist2 <= 81 and x <= 11:
                px[x, y] = IRON_LIT

    # spokes, four arms from hub to rim
    for dx, dy in ((0, -8), (0, 8), (-8, 0), (8, 0)):
        steps = 8
        for i in range(steps + 1):
            sx = 11 + dx * i // steps
            sy = 10 + dy * i // steps
            if 0 <= sx < w and 0 <= sy < h:
                px[sx, sy] = IRON_DEEP

    circle(px, 11, 10, 2, BONE, w, h)                   # hub

    return im


SPRITES = {
    "town2d-station-eng-bench": render_bench,
    "town2d-station-eng-gears": render_gears,
    "town2d-station-eng-crate": render_crate,
    "town2d-station-eng-flywheel": render_flywheel,
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
            print(f"gen-engineering-interior.py: FAIL {name} has {len(gaps)} enclosed transparent "
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
                print(f"gen-engineering-interior.py: drift: {line}", file=sys.stderr)
            return 1
        print(f"gen-engineering-interior.py: check OK -- {len(SPRITES)} sprites match committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
