#!/usr/bin/env python3
"""Bake the narrator's spoken library.

The generate -> curate -> freeze pipeline the owner ruled for the narrator
(MAKERS-MARK.md 11.7.6): lines are synthesised at CONTENT time, listened to, and
committed as static audio. Nothing here runs at play time -- runtime selection stays
deterministic in NarratorVoiceDirector, the golden replay is untouched, and the
sim's no-runtime-LLM law is never in question.

    python tools/generate-narrator-lines.py [--voice bm_george] [--dry-run]

TTS is Kokoro-82M via kokoro-onnx -- Apache-2.0 model, MIT wrapper, runs on the CPU
in about a second a line. No API key, no account, no vendor. It was chosen over the
GPU options because a 20-line library does not need 5GB of VRAM and a wheel fight,
and because a dry narrator is the one register small TTS does WELL: understatement
survives flatness, where under-acted warmth reads as a robot.

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

# Where the isolated TTS install lives. Kept out of the repo: it is 350MB of model
# weights plus onnxruntime, and it is a build tool, not a dependency of the game.
TTS_HOME = r"C:\Tools\tts-kokoro"
MODEL = os.path.join(TTS_HOME, "models", "kokoro-v1.0.onnx")
VOICES = os.path.join(TTS_HOME, "models", "voices-v1.0.bin")

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


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--voice", default="bm_george",
                    help="Kokoro voice id (bm_* British, am_* American). Default bm_george: "
                         "older, dry, and warm without pushing -- the register the tone law asks for.")
    ap.add_argument("--lang", default=None, help="defaults to en-gb for bm_*, en-us otherwise")
    ap.add_argument("--dry-run", action="store_true", help="parse and report, synthesise nothing")
    args = ap.parse_args()

    lang = args.lang or ("en-gb" if args.voice.startswith("bm_") else "en-us")
    root = repo_root()
    library = parse_lines(root)
    total = sum(len(v) for v in library.values())
    print(f"parsed {total} lines from NarratorVoiceDirector.cs")

    if args.dry_run:
        for trigger, lines in library.items():
            for i, text in enumerate(lines):
                print(f"  {SLUG[trigger]}-{i:02d}  {text}")
        return

    sys.path.insert(0, TTS_HOME)
    try:
        from kokoro_onnx import Kokoro
        import soundfile as sf
        import numpy as np
        import pyloudnorm as pyln
    except ImportError as exc:
        sys.exit(f"TTS not installed ({exc}). Set it up with:\n"
                 f'  <python313> -m pip install --target "{TTS_HOME}" kokoro-onnx soundfile pyloudnorm\n'
                 f"  then fetch kokoro-v1.0.onnx and voices-v1.0.bin from the kokoro-onnx releases "
                 f'into "{os.path.join(TTS_HOME, "models")}".')

    if not os.path.exists(MODEL):
        sys.exit(f"missing model weights at {MODEL}")

    out_dir = os.path.join(root, "godot", "assets", "audio", "narrator")
    os.makedirs(out_dir, exist_ok=True)

    kokoro = Kokoro(MODEL, VOICES)
    if args.voice not in kokoro.get_voices():
        sys.exit(f"unknown voice '{args.voice}'. Available: {', '.join(sorted(kokoro.get_voices()))}")

    meter = None
    written = 0
    for trigger, lines in library.items():
        for i, text in enumerate(lines):
            samples, sr = kokoro.create(text, voice=args.voice, speed=SPEED, lang=lang)
            samples = samples.astype(np.float32)
            if meter is None:
                meter = pyln.Meter(sr)

            loudness = meter.integrated_loudness(samples)
            samples = pyln.normalize.loudness(samples, loudness, TARGET_LUFS)

            # Normalising to a fixed loudness can push transients past full scale; a clipped
            # narrator is worse than a quiet one, so back the whole line off rather than let
            # it distort.
            peak = float(np.max(np.abs(samples)))
            if peak > 0.97:
                samples = samples * (0.97 / peak)

            name = f"{SLUG[trigger]}-{i:02d}.ogg"
            sf.write(os.path.join(out_dir, name), samples, sr, format="OGG", subtype="VORBIS")
            print(f"  {name:24s} {len(samples)/sr:5.2f}s  raw {loudness:6.1f} LUFS")
            written += 1

    print(f"\nwrote {written} lines to {out_dir} (voice {args.voice}, {lang}, {TARGET_LUFS} LUFS)")
    print("Listen before committing. Delete a take you would not want to hear twice.")


if __name__ == "__main__":
    main()
