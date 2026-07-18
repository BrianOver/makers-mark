#!/usr/bin/env python
"""Height-from-luminance -> Sobel -> tangent-space normal map (_n.png).

Stage 3 of the Maker's Mark art pipeline (generate -> cutout -> NORMALMAP).
Produces a Godot-ready tangent-space normal map from an RGBA cutout: blurred
luminance is treated as a height field, np.gradient gives the surface slope, and
the result is packed into an RGBA PNG with the source alpha preserved.

Standard low-relief approximation for painterly sprites; a Laigter quality pass
can replace this where the Sobel map reads embossed/tinny (see README.md).
Validated on the tavern pilot (PR #32) -- the math is kept byte-identical.

Godot normal-map convention (IMPORTANT):
    Godot uses +Y UP (green channel points up), the OpenGL convention. Image
    rows run +Y DOWN, so the Y gradient is emitted as +gy here (an effective
    green-channel flip). Do NOT change the sign -- flipping it inverts every
    bump so lit relief caves in instead of standing out. When importing the
    _n.png in Godot, leave "Normal Map -> Flip Y" OFF (it is already correct).

Local tooling only -- never wired into CI.

Usage:
    python normalmap.py IN.png OUT_n.png [strength]

    strength : bump gain, default 2.5. Higher = deeper relief; too high reads
               tinny/embossed. Tune per asset; the tavern shipped at 2.5.

Example:
    python normalmap.py forge.png forge_n.png 2.5
"""
import argparse
import sys
from pathlib import Path


def _die(msg: str, code: int = 1) -> None:
    print(f"normalmap.py: error: {msg}", file=sys.stderr)
    raise SystemExit(code)


def parse_args(argv=None) -> argparse.Namespace:
    p = argparse.ArgumentParser(
        prog="normalmap.py",
        description="Height-from-luminance tangent-space normal map for Godot.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Pipeline order: cutout.py -> normalmap.py -> Godot import.\n"
            "Godot: import _n.png with 'Flip Y' OFF. See art/pipeline/README.md."
        ),
    )
    p.add_argument("src", help="input RGBA cutout PNG (diffuse)")
    p.add_argument("dst", help="output tangent-space normal map (_n.png)")
    p.add_argument(
        "strength",
        nargs="?",
        default=2.5,
        type=float,
        help="bump gain (default 2.5; higher = deeper relief, tinny if too high)",
    )
    return p.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)

    src = Path(args.src)
    if not src.is_file():
        _die(f"input not found: {src}")
    dst = Path(args.dst)
    if dst.parent and not dst.parent.exists():
        _die(f"output directory does not exist: {dst.parent}")

    strength = args.strength

    # Imports deferred so --help works without the imaging stack installed.
    try:
        import numpy as np
        from PIL import Image, ImageFilter
    except ImportError as e:  # pragma: no cover - env-dependent
        _die(
            f"missing dependency ({e.name}). Activate the pipeline venv and "
            "install deps -- see art/pipeline/requirements.txt and README.md."
        )

    # --- proven math (byte-identical to the PR #32 tavern pipeline) ----------
    im = Image.open(src).convert("RGBA")
    rgb = np.asarray(im).astype(np.float32)
    alpha = rgb[..., 3] / 255.0

    # height = blurred luminance (blur kills paint noise, keeps forms)
    lum = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]) / 255.0
    h = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(
        ImageFilter.GaussianBlur(3))).astype(np.float32) / 255.0

    gy, gx = np.gradient(h)
    nx = -gx * strength
    ny = gy * strength  # +Y down in image space; Godot uses +Y up => this is the flip
    nz = np.ones_like(h)
    norm = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / norm, ny / norm, nz / norm], axis=-1)

    out = ((n * 0.5 + 0.5) * 255).astype(np.uint8)
    rgba = np.dstack([out, (alpha * 255).astype(np.uint8)])
    # ------------------------------------------------------------------------

    Image.fromarray(rgba, "RGBA").save(dst)
    print("saved", dst)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
