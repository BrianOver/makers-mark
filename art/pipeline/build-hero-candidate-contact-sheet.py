#!/usr/bin/env python
"""U13 (world-and-interiors plan): assembles the owner-facing contact sheet from the raw
receipt.ps1 captures in runs/receipts/candidates/heroes-r3/raw/ -- one image, judged in seconds,
instead of six separate PNGs in a viewer (the plan's own deliverable shape).

Row A (scale bump): three FULL, un-cropped town frames -- 0.50 (shipped), 0.65, 0.75 -- so the
owner sees the town-proportion cost, plus a matched crop of the player at the forge door (same
pixel box, same world position, every capture) so the on-screen growth is unmistakable even
though it is subtle in the full-frame thumbnails alone.

Row B (motion): one full frame for in-world context, then three NATIVE-RESOLUTION crops around
the candidate figure blown up 6x with nearest-neighbour (the same filter Godot itself uses) --
labelled as a legibility crop, never presented as the true on-screen size, so nobody mistakes the
crop for the real proportion. The full frame is the honest "this is what 13x22px actually looks
like in the world" reference.

Regenerate any time the raw captures change:
    python art/pipeline/build-hero-candidate-contact-sheet.py
"""
import os
import textwrap

from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW_DIR = os.path.join(REPO_ROOT, "runs", "receipts", "candidates", "heroes-r3", "raw")
OUT_PATH = os.path.join(REPO_ROOT, "runs", "receipts", "candidates", "heroes-r3", "contact-sheet.png")

BG = (18, 18, 22, 255)
FG = (235, 235, 240, 255)
DIM = (170, 170, 180, 255)
ACCENT = (240, 190, 90, 255)
PANEL_BORDER = (70, 70, 80, 255)


def _font(size: int) -> ImageFont.ImageFont:
    for candidate in (
        "C:/Windows/Fonts/consola.ttf",
        "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/arial.ttf",
    ):
        if os.path.exists(candidate):
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


F_TITLE = _font(28)
F_HEAD = _font(20)
F_BODY = _font(15)
F_SMALL = _font(13)


def _wrapped(draw: ImageDraw.ImageDraw, xy, text, font, max_width, fill, line_gap=4):
    """Word-wraps `text` to fit max_width (px) at `font`, drawing one line at a time."""
    words = text.split()
    lines, cur = [], ""
    for w in words:
        trial = (cur + " " + w).strip()
        if draw.textlength(trial, font=font) <= max_width or not cur:
            cur = trial
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    x, y = xy
    line_h = font.size + line_gap
    for line in lines:
        draw.text((x, y), line, font=font, fill=fill)
        y += line_h
    return y


def _panel(img: Image.Image, caption: str, sub: str, width: int, caption_h: int = 66) -> Image.Image:
    """Scales `img` to `width` (keeping aspect), then stacks a wrapped caption strip below it."""
    scale = width / img.width
    resized = img.resize((width, int(img.height * scale)), Image.NEAREST)
    pad_top = 4
    canvas = Image.new("RGBA", (width + 8, resized.height + pad_top + caption_h), BG)
    d = ImageDraw.Draw(canvas)
    d.rectangle([0, 0, canvas.width - 1, canvas.height - 1], outline=PANEL_BORDER, width=2)
    canvas.paste(resized, (4, pad_top))
    y = resized.height + pad_top + 6
    d.text((10, y), caption, font=F_HEAD, fill=ACCENT)
    y += F_HEAD.size + 6
    _wrapped(d, (10, y), sub, F_SMALL, width - 12, DIM)
    return canvas


def _crop_scaled(path: str, box, zoom: int) -> Image.Image:
    im = Image.open(path).convert("RGBA").crop(box)
    return im.resize((im.width * zoom, im.height * zoom), Image.NEAREST)


def _row(panels, gap: int) -> Image.Image:
    h = max(p.height for p in panels)
    w = sum(p.width for p in panels) + gap * (len(panels) - 1)
    row = Image.new("RGBA", (w, h), BG)
    x = 0
    for p in panels:
        row.paste(p, (x, (h - p.height) // 2))
        x += p.width + gap
    return row


def main() -> None:
    def raw(name: str) -> str:
        return os.path.join(RAW_DIR, name)

    scale_files = [
        ("u13-scale050-town-08d8639.png", "0.50 -- SHIPPED (current)", "CharacterSpriteScale unchanged. Reference column."),
        ("u13-scale065-town-08d8639.png", "0.65 candidate", "+30% on screen, same PNGs. Sampling is no longer a clean 2:1."),
        ("u13-scale075-town-08d8639.png", "0.75 candidate", "+50% on screen. Town proportion (buildings vs. cast) shifts."),
    ]
    full_panels = [_panel(Image.open(raw(f)).convert("RGBA"), cap, sub, 480) for f, cap, sub in scale_files]
    row_a_full = _row(full_panels, 24)

    # Matched crop: the player at the forge door, SAME pixel box in every capture (same world
    # position every time -- only the rendered sprite size changes with the scale constant).
    player_box = (388, 260, 470, 400)
    player_zoom = 4
    crop_labels = ["0.50 (shipped)", "0.65", "0.75"]
    crop_panels = [
        _panel(_crop_scaled(raw(f), player_box, player_zoom), label, "Same crop, same world position.", 240, caption_h=64)
        for (f, _, _), label in zip(scale_files, crop_labels)
    ]
    row_a_crop = _row(crop_panels, 24)

    # Row B: one full frame for honest context, then three native-crop-zoomed poses.
    full_ctx = _panel(
        Image.open(raw("u13-motion-HeroCandidateClosed-08d8639.png")).convert("RGBA"),
        "In-world placement (full frame)",
        "Candidate figure (neutral, untinted) stands beside the shipped cast at the same scale.",
        480,
    )

    pose_box = (528, 338, 562, 398)  # tight box around the mounted candidate figure, native px
    pose_zoom = 6
    pose_files = [
        ("u13-motion-HeroCandidateClosed-08d8639.png", "closed", "Crop x6, nearest -- NOT the true on-screen size (see full frame, left)."),
        ("u13-motion-HeroCandidateMid-08d8639.png", "mid -- NEW pose", "The one new leg pose authored for this candidate."),
        ("u13-motion-HeroCandidateOpen-08d8639.png", "open", "Identical to today's shipped _step frame."),
    ]
    pose_panels = [_panel(_crop_scaled(raw(f), pose_box, pose_zoom), cap, sub, 280) for f, cap, sub in pose_files]
    row_b = _row([full_ctx] + pose_panels, 24)

    width = max(row_a_full.width, row_a_crop.width, row_b.width) + 48
    header_h = 150
    section_h = 34
    total_h = (header_h + section_h + row_a_full.height + 20 + row_a_crop.height + 34
               + section_h + row_b.height + 40)

    sheet = Image.new("RGBA", (width, total_h), BG)
    d = ImageDraw.Draw(sheet)
    d.text((24, 20), "U13 -- Hero Visuals, Third Round: Candidates", font=F_TITLE, fill=FG)
    d.text((24, 58),
           "docs/plans/2026-08-02-004-feat-world-and-interiors-plan.md, U13. Nothing here ships by "
           "default -- his pick lands as a follow-up PR.", font=F_BODY, fill=DIM)
    d.text((24, 82),
           "Rendered in-engine via tools/receipt.ps1 (real Godot 4.6.3 build, Nearest filter, no "
           "mipmaps) -- not mockups, not an image viewer.", font=F_BODY, fill=DIM)
    d.text((24, 104),
           "Dead ends already closed: same-size repaint (#329, 0.07% diff, invisible); a canvas "
           "bigger than the player (CastProportionTests).", font=F_BODY, fill=DIM)

    y = header_h
    d.text((24, y), "DIAL A -- SCREEN SCALE  (free: one constant, zero new art)", font=F_HEAD, fill=ACCENT)
    y += section_h
    sheet.paste(row_a_full, (24, y), row_a_full)
    y += row_a_full.height + 20
    sheet.paste(row_a_crop, (24, y), row_a_crop)
    y += row_a_crop.height + 34

    d.text((24, y), "DIAL B -- MOTION  (striker only: +1 new leg pose, authored this unit)", font=F_HEAD, fill=ACCENT)
    y += section_h
    sheet.paste(row_b, (24, y), row_b)

    sheet.convert("RGB").save(OUT_PATH)
    print(f"wrote {OUT_PATH} ({sheet.width}x{sheet.height})")


if __name__ == "__main__":
    main()
