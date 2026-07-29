#!/usr/bin/env python
"""Author the town's character sprites as explicit pixel grids.

WHY THIS IS NOT THE SDXL PIPELINE
---------------------------------
Every backdrop, portrait, monster and prop in `godot/assets/art/` comes from the local
ComfyUI/SDXL chain (see `godot/assets/art/README.md`). These sprites deliberately do not,
for the same reason `docs/plans/2026-07-28-001-feat-animation-motion-adventure.md` rejected
generated walk cycles: at 20x36 a diffusion render downscales to mush, and it cannot hold
identity or palette between a base frame and its step frame. Tried and confirmed once more
before writing this: an SDXL pass at 768 returned a two-view character turnaround in saturated
purples -- unusable at sprite scale even before the downsample.

So these are hand-authored, one pixel at a time, as ASCII grids mapped through the style-bible
palette. That makes them reviewable in a diff, editable without a GPU, and byte-identical on
every machine -- and it is the only way to guarantee the base and step frames differ ONLY in
the legs, which is what makes the 2-frame walk read as walking instead of flickering.

NEUTRAL BY CONTRACT
-------------------
`TownAssets2D.ForHero` documents that hero bodies "are drawn neutral-tinted so HeroActor2D can
multiply in the class color via modulate ... never baked in here". So the palette below is
deliberately desaturated: the class identity lives in the SILHOUETTE (slab shield / light
leathers / flared robe), and the colour arrives in-engine. Baking a crimson Striker here would
double-tint the moment `ClassColors.RoleColor` multiplied over it.

Sizes match what is already on disk and what `TownAssets2D`/`TownLayout2D` lay out against
(20x36 bodies) -- changing them would move every hand-placed tile coordinate in the town.

Usage:
    python tools/art/gen_town_sprites.py [--out DIR] [--check]

    --check   render in memory and compare against the committed PNGs; non-zero exit on any
              difference, and writes nothing. Use as a drift guard.
"""
from __future__ import annotations

import argparse
import os
import sys

from PIL import Image

# ── palette ────────────────────────────────────────────────────────────────────────────────────
# Style-bible hexes (docs/style-bible.md), desaturated toward bone/iron for the neutral contract.
PALETTE = {
    ".": (0, 0, 0, 0),          # transparent
    "o": (20, 15, 31, 255),     # Void — outline
    "d": (42, 36, 56, 255),     # Iron — deepest shading
    "m": (110, 104, 128, 255),  # mid tone
    "l": (184, 176, 198, 255),  # light
    "h": (216, 207, 224, 255),  # Bone — highlight / rim
    "e": (224, 145, 63, 255),   # Ember — candle rim light, upper-left only
    "t": (63, 176, 172, 255),   # Coolant — the one faint circuit trace every metal object carries
    "r": (107, 76, 154, 255),   # Arcane — the one rune glyph
}

WIDTH, HEIGHT = 20, 36

# ── the sprites ────────────────────────────────────────────────────────────────────────────────
# 20 columns x 36 rows. Read them as pictures; that is the point of authoring this way.
#
# Shared skeleton so the three classes stand at the same height and their step frames line up:
#   rows 2-10  head/helm      rows 11-24  torso + arms      rows 25-33  legs      rows 34-35  feet

VANGUARD = [
    "....................",
    "....................",
    ".......oooooo.......",
    "......ohhhllmo......",
    "......ohllmmmo......",
    "......ohlommmo......",  # visor slit
    "......ohlommmo......",
    "......omllmmdo......",
    ".......omllmo.......",
    "........oooo........",
    ".......omllmo.......",  # gorget
    "...oooomllllmoooo...",
    "..ohhlmmllllmmmldo..",  # pauldrons, ember rim goes upper-left
    ".oehhlmmllllmmmmldo.",
    ".oehlmmmllllmmmmmdo.",
    ".oehlmmmltttlmmmmdo.",  # circuit trace across the breastplate
    "..ooolmmmlrlmmmlooo.",  # rune glyph at the heart
    "ooooolmmmlllmmmlo...",
    "ohhhmolmmmmmmmmlo...",  # slab shield, left arm — face lit so it reads as a plane
    "ohhlmmolmmmmmmmlo...",
    "ohlmmtmolmmmmmmlo...",  # shield boss carries the circuit trace
    "ohlmmmmolmmmmmmlo...",
    "ohlmmmmmolmmmmmlo...",
    ".ohlmmmmolmmmmmlo...",
    "..ooooooolmmmmmlo...",
    ".......oomlmmlmoo...",
    "........olmoolmo....",
    "........olmoolmo....",
    "........olmoolmo....",
    "........odmoodmo....",
    "........odmoodmo....",
    "........odmoodmo....",
    "........odmoodmo....",
    "........oddooddo....",
    ".......ohhdoohhdo...",  # boots
    ".......oooooooooo...",
]

VANGUARD_STEP = VANGUARD[:25] + [
    ".......oomlmmlmoo...",
    ".......olmoo.olmo...",  # near leg swings forward, far leg trails
    "......olmo....olmo..",
    "......olmo....olmo..",
    ".....odmo.....odmo..",
    ".....odmo.....odmo..",
    ".....odmo......odmo.",
    ".....odmo......odmo.",
    ".....oddo......oddo.",
    "....ohhdo.....ohhdo.",
    "....ooooo.....ooooo.",
]

STRIKER = [
    "....................",
    "....................",
    "........oooo........",
    ".......ohhllo.......",
    "......ohhlllmo......",  # hood
    "......ohlmmmmo......",
    "......ohlmoomo......",  # eyes in shadow
    ".......olmmmo.......",
    ".......olmmmo.......",
    "........oooo........",
    "........ollo........",
    ".....oooollooooo....",
    "....ohhlmmllmmldo...",
    "...oehlmmmllmmmmdo..",
    "...oehlmmtllmmmmdo..",  # crossed strap with a circuit trace
    "...oehlmmmllmmmmdo..",
    "....ohlmmmrlmmmldo..",  # rune at the belt
    "....oolmmmllmmmloo..",
    "...ohdolmmllmmlodho.",  # blade hilt right, dagger left
    "...ohdolmmllmmlodho.",
    "...ohdo.olmmlo.odho.",
    "...ohdo..ollo..odho.",
    "...ohdo..ollo..odho.",
    "....oo...ollo...oo..",
    ".........ollo.......",
    "........omllmo......",
    "........olmmlo......",
    "........olmoolo.....",
    "........olmoolo.....",
    "........odmoodo.....",
    "........odmoodo.....",
    "........odmoodo.....",
    "........odmoodo.....",
    "........oddoodo.....",
    ".......ohhdoohdo....",
    ".......oooooooo.....",
]

STRIKER_STEP = STRIKER[:25] + [
    "........omllmo......",
    ".......olmmlo.......",
    "......olmo..olo.....",
    "......olmo..olo.....",
    ".....odmo....odo....",
    ".....odmo....odo....",
    "....odmo......odo...",
    "....odmo......odo...",
    "....oddo......odo...",
    "...ohhdo.....ohdo...",
    "...ooooo.....oooo...",
]

MYSTIC = [
    "....................",
    "....................",
    ".......oooooo.......",
    "......ohhlllmo......",
    ".....ohhllmmmmo.....",  # deep cowl
    ".....ohlmooommo.....",
    ".....ohlmoooomo.....",  # face is shadow only
    "......olmmmmmo......",
    ".......olmmmo.......",
    "........oooo........",
    "........ollo........",
    "......ooollooo......",
    ".....ohhlmllmmdo....",
    "....oehhlmllmmmdo...",
    "....oehlmmllmmmdo..o",  # staff enters upper right
    "....oehlmtrtlmmdo.or",  # rune between two circuit traces
    "....ohlmmmllmmmdo.or",
    "...oohlmmmllmmmdoo.o",
    "..ohdohlmmmllmmldo.o",
    "..ohdo.olmmmllmlo.o.",
    "..ohdo.olmmmllmlo.o.",
    "...oo..olmmmllmlo.o.",
    ".......olmmmllmlo.o.",
    "......olmmmmlo......",
    "......olmmmmlo......",
    ".....olmmmmmmlo.....",
    ".....olmmmmmmlo.....",
    "....olmmmmmmmmlo....",
    "....olmmmmmmmmlo....",
    "...olmmmmmmmmmmlo...",
    "...olmmmmmmmmmmlo...",
    "...odmmmmmmmmmmdo...",
    "...odmmmmmmmmmmdo...",
    "...oddddddddddddo...",
    "...oooooooooooooo...",
    "....................",
]

# The Mystic hovers rather than steps: the robe hides the legs entirely, so a leg-swap frame would
# be invisible. Its step frame shifts the hem instead — the robe swaying as weight transfers.
MYSTIC_STEP = MYSTIC[:25] + [
    ".....olmmmmmmlo.....",
    "....olmmmmmmlo......",
    "...olmmmmmmmmlo.....",
    "...olmmmmmmmmlo.....",
    "..olmmmmmmmmmmlo....",
    "..olmmmmmmmmmmlo....",
    "..odmmmmmmmmmmdo....",
    "..odmmmmmmmmmmdo....",
    "..oddddddddddddo....",
    "..oooooooooooooo....",
    "....................",
]

SPRITES = {
    "town2d-hero-vanguard": VANGUARD,
    "town2d-hero-vanguard_step": VANGUARD_STEP,
    "town2d-hero-striker": STRIKER,
    "town2d-hero-striker_step": STRIKER_STEP,
    "town2d-hero-mystic": MYSTIC,
    "town2d-hero-mystic_step": MYSTIC_STEP,
}


def die(message: str) -> None:
    print(f"gen_town_sprites.py: error: {message}", file=sys.stderr)
    raise SystemExit(1)


def render(grid: list[str], name: str) -> Image.Image:
    """Rasterize one ASCII grid. Validates shape loudly — a short row would silently shift
    every pixel after it, which is exactly the kind of defect a diff cannot show."""
    if len(grid) != HEIGHT:
        die(f"{name}: expected {HEIGHT} rows, got {len(grid)}")

    image = Image.new("RGBA", (WIDTH, HEIGHT), PALETTE["."])
    pixels = image.load()
    for y, row in enumerate(grid):
        if len(row) != WIDTH:
            die(f"{name}: row {y} is {len(row)} chars, expected {WIDTH}")
        for x, char in enumerate(row):
            if char not in PALETTE:
                die(f"{name}: row {y} col {x} uses '{char}', which is not in the palette")
            pixels[x, y] = PALETTE[char]

    return image


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out",
        default=os.path.join("godot", "assets", "art"),
        help="output directory (default: godot/assets/art)")
    parser.add_argument(
        "--check",
        action="store_true",
        help="compare against committed PNGs instead of writing; non-zero exit on any difference")
    args = parser.parse_args()

    drift = []
    for name, grid in SPRITES.items():
        image = render(grid, name)
        path = os.path.join(args.out, f"{name}.png")

        if args.check:
            if not os.path.exists(path):
                drift.append(f"{name}: no committed PNG at {path}")
            elif list(Image.open(path).convert("RGBA").getdata()) != list(image.getdata()):
                drift.append(f"{name}: committed PNG differs from the grid in this script")
            continue

        image.save(path)
        print(f"wrote {path} ({WIDTH}x{HEIGHT})")

    if drift:
        for line in drift:
            print(f"gen_town_sprites.py: drift: {line}", file=sys.stderr)
        return 1

    if args.check:
        print(f"no drift — {len(SPRITES)} sprites match their committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
