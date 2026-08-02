# Composed-track forensics — U6 (2026-08-02 shell-and-audio plan)

Measured, not guessed, per the plan's own discipline: integrated LUFS via ffmpeg `loudnorm`
(analysis pass only — no dynaudnorm applied to the shipped files), plus a windowed loudness pass
(`astats`, RMS per fixed-size window) for the shapes a single integrated number can't show — a
silent hole, or a boosted noise floor. ffmpeg binary used: the `imageio_ffmpeg` package's bundled
`ffmpeg-win-x86_64-v7.1.exe` (not on PATH on this machine; located under the user's Python
site-packages rather than installed system-wide, per the plan's own note that this would be
necessary). Reference target: -21.7 LUFS effective (the praised `night-still.mp3`'s own raw level).

## Integrated LUFS (ffmpeg loudnorm analysis pass)

| File | Raw LUFS | Code TrimDb (before) | Code TrimDb (after) | Effective (after) |
|---|---|---|---|---|
| `day-first-light.mp3` | -13.30 → **-13.32** (after edit, see below) | -8.4 | -8.4 (unchanged) | -21.7 |
| `town-dusk.mp3` | -13.77 | -8.0 | -8.0 (unchanged) | -21.8 |
| `night-still.mp3` (60s, praised) | -21.73 | n/a (not wired) | **0** (now wired — Camp) | -21.7 |
| `night-still-long.mp3` (185s) | -27.15 | **+5.45** | n/a (reverted out of the table) | n/a |
| `quest-wait.mp3` | -14.30 | -7.5 | -7.5 (unchanged) | -21.8 |

Only one row's *code* changed level: Camp moved from `night-still-long` (TrimDb +5.45) to
`night-still` (TrimDb 0). `day-first-light`'s TrimDb is unchanged — its raw LUFS barely moved from
the content edit (see below), because `loudnorm`'s own gating already discounted the near-silent
stretches that edit removed.

## night-still-long.mp3 — why the boost, not the file, was the bug

Windowed RMS (`astats`, 10s frames) over the full 185s:

```
t=0s   -63.5dB   t=50s  -63.7dB   t=100s -63.5dB   t=150s -63.7dB
t=10s  -63.7dB   t=60s  -63.9dB   t=110s -45.6dB   t=160s -63.6dB
t=20s  -63.7dB   t=70s  -63.8dB   t=120s -63.5dB   t=170s -63.7dB
t=30s  -63.8dB   t=80s  -63.6dB   t=130s -63.5dB   t=180s -66.3dB
t=40s  -63.8dB   t=90s  -55.8dB   t=140s -63.6dB
```

The file sits at a near-constant **-63 to -64dBFS windowed RMS for essentially the entire 185s**,
with two brief partial swells to -55.8dB (t=90s) and -45.6dB (t=110s) — this generation is
basically hiss riding under near-total silence, not a quiet-but-present ambient bed. The
`AudioDirector.MusicDb` + `TrimDb` chain applies a SINGLE linear gain to the whole file; boosting
by +5.45dB to reach -21.7 effective lifts that -63dB noise floor to roughly -58dB effective —
still "quiet" in isolation, but audible as a floor of hiss riding under a track whose actual
musical content is this sparse, which is what surfaced as "loud static randomly at night" during
the track's own quiet stretches (nearly all of it).

`night-still.mp3` (the 60s original, now reverted to) has a much healthier envelope — no
near-total-silence windows, consistent -21.8 to -42.9dB RMS across its 60s at raw level, matching
its own -21.73 raw LUFS without any boost needed. **Fix: Camp's `ComposedTracks` row reverted to
`night-still` / TrimDb 0** (`godot/scripts/audio/AudioDirector.cs`). The loop-length win
(60s → 185s) trades back until U9 lands a clean ≥180s regeneration at raw ≈ -21.7 with no boost
needed — disclosed to the owner (Open Question 4), not hidden.

## day-first-light.mp3 — "all I hear is the bass, then I think the track ends"

Windowed RMS (`astats`, 1s frames) over the original 150.02s file found the track is consistently
loud (-12 to -17dB RMS almost throughout — matching its -13.3 raw LUFS, and a narrow measured LRA
of 4.0 LU, i.e. NOT dynamically bass-then-quiet across normal listening) **except for two genuine
near-total-silence dropouts**:

- **t=40s to t=46s** (~6s): RMS falls from -17.4dB (t=39s) to -63.5/-65.7/-65.2/-64.8dB (t=40-44s)
  before climbing back through -32.1dB (t=46s) to -16.3dB (t=47s) and settling back to the normal
  -13 to -15dB range by t=48-50s.
- **t=143s to t=150s** (tail, ~7s): RMS falls from -14.8dB (t=141s) through -19.4dB (t=142s) to
  -65.8/-64.9/-64.9/-64.9/-64.7/-65.1dB (t=143-149s), running to the file's own end at 150.02s.

Both are abrupt (a ~1-2s decay into full digital silence, not a graceful musical fade) — the same
"the generation produces near-silent stretches" failure mode #340's own attempt log already
recorded for the night brief, evidently not unique to that brief. At normal Morning-phase dwell
times (anywhere over ~40 real seconds — completely ordinary for reading recipes/setting up crafts)
a player reaches the FIRST hole and hears the track cut to near-total silence for 6 seconds before
partially recovering — this is the mechanism behind "all I hear is the bass, then I think the
track ends," reached far sooner and far more often than the tail hole.

**Fix (re-encode, not a regeneration — no GPU):** both holes removed with ffmpeg. The mid-track
hole was closed with a 1.0s equal-power crossfade splice (`atrim` + `acrossfade`) joining the last
second of the [0, 39.0s) segment to the first second of the [48.0, 144.0s) segment — both sides of
the splice are solidly musical material (-14 to -15dB RMS) at nearly identical level, so the join
reads as a normal phrase transition, not a cut (verified: no new clipping — true peak unchanged at
+0.03dB; RMS either side of the splice point stays in the normal -13 to -17dB band, no new dip).
The tail hole is dropped entirely by ending the kept segment at 144.0s (safely past the point the
original decay had already reached silence, so the new file's own end has no audible truncation
click). Net: `day-first-light.mp3` is now **134.04s** (was 150.02s), raw LUFS **-13.32** (was
-13.30 — the removed material was gated out of `loudnorm`'s own measurement already, hence the
near-zero shift), no remaining near-total-silence window anywhere in the file (full 1s-window scan
re-run post-edit; the only sub-audible windows are the intentional intro fade-in, t=0-4s, and one
natural decay-tail window at the very end, both legitimate content, not defects).

`day-first-light`'s TrimDb is unchanged (-8.4dB, still lands at -21.7 effective) — the edit didn't
change the level, only removed dead air.

## Underground bed ("vigil") noise-share — U7

Not an ffmpeg measurement (the Underground bed is synthesized PCM, not an mp3 asset) — measured
with a throwaway console harness mirroring `MusicBed.Underground`'s exact drone/air/drip/pulse
construction (the same method `MusicBed.LoopSeconds`'s own doc cites), computing the cavern-air
noise layer's RMS as a share of the full theme's RMS:

| | Amplitude | Low-pass cutoff | Air-layer RMS | Full-theme RMS | Noise share |
|---|---|---|---|---|---|
| Shipped | 0.13 | 340Hz | 0.01015 | 0.14189 | **7.16%** |
| U7 (this unit) | 0.07 | 260Hz | 0.00481 | 0.14440 | **3.33%** |

Roughly halved. Pinned in `godot/tests/AudioTests.cs` as a ceiling (5.0%) on
`MusicBed.UndergroundAirLayer()`'s live RMS share of `MusicBed.Underground()`'s own RMS — a
relative measurement (against the theme's own total, not an absolute amplitude), reacting
correctly if either constant drifts back up. Drips, pulse, and the four drone partials are
untouched — the owner said "good but," not "replace."

Separately (not a level issue, already fixed by U1): `AudioDirector.SetScene("depths")` now routes
through the same `LogBedSwap` every other bed swap uses, so the vigil bed swap appears in the
session log as `MUSIC: synth bed for <phase>` is replaced by the Underground theme — previously
the one music change the owner's own session log couldn't see.

## Forge cues (bellows hold + strike/quench) — U8

The held-bellows complaint ("particularly the bellows shift since you have to hold") was a SHAPE
problem, not just a level one: the bellows fired a single 0.3s one-shot per grip
(`Cue.Bellows`, normalized to 0.30 — double the venue-cue level of 0.15 every building-entrance cue
already settled on), so a multi-second hold played one breath, then silence for the rest of the
grip. Fix: `AudioDirector` gained a dedicated `StartLoop`/`StopLoop` API on one extra voice,
manually retriggering the (already seam-safe — its envelope starts and ends at exactly zero)
Bellows clip for as long as the hold lasts, with a 120ms fade on release so letting go never
clicks. `ForgePanel` drives it off `ForgeMinigame.IsPumping` the same continuous-poll way the
furnace-glow VFX already reads that gauge — no change needed to `ForgeMinigame` itself. The
discrete drag-pump stroke (`PumpStroke`) keeps firing the same cue as a one-shot via `Play()`
(never the loop voice), so drag-pumping stays tactile.

Level trims (`Synth.Normalise` peak target, and the actual measured peak/dBFS each target
produces — measured with a throwaway harness mirroring each cue's exact recipe, same method as
the Underground noise-share numbers above):

| Cue | Target before → after | Measured peak before → after | dBFS before → after |
|---|---|---|---|
| `Cue.Bellows` | 0.30 → **0.15** | 0.291 → 0.149 | -10.7 → **-16.5** |
| `Cue.HammerOnBeat` | 0.32 → **0.22** | 0.310 → 0.217 | -10.2 → **-13.3** |
| `Cue.HammerOffBeat` | 0.24 → **0.16** | 0.236 → 0.159 | -12.6 → **-16.0** |
| `Cue.Quench` | 0.35 → **0.26** | 0.336 → 0.254 | -9.5 → **-11.9** |

For reference, `Cue.EnterForge` (a venue-entrance cue, "ambient, must not be harsh") measures
0.149 peak — Bellows now lands at exactly that same level. `Cue.EnterMarket`, the loudest of the
five venue cues, measures 0.217 peak; the automated regression pin
(`AudioTests.Bellows_IsNoLouderThanAVenueCue`) checks Bellows against the loudest of the five, not
just EnterForge, for headroom.

On/off-beat contrast preserved (0.22:0.16 ≈ 1.38×, was 0.32:0.24 ≈ 1.33×), so the tempo-feedback
tests (`AnOnBeatHammerBlow_SoundsBrighterAndLonger_ThanAMistimedOne`,
`HammerAndQuenchCues_RiseSlowerThanAnInstantAttackCue`) — both relative/structural, immune to a
uniform amplitude scale — stay meaningful and green.

## Acceptance numbers for U9 (if/when it runs)

- `night-still` replacement: raw LUFS ≈ -21.7, no positive TrimDb, ≥180s, no near-total-silence
  window longer than the intro/outro fades already present in a healthy composed track.
- `day-first-light` v2 (optional): raw LUFS ≈ -13 to -14 (matches the current file's own
  already-acceptable level), and — the acceptance bar this forensics pass adds — no windowed RMS
  dropout to within 40dB of the track's own noise floor for longer than ~1s anywhere in the file.
