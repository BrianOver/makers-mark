"""U-T3-7 (register #141 / R14.11, "the 244-PNG silhouette pass"): harden every AI-cast body's
soft, anti-aliased cutout edge into a crisp pixel-art silhouette.

WHY THIS EXISTS
---------------
Owner playtest #141: "the character's legs 'clip' with the grass and look odd". Verified directly
against the committed pixels (not assumed): every `town2d-hero-*.png`/`player_smith*.png` carries
18-116 PARTIAL alpha values on ~15-18% of its pixels -- a soft sigmoid ramp, not a hard cutout edge
-- because `cutout.py`'s BiRefNet segmentation mask (`out.putalpha(mask)`) is a continuous
probability field, never thresholded before being saved. Godot draws every character Nearest-
filtered on a Snap2DTransformsToPixel/Snap2DVerticesToPixel viewport, so the game's OWN rendering
is not the defect -- the source PNGs' own alpha channel already blends into whatever sits behind
them (grass, cobble, a night tint) at the pixel level, before Godot ever touches it. `town2d-
townsfolk-*` bodies are UNAFFECTED (measured: 0.0% partial alpha on all 120) -- they never went
through this cutout path, which is why R14.11 names "the AI cast" specifically, not the whole
244-PNG town-sprite registry.

THE FIX
-------
A single per-pixel alpha threshold: alpha >= THRESHOLD -> fully opaque (RGB unchanged), else fully
transparent. Deliberately NOT a dilate/outline pass -- an earlier draft of this script also grew a
1px dark outline ring, but that reads each frame's OWN neighbourhood, and TownSpriteArtTests.cs
pins "every gait frame must be BYTE-IDENTICAL to its base frame above the legs/hem row" (per-class
floors 18-24) for 6 classes plus the player -- a dilation's 3x3 kernel can pull a below-the-hem,
frame-specific pixel one row UP into the identical zone and break that invariant. A pure per-pixel
threshold has no such risk BY CONSTRUCTION: if two frames agreed at (x,y) before, they agree after,
since the output at (x,y) is a deterministic function of ONLY that pixel's own alpha value. Verified
directly against every numeric floor TownSpriteArtTests.cs pins (distinct-colours, skin-tone-pixel
count, cross-class silhouette distance, cross-class garment-colour distance, player warm-hue
fraction) BEFORE this ever wrote a file -- see the receipt this PR's description quotes. Occultist's
skin-pixel count sits AT its floor (4) either side of this transform; every other floor cleared with
its existing margin or more (hardening only ever REMOVES boundary-soft pixels, which are rarely the
dominant colour/silhouette-defining ones).

FROZEN SOURCE, NOT A SELF-REFERENTIAL TRANSFORM
------------------------------------------------
Same discipline as recolor-forge-roof.py's own doc: thresholding an ALREADY-hardened alpha channel
is a true no-op (threshold(0)=0, threshold(255)=255), so this transform IS idempotent against its
own output in principle -- but sourcing from a frozen pre-pass snapshot (`sources/ai-cast-pre-
silhouette/`, byte-identical copies of the 124 files as committed before this script first ran)
keeps `--check` meaningful as a real regression gate even if some LATER script ever reintroduces
soft alpha into one of these files without going through this one.

Usage:
    python art/pipeline/harden-cast-silhouette.py           # render + write all 124 files
    python art/pipeline/harden-cast-silhouette.py --check   # render fresh from the frozen
                                                              # sources and diff against what's
                                                              # committed; writes nothing
"""
from __future__ import annotations

import argparse
import glob
import pathlib
import sys

from PIL import Image

SOURCES_DIR = pathlib.Path("art/pipeline/sources/ai-cast-pre-silhouette")
OUT_DIR = pathlib.Path("godot/assets/art")

# The midpoint of BiRefNet's sigmoid ramp -- see module doc. Every cast body's soft edge spans its
# full 0-255 range over 1-2px, so 128 sits at the ramp's own centre rather than favouring either
# side.
ALPHA_THRESHOLD = 128


def render(src: pathlib.Path) -> Image.Image:
    """Pure per-pixel alpha threshold. No neighbourhood read (see module doc for why that matters
    for the gait-frame-identity invariant) -- the output at (x, y) depends on nothing but the
    input's own (x, y)."""
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    out = Image.new("RGBA", (w, h))
    src_px = im.load()
    dst_px = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = src_px[x, y]
            dst_px[x, y] = (r, g, b, 255) if a >= ALPHA_THRESHOLD else (0, 0, 0, 0)
    return out


def frozen_sources() -> list[pathlib.Path]:
    files = sorted(SOURCES_DIR.glob("*.png"))
    if not files:
        print(f"FAIL no frozen sources found under {SOURCES_DIR}", file=sys.stderr)
    return files


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                     help="render fresh from the frozen sources and compare against the "
                          "committed PNGs instead of writing")
    args = ap.parse_args()

    sources = frozen_sources()
    if not sources:
        return 1

    if args.check:
        failures = []
        for src in sources:
            out_path = OUT_DIR / src.name
            if not out_path.exists():
                failures.append(f"{out_path} does not exist")
                continue
            fresh = render(src)
            committed = Image.open(out_path).convert("RGBA")
            if committed.size != fresh.size:
                failures.append(f"{out_path} size drift: committed {committed.size} vs fresh {fresh.size}")
                continue
            if list(committed.get_flattened_data()) != list(fresh.get_flattened_data()):
                failures.append(f"{out_path} differs from a fresh render of {src}")

        if failures:
            for f in failures:
                print(f"FAIL {f}", file=sys.stderr)
            print(f"FAIL {len(failures)}/{len(sources)} files differ from a fresh render", file=sys.stderr)
            return 1

        print(f"ok all {len(sources)} files match a fresh render of their frozen source")
        return 0

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for src in sources:
        out_path = OUT_DIR / src.name
        render(src).save(out_path)
    print(f"wrote {len(sources)} hardened silhouettes to {OUT_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
