"""Author the town venue exteriors at R14.8's role-ranked sizes (register #142/#143).

WHY THIS EXISTS
---------------
The owner's words: "heroes are too big compared to the buildings; make the buildings bigger".
R14.8 ruled the shape of the fix -- buildings grow to 3.5-5.5x the character body, RANKED BY ROLE
rather than multiplied uniformly, because a uniform 2x would have left the market and the bounty
hall still undersized, i.e. both complaints still open. R14.10 exempts market.png: the owner named
that art as art he likes and there is no fallback copy on disk.

WHY GENERATED AND NOT HAND-AUTHORED
-----------------------------------
The sibling scripts here (gen-market.py, gen-ground-tiles.py) hand-author their pixels and say why:
"at 20x36 a diffusion render downscales to mush". That finding is about SPRITE scale and it still
holds there. These are 121-187px buildings, three to five times that -- and they are also the
assets whose committed provenance (art/build/*.build.json) already records an SDXL chain.
forge.png, tavern.png, mine-gate.png and noticeboard.png were every one of them generated. This
script re-runs the chain those files already describe rather than inventing a second one.

THE CHAIN, READ FROM art/build/forge.build.json
-----------------------------------------------
SDXL base 1.0 + pixel-art-xl LoRA (0.9/0.9), dpmpp_2m/karras, 30 steps, cfg 7.0, and IPAdapter
(vit-h, style transfer, weight 0.9) referencing market.png so palette and lighting lock to the one
building the owner has explicitly approved. Then the cutout below, then a BOX downsample straight
to the target height -- no quantisation, the same recipe market.png's own committed pixels came
from.

TWO THINGS THAT COST A ROUND EACH, WRITTEN DOWN SO THEY DO NOT COST ANOTHER
--------------------------------------------------------------------------
1. NAMING THE ROOM'S FUNCTION SUMMONS ITS CLUTTER. Asking for a bounty hall "thick with pinned
   parchment notices" returned four cottages, twice, across two seeds. Asking for the ARCHITECTURE
   instead -- a stone hall with a shield device over the door -- landed it on the first try. It is
   the same lesson the painted interiors learned from the other side (#587: "workshop interior"
   implies barrels no negative prompt can remove).

2. THE BACKDROP MUST BE A COLOUR THE SUBJECT NEVER CONTAINS. The first mine-gate batch put a
   purple-stone arch on a purple field. The cutout then had no tolerance that worked: low kept the
   whole backdrop (95% fill -- the "finished" gate was a rectangle of its own background, and only
   a RENDERED screenshot showed it, never a measurement), high ate the building (35% fill at
   tolerance 20). A chroma backdrop cuts identically at tolerance 30 and at 90, which is the actual
   test of a good backdrop.

Usage:
    python art/pipeline/gen-venues-r14.py --venue forge
    python art/pipeline/gen-venues-r14.py --cut IN.png OUT.png --height 168

--venue needs a local ComfyUI on 127.0.0.1:8188 with ref-market.png already uploaded to its input
directory. --cut is pure PIL and needs no GPU.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import urllib.request
from collections import deque

from PIL import Image

HOST = "http://127.0.0.1:8188"
CHARACTER_BODY_PX = 34  # tallest resolved character body; VenueArtContractTests measures the same

# Role-ranked inside R14.8's 3.5-5.5x band. The tavern is the town's social centre and takes the
# top of the band. The mine gate takes the FLOOR, and not for art reasons: it is pinned at the
# grid's north edge, and TownPlacementTests fails the moment its sprite reaches past y=0.
VENUES = {
    "forge": dict(seed=714010, height=168, ratio=5.0, prompt=(
        "pixel art sprite of one single tall blacksmith forge building, four storeys, whole "
        "building visible from roof ridge down to its doorstep, stone ground floor with a wide "
        "arched forge mouth glowing with embers, timber upper floors, steep dark purple slate "
        "roof, tall brick chimney with smoke, hanging anvil sign, plain flat backdrop")),
    "tavern": dict(seed=714020, height=185, ratio=5.5, prompt=(
        "pixel art sprite of one single tall timber-framed tavern building, four storeys, whole "
        "building visible from roof ridge down to its doorstep, stone ground floor, jettied timber "
        "upper floors, steep dark purple slate roof, warm glowing windows, hanging tankard sign "
        "over the door, chimney with smoke, plain flat backdrop")),
    "mine-gate": dict(seed=714041, height=119, ratio=3.5, prompt=(
        "pixel art sprite of one single mine entrance gatehouse built into dark rock, heavy timber "
        "gate frame, dark tunnel mouth, iron portcullis, stone tower above with a steep dark purple "
        "slate roof, lanterns, the building sits alone on a completely flat solid bright chroma "
        "green background, chroma key green screen, no scenery")),
    "noticeboard": dict(seed=714032, height=134, ratio=4.0, prompt=(
        "pixel art sprite of one single small stone guild hall whose front is plastered with wanted "
        "posters, dozens of torn paper notices nailed across a big timber board beside the door, "
        "iron lantern, steep dark purple slate roof, two storeys, whole building visible, plain "
        "flat backdrop")),
}

NEGATIVE = (
    "two buildings, three buildings, multiple, grid, sprite sheet, tiled, duplicate, repeated, "
    "collage, isometric, aerial view, top down, ground, grass, dirt, cobblestone, base plate, "
    "diorama, town, street, fence, tree, people, text, watermark, blur, photo, realistic, "
    "3d render, cropped, cut off")


def graph(prompt: str, seed: int, prefix: str, w: int = 896, h: int = 1152, batch: int = 4) -> dict:
    """The chain art/build/forge.build.json records, as an API-format ComfyUI graph.

    896x1152 rather than 832x1216: the taller frame made the LoRA emit a 2x2 sheet of building
    variants instead of one subject, every time.
    """
    return {
        "1": {"class_type": "CheckpointLoaderSimple",
              "inputs": {"ckpt_name": "sd_xl_base_1.0.safetensors"}},
        "11": {"class_type": "LoraLoader",
               "inputs": {"model": ["1", 0], "clip": ["1", 1],
                          "lora_name": "pixel-art-xl.safetensors",
                          "strength_model": 0.9, "strength_clip": 0.9}},
        "2": {"class_type": "LoadImage", "inputs": {"image": "ref-market.png"}},
        "3": {"class_type": "IPAdapterUnifiedLoader",
              "inputs": {"model": ["11", 0], "preset": "STANDARD (medium strength)"}},
        "4": {"class_type": "IPAdapter",
              "inputs": {"model": ["3", 0], "ipadapter": ["3", 1], "image": ["2", 0],
                         "weight": 0.9, "weight_type": "style transfer",
                         "start_at": 0, "end_at": 1}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["11", 1]}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["11", 1]}},
        "7": {"class_type": "EmptyLatentImage",
              "inputs": {"width": w, "height": h, "batch_size": batch}},
        "8": {"class_type": "KSampler",
              "inputs": {"model": ["4", 0], "positive": ["5", 0], "negative": ["6", 0],
                         "latent_image": ["7", 0], "seed": seed, "steps": 30, "cfg": 7,
                         "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 1}},
        "9": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["1", 2]}},
        "10": {"class_type": "SaveImage",
               "inputs": {"images": ["9", 0], "filename_prefix": prefix}},
    }


def cut(im: Image.Image, tol: int = 60) -> Image.Image:
    """Border-seeded flood fill against a GLOBAL reference colour.

    The reference is the median of the frame's own border pixels -- the recipe the build.json
    files record. Comparing each candidate against that ONE colour, rather than against the
    neighbour it was reached from, is the whole difference between a cutout and a hole: region
    growing walks a chain of small steps from a dark backdrop into dark stone and keeps going.
    """
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()

    border = ([px[x, 0][:3] for x in range(w)] + [px[x, h - 1][:3] for x in range(w)]
              + [px[0, y][:3] for y in range(h)] + [px[w - 1, y][:3] for y in range(h)])
    ref = tuple(sorted(c[i] for c in border)[len(border) // 2] for i in range(3))

    def is_bg(x: int, y: int) -> bool:
        r, g, b, _ = px[x, y]
        return abs(r - ref[0]) + abs(g - ref[1]) + abs(b - ref[2]) <= tol

    bg = bytearray(w * h)
    q: deque = deque()

    def seed(x: int, y: int) -> None:
        if not bg[y * w + x] and is_bg(x, y):
            bg[y * w + x] = 1
            q.append((x, y))

    for x in range(w):
        seed(x, 0)
        seed(x, h - 1)
    for y in range(h):
        seed(0, y)
        seed(w - 1, y)

    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h and not bg[ny * w + nx] and is_bg(nx, ny):
                bg[ny * w + nx] = 1
                q.append((nx, ny))

    for y in range(h):
        row = y * w
        for x in range(w):
            if bg[row + x]:
                px[x, y] = (0, 0, 0, 0)
    return im


def fit(im: Image.Image, target_h: int) -> Image.Image:
    """Crop to the alpha box, BOX-downsample to the target height, pad 1px of transparency.

    The pad is not cosmetic: VenueArtContractTests assertion 1 fails a venue baked flush to its own
    canvas edge, because such a sprite cannot be repositioned or resized without clipping.
    """
    box = im.getbbox()
    if box is None:
        raise SystemExit("cutout removed everything -- lower the tolerance")
    im = im.crop(box)
    target_w = max(1, round(im.width * target_h / im.height))
    im = im.resize((target_w, target_h), Image.BOX)
    out = Image.new("RGBA", (im.width + 2, im.height + 2), (0, 0, 0, 0))
    out.paste(im, (1, 1))
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--venue", choices=sorted(VENUES), help="enqueue a 4-candidate batch")
    ap.add_argument("--cut", nargs=2, metavar=("IN", "OUT"), help="cut and size one candidate")
    ap.add_argument("--height", type=int, help="target height for --cut, before the 1px pad")
    ap.add_argument("--tolerance", type=int, default=60,
                    help="background match tolerance, summed RGB distance (default 60)")
    args = ap.parse_args()

    if args.venue:
        v = VENUES[args.venue]
        body = json.dumps({"prompt": graph(v["prompt"], v["seed"], f"cand_{args.venue}")}).encode()
        req = urllib.request.Request(HOST + "/prompt", data=body,
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req) as r:
            print(json.load(r)["prompt_id"])
        return 0

    if args.cut:
        src, dst = args.cut
        if not args.height or args.height <= 0:
            print("--cut needs --height", file=sys.stderr)
            return 2
        out = fit(cut(Image.open(src), args.tolerance), args.height)
        pathlib.Path(dst).parent.mkdir(parents=True, exist_ok=True)
        out.save(dst)
        opaque = sum(1 for p in out.get_flattened_data() if p[3] > 0)
        fill = opaque / (out.width * out.height)
        print(f"{dst}: {out.width}x{out.height}, {fill:.0%} fill, "
              f"{out.height / CHARACTER_BODY_PX:.2f}x the character body")
        if fill > 0.92:
            print("  WARNING: >92% fill usually means the backdrop was NOT removed -- render the "
                  "town and look before believing this one", file=sys.stderr)
        return 0

    ap.print_help()
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
