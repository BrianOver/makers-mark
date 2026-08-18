# Composed music beds — provenance

Where every music bed in this game came from, and how it was mastered.

The narrator lines have carried an attribution file for a while. The music never did, and
§11.14.6 (U-T4-9) names that gap: *"the narrator has an attribution file, the music has
none."* This is that file. If a bed is regenerated or re-mastered, its row changes in the
same PR — a provenance file that lags the bytes is worse than none, because it makes a
false claim look documented.

## The beds

| id | phase | length | origin | mastered |
|---|---|---|---|---|
| `day-first-light` | Morning | 54.0 s | **Generated 2026-08-17**, ACE-Step 1.5 turbo (local, ComfyUI) | fold + limiter, below |
| `town-dusk` | Evening | 50.0 s | **Generated 2026-08-17**, ACE-Step 1.5 turbo (local, ComfyUI) | fold + limiter, below |
| `night-still` | Camp | 51.5 s | earlier generation, repaired in place by U-T4-8 (trim + 4 s equal-power fold) | see U-T4-8 |
| `quest-wait` | Expedition / ExpeditionDeep | 49.5 s | earlier generation, repaired in place by U-T4-8 (trim + 4 s equal-power fold) | see U-T4-8 |

All four are instrumental, locally generated, and carry no third-party sample content.

## Generation settings (the two regenerated 2026-08-17)

Model stack, all local: `acestep_v1.5_turbo.safetensors` (UNet) · `ace_1.5_vae.safetensors` ·
`qwen_4b_ace15.safetensors` + `qwen_0.6b_ace15.safetensors` (text encoders).

| | `day-first-light` | `town-dusk` |
|---|---|---|
| seed | 7788 | 4242 |
| duration generated | 60 s | 60 s |
| steps | 24 | 24 |
| cfg / guidance | 2.5 / 3 | 2.5 / 3 |
| key / bpm | C major / 60 | A minor / 64 |
| `generate_audio_codes` | **false** | **false** |
| lyrics | *(empty)* | *(empty)* |

**Two settings did all the work, and they are the reason to write this down.** A first attempt
using the LLM audio-codes path with `[instrumental]` as the lyric produced a track whose median
100 ms window sat at **−64.8 dBFS** — silence — with sparse full-scale spikes: 1% of windows
within 20 dB of the loudest, and a 53.9 dB spread. That is not a quiet bed, it is an impulse
train, and it is exactly the `MostlySilent` + `IsolatedTransient` shape the content gates exist
to catch. Turning audio codes **off** and leaving lyrics **empty** moved the same prompt to a
median of −40.6 dBFS and a 22.2 dB spread. Anyone regenerating a bed should start there.

## Mastering recipe

Both beds were generated at 60 s and folded down to a shorter loop, then levelled:

1. **Equal-power head-to-tail fold.** The generated overhang past the target length is the
   material that naturally follows the last sample, so it is folded back onto the head:
   `out[i] = x[i]·√t + x[total+i]·√(1−t)` over the fold, `t = i/fold`. Equal-power (√), never
   linear — a linear crossfade dips about 3 dB through the middle of the fold, which writes a
   level lurch into the very seam `AudioContentGateTests.Gate.LoopSeamLevelLurch` measures.
   This makes the wrap continuous **by construction** rather than by inspection.
2. **Gain, then a true-peak limiter** — `alimiter=limit=<n>:level=disabled`. The
   `level=disabled` matters: ffmpeg's alimiter applies auto make-up gain by default, and with it
   on, an earlier pass came back at **+0.3 dBFS** with RMS 4 dB hot. Gain alone cannot reach the
   `MusicBed` band on material with a ~20 dB crest without clipping; the limiter is what buys the
   level.
3. **Encode** libvorbis `-q:a 5`, 48 kHz stereo.

| | fold | gain | limiter |
|---|---|---|---|
| `day-first-light` | 54 s target, 6 s fold | +23 dB | 0.84 |
| `town-dusk` | 50 s target, 10 s fold | +25 dB | 0.72 |

**Fold length is not cosmetic — it was chosen by measurement.** Sweeping trim/fold pairs on the
same source gave seam lurches of 3.98 dB (54/6), 6.90 (56/4), 8.69 (50/10) and 18.81 (48/12) for
Dawn; for Evening, 0.79 dB (50/10) against 13.40 (54/6). The fold is always continuous
mathematically; what varies is whether the music's own energy happens to match across the trim
point. Pick the pair that measures, do not assume one.

**Master to a margin, never to the ceiling.** `town-dusk` first came back at −0.99 dBTP against
a −1.0 dBTP ceiling — over by 0.01 dB — because the limiter was set at the target rather than
under it. The lossy encode's own overshoot is not predictable from the input level, so the
limiter ceiling has to leave room. Same lesson the narrator mastering pass learned the hard way.

## Verification

Every number above is the **engine gate's own** measurement, not a separate tool's — the beds are
decoded through the same `AudioStreamPlayback.MixAudio` path the game plays through.

Landing these two took `AudioContentGateTests.PendingContentExemptions` from **4 entries to 0**,
and `MixBudget.PendingExemptions` lost both bed rows. Both beds now sit in the
`MusicBed` band (−32 ±1.5 dBFS effective) with **`TrimDb: 0`** — no compensating trim at all,
where the old files needed −6.9 and −3.8.
