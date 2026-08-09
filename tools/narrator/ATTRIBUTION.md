# The narrator's voice — where it comes from, and what that obliges us to say

`vctk-p254-reference.flac` is twenty seconds of **speaker p254 of the CVSTR VCTK Corpus**,
recorded at the Centre for Speech Technology Research, University of Edinburgh, and
released under **Creative Commons Attribution 4.0 International (CC BY 4.0)**.

That licence permits commercial use and derivative works — including voice cloning — on
one condition: **attribution**. So the game says it, in the credits, in these words:

> Voice derived from the CSTR VCTK Corpus (University of Edinburgh), CC BY 4.0.

`tools/generate-narrator-lines.py` clones this clip with Chatterbox (MIT model, MIT code)
to bake `godot/assets/audio/narrator/*.ogg`. The clone is a derivative of a CC BY 4.0
work, which is why the credit line is not optional and why the reference clip is committed
rather than fetched: a build input that lives only on one machine is a build that cannot
be reproduced, and an attribution that depends on remembering is an attribution that will
be dropped the first time someone regenerates the library.

## Why a recorded human and not a synthetic voice

The bake-off ran four ways — Kokoro `bm_george`, Kokoro `bm_lewis`, a Chatterbox clone of
`bm_lewis`, and this. The synthetic voices are clean and read as a machine being careful.
p254 reads as a person who has seen this before, which is the only register the narrator
has: he is not selling the moment, he already knows how it ends. The owner picked it by
ear, which is the only instrument that settles this.

## What this licence does NOT cover

Cloning a **living public person's** voice is a different question with a different answer
(right of publicity, the ELVIS Act) and a public-domain recording of them — LibriVox, an
old broadcast — is not consent. VCTK's speakers consented to exactly this use at recording
time. That is why the reference is a corpus speaker and not a famous narrator, and any
future voice must clear the same bar before it lands.
