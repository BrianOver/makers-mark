"""Boost the town2d building set's shared structural palette — Option C of the 2026-08-01
building-exterior receipt (docs/plans; see TownLayout2D.Venues for the full trace).

WHY THIS EXISTS
---------------
The owner's playtest verdict on the town2d pixel buildings (post-#316) was "these look WORSE
than the old SDXL set." Measuring both sets (average HLS over every opaque pixel) shows why:
the shared structural tones every town2d building draws from -- VOID/IRON_DEEP/IRON/IRON_LIT,
the outline and two wall-shadow planes -- sit at saturation 0.14-0.35, while the SDXL set's
equivalents run 0.16-0.41. The gap reads as "flat and muddy" against the town's own grass
(saturation ~0.20-0.26, unchanged by any of this and NOT the cause — see the receipt notes:
identical ground under both the liked SDXL render and the disliked pixel render rules it out).
Every OTHER structural fix (ground desaturation) was tried and rendered first and moved the
needle far less than this one; see the receipt writeup for the side-by-side.

WHY THESE FIVE COLOURS, NOT A REPAINT
----------------------------------------
town2d-forge/market/tavern/board/mine-gate.png all draw from the SAME small (4-10 colour) named
palette gen-market.py already documents (VOID/IRON_DEEP/IRON/IRON_LIT/WOOD/BONE/EMBER/PARCHMENT/
ARCANE/COOLANT) -- confirmed by counting unique committed colours per file. Only the four
low-chroma STRUCTURAL tones (the outline and the three body/shadow planes) are touched here;
EMBER/BONE/WOOD/PARCHMENT/ARCANE/COOLANT (the accent colours: lit windows, linework, timber,
signage, the rune, the circuit trace) were already vivid and are left alone. This is a saturation
turn-up on the palette the buildings already use, not a new one -- the hue of every remapped
colour is unchanged; only saturation and lightness move.

SELF-CHECKING BY CONSTRUCTION
--------------------------------
The remap keys (the muddy originals) and values (the boosted replacements) are disjoint by
inspection -- no boosted value collides with an original key -- so applying this remap twice
changes nothing on the second pass: there is no separate frozen source to keep in sync (unlike
recolor-forge-roof.py, which needs one; see that script's own header for why). `--check` uses
exactly this: it re-applies the remap to the CURRENTLY COMMITTED files and asserts nothing
changes. If any of the five files drifts back toward the muddy originals, the remap would touch
pixels again and `--check` fails.

NOT WIRED BY DEFAULT
-----------------------
This is Option C on the receipt, not the shipped option (see TownLayout2D.Venues' own doc for
which one shipped and why) -- these five PNGs are boosted and committed so switching TO this
option is a five-line TownLayout2D.Venues edit, not a re-render. Sitting boosted-but-possibly-
unreferenced is the SAME shape #316 itself warned about (committed art nobody's pointing at) --
the difference this time is it is documented here, in the receipt, and in Venues' own comment,
so it cannot repeat as a silent multi-week miss.

Usage:
    python art/pipeline/boost-town2d-palette.py [--check]
    --check   verify every target PNG is already at this remap's fixed point; writes nothing.
"""
from __future__ import annotations

import argparse
import pathlib
import sys

from PIL import Image

TARGETS = [
    pathlib.Path("godot/assets/art/town2d-forge.png"),
    pathlib.Path("godot/assets/art/town2d-market.png"),
    pathlib.Path("godot/assets/art/town2d-tavern.png"),
    pathlib.Path("godot/assets/art/town2d-board.png"),
    pathlib.Path("godot/assets/art/town2d-mine-gate.png"),
]

# old (as committed today) -> new. Hue held fixed per entry; only saturation/lightness rise.
# Accent colours (EMBER/BONE/WOOD/PARCHMENT/ARCANE/COOLANT) are not in this table -- they were
# already vivid (see module doc) and are passed through untouched.
REMAP: dict[tuple[int, int, int, int], tuple[int, int, int, int]] = {
    (20, 15, 31, 255): (24, 16, 40, 255),   # VOID outline -- a touch richer, still near-black
    (30, 25, 42, 255): (38, 27, 61, 255),   # IRON_DEEP shadow plane
    (42, 36, 56, 255): (58, 42, 84, 255),   # IRON body (the dominant wall tone)
    (61, 50, 66, 255): (86, 55, 92, 255),   # IRON_LIT -- was LESS saturated than IRON; fixed
    (46, 38, 52, 255): (60, 42, 72, 255),   # tavern's extra shade tone
}


def remap(im: Image.Image) -> Image.Image:
    im = im.convert("RGBA")
    w, h = im.size
    out = Image.new("RGBA", (w, h))
    src = im.load()
    dst = out.load()
    for y in range(h):
        for x in range(w):
            p = src[x, y]
            dst[x, y] = REMAP.get(p, p)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="verify every target is already at this remap's fixed point")
    args = ap.parse_args()

    ok = True
    for target in TARGETS:
        if not target.exists():
            print(f"FAIL {target} does not exist", file=sys.stderr)
            ok = False
            continue

        committed = Image.open(target).convert("RGBA")
        fresh = remap(committed)
        unchanged = list(committed.get_flattened_data()) == list(fresh.get_flattened_data())

        if args.check:
            if not unchanged:
                print(f"FAIL {target} is not at the boosted fixed point -- remap not applied "
                      "(or reverted)", file=sys.stderr)
                ok = False
            else:
                print(f"ok {target} already boosted")
            continue

        if unchanged:
            print(f"{target} already boosted -- nothing to do")
        else:
            fresh.save(target)
            print(f"wrote {target} (structural palette boosted)")

    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
