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

## THIS SCRIPT DOES NOT CURATE. Measured, 2026-08-14.

An earlier version of this file claimed to auto-screen out the known failure modes — concept
sheets, plates, pairs — so that a 90-image batch would not need eyes. That claim was tested and is
false, twice over:

1. **The screen rejected the game's own shipped art.** Run against all 48 committed item icons —
   curated, hand-picked, in the game today — the plausible-looking thresholds rejected **16 of
   them**. A tower shield fills 91% of its frame; `item-engineering-clockwork-glaive` splits 50/50
   into two connected components; `item-kite-shield` splits 56/44. A legitimate two-part item is
   numerically indistinguishable from a two-item concept sheet, so the multi-subject test was
   deleted rather than tuned.

2. **What survives the screen is still mostly wrong.** A real batch over eight starter recipes
   rendered 42 candidates, of which 13 passed every structural check. Looking at those 13: two are
   arguably usable. The rest are a cake stand and a lidded urn (for a buckler), a full armoured
   figure (for a hauberk), a sword *plus* a figure, and a set of weapons that are simply not the
   weapon that was asked for. Silhouette and coverage cannot see any of that.

So the screen now does one honest job — discard a cutout that came back EMPTY, which means BiRefNet
found no subject — and reports the measurements for everything else so a human can sort a batch
quickly. **Every kept candidate needs eyes before it ships.** The bottleneck for item variation is
art direction (the subject strings are specific and good; the master prompt's "one structure
centered" pulls hard toward furniture), not throughput.

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

# --- screening thresholds -------------------------------------------------------------------------
# CALIBRATED against all 48 committed item icons, not guessed. That distinction is the whole reason
# these numbers are what they are: the first version of this screen used plausible-sounding limits
# (coverage <= 0.72, one blob >= 70% of the opaque area) and **would have rejected 16 of the 48
# icons the game actually ships** — the curated, hand-picked ones. Measured spread of the shipped
# set:
#
#     coverage      0.137 .. 0.909   (median 0.573)   a tower shield legitimately fills the frame
#     aspect        0.330 .. 1.400   (median 0.946)
#     largest blob  0.265 .. 1.000   (median 1.000)
#     runner-up     0.000 .. 0.497   (median 0.000)
#
# The last row killed the multi-subject test outright. `item-engineering-clockwork-glaive` splits
# 50/50 and `item-kite-shield` splits 56/44 — a legitimate two-part item is numerically identical
# to a two-item concept sheet, so no threshold on blob shares can separate them. It was dropped
# rather than tuned; a gate that fires on a third of the shipped corpus is not a gate.
#
# What survives is the one check the corpus supports: a cutout that came back empty or nearly so,
# which means BiRefNet found no subject (usually because SDXL returned a light-ground plate). The
# rest is REPORTED alongside each candidate as information for the human doing the actual curating.
MIN_ALPHA_COVERAGE = 0.05       # shipped minimum is 0.137; this only catches a failed cutout
MIN_ASPECT, MAX_ASPECT = 0.25, 4.00  # shipped range is 0.33 .. 1.40, widened so it never gates alone


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


def reclaim_comfy_vram() -> None:
    """Unload ComfyUI's cached models before the startup check.

    An idle ComfyUI holds its last checkpoint resident — around 7 GB of a 16 GB card — so a second
    run started minutes after a first would fail the startup floor while the GPU is, in every sense
    that matters, free. That is a real trap and not a hypothetical: it is exactly how this script's
    first batch aborted. The model reloads on the first render anyway (a few seconds), so the only
    thing this costs is that reload, and what it buys is a startup check that measures OTHER
    pressure — a game, a browser, another agent's job — rather than our own cache.

    Best-effort: a ComfyUI too old to expose /free, or not running at all, is not a reason to stop
    here. The startup check immediately after is the real gate."""
    try:
        post_raw("/free", {"unload_models": True, "free_memory": True})
        time.sleep(2.0)  # the free is asynchronous; give the allocator a moment to actually return it
    except (urllib.error.URLError, urllib.error.HTTPError, OSError) as exc:
        print(f"note: could not ask ComfyUI to free VRAM ({exc}); checking the GPU as-is")


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


def post_raw(path: str, payload: dict) -> bytes:
    """POST and hand back the body untouched. `/free` answers with an EMPTY body, so a helper that
    always parses JSON turns a successful call into a JSONDecodeError — which is how the first run
    of this script died after passing every GPU check."""
    request = urllib.request.Request(
        f"{COMFY}{path}", data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def post(path: str, payload: dict) -> dict:
    return json.loads(post_raw(path, payload))


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
def blob_shares(alpha: Image.Image, threshold: int = 16) -> tuple[float, float]:
    """(largest, second-largest) 4-connected component as shares of all opaque pixels. A lone item
    is ~(1.0, 0.0); a variation plate or an inventory grid splits both ways. Iterative flood fill
    (no recursion) — these are 512x512 images and Python's stack is not.

    Both numbers are needed, measured: a candidate showing a longsword AND a full armoured figure
    scored 0.72 on the largest blob alone and PASSED a largest-only check, because one of two big
    subjects can still dominate. The second-largest share is what actually says "there are two
    things here", and it is nearly zero for a legitimate single item with a stray speck."""
    width, height = alpha.size
    pixels = alpha.load()
    seen = bytearray(width * height)
    opaque = 0
    sizes: list[int] = []

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
            sizes.append(size)

    if not opaque:
        return 0.0, 0.0
    sizes.sort(reverse=True)
    second = sizes[1] if len(sizes) > 1 else 0
    return sizes[0] / opaque, second / opaque


def screen(cutout: Image.Image) -> tuple[bool, str]:
    """(kept, description). KEPT IS NOT "GOOD" — see the threshold block above. This rejects a
    cutout that came back empty or absurdly shaped, and describes everything else so whoever
    curates the batch can sort by the numbers instead of opening ninety files blind."""
    alpha = cutout.getchannel("A")
    width, height = alpha.size
    # get_flattened_data(), not the deprecated getdata() — Pillow 14 removes it (2027-10-15). Same
    # call gen_town_sprites.py and gen-market.py already moved to.
    coverage = sum(1 for p in alpha.get_flattened_data() if p >= 16) / float(width * height)

    box = alpha.getbbox()
    if not box:
        return False, "no opaque pixels at all — the cutout found no subject"
    if coverage < MIN_ALPHA_COVERAGE:
        return False, f"near-empty cutout ({coverage:.1%} opaque) — usually a light-ground plate"

    aspect = (box[2] - box[0]) / max(1, box[3] - box[1])
    if not (MIN_ASPECT <= aspect <= MAX_ASPECT):
        return False, f"bounding box aspect {aspect:.2f} outside [{MIN_ASPECT}, {MAX_ASPECT}]"

    _, runner_up = blob_shares(alpha)
    parts = "1 part" if runner_up < 0.02 else f"second part {runner_up:.0%}"
    return True, f"KEPT FOR REVIEW — {coverage:.1%} opaque, aspect {aspect:.2f}, {parts}"


# cutout.py needs torch + transformers (BiRefNet). The README's route is a dedicated venv under
# art/pipeline/.venv, which is the right answer for a workstation that does this often. It does not
# exist on a fresh checkout, and installing a CUDA torch build to remove some backgrounds is a
# 2.5 GB detour — so fall back to ComfyUI portable's own embedded interpreter, which necessarily
# already has a working GPU torch (it is what renders the candidates in the first place). Explicit
# ladder rather than a bare `sys.executable`, because the failure that motivated it was
# cutout.py exiting with "missing dependency (torch)" AFTER a successful render had already been
# paid for.
CUTOUT_INTERPRETERS = [
    os.path.join(os.path.dirname(__file__), ".venv", "Scripts", "python.exe"),
    r"C:\Tools\ComfyUI_windows_portable\python_embeded\python.exe",
    sys.executable,
]


def cutout_interpreter() -> str:
    for candidate in CUTOUT_INTERPRETERS:
        if candidate == sys.executable or os.path.exists(candidate):
            return candidate
    return sys.executable


def cutout(source: str, dest: str) -> None:
    result = subprocess.run(
        [cutout_interpreter(), os.path.join(os.path.dirname(__file__), "cutout.py"),
         source, dest, "--trim"],
        capture_output=True, text=True)
    if result.returncode != 0:
        die(f"cutout.py failed on {source} using {cutout_interpreter()}:\n"
            f"{result.stdout}\n{result.stderr}")


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
    reclaim_comfy_vram()
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
    print(f"\n{made} kept of {attempted} rendered; GPU now {free} MiB free at {temp} C")
    print("KEPT IS NOT CURATED. The screen only discards empty cutouts and absurd aspect ratios; it "
          "cannot tell a buckler from a candy dish, and has been measured doing exactly that. Look "
          "at every kept image before any of it ships.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
