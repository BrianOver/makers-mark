"""Candidate B for the hero/NPC sprite-quality pass -- the "bigger" direction, rendered for
comparison only. NOT the shipped path (see WHY THIS IS NOT COMMITTED ART below).

BACKGROUND -- what "scale headroom" actually means here
--------------------------------------------------------
`TownLayout2D.CharacterSpriteScale` is a fixed 0.5 `Node2D.Scale` applied to every character
(player/hero/townsfolk) via `CharacterArtRoot()`. The naive story -- "the engine already halves
everything, so drawing a bigger source PNG at the same 0.5 constant will just reveal more detail
at the SAME on-screen size" -- is FALSE for this project's actual rendering setup, and worth
spelling out because it is an easy trap:

  - The whole 2D viewport forces `CanvasItemDefaultTextureFilter = Nearest`
    (`Town2D.cs`'s `WorldViewport`/`ViewportContainer` construction), and every `town2d-hero-*`
    import has `mipmaps/generate=false`. With no mipmaps and a Nearest filter, a sub-1.0 Node2D
    scale is POINT-SAMPLE decimation, not a quality downsample: one output pixel keeps exactly one
    input texel and the other three (at a clean 2:1 ratio) are simply discarded, never blended.
  - So a bigger source PNG under the SAME fixed 0.5 constant does not "reveal more detail at the
    same size" -- on-screen size is `sourcePixels * 0.5`, full stop, so a bigger source PNG lands
    BIGGER on screen, linearly. There is no free lunch from the engine's own scaling step.
  - Verified nothing else hardcodes hero/player pixel size to break by this: `HeroActor2D`,
    `PlayerController2D` and `TownsfolkNpc2D` all derive their feet-offset/pick-radius from the
    RESOLVED texture's own `GetHeight()` at runtime, not a constant, and
    `AssetResolutionCensusTests` only pins that an id resolves, never its dimensions -- so shipping
    a bigger PNG under an existing id is safe to the render path. It is a genuine on-screen SIZE
    change, though, and that is Brian's call, not a "free" quality win -- which is exactly why this
    lives here as a comparison render, not a `godot/assets/art/` commit.

THIS SCRIPT: a clean, silhouette-preserving 2x upscale of the ALREADY re-shaded bodies in
`tools/art/gen_town_sprites.py` (imported directly, not re-authored -- one quality bar, one source
of truth), landing at 40x72. Chosen over an from-scratch bigger redraw for the same reason
`gen_town_sprites.py`'s own quality pass was a recolor and not a redraw: it is provably
silhouette-identical (nearest-neighbor 2x turns every source pixel into a fixed 2x2 block, so it
cannot introduce a new gap or misalign base/step), and it gives a REAL, demonstrable legibility win
independent of the size question -- every single-pixel accent (the vanguard's rune glyph, the
coolant trace, the visor slit) was one texel wide at 20x36 and becomes a solid 2x2 patch at 40x72,
which is more likely to survive whatever sub-pixel alignment the engine's Nearest sampling lands on
than a 1-texel accent ever was.

WHY THIS IS NOT COMMITTED ART
------------------------------
Shipping this under the real `town2d-hero-*` ids would make heroes ~2x their current on-screen
size (10x18 -> 20x36 virtual px, i.e. roughly 1.1 tiles tall today vs ~2.25 tiles tall here --
noticeably TALLER than the player's own 15x23px/~1.4-tile figure, the exact "nearly three tiles
tall" proportion problem `CharacterSpriteScale`'s own doc comment records as the bad state it was
introduced to fix). That is a size call only Brian's eyes should make, so this writes to
`art/pipeline/candidates/` (gitignored scratch, per this pipeline's own README) rather than
`godot/assets/art/`. `--install` below exists ONLY to stage a temporary in-game receipt screenshot
for comparison; `python tools/art/gen_town_sprites.py` (no flags) restores the shipped Candidate A
files afterward -- see the hero-sprite-quality PR description for the exact before/candidate-a/
candidate-b receipt sequence this was used for.

Usage:
    python art/pipeline/gen-heroes-hd.py             # renders 6 PNGs into candidates/ (2x, 40x72)
    python art/pipeline/gen-heroes-hd.py --install    # also copies them over the real
                                                       # godot/assets/art/town2d-hero-*.png ids,
                                                       # for a receipt.ps1 comparison capture ONLY
"""
from __future__ import annotations

import argparse
import importlib.util
import pathlib
import shutil
import sys

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parents[2]
SOURCE_SCRIPT = REPO / "tools" / "art" / "gen_town_sprites.py"
CANDIDATES_DIR = pathlib.Path(__file__).resolve().parent / "candidates"
SHIP_DIR = REPO / "godot" / "assets" / "art"
SCALE = 2


def load_source_module():
    """Import tools/art/gen_town_sprites.py by path so PALETTE/SPRITES/WIDTH/HEIGHT come from the
    ONE place the quality-pass grids are authored -- never a second, driftable copy of them."""
    spec = importlib.util.spec_from_file_location("gen_town_sprites", SOURCE_SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def render_grid(grid: list[str], palette: dict, width: int, height: int) -> Image.Image:
    image = Image.new("RGBA", (width, height), palette["."])
    pixels = image.load()
    for y, row in enumerate(grid):
        for x, char in enumerate(row):
            pixels[x, y] = palette[char]
    return image


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--install", action="store_true",
                     help="also copy the rendered PNGs over the real town2d-hero-* ids in "
                          "godot/assets/art/, for a temporary receipt.ps1 comparison capture")
    args = ap.parse_args()

    mod = load_source_module()
    CANDIDATES_DIR.mkdir(parents=True, exist_ok=True)

    for name, grid in mod.SPRITES.items():
        base = render_grid(grid, mod.PALETTE, mod.WIDTH, mod.HEIGHT)
        hd = base.resize((mod.WIDTH * SCALE, mod.HEIGHT * SCALE), Image.NEAREST)
        out_path = CANDIDATES_DIR / f"{name}.png"
        hd.save(out_path)
        print(f"wrote {out_path} ({hd.width}x{hd.height})")

        if args.install:
            ship_path = SHIP_DIR / f"{name}.png"
            shutil.copyfile(out_path, ship_path)
            print(f"  installed over {ship_path} (TEMPORARY -- restore with "
                  f"'python tools/art/gen_town_sprites.py')")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
