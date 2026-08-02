#!/usr/bin/env python
"""Author the town's character sprites as explicit pixel grids.

WHY THIS IS NOT THE SDXL PIPELINE
---------------------------------
Every backdrop, portrait, monster and prop in `godot/assets/art/` comes from the local
ComfyUI/SDXL chain (see `godot/assets/art/README.md`). These sprites deliberately do not,
for the same reason `docs/plans/2026-07-28-001-feat-animation-motion-adventure.md` rejected
generated walk cycles: at sprite scale a diffusion render downscales to mush, and it cannot
hold identity or palette between a base frame and its step frame. Tried and confirmed once more
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
(invisible), and a naive 2x upscale would make heroes TOWER over the player, which is the exact
regression this plan bans. (2) only three of six hero classes (vanguard/striker/mystic) had a
town body at all -- sentinel/occultist/skirmisher fell back to `IconRegistry.Sprite`'s roster
SVG, a portrait-style scribble never meant to walk around a town.

The fix: WIDTH/HEIGHT go from 20x36 to 26x44 (13x22 on screen -- 1.6x the canvas, still under
the player's 15x23; `CastProportionTests.cs` pins the invariant permanently), and the three
missing classes are authored from scratch at the new size, using the SAME volume-shading idiom
as the quality pass (gradient runs, palette sampled from these same committed grids, never
picked by eye).

Six hand-typed 26-wide-by-44-tall ASCII grids per class (base + step) is a lot of surface for a
human reviewer to verify by eye -- "is this actually symmetric," "did the walk-frame diff leak
above the legs" are hard questions to answer by staring at raw dot-and-letter soup. So this pass
adds a THIN set of helper functions (`mirror`/`centered`/`overlay`, plus the pre-existing
left-anchor need factored out as `rowL`) that make the INTENT of a row legible in the diff
instead of hiding it in whitespace-counting:

    row(centered(mirror("ohhll")))                  # a symmetric gradient band, width says so
    overlay("oohlliddo", {6: 'e'})                  # a symmetric row with ONE asymmetric pixel

This is the same "build from reusable pieces" idiom the file already used (`X_STEP = X[:25] +
[...]`), just applied one level finer so six classes' shared skeleton (see SHARED SKELETON
below) doesn't need retyping six times. Every helper call still produces one committed, literal
26-character string per row -- `render()`'s validation and the `--check` drift guard are
unchanged and see exactly the same flat list of strings they always did. Nothing about
determinism, GPU-freedom, or diff-reviewability regresses; the diff now shows *why* a row is
shaped the way it is, not just that it is.

SHARED SKELETON (all six classes, so their walk frames line up and reuse is possible)
---------------------------------------------------------------------------------------
    rows 0-1    empty margin above the head
    rows 2-12   head / helm / cowl (11 rows, class-specific)
    rows 13-30  torso + arms / robe-upper (18 rows, class-specific)
    rows 31-43  legs (humanoid classes) or hem (robed classes) -- 13 rows, ONE of two shared
                blocks (LEGS_BASE/LEGS_STEP, HEM_BASE/HEM_STEP), exactly like the four humanoid
                classes already share a walk cycle and the two robed classes already share a
                hover-sway, rather than every class re-deriving its lower body from scratch.

Base and step frames for every class differ ONLY from row 31 down (legs-only delta, matching
this file's long-standing discipline) -- guaranteed by construction, since every `X_STEP` is
built from the exact same `HEAD_X + TORSO_X` list as `X`, with only the final LEGS/HEM block
swapped.

Sizes match what `TownAssets2D`/`TownLayout2D` lay out against; `TownLayout2D.TileSize` and the
hand-placed tile coordinates are untouched -- this is a canvas resize, not a layout change, and
the census never pinned dimensions (verified: `AssetResolutionCensusTests` checks resolution,
not size).

IMPORT SETTING EVERY `_step` PAIR MUST CARRY: `process/fix_alpha_border=false`
-------------------------------------------------------------------------------
Godot's default texture import bakes a filler RGB into fully-transparent (alpha 0) pixels near
an opaque edge, to stop bilinear/mipmap sampling from bleeding a dark fringe in from outside a
cutout -- irrelevant here (Nearest filtering, mipmaps off, by design, everywhere in this
pipeline). Because a base/step pair's opaque legs/hem legitimately diverge below row 31, that
border-fix can pick a DIFFERENT filler colour for the same transparent coordinate a row or two
ABOVE the real divergence in each texture -- same alpha (invisible either way), different RGB,
which `TownSpriteArtTests.StepFrames_DifferOnlyBelowTheWaist` still catches (it does not know a
pixel's colour is meaningless once its alpha is 0). Caught in CI once (U6): the committed PNG
BYTES were already byte-identical above row 31 (verified directly, bypassing Godot), so the
generator's grids were never the bug -- the fix lives in each `.import` sidecar
(`process/fix_alpha_border=false`), not here. Every `town2d-hero-*_step` pair, present or
future, needs that setting or the walk-frame test can fail on a phantom diff.

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
# Unchanged since the quality pass -- U6 reuses every tone verbatim (never picks a new hue).
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

WIDTH, HEIGHT = 26, 44

# ── row-construction helpers (U6 — see module doc's "U6 SCALE-UP" section for the rationale) ────


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
    for a mostly-symmetric torso row that needs one or two small asymmetric details (the ember
    rim light, which is upper-left ONLY per the palette's own doc; a rune or trace that should
    land as a single pixel rather than mirrored into a pair; a blade hilt poking out at a fixed
    column) without hand-counting the whole row's dot padding to find the right index."""
    chars = list(centered(mirror(left), width))
    for index, char in overrides.items():
        chars[index] = char
    return row("".join(chars), width)


EMPTY_MARGIN = ["." * WIDTH, "." * WIDTH]  # rows 0-1, every class

# ── shared lower body: LEGS (vanguard/sentinel/striker/skirmisher) ───────────────────────────────
# Rows 31-43. Four rows of thigh->shin gradient, three of ankle, boots, soles -- one block shared
# by every humanoid class exactly as the pre-U6 file already shared near-identical leg rows
# between vanguard/striker (only the robed mystic differed). STEP spreads the legs apart with a
# growing gap (the walking stride), matching the original VANGUARD_STEP/STRIKER_STEP idiom scaled
# up, and shares rows 0-30 with LEGS_BASE byte-for-byte down through row 31's shared top-of-thigh
# row -- so base and step frames provably differ ONLY at or below row 32.
LEGS_BASE = [
    row(centered(mirror("ollmdo"))),  # 31 thigh
    row(centered(mirror("ollmdo"))),  # 32 thigh
    row(centered(mirror("ollmdo"))),  # 33 thigh
    row(centered(mirror("odmido"))),  # 34 thigh -> shin
    row(centered(mirror("odmido"))),  # 35 shin
    row(centered(mirror("odmido"))),  # 36 shin
    row(centered(mirror("odmido"))),  # 37 shin
    row(centered(mirror("odmido"))),  # 38 shin
    row(centered(mirror("odmdo"))),  # 39 ankle
    row(centered(mirror("odmdo"))),  # 40 ankle
    row(centered(mirror("odmdo"))),  # 41 ankle
    row(centered(mirror("ohhdo"))),  # 42 boots
    row(centered("o" * 10)),  # 43 soles
]
assert len(LEGS_BASE) == 13

LEGS_STEP = [
    row(centered(mirror("ollmdo"))),  # 31 shared top (identical to LEGS_BASE)
    row("......" + "ollmdo" + ".." + "odmllo" + "......"),  # 32 stride: gap opens
    row("....." + "ollmdo" + "...." + "odmllo" + "....."),  # 33 gap wider
    row("....." + "odmido" + "...." + "odimdo" + "....."),  # 34 shin, same spacing
    row("....." + "odmido" + "...." + "odimdo" + "....."),  # 35
    row("...." + "odmido" + "......" + "odimdo" + "...."),  # 36 gap widest (shin)
    row("...." + "odmido" + "......" + "odimdo" + "...."),  # 37
    row("...." + "odmido" + "......" + "odimdo" + "...."),  # 38
    row("...." + "odmdo" + "........" + "odmdo" + "...."),  # 39 ankle, narrower leg
    row("...." + "odmdo" + "........" + "odmdo" + "...."),  # 40
    row("...." + "odmdo" + "........" + "odmdo" + "...."),  # 41
    row("...." + "ohhdo" + "........" + "odhho" + "...."),  # 42 boots, offset stride
    row("...." + "ooooo" + "........" + "ooooo" + "...."),  # 43 soles
]
assert len(LEGS_STEP) == 13

# ── shared lower body: HEM (mystic/occultist — the robe hovers rather than steps) ────────────────
# Rows 31-43. Widening bell-shaped skirt (l/m/i/d tones), a flat dark hem edge and ground-contact
# outline -- scaled up from the original MYSTIC's own rows 25-35. Base and step are identical
# above the skirt; the step frame sways the whole skirt left/right by a few columns per row
# (`sway`) rather than swapping legs, exactly as the original MYSTIC_STEP's own doc explains:
# the robe hides the legs entirely, so a leg-swap frame would be invisible -- the hem itself has
# to carry the motion.
HEM_BASE = [
    row(centered(mirror("ooilmmi"))),  # 31 w14
    row(centered(mirror("ooilmmi"))),  # 32 w14
    row(centered(mirror("oollmmid"))),  # 33 w16
    row(centered(mirror("oollmmid"))),  # 34 w16
    row(centered(mirror("oolllmmid"))),  # 35 w18
    row(centered(mirror("oolllmmid"))),  # 36 w18
    row(centered(mirror("ooilllmmid"))),  # 37 w20
    row(centered(mirror("ooilllmmid"))),  # 38 w20
    row(centered(mirror("oodilllmmid"))),  # 39 w22
    row(centered(mirror("oodilllmmid"))),  # 40 w22
    row(centered(mirror("ooddilllmmid"))),  # 41 w24
    row(centered("d" * 24)),  # 42 hem edge, flat dark
    row(centered("o" * 24)),  # 43 ground contact
]
assert len(HEM_BASE) == 13


def _sway(s: str, shift: int) -> str:
    """Shifts an already-centered row's content left/right by `shift` columns, refilling the
    vacated side with transparent dots -- the hem-sway motion HEM_STEP uses in place of a leg
    swap (see HEM_BASE's doc)."""
    if shift == 0:
        return s
    if shift > 0:
        return s[shift:] + "." * shift
    n = -shift
    return "." * n + s[:-n]


HEM_STEP = [
    row(_sway(centered(mirror("ooilmmi")), 0)),  # 31 shared top (identical to HEM_BASE)
    row(_sway(centered(mirror("ooilmmi")), -1)),  # 32 sway begins
    row(_sway(centered(mirror("oollmmid")), -1)),  # 33
    row(_sway(centered(mirror("oollmmid")), -1)),  # 34
    row(_sway(centered(mirror("oolllmmid")), -2)),  # 35 sway widens
    row(_sway(centered(mirror("oolllmmid")), -2)),  # 36
    row(_sway(centered(mirror("ooilllmmid")), -2)),  # 37
    row(_sway(centered(mirror("ooilllmmid")), -2)),  # 38
    row(_sway(centered(mirror("oodilllmmid")), -1)),  # 39 sway settles
    row(_sway(centered(mirror("oodilllmmid")), -1)),  # 40
    row(_sway(centered(mirror("ooddilllmmid")), -1)),  # 41
    row(_sway(centered("d" * 24), 0)),  # 42 hem edge
    row(_sway(centered("o" * 24), 0)),  # 43 ground contact
]
assert len(HEM_STEP) == 13

# ================================================================================================
# VANGUARD — closed great-helm w/ visor slit, slab shield on the left arm, rune+trace on the
# chest. Redrawn from the 20x36 quality-pass grid at the new 26x44 canvas; same silhouette idiom
# (visor slit, pauldrons, shield-face highlight, boss trace+rune), more native pixels to draw it.
# ================================================================================================
HEAD_VANGUARD = [
    row(centered("o" * 10)),  # 2 crown
    row(centered(mirror("ohhll"))),  # 3
    row(centered(mirror("ohllm"))),  # 4
    row(centered(mirror("ohloo"))),  # 5 visor slit
    row(centered(mirror("ohloo"))),  # 6 visor slit
    row(centered(mirror("ohlmi"))),  # 7
    row(centered(mirror("ohlm"))),  # 8 taper
    row(centered(mirror("ohl"))),  # 9 chin
    row(centered("o" * 6)),  # 10 neck gap
    row(centered(mirror("ommll"))),  # 11 gorget
    row(centered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(HEAD_VANGUARD) == 11

# Every torso row 14-29 = LEFTPART (10 chars: pauldron -> shield -> taper) + FLANK (9 chars, the
# right-side chest/arm strip — held CONSTANT so the body's right edge is one straight line while
# only the shield side bulges). A fully ad-hoc first draft (no shared FLANK) produced a
# ragged-staircase right edge, caught by rendering a preview, not by reading the grid back.
FLANK = "lllmiddlo"  # right chest/arm strip, 9 chars
FLANK_TRACE = "tllmiddlo"  # same strip, first char swapped for the one coolant trace
FLANK_RUNE = "rllmiddlo"  # same strip, first char swapped for the one rune glyph

TORSO_VANGUARD = [
    row(centered(mirror("oooimmlll"))),  # 13 shoulders (symmetric)
    row_left("oehhlliddl" + FLANK),  # 14 pauldrons, ember upper-left
    row_left("oehllmdidl" + FLANK),  # 15 pauldrons
    row_left("ooollmdidl" + FLANK_RUNE),  # 16 chest top, rune at the heart
    row_left("ooollmdidl" + FLANK_TRACE),  # 17 chest, coolant trace
    row_left("oooollmdid" + FLANK),  # 18 chest taper, shield about to begin
    row_left("oooooollmd" + FLANK),  # 19 shield begins (full width, left edge at col 0)
    row_left("ohhhmmilld" + FLANK),  # 20 shield face, lit (highlight band)
    row_left("ohhllmilld" + FLANK),  # 21
    row_left("ohlltmidld" + FLANK),  # 22 shield boss, coolant trace
    row_left("ohllrmidld" + FLANK),  # 23 shield boss, rune
    row_left("ohlllmidld" + FLANK),  # 24
    row_left("ohlllmidld" + FLANK),  # 25
    row_left("ohlllmidld" + FLANK),  # 26
    row_left(".ooollmdi" + FLANK),  # 27 shield ends, receding
    row_left("...oollmd" + FLANK),  # 28 waist taper
    row_left(".....oolm" + FLANK),  # 29 waist taper, near hip
    row(centered(mirror("ooilmmi"))),  # 30 hips (feeds LEGS row 31)
]
assert len(TORSO_VANGUARD) == 18

VANGUARD = EMPTY_MARGIN + HEAD_VANGUARD + TORSO_VANGUARD + LEGS_BASE
VANGUARD_STEP = EMPTY_MARGIN + HEAD_VANGUARD + TORSO_VANGUARD + LEGS_STEP

# ================================================================================================
# SENTINEL — bronze bulwark (add-on class, new town body). Closed FULL helm with no visor slit
# (a smooth plate reads as more armored than the Vanguard's slit), a bigger/longer tower shield
# (shield rows span more of the torso, LEFTPART one char wider) — matching its data: "soaks more,
# hits slower" than the Vanguard (SentinelClass.cs).
# ================================================================================================
HEAD_SENTINEL = [
    row(centered("o" * 12)),  # 2 crown (wider)
    row(centered(mirror("oohhll"))),  # 3
    row(centered(mirror("oohllm"))),  # 4
    row(centered(mirror("oohlmi"))),  # 5 no slit -- full plate
    row(centered(mirror("oohlmi"))),  # 6
    row(centered(mirror("oohlmd"))),  # 7 cheek shadow
    row(centered(mirror("oohlm"))),  # 8 taper
    row(centered(mirror("oohl"))),  # 9 chin
    row(centered("o" * 6)),  # 10 neck gap
    row(centered(mirror("oommll"))),  # 11 gorget (wider)
    row(centered(mirror("ooiimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(HEAD_SENTINEL) == 11

FLANK_SEN = "lllmiddlo"
FLANK_SEN_TRACE = "tllmiddlo"
FLANK_SEN_RUNE = "rllmiddlo"

TORSO_SENTINEL = [
    row(centered(mirror("ooidmmlll"))),  # 13 shoulders (symmetric)
    row_left("oehhlliddli" + FLANK_SEN),  # 14 pauldrons, ember upper-left
    row_left("oehllmdidli" + FLANK_SEN),  # 15 pauldrons
    row_left("oooooollmdi" + FLANK_SEN_RUNE),  # 16 shield begins early + rune
    row_left("ohhhmmilldi" + FLANK_SEN_TRACE),  # 17 shield face lit + trace
    row_left("ohhllmilldi" + FLANK_SEN),  # 18
    row_left("ohlllmidldi" + FLANK_SEN),  # 19
    row_left("ohlllmidldi" + FLANK_SEN),  # 20
    row_left("ohlllmidldi" + FLANK_SEN),  # 21
    row_left("ohlllmidldi" + FLANK_SEN),  # 22
    row_left("ohlllmidldi" + FLANK_SEN),  # 23
    row_left("ohlllmidldi" + FLANK_SEN),  # 24
    row_left("ohlllmidldi" + FLANK_SEN),  # 25
    row_left("ohlllmidldi" + FLANK_SEN),  # 26 (shield spans longer than the Vanguard's)
    row_left(".ooollmdid" + FLANK_SEN),  # 27 shield ends, receding
    row_left("...oollmdi" + FLANK_SEN),  # 28 waist taper
    row_left(".....oolmd" + FLANK_SEN),  # 29 waist taper
    row(centered(mirror("ooilmmi"))),  # 30 hips (feeds LEGS row 31)
]
assert len(TORSO_SENTINEL) == 18

SENTINEL = EMPTY_MARGIN + HEAD_SENTINEL + TORSO_SENTINEL + LEGS_BASE
SENTINEL_STEP = EMPTY_MARGIN + HEAD_SENTINEL + TORSO_SENTINEL + LEGS_STEP

# ================================================================================================
# STRIKER — hooded duelist: pointed hood with shadowed eyes, crossed strap across the chest, dual
# blade hilts at the hips (no shield -- both arms symmetric, leaner silhouette than the anchors).
# Redrawn from the 20x36 quality-pass grid at the new 26x44 canvas.
# ================================================================================================
HEAD_STRIKER = [
    row(centered("o" * 8)),  # 2 hood point
    row(centered(mirror("ohhl"))),  # 3
    row(centered(mirror("ohhll"))),  # 4 hood widens
    row(centered(mirror("ohllm"))),  # 5
    row(centered(mirror("ohlmo"))),  # 6 eyes in shadow
    row(centered(mirror("ollm"))),  # 7 taper (jaw under hood)
    row(centered(mirror("ollm"))),  # 8 hold
    row(centered(mirror("oll"))),  # 9 chin
    row(centered("o" * 6)),  # 10 neck gap
    row(centered(mirror("ommll"))),  # 11 gorget
    row(centered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(HEAD_STRIKER) == 11

TORSO_STRIKER = [
    row(centered(mirror("ooimlll"))),  # 13 shoulders (symmetric, narrower than the anchors)
    overlay("oohlliddo", {6: "e"}),  # 14 shoulders — ember upper-left ONLY (single override,
    #    not mirrored — see PALETTE's 'e' doc: "upper-left only")
    overlay("oohllmdo", {6: "e"}),  # 15 ember continues
    overlay("oollmiddo", {12: "t"}),  # 16 coolant trace, single centre pixel
    overlay("oollmiddo", {12: "r"}),  # 17 rune at the belt, single centre pixel
    row(centered(mirror("oollmido"))),  # 18 chest
    row(centered(mirror("oollmido"))),  # 19 hold
    row(centered(mirror("oolmiddo"))),  # 20 waist narrows (leaner than the anchors)
    row(centered(mirror("oolmiddo"))),  # 21 hold
    overlay("oolmiddo", {6: "o", 7: "h", 8: "h", 17: "o", 18: "h", 19: "h"}),  # 22 hilts appear
    overlay("oolmiddo", {6: "o", 7: "d", 8: "o", 17: "o", 18: "d", 19: "o"}),  # 23 hilts
    overlay("oolmiddo", {7: "o", 8: "d", 18: "d", 19: "o"}),  # 24 blades taper
    overlay("oolmiddo", {7: "d", 8: "o", 18: "o", 19: "d"}),  # 25 blade tips (no enclosed hole)
    row(centered(mirror("oolmiddo"))),  # 26 torso resumes below the hilts
    row(centered(mirror("oolmiddo"))),  # 27 hold
    row(centered(mirror("oolmido"))),  # 28 waist taper
    row(centered(mirror("oolmdo"))),  # 29 waist taper, near hip
    row(centered(mirror("ooilmmi"))),  # 30 hips (feeds LEGS row 31)
]
assert len(TORSO_STRIKER) == 18

STRIKER = EMPTY_MARGIN + HEAD_STRIKER + TORSO_STRIKER + LEGS_BASE
STRIKER_STEP = EMPTY_MARGIN + HEAD_STRIKER + TORSO_STRIKER + LEGS_STEP

# ================================================================================================
# SKIRMISHER — light flanker (add-on class, new town body). An open cap rather than a deep hood
# (more face shows than the Striker's shadow-eyes — "a mobility lean" per SkirmisherClass.cs), a
# quiver strap on the back (upper-right bump), crossed daggers at the belt (a centred X, not hip
# hilts — a different silhouette read than the Striker's paired blades so the two don't twin).
# ================================================================================================
HEAD_SKIRMISHER = [
    row(centered("o" * 8)),  # 2 cap crown
    row(centered(mirror("ohhl"))),  # 3
    row(centered(mirror("ohll"))),  # 4 cap widens
    row(centered(mirror("ohlm"))),  # 5 face lighter than the Striker's shadow-eyes
    row(centered(mirror("ollm"))),  # 6
    row(centered(mirror("oll"))),  # 7 taper
    row(centered(mirror("oll"))),  # 8 hold
    row(centered(mirror("oll"))),  # 9 chin
    row(centered("o" * 6)),  # 10 neck gap
    row(centered(mirror("ommll"))),  # 11 gorget
    row(centered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(HEAD_SKIRMISHER) == 11

TORSO_SKIRMISHER = [
    row(centered(mirror("ooimlll"))),  # 13 shoulders (leaner, symmetric)
    overlay("oohlliddo", {6: "e", 20: "i", 21: "d"}),  # 14 ember left, quiver right begins
    overlay("oohllmdo", {19: "i", 20: "d"}),  # 15 quiver strap continues
    overlay("oolmiddo", {12: "t", 18: "i", 19: "d"}),  # 16 coolant trace, quiver
    overlay("oolmiddo", {12: "r"}),  # 17 rune at the belt
    row(centered(mirror("oolmido"))),  # 18 chest, leaner taper
    row(centered(mirror("oolmido"))),  # 19 hold
    row(centered(mirror("olmido"))),  # 20 waist narrows
    row(centered(mirror("olmido"))),  # 21 hold
    overlay("olmido", {10: "o", 11: "h", 14: "h", 15: "o"}),  # 22 crossed daggers appear
    overlay("olmido", {9: "o", 10: "d", 15: "d", 16: "o"}),  # 23 daggers cross
    overlay("olmido", {11: "o", 12: "d", 13: "d", 14: "o"}),  # 24 blade tips converge
    row(centered(mirror("olmido"))),  # 25 daggers end
    row(centered(mirror("olmido"))),  # 26 waist
    row(centered(mirror("olmdo"))),  # 27 waist taper
    row(centered(mirror("olmdo"))),  # 28 hold
    row(centered(mirror("olmo"))),  # 29 near hip
    row(centered(mirror("ooilmmi"))),  # 30 hips (feeds LEGS row 31)
]
assert len(TORSO_SKIRMISHER) == 18

SKIRMISHER = EMPTY_MARGIN + HEAD_SKIRMISHER + TORSO_SKIRMISHER + LEGS_BASE
SKIRMISHER_STEP = EMPTY_MARGIN + HEAD_SKIRMISHER + TORSO_SKIRMISHER + LEGS_STEP

# ================================================================================================
# MYSTIC — deep cowl, face is shadow only, a hand reaches out and a staff enters the frame on the
# right (outside the robe's own silhouette, like the original's "...o" / "...or" staff pieces),
# rune between traces on the chest. Redrawn from the 20x36 quality-pass grid at 26x44. The robe
# hovers rather than steps -- see HEM_STEP's own doc.
# ================================================================================================
HEAD_MYSTIC = [
    row(centered("o" * 10)),  # 2 crown
    row(centered(mirror("ohhlll"))),  # 3
    row(centered(mirror("ohhlllm"))),  # 4 cowl flares
    row(centered(mirror("ohlmoo"))),  # 5 shadow face starts
    row(centered(mirror("ohlmoo"))),  # 6 face is shadow
    row(centered(mirror("ohllm"))),  # 7 chin under cowl
    row(centered(mirror("ollm"))),  # 8 taper
    row(centered(mirror("oll"))),  # 9 chin
    row(centered("o" * 6)),  # 10 neck gap
    row(centered(mirror("ommll"))),  # 11 gorget
    row(centered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(HEAD_MYSTIC) == 11

TORSO_MYSTIC = [
    row(centered(mirror("ooiimmll"))),  # 13 shoulders (robed, wider)
    overlay("oohlliddo", {6: "e"}),  # 14 ember upper-left; staff not yet in frame at this row
    overlay("oohllmdo", {6: "e", 21: "m", 22: "m", 23: "d", 24: "d"}),  # 15 hand reaches, staff enters
    overlay("oolmiddo", {12: "t", 21: "m", 22: "d", 23: "d", 24: "d"}),  # 16 coolant trace + staff, gripped
    overlay("oolmiddo", {12: "r", 24: "r"}),  # 17 rune on chest + on the staff
    overlay("oollmiddo", {24: "d"}),  # 18 chest widens, staff
    overlay("oollmiddo", {24: "d"}),  # 19 hold, staff
    overlay("oolllmiddo", {24: "d"}),  # 20 robe widens, staff
    overlay("oolllmiddo", {24: "d"}),  # 21 hold, staff
    overlay("ooilllmiddo", {24: "d"}),  # 22 robe widens, staff
    overlay("ooilllmiddo", {24: "d"}),  # 23 hold, staff
    overlay("ooilllmiddo", {24: "d"}),  # 24 hold, staff
    overlay("ooilllmiddo", {24: "d"}),  # 25 hold, staff (last row with the staff in frame)
    row(centered(mirror("ooilllmiddo"))),  # 26 robe continues, staff released
    row(centered(mirror("oolllmiddo"))),  # 27 robe cinches at the waist
    row(centered(mirror("oollmiddo"))),  # 28 waist
    row(centered(mirror("oolmiddo"))),  # 29 waist, narrowest point (belt)
    row(centered(mirror("ooimmll"))),  # 30 hip lead-in (feeds HEM row 31)
]
assert len(TORSO_MYSTIC) == 18

MYSTIC = EMPTY_MARGIN + HEAD_MYSTIC + TORSO_MYSTIC + HEM_BASE
MYSTIC_STEP = EMPTY_MARGIN + HEAD_MYSTIC + TORSO_MYSTIC + HEM_STEP

# ================================================================================================
# OCCULTIST — deep cowl with two small horn-tips (vs the Mystic's smooth dome), a paired
# rune-and-eye glyph on the chest (vs the Mystic's single rune), the same reaching-hand-and-staff
# silhouette. Shares the Mystic's HEM (robe skirt) — the same precedent as the four humanoid
# classes sharing LEGS: the distinguishing read is the head + chest icon, not the hem.
# ================================================================================================
HEAD_OCCULTIST = [
    overlay("o" * 4, {7: "d", 18: "d"}),  # 2 crown + two small horn-tips
    row(centered(mirror("ohhll"))),  # 3 cowl (pointier than the Mystic's)
    row(centered(mirror("ohhllm"))),  # 4 cowl flares sharply
    row(centered(mirror("ohlmoo"))),  # 5 shadow face starts
    row(centered(mirror("ohlmoo"))),  # 6 face is shadow
    row(centered(mirror("ohllm"))),  # 7 chin under cowl
    row(centered(mirror("ollm"))),  # 8 taper
    row(centered(mirror("oll"))),  # 9 chin
    row(centered("o" * 6)),  # 10 neck gap
    row(centered(mirror("ommll"))),  # 11 gorget
    row(centered(mirror("ooimmll"))),  # 12 gorget -> shoulder lead-in
]
assert len(HEAD_OCCULTIST) == 11

TORSO_OCCULTIST = [
    row(centered(mirror("ooiimmll"))),  # 13 shoulders (robed, wider)
    overlay("oohlliddo", {6: "e"}),  # 14 ember upper-left
    overlay("oohllmdo", {6: "e", 21: "m", 22: "m", 23: "d", 24: "d"}),  # 15 hand reaches, staff
    overlay("oolmiddo", {11: "r", 13: "d", 21: "m", 22: "d", 23: "d", 24: "d"}),  # 16 rune+eye, staff
    overlay("oolmiddo", {11: "d", 13: "r", 24: "r"}),  # 17 rune+eye glyph, staff continues
    overlay("oollmiddo", {24: "d"}),  # 18 chest widens, staff
    overlay("oollmiddo", {24: "d"}),  # 19 hold, staff
    overlay("oolllmiddo", {24: "d"}),  # 20 robe widens, staff
    overlay("oolllmiddo", {24: "d"}),  # 21 hold, staff
    overlay("ooilllmiddo", {24: "d"}),  # 22 robe widens, staff
    overlay("ooilllmiddo", {24: "d"}),  # 23 hold, staff
    overlay("ooilllmiddo", {24: "d"}),  # 24 hold, staff
    overlay("ooilllmiddo", {24: "d"}),  # 25 hold, staff (last row with the staff in frame)
    row(centered(mirror("ooilllmiddo"))),  # 26 robe continues, staff released
    row(centered(mirror("oolllmiddo"))),  # 27 robe cinches at the waist
    row(centered(mirror("oollmiddo"))),  # 28 waist
    row(centered(mirror("oolmiddo"))),  # 29 waist, narrowest point (belt)
    row(centered(mirror("ooimmll"))),  # 30 hip lead-in (feeds HEM row 31)
]
assert len(TORSO_OCCULTIST) == 18

OCCULTIST = EMPTY_MARGIN + HEAD_OCCULTIST + TORSO_OCCULTIST + HEM_BASE
OCCULTIST_STEP = EMPTY_MARGIN + HEAD_OCCULTIST + TORSO_OCCULTIST + HEM_STEP

SPRITES = {
    "town2d-hero-vanguard": VANGUARD,
    "town2d-hero-vanguard_step": VANGUARD_STEP,
    "town2d-hero-sentinel": SENTINEL,
    "town2d-hero-sentinel_step": SENTINEL_STEP,
    "town2d-hero-striker": STRIKER,
    "town2d-hero-striker_step": STRIKER_STEP,
    "town2d-hero-skirmisher": SKIRMISHER,
    "town2d-hero-skirmisher_step": SKIRMISHER_STEP,
    "town2d-hero-mystic": MYSTIC,
    "town2d-hero-mystic_step": MYSTIC_STEP,
    "town2d-hero-occultist": OCCULTIST,
    "town2d-hero-occultist_step": OCCULTIST_STEP,
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
