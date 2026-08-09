#!/usr/bin/env python3
"""Bake the narrator's spoken library.

The generate -> curate -> freeze pipeline the owner ruled for the narrator
(MAKERS-MARK.md 11.7.6): lines are synthesised at CONTENT time, listened to, and
committed as static audio. Nothing here runs at play time -- runtime selection stays
deterministic in NarratorVoiceDirector, the golden replay is untouched, and the
sim's no-runtime-LLM law is never in question.

    python tools/generate-narrator-lines.py                 # the shipped voice
    python tools/generate-narrator-lines.py --dry-run       # parse and report only
    python tools/generate-narrator-lines.py --engine kokoro --voice bm_george

TWO ENGINES, and the shipped one is `chatterbox`:

  chatterbox  Chatterbox (MIT model, MIT code) cloning a REAL RECORDED HUMAN from
              tools/narrator/vctk-p254-reference.flac -- speaker p254 of the VCTK
              Corpus (CC BY 4.0, consent-recorded at the University of Edinburgh).
              It won the bake-off on the only axis that mattered: it sounds like a
              person who has seen this before, where the synthetic voices sound like
              a machine being careful. See tools/narrator/ATTRIBUTION.md.
  kokoro      Kokoro-82M via kokoro-onnx (Apache-2.0 model, MIT wrapper). Kept as the
              zero-dependency fallback: pure CPU, one second a line, no reference clip.

THE LINES ARE NOT DEFINED HERE. They live in
sim/GameSim/Presentation/NarratorVoiceDirector.cs, which is what the game actually
reads, and this script parses them out of it. Two copies of the library would drift
the moment someone edited one, and the failure would be silent -- a recording whose
text no longer matches what the screen says. The pinned counts below are the tripwire
for a parse that half-works.
"""

import argparse
import os
import re
import subprocess
import sys

# Where the isolated TTS installs live. Kept out of the repo: together they are over a
# gigabyte of model weights, and they are build tools, not dependencies of the game.
TTS_HOME = r"C:\Tools\tts-kokoro"
MODEL = os.path.join(TTS_HOME, "models", "kokoro-v1.0.onnx")
VOICES = os.path.join(TTS_HOME, "models", "voices-v1.0.bin")
CHATTERBOX_HOME = r"C:\Tools\tts-chatterbox"

# Chatterbox is a diffusion model on a 5GB budget. This machine is the owner's daily
# driver and may be running a game; taking the GPU out from under it is not worth
# shaving minutes off a job that runs once. Under the floor, fall back to the CPU
# rather than abort -- twenty lines is ten minutes of CPU and nobody is waiting.
VRAM_FLOOR_BYTES = 14 * 1000 ** 3

# What the C# pins. A parse that finds a different shape is a parse that is wrong --
# and silently generating 3 lines instead of 6 would leave the library quietly partial.
EXPECTED = {"VigilOpening": 6, "DeathEpitaph": 6, "ProvenSave": 4, "KillingBlow": 4}

SLUG = {
    "VigilOpening": "vigil-opening",
    "DeathEpitaph": "death-epitaph",
    "ProvenSave": "proven-save",
    "KillingBlow": "killing-blow",
}

# Baked loudness. Every line lands at the same level so ONE number in AudioDirector
# (NarratorDb) is the whole mix, and no line ever needs a positive trim to be heard --
# the +5.45dB boost on night-still-long is why that rule exists.
TARGET_LUFS = -16.0

# Slower than conversational. The narrator is not in a hurry; nothing is waiting on him.
SPEED = 0.92

# Ceiling applied AFTER loudness-matching, in the float domain, before the ogg encode.
# 0.97 was not enough and this is measurable, not theoretical: three of the twenty lines came
# back OVER full scale when decoded (death-epitaph-02 at 1.014), because Vorbis is lossy and
# the decoded waveform overshoots the samples it was given. ~1dB of headroom absorbs that.
# The check that catches a regression here is the QC pass: decode every written file and
# assert its peak, rather than trusting the number we wrote before the encoder touched it.
PEAK_CEILING = 0.89


def repo_root() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    while here and not os.path.exists(os.path.join(here, "Game.sln")):
        parent = os.path.dirname(here)
        if parent == here:
            sys.exit("could not find Game.sln above " + os.path.dirname(os.path.abspath(__file__)))
        here = parent
    return here


def parse_lines(root: str) -> dict:
    """Pull the spoken library out of the C# that the game actually reads."""
    path = os.path.join(root, "sim", "GameSim", "Presentation", "NarratorVoiceDirector.cs")
    with open(path, encoding="utf-8") as fh:
        source = fh.read()

    out = {}
    for trigger in EXPECTED:
        block = re.search(
            r"\[Trigger\." + trigger + r"\]\s*=\s*\[(.*?)\]\s*,\s*\n", source, re.S)
        if not block:
            sys.exit(f"could not find the {trigger} block in {path}")
        # Only whole double-quoted strings; the file has no escaped quotes inside lines
        # and a test forbids template slots, so this stays simple on purpose.
        found = re.findall(r'"([^"\\]*)"', block.group(1))
        out[trigger] = found

    for trigger, want in EXPECTED.items():
        got = len(out[trigger])
        if got != want:
            sys.exit(f"{trigger}: parsed {got} lines, expected {want}. The C# changed shape "
                     f"or the parse broke -- refusing to bake a partial library.")
    return out


def load_kokoro(args):
    """The fallback engine: pure CPU, no reference clip, one voice id."""
    sys.path.insert(0, TTS_HOME)
    try:
        from kokoro_onnx import Kokoro
    except ImportError as exc:
        sys.exit(f"Kokoro not installed ({exc}). Set it up with:\n"
                 f'  <python313> -m pip install --target "{TTS_HOME}" kokoro-onnx soundfile pyloudnorm\n'
                 f"  then fetch kokoro-v1.0.onnx and voices-v1.0.bin from the kokoro-onnx releases "
                 f'into "{os.path.join(TTS_HOME, "models")}".')
    if not os.path.exists(MODEL):
        sys.exit(f"missing model weights at {MODEL}")

    kokoro = Kokoro(MODEL, VOICES)
    if args.voice not in kokoro.get_voices():
        sys.exit(f"unknown voice '{args.voice}'. Available: {', '.join(sorted(kokoro.get_voices()))}")
    lang = args.lang or ("en-gb" if args.voice.startswith("bm_") else "en-us")

    def speak(text, _seed):
        samples, sr = kokoro.create(text, voice=args.voice, speed=SPEED, lang=lang)
        return samples, sr

    return speak, f"kokoro/{args.voice} {lang}"


def load_chatterbox(args, root):
    """The shipped engine: clone a recorded human from a committed reference clip."""
    sys.path.insert(0, CHATTERBOX_HOME)
    try:
        import torch
        from chatterbox.tts import ChatterboxTTS
    except ImportError as exc:
        sys.exit(f"Chatterbox not installed ({exc}). Set it up with:\n"
                 f'  <python313> -m pip install --target "{CHATTERBOX_HOME}" chatterbox-tts "setuptools<81"\n'
                 f"  then DELETE the torch/ torchaudio/ functorch/ torchgen/ dirs it drags in -- they are a\n"
                 f"  CPU build that would shadow this machine's CUDA torch.")

    ref = args.ref or os.path.join(root, "tools", "narrator", "vctk-p254-reference.flac")
    if not os.path.exists(ref):
        sys.exit(f"missing the reference clip at {ref}")

    device = "cpu"
    if torch.cuda.is_available():
        free, _ = torch.cuda.mem_get_info()
        if free >= VRAM_FLOOR_BYTES:
            device = "cuda"
        else:
            print(f"  GPU has only {free/1e9:.1f}GB free (floor {VRAM_FLOOR_BYTES/1e9:.0f}GB) "
                  f"-- synthesising on the CPU instead, about 10s a line")
    if device == "cpu":
        torch.set_num_threads(max(1, (os.cpu_count() or 8) - 2))

    model = ChatterboxTTS.from_pretrained(device=device)

    def speak(text, seed):
        # Chatterbox samples; without a pinned seed the same command produces a different
        # library every run, and "regenerate line 4" becomes "regenerate everything".
        torch.manual_seed(seed)
        wav = model.generate(text, audio_prompt_path=ref, exaggeration=args.exaggeration,
                             cfg_weight=args.cfg, temperature=args.temperature)
        return wav.squeeze(0).cpu().numpy(), model.sr

    return speak, f"chatterbox/{os.path.basename(ref)} on {device}"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--engine", default="chatterbox", choices=("chatterbox", "kokoro"),
                    help="chatterbox (shipped: clones the VCTK p254 reference) or kokoro (fallback)")
    ap.add_argument("--ref", default=None,
                    help="chatterbox reference clip; defaults to tools/narrator/vctk-p254-reference.flac")
    ap.add_argument("--takes", type=int, default=3,
                    help="chatterbox takes per line; the median-length one is kept (default 3)")
    ap.add_argument("--exaggeration", type=float, default=0.35)
    ap.add_argument("--cfg", type=float, default=0.5)
    ap.add_argument("--temperature", type=float, default=0.7)
    ap.add_argument("--voice", default="bm_george",
                    help="Kokoro voice id (bm_* British, am_* American). Default bm_george: "
                         "older, dry, and warm without pushing -- the register the tone law asks for.")
    ap.add_argument("--lang", default=None, help="defaults to en-gb for bm_*, en-us otherwise")
    ap.add_argument("--only", default=None,
                    help="regenerate one trigger only (e.g. death-epitaph), leaving the rest alone")
    ap.add_argument("--dry-run", action="store_true", help="parse and report, synthesise nothing")
    args = ap.parse_args()

    root = repo_root()
    library = parse_lines(root)
    total = sum(len(v) for v in library.values())
    print(f"parsed {total} lines from NarratorVoiceDirector.cs")

    if args.only:
        keep = {t: l for t, l in library.items() if SLUG[t] == args.only}
        if not keep:
            sys.exit(f"unknown trigger '{args.only}'. Try one of: {', '.join(sorted(SLUG.values()))}")
        library = keep

    if args.dry_run:
        for trigger, lines in library.items():
            for i, text in enumerate(lines):
                print(f"  {SLUG[trigger]}-{i:02d}  {text}")
        return

    sys.path.insert(0, CHATTERBOX_HOME if args.engine == "chatterbox" else TTS_HOME)
    try:
        import soundfile as sf
        import numpy as np
        import pyloudnorm as pyln
    except ImportError as exc:
        sys.exit(f"missing audio deps ({exc}) -- soundfile, numpy and pyloudnorm are required")

    if args.engine == "chatterbox":
        speak, describe = load_chatterbox(args, root)
        takes = max(1, args.takes)
    else:
        speak, describe = load_kokoro(args)
        takes = 1  # Kokoro is deterministic; extra takes would be identical files.

    out_dir = os.path.join(root, "godot", "assets", "audio", "narrator")
    os.makedirs(out_dir, exist_ok=True)

    meter = None
    written = 0
    for trigger, lines in library.items():
        for i, text in enumerate(lines):
            # A take that races or drags is the usual failure of a cloning model, and both
            # show up as an outlier duration. Taking the MEDIAN of three discards the outlier
            # in either direction without a human in the loop -- which is the honest limit
            # here: this picks the least-weird take, not the best-acted one.
            candidates = []
            for take in range(takes):
                samples, sr = speak(text, seed=1000 * (i + 1) + take)
                candidates.append(np.asarray(samples, dtype=np.float32))
            candidates.sort(key=len)
            samples = candidates[len(candidates) // 2]

            if meter is None or meter.rate != sr:
                meter = pyln.Meter(sr)

            loudness = meter.integrated_loudness(samples)
            samples = pyln.normalize.loudness(samples, loudness, TARGET_LUFS)

            # Normalising to a fixed loudness can push transients past full scale; a clipped
            # narrator is worse than a quiet one, so back the whole line off rather than let
            # it distort.
            peak = float(np.max(np.abs(samples)))
            if peak > PEAK_CEILING:
                samples = samples * (PEAK_CEILING / peak)

            name = f"{SLUG[trigger]}-{i:02d}.ogg"
            path = os.path.join(out_dir, name)
            sf.write(path, samples, sr, format="OGG", subtype="VORBIS")

            # Read back what the ENCODER produced, not what we handed it. The lossy round trip
            # is where the clipping came from, so it is the only place the check means anything.
            decoded, _ = sf.read(path, dtype="float32")
            out_peak = float(np.max(np.abs(decoded)))
            warn = "  CLIPPED AFTER ENCODE" if out_peak >= 1.0 else ""
            print(f"  {name:24s} {len(samples)/sr:5.2f}s  raw {loudness:6.1f} LUFS  "
                  f"peak {out_peak:.3f}{warn}")
            written += 1

    print(f"\nwrote {written} lines to {out_dir} ({describe}, {TARGET_LUFS} LUFS)")
    print("Listen before committing. Delete a take you would not want to hear twice.")


if __name__ == "__main__":
    main()
