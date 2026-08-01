"""Recolor forge.png's roof out of magenta (#520051-family) into a terracotta sampled from
tavern.png's own committed shingles — Option D of the 2026-08-01 building-exterior receipt.

WHY THIS EXISTS
---------------
#316 traced the town's real defect to TownLayout2D.Venues pointing at the wrong ASSET FAMILY
(the town2d pixel set sat committed and unreferenced while the town kept drawing the older SDXL
buildings), and swapped all five venues to the pixel set to fix it. But the owner's playtest
verdict was "the buildings look WORSE, we only asked for interior changes" — he prefers the SDXL
look and never asked for an exterior swap. The one thing that WAS actually broken in the SDXL
set is forge.png's roof: hue ~283-353 (magenta/hot-pink), saturation up to 1.0, while every other
roof/plum surface in the town (tavern, market, the well) sits in a muted 0.15-0.33 saturation
band. That is the real defect #316's commit message points at (#520051) — not the asset family.
This script is "the old look, minus the one thing that was broken": keep the SDXL buildings, fix
only the roof that clashed.

WHY A FROZEN SOURCE, NOT A SELF-REFERENTIAL TRANSFORM
--------------------------------------------------------
The first version of this script re-read `godot/assets/art/forge.png` as its own input, hoping
hue-selection would be naturally idempotent (a fixed pixel's hue would no longer fall in the
"still magenta" band, so re-running would touch nothing). That failed `--check` on the very
first run: the roof's OWN target hue (terracotta, ~2 degrees) sits only ~57 degrees from the
magenta centre on the hue wheel, inside the span needed to also catch the roof's brightest
highlight stripe (which sits at ~0-2 degrees, wrapping past 360) — so the fixed colour and the
defect it replaces are neighbours on the wheel, and no hue-only gate can tell them apart. Sourcing
from a frozen pre-fix copy instead (`sources/forge-sdxl-magenta.png`, byte-identical to
forge.png before this script ever ran) sidesteps the whole problem: the transform is a pure
function of a FIXED input, exactly gen-market.py's shape, and `--check` is the same "fresh render
vs committed" comparison gen-market.py already uses — no self-reference, no wheel-neighbour trap.

WHY THIS HUE, NOT A NEW ONE
----------------------------
TARGET_HUE is sampled directly from tavern.png rows 0-30 (its own shingled roof, hue ~2 degrees,
saturation ~0.30) — the only other SDXL building with a banded/shingled roof rather than a flat
plum one, so the forge roof joins a family the town already has instead of a seventh hand-picked
colour.

SELECTION, NOT A REPAINT
--------------------------
Two independent gates keep this from touching anything but the offending stripes:
  1. Spatial: y < ROOF_MAX_Y (45) — verified against the source PNG's own pixel column x=5,
     where the magenta band runs rows 7-41 and the fascia shadow (low-saturation, left alone)
     starts at 42. Below that is wall/window/door, none of which is broken.
  2. Colour: hue within HUE_WRAP_SPAN degrees of MAGENTA_CENTER on the hue wheel (a circular, not
     linear, distance — the roof's brightest highlight stripe sits at hue ~0-2, which wraps PAST
     360 from the ~300-350 body of the band; a naive `260 <= hue <= 350` range clips that
     highlight and leaves a visible unfixed streak, caught by rendering and eyeballing this
     transform before committing it), AND saturation/lightness bounds that keep the chimney
     (tan/orange, hue ~15-38) and the fascia shadow (desaturated, S<0.35) out.
Lightness is never touched, so the roof's existing shingle banding (the actual light/dark stripe
read) survives pixel-for-pixel — only the hue and saturation that made it magenta move.

Usage:
    python art/pipeline/recolor-forge-roof.py [--check]
    --check   render the transform fresh from the frozen source and compare against the
              committed PNG; writes nothing, exits non-zero on any difference (same contract
              as gen-market.py --check).
"""
from __future__ import annotations

import argparse
import colorsys
import pathlib
import sys

from PIL import Image

# Frozen, never-touched-again copy of forge.png as it stood before this script ever ran — the
# deterministic INPUT (see "why a frozen source" above). Only this file and the constants below
# determine the output; the committed OUTPUT is never read as an input.
SOURCE = pathlib.Path("art/pipeline/sources/forge-sdxl-magenta.png")
OUT = pathlib.Path("godot/assets/art/forge.png")

# Sampled from tavern.png rows 0-30 (its own shingled roof) — see module doc.
TARGET_HUE = 2.0 / 360.0
SATURATION_SCALE = 0.42  # maps the roof's ~0.6-1.0 saturation down into tavern's ~0.30 band

# Circular hue window: pixels within HUE_WRAP_SPAN degrees of MAGENTA_CENTER, measured the short
# way around the wheel — covers the band's deep magenta (~283-350) AND its highlight stripe
# (~0-2, only ~55-57 degrees from centre going the OTHER way around 360/0) in one gate, where a
# plain `260 <= hue <= 350` linear range clips the highlight (see module doc).
MAGENTA_CENTER = 305.0
HUE_WRAP_SPAN = 60.0

SAT_MIN = 0.35
LIGHT_MAX = 0.85
ROOF_MAX_Y = 45  # measured boundary; see module doc's column-x=5 trace


def _hue_distance(deg: float) -> float:
    d = abs(deg - MAGENTA_CENTER) % 360.0
    return min(d, 360.0 - d)


def render() -> Image.Image:
    src_im = Image.open(SOURCE).convert("RGBA")
    w, h = src_im.size
    out = Image.new("RGBA", (w, h))
    src = src_im.load()
    dst = out.load()
    for y in range(h):
        for x in range(w):
            p = src[x, y]
            if p[3] <= 10 or y >= ROOF_MAX_Y:
                dst[x, y] = p
                continue
            r, g, b = p[0] / 255.0, p[1] / 255.0, p[2] / 255.0
            hue, light, sat = colorsys.rgb_to_hls(r, g, b)
            if _hue_distance(hue * 360.0) <= HUE_WRAP_SPAN and sat > SAT_MIN and light < LIGHT_MAX:
                nr, ng, nb = colorsys.hls_to_rgb(TARGET_HUE, light, sat * SATURATION_SCALE)
                dst[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), p[3])
            else:
                dst[x, y] = p
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="render in memory and compare against the committed PNG instead of writing")
    args = ap.parse_args()

    if not SOURCE.exists():
        print(f"FAIL {SOURCE} does not exist (frozen source missing)", file=sys.stderr)
        return 1

    fresh = render()

    if args.check:
        if not OUT.exists():
            print(f"FAIL {OUT} does not exist", file=sys.stderr)
            return 1
        committed = Image.open(OUT).convert("RGBA")
        if committed.size != fresh.size:
            print(f"FAIL size drift: committed {committed.size} vs fresh {fresh.size}", file=sys.stderr)
            return 1
        if list(committed.get_flattened_data()) != list(fresh.get_flattened_data()):
            print(f"FAIL {OUT} differs from a fresh render of {SOURCE}", file=sys.stderr)
            return 1
        print(f"ok {OUT} matches a fresh render")
        return 0

    OUT.parent.mkdir(parents=True, exist_ok=True)
    fresh.save(OUT)
    print(f"wrote {OUT} (roof recoloured magenta -> terracotta)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
