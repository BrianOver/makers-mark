#!/usr/bin/env python
"""BiRefNet cutout: gradient-/neutral-bg game sprite -> RGBA transparent PNG.

Stage 2 of the Maker's Mark art pipeline (generate -> CUTOUT -> normalmap).
Runs an SDXL-generated sprite (plain dark neutral background, per
docs/design/asset-style-spec.md) through the BiRefNet segmentation model to
produce a content-only RGBA cutout ready for normal-map generation and Godot
import.

Proven on the tavern pilot (PR #32). Local tooling only -- never wired into CI;
model weights are never committed (pulled to the HF cache on first run).

Usage:
    python cutout.py IN.png OUT.png [--trim] [--revision SHA] [--device cuda]

Examples:
    python cutout.py candidates/forge-07.png candidates/forge-07.cut.png --trim
    python cutout.py in.png out.png --device cpu        # no GPU (slow)
"""
import argparse
import sys
from pathlib import Path

# --- Supply-chain pin (Fornida security rule) -------------------------------
# trust_remote_code=True executes model code fetched from the Hugging Face repo,
# so we pin an exact, reviewed commit. Bump deliberately after re-reviewing the
# repo at the new revision -- never leave this unpinned.
#   repo:     https://huggingface.co/ZhengPeng7/BiRefNet
#   revision: main @ 2026-02-04 (verified via HF API)
MODEL_ID = "ZhengPeng7/BiRefNet"
MODEL_REVISION = "e2bf8e4460fc8fa32bba5ea4d94b3233d367b0e4"


def _die(msg: str, code: int = 1) -> None:
    print(f"cutout.py: error: {msg}", file=sys.stderr)
    raise SystemExit(code)


def parse_args(argv=None) -> argparse.Namespace:
    p = argparse.ArgumentParser(
        prog="cutout.py",
        description="BiRefNet background removal: sprite PNG -> RGBA cutout.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Pipeline order: ComfyUI generate -> cutout.py -> normalmap.py.\n"
            "See art/pipeline/README.md for the full runbook."
        ),
    )
    p.add_argument("src", help="input sprite PNG (neutral/gradient background)")
    p.add_argument("dst", help="output RGBA PNG (transparent background)")
    p.add_argument(
        "--trim",
        action="store_true",
        help="crop to the alpha bounding box so the sprite is content-tight "
        "(V4b runtime scaling assumes trimmed textures)",
    )
    p.add_argument(
        "--revision",
        default=MODEL_REVISION,
        help="pinned BiRefNet commit SHA (default: the reviewed pin; "
        "override only after re-reviewing the repo at that revision)",
    )
    p.add_argument(
        "--device",
        default="cuda",
        choices=("cuda", "cpu"),
        help="inference device (default: cuda; half-precision on cuda only)",
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

    # Imports deferred so --help works without the heavy GPU stack installed.
    try:
        import torch
        from PIL import Image
        from torchvision import transforms
        from transformers import AutoModelForImageSegmentation
    except ImportError as e:  # pragma: no cover - env-dependent
        _die(
            f"missing dependency ({e.name}). Activate the pipeline venv and "
            "install deps -- see art/pipeline/requirements.txt and README.md."
        )

    if args.device == "cuda" and not torch.cuda.is_available():
        _die(
            "CUDA requested but torch.cuda.is_available() is False. Install the "
            "cu130 torch/torchvision wheels for this GPU (see requirements.txt), "
            "or re-run with --device cpu (slow)."
        )
    use_half = args.device == "cuda"

    try:
        model = AutoModelForImageSegmentation.from_pretrained(
            MODEL_ID, revision=args.revision, trust_remote_code=True
        )
    except Exception as e:  # pragma: no cover - network/cache-dependent
        _die(
            f"failed to load {MODEL_ID}@{args.revision}: {e}\n"
            "First run downloads ~1 GB to the HF cache -- check network access "
            "and that the pinned revision still exists. BiRefNet's remote code "
            "may also require 'timm'/'einops' (see requirements.txt)."
        )

    model.to(args.device).eval()
    if use_half:
        model.half()

    img = Image.open(src).convert("RGB")
    tf = transforms.Compose(
        [
            transforms.Resize((1024, 1024)),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
        ]
    )
    x = tf(img).unsqueeze(0).to(args.device)
    if use_half:
        x = x.half()
    with torch.no_grad():
        preds = model(x)[-1].sigmoid().cpu()
    mask = transforms.ToPILImage()(preds[0].squeeze().float()).resize(img.size)

    out = img.convert("RGBA")
    out.putalpha(mask)

    if args.trim:
        # Trim to the ALPHA bounding box (the visible content). Note: the RGB
        # channels still hold the original background under alpha=0, so a plain
        # out.getbbox() over RGBA would see the whole frame and never crop --
        # the alpha channel is what defines "content-tight".
        bbox = out.getchannel("A").getbbox()
        if bbox is None:
            print("cutout.py: warning: empty alpha mask, skipping --trim",
                  file=sys.stderr)
        else:
            out = out.crop(bbox)

    out.save(dst)
    print("saved", dst, out.size)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
