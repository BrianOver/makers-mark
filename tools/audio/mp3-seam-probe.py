#!/usr/bin/env python3
"""Report the loop-seam facts about an MP3: duration, and whether it can loop gaplessly.

WHY THIS EXISTS
---------------
The owner has reported "random static" in the music on two separate playtests. Both times the
investigation reached for a plausible cause (inter-sample clipping, a too-hot noise layer),
re-encoded some files, and the complaint came back. What was never established was the mechanical
one: an MP3 cannot loop seamlessly unless the encoder wrote a gapless tag.

MP3 is a lapped transform. Every encoder prepends silence (encoder delay) and pads the final frame
out to a whole 1152-sample block. A decoder can only strip those if the file says how many samples
to skip -- that is what LAME's `Xing`/`LAME` header carries in its enc-delay/enc-padding fields.
A bare `Info` header (what ffmpeg/Lavf writes by default) declares the file CBR and nothing else.
So on every wrap, the decoder replays the delay and the padding: a burst of non-signal at the seam.
Heard once it is a click; heard over a bed it reads as static.

The discriminator is EXPOSURE, not the track. Cross this tool's `dur` against how long a phase
actually held a bed (the `audio` rows in a `runs/playtest/session-*.jsonl` carry `secs=`), and the
wrap count falls out as arithmetic. In the owner's 2026-08-14 session the only bed he did NOT
complain about was the only one that never reached its first wrap.

Do not infer "which track is clean" from a doc comment about how long a phase lasts. A comment is a
claim; this tool and the session log are measurements. That exact substitution produced a wrong
conclusion once already (see MAKERS-MARK.md 11.12's correction note).

USAGE
-----
    python tools/audio/mp3-seam-probe.py godot/assets/audio/*.mp3

Exit code is 1 if any file lacks gapless metadata, so this can gate a build.
"""

import struct
import sys

# MPEG-1 Layer III bitrate and sample-rate tables, indexed by the header's nibbles.
BITRATES_KBPS = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0]
SAMPLE_RATES_HZ = [44100, 48000, 32000, 0]
SAMPLES_PER_FRAME = 1152


def _skip_id3(data):
    """Byte offset of the first audio frame, plus a description of any ID3v2 tag."""
    if data[:3] != b"ID3":
        return 0, None
    # ID3v2 sizes are syncsafe: seven bits per byte, high bit always clear.
    b = struct.unpack(">4B", data[6:10])
    size = (b[0] << 21) | (b[1] << 14) | (b[2] << 7) | b[3]
    return 10 + size, f"ID3v2 {size}B"


def _first_frame(data, start):
    """Index of the first frame sync (11 set bits), or -1."""
    i = start
    while i < len(data) - 4:
        if data[i] == 0xFF and (data[i + 1] & 0xE0) == 0xE0:
            return i
        i += 1
    return -1


def probe(path):
    with open(path, "rb") as fh:
        data = fh.read()

    offset, id3 = _skip_id3(data)
    frame = _first_frame(data, offset)
    if frame < 0:
        return {"path": path, "error": "no MPEG frame sync found"}

    header = data[frame:frame + 4]
    bitrate = BITRATES_KBPS[(header[2] >> 4) & 0x0F]
    sample_rate = SAMPLE_RATES_HZ[(header[2] >> 2) & 0x03]
    channels = 1 if ((header[3] >> 6) & 0x03) == 3 else 2

    # The Xing/Info header sits inside the first frame, after the side info. Its offset depends on
    # version and channel count; scanning the frame for the tag is simpler and just as reliable.
    window = data[frame:frame + 1024]
    xing = None
    for tag in (b"Xing", b"Info"):
        if tag in window:
            xing = tag.decode()
            xing_at = frame + window.index(tag)
            break

    frames = None
    enc_delay = None
    enc_padding = None
    if xing is not None:
        flags = struct.unpack(">I", data[xing_at + 4:xing_at + 8])[0]
        cursor = xing_at + 8
        if flags & 0x0001:  # frame count present
            frames = struct.unpack(">I", data[cursor:cursor + 4])[0]
            cursor += 4
        if flags & 0x0002:  # byte count
            cursor += 4
        if flags & 0x0004:  # TOC
            cursor += 100
        if flags & 0x0008:  # quality
            cursor += 4
        # The gapless numbers live in a LAME extension, and the ONLY reliable way to find it is the
        # literal b"LAME" marker inside this frame. Two wrong tests were tried first and both
        # reported every one of this repo's four beds as gapless -- the exact opposite of the truth:
        #
        #   1. Trusting the encoder string. ffmpeg writes "Lavf"/"Lavc" and leaves the gapless
        #      fields absent entirely; the name says who encoded it, never what it wrote.
        #   2. Walking a cursor past the Xing flag fields to a fixed offset. When no extension is
        #      present that cursor lands on raw audio, whose bytes are arbitrary and happily decode
        #      to a plausible-looking non-zero delay.
        #
        # A tool that confidently reports the opposite of the truth is worse than no tool, so the
        # marker search is the test: no b"LAME" marker means no gapless data, full stop.
        marker = data.find(b"LAME", xing_at, xing_at + 250)
        if marker > 0:
            packed = data[marker + 21:marker + 24]
            if len(packed) == 3:
                value = (packed[0] << 16) | (packed[1] << 8) | packed[2]
                enc_delay = value >> 12
                enc_padding = value & 0xFFF

    duration = None
    if frames and sample_rate:
        duration = frames * SAMPLES_PER_FRAME / sample_rate

    return {
        "path": path,
        "sample_rate": sample_rate,
        "bitrate": bitrate,
        "channels": channels,
        "xing": xing,
        "frames": frames,
        "duration": duration,
        "enc_delay": enc_delay,
        "enc_padding": enc_padding,
        "id3": id3,
        # Gapless requires BOTH numbers. A file with an Info header and no delay/padding pair
        # cannot be looped seamlessly by any decoder, because the information simply is not there.
        "gapless": enc_delay is not None and enc_padding is not None,
    }


def main(argv):
    paths = argv[1:]
    if not paths:
        print(__doc__.strip().split("USAGE")[1].strip(), file=sys.stderr)
        return 2

    any_ungapless = False
    for path in paths:
        r = probe(path)
        if "error" in r:
            print(f"{path}: ERROR {r['error']}")
            any_ungapless = True
            continue

        name = r["path"].replace("\\", "/").rsplit("/", 1)[-1]
        dur = f"{r['duration']:.2f}s" if r["duration"] else "unknown"
        seam = "GAPLESS" if r["gapless"] else "NO GAPLESS TAG -> replays delay+padding on every wrap"
        print(
            f"{name:24s} {r['sample_rate']}Hz {r['bitrate']}k "
            f"{'stereo' if r['channels'] == 2 else 'mono'}  "
            f"hdr={r['xing']}  dur={dur}  {seam}"
        )
        if not r["gapless"]:
            any_ungapless = True

    if any_ungapless:
        print(
            "\nAt least one file cannot loop gaplessly. Re-encode with LAME "
            "(`lame --nogap`, or ffmpeg's libmp3lame which writes the LAME header), "
            "or ship the bed as a synthesized loop whose end already meets its beginning.",
            file=sys.stderr,
        )
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
