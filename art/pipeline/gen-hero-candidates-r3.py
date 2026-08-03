#!/usr/bin/env python
"""U13 (world-and-interiors plan) motion candidate: a 3-pose leg-cycle for the striker body,
used ONLY to render an owner-facing receipt comparing today's 2-frame walk (closed/open) against
a smoother 4-beat cycle (closed -> mid -> open -> mid) that adds one new leg pose in between.

WHY THIS IS A SEPARATE SCRIPT, NOT AN EDIT TO gen_town_sprites.py
------------------------------------------------------------------
This is a CANDIDATE, not shipped art: per the plan's non-negotiable, "candidate art that is not
wired into the game must not create census rows; keep it out of the resolution path entirely."
gen_town_sprites.py writes into godot/assets/art/, which IconRegistry.Art / AssetCatalog resolve
and AssetResolutionCensusTests enumerates via ClassRegistry.RecruitPool. This script writes into
godot/assets/candidates/heroes-r3/ instead -- a directory IconRegistry never looks at, so these
PNGs cannot resolve through the production path no matter what future code does, by construction
rather than by discipline. godot/tools/shot_harness.gd loads them directly by res:// path for the
one-off receipt states this unit adds (HeroCandidateClosed/Mid/Open), bypassing IconRegistry
entirely -- see that file's own U13 comment block.

WHERE THE PIXELS COME FROM
---------------------------
HEAD_STRIKER, TORSO_STRIKER, LEGS_BASE, LEGS_STEP, and PALETTE are imported straight from
tools/art/gen_town_sprites.py (the committed, shipped module) rather than re-typed here -- so the
"closed" and "open" candidate frames are BYTE-IDENTICAL to the shipped town2d-hero-striker[/_step]
PNGs above the waist and at the two existing leg poses. Only LEGS_MID below is new: it is a
hand-authored halfway point between LEGS_BASE's padding/gap at each of the 13 leg rows and
LEGS_STEP's, so the three candidate frames read as one continuous stride rather than an
arbitrary third pose. The walk cycle plays closed(bob0) -> mid -> open -> mid -> closed, the
classic 4-beat contact/passing/contact/passing timing (passing repeats, same as most 2D games'
walk cycles) -- 3 unique poses, 1 new one authored.

Usage:
    python gen-hero-candidates-r3.py [--out DIR] [--check]
"""
import argparse
import importlib.util
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SOURCE_MODULE_PATH = REPO_ROOT / "tools" / "art" / "gen_town_sprites.py"


def _load_source_module():
    """Imports the shipped sprite-authoring module by path (it is a plain script, not a package
    on sys.path) so this file reads its PALETTE/rows/render() rather than re-transcribing them --
    transcription is exactly how a torso/head could quietly drift from the shipped body."""
    spec = importlib.util.spec_from_file_location("gen_town_sprites", SOURCE_MODULE_PATH)
    if spec is None or spec.loader is None:
        raise SystemExit(f"gen-hero-candidates-r3.py: cannot load {SOURCE_MODULE_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


_src = _load_source_module()
EMPTY_MARGIN = _src.EMPTY_MARGIN
HEAD_STRIKER = _src.HEAD_STRIKER
TORSO_STRIKER = _src.TORSO_STRIKER
LEGS_BASE = _src.LEGS_BASE
LEGS_STEP = _src.LEGS_STEP
PALETTE = _src.PALETTE
WIDTH = _src.WIDTH
HEIGHT = _src.HEIGHT
render = _src.render
row = _src.row

# ── LEGS_MID — the one new pose ────────────────────────────────────────────────────────────────
# Each row below sits halfway between LEGS_BASE's padding/gap and LEGS_STEP's at that same row
# (gap_mid = round(gap_step / 2), pad totals adjusted to keep every row exactly WIDTH wide). Rows
# 34-38 and 39-41 are each a repeated content block in BOTH source frames (BASE and STEP hold the
# shin/ankle content constant across their own repeated rows), so MID repeats them the same way.
LEGS_MID = [
    row(_src.centered(_src.mirror("ollmdo"))),  # 31 shared top -- identical in BASE/STEP/MID
    row("......." + "ollmdo" + "." + "odmllo" + "......"),  # 32 gap1 (STEP: gap2)
    row("......" + "ollmdo" + ".." + "odmllo" + "......"),  # 33 gap2 (STEP: gap4)
    row("......" + "odmido" + ".." + "odimdo" + "......"),  # 34 gap2 (STEP: gap4)
    row("......" + "odmido" + ".." + "odimdo" + "......"),  # 35
    row("......" + "odmido" + "..." + "odimdo" + "....."),  # 36 gap3 (STEP: gap6)
    row("......" + "odmido" + "..." + "odimdo" + "....."),  # 37
    row("......" + "odmido" + "..." + "odimdo" + "....."),  # 38
    row("......" + "odmdo" + "...." + "odmdo" + "......"),  # 39 gap4 (STEP: gap8)
    row("......" + "odmdo" + "...." + "odmdo" + "......"),  # 40
    row("......" + "odmdo" + "...." + "odmdo" + "......"),  # 41
    row("......" + "ohhdo" + "...." + "odhho" + "......"),  # 42 boots, gap4 (STEP: gap8)
    row("......" + "ooooo" + "...." + "ooooo" + "......"),  # 43 soles, gap4 (STEP: gap8)
]
assert len(LEGS_MID) == 13
for _i, _r in enumerate(LEGS_MID):
    assert len(_r) == WIDTH, f"LEGS_MID row {_i + 31}: {len(_r)} chars, want {WIDTH}: {_r!r}"

STRIKER_CANDIDATE_CLOSED = EMPTY_MARGIN + HEAD_STRIKER + TORSO_STRIKER + LEGS_BASE  # == shipped base
STRIKER_CANDIDATE_MID = EMPTY_MARGIN + HEAD_STRIKER + TORSO_STRIKER + LEGS_MID  # new
STRIKER_CANDIDATE_OPEN = EMPTY_MARGIN + HEAD_STRIKER + TORSO_STRIKER + LEGS_STEP  # == shipped step

FRAMES = {
    "striker-candidate-closed": STRIKER_CANDIDATE_CLOSED,
    "striker-candidate-mid": STRIKER_CANDIDATE_MID,
    "striker-candidate-open": STRIKER_CANDIDATE_OPEN,
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out",
        default=os.path.join("godot", "assets", "candidates", "heroes-r3"),
        help="output directory (default: godot/assets/candidates/heroes-r3, "
             "deliberately OUTSIDE godot/assets/art -- see module docstring)")
    parser.add_argument(
        "--check", action="store_true",
        help="compare against committed PNGs instead of writing; non-zero exit on any difference")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    drift = []
    for name, grid in FRAMES.items():
        image = render(grid, name)
        path = os.path.join(args.out, f"{name}.png")
        if args.check:
            if not os.path.exists(path):
                drift.append(f"{name}: no committed PNG at {path}")
            else:
                from PIL import Image
                if (list(Image.open(path).convert("RGBA").get_flattened_data())
                        != list(image.get_flattened_data())):
                    drift.append(f"{name}: committed PNG differs from the grid in this script")
            continue
        image.save(path)
        print(f"wrote {path} ({WIDTH}x{HEIGHT})")

    if drift:
        for line in drift:
            print(f"gen-hero-candidates-r3.py: drift: {line}", file=sys.stderr)
        return 1
    if args.check:
        print(f"no drift -- {len(FRAMES)} candidate frames match their committed PNGs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
