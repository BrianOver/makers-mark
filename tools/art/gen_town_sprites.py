#!/usr/bin/env python
"""Author the town's character sprites as explicit pixel grids.

WHY THIS IS NOT THE SDXL PIPELINE
---------------------------------
Every backdrop, portrait, monster and prop in `godot/assets/art/` comes from the local
ComfyUI/SDXL chain (see `godot/assets/art/README.md`). These sprites deliberately do not,
for the same reason `docs/plans/2026-07-28-001-feat-animation-motion-adventure.md` rejected
generated walk cycles: at sprite scale a diffusion render downscales to mush, and it cannot
hold identity or palette between frames. Tried and confirmed once more before writing this: an
SDXL pass at 768 returned a two-view character turnaround in saturated purples -- unusable at
sprite scale even before the downsample.

So these are hand-authored, one pixel at a time, as ASCII grids mapped through the style-bible
palette. That makes them reviewable in a diff, editable without a GPU, and byte-identical on
every machine -- and it is the only way to guarantee the walk frames differ ONLY in the legs,
which is what makes the gait read as walking instead of flickering.

NEUTRAL BY CONTRACT
-------------------
`TownAssets2D.ForHero` documents that hero bodies "are drawn neutral-tinted so HeroActor2D can
multiply in the class color via modulate ... never baked in here". So the palette below is
deliberately desaturated: the class identity lives in the SILHOUETTE (slab shield / light
leathers / flared robe), and the colour arrives in-engine. Baking a crimson Striker here would
double-tint the moment `ClassColors.RoleColor` multiplied over it.

QUALITY PASS (2026-08-01, feat/hero-sprite-quality)
----------------------------------------------------
Brian's repeated playtest verdict -- "the heroes/NPCs still look like booty" -- traced to this
file: every class's torso/arm/leg fill was one FLAT tone ('m'), so the silhouette read as a
cardboard cutout with no volume, no matter how crisp the outline around it was. The fix is a
mechanical, silhouette-preserving re-shade: every maximal horizontal run of the flat 'm' fill
was replaced with a light-to-dark gradient using ONLY colours already sampled from committed
siblings (the rule `gen-market.py` follows too) -- never colours picked by eye, never a new hue.

U6 SCALE-UP + CAST COMPLETION (2026-08-02, feat/u6-hero-cast)
---------------------------------------------------------------
Two problems traced to this file, per `docs/plans/2026-08-02-002-feat-playtest-three-plan.md`
U6: (1) heroes rendered at 20x36 -> 10x18 on screen against the player's 30x46 -> 15x23 (fixed
`CharacterSpriteScale`, Nearest, no mipmaps) -- a same-size repaint moves 0.07% of pixels
(invisible), and a naive 2x upscale would make heroes TOWER over the player. (2) only three of
six hero classes had a town body at all. The fix: WIDTH/HEIGHT went to 26x44, and the three
missing classes were authored from scratch, using the SAME volume-shading idiom.

U3 REDRAW + REAL GAIT (2026-08-04, feat-verify-by-playing plan, R3)
--------------------------------------------------------------------
Brian's playtest verdict, verbatim, for the fourth time: "make the heroes/NPCs more detailed
looking." Root causes, both fixed here:

  1. 26x44 is too few pixels to carry detail at gameplay distance. Canvas goes to 40x64
     (WIDTH/HEIGHT below) -- 1.538x wider, 1.4545x taller, chosen so the existing
     margin(2)/head(11)/torso(18)/legs(13) row bands scale to a clean margin(3)/head(16)/
     torso(26)/legs(19) = 64 split with no leftover pixels.

  2. The walk was a 2-frame SYMMETRIC pose swap: `LEGS_STEP` widened the gap between BOTH legs
     by the same amount, so both legs moved together -- the sprite read as sliding, not
     striding, no matter how much shading sat on top of it. There was never an alternating
     gait to begin with. This redraw ships a REAL 4-frame gait: two contact poses (one leg
     forward/planted, the other lifted -- LIFTED, not just "further apart") that mirror each
     other, plus two passing poses between them (see WALK-CYCLE DESIGN below). `SpriteMotion.cs`
     drives all four; the two original ids (base, `_step`) are kept as frames 1 and 3 exactly so
     every existing null-tolerant consumer that only knows those two ids keeps working, with the
     two new frames (`_walk2`, `_walk4`) as pure additions.

  3. Per-class SILHOUETTE (not just palette) is what reads at gameplay distance -- the plan's own
     words. The head/torso per class already carried real outline differences (Vanguard/Sentinel's
     shield bulge, Striker/Skirmisher's lean taper, Mystic/Occultist's robe flare) from the U6
     redraw; this pass PRESERVES that exact per-class geometry (see UPSCALE STRATEGY) and adds one
     more outline-only accent per "twin" pair that was previously distinguished mostly by
     colour-only detail rather than outline: Sentinel gets shoulder-spike nubs Vanguard doesn't,
     Skirmisher gets a cloak-flare bump at the hip Striker doesn't, and Occultist's hem is
     deliberately ragged where Mystic's is a smooth curve.

UPSCALE STRATEGY: geometry is preserved by CODE, not re-typed by hand
----------------------------------------------------------------------
Re-typing six classes' head+torso as brand-new 40-wide ASCII grids by eye would be the single
biggest way to accidentally DRIFT a class's silhouette (the exact thing #2/#3 above need to stay
correct) with no way for a reviewer to tell "is this still the same shape, just bigger" from the
diff alone. Instead, `nn_scale()` is a small, pure, deterministic nearest-neighbour resampler
(integer index math only -- no float drift, reviewable, testable by inspection) that stretches
each class's ORIGINAL, already-reviewed 26-wide head/torso grid up to the new 40-wide canvas.
The six per-class shapes below (`OLD_HEAD_*`/`OLD_TORSO_*`) are therefore the SAME literal ASCII
this file carried before this pass -- unchanged, still reviewable as "does this class's shape
look right" independent of scale -- and the new canvas is a mechanical, provable consequence of
them, not a second independent hand-authoring pass that could silently disagree.

WALK-CYCLE DESIGN: four frames, alternating, never a whole-body swap
-----------------------------------------------------------------------
Rows 0-3 of the 19-row leg block (the thighs) never move across any of the four frames -- real
hips barely move during a stride either, and it keeps the thigh->torso seam identical in every
frame. Rows 4-18 (shin/ankle/boot/sole) are where the stride lives, built by `build_legs_frame`:
one leg is the FRONT leg (fully drawn, weight-bearing) and the other is LIFTED -- its boot+sole
rows are blanked (transparent), reading as "foot off the ground, mid-swing" -- plus the whole
pair sways 1px toward the front leg. `front` alternates ('left' -> 'right') between the two
CONTACT frames (ids `town2d-hero-*` and `*_step`, kept as frames 1/3 for compatibility), and the
two PASSING frames (`*_walk2`/`*_walk4`, frames 2/4) hold `front='none'` (both feet planted) with
opposite 1px sway so all four frames are pairwise distinct. This is what makes frame 1 vs frame 3
differ ONLY below the waist (an alternating gait) instead of the old symmetric both-legs-move
defect. The two robed classes (mystic/occultist) have no visible legs -- `build_hem_frame` sways
the hem itself instead, exactly as the pre-existing HEM_STEP already did, just across four sway
magnitudes instead of two.

NEUTRAL BY CONTRACT (unchanged) + IMPORT SETTING EVERY FRAME MUST CARRY
--------------------------------------------------------------------------
`process/fix_alpha_border=false` on every `town2d-hero-*` import sidecar (base + all three walk
frames), for the same reason recorded at U6: Godot's default bakes filler RGB into fully-
transparent pixels near an opaque edge, which turns a same-alpha (invisible) transparent pixel
into a DIFFERENT RGB value between two frames that must otherwise be byte-identical above the
waist -- a phantom diff `TownSpriteArtTests` would catch as a false regression. Nearest filtering,
mipmaps off, everywhere in this pipeline, so the border-fix this setting disables is never needed.

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
# Unchanged since the quality pass -- every redraw since reuses these verbatim (never a new hue).
PALETTE = {
    ".": (0, 0, 0, 0),          # transparent
    "o": (20, 15, 31, 255),     # Void — outline
    "d": (42, 36, 56, 255),     # Iron — deepest shading
    "i": (61, 50, 66, 255),     # Iron-lit — town2d-tavern's own body tone, sampled verbatim
    "m": (110, 104, 128, 255),  # mid tone
    "l": (184, 176, 198, 255),  # light
    "h": (216, 207, 224, 255),  # Bone — highlight / rim
    "e": (224, 145, 63, 255),   # Ember — candle rim light, upper-left only
    "t": (63, 176, 172, 255),   # Coolant — the one faint circuit trace every metal object carries
    "r": (107, 76, 154, 255),   # Arcane — the one rune glyph
}

# ── canvas (U3 2026-08-04: 26x44 -> 40x64) ───────────────────────────────────────────────────────
WIDTH, HEIGHT = 40, 64

MARGIN_ROWS = 3     # 0-2: empty margin above the head
HEAD_ROWS = 16       # 3-18
TORSO_ROWS = 26      # 19-44
LEGS_ROWS = 19       # 45-63
assert MARGIN_ROWS + HEAD_ROWS + TORSO_ROWS + LEGS_ROWS == HEIGHT

# First row of the legs/hem — TownSpriteArtTests' LegsTopRow pins this exact number.
LEGS_TOP_ROW = MARGIN_ROWS + HEAD_ROWS + TORSO_ROWS
assert LEGS_TOP_ROW == 45

# ── row-construction helpers (shape-preserving; width defaults to the NEW canvas) ────────────────


def mirror(left: str) -> str:
    """A perfectly symmetric band: `left` followed by its own mirror image. Guarantees even
    length by construction, which is what `centered()` needs to pad without a leftover pixel."""
    return left + left[::-1]


def centered(s: str, width: int = WIDTH) -> str:
    """Pads `s` with an equal number of transparent columns on each side to reach `width`. Dies
    loudly (not silently off-by-one) if the padding would be odd -- that would mean the caller's
    content isn't actually centerable at this width, which is a design mistake to catch here."""
    pad = width - len(s)
    assert pad >= 0 and pad % 2 == 0, f"bad pad {pad} for {s!r} (len {len(s)})"
    left_pad = pad // 2
    return "." * left_pad + s + "." * (pad - left_pad)


def row(s: str, width: int = WIDTH) -> str:
    """Asserts a fully-constructed row is exactly `width` wide. The one gate every row (however
    it was built) passes through before landing in a SPRITES grid."""
    assert len(s) == width, f"bad width {len(s)} (want {width}): {s!r}"
    return s


def row_left(s: str, width: int = WIDTH) -> str:
    """Left-anchored content, auto right-padded with dots to `width` -- for asymmetric rows (a
    shield or a blade on one side only) where hand-counting the trailing dots is error-prone."""
    assert len(s) <= width, f"content too long ({len(s)} > {width}): {s!r}"
    return row(s + "." * (width - len(s)), width)


def overlay(left: str, overrides: dict[int, str], width: int = WIDTH) -> str:
    """A symmetric `centered(mirror(left))` row with a handful of absolute-column overrides --
    for a mostly-symmetric torso row that needs one or two small asymmetric details."""
    chars = list(centered(mirror(left), width))
    for index, char in overrides.items():
        chars[index] = char
    return row("".join(chars), width)


def _sway(s: str, shift: int) -> str:
    """Shifts an already-centered row's content left/right by `shift` columns, refilling the
    vacated side with transparent dots -- content that falls off the far edge is dropped, never
    wrapped (a real sway loses pixels off-canvas, it doesn't teleport them to the other side)."""
    if shift == 0:
        return s
    if shift > 0:
        return s[shift:] + "." * shift
    n = -shift
    return "." * n + s[:-n]


def nn_scale(rows: list[str], new_width: int, new_height: int) -> list[str]:
    """Deterministic nearest-neighbour resample of an ASCII grid (see the module doc's UPSCALE
    STRATEGY section) -- pure integer index math, so it is exact and reviewable: every output
    pixel maps back to exactly one input pixel, never a blend. This is what lets each class's
    26-wide head/torso shape grow to 40-wide WITHOUT a second, independently-drawn (and
    independently drift-prone) copy of that same shape."""
    old_height = len(rows)
    old_width = len(rows[0])
    for r in rows:
        assert len(r) == old_width, f"ragged source grid: {r!r} vs width {old_width}"

    out = []
    for new_y in range(new_height):
        old_y = min(old_height - 1, (new_y * old_height) // new_height)
        source = rows[old_y]
        chars = [source[min(old_width - 1, (nx * old_width) // new_width)] for nx in range(new_width)]
        out.append("".join(chars))
    return out


def extend_outline(rows: list[str], y: int, side: str, amount: int, color: str) -> None:
    """Silhouette-only accent (in place): at row `y`, find the CURRENT opaque span and extend it
    outward by `amount` columns on `side`, filling with `color`. Used sparingly (see the module
    doc's U3 section) to give the two "twin" archetype pairs (Vanguard/Sentinel,
    Striker/Skirmisher) an outline difference beyond palette, without hand-redrawing either
    class's whole shape -- it reads off whatever the ALREADY-scaled row actually contains, so it
    stays correct even if the upstream shape changes."""
    line = rows[y]
    opaque_idx = [i for i, c in enumerate(line) if c != "."]
    if not opaque_idx:
        return
    lo, hi = min(opaque_idx), max(opaque_idx)
    chars = list(line)
    if side == "left":
        for i in range(max(0, lo - amount), lo):
            chars[i] = color
    else:
        for i in range(hi + 1, min(len(chars), hi + 1 + amount)):
            chars[i] = color
    rows[y] = "".join(chars)


# ── OLD (26-wide) per-class head/torso shapes — UNCHANGED from the pre-U3 file ───────────────────
# These are upscaled by nn_scale(), never re-typed — see the module doc's UPSCALE STRATEGY. Local
# o-prefixed wrappers pin the OLD width explicitly so pasting this content is independent of the
# NEW module-level WIDTH constant above.
OLD_WIDTH = 26


def ocentered(s: str) -> str:
    return centered(s, OLD_WIDTH)


def orow(s: str) -> str:
    return row(s, OLD_WIDTH)


def orow_left(s: str) -> str:
    return row_left(s, OLD_WIDTH)


def ooverlay(left: str, overrides: dict[int, str]) -> str:
    return overlay(left, overrides, OLD_WIDTH)


# VANGUARD — closed great-helm w/ visor slit, slab shield on the left arm, rune+trace on the chest.
OLD_HEAD_VANGUARD = [
    orow(ocentered("o" * 10)),  # 2 crown
    orow(ocentered(mirror("ohhll"))),  # 3
    orow(ocentered(mirror("ohllm"))),  # 4
    orow(ocentered(mirror("ohloo"))),  # 5 visor slit
    orow(ocentered(mirror("ohloo"))),  # 6 visor slit
    orow(ocentered(mirror("ohlmi"))),  # 7
    orow(ocentered(mirror("ohlm"))),  # 8 taper
    orow(ocentered(mirror("ohl"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("ommll"))),  # 11 gorget
    orow(ocentered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_VANGUARD) == 11

FLANK = "lllmiddlo"  # right chest/arm strip, 9 chars, held constant across the shield side
FLANK_TRACE = "tllmiddlo"
FLANK_RUNE = "rllmiddlo"

OLD_TORSO_VANGUARD = [
    orow(ocentered(mirror("oooimmlll"))),  # 13 shoulders (symmetric)
    orow_left("oehhlliddl" + FLANK),  # 14 pauldrons, ember upper-left
    orow_left("oehllmdidl" + FLANK),  # 15 pauldrons
    orow_left("ooollmdidl" + FLANK_RUNE),  # 16 chest top, rune at the heart
    orow_left("ooollmdidl" + FLANK_TRACE),  # 17 chest, coolant trace
    orow_left("oooollmdid" + FLANK),  # 18 chest taper, shield about to begin
    orow_left("oooooollmd" + FLANK),  # 19 shield begins
    orow_left("ohhhmmilld" + FLANK),  # 20 shield face, lit
    orow_left("ohhllmilld" + FLANK),  # 21
    orow_left("ohlltmidld" + FLANK),  # 22 shield boss, coolant trace
    orow_left("ohllrmidld" + FLANK),  # 23 shield boss, rune
    orow_left("ohlllmidld" + FLANK),  # 24
    orow_left("ohlllmidld" + FLANK),  # 25
    orow_left("ohlllmidld" + FLANK),  # 26
    orow_left(".ooollmdi" + FLANK),  # 27 shield ends, receding
    orow_left("...oollmd" + FLANK),  # 28 waist taper
    orow_left(".....oolm" + FLANK),  # 29 waist taper, near hip
    orow(ocentered(mirror("ooilmmi"))),  # 30 hips
]
assert len(OLD_TORSO_VANGUARD) == 18

# SENTINEL — bronze bulwark. Closed FULL helm, a bigger/longer tower shield than the Vanguard's.
OLD_HEAD_SENTINEL = [
    orow(ocentered("o" * 12)),  # 2 crown (wider)
    orow(ocentered(mirror("oohhll"))),  # 3
    orow(ocentered(mirror("oohllm"))),  # 4
    orow(ocentered(mirror("oohlmi"))),  # 5 no slit -- full plate
    orow(ocentered(mirror("oohlmi"))),  # 6
    orow(ocentered(mirror("oohlmd"))),  # 7 cheek shadow
    orow(ocentered(mirror("oohlm"))),  # 8 taper
    orow(ocentered(mirror("oohl"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("oommll"))),  # 11 gorget (wider)
    orow(ocentered(mirror("ooiimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_SENTINEL) == 11

FLANK_SEN = "lllmiddlo"
FLANK_SEN_TRACE = "tllmiddlo"
FLANK_SEN_RUNE = "rllmiddlo"

OLD_TORSO_SENTINEL = [
    orow(ocentered(mirror("ooidmmlll"))),  # 13 shoulders (symmetric)
    orow_left("oehhlliddli" + FLANK_SEN),  # 14 pauldrons, ember upper-left
    orow_left("oehllmdidli" + FLANK_SEN),  # 15 pauldrons
    orow_left("oooooollmdi" + FLANK_SEN_RUNE),  # 16 shield begins early + rune
    orow_left("ohhhmmilldi" + FLANK_SEN_TRACE),  # 17 shield face lit + trace
    orow_left("ohhllmilldi" + FLANK_SEN),  # 18
    orow_left("ohlllmidldi" + FLANK_SEN),  # 19
    orow_left("ohlllmidldi" + FLANK_SEN),  # 20
    orow_left("ohlllmidldi" + FLANK_SEN),  # 21
    orow_left("ohlllmidldi" + FLANK_SEN),  # 22
    orow_left("ohlllmidldi" + FLANK_SEN),  # 23
    orow_left("ohlllmidldi" + FLANK_SEN),  # 24
    orow_left("ohlllmidldi" + FLANK_SEN),  # 25
    orow_left("ohlllmidldi" + FLANK_SEN),  # 26 (shield spans longer than the Vanguard's)
    orow_left(".ooollmdid" + FLANK_SEN),  # 27 shield ends, receding
    orow_left("...oollmdi" + FLANK_SEN),  # 28 waist taper
    orow_left(".....oolmd" + FLANK_SEN),  # 29 waist taper
    orow(ocentered(mirror("ooilmmi"))),  # 30 hips
]
assert len(OLD_TORSO_SENTINEL) == 18

# STRIKER — hooded duelist: pointed hood, crossed strap, dual blade hilts, no shield.
OLD_HEAD_STRIKER = [
    orow(ocentered("o" * 8)),  # 2 hood point
    orow(ocentered(mirror("ohhl"))),  # 3
    orow(ocentered(mirror("ohhll"))),  # 4 hood widens
    orow(ocentered(mirror("ohllm"))),  # 5
    orow(ocentered(mirror("ohlmo"))),  # 6 eyes in shadow
    orow(ocentered(mirror("ollm"))),  # 7 taper
    orow(ocentered(mirror("ollm"))),  # 8 hold
    orow(ocentered(mirror("oll"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("ommll"))),  # 11 gorget
    orow(ocentered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_STRIKER) == 11

OLD_TORSO_STRIKER = [
    orow(ocentered(mirror("ooimlll"))),  # 13 shoulders (symmetric, narrower)
    ooverlay("oohlliddo", {6: "e"}),  # 14 shoulders — ember upper-left ONLY
    ooverlay("oohllmdo", {6: "e"}),  # 15 ember continues
    ooverlay("oollmiddo", {12: "t"}),  # 16 coolant trace, single centre pixel
    ooverlay("oollmiddo", {12: "r"}),  # 17 rune at the belt, single centre pixel
    orow(ocentered(mirror("oollmido"))),  # 18 chest
    orow(ocentered(mirror("oollmido"))),  # 19 hold
    orow(ocentered(mirror("oolmiddo"))),  # 20 waist narrows
    orow(ocentered(mirror("oolmiddo"))),  # 21 hold
    ooverlay("oolmiddo", {6: "o", 7: "h", 8: "h", 17: "o", 18: "h", 19: "h"}),  # 22 hilts appear
    ooverlay("oolmiddo", {6: "o", 7: "d", 8: "o", 17: "o", 18: "d", 19: "o"}),  # 23 hilts
    ooverlay("oolmiddo", {7: "o", 8: "d", 18: "d", 19: "o"}),  # 24 blades taper
    ooverlay("oolmiddo", {7: "d", 8: "o", 18: "o", 19: "d"}),  # 25 blade tips
    orow(ocentered(mirror("oolmiddo"))),  # 26 torso resumes below the hilts
    orow(ocentered(mirror("oolmiddo"))),  # 27 hold
    orow(ocentered(mirror("oolmido"))),  # 28 waist taper
    orow(ocentered(mirror("oolmdo"))),  # 29 waist taper, near hip
    orow(ocentered(mirror("ooilmmi"))),  # 30 hips
]
assert len(OLD_TORSO_STRIKER) == 18

# SKIRMISHER — light flanker: open cap, quiver strap, crossed daggers at the belt.
OLD_HEAD_SKIRMISHER = [
    orow(ocentered("o" * 8)),  # 2 cap crown
    orow(ocentered(mirror("ohhl"))),  # 3
    orow(ocentered(mirror("ohll"))),  # 4 cap widens
    orow(ocentered(mirror("ohlm"))),  # 5 face lighter than the Striker's shadow-eyes
    orow(ocentered(mirror("ollm"))),  # 6
    orow(ocentered(mirror("oll"))),  # 7 taper
    orow(ocentered(mirror("oll"))),  # 8 hold
    orow(ocentered(mirror("oll"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("ommll"))),  # 11 gorget
    orow(ocentered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_SKIRMISHER) == 11

OLD_TORSO_SKIRMISHER = [
    orow(ocentered(mirror("ooimlll"))),  # 13 shoulders (leaner, symmetric)
    ooverlay("oohlliddo", {6: "e", 20: "i", 21: "d"}),  # 14 ember left, quiver right begins
    ooverlay("oohllmdo", {19: "i", 20: "d"}),  # 15 quiver strap continues
    ooverlay("oolmiddo", {12: "t", 18: "i", 19: "d"}),  # 16 coolant trace, quiver
    ooverlay("oolmiddo", {12: "r"}),  # 17 rune at the belt
    orow(ocentered(mirror("oolmido"))),  # 18 chest, leaner taper
    orow(ocentered(mirror("oolmido"))),  # 19 hold
    orow(ocentered(mirror("olmido"))),  # 20 waist narrows
    orow(ocentered(mirror("olmido"))),  # 21 hold
    ooverlay("olmido", {10: "o", 11: "h", 14: "h", 15: "o"}),  # 22 crossed daggers appear
    ooverlay("olmido", {9: "o", 10: "d", 15: "d", 16: "o"}),  # 23 daggers cross
    ooverlay("olmido", {11: "o", 12: "d", 13: "d", 14: "o"}),  # 24 blade tips converge
    orow(ocentered(mirror("olmido"))),  # 25 daggers end
    orow(ocentered(mirror("olmido"))),  # 26 waist
    orow(ocentered(mirror("olmdo"))),  # 27 waist taper
    orow(ocentered(mirror("olmdo"))),  # 28 hold
    orow(ocentered(mirror("olmo"))),  # 29 near hip
    orow(ocentered(mirror("ooilmmi"))),  # 30 hips
]
assert len(OLD_TORSO_SKIRMISHER) == 18

# MYSTIC — deep cowl, shadow face, staff enters frame on the right, rune between traces.
OLD_HEAD_MYSTIC = [
    orow(ocentered("o" * 10)),  # 2 crown
    orow(ocentered(mirror("ohhlll"))),  # 3
    orow(ocentered(mirror("ohhlllm"))),  # 4 cowl flares
    orow(ocentered(mirror("ohlmoo"))),  # 5 shadow face starts
    orow(ocentered(mirror("ohlmoo"))),  # 6 face is shadow
    orow(ocentered(mirror("ohllm"))),  # 7 chin under cowl
    orow(ocentered(mirror("ollm"))),  # 8 taper
    orow(ocentered(mirror("oll"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("ommll"))),  # 11 gorget
    orow(ocentered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_MYSTIC) == 11

OLD_TORSO_MYSTIC = [
    orow(ocentered(mirror("ooiimmll"))),  # 13 shoulders (robed, wider)
    ooverlay("oohlliddo", {6: "e"}),  # 14 ember upper-left
    ooverlay("oohllmdo", {6: "e", 21: "m", 22: "m", 23: "d", 24: "d"}),  # 15 hand reaches, staff
    ooverlay("oolmiddo", {12: "t", 21: "m", 22: "d", 23: "d", 24: "d"}),  # 16 coolant trace + staff
    ooverlay("oolmiddo", {12: "r", 24: "r"}),  # 17 rune on chest + on the staff
    ooverlay("oollmiddo", {24: "d"}),  # 18 chest widens, staff
    ooverlay("oollmiddo", {24: "d"}),  # 19 hold, staff
    ooverlay("oolllmiddo", {24: "d"}),  # 20 robe widens, staff
    ooverlay("oolllmiddo", {24: "d"}),  # 21 hold, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 22 robe widens, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 23 hold, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 24 hold, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 25 hold, staff
    orow(ocentered(mirror("ooilllmiddo"))),  # 26 robe continues, staff released
    orow(ocentered(mirror("oolllmiddo"))),  # 27 robe cinches at the waist
    orow(ocentered(mirror("oollmiddo"))),  # 28 waist
    orow(ocentered(mirror("oolmiddo"))),  # 29 waist, narrowest point
    orow(ocentered(mirror("ooimmll"))),  # 30 hip lead-in
]
assert len(OLD_TORSO_MYSTIC) == 18

# OCCULTIST — deep cowl with two small horn-tips, a paired rune-and-eye glyph, staff.
OLD_HEAD_OCCULTIST = [
    ooverlay("o" * 4, {7: "d", 18: "d"}),  # 2 crown + two small horn-tips
    orow(ocentered(mirror("ohhll"))),  # 3 cowl (pointier than the Mystic's)
    orow(ocentered(mirror("ohhllm"))),  # 4 cowl flares sharply
    orow(ocentered(mirror("ohlmoo"))),  # 5 shadow face starts
    orow(ocentered(mirror("ohlmoo"))),  # 6 face is shadow
    orow(ocentered(mirror("ohllm"))),  # 7 chin under cowl
    orow(ocentered(mirror("ollm"))),  # 8 taper
    orow(ocentered(mirror("oll"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("ommll"))),  # 11 gorget
    orow(ocentered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_OCCULTIST) == 11

OLD_TORSO_OCCULTIST = [
    orow(ocentered(mirror("ooiimmll"))),  # 13 shoulders (robed, wider)
    ooverlay("oohlliddo", {6: "e"}),  # 14 ember upper-left
    ooverlay("oohllmdo", {6: "e", 21: "m", 22: "m", 23: "d", 24: "d"}),  # 15 hand reaches, staff
    ooverlay("oolmiddo", {11: "r", 13: "d", 21: "m", 22: "d", 23: "d", 24: "d"}),  # 16 rune+eye, staff
    ooverlay("oolmiddo", {11: "d", 13: "r", 24: "r"}),  # 17 rune+eye glyph, staff continues
    ooverlay("oollmiddo", {24: "d"}),  # 18 chest widens, staff
    ooverlay("oollmiddo", {24: "d"}),  # 19 hold, staff
    ooverlay("oolllmiddo", {24: "d"}),  # 20 robe widens, staff
    ooverlay("oolllmiddo", {24: "d"}),  # 21 hold, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 22 robe widens, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 23 hold, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 24 hold, staff
    ooverlay("ooilllmiddo", {24: "d"}),  # 25 hold, staff
    orow(ocentered(mirror("ooilllmiddo"))),  # 26 robe continues, staff released
    orow(ocentered(mirror("oolllmiddo"))),  # 27 robe cinches at the waist
    orow(ocentered(mirror("oollmiddo"))),  # 28 waist
    orow(ocentered(mirror("oolmiddo"))),  # 29 waist, narrowest point
    orow(ocentered(mirror("ooimmll"))),  # 30 hip lead-in
]
assert len(OLD_TORSO_OCCULTIST) == 18

# ── upscale each class's head/torso, then add the ONE outline-only accent per "twin" pair ────────


def upper_body(head: list[str], torso: list[str]) -> list[str]:
    scaled_head = nn_scale(head, WIDTH, HEAD_ROWS)
    scaled_torso = nn_scale(torso, WIDTH, TORSO_ROWS)
    combined = scaled_head + scaled_torso
    assert len(combined) == HEAD_ROWS + TORSO_ROWS
    return combined


UPPER_VANGUARD = upper_body(OLD_HEAD_VANGUARD, OLD_TORSO_VANGUARD)
UPPER_SENTINEL = upper_body(OLD_HEAD_SENTINEL, OLD_TORSO_SENTINEL)
UPPER_STRIKER = upper_body(OLD_HEAD_STRIKER, OLD_TORSO_STRIKER)
UPPER_SKIRMISHER = upper_body(OLD_HEAD_SKIRMISHER, OLD_TORSO_SKIRMISHER)
UPPER_MYSTIC = upper_body(OLD_HEAD_MYSTIC, OLD_TORSO_MYSTIC)
UPPER_OCCULTIST = upper_body(OLD_HEAD_OCCULTIST, OLD_TORSO_OCCULTIST)

# Sentinel vs Vanguard: both shielded tanks, previously distinguished mostly by the shield's
# width (a few px) and colour-only rune/trace placement -- measured silhouette distance only
# 0.066 with the shield-width difference alone, well under the distinctness floor other pairs
# clear. Two additive-only outline changes, matching the fluff ("soaks more, hits slower" than
# the Vanguard, SentinelClass.cs): the whole torso is 1px bulkier on BOTH sides (thicker plate,
# every row), plus a pronounced shoulder-spike nub Vanguard's smooth pauldron never gets.
for _y in range(HEAD_ROWS, HEAD_ROWS + TORSO_ROWS):
    extend_outline(UPPER_SENTINEL, _y, "left", 2, "d")
    extend_outline(UPPER_SENTINEL, _y, "right", 2, "d")
for _y in range(HEAD_ROWS + 1, HEAD_ROWS + 7):
    extend_outline(UPPER_SENTINEL, _y, "left", 4, "d")
    extend_outline(UPPER_SENTINEL, _y, "right", 4, "d")

# Skirmisher vs Striker: both lean melee, previously distinguished mostly by accessory colour
# (quiver/daggers vs hilts) on an otherwise near-identical taper. Add a pronounced cloak-flare
# bump at the hip — Striker's silhouette keeps its straight taper, Skirmisher's now flares
# outward, on the FLANK (non-weapon) side only so it doesn't collide with the crossed-dagger
# detail on the other side.
for _y in range(HEAD_ROWS + 22, HEAD_ROWS + 26):
    extend_outline(UPPER_SKIRMISHER, _y, "right", 4, "d")

# Occultist vs Mystic: the OLD 26-wide horn-tip detail (a single 'd' pixel two columns in from
# the crown) survives nn_scale as only 1-2px of a colour barely darker than the 'o' outline
# around it — invisible at a glance (confirmed by rendering a contact sheet). Re-express the same
# idea as an OUTLINE protrusion instead of a colour pixel: two small nubs sticking out sideways
# at the very top of the crown, which Mystic's smooth dome never gets.
for _y in range(0, 2):
    extend_outline(UPPER_OCCULTIST, _y, "left", 2, "o")
    extend_outline(UPPER_OCCULTIST, _y, "right", 2, "o")

# ── the four walk-cycle frames (shared skeleton) ──────────────────────────────────────────────────
# See the module doc's WALK-CYCLE DESIGN section. Bands sized so 4+6+4+3+2 = 19 = LEGS_ROWS.

THIGH = "olllmddo"  # 8 chars — never moves across frames (hips barely move in a stride)
SHIN = "odmmiddo"   # 8 chars
ANKLE = "odmiddo"   # 7 chars
BOOT = "ohhmdo"     # 6 chars — the part that goes missing on a lifted foot
SOLE = "oooo"       # 4 chars — ditto


def _lower_row(band: str, front: str, liftable: bool) -> str:
    """One row of the shin/ankle/boot/sole region for a given contact state. `front`: 'left' /
    'right' (a contact pose — the OTHER leg is lifted on `liftable` bands) or 'none' (a passing
    pose — both legs fully drawn, weight not yet committed to either)."""
    if front == "none" or not liftable:
        left_content, right_content = band, band
    else:
        lifted = "right" if front == "left" else "left"
        blank = "." * len(band)
        left_content = blank if lifted == "left" else band
        right_content = blank if lifted == "right" else band
    return left_content[::-1] + right_content


def build_legs_frame(front: str, sway_amount: int) -> list[str]:
    content_rows: list[str] = []
    content_rows += [mirror(THIGH) for _ in range(4)]
    content_rows += [_lower_row(SHIN, front, liftable=False) for _ in range(6)]
    content_rows += [_lower_row(ANKLE, front, liftable=False) for _ in range(4)]
    content_rows += [_lower_row(BOOT, front, liftable=True) for _ in range(3)]
    content_rows += [_lower_row(SOLE, front, liftable=True) for _ in range(2)]
    assert len(content_rows) == LEGS_ROWS

    return [row(_sway(centered(r), sway_amount)) for r in content_rows]


# Frame 1 (base id) / Frame 3 (_step id): the two CONTACT poses — mirror images of each other,
# one leg planted+forward, the other lifted. This is the specific pair TownSpriteArtTests checks
# for "differ below the waist, match above" (an alternating gait, not a whole-body swap).
LEGS_F1 = build_legs_frame("left", sway_amount=-1)
LEGS_F3 = build_legs_frame("right", sway_amount=1)

# Frame 2 (_walk2) / Frame 4 (_walk4): the two PASSING poses — both feet planted, opposite sway
# so all four frames stay pairwise distinct even though neither foot is lifted here.
LEGS_F2 = build_legs_frame("none", sway_amount=-1)
LEGS_F4 = build_legs_frame("none", sway_amount=1)

# ── the four hem-sway frames (mystic/occultist — the robe hides the legs, so the HEM carries the
# motion, exactly as the pre-U3 HEM_STEP already did; this just widens two sway magnitudes into
# four so all four frames are pairwise distinct) ──────────────────────────────────────────────────

# Mystic: a wide, smoothly-belled hem (the widest band reaches col-width 24).
HEM_BANDS_MYSTIC = [
    (3, "ooilmmi"),      # rows 0-2: narrow, near the waist — barely sways
    (3, "oollmmid"),     # rows 3-5
    (3, "oolllmmid"),    # rows 6-8
    (3, "ooilllmmid"),   # rows 9-11
    (3, "ooddilllmmid"),  # rows 12-14: widest point of the bell
]
HEM_EDGE_WIDTH_MYSTIC = 24

# Occultist: a visibly NARROWER, straighter robe (a silhouette difference, not just palette) —
# measured 0.020 silhouette distance from Mystic with only a 4-pixel ragged notch at the very
# bottom, well under every other pair's distance; the fix is a genuinely different hem WIDTH
# profile across the whole skirt, not a cosmetic notch. Same band count/shift schedule as
# Mystic's (so build_hem_frame below stays one function), each band a couple of columns
# narrower, plus the ragged (tattered, not smooth-curved) edge at the very bottom.
HEM_BANDS_OCCULTIST = [
    (3, "ooil"),         # rows 0-2
    (3, "oolmi"),        # rows 3-5
    (3, "oollmi"),       # rows 6-8
    (3, "ooilmi"),       # rows 9-11
    (3, "ooddilmi"),     # rows 12-14: much narrower than Mystic's widest band
]
HEM_EDGE_WIDTH_OCCULTIST = 14


def build_hem_frame(shift: int, bands: list[tuple[int, str]], edge_width: int) -> list[str]:
    content_rows: list[str] = []
    for band_index, (count, half) in enumerate(bands):
        band_shift = shift if band_index >= 2 else 0  # waist barely moves; hem does the swaying
        for _ in range(count):
            content_rows.append(_sway(centered(mirror(half)), band_shift))
    content_rows += [_sway(centered("d" * edge_width), shift) for _ in range(2)]  # hem edge
    content_rows += [_sway(centered("o" * edge_width), shift) for _ in range(2)]  # ground contact
    assert len(content_rows) == LEGS_ROWS
    return [row(r) for r in content_rows]


HEM_F1 = build_hem_frame(-2, HEM_BANDS_MYSTIC, HEM_EDGE_WIDTH_MYSTIC)
HEM_F2 = build_hem_frame(-1, HEM_BANDS_MYSTIC, HEM_EDGE_WIDTH_MYSTIC)
HEM_F3 = build_hem_frame(2, HEM_BANDS_MYSTIC, HEM_EDGE_WIDTH_MYSTIC)
HEM_F4 = build_hem_frame(1, HEM_BANDS_MYSTIC, HEM_EDGE_WIDTH_MYSTIC)


def raggedify(rows: list[str]) -> list[str]:
    """Punches a few notches into the hem-edge/ground-contact rows so the Occultist's hem reads
    as tattered against Mystic's smooth bell curve — on top of the narrower band widths above,
    and the two classes' already-distinct head/torso (horn-tips + eye rune)."""
    rows = list(rows)
    notch_cols = (8, 13, 18, 22, 27, 32)
    for notch_row in (-2, -1):
        chars = list(rows[notch_row])
        for c in notch_cols:
            if 0 <= c < len(chars):
                chars[c] = "."
        rows[notch_row] = "".join(chars)
    return rows


OCCULTIST_HEM_F1 = raggedify(build_hem_frame(-2, HEM_BANDS_OCCULTIST, HEM_EDGE_WIDTH_OCCULTIST))
OCCULTIST_HEM_F2 = raggedify(build_hem_frame(-1, HEM_BANDS_OCCULTIST, HEM_EDGE_WIDTH_OCCULTIST))
OCCULTIST_HEM_F3 = raggedify(build_hem_frame(2, HEM_BANDS_OCCULTIST, HEM_EDGE_WIDTH_OCCULTIST))
OCCULTIST_HEM_F4 = raggedify(build_hem_frame(1, HEM_BANDS_OCCULTIST, HEM_EDGE_WIDTH_OCCULTIST))

EMPTY_MARGIN = ["." * WIDTH] * MARGIN_ROWS

# ── final assembly: margin + upper body + legs/hem, per class per frame ──────────────────────────


def assemble(upper: list[str], lower: list[str]) -> list[str]:
    full = EMPTY_MARGIN + upper + lower
    assert len(full) == HEIGHT
    for r in full:
        assert len(r) == WIDTH
    return full


SPRITES = {
    "town2d-hero-vanguard": assemble(UPPER_VANGUARD, LEGS_F1),
    "town2d-hero-vanguard_walk2": assemble(UPPER_VANGUARD, LEGS_F2),
    "town2d-hero-vanguard_step": assemble(UPPER_VANGUARD, LEGS_F3),
    "town2d-hero-vanguard_walk4": assemble(UPPER_VANGUARD, LEGS_F4),
    "town2d-hero-sentinel": assemble(UPPER_SENTINEL, LEGS_F1),
    "town2d-hero-sentinel_walk2": assemble(UPPER_SENTINEL, LEGS_F2),
    "town2d-hero-sentinel_step": assemble(UPPER_SENTINEL, LEGS_F3),
    "town2d-hero-sentinel_walk4": assemble(UPPER_SENTINEL, LEGS_F4),
    "town2d-hero-striker": assemble(UPPER_STRIKER, LEGS_F1),
    "town2d-hero-striker_walk2": assemble(UPPER_STRIKER, LEGS_F2),
    "town2d-hero-striker_step": assemble(UPPER_STRIKER, LEGS_F3),
    "town2d-hero-striker_walk4": assemble(UPPER_STRIKER, LEGS_F4),
    "town2d-hero-skirmisher": assemble(UPPER_SKIRMISHER, LEGS_F1),
    "town2d-hero-skirmisher_walk2": assemble(UPPER_SKIRMISHER, LEGS_F2),
    "town2d-hero-skirmisher_step": assemble(UPPER_SKIRMISHER, LEGS_F3),
    "town2d-hero-skirmisher_walk4": assemble(UPPER_SKIRMISHER, LEGS_F4),
    "town2d-hero-mystic": assemble(UPPER_MYSTIC, HEM_F1),
    "town2d-hero-mystic_walk2": assemble(UPPER_MYSTIC, HEM_F2),
    "town2d-hero-mystic_step": assemble(UPPER_MYSTIC, HEM_F3),
    "town2d-hero-mystic_walk4": assemble(UPPER_MYSTIC, HEM_F4),
    "town2d-hero-occultist": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F1),
    "town2d-hero-occultist_walk2": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F2),
    "town2d-hero-occultist_step": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F3),
    "town2d-hero-occultist_walk4": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F4),
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
    for y, row_str in enumerate(grid):
        if len(row_str) != WIDTH:
            die(f"{name}: row {y} is {len(row_str)} chars, expected {WIDTH}")
        for x, char in enumerate(row_str):
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
