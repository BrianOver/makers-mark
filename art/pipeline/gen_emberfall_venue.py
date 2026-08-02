#!/usr/bin/env python
"""Author the Emberfall Foundry venue art set (task #80) as explicit pixel grids.

WHY THIS IS NOT THE SDXL PIPELINE
----------------------------------
Every Gloomwood/Sunken Crypt backdrop and monster in `godot/assets/art/` came from the local
ComfyUI/SDXL chain (see `art/pipeline/README.md`) -- a GPU-bound, agent-driven generate/curate loop.
This session has no GPU/ComfyUI access (repo hard rule), so Emberfall is hand-authored instead, same
recipe `gen_town_sprites.py` and `gen-market.py` already proved out for GPU-less assets: explicit
pixel primitives (filled ellipses + rects), a small fixed palette, reviewable in a diff, editable
without a GPU, byte-identical on every machine, and a `--check` drift guard.

WHY THESE COLOURS
------------------
Every colour below is one of two things, never picked by eye:
  1. A style-bible hex (docs/style-bible.md) INDEPENDENTLY CONFIRMED present in already-committed
     pixels: VOID/BONE/EMBER all appear verbatim in `godot/assets/art/town2d-forge.png`'s own pixel
     data (sampled directly with PIL before writing this -- see the exact percentages in the PR),
     and EMBER is also `godot/assets/icons/gold.svg`'s literal fill. These are not "close enough" --
     they are the identical (R,G,B) tuples already on disk.
  2. The `den` palette family's own canonical anchor hexes, already checked into
     `art/GameArt/PaletteRegistry.cs` (RUST = Blood #b5462f, the Striker role colour; CHARCOAL
     #2b2b2b; ASH #6e655c) and `docs/design/2026-07-18-variety-tone-direction.md`'s family registry
     table -- the SAME single source of truth `EmberfallSpecs.cs`'s `PaletteId: "den"` points at.
     Den has never been rendered before (Emberfall is its first asset), so CHARCOAL/ASH have no
     prior pixel to sample from; using the family's own already-reviewed, already-merged anchor
     hexes is the equivalent move for a first-use colour, not an eyeballed pick.
COOLANT and ARCANE are the same "one circuit trace, one rune glyph" accent motif every other
hand-authored sprite in this repo carries (gen_town_sprites.py, gen-market.py) -- kept here purely
for cross-asset family consistency (Emberfall is "abandoned DWARVEN smelting halls", so a dwarven
rune glyph fits the flavor text directly).

WHY THESE ARE DISTINCT FROM GLOOMWOOD/CRYPT
--------------------------------------------
Gloomwood is moss-green + verdigris-teal (damp, overgrown). The Crypt is bone/parchment + cold-cyan
(drowned, funerary-stone). Emberfall is rust-red + charcoal + hot coal-orange (den family) -- no
green, no cyan, no cold anywhere in this file. Same pixel-grid technique and the same universal
Void-outline/Bone-highlight/one-Coolant-trace/one-Arcane-rune motifs as every other venue, so it
reads as the same game -- just the one built around a foundry that never finished cooling.

ROSTER (verbatim from sim/GameSim/Venues/Emberfall/EmberfallFoundryVenue.cs's floor switch --
never invented; see that file and PR #80's own roster table)
-----------------------------------------------------------------------------------------------
F1 Cinder Imp | F2 Slag Hound | F3 The Bellows-Mad | F4 Molten Archivist |
F5 The Undying Forge-Heart (boss)

SIZES
-----
`emberfall-backdrop` is 160x160 to match `mine-backdrop.png` / `gloomwood-backdrop.png` /
`sunkencrypt-backdrop.png` EXACTLY -- all three siblings share that one identical pixel footprint
(verified with PIL before writing this), so this is not a guess. The five monster portraits have no
single equivalent sibling dimension to match (each SDXL sibling is independently content-trimmed,
from 686x945 to 1024x1024) -- since this is a fixed-primitive pixel grid rather than a trimmed
photographic cutout, a uniform canvas per creature (bumped up for the F5 boss, mirroring the
Mine's own bigger-boss convention -- town2d-monster-forgeworm 64x110 vs town2d-monster-cave-rat
60x41) is the honest equivalent, judged at BestiaryPanel's actual 256x256 portrait box
(KeepAspectCentered -- every size here downscales into that box, never upscales past 1.0, so there
is no nearest-neighbour decimation risk regardless of exact pixel count).

NORMAL MAPS
-----------
The five monster diffuses get a `_n.png` sibling via the SAME `art/pipeline/normalmap.py` Sobel
tool the SDXL pipeline's stage 3 uses (pure numpy/Pillow, no GPU, no ComfyUI) -- this script shells
out to it after writing each diffuse. The backdrop does not (AssetSpec.NormalMap=false, matching
every sibling backdrop -- a flat far plane, never lit).

Usage:
    python art/pipeline/gen_emberfall_venue.py [--out DIR] [--check] [--no-normals]

    --check       render in memory and compare against the committed PNGs (diffuse content only);
                  non-zero exit on any difference or enclosed-hole defect, writes nothing.
    --no-normals  skip the normalmap.py subprocess calls (diffuse-only smoke run).
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys

from PIL import Image

# ── palette (see module doc above for full provenance) ──────────────────────────────────────────
CLEAR = (0, 0, 0, 0)
VOID = (20, 15, 31, 255)        # style-bible Void #140f1f -- verified verbatim in town2d-forge.png
BONE = (216, 207, 224, 255)     # style-bible Bone #d8cfe0 -- verified verbatim in town2d-forge.png
EMBER = (224, 145, 63, 255)     # style-bible Ember #e0913f -- verified verbatim in town2d-forge.png
                                 # AND godot/assets/icons/gold.svg's literal fill
RUST = (181, 70, 47, 255)       # PaletteRegistry.Den "rust" anchor = style-bible Blood #b5462f
CHARCOAL = (43, 43, 43, 255)    # PaletteRegistry.Den "charcoal" anchor #2b2b2b
ASH = (110, 101, 92, 255)       # PaletteRegistry.Den "ash" anchor #6e655c
COOLANT = (63, 176, 172, 255)   # style-bible Coolant -- the one circuit-trace accent
ARCANE = (107, 76, 154, 255)    # style-bible Arcane -- the one dwarven-rune accent

OUT_DIR_DEFAULT = os.path.join("godot", "assets", "art")
NORMALMAP_SCRIPT = os.path.join("art", "pipeline", "normalmap.py")
NORMAL_STRENGTH = "2.5"  # matches every other committed normal map in this repo (README convention)


# ── drawing primitives (mirrors art/pipeline/gen-market.py's rect()/outline() idiom) ─────────────

def blank(w: int, h: int) -> Image.Image:
    return Image.new("RGBA", (w, h), CLEAR)


def rect(px, w, h, x0, y0, x1, y1, color) -> None:
    """Inclusive filled rectangle, clipped to the canvas."""
    for y in range(max(0, y0), min(h - 1, y1) + 1):
        for x in range(max(0, x0), min(w - 1, x1) + 1):
            px[x, y] = color


def outline_rect(px, w, h, x0, y0, x1, y1, color=VOID) -> None:
    """Inclusive 1px rectangle border."""
    for x in range(max(0, x0), min(w - 1, x1) + 1):
        if 0 <= y0 < h:
            px[x, y0] = color
        if 0 <= y1 < h:
            px[x, y1] = color
    for y in range(max(0, y0), min(h - 1, y1) + 1):
        if 0 <= x0 < w:
            px[x0, y] = color
        if 0 <= x1 < w:
            px[x1, y] = color


def ellipse(px, w, h, cx, cy, rx, ry, color) -> None:
    """Filled ellipse (simple distance test -- small canvases, cost is irrelevant)."""
    if rx <= 0 or ry <= 0:
        return
    for y in range(max(0, cy - ry), min(h - 1, cy + ry) + 1):
        for x in range(max(0, cx - rx), min(w - 1, cx + rx) + 1):
            dx = (x - cx) / rx
            dy = (y - cy) / ry
            if dx * dx + dy * dy <= 1.0:
                px[x, y] = color


def rounded_body(px, w, h, cx, cy, rx, ry, color, rim=BONE, edge=VOID) -> None:
    """Outlined, rim-lit filled ellipse -- the fix for the exact "flat cardboard cutout" defect
    `gen_town_sprites.py`'s own quality-pass note documents (a single flat fill tone reads as no
    volume no matter how crisp the outline is). Three layers: an outline ellipse, a rim-light
    ellipse inset from it, and the base colour inset further AND offset toward lower-right --
    the offset is what leaves a `rim` sliver visible only on the upper-left arc (this repo's
    established light-direction convention, see gen_town_sprites.py's pauldron/rivet comments)
    instead of a uniform ring all the way round."""
    ellipse(px, w, h, cx, cy, rx, ry, edge)
    ellipse(px, w, h, cx, cy, max(1, rx - 2), max(1, ry - 2), rim)
    ellipse(px, w, h, cx + 2, cy + 2, max(1, rx - 4), max(1, ry - 4), color)


def dot(px, w, h, x, y, color) -> None:
    if 0 <= x < w and 0 <= y < h:
        px[x, y] = color


def line(px, w, h, x0, y0, x1, y1, color) -> None:
    """Simple integer-step line (Bresenham-lite) for cracks/trim -- short segments only."""
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(steps + 1):
        x = round(x0 + (x1 - x0) * i / steps)
        y = round(y0 + (y1 - y0) * i / steps)
        dot(px, w, h, x, y, color)


def holes(im: Image.Image) -> list[tuple[int, int]]:
    """Transparent pixels fully enclosed by opaque ones on all four sides -- same guard
    `gen-market.py` runs (an invisible gap the editor's dark background can hide). The backdrop
    is RGB (no alpha, matching its siblings) and is opaque by construction, so it has nothing to
    check -- the guard is only meaningful for the RGBA monster cutouts."""
    if im.mode != "RGBA":
        return []

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


# ── the backdrop (160x160 -- matches mine/gloomwood/sunkencrypt-backdrop.png exactly) ────────────

def render_backdrop() -> Image.Image:
    w = h = 160
    im = Image.new("RGB", (w, h), VOID[:3])
    px = im.load()

    # Deep ash-gloom upper hall, lit ember floor below -- one hard band split, no gradient blend
    # (matches the block-fill idiom every other hand-authored asset in this repo uses).
    rect(px, w, h, 0, 0, w - 1, 95, CHARCOAL[:3])
    rect(px, w, h, 0, 0, w - 1, 40, VOID[:3])

    # Two furnace archway silhouettes, each glowing at the mouth.
    for ax in (28, 118):
        rect(px, w, h, ax - 18, 30, ax + 18, 95, VOID[:3])
        rect(px, w, h, ax - 12, 44, ax + 12, 88, CHARCOAL[:3])
        rect(px, w, h, ax - 7, 58, ax + 7, 88, EMBER[:3])
        rect(px, w, h, ax - 4, 62, ax + 4, 84, RUST[:3])

    # Molten channel crossing the floor -- a fixed (never random) undulation so a regeneration is
    # byte-identical, same convention gen-market.py's awning stripe phase uses.
    wobble = [0, 0, 1, 1, 2, 2, 1, 1, 0, 0, -1, -1, -2, -2, -1, -1]
    for x in range(w):
        offset = wobble[x % len(wobble)]
        y0 = 108 + offset
        rect(px, w, h, x, y0, x, y0 + 9, RUST[:3])
        rect(px, w, h, x, y0, x, y0 + 2, EMBER[:3])
        px[x, y0] = BONE[:3]  # thin bright rim where the surface catches light

    # Ash floor below the channel.
    rect(px, w, h, 0, 121, w - 1, h - 1, CHARCOAL[:3])
    rect(px, w, h, 0, 150, w - 1, h - 1, VOID[:3])

    # Scattered embers drifting up from the channel -- fixed positions (deterministic).
    embers = [
        (10, 20), (22, 55), (45, 12), (70, 30), (95, 8), (112, 50),
        (135, 18), (150, 60), (60, 70), (140, 90), (18, 85), (100, 22),
    ]
    for ex, ey in embers:
        dot(px, w, h, ex, ey, EMBER[:3])
        dot(px, w, h, ex + 1, ey, RUST[:3])

    # One dwarven rune glyph, faint on the left archway pillar -- the "one rune" motif.
    rune_x, rune_y = 12, 66
    rect(px, w, h, rune_x, rune_y, rune_x + 3, rune_y, ARCANE[:3])
    rect(px, w, h, rune_x + 1, rune_y + 1, rune_x + 2, rune_y + 3, ARCANE[:3])
    rect(px, w, h, rune_x, rune_y + 4, rune_x + 3, rune_y + 4, ARCANE[:3])

    return im


# ── F1 Cinder Imp (100x120) ──────────────────────────────────────────────────────────────────────

def render_cinder_imp() -> Image.Image:
    w, h = 100, 120
    im = blank(w, h)
    px = im.load()

    cx, cy = 50, 68
    rounded_body(px, w, h, cx, cy, 30, 32, ASH)

    # Horns.
    rect(px, w, h, cx - 22, cy - 34, cx - 16, cy - 22, VOID)
    rect(px, w, h, cx - 20, cy - 32, cx - 18, cy - 24, CHARCOAL)
    rect(px, w, h, cx + 16, cy - 34, cx + 22, cy - 22, VOID)
    rect(px, w, h, cx + 18, cy - 32, cx + 20, cy - 24, CHARCOAL)

    # Ember-glow seam cracks across the body.
    line(px, w, h, cx - 20, cy - 8, cx - 4, cy + 6, EMBER)
    line(px, w, h, cx + 6, cy - 12, cx + 20, cy + 2, EMBER)
    line(px, w, h, cx - 10, cy + 14, cx + 2, cy + 22, EMBER)

    # Big soft apologetic eyes.
    for ex in (cx - 11, cx + 11):
        ellipse(px, w, h, ex, cy - 6, 8, 9, BONE)
        ellipse(px, w, h, ex, cy - 4, 3, 4, VOID)
        dot(px, w, h, ex - 1, cy - 7, BONE)  # tiny highlight

    # Stubby arms hugging the stolen coal at its chest.
    ellipse(px, w, h, cx, cy + 18, 7, 6, EMBER)
    ellipse(px, w, h, cx, cy + 18, 4, 3, BONE)
    ellipse(px, w, h, cx - 16, cy + 20, 6, 8, ASH)
    ellipse(px, w, h, cx + 16, cy + 20, 6, 8, ASH)

    # Little feet -- bridged to the body with a solid rect first so the ellipse-vs-ellipse seam
    # (the body's tapering bottom edge) can never leave an enclosed transparent pocket between them.
    rect(px, w, h, cx - 16, cy + 26, cx + 16, cy + 34, CHARCOAL)
    ellipse(px, w, h, cx - 10, cy + 34, 7, 5, CHARCOAL)
    ellipse(px, w, h, cx + 10, cy + 34, 7, 5, CHARCOAL)

    return im


# ── F2 Slag Hound (120x100) ──────────────────────────────────────────────────────────────────────

def render_slag_hound() -> Image.Image:
    w, h = 120, 100
    im = blank(w, h)
    px = im.load()

    cx, cy = 62, 56
    rounded_body(px, w, h, cx, cy, 42, 24, CHARCOAL)

    # Head (front, facing left).
    hx, hy = 22, 48
    rounded_body(px, w, h, hx, hy, 16, 15, ASH)

    # Ears.
    rect(px, w, h, hx - 4, hy - 20, hx + 2, hy - 10, VOID)
    rect(px, w, h, hx - 2, hy - 18, hx, hy - 11, CHARCOAL)
    rect(px, w, h, hx + 10, hy - 20, hx + 16, hy - 10, VOID)
    rect(px, w, h, hx + 12, hy - 18, hx + 14, hy - 11, CHARCOAL)

    # Big loyal eyes.
    ellipse(px, w, h, hx - 2, hy - 2, 5, 6, BONE)
    ellipse(px, w, h, hx - 2, hy - 1, 2, 3, VOID)
    ellipse(px, w, h, hx + 8, hy - 2, 5, 6, BONE)
    ellipse(px, w, h, hx + 8, hy - 1, 2, 3, VOID)

    # Snout.
    rect(px, w, h, hx - 14, hy + 2, hx - 4, hy + 8, ASH)
    dot(px, w, h, hx - 13, hy + 5, VOID)

    # Molten seam cracks down the back and legs.
    line(px, w, h, cx - 20, cy - 12, cx + 20, cy - 8, EMBER)
    line(px, w, h, cx - 10, cy + 6, cx + 24, cy + 10, EMBER)

    # Legs.
    for lx in (30, 52, 76, 96):
        rect(px, w, h, lx, cy + 14, lx + 8, cy + 34, CHARCOAL)
        rect(px, w, h, lx + 2, cy + 26, lx + 6, cy + 30, EMBER)

    # Tail, ember-tipped -- a solid bridging rect rooted well inside the body first (guarantees
    # no seam gap against the ellipse's curved edge), then the tapering blobs on top.
    rect(px, w, h, cx + 20, cy - 14, cx + 40, cy - 2, CHARCOAL)
    for tx, ty in ((cx + 32, cy - 7), (cx + 36, cy - 10), (cx + 40, cy - 13), (cx + 44, cy - 16)):
        ellipse(px, w, h, tx, ty, 5, 5, CHARCOAL)
    ellipse(px, w, h, cx + 51, cy - 19, 4, 4, EMBER)

    return im


# ── F3 The Bellows-Mad (110x130) ─────────────────────────────────────────────────────────────────

def render_bellows_mad() -> Image.Image:
    w, h = 110, 130
    im = blank(w, h)
    px = im.load()

    cx = 55
    # Blocky furnace torso.
    rect(px, w, h, cx - 34, 20, cx + 34, 96, VOID)
    rect(px, w, h, cx - 31, 23, cx + 31, 93, CHARCOAL)

    # Bellows-fold stripes across the lower torso (accordion look).
    for i, y0 in enumerate(range(70, 92, 5)):
        band = ASH if i % 2 == 0 else CHARCOAL
        rect(px, w, h, cx - 31, y0, cx + 31, y0 + 3, band)

    # Rivets (small Bone dots) along the shoulders.
    for rx in range(cx - 26, cx + 27, 13):
        dot(px, w, h, rx, 27, BONE)

    # Big glowing furnace-mouth grin.
    rect(px, w, h, cx - 20, 42, cx + 20, 60, VOID)
    rect(px, w, h, cx - 17, 44, cx + 17, 58, EMBER)
    rect(px, w, h, cx - 17, 52, cx + 17, 58, RUST)

    # Big obsessive round eyes above the mouth.
    for ex in (cx - 14, cx + 14):
        ellipse(px, w, h, ex, 30, 7, 8, BONE)
        ellipse(px, w, h, ex, 31, 3, 4, VOID)

    # Stubby riveted arms.
    rect(px, w, h, cx - 46, 46, cx - 34, 74, VOID)
    rect(px, w, h, cx - 44, 48, cx - 36, 72, ASH)
    rect(px, w, h, cx + 34, 46, cx + 46, 74, VOID)
    rect(px, w, h, cx + 36, 48, cx + 44, 72, ASH)
    dot(px, w, h, cx - 40, 60, BONE)
    dot(px, w, h, cx + 40, 60, BONE)

    # Short legs.
    rect(px, w, h, cx - 24, 96, cx - 8, 118, VOID)
    rect(px, w, h, cx - 22, 98, cx - 10, 116, CHARCOAL)
    rect(px, w, h, cx + 8, 96, cx + 24, 118, VOID)
    rect(px, w, h, cx + 10, 98, cx + 22, 116, CHARCOAL)

    return im


# ── F4 Molten Archivist (100x130) ────────────────────────────────────────────────────────────────

def render_molten_archivist() -> Image.Image:
    w, h = 100, 130
    im = blank(w, h)
    px = im.load()

    cx = 50
    # Robed body, wider at the hem.
    rounded_body(px, w, h, cx, 40, 22, 22, CHARCOAL)
    rect(px, w, h, cx - 30, 40, cx + 30, 108, VOID)
    rect(px, w, h, cx - 27, 40, cx + 27, 105, CHARCOAL)
    rect(px, w, h, cx - 27, 40, cx - 15, 70, ASH)  # upper-left rim light down the robe's side

    # Scalloped hem (mirrors gen-market.py's cloth-hem trick).
    for x in range(cx - 27, cx + 28):
        dip = 3 if 1 <= (x - (cx - 27)) % 6 <= 3 else 0
        rect(px, w, h, x, 100 + dip, x, 105, ASH)
        dot(px, w, h, x, 100 + dip, VOID)

    # Ember trim tracing the robe's edges.
    line(px, w, h, cx - 27, 42, cx - 27, 100, EMBER)
    line(px, w, h, cx + 27, 42, cx + 27, 100, EMBER)

    # Hood shadow with two wary eyes peeking out.
    ellipse(px, w, h, cx, 30, 14, 13, VOID)
    for ex in (cx - 6, cx + 6):
        ellipse(px, w, h, ex, 30, 4, 5, BONE)
        ellipse(px, w, h, ex, 31, 2, 2, VOID)

    # The hoarded ledger, hugged at the chest.
    rect(px, w, h, cx - 12, 62, cx + 12, 80, VOID)
    rect(px, w, h, cx - 10, 64, cx + 10, 78, ASH)
    rect(px, w, h, cx - 8, 66, cx + 8, 76, RUST)
    line(px, w, h, cx, 66, cx, 76, BONE)  # spine page-line

    # One dwarven rune on the robe (the "one rune" motif).
    rect(px, w, h, cx - 3, 90, cx + 3, 90, ARCANE)
    rect(px, w, h, cx - 2, 91, cx + 2, 94, ARCANE)
    rect(px, w, h, cx - 3, 95, cx + 3, 95, ARCANE)

    return im


# ── F5 The Undying Forge-Heart (boss, 140x160) ───────────────────────────────────────────────────

def render_undying_forge_heart() -> Image.Image:
    w, h = 140, 160
    im = blank(w, h)
    px = im.load()

    cx, cy = 70, 90
    # Heart-shaped furnace: two upper lobes + one large lower body.
    rounded_body(px, w, h, cx - 22, cy - 34, 26, 24, CHARCOAL)
    rounded_body(px, w, h, cx + 22, cy - 34, 26, 24, CHARCOAL)
    rounded_body(px, w, h, cx, cy + 10, 46, 50, CHARCOAL)

    # Iron bands (Bone rings) -- "iron-banded" per the sim flavor text.
    for by in (cy - 6, cy + 22, cy + 46):
        rect(px, w, h, cx - 40, by, cx + 40, by + 2, BONE)

    # Glowing core, brightest at the center, veined cracks radiating outward.
    ellipse(px, w, h, cx, cy + 6, 16, 16, EMBER)
    ellipse(px, w, h, cx, cy + 6, 8, 8, BONE)
    for dx, dy in ((-30, -20), (30, -20), (-34, 20), (34, 20), (0, 44), (-14, -30), (14, -30)):
        line(px, w, h, cx, cy + 6, cx + dx, cy + 6 + dy, EMBER)
    for dx, dy in ((-30, -20), (30, -20), (-34, 20), (34, 20)):
        dot(px, w, h, cx + dx, cy + 6 + dy, RUST)

    # A pair of watching ember-slit "eyes" high on the upper lobes -- imposing, not cute.
    for ex in (cx - 22, cx + 22):
        rect(px, w, h, ex - 6, cy - 40, ex + 6, cy - 36, VOID)
        rect(px, w, h, ex - 4, cy - 39, ex + 4, cy - 37, EMBER)

    return im


# ── registry ─────────────────────────────────────────────────────────────────────────────────────

# Backdrop: diffuse only (AssetSpec.NormalMap=false, matches every sibling backdrop).
BACKDROP = {
    "emberfall-backdrop": render_backdrop,
}

# Monsters: diffuse + normal map (AssetSpec.NormalMap=true, matches every sibling monster).
MONSTERS = {
    "emberfall-cinder-imp": render_cinder_imp,
    "emberfall-slag-hound": render_slag_hound,
    "emberfall-bellows-mad": render_bellows_mad,
    "emberfall-molten-archivist": render_molten_archivist,
    "emberfall-undying-forge-heart": render_undying_forge_heart,
}


def die(message: str) -> None:
    print(f"gen_emberfall_venue.py: error: {message}", file=sys.stderr)
    raise SystemExit(1)


def make_normal_map(diffuse_path: str, normal_path: str) -> None:
    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    script = os.path.join(repo_root, NORMALMAP_SCRIPT)
    result = subprocess.run(
        [sys.executable, script, diffuse_path, normal_path, NORMAL_STRENGTH],
        capture_output=True, text=True)
    if result.returncode != 0:
        die(f"normalmap.py failed for {diffuse_path}:\n{result.stdout}\n{result.stderr}")
    print(result.stdout.strip())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=OUT_DIR_DEFAULT, help="output directory (default: godot/assets/art)")
    parser.add_argument("--check", action="store_true",
                         help="compare against committed PNGs instead of writing; non-zero exit on any difference")
    parser.add_argument("--no-normals", action="store_true",
                         help="skip generating _n.png normal maps (diffuse-only smoke run)")
    args = parser.parse_args()

    all_assets = {**BACKDROP, **MONSTERS}
    drift: list[str] = []
    hole_failures: list[str] = []

    for name, render in all_assets.items():
        image = render()

        gaps = holes(image)
        if gaps:
            hole_failures.append(f"{name}: {len(gaps)} enclosed transparent pixel(s), first at {gaps[:6]}")
            continue

        path = os.path.join(args.out, f"{name}.png")

        if args.check:
            if not os.path.exists(path):
                drift.append(f"{name}: no committed PNG at {path}")
            else:
                # Compare in whichever mode this asset actually ships (RGB for the backdrop, no
                # alpha channel to begin with; RGBA for the monster cutouts) -- comparing a 3-tuple
                # committed PNG against a freshly-rendered 4-tuple RGBA in-memory image (or vice
                # versa) is a spurious mismatch, not a real content drift.
                mode = image.mode
                committed = list(Image.open(path).convert(mode).get_flattened_data())
                fresh = list(image.get_flattened_data())
                if committed != fresh:
                    drift.append(f"{name}: committed PNG differs from the grid in this script")
            continue

        os.makedirs(args.out, exist_ok=True)
        if name in BACKDROP:
            image.convert("RGB").save(path)
        else:
            image.save(path)
        print(f"wrote {path} ({image.size[0]}x{image.size[1]})")

        if name in MONSTERS and not args.no_normals:
            normal_path = os.path.join(args.out, f"{name}_n.png")
            make_normal_map(path, normal_path)

    if hole_failures:
        for line_ in hole_failures:
            print(f"gen_emberfall_venue.py: FAIL {line_}", file=sys.stderr)
        return 1

    if drift:
        for line_ in drift:
            print(f"gen_emberfall_venue.py: drift: {line_}", file=sys.stderr)
        return 1

    if args.check:
        print(f"no drift — {len(all_assets)} assets match their committed PNGs")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
