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

NEUTRAL BY CONTRACT -- SUPERSEDED 2026-08-04 (see COLOUR + MATERIAL PASS below)
---------------------------------------------------------------------------------
`TownAssets2D.ForHero` used to document that hero bodies "are drawn neutral-tinted so
HeroActor2D can multiply in the class color via modulate ... never baked in here". That was true
through U6: the palette was desaturated, and `HeroActor2D.BuildSprite` multiplied the whole
sprite by `ClassColors.RoleColor` at runtime. The colour + material pass below replaces that --
see its own section for why a single whole-sprite multiply can never give steel and cloth
different material contrast, which is the reason `HeroActor2D`'s modulate is now `White` (see
that file's own U3 comment) instead of `classColor`.

COLOUR + MATERIAL PASS (2026-08-04, same U3 unit, second round)
-------------------------------------------------------------------
The first U3 pass (bigger canvas, real gait, per-class silhouette) shipped with EVERY class in
the same desaturated grey-bone ramp plus one accent pixel each -- reviewed against a contact
sheet and correctly called out as still reading "as one grey figure in six shapes": bigger and
better-shaped is not the same as "looks like a person," and a per-class ACCENT pixel is not a
per-class COLOUR identity. Three changes, in the art itself (not a runtime tint -- see above):

  1. Per-class GARMENT colour, sourced from the same place the rest of the game already gets it:
     `ClassDefinition.ColorRgb` (`sim/GameSim/Classes/**`) -- steel-blue Vanguard, bronze
     Sentinel, crimson Striker, emerald Skirmisher, violet Mystic, dark-violet Occultist. Using
     the SIM's own pinned hue (rather than picking a new one here) means the walking body now
     agrees with every panel/chip that already shows that colour, and gives a documented,
     non-arbitrary source for each choice. `cloth_ramp()` derives a light/mid/dark/deepest ramp
     from each hue by a fixed lerp-toward-white/black formula (deterministic, no eyeballing).

  2. MATERIAL contrast: armour (helm/shield/greaves) stays in the original neutral steel ramp
     (o/d/i/m/l/h) -- unchanged, so it still reads as metal, not tinted cloth. Only the CLOTH
     regions (the flank/gambeson sleeve on shielded classes; the full leather/robe on the other
     four) are re-let onto four new placeholder letters (`c`/`n`/`k`/`w` -- light/mid/dark/
     deepest) that `to_cloth()` mechanically substitutes for their neutral equivalents
     (l->c, m->n, i->k, d->w) in the ASCII BEFORE it is coloured, then a per-class palette (see
     `class_palette()`) fills those four letters with that class's own ramp. Steel and cloth
     therefore differ in BOTH hue and in how hard the light/dark step is (the neutral ramp's
     highlight-to-outline jump stays large and abrupt -- hard specular; the derived cloth ramp's
     four stops are closer together -- soft matte), which is what makes the shading read as two
     different MATERIALS rather than one tone recoloured.

  3. SKIN ('f') + HAIR ('j'), one shared tone each (a fantasy tan and a dark brown -- picked once,
     reused everywhere, same "no eyeballing per class" discipline as the garment ramps): every
     class gets a real skin-tone patch (the single biggest "that's a person" cue per the review),
     placed wherever its established silhouette actually exposes flesh -- a visor-slit cheek
     peek for the two full-helm tanks (no room for more without contradicting "closed helm"),
     full face + hair for the two open-faced classes (Striker/Skirmisher), and a dim glimpse at
     the shadow-hood's edge for the two cowled casters (their faces are deliberately shadowed BY
     DESIGN -- a full face would contradict Mystic/Occultist's own established silhouette). Two
     adjacent 'o' (void) pixels flanked by skin read as eyes at this scale without a dedicated
     eye colour.

This is why `HeroActor2D`'s per-hero `Modulate` changed from `classColor` to `White`: multiplying
an ALREADY-coloured, material-differentiated sprite by an unrelated runtime tint would wash the
steel back into whatever hue `classColor` happens to be (the exact bug this pass exists to fix,
just moved into the armour instead of out of it) and silently retune every hue chosen here.

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
    "f": (196, 148, 110, 255),  # Skin — one shared fantasy-tan tone, every class, never per-class
    "j": (58, 42, 34, 255),     # Hair — one shared dark-brown tone, every class that shows any
}

# ── per-class garment colour + material contrast (2026-08-04 COLOUR + MATERIAL PASS) ─────────────
# 'c'/'n'/'k'/'w' (cloth light/mid/dark/deepest) are PLACEHOLDER letters: they are never in
# PALETTE above, only in the per-class palette class_palette() returns, so the same ASCII grid
# renders in a different class's own hue by construction (never a second copy of the grid).

WHITE_RGB = (255, 255, 255)
BLACK_RGB = (0, 0, 0)


def _lerp_rgb(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int, int]:
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)  # type: ignore[return-value]


def cloth_ramp(hue: tuple[int, int, int]) -> dict[str, tuple[int, int, int, int]]:
    """Four cloth tones derived from one class hue by a fixed formula (never picked by eye,
    never per-class-tuned by hand): light/mid stay close to the hue (a SOFT, low-contrast step,
    unlike the neutral steel ramp's hard highlight-to-outline jump — see the module doc's
    MATERIAL contrast point), dark/deepest fall toward black for the shaded side."""
    return {
        "c": _lerp_rgb(hue, WHITE_RGB, 0.50),
        "n": _lerp_rgb(hue, WHITE_RGB, 0.12),
        "k": _lerp_rgb(hue, BLACK_RGB, 0.30),
        "w": _lerp_rgb(hue, BLACK_RGB, 0.55),
    }


# Sourced verbatim from sim/GameSim/Classes/**'s ClassDefinition.ColorRgb (the SAME hue every
# hero panel/chip/ledger row already shows for this class) -- never a colour invented here.
CLASS_HUES: dict[str, tuple[int, int, int]] = {
    "vanguard": (69, 130, 181),     # steel blue — sim/GameSim/Classes/ClassRegistry.cs
    "sentinel": (176, 141, 87),     # bronze — sim/GameSim/Classes/Sentinel/SentinelClass.cs
    "striker": (219, 20, 61),       # crimson — ClassRegistry.cs
    "skirmisher": (46, 204, 113),   # emerald — Skirmisher/SkirmisherClass.cs
    "mystic": (138, 43, 227),       # violet — ClassRegistry.cs
    "occultist": (85, 26, 110),     # dark violet — Occultist/OccultistClass.cs
}

# The player carries no ClassDefinition (he is not a hero) -- a worn-leather-apron brown, deliberately
# distinct from every hue above so the smith never reads as an off-duty hero of any class.
PLAYER_HUE: tuple[int, int, int] = (110, 74, 42)


def class_palette(class_id: str) -> dict[str, tuple[int, int, int, int]]:
    """The base PALETTE plus this class's cloth ramp merged in -- the one dict render() needs to
    draw this class's sprites. A KeyError here (unknown class_id) is a real authoring bug, so it
    is left to raise rather than silently falling back to some default hue."""
    return {**PALETTE, **cloth_ramp(CLASS_HUES[class_id])}


_CLOTH_MAP = {"l": "c", "m": "n", "i": "k", "d": "w"}


def to_cloth(s: str) -> str:
    """Mechanically remaps the neutral steel letters to their cloth-ramp equivalents
    (l->c, m->n, i->k, d->w), leaving every other character (outline, accents, transparency)
    untouched. Applied to whichever REGION of a class's ASCII should read as garment rather than
    armour -- see the module doc's MATERIAL contrast point."""
    return "".join(_CLOTH_MAP.get(ch, ch) for ch in s)

# ── canvas (U3 2026-08-04: 26x44 -> 40x64) ───────────────────────────────────────────────────────
WIDTH, HEIGHT = 40, 64

MARGIN_ROWS = 3     # 0-2: empty margin above the head
HEAD_ROWS = 16       # 3-18
TORSO_ROWS = 26      # 19-44
LEGS_ROWS = 19       # 45-63
assert MARGIN_ROWS + HEAD_ROWS + TORSO_ROWS + LEGS_ROWS == HEIGHT

# First row of the legs/hem on THIS (authoring-resolution) canvas. The SHIPPED asset is halved by
# rarity_downsample_2x() below (see SHIPPED RESOLUTION note near main()) — TownSpriteArtTests'
# LegsTopRow pins LEGS_TOP_ROW // 2 (22), the boundary as it actually lands in the committed PNG.
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
    orow(ocentered(mirror("ohlfo"))),  # 5 visor slit — skin peek flanking the shadowed centre
    orow(ocentered(mirror("ohlfo"))),  # 6 visor slit — skin peek flanking the shadowed centre
    orow(ocentered(mirror("ohlmi"))),  # 7
    orow(ocentered(mirror("ohlm"))),  # 8 taper
    orow(ocentered(mirror("ohl"))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("ommll"))),  # 11 gorget
    orow(ocentered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_VANGUARD) == 11

# Right chest/arm strip, 9 chars, held constant across the shield side — the gambeson sleeve
# under the pauldron, so it is CLOTH (to_cloth()'d, coloured steel-blue by class_palette()) while
# the shield/plate on the other side of the row stays the neutral steel ramp untouched.
FLANK = to_cloth("lllmiddlo")
FLANK_TRACE = to_cloth("tllmiddlo")
FLANK_RUNE = to_cloth("rllmiddlo")

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
    orow(ocentered(mirror("oohfm"))),  # 8 taper — skin begins (no visor slit to use instead)
    orow(ocentered(mirror("oohf"))),  # 9 chin — jaw-skin peek continues
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("oommll"))),  # 11 gorget (wider)
    orow(ocentered(mirror("ooiimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_SENTINEL) == 11

FLANK_SEN = to_cloth("lllmiddlo")  # gambeson sleeve, coloured bronze — same reasoning as Vanguard's FLANK
FLANK_SEN_TRACE = to_cloth("tllmiddlo")
FLANK_SEN_RUNE = to_cloth("rllmiddlo")

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

# STRIKER — hooded duelist: pointed hood, crossed strap, dual blade hilts, no shield. The hood is
# FABRIC, same leather/cloth material as the rest of this class's no-armor body (see the module
# doc's MATERIAL contrast point) -- to_cloth()'d exactly like the torso below it, so the hood reads
# as the same crimson leather rather than the neutral steel ramp every full-helm class's headwear
# correctly uses. Before this pass every hood/cowl class (this one, Mystic, Occultist) skipped that
# wrap and so wore what read as a steel helmet -- Skirmisher's cap already got this treatment;
# this brings the other three hood-wearers in line with it.
OLD_HEAD_STRIKER = [
    orow(ocentered(to_cloth("o" * 8))),  # 2 hood point
    orow(ocentered(mirror(to_cloth("ohhl")))),  # 3
    orow(ocentered(mirror(to_cloth("ohhll")))),  # 4 hood widens
    orow(ocentered(mirror(to_cloth("ohllj")))),  # 5 hair fringe at the hood edge
    orow(ocentered(mirror(to_cloth("ohlfo")))),  # 6 skin cheeks flanking eyes-in-shadow
    orow(ocentered(mirror(to_cloth("ollm")))),  # 7 taper
    orow(ocentered(mirror(to_cloth("ollm")))),  # 8 hold
    orow(ocentered(mirror(to_cloth("oll")))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror(to_cloth("ommll")))),  # 11 gorget
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_STRIKER) == 11

# No shield, so the WHOLE torso is leather, not just a flank strip — every content string below
# is to_cloth()'d (coloured crimson by class_palette()); the hilt highlights inside the overlay
# overrides stay literal 'h'/'o'/'d' (steel), a small metal accent against the leather field.
OLD_TORSO_STRIKER = [
    orow(ocentered(mirror(to_cloth("ooimlll")))),  # 13 shoulders (symmetric, narrower)
    ooverlay(to_cloth("oohlliddo"), {6: "e"}),  # 14 shoulders — ember upper-left ONLY
    ooverlay(to_cloth("oohllmdo"), {6: "e"}),  # 15 ember continues
    ooverlay(to_cloth("oollmiddo"), {12: "t"}),  # 16 coolant trace, single centre pixel
    ooverlay(to_cloth("oollmiddo"), {12: "r"}),  # 17 rune at the belt, single centre pixel
    orow(ocentered(mirror(to_cloth("oollmido")))),  # 18 chest
    orow(ocentered(mirror(to_cloth("oollmido")))),  # 19 hold
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 20 waist narrows
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 21 hold
    ooverlay(to_cloth("oolmiddo"), {6: "o", 7: "h", 8: "h", 17: "o", 18: "h", 19: "h"}),  # 22 hilts appear
    ooverlay(to_cloth("oolmiddo"), {6: "o", 7: "d", 8: "o", 17: "o", 18: "d", 19: "o"}),  # 23 hilts
    ooverlay(to_cloth("oolmiddo"), {7: "o", 8: "d", 18: "d", 19: "o"}),  # 24 blades taper
    ooverlay(to_cloth("oolmiddo"), {7: "d", 8: "o", 18: "o", 19: "d"}),  # 25 blade tips
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 26 torso resumes below the hilts
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 27 hold
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 28 waist taper
    orow(ocentered(mirror(to_cloth("oolmdo")))),  # 29 waist taper, near hip
    orow(ocentered(mirror(to_cloth("ooilmmi")))),  # 30 hips
]
assert len(OLD_TORSO_STRIKER) == 18

# SKIRMISHER — light flanker: open cap, quiver strap, crossed daggers at the belt. "More face
# shows than the Striker's shadow-eyes" per the class doc, so rows 6-9 are the exposed jaw/chin
# (skin), not cap fabric — cap fabric (cloth-ified, emerald) stops at row 5's hairline.
OLD_HEAD_SKIRMISHER = [
    orow(ocentered(to_cloth("o" * 8))),  # 2 cap crown
    orow(ocentered(mirror(to_cloth("ohhl")))),  # 3
    orow(ocentered(mirror(to_cloth("ohlj")))),  # 4 cap widens, hair peeks at the brim
    orow(ocentered(mirror("ohfo"))),  # 5 skin cheeks flanking eyes (open face, not cap)
    orow(ocentered(mirror("offo"))),  # 6 jaw, skin
    orow(ocentered(mirror("off"))),  # 7 jaw taper, skin
    orow(ocentered(mirror("off"))),  # 8 hold, skin
    orow(ocentered(mirror("of"))),  # 9 chin, skin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror(to_cloth("ommll")))),  # 11 collar
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 12 collar -> shoulder lead-in
]
assert len(OLD_HEAD_SKIRMISHER) == 11

# Leather + cap, no shield — the whole torso is to_cloth()'d (coloured emerald), same reasoning
# as Striker's; the quiver strap and dagger accents stay literal 'i'/'d'/'h'/'o' (leather-strap
# brown-black and steel blade highlights are already distinct from the emerald leather field).
OLD_TORSO_SKIRMISHER = [
    orow(ocentered(mirror(to_cloth("ooimlll")))),  # 13 shoulders (leaner, symmetric)
    ooverlay(to_cloth("oohlliddo"), {6: "e", 20: "i", 21: "d"}),  # 14 ember left, quiver right begins
    ooverlay(to_cloth("oohllmdo"), {19: "i", 20: "d"}),  # 15 quiver strap continues
    ooverlay(to_cloth("oolmiddo"), {12: "t", 18: "i", 19: "d"}),  # 16 coolant trace, quiver
    ooverlay(to_cloth("oolmiddo"), {12: "r"}),  # 17 rune at the belt
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 18 chest, leaner taper
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 19 hold
    orow(ocentered(mirror(to_cloth("olmido")))),  # 20 waist narrows
    orow(ocentered(mirror(to_cloth("olmido")))),  # 21 hold
    ooverlay(to_cloth("olmido"), {10: "o", 11: "h", 14: "h", 15: "o"}),  # 22 crossed daggers appear
    ooverlay(to_cloth("olmido"), {9: "o", 10: "d", 15: "d", 16: "o"}),  # 23 daggers cross
    ooverlay(to_cloth("olmido"), {11: "o", 12: "d", 13: "d", 14: "o"}),  # 24 blade tips converge
    orow(ocentered(mirror(to_cloth("olmido")))),  # 25 daggers end
    orow(ocentered(mirror(to_cloth("olmido")))),  # 26 waist
    orow(ocentered(mirror(to_cloth("olmdo")))),  # 27 waist taper
    orow(ocentered(mirror(to_cloth("olmdo")))),  # 28 hold
    orow(ocentered(mirror(to_cloth("olmo")))),  # 29 near hip
    orow(ocentered(mirror(to_cloth("ooilmmi")))),  # 30 hips
]
assert len(OLD_TORSO_SKIRMISHER) == 18

# MYSTIC — deep cowl, shadow face, staff enters frame on the right, rune between traces. The cowl
# is robe fabric, not a helmet -- to_cloth()'d like the torso below it (see the STRIKER head's own
# comment for why this wrap was missing before this pass on every hood/cowl class but Skirmisher).
OLD_HEAD_MYSTIC = [
    orow(ocentered(to_cloth("o" * 10))),  # 2 crown
    orow(ocentered(mirror(to_cloth("ohhlll")))),  # 3
    orow(ocentered(mirror(to_cloth("ohhlllm")))),  # 4 cowl flares
    orow(ocentered(mirror(to_cloth("ohlfoo")))),  # 5 shadow face — a hint of skin at the shadow's edge
    orow(ocentered(mirror(to_cloth("ohlfoo")))),  # 6 face is (mostly) shadow — same skin hint
    orow(ocentered(mirror(to_cloth("ohllm")))),  # 7 chin under cowl
    orow(ocentered(mirror(to_cloth("ollm")))),  # 8 taper
    orow(ocentered(mirror(to_cloth("oll")))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror(to_cloth("ommll")))),  # 11 gorget
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_MYSTIC) == 11

# The whole torso is robe (to_cloth()'d, coloured violet) — the staff/rune accents stay literal
# 'm'/'d'/'r' (a wooden staff and its rune are not the robe fabric).
OLD_TORSO_MYSTIC = [
    orow(ocentered(mirror(to_cloth("ooiimmll")))),  # 13 shoulders (robed, wider)
    ooverlay(to_cloth("oohlliddo"), {6: "e"}),  # 14 ember upper-left
    ooverlay(to_cloth("oohllmdo"), {6: "e", 21: "m", 22: "m", 23: "d", 24: "d"}),  # 15 hand reaches, staff
    ooverlay(to_cloth("oolmiddo"), {12: "t", 21: "m", 22: "d", 23: "d", 24: "d"}),  # 16 coolant trace + staff
    ooverlay(to_cloth("oolmiddo"), {12: "r", 24: "r"}),  # 17 rune on chest + on the staff
    ooverlay(to_cloth("oollmiddo"), {24: "d"}),  # 18 chest widens, staff
    ooverlay(to_cloth("oollmiddo"), {24: "d"}),  # 19 hold, staff
    ooverlay(to_cloth("oolllmiddo"), {24: "d"}),  # 20 robe widens, staff
    ooverlay(to_cloth("oolllmiddo"), {24: "d"}),  # 21 hold, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 22 robe widens, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 23 hold, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 24 hold, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 25 hold, staff
    orow(ocentered(mirror(to_cloth("ooilllmiddo")))),  # 26 robe continues, staff released
    orow(ocentered(mirror(to_cloth("oolllmiddo")))),  # 27 robe cinches at the waist
    orow(ocentered(mirror(to_cloth("oollmiddo")))),  # 28 waist
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 29 waist, narrowest point
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 30 hip lead-in
]
assert len(OLD_TORSO_MYSTIC) == 18

# OCCULTIST — deep cowl with two small horn-tips, a paired rune-and-eye glyph, staff. Cowl fabric
# to_cloth()'d like Mystic's (see that class's own comment); the horn-tip overrides stay literal
# 'd' (Iron deepest) -- a horn is keratin/bone, not cloth, so it deliberately keeps the hard neutral
# tone as a material contrast against the now-soft cowl around it, same trick as a hilt against leather.
OLD_HEAD_OCCULTIST = [
    ooverlay(to_cloth("o" * 4), {7: "d", 18: "d"}),  # 2 crown + two small horn-tips
    orow(ocentered(mirror(to_cloth("ohhll")))),  # 3 cowl (pointier than the Mystic's)
    orow(ocentered(mirror(to_cloth("ohhllm")))),  # 4 cowl flares sharply
    orow(ocentered(mirror(to_cloth("ohlfoo")))),  # 5 shadow face — a hint of skin at the shadow's edge
    orow(ocentered(mirror(to_cloth("ohlfoo")))),  # 6 face is (mostly) shadow — same skin hint
    orow(ocentered(mirror(to_cloth("ohllm")))),  # 7 chin under cowl
    orow(ocentered(mirror(to_cloth("ollm")))),  # 8 taper
    orow(ocentered(mirror(to_cloth("oll")))),  # 9 chin
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror(to_cloth("ommll")))),  # 11 gorget
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 12 gorget -> shoulder lead-in
]
assert len(OLD_HEAD_OCCULTIST) == 11

# The whole torso is robe (to_cloth()'d, coloured dark-violet — deeper/less saturated than
# Mystic's per ClassDefinition.ColorRgb) — staff/rune/eye accents stay literal, same as Mystic's.
OLD_TORSO_OCCULTIST = [
    orow(ocentered(mirror(to_cloth("ooiimmll")))),  # 13 shoulders (robed, wider)
    ooverlay(to_cloth("oohlliddo"), {6: "e"}),  # 14 ember upper-left
    ooverlay(to_cloth("oohllmdo"), {6: "e", 21: "m", 22: "m", 23: "d", 24: "d"}),  # 15 hand reaches, staff
    ooverlay(to_cloth("oolmiddo"), {11: "r", 13: "d", 21: "m", 22: "d", 23: "d", 24: "d"}),  # 16 rune+eye, staff
    ooverlay(to_cloth("oolmiddo"), {11: "d", 13: "r", 24: "r"}),  # 17 rune+eye glyph, staff continues
    ooverlay(to_cloth("oollmiddo"), {24: "d"}),  # 18 chest widens, staff
    ooverlay(to_cloth("oollmiddo"), {24: "d"}),  # 19 hold, staff
    ooverlay(to_cloth("oolllmiddo"), {24: "d"}),  # 20 robe widens, staff
    ooverlay(to_cloth("oolllmiddo"), {24: "d"}),  # 21 hold, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 22 robe widens, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 23 hold, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 24 hold, staff
    ooverlay(to_cloth("ooilllmiddo"), {24: "d"}),  # 25 hold, staff
    orow(ocentered(mirror(to_cloth("ooilllmiddo")))),  # 26 robe continues, staff released
    orow(ocentered(mirror(to_cloth("oolllmiddo")))),  # 27 robe cinches at the waist
    orow(ocentered(mirror(to_cloth("oollmiddo")))),  # 28 waist
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 29 waist, narrowest point
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 30 hip lead-in
]
assert len(OLD_TORSO_OCCULTIST) == 18

# ── TOWNSFOLK CIVILIANS (U6, world-and-interiors plan) ────────────────────────────────────────────
# TownsfolkNpc2D.ResolveSprite() used to hand every background villager the VANGUARD hero body
# (closed great-helm, slab shield, steel-blue baked cloth) with a runtime colour multiply — several
# identically-shaped "civilians" walking the plaza reads as reuse within seconds, and the shield/
# pauldron silhouette makes them look like off-duty adventurers, not townsfolk. Two bodies, same
# 40x64 canvas / 26-wide authoring grid / 4-frame gait every hero class already uses, but genuinely
# civilian: bare head + hair (no helm), a plain full-cloth tunic (no shield, no pauldron, no
# weapon/quiver/staff, no rune/ember/coolant accents — those mark a hero touched by the Mine, a
# townsperson is deliberately plainer), and the leg frames REUSE the existing CLOTH_LEGS_F1..F4
# built below (those are letter-only, hue-agnostic — see build_legs_frame — so no new leg art is
# needed, just a new palette).
#
# Neither hue is invented here: both are the exact 0-1 floats already committed and reviewed in
# TownsfolkNpc2D.CivilianPalette (godot/scripts/town2d/TownsfolkNpc2D.cs), converted 0-1 -> 0-255
# the same way every other literal in this file already is (round(x * 255)). Reusing that existing
# pair rather than picking two new RGB triples keeps the "never a colour invented per class"
# discipline this file has followed since the quality pass.
CIVILIAN_BROAD_HUE: tuple[int, int, int] = (115, 92, 56)   # "brown" — CivilianPalette[0]
CIVILIAN_SLIGHT_HUE: tuple[int, int, int] = (82, 107, 77)  # "muted green" — CivilianPalette[1]

# BROAD — stocky build: wide cropped-hair skull, square jaw that holds its width instead of
# tapering, thick neck. No helm (townsfolk don't wear one).
OLD_HEAD_CIVILIAN_BROAD = [
    orow(ocentered("j" * 12)),  # 2 crown — broad skull, cropped hair
    orow(ocentered(mirror("jjhl"))),  # 3 hair sheen
    orow(ocentered(mirror("jffhl"))),  # 4 hairline meets brow
    orow(ocentered(mirror("jffo"))),  # 5 eyes flanked by skin
    orow(ocentered(mirror("offo"))),  # 6 cheeks, broad
    orow(ocentered(mirror("off"))),  # 7 jaw — holds its width (square jaw), no taper yet
    orow(ocentered(mirror("off"))),  # 8 jaw hold
    orow(ocentered(mirror("of"))),  # 9 chin — still broad, flat, not pointed
    orow(ocentered("o" * 8)),  # 10 neck gap — thick neck
    orow(ocentered(mirror(to_cloth("ommmll")))),  # 11 collar — thick neck/collar, cloth (brown)
    orow(ocentered(mirror(to_cloth("ooimmmll")))),  # 12 collar -> shoulder lead-in, wide
]
assert len(OLD_HEAD_CIVILIAN_BROAD) == 11

# Whole torso is plain cloth (to_cloth()'d, coloured brown) — no shield, no pauldron, no weapon:
# a stocky silhouette that stays wide from chest to belly before narrowing at the very end, unlike
# every hero class's earlier waist taper.
OLD_TORSO_CIVILIAN_BROAD = [
    orow(ocentered(mirror(to_cloth("oiimmmlll")))),  # 13 shoulders — wide, symmetric
    # Ember upper-left on rows 14-15, same two-row treatment every unshielded hero class's torso
    # already carries (style-bible "candle-glow rim light, upper-left edge of focal objects") --
    # civilians previously had NONE, the one lighting gap between them and the hero cast.
    ooverlay(to_cloth("oohlliidd"), {6: "e"}),  # 14 upper chest, broad
    ooverlay(to_cloth("oohllmidd"), {6: "e"}),  # 15 ember continues
    orow(ocentered(mirror(to_cloth("ooollmidd")))),  # 16 chest
    orow(ocentered(mirror(to_cloth("ooollmidd")))),  # 17 hold
    orow(ocentered(mirror(to_cloth("oooollmid")))),  # 18 chest, still wide
    orow(ocentered(mirror(to_cloth("oooollmid")))),  # 19 hold
    orow(ocentered(mirror(to_cloth("ooollmid")))),  # 20 belly — stocky build stays wide here
    orow(ocentered(mirror(to_cloth("ooollmid")))),  # 21 hold
    orow(ocentered(mirror(to_cloth("ooollmid")))),  # 22 hold
    orow(ocentered(mirror(to_cloth("oollmid")))),  # 23 waist begins narrowing (late, unlike heroes)
    orow(ocentered(mirror(to_cloth("oollmid")))),  # 24 hold
    orow(ocentered(mirror(to_cloth("oolmid")))),  # 25 waist
    orow(ocentered(mirror(to_cloth("oolmid")))),  # 26 hold
    orow(ocentered(mirror(to_cloth("oolmd")))),  # 27 waist taper
    orow(ocentered(mirror(to_cloth("oolmd")))),  # 28 hold
    orow(ocentered(mirror(to_cloth("oomd")))),  # 29 near hip
    orow(ocentered(mirror(to_cloth("ooilmmi")))),  # 30 hips (shared convention, every class ends here)
]
assert len(OLD_TORSO_CIVILIAN_BROAD) == 18

# SLIGHT — leaner build: narrower skull, longer hair, jaw that narrows immediately to a pointed
# chin, slender neck. A simple tunic that narrows to a cinched waist then flares into a hem — the
# one silhouette beat this pair uses that no hero class has (heroes only ever narrow toward the
# hips), so BROAD/SLIGHT read as two different garments, not just two widths of the same one.
OLD_HEAD_CIVILIAN_SLIGHT = [
    orow(ocentered("j" * 8)),  # 2 crown — narrower skull, hair started already
    orow(ocentered(mirror("jjjhl"))),  # 3 hair, longer strands than BROAD's crop
    orow(ocentered(mirror("jffhl"))),  # 4 hairline meets brow
    orow(ocentered(mirror("jffo"))),  # 5 eyes flanked by skin
    orow(ocentered(mirror("off"))),  # 6 cheeks, narrow
    orow(ocentered(mirror("of"))),  # 7 jaw — narrows immediately (slight build)
    orow(ocentered(mirror("of"))),  # 8 jaw hold
    orow(ocentered(mirror("f"))),  # 9 chin — comes to a point, unlike BROAD's flat chin
    orow(ocentered("o" * 6)),  # 10 neck gap — slender neck
    orow(ocentered(mirror(to_cloth("ommll")))),  # 11 collar, cloth (green)
    orow(ocentered(mirror(to_cloth("ooimmll")))),  # 12 collar -> shoulder lead-in
]
assert len(OLD_HEAD_CIVILIAN_SLIGHT) == 11

OLD_TORSO_CIVILIAN_SLIGHT = [
    orow(ocentered(mirror(to_cloth("ooimlll")))),  # 13 shoulders — narrow, symmetric
    # Ember upper-left on rows 14-15 — see CIVILIAN_BROAD's own comment; index differs (7, not 6)
    # because this class's narrower 8-char left-string shifts the centering pad by one column.
    ooverlay(to_cloth("oohllidd"), {7: "e"}),  # 14 upper chest
    ooverlay(to_cloth("oohllmdd"), {7: "e"}),  # 15
    orow(ocentered(mirror(to_cloth("oollmidd")))),  # 16 chest
    orow(ocentered(mirror(to_cloth("oollmidd")))),  # 17 hold
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 18 chest taper — narrows quickly (slight build)
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 19 hold
    orow(ocentered(mirror(to_cloth("olmido")))),  # 20 waist narrows
    orow(ocentered(mirror(to_cloth("olmido")))),  # 21 hold
    orow(ocentered(mirror(to_cloth("olmdo")))),  # 22 waist — narrowest point of the whole figure
    orow(ocentered(mirror(to_cloth("olmdo")))),  # 23 hold
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 24 tunic hem begins flaring back out
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 25 hem flares
    orow(ocentered(mirror(to_cloth("oollmiddo")))),  # 26 hem flares more — widest point of the hem
    orow(ocentered(mirror(to_cloth("oollmiddo")))),  # 27 hold
    orow(ocentered(mirror(to_cloth("oolmiddo")))),  # 28 hem narrows back in toward the legs
    orow(ocentered(mirror(to_cloth("oolmido")))),  # 29 near hip
    orow(ocentered(mirror(to_cloth("ooilmmi")))),  # 30 hips (shared convention, every class ends here)
]
assert len(OLD_TORSO_CIVILIAN_SLIGHT) == 18

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

UPPER_CIVILIAN_BROAD = upper_body(OLD_HEAD_CIVILIAN_BROAD, OLD_TORSO_CIVILIAN_BROAD)
UPPER_CIVILIAN_SLIGHT = upper_body(OLD_HEAD_CIVILIAN_SLIGHT, OLD_TORSO_CIVILIAN_SLIGHT)

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

# Steel greaves+boots (Vanguard/Sentinel — armoured legs, stay in the neutral ramp).
STEEL_THIGH = "olllmddo"  # 8 chars — never moves across frames (hips barely move in a stride)
STEEL_SHIN = "odmmiddo"   # 8 chars
STEEL_ANKLE = "odmiddo"   # 7 chars
STEEL_BOOT = "ohhmdo"     # 6 chars — the part that goes missing on a lifted foot
STEEL_SOLE = "oooo"       # 4 chars — ditto

# Leather trousers+boots (Striker/Skirmisher — to_cloth()'d, coloured by class_palette() same as
# their torso, so a class's legs and torso are visibly the SAME material/colour, not two guesses).
CLOTH_THIGH = to_cloth(STEEL_THIGH)
CLOTH_SHIN = to_cloth(STEEL_SHIN)
CLOTH_ANKLE = to_cloth(STEEL_ANKLE)
CLOTH_BOOT = STEEL_BOOT  # boots themselves stay leather-dark/steel-buckled either way
CLOTH_SOLE = STEEL_SOLE


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


def build_legs_frame(
    front: str,
    sway_amount: int,
    thigh: str = STEEL_THIGH,
    shin: str = STEEL_SHIN,
    ankle: str = STEEL_ANKLE,
    boot: str = STEEL_BOOT,
    sole: str = STEEL_SOLE,
) -> list[str]:
    content_rows: list[str] = []
    content_rows += [mirror(thigh) for _ in range(4)]
    content_rows += [_lower_row(shin, front, liftable=False) for _ in range(6)]
    content_rows += [_lower_row(ankle, front, liftable=False) for _ in range(4)]
    content_rows += [_lower_row(boot, front, liftable=True) for _ in range(3)]
    content_rows += [_lower_row(sole, front, liftable=True) for _ in range(2)]
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

_cloth_leg_kwargs = dict(thigh=CLOTH_THIGH, shin=CLOTH_SHIN, ankle=CLOTH_ANKLE, boot=CLOTH_BOOT, sole=CLOTH_SOLE)
CLOTH_LEGS_F1 = build_legs_frame("left", sway_amount=-1, **_cloth_leg_kwargs)
CLOTH_LEGS_F2 = build_legs_frame("none", sway_amount=-1, **_cloth_leg_kwargs)
CLOTH_LEGS_F3 = build_legs_frame("right", sway_amount=1, **_cloth_leg_kwargs)
CLOTH_LEGS_F4 = build_legs_frame("none", sway_amount=1, **_cloth_leg_kwargs)

# ── the four hem-sway frames (mystic/occultist — the robe hides the legs, so the HEM carries the
# motion, exactly as the pre-U3 HEM_STEP already did; this just widens two sway magnitudes into
# four so all four frames are pairwise distinct) ──────────────────────────────────────────────────

# Mystic: a wide, smoothly-belled hem (the widest band reaches col-width 24). to_cloth()'d so the
# robe's hem is the SAME violet ramp as its torso.
HEM_BANDS_MYSTIC = [
    (3, to_cloth("ooilmmi")),      # rows 0-2: narrow, near the waist — barely sways
    (3, to_cloth("oollmmid")),     # rows 3-5
    (3, to_cloth("oolllmmid")),    # rows 6-8
    (3, to_cloth("ooilllmmid")),   # rows 9-11
    (3, to_cloth("ooddilllmmid")),  # rows 12-14: widest point of the bell
]
HEM_EDGE_WIDTH_MYSTIC = 24

# Occultist: a visibly NARROWER, straighter robe (a silhouette difference, not just palette) —
# measured 0.020 silhouette distance from Mystic with only a 4-pixel ragged notch at the very
# bottom, well under every other pair's distance; the fix is a genuinely different hem WIDTH
# profile across the whole skirt, not a cosmetic notch. Same band count/shift schedule as
# Mystic's (so build_hem_frame below stays one function), each band a couple of columns
# narrower, plus the ragged (tattered, not smooth-curved) edge at the very bottom.
HEM_BANDS_OCCULTIST = [
    (3, to_cloth("ooil")),         # rows 0-2
    (3, to_cloth("oolmi")),        # rows 3-5
    (3, to_cloth("oollmi")),       # rows 6-8
    (3, to_cloth("ooilmi")),       # rows 9-11
    (3, to_cloth("ooddilmi")),     # rows 12-14: much narrower than Mystic's widest band
]
HEM_EDGE_WIDTH_OCCULTIST = 14


def build_hem_frame(shift: int, bands: list[tuple[int, str]], edge_width: int) -> list[str]:
    content_rows: list[str] = []
    for band_index, (count, half) in enumerate(bands):
        band_shift = shift if band_index >= 2 else 0  # waist barely moves; hem does the swaying
        for _ in range(count):
            content_rows.append(_sway(centered(mirror(half)), band_shift))
    # Hem edge is the robe's own deepest cloth tone (a coloured trim, not a neutral shadow line);
    # ground contact stays neutral 'o' — that one row is the universal ground-shadow, not fabric.
    content_rows += [_sway(centered("w" * edge_width), shift) for _ in range(2)]  # hem edge
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


HERO_GRIDS: dict[str, list[str]] = {
    "town2d-hero-vanguard": assemble(UPPER_VANGUARD, LEGS_F1),
    "town2d-hero-vanguard_walk2": assemble(UPPER_VANGUARD, LEGS_F2),
    "town2d-hero-vanguard_step": assemble(UPPER_VANGUARD, LEGS_F3),
    "town2d-hero-vanguard_walk4": assemble(UPPER_VANGUARD, LEGS_F4),
    "town2d-hero-sentinel": assemble(UPPER_SENTINEL, LEGS_F1),
    "town2d-hero-sentinel_walk2": assemble(UPPER_SENTINEL, LEGS_F2),
    "town2d-hero-sentinel_step": assemble(UPPER_SENTINEL, LEGS_F3),
    "town2d-hero-sentinel_walk4": assemble(UPPER_SENTINEL, LEGS_F4),
    "town2d-hero-striker": assemble(UPPER_STRIKER, CLOTH_LEGS_F1),
    "town2d-hero-striker_walk2": assemble(UPPER_STRIKER, CLOTH_LEGS_F2),
    "town2d-hero-striker_step": assemble(UPPER_STRIKER, CLOTH_LEGS_F3),
    "town2d-hero-striker_walk4": assemble(UPPER_STRIKER, CLOTH_LEGS_F4),
    "town2d-hero-skirmisher": assemble(UPPER_SKIRMISHER, CLOTH_LEGS_F1),
    "town2d-hero-skirmisher_walk2": assemble(UPPER_SKIRMISHER, CLOTH_LEGS_F2),
    "town2d-hero-skirmisher_step": assemble(UPPER_SKIRMISHER, CLOTH_LEGS_F3),
    "town2d-hero-skirmisher_walk4": assemble(UPPER_SKIRMISHER, CLOTH_LEGS_F4),
    "town2d-hero-mystic": assemble(UPPER_MYSTIC, HEM_F1),
    "town2d-hero-mystic_walk2": assemble(UPPER_MYSTIC, HEM_F2),
    "town2d-hero-mystic_step": assemble(UPPER_MYSTIC, HEM_F3),
    "town2d-hero-mystic_walk4": assemble(UPPER_MYSTIC, HEM_F4),
    "town2d-hero-occultist": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F1),
    "town2d-hero-occultist_walk2": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F2),
    "town2d-hero-occultist_step": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F3),
    "town2d-hero-occultist_walk4": assemble(UPPER_OCCULTIST, OCCULTIST_HEM_F4),
}

# name -> (grid, palette) — each hero id's palette is the base PALETTE plus ITS class's cloth
# ramp; the class id is the id's own middle segment ("town2d-hero-<classId>[_suffix]").
SPRITES: dict[str, tuple[list[str], dict[str, tuple[int, int, int, int]]]] = {
    name: (grid, class_palette(name.removeprefix("town2d-hero-").split("_")[0]))
    for name, grid in HERO_GRIDS.items()
}


# ── TOWNSFOLK CIVILIAN sprites — reuse the existing cloth-leg frames (letter-only, hue-agnostic;
# see build_legs_frame) rather than building a third leg-frame set. Not routed through
# class_palette()/CLASS_HUES: those are keyed by SIM class id and raise on an unknown key by
# design (civilians are not a sim class) — a small dedicated helper instead.
CIVILIAN_HUES: dict[str, tuple[int, int, int]] = {
    "broad": CIVILIAN_BROAD_HUE,
    "slight": CIVILIAN_SLIGHT_HUE,
}


def civilian_palette(civilian_id: str) -> dict[str, tuple[int, int, int, int]]:
    return {**PALETTE, **cloth_ramp(CIVILIAN_HUES[civilian_id])}


CIVILIAN_GRIDS: dict[str, list[str]] = {
    "town2d-townsfolk-broad": assemble(UPPER_CIVILIAN_BROAD, CLOTH_LEGS_F1),
    "town2d-townsfolk-broad_walk2": assemble(UPPER_CIVILIAN_BROAD, CLOTH_LEGS_F2),
    "town2d-townsfolk-broad_step": assemble(UPPER_CIVILIAN_BROAD, CLOTH_LEGS_F3),
    "town2d-townsfolk-broad_walk4": assemble(UPPER_CIVILIAN_BROAD, CLOTH_LEGS_F4),
    "town2d-townsfolk-slight": assemble(UPPER_CIVILIAN_SLIGHT, CLOTH_LEGS_F1),
    "town2d-townsfolk-slight_walk2": assemble(UPPER_CIVILIAN_SLIGHT, CLOTH_LEGS_F2),
    "town2d-townsfolk-slight_step": assemble(UPPER_CIVILIAN_SLIGHT, CLOTH_LEGS_F3),
    "town2d-townsfolk-slight_walk4": assemble(UPPER_CIVILIAN_SLIGHT, CLOTH_LEGS_F4),
}

CIVILIAN_SPRITES: dict[str, tuple[list[str], dict[str, tuple[int, int, int, int]]]] = {
    name: (grid, civilian_palette(name.removeprefix("town2d-townsfolk-").split("_")[0]))
    for name, grid in CIVILIAN_GRIDS.items()
}


# ── VARIATION POOLS (2026-08-14, owner direction: "we want more variation ... heroes, NPCs,
# enemies, items we craft should all be a little unique") ────────────────────────────────────────
# Six heroes and a plaza of villagers currently share ONE body per class and ONE per civilian
# build, so a second vanguard is pixel-identical to the first and four townsfolk read as two
# people cloned. The fix is a POOL: extra whole-figure variants committed alongside each base
# sprite, one of which `GodotClient.ArtVariants.Pick` selects from a stable sim id (HeroId, the
# villager's spawn index) — so a given hero looks the same on day 1 and on day 40, across a
# save/load, on every machine, without a single runtime draw.
#
# WHAT VARIES, AND WHAT DELIBERATELY DOES NOT. Skin tone, hair tone, and a garment dye-tint vary.
# The class HUE does not: `CLASS_HUES` is sourced verbatim from each `ClassDefinition.ColorRgb`,
# the same colour that class's panel chip, ledger row and roster card already show, so it is
# legibility, not decoration — a violet figure must stay readable as the mystic at a glance. The
# dye-tints below therefore BLEND toward an anchor by a small t rather than replacing the hue: a
# sun-bleached vanguard is still obviously steel-blue. Civilians carry no such contract (they are
# not a sim class), but run through the same table for one code path rather than two.
#
# NO SILHOUETTE VARIATION HERE, on purpose: every variant reuses its base's ASCII grid untouched,
# so all four gait frames stay in lockstep and a new variant can never desync the walk cycle. Body
# shape is what `broad`/`slight` and the six class figures already provide.

# Index 0 is always the BASE sprite and every table's entry 0 reproduces the existing palette
# exactly (PALETTE["f"], PALETTE["j"], the untouched class hue) — so `--check` reports zero drift
# on the 32 sprites that shipped before this section existed.
VARIANT_COUNT = 5  # base + 4 extra bodies per class/civilian build

SKIN_TONES: list[tuple[int, int, int]] = [
    (196, 148, 110),  # 0 — PALETTE["f"] verbatim, the shared tone every base sprite bakes
    (232, 190, 152),  # 1 — pale
    (166, 116, 80),   # 2 — tan
    (124, 82, 56),    # 3 — deep brown
    (214, 164, 126),  # 4 — light warm
]

HAIR_TONES: list[tuple[int, int, int]] = [
    (58, 42, 34),     # 0 — PALETTE["j"] verbatim
    (28, 24, 26),     # 1 — near-black
    (122, 74, 38),    # 2 — auburn
    (150, 132, 96),   # 3 — straw blond
    (108, 104, 110),  # 4 — grey
]

# (anchor colour, blend weight). Small weights on purpose — see the class-hue note above; 0.20 is
# about where a steel-blue vanguard stops reading as steel-blue, so nothing here exceeds 0.18.
GARMENT_DYES: list[tuple[tuple[int, int, int], float]] = [
    ((0, 0, 0), 0.0),          # 0 — the class/civilian hue, untouched
    ((255, 214, 170), 0.18),   # 1 — sun-bleached
    ((40, 44, 70), 0.16),      # 2 — travel-stained, cooler
    ((176, 96, 48), 0.14),     # 3 — rust-dyed
    ((90, 120, 96), 0.14),     # 4 — moss-dyed
]

assert len(SKIN_TONES) == len(HAIR_TONES) == len(GARMENT_DYES) == VARIANT_COUNT
assert SKIN_TONES[0] + (255,) == PALETTE["f"], "variant 0 must reproduce the base skin tone exactly"
assert HAIR_TONES[0] + (255,) == PALETTE["j"], "variant 0 must reproduce the base hair tone exactly"


def variant_palette(base_hue: tuple[int, int, int], index: int) -> dict[str, tuple[int, int, int, int]]:
    """This individual's full palette. `index` 0 returns a dict byte-identical to the pre-variation
    palette for `base_hue`; 1..VARIANT_COUNT-1 swap in that row's skin/hair and dye the garment."""
    anchor, weight = GARMENT_DYES[index]
    hue = base_hue if weight == 0.0 else _lerp_rgb(base_hue, anchor, weight)[:3]
    return {
        **PALETTE,
        "f": SKIN_TONES[index] + (255,),
        "j": HAIR_TONES[index] + (255,),
        **cloth_ramp(hue),
    }


# Must stay in sync with GodotClient.ArtVariants.VariantPrefix — the C# resolver splits on this
# exact string. Named here rather than inlined so the coupling is greppable from both sides.
ARTVARIANTS_PREFIX = "-v"


def variant_id(base_id: str, index: int) -> str:
    """`<body>-v<N>` inserted BEFORE any frame suffix, so a variant's four gait frames stay
    siblings of each other rather than of the base body: `town2d-hero-vanguard_walk2` at index 2
    becomes `town2d-hero-vanguard-v3_walk2`, which is exactly what `ArtVariants.Pick` composes
    (it returns the varied BODY id and the caller appends `_walk2`). Index 0 is the base id
    unchanged — variant numbering is 1-based on screen, so index 1 is "-v2"."""
    if index == 0:
        return base_id
    body, sep, suffix = base_id.partition("_")
    return f"{body}{ARTVARIANTS_PREFIX}{index + 1}{sep}{suffix}"


VARIANT_SPRITES: dict[str, tuple[list[str], dict[str, tuple[int, int, int, int]]]] = {}
for _index in range(1, VARIANT_COUNT):
    for _name, _grid in HERO_GRIDS.items():
        _class_id = _name.removeprefix("town2d-hero-").split("_")[0]
        VARIANT_SPRITES[variant_id(_name, _index)] = (_grid, variant_palette(CLASS_HUES[_class_id], _index))
    for _name, _grid in CIVILIAN_GRIDS.items():
        _civilian_id = _name.removeprefix("town2d-townsfolk-").split("_")[0]
        VARIANT_SPRITES[variant_id(_name, _index)] = (_grid, variant_palette(CIVILIAN_HUES[_civilian_id], _index))

# Every variant ships its WHOLE frame set or none of it: a pool entry missing `_walk2` would show
# one villager freezing mid-stride, which is the kind of defect that reads as an engine bug.
assert len(VARIANT_SPRITES) == (VARIANT_COUNT - 1) * (len(HERO_GRIDS) + len(CIVILIAN_GRIDS))
for _base in list(HERO_GRIDS) + list(CIVILIAN_GRIDS):
    for _i in range(1, VARIANT_COUNT):
        assert variant_id(_base, _i) in VARIANT_SPRITES, f"variant frame missing for {_base} v{_i + 1}"


# ── MINE MONSTERS (§11.10 U4, 2026-08-14) ────────────────────────────────────────────────────────
# The five creatures DelveStage draws had NO generator. `art/build/town2d-monster-*.build.json`
# recorded them as `Status: "unreproducible-legacy"` -- no seed, no model, no script, and the repo
# never recorded whether the original pass was AI or hand-pixel. Measured before replacing them:
# 972 to 5,755 distinct opaque colours each and sizes from 60x41 to 84x99, which is the signature of
# a generated image downscaled, not of authored pixel art. So there was nothing to vary FROM, and
# ASSETS.md's "Hand-pixel Python" credit for this row was false. Authoring them here makes the
# credit true and gives every one of them a variation pool for free.
#
# WIDTH, NOT HALVED. These author at the module's own 40-wide canvas so `mirror`/`centered`/`row`
# work unchanged, and they ship at that size rather than going through rarity_downsample_2x --
# `DelveStage.MonsterWidth` is 120, so a 40-wide source scales by exactly 3.0. Integer, no shimmer.
# (The old art was 56-84 wide and was being scaled UP by 1.4-2.1x, which is why it read soft.)
#
# Each monster is a silhouette a player must recognise instantly at speed, so they are built for
# DISTINCT OUTLINES first: the rat is long and low, the spider is wide and legged, the ghoul is
# tall and narrow, the golem is a broad block, the worm is a vertical coil.

MONSTER_HUES: dict[str, tuple[int, int, int]] = {
    "cave-rat": (96, 76, 62),        # damp brown fur
    "tunnel-spider": (78, 60, 96),   # chitin, violet-black
    "deep-ghoul": (118, 138, 106),   # drowned green
    "ore-golem": (110, 102, 92),     # wet stone
    "forgeworm": (158, 76, 44),      # cooling slag
}


def monster_palette(monster_id: str, index: int = 0) -> dict[str, tuple[int, int, int, int]]:
    """This monster's palette. `index` 0 is the base body; 1..VARIANT_COUNT-1 reuse the SAME
    variation tables the town figures use (see variant_palette) so a variant monster is tinted by
    exactly the mechanism a variant villager is, rather than a second scheme to keep in sync."""
    return variant_palette(MONSTER_HUES[monster_id], index)


def mrow(s: str) -> str:
    """One monster row, written out at full canvas width.

    Deliberately NOT `centered(mirror(half))`, which is how the hero bodies are built. That idiom
    needs its half-row to end exactly at the centre line, and writing an already-padded half —
    `"....occnkko....."` — mirrors the padding outward and renders the creature TWICE, once at each
    edge. That is not hypothetical: the first draft of this section did it, and five monsters came
    out as nine shapes. Monsters are asymmetric enough (a rat faces left, a spider's legs cross)
    that the mirror bought little, so every row here is simply written in full and width-asserted."""
    return row(s)


# SHADING, NOT SILHOUETTE. The first draft of these five used one body tone throughout and the
# render was flat: the spider read as a smudge and the ghoul as a chess pawn, which is worse than
# the painterly art they replace. Every grid below therefore carries the same four-step cloth ramp
# the hero bodies use -- `c` light (upper-left, where the light is), `n` mid, `k` dark, `w` deepest
# (lower-right) -- plus `o` outline and `e` for the one lit feature that makes each creature read.

# CAVE RAT -- long and low, humped back, snout left, bald tail right. Asymmetric, so not mirrored.
CAVE_RAT = [
    row_left("." * 40),
    row_left("..............oooooo...................."),
    row_left("...........oooccnnkkoo.................."),
    row_left(".........ooccccnnnkkkkoo................"),
    row_left("......oooccccnnnnnkkkkkkkoo............."),
    row_left("....ooccccnnnnnnnnnkkkkkkkkkoo.........."),
    row_left("...occcnnnnnnnnnnnnkkkkkkkkkkkoo........"),
    row_left("..occnnnnnnnnnnnnnnkkkkkkkkkkkkkoo......"),
    row_left(".ocennnnnnnnnnnnnnnkkkkkkkkkkkkkkkoo...."),
    row_left(".ocnnnnnnnnnnnnnnnnkkkkkkkkkkkkkkkwwoo.."),
    row_left("oennnnnnnnnnnnnnnnnkkkkkkkkkkkkkkwwwwwo."),
    row_left("onnnnnnnnnnnnnnnnnnkkkkkkkkkkkkkwwwwwwwo"),
    row_left(".onnnnnnnnnnnnnnnnkkkkkkkkkkkkkwwwwwwwo."),
    row_left("..onnnnnnnnnnnnnkkkkkkkkkkkkkwwwwwwoo..."),
    row_left("...oonnnnnnnnkkkkkkkkkkkkkwwwwwwoo......"),
    row_left(".....ooonnkkkkkkkkkkkkwwwwwwoo.........."),
    row_left("........ooo.okko.okko.okko.o............"),
    row_left("...........okko.okko.okko..............."),
    row_left("...........owwo.owwo.owwo..............."),
    row_left("...........oooo.oooo.oooo..............."),
]


# TUNNEL SPIDER -- wide, low, eight real legs, lit eye cluster. Symmetric.
TUNNEL_SPIDER = [
    mrow("........................................"),
    mrow("...................oo..................."),
    mrow("............o..........okko............."),
    mrow("............oko.........okko............"),
    mrow("............okko........okko............"),
    mrow("............okko.......okko............."),
    mrow(".............okko.....okko.............."),
    mrow("..............okko...okko..............."),
    mrow("..............okkoooccnno..............."),
    mrow("..............ooccccnnnkko.............."),
    mrow(".............occcennnnkkkko............."),
    mrow("............occcennnnkkkkkwo............"),
    mrow("............occnnnnnkkkkkwwo............"),
    mrow(".............onnnnkkkkkwwwo............."),
    mrow("..............onnkkkkwwwoo.............."),
    mrow(".............o..oowwwoo..o.............."),
    mrow("............oko...oooo..oko............."),
    mrow("............oko..........oko............"),
    mrow("...........oo..............oo..........."),
]


# DEEP GHOUL -- tall, narrow, hunched, long arms hanging past the knee. Symmetric.
DEEP_GHOUL = [
    mrow("........................................"),
    mrow("..................oooo.................."),
    mrow(".................occnko................."),
    mrow(".................oceeko................."),
    mrow(".................ocnkko................."),
    mrow("..................onko.................."),
    mrow("...............ooocnkooo................"),
    mrow("..............occcnnnkkwo..............."),
    mrow(".............occccnnnkkkwo.............."),
    mrow("............oocccnnnnkkkwwo............."),
    mrow("............oconnnnnnkkkwwo............."),
    mrow("............ocnnnnnnnkkkwwo............."),
    mrow("............ocnnnnnnnkkkwwo............."),
    mrow("............oconnnnnnkkkwwo............."),
    mrow("............oco.nnnnkkk.wwo............."),
    mrow("............oco.onnkkko.wwo............."),
    mrow("............owo.onnkkko.owo............."),
    mrow("............ooo.onnkkko.ooo............."),
    mrow(".................onkkwo................."),
    mrow(".................onkkwo................."),
    mrow("................oonkkwoo................"),
    mrow("...............okko.owwo................"),
    mrow("...............oooo.oooo................"),
]


# ORE GOLEM -- broad blocky mass, ember seams in the cracks, stubby legs. Symmetric.
ORE_GOLEM = [
    mrow("........................................"),
    mrow(".................oooooo................."),
    mrow("...............occcnnkko................"),
    mrow("...............oceenkkwo................"),
    mrow("...............occnnkkwo................"),
    mrow(".............oooocnnkkwoooo............."),
    mrow("............occcccnnnkkkkkwo............"),
    mrow("...........occcccnnnnnkkkkkwo..........."),
    mrow("..........occccnnnneekkkkkwwo..........."),
    mrow("..........occcnnnnnnnkkkkkwwo..........."),
    mrow("..........occnnnnnnnnkkkkkwwo..........."),
    mrow("..........ocnnnneennnkkkkwwwo..........."),
    mrow("..........ocnnnnnnnnnkkkwwwwo..........."),
    mrow("...........onnnnnnnnkkkwwwwo............"),
    mrow("............onnnnnnkkkwwwwo............."),
    mrow(".............onnnnkkkwwwwo.............."),
    mrow(".............onnno.okkwwwo.............."),
    mrow(".............onnno.okkwwwo.............."),
    mrow(".............onnno.okkwwwo.............."),
    mrow("............occnno.okkwwwwo............."),
    mrow("............oooooo.oooooooo............."),
]


# FORGEWORM -- vertical coil, ringed segments, molten maw at the top. Symmetric.
FORGEWORM = [
    mrow("........................................"),
    mrow(".................oooooo................."),
    mrow("................oceeeeko................"),
    mrow("..............oceeewweeko..............."),
    mrow("..............oceewwwweko..............."),
    mrow("..............occnnnnkkko..............."),
    mrow("...............occnnkkko................"),
    mrow("................ocnkkwo................."),
    mrow("...............occcnnkkwo..............."),
    mrow("..............occccnnkkkwo.............."),
    mrow("..............occcnnnkkkwo.............."),
    mrow("...............occnnkkkwo..............."),
    mrow("................ocnkkwo................."),
    mrow("...............occcnnkkwo..............."),
    mrow("..............occccnnkkkwo.............."),
    mrow("..............occcnnnkkkwo.............."),
    mrow("...............occnnkkkwo..............."),
    mrow("................ocnkkwo................."),
    mrow("...............occcnnkkwo..............."),
    mrow("..............occccnnkkkwo.............."),
    mrow(".............occcccnnnkkkwo............."),
    mrow(".............oooooooooooooo............."),
]


MONSTER_GRIDS: dict[str, list[str]] = {
    "cave-rat": CAVE_RAT,
    "tunnel-spider": TUNNEL_SPIDER,
    "deep-ghoul": DEEP_GHOUL,
    "ore-golem": ORE_GOLEM,
    "forgeworm": FORGEWORM,
}

MONSTER_SPRITES: dict[str, tuple[list[str], dict[str, tuple[int, int, int, int]]]] = {}
for _mid, _grid in MONSTER_GRIDS.items():
    MONSTER_SPRITES[f"town2d-monster-{_mid}"] = (_grid, monster_palette(_mid, 0))
    for _i in range(1, VARIANT_COUNT):
        MONSTER_SPRITES[f"town2d-monster-{_mid}{ARTVARIANTS_PREFIX}{_i + 1}"] = (
            _grid, monster_palette(_mid, _i))

# Silhouette distinctness is the whole design brief for these five (see the section header), so it
# is asserted rather than eyeballed: no two monsters may share an outline shape.
_MONSTER_SILHOUETTES = {
    mid: tuple("".join("#" if ch != "." else "." for ch in r) for r in grid)
    for mid, grid in MONSTER_GRIDS.items()
}
assert len(set(_MONSTER_SILHOUETTES.values())) == len(MONSTER_GRIDS), \
    "two monsters share an identical silhouette — they must read apart at a glance"


# ── TOWN PROPS (§11.10 U9, 2026-08-14) ───────────────────────────────────────────────────────────
# TownLayout2D lays down 12 trees, 8 lanterns and 2 crates from THREE sprite ids, so the tree line
# reads as one tree copied twelve times. This gives each of those three a pool.
#
# RECOLOURED, NOT RE-AUTHORED — the one place this section departs from every other generator in
# this file. The committed props are good art the owner already accepted (the tree is 6 flat
# colours, the crate 5; the lantern carries a soft alpha glow at 360). Re-authoring them as ASCII
# grids the way U4 did for the monsters would be re-litigating art nobody complained about — the
# complaint was that there are twelve IDENTICAL trees, not that the tree is wrong. So the base PNG
# is the input and each variant is a deterministic colour transform of it.
#
# The consequence, stated because it is unusual here: this pass READS a committed PNG rather than
# being self-contained like the ASCII grids. That is deliberate and desirable — retouch the base
# tree and its whole pool follows on the next run — but it does mean a base PNG and its variants
# must be regenerated together, which `--check` enforces by diffing all of them.

PROP_VARIANT_COUNT = 5  # base + 4, matching the town cast


def _rgb_to_hsv(r: int, g: int, b: int) -> tuple[float, float, float]:
    """Integer RGB (0-255) to HSV floats. Hand-rolled rather than colorsys so the arithmetic is
    visible and the rounding is ours — the same reason cloth_ramp computes its ramp inline."""
    rf, gf, bf = r / 255.0, g / 255.0, b / 255.0
    high, low = max(rf, gf, bf), min(rf, gf, bf)
    span = high - low
    if span == 0:
        hue = 0.0
    elif high == rf:
        hue = ((gf - bf) / span) % 6
    elif high == gf:
        hue = (bf - rf) / span + 2
    else:
        hue = (rf - gf) / span + 4
    return hue / 6.0, (span / high if high else 0.0), high


def _hsv_to_rgb(h: float, s: float, v: float) -> tuple[int, int, int]:
    i = int(h * 6) % 6
    f = h * 6 - int(h * 6)
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    r, g, b = [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)][i]
    return round(r * 255), round(g * 255), round(b * 255)


# (hue shift in turns, saturation multiplier, value multiplier). Index 0 is identity, so variant 0
# reproduces the committed base byte-for-byte and `--check` stays quiet on the three originals.
PROP_TINTS: list[tuple[float, float, float]] = [
    (0.000, 1.00, 1.00),   # 0 — the committed art, untouched
    (0.030, 0.88, 1.10),   # 1 — sun-faded, warmer
    (-0.035, 1.12, 0.86),  # 2 — deeper, older
    (0.055, 0.72, 0.97),   # 3 — greyed, weathered
    (-0.015, 1.05, 1.14),  # 4 — fresher, brighter
]
assert PROP_TINTS[0] == (0.0, 1.0, 1.0), "variant 0 must be the identity transform"

PROP_IDS = ["town2d-prop-tree", "town2d-prop-lantern", "town2d-prop-crate"]

# Below this value a pixel is outline or deep shadow. Those stay put: shifting them makes the
# silhouette read as a different-coloured OBJECT rather than the same object in different light,
# and at 16-28px wide the outline is most of what carries the shape.
PROP_OUTLINE_VALUE_FLOOR = 0.18


def recolour_prop(image: Image.Image, index: int) -> Image.Image:
    """One prop variant. Alpha is copied through untouched — the lantern's glow is soft alpha, and
    perturbing it would change the shape of the light rather than its colour."""
    hue_shift, sat_mul, val_mul = PROP_TINTS[index]
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    src, dst = image.load(), out.load()

    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            h, s, v = _rgb_to_hsv(r, g, b)
            if v >= PROP_OUTLINE_VALUE_FLOOR:
                h = (h + hue_shift) % 1.0
                s = min(1.0, s * sat_mul)
                v = min(1.0, v * val_mul)
                r, g, b = _hsv_to_rgb(h, s, v)
            dst[x, y] = (r, g, b, a)

    return out


# ── PLAYER SMITH (2026-08-04 second round): brought up to the SAME treatment as the heroes above
# -- bigger canvas, real 4-frame gait, baked colour, a real face -- so the player is not the one
# crude sprite left once the heroes carry all of this. No prior generator source existed for
# `player_smith.png` (committed once, by hand, in the 2.5D pivot PR) -- authored fresh here at
# OLD_WIDTH=26 using the exact same helpers/idiom as every class above, then upscaled the same way.
# ────────────────────────────────────────────────────────────────────────────────────────────────
PLAYER_WIDTH, PLAYER_HEIGHT = 44, 68
PLAYER_MARGIN_ROWS = 3
PLAYER_HEAD_ROWS = 17
PLAYER_TORSO_ROWS = 28
PLAYER_LEGS_ROWS = 20
assert PLAYER_MARGIN_ROWS + PLAYER_HEAD_ROWS + PLAYER_TORSO_ROWS + PLAYER_LEGS_ROWS == PLAYER_HEIGHT

# CastProportionTests' "player stays tallest" pin: 68 > 64 (hero HEIGHT) at the same
# CharacterSpriteScale -- the one size fact U4 needs from this file (see the module's own note).
assert PLAYER_HEIGHT > HEIGHT

# Bare head: hair on top (no helmet), then skin/eyes -- unlike every hero class, nothing hides the
# player's face, so this is the one class-like figure that gets a FULL face rather than a peek.
OLD_HEAD_PLAYER = [
    orow(ocentered("j" * 10)),  # 2 hair crown
    orow(ocentered(mirror("jjhl"))),  # 3 hair with a highlight sheen
    orow(ocentered(mirror("jffhl"))),  # 4 hairline meets forehead skin
    orow(ocentered(mirror("jffo"))),  # 5 eyes (o,o centre) flanked by skin, temple hair at the edge
    orow(ocentered(mirror("off"))),  # 6 cheeks
    orow(ocentered(mirror("of"))),  # 7 jaw
    orow(ocentered(mirror("of"))),  # 8 jaw hold
    orow(ocentered(mirror("f"))),  # 9 chin point
    orow(ocentered("o" * 6)),  # 10 neck gap
    orow(ocentered(mirror("onncc"))),  # 11 shirt collar
    orow(ocentered(mirror("ooknncc"))),  # 12 collar -> shoulder lead-in
]
assert len(OLD_HEAD_PLAYER) == 11

# Shirt (cloth-ramp letters c/n/k/w, coloured by PLAYER_HUE) with a leather apron bibbed over the
# centre, ALSO in the cloth ramp (n/k/w, unchanged from the original design) -- the apron WIDENS
# going down the chest then narrows again at the waist tie.
#
# 2026-08-15 owner playtest ("main character looks awful -- the generic shopkeeper sprite was
# better"). Measured on the committed PNG: the neutral steel-violet ramp (o/d/l/m/i) covered
# ~60% of the opaque area (shoulders/collar/waist -- everywhere the SHIRT shows) while the warm
# PLAYER_HUE was confined to the apron bib alone (~22.5%), against town2d-townsfolk-broad's
# cloth-ramp coverage across its WHOLE torso+trousers. The first attempt at a fix mechanically
# swapped shirt<->apron (shirt warm, apron neutral) -- reviewed against a rendered contact sheet
# and rejected here at the SAME diagnosis stage that produced it: the apron shape already occupies
# most of the torso's own width at its "bib dominant" rows (14-24 below fully span the row there),
# so recolouring it neutral without reshaping it landed at 43.5% warm, UNDER this file's own
# `--check`-pinned 45% floor (TownSpriteArtTests) -- the diagnosis was right about the SHIRT, wrong
# about the APRON needing to give up its colour rather than just no longer being the ONLY warm
# thing. Kept here instead: the shirt (every neutral l/m/i/d letter below, i.e. everywhere the
# apron does NOT cover) becomes cloth-ramp too, and the apron KEEPS the warm tone it always had
# (n/k/w below are untouched from the original grid) -- both materials now read as the player's
# own identity hue, distinguished from each other by VALUE/shading (the same "material contrast
# via shading, not just hue" idiom the shielded classes already use for armour-vs-cloth), not by
# one of them being grey. The trousers get the matching fix just below (CLOTH_LEGS_F1..F4, not
# the neutral LEGS_F1..F4 every other body's STEEL variant would reuse) -- the player was the only
# figure in the cast still in fully-steel trousers; boots/soles stay neutral for every character,
# player included (CLOTH_BOOT/CLOTH_SOLE alias STEEL_BOOT/STEEL_SOLE already, by design). Measured
# result: 61.8% warm, comfortably clear of the 45% floor and in the same range as the civilians'
# own coverage.
OLD_TORSO_PLAYER = [
    orow(ocentered(mirror("ooknnccc"))),  # 13 shoulders (shirt)
    orow(ocentered(mirror("ohhccnk"))),  # 14 collar -> apron strap begins
    orow(ocentered(mirror("ohccnnk"))),  # 15
    orow(ocentered(mirror("occnnkw"))),  # 16 apron widens
    orow(ocentered(mirror("occnkw"))),  # 17
    orow(ocentered(mirror("ocnkww"))),  # 18
    orow(ocentered(mirror("onkkww"))),  # 19 apron bib dominant
    orow(ocentered(mirror("onkkww"))),  # 20 hold
    orow(ocentered(mirror("onkkww"))),  # 21 hold
    orow(ocentered(mirror("onkkw"))),  # 22 apron narrows toward the waist tie
    orow(ocentered(mirror("onkw"))),  # 23
    orow(ocentered(mirror("onkw"))),  # 24 hold
    orow(ocentered(mirror("ockw"))),  # 25 shirt/trouser waistband returns
    orow(ocentered(mirror("ocnw"))),  # 26
    orow(ocentered(mirror("ocnw"))),  # 27
    orow(ocentered(mirror("oonw"))),  # 28
    orow(ocentered(mirror("oonw"))),  # 29 hold, near hip
    orow(ocentered(mirror("ookcnnk"))),  # 30 hips (feeds the legs)
]
assert len(OLD_TORSO_PLAYER) == 18


def _upscale(head: list[str], torso: list[str], width: int, head_rows: int, torso_rows: int) -> list[str]:
    scaled_head = nn_scale(head, width, head_rows)
    scaled_torso = nn_scale(torso, width, torso_rows)
    combined = scaled_head + scaled_torso
    assert len(combined) == head_rows + torso_rows
    return combined


UPPER_PLAYER = _upscale(OLD_HEAD_PLAYER, OLD_TORSO_PLAYER, PLAYER_WIDTH, PLAYER_HEAD_ROWS, PLAYER_TORSO_ROWS)

# Legs reuse the CLOTH leg frames (2026-08-15 palette fix, see OLD_TORSO_PLAYER's doc) rescaled to
# the player's own canvas — same alternating-gait guarantee, no second gait implementation to keep
# in sync. The player used to be the only figure in the cast still wearing STEEL (fully neutral)
# trousers while every hero/townsfolk class already wears CLOTH ones (own-hue thigh/shin/ankle,
# boot+sole always neutral regardless — CLOTH_BOOT/CLOTH_SOLE alias STEEL_BOOT/STEEL_SOLE, so this
# does not touch the boot itself, only brings the trouser leg in line with the rest of the cast.
PLAYER_LEGS_F1 = nn_scale(CLOTH_LEGS_F1, PLAYER_WIDTH, PLAYER_LEGS_ROWS)
PLAYER_LEGS_F2 = nn_scale(CLOTH_LEGS_F2, PLAYER_WIDTH, PLAYER_LEGS_ROWS)
PLAYER_LEGS_F3 = nn_scale(CLOTH_LEGS_F3, PLAYER_WIDTH, PLAYER_LEGS_ROWS)
PLAYER_LEGS_F4 = nn_scale(CLOTH_LEGS_F4, PLAYER_WIDTH, PLAYER_LEGS_ROWS)

PLAYER_EMPTY_MARGIN = ["." * PLAYER_WIDTH] * PLAYER_MARGIN_ROWS


def _assemble_player(lower: list[str]) -> list[str]:
    full = PLAYER_EMPTY_MARGIN + UPPER_PLAYER + lower
    assert len(full) == PLAYER_HEIGHT
    for r in full:
        assert len(r) == PLAYER_WIDTH
    return full


_player_palette = {**PALETTE, **cloth_ramp(PLAYER_HUE)}

PLAYER_SPRITES: dict[str, tuple[list[str], dict[str, tuple[int, int, int, int]]]] = {
    "player_smith": (_assemble_player(PLAYER_LEGS_F1), _player_palette),
    "player_smith_walk2": (_assemble_player(PLAYER_LEGS_F2), _player_palette),
    "player_smith_step": (_assemble_player(PLAYER_LEGS_F3), _player_palette),
    "player_smith_walk4": (_assemble_player(PLAYER_LEGS_F4), _player_palette),
}


def die(message: str) -> None:
    print(f"gen_town_sprites.py: error: {message}", file=sys.stderr)
    raise SystemExit(1)


# ── SHIPPED RESOLUTION (2026-08-12, asymmetric-decimation fix) ───────────────────────────────────
# Every character sprite used to ship at this file's full authoring canvas (40x64 / 44x68) and get
# halved at RUNTIME by TownLayout2D.CharacterSpriteScale=0.5 under a Nearest filter with mipmaps
# off. That "2:1 decimation" was never clean: Nearest keeps exactly one column/row out of every
# mirrored pair, chosen by pixel-grid alignment, not by the art — a bilaterally-symmetric silhouette
# came out visibly lopsided on screen, and any single-pixel authored accent (a visor slit, a rune, a
# coolant trace, a shield boss) was a coin flip to survive at all. Simulated with a real committed
# sprite (PIL nearest-downscale, same math a GPU Nearest sampler does for an exact half-scale draw):
# every mirror-symmetric, non-empty row broke symmetry after the "decimation", by up to 10 of 20
# output columns.
#
# The fix moves the halving OFFLINE, into this generator, so the committed PNG already IS the
# on-screen pixel grid and TownLayout2D.CharacterSpriteScale is 1.0 (a pure pass-through — no
# runtime decimation is left to get wrong). `rarity_downsample_2x()` below does the halving with a
# 2x2-block combine that is a pure function of each block's own pixel VALUES (plus a whole-image
# frequency table) and never of pixel POSITION, which is what makes it provably safe for a mirror-
# symmetric source: reflecting the source maps every block onto a value-identical mirror block, so
# the same deterministic choice is made on both sides and the output is symmetric wherever the
# input is. `_selftest_rarity_downsample_symmetry()` pins exactly that property.
def rarity_downsample_2x(image: Image.Image, freq_source: Image.Image | None = None) -> Image.Image:
    """Halves `image` in both dimensions, tuned for flat-shaded pixel art rather than photographic
    content. ALPHA is a plain box average of the 2x2 block (a smooth, symmetric silhouette edge).
    RGB is deliberately NOT blended: a plain colour average dilutes a rare one-pixel accent (skin
    hint, rune, visor slit, coolant trace) toward whichever colour shares its block, which is
    almost always the far more common outline/base tone — measured on the real committed art, a
    naive average crushed 3 of 6 hero classes' entire skin-tone region to zero exact-match pixels.
    Instead, each opaque pixel in the block votes with its own colour, and the block keeps whichever
    colour is globally RAREST in this image (ties broken by the RGBA tuple itself, so the pick is
    fully deterministic) — the rare accent wins over the common fill whenever the two compete for
    the same output pixel, and blocks that are already a single solid colour are untouched.

    `freq_source` supplies the frequency table from a DIFFERENT image — pass a body's base frame
    when halving its gait frames, and every frame of that body then ranks colours identically.
    Without it the table is per-frame, and since a stride changes how much leg colour is on screen,
    a rarity TIE two rows into the hair can resolve differently from one frame to the next: the
    figure's head shimmers while it walks. Measured on the committed art before this parameter
    existed — skirmisher only, `_walk2` and `_walk4` only, exactly 2 pixels each, the other five
    classes clean — small enough to have shipped unnoticed and to survive a diff, and a real defect
    all the same. Defaults to `image` itself, which is the identity behaviour every other caller
    (and the self-test below) already relies on."""
    width, height = image.size
    if width % 2 != 0 or height % 2 != 0:
        die(f"rarity_downsample_2x: {width}x{height} has an odd dimension, cannot halve exactly")

    pixels = image.load()

    source = freq_source if freq_source is not None else image
    if source.size != image.size:
        die(f"rarity_downsample_2x: freq_source is {source.size}, must match the image's {image.size}")
    source_pixels = source.load()

    freq: dict[tuple[int, int, int, int], int] = {}
    for y in range(height):
        for x in range(width):
            pixel = source_pixels[x, y]
            if pixel[3] == 0:
                continue
            freq[pixel] = freq.get(pixel, 0) + 1

    new_width, new_height = width // 2, height // 2
    out = Image.new("RGBA", (new_width, new_height), (0, 0, 0, 0))
    out_pixels = out.load()
    for by in range(new_height):
        for bx in range(new_width):
            block = [
                pixels[2 * bx, 2 * by], pixels[2 * bx + 1, 2 * by],
                pixels[2 * bx, 2 * by + 1], pixels[2 * bx + 1, 2 * by + 1],
            ]
            out_alpha = round(sum(p[3] for p in block) / 4)
            opaque = {p for p in block if p[3] > 0}
            if not opaque:
                out_pixels[bx, by] = (0, 0, 0, 0)
                continue
            rarest = min(opaque, key=lambda c: (freq[c], c))
            out_pixels[bx, by] = (rarest[0], rarest[1], rarest[2], out_alpha)

    return out


def _selftest_rarity_downsample_symmetry() -> None:
    """Regression pin for the asymmetric-decimation bug: builds a synthetic 8x4 RGBA image whose
    every row is an exact left/right mirror (including a rare one-pixel accent colour sandwiched
    against a common fill, and a fully-transparent row), then asserts EVERY row of the 4x2
    downsample output is also an exact mirror. Runs unconditionally on import — a future change to
    rarity_downsample_2x that reintroduces position-dependent behaviour (e.g. swapping in a plain
    left-to-right column pick) fails loudly here before it ever touches a hero sprite."""
    common = (20, 15, 31, 255)   # a colour that dominates the image, like the outline tone
    accent = (196, 148, 110, 255)  # a colour that appears once per mirrored side, like a skin hint
    other = (110, 104, 128, 255)
    transparent = (0, 0, 0, 0)

    rows = [
        [common, other, accent, common, common, accent, other, common],  # accent flanked by common
        [common, common, common, common, common, common, common, common],  # solid block
        [other, other, common, common, common, common, other, other],
        [transparent] * 8,  # fully empty row must stay fully empty
    ]
    for row in rows:
        assert row == row[::-1], f"selftest fixture row is not itself mirror-symmetric: {row}"

    src = Image.new("RGBA", (8, 4), (0, 0, 0, 0))
    src_pixels = src.load()
    for y, row in enumerate(rows):
        for x, color in enumerate(row):
            src_pixels[x, y] = color

    out = rarity_downsample_2x(src)
    out_pixels = out.load()
    for y in range(out.height):
        out_row = [out_pixels[x, y] for x in range(out.width)]
        assert out_row == out_row[::-1], (
            f"rarity_downsample_2x broke mirror symmetry on selftest row {y}: {out_row} — this is "
            "exactly the asymmetric-decimation bug the 2026-08-12 fix exists to prevent."
        )

    # The accent colour is globally rarer than `common`/`other` in this fixture (2 vs 8/4
    # occurrences) and fully occupies its own 2x2 block (rows 0-1, cols 2-3 and cols 4-5 after
    # mirroring) — it must survive the downsample exactly, not get diluted into a blend.
    assert out_pixels[1, 0] == accent, f"accent colour did not survive the downsample: {out_pixels[1, 0]}"


_selftest_rarity_downsample_symmetry()


def render(grid: list[str], name: str, palette: dict[str, tuple[int, int, int, int]], width: int, height: int) -> Image.Image:
    """Rasterize one ASCII grid against `palette`. Validates shape loudly — a short row would
    silently shift every pixel after it, which is exactly the kind of defect a diff cannot show."""
    if len(grid) != height:
        die(f"{name}: expected {height} rows, got {len(grid)}")

    image = Image.new("RGBA", (width, height), palette["."])
    pixels = image.load()
    for y, row_str in enumerate(grid):
        if len(row_str) != width:
            die(f"{name}: row {y} is {len(row_str)} chars, expected {width}")
        for x, char in enumerate(row_str):
            if char not in palette:
                die(f"{name}: row {y} col {x} uses '{char}', which is not in the palette")
            pixels[x, y] = palette[char]

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

    # ── 2026-08-15 six-hero-cast ship wave ─────────────────────────────────────────────────────
    # These 28 ids no longer come from THIS script. The six hero classes' base body (all 4 gait
    # frames) and the player smith (all 4 gait frames) were replaced by an SDXL composite cast
    # (art/build/town2d-hero-<class>*.build.json, art/build/player_smith*.build.json) -- the
    # owner reviewed and approved them at ship size ("the six heroes at ship size are fantastic";
    # "the new smith is way better also"). The SPRITES/PLAYER_SPRITES dict entries above are NOT
    # deleted: VARIANT_SPRITES still recolours the SAME hand-drawn UPPER_<CLASS>/CLOTH_LEGS_*
    # geometry for the untouched -v2.. pools, and PLAYER_LEGS_* still derives from CLOTH_LEGS_*
    # too -- deleting the grids would break those. But these 28 ids themselves must never be
    # written or --check'd against this script's own hand-drawn output again, or a plain
    # `python tools/art/gen_town_sprites.py` run would silently clobber the approved AI art back
    # to the old look with no error. AssetProvenanceTests' HandAuthoredPrefix comment carries the
    # test-side half of this same carve-out.
    #
    # 2026-08-15, same-day follow-up: the six classes' -v2..-v5 VARIANT pools got the same
    # treatment (96 more PNGs, deterministic PIL recolours of the AI bases per the SAME
    # SKIN_TONES/HAIR_TONES/GARMENT_DYES rows this script already owns -- not a second render
    # pipeline). Those ids are DERIVED from CLASS_HUES + variant_id(), not hand-listed, on purpose:
    # this repo has been burned before by an exclusion set that was a literal id array and silently
    # stopped covering a growing family (see the hand-listed-fixtures lesson). CIVILIAN variants are
    # NOT in this set -- townsfolk still render from this script's own hand-drawn geometry.
    _ai_composite_hero_ids = {
        f"town2d-hero-{cls}{suffix}"
        for cls in CLASS_HUES
        for suffix in ("", "_step", "_walk2", "_walk4")
    }
    _ai_composite_cast_ids = (
        _ai_composite_hero_ids
        | {
            variant_id(base_id, index)
            for base_id in _ai_composite_hero_ids
            for index in range(1, VARIANT_COUNT)
        }
        | {f"player_smith{suffix}" for suffix in ("", "_step", "_walk2", "_walk4")}
    )

    all_sprites = {
        name: value
        for name, value in {**SPRITES, **PLAYER_SPRITES, **CIVILIAN_SPRITES, **VARIANT_SPRITES,
                             **MONSTER_SPRITES}.items()
        if name not in _ai_composite_cast_ids
    }

    def canvas_of(sprite_name: str) -> tuple[int, int]:
        """(width, height) for one sprite. The town cast and the player are fixed-canvas; monsters
        vary in height by creature (a rat is not a worm), so their height comes from the grid
        itself rather than a constant."""
        if sprite_name.startswith("player_smith"):
            return PLAYER_WIDTH, PLAYER_HEIGHT
        if sprite_name.startswith("town2d-monster-"):
            return WIDTH, len(all_sprites[sprite_name][0])
        return WIDTH, HEIGHT

    def is_halved(sprite_name: str) -> bool:
        """Monsters author AT their shipped size; everyone else authors at 2x and is halved by
        rarity_downsample_2x. See the MINE MONSTERS section header — DelveStage.MonsterWidth is
        120, so a 40-wide monster scales by exactly 3.0, and halving it first would throw away
        the detail that makes it readable."""
        return not sprite_name.startswith("town2d-monster-")

    drift = []
    # Every gait frame of one body halves against the BASE frame's colour-frequency table, so a
    # rarity tie resolves the same way in all four — see rarity_downsample_2x's `freq_source` note
    # for the head-shimmer this closes. Keyed on the id with its frame suffix stripped; a body
    # whose base frame is somehow absent falls back to per-frame frequency (the old behaviour).
    base_frames: dict[str, Image.Image] = {}

    def render_full(sprite_name: str) -> Image.Image:
        grid_, palette_ = all_sprites[sprite_name]
        w, h = canvas_of(sprite_name)
        return render(grid_, sprite_name, palette_, w, h)

    for name in all_sprites:
        if not is_halved(name):
            continue  # monsters are single-frame; there is no gait to keep consistent
        body_id = name.partition("_")[0] if not name.startswith("player_smith") else "player_smith"
        if body_id not in base_frames and body_id in all_sprites:
            base_frames[body_id] = render_full(body_id)

    for name, (grid, palette) in all_sprites.items():
        width, height = canvas_of(name)
        image = render(grid, name, palette, width, height)
        if is_halved(name):
            body_id = name.partition("_")[0] if not name.startswith("player_smith") else "player_smith"
            # Halve to the actual on-screen resolution here, offline — see the SHIPPED RESOLUTION
            # note above rarity_downsample_2x(). The committed PNG is this halved image, not the
            # authoring canvas; TownLayout2D.CharacterSpriteScale draws it at 1.0 from here on.
            image = rarity_downsample_2x(image, freq_source=base_frames.get(body_id))
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
        print(f"wrote {path} ({image.width}x{image.height})")

    # ---- prop variants: a colour transform of committed art, not an ASCII render ----------------
    # Runs after the grid pass because it READS committed PNGs (see the TOWN PROPS section header).
    # A missing base is a hard error rather than a skip: silently generating no pool is exactly the
    # "committed but invisible" failure the art-miss logging exists to catch.
    for prop_id in PROP_IDS:
        base_path = os.path.join(args.out, f"{prop_id}.png")
        if not os.path.exists(base_path):
            die(f"{prop_id}: no committed base PNG at {base_path} — cannot build its variant pool")
        base = Image.open(base_path).convert("RGBA")

        for index in range(1, PROP_VARIANT_COUNT):
            name = f"{prop_id}{ARTVARIANTS_PREFIX}{index + 1}"
            image = recolour_prop(base, index)
            path = os.path.join(args.out, f"{name}.png")

            if args.check:
                if not os.path.exists(path):
                    drift.append(f"{name}: no committed PNG at {path}")
                elif (list(Image.open(path).convert("RGBA").get_flattened_data())
                        != list(image.get_flattened_data())):
                    drift.append(f"{name}: committed PNG differs from the recolour in this script")
                continue

            image.save(path)
            print(f"wrote {path} ({image.width}x{image.height})")

    if drift:
        for line in drift:
            print(f"gen_town_sprites.py: drift: {line}", file=sys.stderr)
        return 1

    if args.check:
        print(f"no drift — {len(all_sprites)} sprites match their committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
