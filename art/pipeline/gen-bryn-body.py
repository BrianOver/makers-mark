"""U36 (§11, R27): Bryn the mentor gets her own body — `town2d-townsfolk-bryn`.

WHY THIS EXISTS
---------------
`AssetResolutionCensusTests.InteriorRoomSpriteIds_ResolveToCommittedArt` walks
`InteriorLayout2D.Rooms` and asserts every station's sprite id resolves to committed art. U36 makes
Bryn the `mentor` station in the `forge` room with her own dedicated id, so that id needs real
pixels or the census fails — and it must, because the alternative on screen is a loud magenta
placeholder box in the room every player enters first. `KnownPendingIds` in that test file is
deliberately EMPTY and pinned empty by its own tripwire; an allowlist with no expiry is the defect,
not the remedy, so "add it to the pending list" is not a door that exists.

WHY A RECOLOUR AND NOT A RENDER
-------------------------------
Every `town2d-townsfolk-*` body in this repo ships as one of two things: an owner-approved
AI-composite base render, or a DETERMINISTIC PIL RECOLOUR of that base. The 120 committed
`town2d-townsfolk-*-v{2..15}` frames are all the second kind — see any of their
`art/build/<id>.build.json` files, which record "zero diffusion, zero seed" and describe exactly
this recipe: nearest-reference pixel classification per zone (skin / hair / garment) with a
luminance-mapped tone transfer, reusing the base's own crop and alpha untouched.

This script is that same recipe with Bryn's own tones. It is deliberately NOT a new SDXL render:
a fresh render would have to match the committed cast's silhouette, crop, palette weight and
downsample pipeline by luck, and at 20x32 the thing that actually distinguishes one townsperson
from another is the palette, not the geometry. A recolour matches the cast BY CONSTRUCTION.

`tools/art/gen_town_sprites.py` is NOT the source of any town-cast body. Its ASCII grids are a dead
path for these ids and it now refuses them outright with a hard `die()` guard (P2-SCREEN-34, #872) —
added after Bryn's id reached that script by accident and cost three authoring passes on an
unusable blob before anyone found the cause.

WHAT MAKES IT SAFE
------------------
`TownSpriteArtTests` pins "every gait frame is byte-identical to its base frame above the hem row"
for the whole cast. This script cannot break that invariant BY CONSTRUCTION: the output at a pixel
is a pure function of that pixel's own colour, with no neighbourhood term anywhere, and the same
function is applied to all four frames. If two frames agreed at (x, y) before, they agree after.

Alpha is copied through untouched, so the silhouette is byte-identical to `town2d-townsfolk-slight`'s
and Bryn stands exactly as tall as everyone else in the plaza.

WHY SLIGHT AND NOT BROAD
------------------------
`town2d-townsfolk-broad` is the id `Town2D.BuildRivalSmith` hands Corren, the rival smith. Before
U36, Bryn borrowed that same body — the town's teacher and its rival smith were visually one person.
Deriving her from `slight` keeps that apart even in the degenerate case where a future reader
compares silhouettes rather than palettes.

Bryn's id is deliberately absent from `TownsfolkNpc2D.CivilianIds`, so no wandering ambient villager
is ever handed her body.

USAGE
-----
    python art/pipeline/gen-bryn-body.py            # writes the four frames + build receipts
    python art/pipeline/gen-bryn-body.py --check    # verifies committed output matches, writes nothing

Run from the repo root. `--check` is what CI or a reviewer runs: it regenerates in memory and
compares byte-for-byte against what is committed, so a hand-edited PNG or a drifted source frame is
caught rather than assumed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys

from PIL import Image

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
ART_DIR = REPO_ROOT / "godot" / "assets" / "art"
BUILD_DIR = REPO_ROOT / "art" / "build"

SOURCE_ID = "town2d-townsfolk-slight"
TARGET_ID = "town2d-townsfolk-bryn"

# The four frames every town-cast body ships as: the contact pose plus a 4-frame gait.
FRAME_SUFFIXES = ("", "_step", "_walk2", "_walk4")

# Row bands the anchor colours are sampled from, read off the base frame's own luminance map (see
# this file's git history for the dump). Rows 0-3 are hair, 4-8 are the face, 10-20 are the garment.
# Sampling ANCHORS from the base rather than hard-coding them means this script keeps working if the
# base render is ever re-cut: the zones move with it.
HAIR_ROWS = range(0, 4)
SKIN_ROWS = range(4, 9)
GARMENT_ROWS = range(10, 21)

# Bryn's own tones. A weathered journeyman smith: hair tied back and darker than the base's, skin
# ruddier and more weathered, and a soot-dusted leather apron over a forge-scorched tunic in place of
# the base's cloth. Chosen to sit inside the cast's existing range rather than outside it -- she must
# read as another person in this town, not as a character from a different game.
BRYN_HAIR = (86, 48, 34)
BRYN_SKIN = (206, 156, 124)
BRYN_GARMENT = (94, 74, 61)


def _band_anchor(image: Image.Image, rows: range) -> tuple[float, float, float]:
    """The mean opaque colour of a row band — this zone's reference colour."""
    pixels = image.load()
    width = image.size[0]
    total = [0.0, 0.0, 0.0]
    count = 0
    for y in rows:
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a < 128:
                continue
            total[0] += r
            total[1] += g
            total[2] += b
            count += 1
    if count == 0:
        raise SystemExit(f"band {rows} is fully transparent in the source frame — the crop moved")
    return (total[0] / count, total[1] / count, total[2] / count)


def _luminance(rgb: tuple[float, float, float]) -> float:
    """Rec. 601 luma. Any consistent weighting works; this one is named so the choice is not a
    mystery to the next reader."""
    return 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]


def _nearest_zone(
    rgb: tuple[int, int, int], anchors: dict[str, tuple[float, float, float]]
) -> str:
    return min(
        anchors,
        key=lambda zone: sum((rgb[i] - anchors[zone][i]) ** 2 for i in range(3)),
    )


def _transfer(
    rgb: tuple[int, int, int],
    anchor: tuple[float, float, float],
    target: tuple[int, int, int],
) -> tuple[int, int, int]:
    """Luminance-mapped tone transfer: keep this pixel's brightness RELATIVE to its zone's anchor,
    and take its hue from the target tone. Shading, folds and highlights therefore survive the
    recolour — a flat fill would erase them and produce exactly the blob this unit already produced
    once through a different route."""
    anchor_luma = _luminance(anchor)
    if anchor_luma <= 0.0:
        ratio = 1.0
    else:
        ratio = _luminance(rgb) / anchor_luma
    # Clamped so a specular highlight cannot multiply the target into a washed-out white, and a
    # deep shadow cannot collapse it to black. The bounds are measured, not guessed: at the
    # library default of (0.35, 1.75) the face blew out to a featureless near-white blob at draw
    # size -- four clamp pairs were rendered at 8x and compared before this one was kept.
    ratio = max(0.40, min(1.10, ratio))
    return tuple(max(0, min(255, round(target[i] * ratio))) for i in range(3))


def recolour(source: Image.Image) -> Image.Image:
    source = source.convert("RGBA")
    anchors = {
        "hair": _band_anchor(source, HAIR_ROWS),
        "skin": _band_anchor(source, SKIN_ROWS),
        "garment": _band_anchor(source, GARMENT_ROWS),
    }
    targets = {"hair": BRYN_HAIR, "skin": BRYN_SKIN, "garment": BRYN_GARMENT}

    out = Image.new("RGBA", source.size)
    src = source.load()
    dst = out.load()
    width, height = source.size
    for y in range(height):
        for x in range(width):
            r, g, b, a = src[x, y]
            if a == 0:
                dst[x, y] = (0, 0, 0, 0)
                continue
            zone = _nearest_zone((r, g, b), anchors)
            nr, ng, nb = _transfer((r, g, b), anchors[zone], targets[zone])
            # Alpha copied through untouched: the silhouette stays byte-identical to the base's.
            dst[x, y] = (nr, ng, nb, a)
    return out


def _png_bytes(image: Image.Image) -> bytes:
    import io

    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def _receipt(frame_id: str, diffuse_sha: str, source_frame: str) -> dict:
    return {
        "Seed": None,
        "Model": None,
        "Lora": None,
        "Steps": None,
        "CfgMilli": None,
        "SamplerResolved": None,
        "SchedulerResolved": None,
        "PaletteSha256": None,
        "DiffuseSha256": diffuse_sha,
        "NormalSha256": None,
        "Uid": None,
        "HandFinished": False,
        "Status": "procedural",
        "Provenance": {
            "drafts": None,
            "paintoverNote": None,
            "aiDisclosure": (
                "Deterministic PIL recolour of the owner-approved "
                f"{source_frame} AI-composite base render -- ZERO diffusion, ZERO seed, the same "
                "discipline every committed town2d-townsfolk-*-v{2..15} frame already ships under "
                "(see any of their own build.json files). Recipe, implemented in "
                "art/pipeline/gen-bryn-body.py and reproducible with its --check flag: three zone "
                "anchor colours are sampled from the BASE frame's own row bands (hair rows 0-3, "
                "face rows 4-8, garment rows 10-20) rather than hard-coded, each pixel is assigned "
                "to its nearest anchor in RGB, and its colour is replaced by Bryn's tone for that "
                "zone scaled by the pixel's own luminance relative to that anchor -- so shading, "
                "folds and highlights survive. Alpha is copied through untouched, so this frame's "
                "silhouette is byte-identical to the base's. The transform is a pure per-pixel "
                "function with no neighbourhood term, applied identically to all four frames, so "
                "TownSpriteArtTests' 'every gait frame is byte-identical to its base above the hem' "
                "invariant holds by construction. Derived from 'slight' rather than 'broad' on "
                "purpose: 'broad' is the body Town2D.BuildRivalSmith hands Corren the rival smith, "
                "and before U36 the town's teacher and its rival smith were visually the same "
                "person. NOT generated by tools/art/gen_town_sprites.py, whose ASCII grids are a "
                "dead path for every town-cast id and now refuse them outright (P2-SCREEN-34)."
            ),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="regenerate in memory and compare against the committed PNGs; write nothing",
    )
    args = parser.parse_args()

    drift = []
    for suffix in FRAME_SUFFIXES:
        source_path = ART_DIR / f"{SOURCE_ID}{suffix}.png"
        target_path = ART_DIR / f"{TARGET_ID}{suffix}.png"
        if not source_path.exists():
            print(f"missing source frame: {source_path}", file=sys.stderr)
            return 1

        produced = _png_bytes(recolour(Image.open(source_path)))
        digest = hashlib.sha256(produced).hexdigest()

        if args.check:
            if not target_path.exists():
                drift.append(f"{target_path.name} is not committed")
            elif target_path.read_bytes() != produced:
                drift.append(f"{target_path.name} differs from what this script produces")
            continue

        target_path.write_bytes(produced)
        receipt_path = BUILD_DIR / f"{TARGET_ID}{suffix}.build.json"
        receipt_path.write_text(
            json.dumps(_receipt(f"{TARGET_ID}{suffix}", digest, f"{SOURCE_ID}{suffix}"), indent=2)
            + "\n",
            encoding="utf-8",
        )
        print(f"wrote {target_path.relative_to(REPO_ROOT)}  sha256={digest[:12]}")

    if args.check:
        if drift:
            for line in drift:
                print(f"DRIFT: {line}", file=sys.stderr)
            print(
                "Committed art no longer matches art/pipeline/gen-bryn-body.py. Either the base "
                "frames changed or a PNG was hand-edited; re-run this script without --check and "
                "review the diff.",
                file=sys.stderr,
            )
            return 1
        print(f"{TARGET_ID}: all {len(FRAME_SUFFIXES)} committed frames match the generator")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
