#!/usr/bin/env python
"""Render VARIATION SIBLINGS for the committed item icons — `item-<recipe>-v2`, `-v3`, … — so two
swords off one recipe are not the same picture.

Owner direction, 2026-08-14: "Heroes, NPCs, enemies, items we craft etc should all be a little
unique — obviously we cannot generate on the fly so we need a large collection of assets that get
randomly picked." The picking half is `GodotClient.ArtVariants`; this is the collection half for
items. The town figures' half is `tools/art/gen_town_sprites.py` (procedural, no GPU).

A sibling is the SAME spec at a DIFFERENT SEED: same master prompt, same track profile, same
palette-family clause, same negative escalation — read straight out of `AssetRegistry` by
`dump-item-specs`, never retyped here. That keeps a variant recognisably the same KIND of object
(a hero must still read "that's a Gloomsteel Blade" at a glance) while the specific blade differs.

    dotnet run --project art/pipeline/dump-item-specs -- art/pipeline/item-jobs.json
    python art/pipeline/gen-item-variants.py --jobs art/pipeline/item-jobs.json --variants 2

Seed offsets start at VARIANT_SEED_STRIDE (1000), deliberately clear of the `seed+1`, `seed+2`
re-roll offsets the README reserves for curating a BASE icon — so a variant can never collide with
a re-roll of the thing it varies.

## Auto-screening, and its honest limits

Curation is normally a human eye, and 45 recipes x N variants is more than one night of eyes. This
script therefore screens each candidate against the failure modes that are MEASURABLE, and says so
in its report rather than claiming the output is curated:

  * empty / near-empty cutout        -> alpha coverage floor
  * a full-frame plate or backdrop   -> alpha coverage ceiling
  * a concept sheet, pair, or set    -> one connected component must dominate the opaque area
  * a wildly wrong aspect            -> trimmed bounding-box ratio window

It CANNOT judge whether the art is good. Anything it passes still wants a human look before it is
trusted, and the report names how many candidates each item needed.

GPU SAFETY (hard rules, see docs): one job at a time, never start under the free-VRAM floor, abort
on temperature. This script renders strictly serially and re-checks `nvidia-smi` between renders,
aborting the whole run rather than pushing through a hot or crowded GPU.
"""
from __future__ import annotations

import argparse
import io
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

from PIL import Image

COMFY = "http://127.0.0.1:8188"  # IPv4 literal on purpose — "localhost" can resolve to IPv6 (README §0)
CHECKPOINT = "sd_xl_base_1.0.safetensors"
VARIANT_SEED_STRIDE = 1000

# --- GPU guard rails ------------------------------------------------------------------------------
MIN_FREE_MIB_TO_START = 13900   # the free-VRAM floor; measured idle headroom on this 16 GB card
MIN_FREE_MIB_MIDRUN = 3000      # once the model is resident it holds ~8 GB; this is the real floor
MAX_TEMP_C = 83                 # hard abort, never a pause-and-continue

# --- screening thresholds (measured against the committed icon set, see --report) -----------------
MIN_ALPHA_COVERAGE = 0.06
MAX_ALPHA_COVERAGE = 0.72
MIN_DOMINANT_SHARE = 0.70       # largest connected blob as a share of all opaque pixels
MIN_ASPECT, MAX_ASPECT = 0.30, 3.20


def die(message: str) -> None:
    print(f"gen-item-variants.py: {message}", file=sys.stderr)
    raise SystemExit(1)


def gpu_state() -> tuple[int, int]:
    """(free MiB, temp C) straight from nvidia-smi. Never ComfyUI's /system_stats — that reports a
    torch-pool figure that has been observed frozen at a stale startup snapshot for minutes."""
    out = subprocess.run(
        ["nvidia-smi", "--query-gpu=memory.free,temperature.gpu", "--format=csv,noheader,nounits"],
        capture_output=True, text=True, check=True).stdout.strip().splitlines()[0]
    free, temp = (int(v.strip()) for v in out.split(","))
    return free, temp


def check_gpu(floor: int, label: str) -> None:
    free, temp = gpu_state()
    if temp > MAX_TEMP_C:
        die(f"ABORT ({label}): GPU at {temp} C, over the {MAX_TEMP_C} C hard limit")
    if free < floor:
        die(f"ABORT ({label}): only {free} MiB VRAM free, under the {floor} MiB floor")


# --- ComfyUI ---------------------------------------------------------------------------------------
def workflow(job: dict, seed: int) -> dict:
    """A plain SDXL txt2img graph in ComfyUI API format. Kept inline rather than loaded from a saved
    workflow file so the settings that ship are the ones the AssetSpec dictated, with nothing in
    between that can be edited in a GUI and silently disagree."""
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "2": {"class_type": "CLIPTextEncode", "inputs": {"text": job["prompt"], "clip": ["1", 1]}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": job["negative"], "clip": ["1", 1]}},
        "4": {"class_type": "EmptyLatentImage",
              "inputs": {"width": job["width"], "height": job["height"], "batch_size": 1}},
        "5": {"class_type": "KSampler", "inputs": {
            "seed": seed, "steps": job["steps"], "cfg": job["cfg"],
            "sampler_name": job["sampler"], "scheduler": job["scheduler"], "denoise": 1.0,
            "model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0], "latent_image": ["4", 0]}},
        "6": {"class_type": "VAEDecode", "inputs": {"samples": ["5", 0], "vae": ["1", 2]}},
        "7": {"class_type": "SaveImage", "inputs": {"filename_prefix": "mm-variant", "images": ["6", 0]}},
    }


def post(path: str, payload: dict) -> dict:
    request = urllib.request.Request(
        f"{COMFY}{path}", data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def get(path: str) -> bytes:
    with urllib.request.urlopen(f"{COMFY}{path}", timeout=60) as response:
        return response.read()


def render(job: dict, seed: int, timeout_s: float = 180.0) -> Image.Image:
    prompt_id = post("/prompt", {"prompt": workflow(job, seed)})["prompt_id"]

    deadline = time.time() + timeout_s
    while time.time() < deadline:
        time.sleep(1.0)
        history = json.loads(get(f"/history/{prompt_id}") or b"{}")
        entry = history.get(prompt_id)
        if not entry:
            continue
        status = entry.get("status", {})
        if status.get("status_str") == "error":
            die(f"ComfyUI reported an error for {job['id']} seed {seed}: {status}")
        images = [i for out in entry.get("outputs", {}).values() for i in out.get("images", [])]
        if images:
            image = images[0]
            raw = get(f"/view?filename={image['filename']}&subfolder={image.get('subfolder','')}"
                      f"&type={image.get('type','output')}")
            return Image.open(io.BytesIO(raw)).convert("RGB")

    die(f"timed out after {timeout_s}s waiting on {job['id']} seed {seed}")
    raise AssertionError("unreachable")


# --- screening -------------------------------------------------------------------------------------
def largest_blob_share(alpha: Image.Image, threshold: int = 16) -> float:
    """Share of opaque pixels belonging to the single largest 4-connected component. A lone item is
    ~1.0; a three-blade variation plate or an inventory grid splits well below the floor. Iterative
    flood fill (no recursion) — these are 512x512 images and Python's stack is not."""
    width, height = alpha.size
    pixels = alpha.load()
    seen = bytearray(width * height)
    opaque = biggest = 0

    for start_y in range(height):
        for start_x in range(width):
            index = start_y * width + start_x
            if seen[index] or pixels[start_x, start_y] < threshold:
                continue
            size = 0
            stack = [(start_x, start_y)]
            seen[index] = 1
            while stack:
                x, y = stack.pop()
                size += 1
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if 0 <= nx < width and 0 <= ny < height:
                        n = ny * width + nx
                        if not seen[n] and pixels[nx, ny] >= threshold:
                            seen[n] = 1
                            stack.append((nx, ny))
            opaque += size
            biggest = max(biggest, size)

    return biggest / opaque if opaque else 0.0


def screen(cutout: Image.Image) -> tuple[bool, str]:
    alpha = cutout.getchannel("A")
    width, height = alpha.size
    coverage = sum(1 for p in alpha.getdata() if p >= 16) / float(width * height)

    if coverage < MIN_ALPHA_COVERAGE:
        return False, f"near-empty cutout ({coverage:.1%} opaque)"
    if coverage > MAX_ALPHA_COVERAGE:
        return False, f"full-frame plate ({coverage:.1%} opaque)"

    box = alpha.getbbox()
    if not box:
        return False, "no opaque pixels at all"
    aspect = (box[2] - box[0]) / max(1, box[3] - box[1])
    if not (MIN_ASPECT <= aspect <= MAX_ASPECT):
        return False, f"bounding box aspect {aspect:.2f} outside [{MIN_ASPECT}, {MAX_ASPECT}]"

    share = largest_blob_share(alpha)
    if share < MIN_DOMINANT_SHARE:
        return False, f"multiple subjects — largest blob is only {share:.0%} of the opaque area"

    return True, f"ok ({coverage:.1%} opaque, one subject at {share:.0%})"


def cutout(source: str, dest: str) -> None:
    result = subprocess.run(
        [sys.executable, os.path.join(os.path.dirname(__file__), "cutout.py"), source, dest, "--trim"],
        capture_output=True, text=True)
    if result.returncode != 0:
        die(f"cutout.py failed on {source}:\n{result.stdout}\n{result.stderr}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--jobs", required=True, help="JSON from dump-item-specs")
    parser.add_argument("--variants", type=int, default=2,
                        help="extra siblings per item (the base icon is variant 1)")
    parser.add_argument("--tries", type=int, default=4,
                        help="max candidates rendered per wanted variant before giving up on it")
    parser.add_argument("--out", default=os.path.join("art", "pipeline", "candidates"),
                        help="scratch dir for candidates (gitignored)")
    parser.add_argument("--only", default="", help="comma-separated item ids, for a spot check")
    args = parser.parse_args()

    payload = json.load(io.open(args.jobs, encoding="utf-8"))
    jobs = payload["jobs"]
    if args.only:
        wanted = {s.strip() for s in args.only.split(",") if s.strip()}
        jobs = [j for j in jobs if j["id"] in wanted]
    if not jobs:
        die("no jobs selected")

    os.makedirs(args.out, exist_ok=True)
    check_gpu(MIN_FREE_MIB_TO_START, "startup")
    print(f"rendering {args.variants} sibling(s) for {len(jobs)} item(s); "
          f"up to {args.tries} candidates each")

    report: list[str] = []
    made = attempted = 0

    for job in jobs:
        for variant in range(2, 2 + args.variants):
            accepted = None
            for attempt in range(args.tries):
                check_gpu(MIN_FREE_MIB_MIDRUN, f"{job['id']} v{variant}")
                seed = int(job["seed"]) + VARIANT_SEED_STRIDE * variant + attempt
                attempted += 1

                raw_path = os.path.join(args.out, f"{job['id']}-v{variant}-raw{attempt}.png")
                render(job, seed).save(raw_path)

                cut_path = os.path.join(args.out, f"{job['id']}-v{variant}.png")
                cutout(raw_path, cut_path)

                ok, why = screen(Image.open(cut_path).convert("RGBA"))
                if ok:
                    accepted = (seed, attempt, why)
                    break
                os.remove(cut_path)
                report.append(f"  rejected {job['id']}-v{variant} seed {seed}: {why}")

            if accepted:
                made += 1
                report.append(f"ACCEPT {job['id']}-v{variant} seed {accepted[0]} "
                              f"(candidate {accepted[1] + 1}/{args.tries}) — {accepted[2]}")
            else:
                report.append(f"MISS   {job['id']}-v{variant}: no candidate passed screening")

    free, temp = gpu_state()
    print("\n".join(report))
    print(f"\n{made} accepted of {attempted} rendered; GPU now {free} MiB free at {temp} C")
    print("SCREENED, NOT CURATED — every accepted image still wants a human look before it ships.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
