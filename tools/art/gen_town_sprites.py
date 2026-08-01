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
(A second, LARGER direction -- redrawing at the `CharacterSpriteScale` headroom that
`TownLayout2D.CharacterSpriteScale`'s doc describes -- is deliberately a separate, uncommitted
script under `art/pipeline/candidates/`, not this file: this file's job is the town's hand-placed
20x36 contract, not a size change, which is a Brian-eyes design call, not a quality pass.)

QUALITY PASS (2026-08-01, feat/hero-sprite-quality)
----------------------------------------------------
Brian's repeated playtest verdict -- "the heroes/NPCs still look like booty" -- traced to this
file: every class's torso/arm/leg fill was one FLAT tone ('m'), so the silhouette read as a
cardboard cutout with no volume, no matter how crisp the outline around it was. The fix is a
mechanical, silhouette-preserving re-shade, not a redraw: every maximal horizontal run of the
flat 'm' fill is replaced with a light-to-dark gradient (added one new tone, 'i', below) using
ONLY colours already sampled from committed siblings (same rule `gen-market.py` follows) --
never colours picked by eye, never a new hue. Because the transform only recolors existing 'm'
cells (every other character -- outline, accent, already-placed highlight/shadow -- is left
byte-for-byte untouched), it is provably silhouette-safe: it cannot open a new transparent gap,
cannot shift any outline pixel, and cannot desync a base frame from its step frame (both are
built from the identical grids below, exactly as before). Same 20x36 canvas, same `--check`
drift guard, same ids -- zero risk to `TownAssets2D`'s dynamic `sprite.GetHeight()` sizing or the
census tests, which pin ids/resolution, never pixel content.

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
    "i": (61, 50, 66, 255),     # Iron-lit — QUALITY PASS: town2d-tavern's own body tone, sampled
                                # verbatim (never picked by eye); sits between 'd' and 'm' so a
                                # flat fill run can step through FOUR tones instead of two.
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
    "......ohlllmdo......",
    "......ohlolmdo......",  # visor slit
    "......ohlolmdo......",
    "......omlllido......",
    ".......omllmo.......",
    "........oooo........",
    ".......omllmo.......",  # gorget
    "...oooomllllmoooo...",
    "..ohhllilllllmdldo..",  # pauldrons, ember rim goes upper-left
    ".oehhllilllllmidldo.",
    ".oehllmdllllllmiddo.",
    ".oehllmdltttllmiddo.",  # circuit trace across the breastplate
    "..ooollmdlrllmdlooo.",  # rune glyph at the heart
    "ooooollmdllllmdlo...",
    "ohhhmollllmmiidlo...",  # slab shield, left arm — face lit so it reads as a plane
    "ohhlliolllmmiidlo...",
    "ohllitmolllmmidlo...",  # shield boss carries the circuit trace
    "ohllmidolllmmidlo...",
    "ohlllmidolllmidlo...",
    ".ohllmidolllmidlo...",
    "..ooooooolllmidlo...",
    ".......oomllilmoo...",
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

# QUALITY-PASS NOTE (applies to every grid below): each maximal run of the original flat 'm'
# fill was mechanically re-shaded into a light-to-dark gradient using the new 'i' tone (see
# PALETTE) plus the existing 'l'/'m'/'d' steps -- e.g. row 4's "ohllmmmo" (one flat mid-tone
# blob) became "ohlllmdo" (light rim -> mid -> shadow). Outline, transparency and every
# already-placed accent character (o/e/t/r and any hand-placed h/l/d) are untouched, so the
# silhouette, the holes-free guarantee, and base/step alignment are all identical to before —
# only the fill has volume now. See this file's module doc for the full rationale.
VANGUARD_STEP = VANGUARD[:25] + [
    ".......oomllilmoo...",
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
    "......ohllmido......",
    "......ohlmoomo......",  # eyes in shadow
    ".......ollmdo.......",
    ".......ollmdo.......",
    "........oooo........",
    "........ollo........",
    ".....oooollooooo....",
    "....ohhllilllildo...",
    "...oehllmdlllmiddo..",
    "...oehllitlllmiddo..",  # crossed strap with a circuit trace
    "...oehllmdlllmiddo..",
    "....ohllmdrllmdldo..",  # rune at the belt
    "....oollmdlllmdloo..",
    "...ohdollilllilodho.",  # blade hilt right, dagger left
    "...ohdollilllilodho.",
    "...ohdo.ollilo.odho.",
    "...ohdo..ollo..odho.",
    "...ohdo..ollo..odho.",
    "....oo...ollo...oo..",
    ".........ollo.......",
    "........omllmo......",
    "........ollilo......",
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
    ".......ollilo.......",
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
    ".....ohhlllmido.....",  # deep cowl
    ".....ohlmooolio.....",
    ".....ohlmoooomo.....",  # face is shadow only
    "......olllmido......",
    ".......ollmdo.......",
    "........oooo........",
    "........ollo........",
    "......ooollooo......",
    ".....ohhlmlllido....",
    "....oehhlmlllmddo...",
    "....oehllilllmddo..o",  # staff enters upper right
    "....oehlmtrtllido.or",  # rune between two circuit traces
    "....ohllmdlllmddo.or",
    "...oohllmdlllmddoo.o",
    "..ohdohllmdlllildo.o",
    "..ohdo.ollmdllmlo.o.",
    "..ohdo.ollmdllmlo.o.",
    "...oo..ollmdllmlo.o.",
    ".......ollmdllmlo.o.",
    "......ollmidlo......",
    "......ollmidlo......",
    ".....olllmmidlo.....",
    ".....olllmmidlo.....",
    "....ollllmmiidlo....",
    "....ollllmmiidlo....",
    "...ollllmmmiiidlo...",
    "...ollllmmmiiidlo...",
    "...odlllmmmiiiddo...",
    "...odlllmmmiiiddo...",
    "...oddddddddddddo...",
    "...oooooooooooooo...",
    "....................",
]

# The Mystic hovers rather than steps: the robe hides the legs entirely, so a leg-swap frame would
# be invisible. Its step frame shifts the hem instead — the robe swaying as weight transfers.
MYSTIC_STEP = MYSTIC[:25] + [
    ".....olllmmidlo.....",
    "....olllmmidlo......",
    "...ollllmmiidlo.....",
    "...ollllmmiidlo.....",
    "..ollllmmmiiidlo....",
    "..ollllmmmiiidlo....",
    "..odlllmmmiiiddo....",
    "..odlllmmmiiiddo....",
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
            # get_flattened_data(), not the deprecated getdata() (Pillow 14 removes it, 2027-10-15)
            # -- same call gen-market.py already uses for its own --check comparison.
            elif (list(Image.open(path).convert("RGBA").get_flattened_data())
                    != list(image.get_flattened_data())):
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
