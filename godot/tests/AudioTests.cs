#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GameSim.Contracts;
using GameSim.Presentation;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using GodotClient.Tools;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The audio layer — synthesized SFX and the phase-keyed music bed.
///
/// <para><b>What these tests are careful about.</b> "The sound played" is the easiest possible check to
/// fake: <c>AudioStreamPlayer.Playing</c> is true for a buffer of pure silence, and Godot's headless
/// dummy driver accepts <c>Play()</c> on anything. So every assertion here decodes the actual PCM and
/// measures it. A cue that generates 4000 zero samples would satisfy any reasonable-looking API test
/// and be completely inaudible in the game — which is the same shape as the seam bugs that have already
/// shipped past this suite twice.</para>
///
/// <para>Runtime-gated only because <see cref="AudioStreamWav"/> is a Godot type; nothing here needs a
/// frame, so there is no SubViewport/frame-pumping hazard.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AudioTests
{
    private static readonly Cue[] AllCues = Enum.GetValues<Cue>();

    private static readonly DayPhase[] AllPhases =
    {
        DayPhase.Morning, DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening,
    };

    /// <summary>Decodes 16-bit mono PCM back to floats in [-1, 1].</summary>
    private static float[] Pcm(AudioStreamWav wav)
    {
        var data = wav.Data;
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var s16 = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            samples[i] = s16 / (float)short.MaxValue;
        }

        return samples;
    }

    /// <summary>
    /// The bellows is a breath, not a hiss — pinned as a NUMBER because it has now been reported twice.
    ///
    /// <para>U8 answered "the bellows shift since you have to hold" by halving the cue's amplitude
    /// (0.30 to 0.15). On 2026-08-14 the owner reported it again: "too loud and abrasive". The second
    /// word is the one that mattered — abrasive is timbre, and no amount of level cutting fixes
    /// timbre. The cue was hash noise behind a SINGLE-pole filter at 700Hz, which rolls off just
    /// 6dB/octave, so at 2.8kHz the noise was still only ~12dB down. That is the hiss band.</para>
    ///
    /// <para>Level alone cannot be the assertion, because level alone is exactly the fix that already
    /// failed once. This measures the cue's HIGH-BAND ENERGY SHARE, so a future session that quietly
    /// restores a brighter filter — or swaps the cascade back to one pole — goes red here instead of
    /// arriving as a third identical complaint from the owner.</para>
    /// </summary>
    [TestCase]
    public void TheBellows_ReadsAsBreath_NotHiss()
    {
        var pcm = Pcm(SfxLibrary.Get(Cue.Bellows));

        var low = (float[])pcm.Clone();
        for (var pole = 0; pole < BassFilterPoles; pole++)
        {
            Synth.LowPass(low, 900f);
        }

        var full = Rms(pcm);

        // Vacuous-green guard: a silent cue would trivially "pass" any brightness ceiling by having no
        // energy anywhere. Silence is a different bug, and it must not read as this one being fixed.
        AssertThat(full > 0.001f)
            .OverrideFailureMessage($"The bellows cue is effectively silent (RMS {full:0.#####}).")
            .IsTrue();

        var highShare = 1f - (Rms(low) / full);

        // Ceiling calibrated against the two-pole 320Hz cue this repo actually ships, with headroom
        // for float drift across OSes — not a round number picked to be comfortably true.
        AssertThat(highShare < 0.45f)
            .OverrideFailureMessage(
                $"The bellows is {highShare:P0} high-band energy. Above ~45% it stops reading as moving "
                + "air and starts reading as hiss — the owner's word was \"abrasive\", and that is this "
                + "number, not the gain. Check the LowPass cascade in SfxLibrary before touching level.")
            .IsTrue();
    }

    private static float Rms(float[] s) => s.Length == 0 ? 0f : MathF.Sqrt(s.Sum(v => v * v) / s.Length);

    private static float Peak(float[] s) => s.Length == 0 ? 0f : s.Max(MathF.Abs);

    /// <summary>
    /// A well-timed hammer blow must SOUND better than a mistimed one.
    ///
    /// <para>The forge's tempo bonus is worth 2.2x and is the only skill the minigame teaches — and it had no
    /// audible signal at all: <c>ForgePanel</c> played one local sine for every strike regardless of timing, so
    /// a player had to watch the gauge to learn rhythm instead of hearing it. Brian could not get the forge to
    /// work and was judging it with that feedback missing.</para>
    ///
    /// <para>Asserted on measured brightness and length rather than "the buffers differ" — two cues can differ
    /// byte-for-byte and still be indistinguishable to an ear, which is exactly the kind of check that passes
    /// while the game feels dead. <c>EveryCue_SoundsDifferentFromEveryOther</c> already owns mere difference.</para>
    /// </summary>
    [TestCase]
    public void AnOnBeatHammerBlow_SoundsBrighterAndLonger_ThanAMistimedOne()
    {
        var onBeat = Pcm(SfxLibrary.Get(Cue.HammerOnBeat));
        var offBeat = Pcm(SfxLibrary.Get(Cue.HammerOffBeat));

        // Share of energy ABOVE the low band. A ringing anvil keeps its upper partials; a dull mistimed thud
        // does not. Reuses the same pole count the bass test justifies — one pole is far too gentle to
        // separate bands and would report a confident wrong number.
        float HighShare(float[] pcm)
        {
            var low = (float[])pcm.Clone();
            for (var pole = 0; pole < BassFilterPoles; pole++)
            {
                Synth.LowPass(low, 900f);
            }

            var full = Rms(pcm);
            return full <= 0f ? 0f : 1f - (Rms(low) / full);
        }

        var brightOn = HighShare(onBeat);
        var brightOff = HighShare(offBeat);

        AssertThat(brightOn > brightOff)
            .OverrideFailureMessage(
                $"An on-beat strike is {brightOn:P0} high-band energy and a mistimed one {brightOff:P0}. The " +
                "good hit has to ring brighter, or the player cannot hear the difference between playing well " +
                "and playing badly.")
            .IsTrue();

        AssertThat(onBeat.Length > offBeat.Length)
            .OverrideFailureMessage(
                $"An on-beat strike lasts {onBeat.Length} samples and a mistimed one {offBeat.Length}. The good " +
                "hit should ring on; the bad one should die.")
            .IsTrue();
    }

    [TestCase]
    public void EveryCue_IsActuallyAudible_NotSilence()
    {
        foreach (var cue in AllCues)
        {
            var wav = SfxLibrary.Get(cue);
            var pcm = Pcm(wav);

            AssertThat(pcm.Length)
                .OverrideFailureMessage($"{cue} produced an empty buffer.")
                .IsGreater(100);

            // RMS, not peak: a single non-zero sample in a field of zeros would pass a peak check and
            // be silent to a human. This is the assertion that makes "the sound exists" mean something.
            AssertThat(Rms(pcm))
                .OverrideFailureMessage(
                    $"{cue} is effectively silent (RMS {Rms(pcm):0.####}, peak {Peak(pcm):0.####}). It " +
                    "would 'play' fine and nobody would hear it.")
                .IsGreater(0.01f);

            AssertThat(Peak(pcm))
                .OverrideFailureMessage($"{cue} clips (peak {Peak(pcm):0.###}).")
                .IsLessEqual(1.0f);

            AssertThat(wav.MixRate).IsEqual(Synth.SampleRate);
            AssertThat(wav.Stereo).IsFalse();
        }
    }

    /// <summary>
    /// Guards a switch fallthrough: if a new <see cref="Cue"/> is added and nobody writes a recipe for
    /// it, <see cref="SfxLibrary"/>'s default arm hands back a generic beep — and every call site would
    /// still "work" while the game quietly lost a sound. Comparing buffers catches that.
    /// </summary>
    [TestCase]
    public void EveryCue_SoundsDifferentFromEveryOther()
    {
        var fingerprints = new Dictionary<string, Cue>();
        foreach (var cue in AllCues)
        {
            var pcm = Pcm(SfxLibrary.Get(cue));
            var key = $"{pcm.Length}:{Rms(pcm):0.00000}";

            AssertThat(fingerprints.ContainsKey(key))
                .OverrideFailureMessage(
                    $"{cue} is byte-comparable to {(fingerprints.TryGetValue(key, out var other) ? other.ToString() : "another cue")} " +
                    "— it probably fell through to SfxLibrary's default arm and is a placeholder beep.")
                .IsFalse();

            fingerprints[key] = cue;
        }
    }

    [TestCase]
    public void EveryPhase_HasAnAudibleMusicBed_MarkedAsLooping()
    {
        foreach (var phase in AllPhases)
        {
            var wav = MusicBed.For(phase);
            var pcm = Pcm(wav);

            AssertThat(Rms(pcm))
                .OverrideFailureMessage($"The {phase} music bed is effectively silent (RMS {Rms(pcm):0.####}).")
                .IsGreater(0.01f);

            AssertThat(wav.LoopMode)
                .OverrideFailureMessage($"The {phase} bed is not marked as looping, so the music stops after one pass.")
                .IsEqual(AudioStreamWav.LoopModeEnum.Forward);

            AssertThat(wav.LoopEnd)
                .OverrideFailureMessage($"The {phase} bed's LoopEnd is not the end of the buffer.")
                .IsEqual(pcm.Length);
        }
    }

    /// <summary>
    /// The seamlessness claim, verified rather than asserted in a comment. <c>MusicBed.SnapToLoop</c>
    /// exists so every drone completes a whole number of cycles across the loop; if that math is wrong
    /// the waveform jumps at the loop point and the player hears a click every 24 seconds forever —
    /// the kind of defect that is obvious in play and invisible to a "does it generate" test.
    /// </summary>
    [TestCase]
    public void TheMusicLoop_JoinsItselfWithoutAStep()
    {
        foreach (var phase in AllPhases)
        {
            var pcm = Pcm(MusicBed.For(phase));
            var step = MathF.Abs(pcm[^1] - pcm[0]);

            // Compared against the bed's own peak, so this stays meaningful if levels are retuned.
            var allowed = Peak(pcm) * 0.25f;

            AssertThat(step)
                .OverrideFailureMessage(
                    $"The {phase} loop steps by {step:0.####} between its last and first sample " +
                    $"(peak {Peak(pcm):0.###}, allowed {allowed:0.####}). That discontinuity is an " +
                    "audible click on every repeat — check MusicBed.SnapToLoop.")
                .IsLessEqual(allowed);
        }
    }

    /// <summary>The Mine's theme must be audible, loop cleanly, and NOT be one of the town beds — the
    /// whole point is that down there sounds like a different place.</summary>
    [TestCase]
    public void TheUndergroundTheme_IsItsOwnPlace()
    {
        var pcm = Pcm(MusicBed.Underground());

        AssertThat(Rms(pcm))
            .OverrideFailureMessage($"The Mine theme is effectively silent (RMS {Rms(pcm):0.####}).")
            .IsGreater(0.01f);

        AssertThat(MusicBed.Underground().LoopMode).IsEqual(AudioStreamWav.LoopModeEnum.Forward);

        var step = MathF.Abs(pcm[^1] - pcm[0]);
        AssertThat(step)
            .OverrideFailureMessage($"The Mine loop steps by {step:0.####} at its seam — an audible click every repeat.")
            .IsLessEqual(Peak(pcm) * 0.25f);

        foreach (var phase in AllPhases)
        {
            var town = Pcm(MusicBed.For(phase));
            AssertThat(MathF.Abs(Rms(pcm) - Rms(town)) > 0.0001f)
                .OverrideFailureMessage($"The Mine theme is indistinguishable from the {phase} bed.")
                .IsTrue();
        }

        // U7 (R7): "vigil music good but too much background static" — the cavern-air noise layer IS
        // static on purpose, so this pins a SHARE ceiling (relative to the theme's own RMS), not an
        // absolute amplitude — a master-level change to Underground() stays free to move without
        // breaking this. MusicBed.UndergroundAirLayer() is the SAME production buffer summed into
        // Underground() above, measured live rather than a frozen recipe copy, so this reacts if the
        // amplitude/cutoff ever drifts back up. Measured (throwaway console harness): shipped
        // (0.13/340Hz) ~7.2%, U7's tuned (0.07/260Hz) ~3.3% — the ceiling below sits well above the
        // tuned value and well under the shipped one.
        var airRms = Rms(MusicBed.UndergroundAirLayer());
        var noiseShare = airRms / Rms(pcm);
        AssertThat(noiseShare)
            .OverrideFailureMessage(
                $"The Underground theme's cavern-air layer is {noiseShare:P1} of the theme's own RMS " +
                $"(ceiling {MaxUndergroundNoiseShare:P1}). That is the background static the owner " +
                "asked to have cut — move UndergroundAirLayer's amplitude/cutoff back down, not the " +
                "drips/pulse/drones (\"good but,\" not \"replace\").")
            .IsLessEqual(MaxUndergroundNoiseShare);
    }

    /// <summary>U7 ceiling for <see cref="TheUndergroundTheme_IsItsOwnPlace"/>'s noise-share check —
    /// see that assertion's own doc for the measured before/after numbers.</summary>
    private const float MaxUndergroundNoiseShare = 0.05f;

    /// <summary>
    /// "Lower the base" as a measurement rather than a memory.
    ///
    /// <para>The owner's playtest note was "its a little loud and loops too quickly, lower the base". The
    /// levels that produced that are just numeric literals in <see cref="MusicBed"/>, so the next person to
    /// nudge a drone amplitude has nothing to tell them they have undone it. This measures the share of the
    /// bed's energy living below <see cref="BassCutoffHz"/> and pins it.</para>
    ///
    /// <para>Deliberately a RATIO, not an absolute: a ratio survives a master-level change, which is a
    /// separate knob (<c>AudioDirector.MusicDb</c>) that should be free to move without breaking this.</para>
    /// </summary>
    [TestCase]
    public void TheMusicBeds_AreNotBassHeavy()
    {
        foreach (var (name, wav) in AllPhases
                     .Select(p => (p.ToString(), MusicBed.For(p)))
                     .Append(("Underground", MusicBed.Underground())))
        {
            var full = Pcm(wav);
            var low = (float[])full.Clone();

            // Cascade the filter. Synth.LowPass is one-pole (-6dB/octave), which at 150Hz leaves a 175Hz
            // fifth only ~1.4dB down — so a single pass "measuring the bass" was really measuring the whole
            // chord and reported 66% for a bed with almost nothing under its root. Four poles is
            // -24dB/octave, which actually separates the fundamental from the partials above it. A weak
            // instrument reading the wrong number confidently is the same failure as a seam test passing.
            for (var pole = 0; pole < BassFilterPoles; pole++)
            {
                Synth.LowPass(low, BassCutoffHz);
            }

            var share = Rms(full) <= 0f ? 0f : Rms(low) / Rms(full);

            // The Mine gets a higher allowance ON PURPOSE. It is pitched a fourth below the deepest town
            // bed specifically so the Depths sound like somewhere else, so holding it to the town's limit
            // would be asking it to stop being the low place. Encoding the intent as a separate number is
            // honest; quietly raising the shared limit to make one bed pass would not be.
            var limit = name == "Underground" ? MaxUndergroundBassShare : MaxBassShare;

            AssertThat(share)
                .OverrideFailureMessage(
                    $"The {name} bed puts {share:P0} of its energy below {BassCutoffHz}Hz (limit " +
                    $"{limit:P0}). That is the boom the owner asked to have taken out: \"lower the " +
                    "base\". Fix by moving drone amplitude UP the harmonic series (root -> octave) rather " +
                    "than by turning the whole bed down — the ear infers the fundamental from the upper " +
                    "partials, so the chord survives and only the weight goes.")
                .IsLessEqual(limit);
        }
    }

    /// <summary>
    /// U-audio-2 postmortem: this test's onset detector used a hardcoded ABSOLUTE PCM delta (0.06) as
    /// "a pluck attack is a fast rise." That number was never actually testing note DENSITY — it was
    /// testing "is this bed loud enough that its attacks exceed a fixed value," a loudness assertion
    /// wearing a density assertion's name, and it only ever passed because it happened to sit just under
    /// the attack size at the bed's old peak (0.5): 13 onsets measured there, barely clearing the &gt;12
    /// floor below.
    ///
    /// <para>The moment this same PR dropped the bed peak 0.5 -> 0.42 alongside the drone rebalance
    /// (<see cref="MusicBed.Build"/>) to answer the owner's separate "too loud" complaint, the identical
    /// melody — MORE notes than before (130 vs 69 slots filled, because <see cref="MusicBed.MelodySlots"/>
    /// correctly scaled with the longer loop) — produced onset deltas that fell under 0.06, and the count
    /// silently went to zero. Verified with a throwaway console harness reproducing the exact shipped
    /// synthesis math: isolating the loop-length change alone (new 120s, OLD peak/weights) still found 26
    /// onsets; isolating the peak/weight change alone (OLD 60s loop, NEW peak/weights) already found ZERO.
    /// That is proof, not a guess, that the level change broke the detector and the melody itself was
    /// never the problem — the density fix from the PR before this one was, and remains, correct.</para>
    /// </summary>
    private const float OnsetThresholdFraction = 0.12f;

    /// <summary>
    /// The loop must be long enough that the ear does not memorise it.
    ///
    /// <para>"loops too quickly" was a real defect at 24 seconds: short enough to learn, after which the
    /// seam stops being seamless and starts being an event. Pinned as a floor rather than an exact value so
    /// the length stays free to grow.</para>
    ///
    /// <para>Also checks that the melody grid scaled WITH the loop. Lengthening <c>LoopSeconds</c> while
    /// leaving a fixed slot count would have produced the same number of notes spread thinner — a sparser
    /// bed rather than a longer one, which is not what was asked for and would be easy to ship by accident.
    /// The onset threshold is a <see cref="OnsetThresholdFraction"/> of THIS buffer's own peak rather than
    /// an absolute PCM value — see that constant's doc for why an absolute number silently turned this
    /// into a loudness test and broke on the very next legitimate level change. 0.12 was chosen by scanning
    /// fractions against both the pre- and post-retune mixes: 0.14 and above under-detect even the mix this
    /// test used to pass against (8 onsets on the new mix, 0 on the old one at 0.16); 0.08-0.10 over-detect
    /// by an order of magnitude (250-700+ "onsets," which is harmonic-beat ripple inside a single note's
    /// decay getting counted repeatedly, not one crossing per note) — 0.12 sits in the flat, well-behaved
    /// part of that curve for both mixes.</para>
    /// </summary>
    [TestCase]
    public void TheMusicLoop_IsLongEnoughNotToBeMemorised()
    {
        AssertThat(MusicBed.LoopSeconds)
            .OverrideFailureMessage(
                $"The music loop is {MusicBed.LoopSeconds}s. Below {MinLoopSeconds}s the player learns the " +
                "figure and then hears the repeat as an event — the owner's \"loops too quickly\".")
            .IsGreaterEqual(MinLoopSeconds);

        // Note density held constant: count actual note onsets rather than trusting the slot arithmetic.
        var pcm = Pcm(MusicBed.For(DayPhase.Morning));
        var threshold = Peak(pcm) * OnsetThresholdFraction;
        var onsets = 0;
        for (var i = 1; i < pcm.Length; i++)
        {
            // A pluck attack is a fast rise; the drone underneath changes far more slowly than this.
            if (MathF.Abs(pcm[i]) - MathF.Abs(pcm[i - 1]) > threshold)
            {
                onsets++;
                i += Synth.SampleRate / 20; // debounce one attack, don't count its ripple
            }
        }

        AssertThat(onsets)
            .OverrideFailureMessage(
                $"Only {onsets} note attacks across {MusicBed.LoopSeconds}s (peak {Peak(pcm):0.####}, " +
                $"onset threshold {threshold:0.####}). The melody grid did not scale with the loop length, " +
                "so the track got longer by getting emptier rather than by having more music in it.")
            .IsGreater(12);
    }

    /// <summary>Where "bass" starts, for <see cref="TheMusicBeds_AreNotBassHeavy"/>. Above the beds' roots
    /// (98-131Hz) so the fundamental itself counts as bass, which is the point.</summary>
    private const float BassCutoffHz = 150f;

    /// <summary>Poles in the measurement filter. One is far too gentle to separate a fundamental from the
    /// fifth above it — see the comment at the cascade.</summary>
    private const int BassFilterPoles = 4;

    /// <summary>
    /// Ceiling on the low-band energy share. Tightened 0.45 -> 0.35 in U-audio-2 alongside the second
    /// "too loud" / bass-heavy report: the FIRST fix (0.34 -> 0.17 root amplitude) left a ceiling loose
    /// enough (0.45) that it could not have caught its own regression back toward the old numbers — the
    /// measured post-fix shares (14.8%-29.3% across the five phases, see the PR body) all clear 0.35 with
    /// real headroom, so this is a floor pulled up to where the fix actually landed, not a number picked
    /// to make the test pass.
    /// </summary>
    private const float MaxBassShare = 0.35f;

    /// <summary>The Mine's allowance. Higher because it is deliberately the lowest-pitched thing in the
    /// game (a fourth under the deepest town bed) — see the note at the comparison. Still a ceiling: it
    /// caught the original 0.40-amplitude D2 fundamental and the ~37Hz sub-bass pulse under it. Left
    /// untouched in U-audio-2 — the owner's repeat complaint was about the town beds he actually hears
    /// every session, and Underground was never part of that report.</summary>
    private const float MaxUndergroundBassShare = 0.55f;

    /// <summary>
    /// Floor on loop length. 24s was the original reported defect, 45s was the floor when the fix landed
    /// at 60s. The owner said "loops too quickly" again on the very next playtest with 60s shipped, so a
    /// floor that 60s already cleared was not actually testing for "long enough" — it was testing for
    /// "longer than the number everyone already agreed was too short." Raised to 90s alongside the fix
    /// landing at 120s: comfortably below the new number (room to tune down slightly) but well above the
    /// 60s that was already proven, twice, not to be enough.
    /// </summary>
    private const float MinLoopSeconds = 90f;

    /// <summary>A scene takes the music and gives it back. The trap: <c>SetPhase</c> ignores an unchanged
    /// phase, so if closing a scene did not explicitly restore the bed the game would fall silent until
    /// the day happened to move on.
    ///
    /// <para><b>U4 fix (playtest-three plan): "the day's bed" is no longer always the synth bed.</b>
    /// This test used to compare the restored stream directly against <c>MusicBed.For(DayPhase.Morning)</c>
    /// — correct back when Morning had no composed entry, since <c>ResolveBed</c>'s ladder had nowhere
    /// else to land. U4 gave Morning a composed track (<c>day-first-light</c>), so Morning's bed is now
    /// that file, not the synth one, and the hardcoded comparison went red — not because
    /// <c>SetScene(null)</c> stopped restoring the bed, but because the TEST'S OWN assumption about what
    /// "the bed" resolves to for Morning was stale. Fixed by asking the same composed-first question
    /// production asks, via <c>AudioDirector.LoadComposedTrackForCensus</c> — the same public loader
    /// <c>ResolveBed</c> itself calls (see that method's own doc) — falling back to the synth bed only if
    /// no composed entry exists, exactly mirroring <c>ResolveBed</c>'s own ladder. This keeps the
    /// invariant this test actually exists for (leaving a scene must restore SOME correct bed, not
    /// silence or the leftover Mine theme) intact and phase-mapping-agnostic, rather than re-hardcoding a
    /// second assumption that the next composed-track remap would just break again.</para>
    /// </summary>
    [TestCase]
    public void AScene_TakesTheMusic_AndGivesItBack()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            director.SetPhase(DayPhase.Morning);

            director.SetScene("depths");
            var underground = director.GetChildren().OfType<AudioStreamPlayer>()
                .Any(p => p.Stream == MusicBed.Underground());
            AssertThat(underground)
                .OverrideFailureMessage("Opening the Depths did not switch the music to the Mine theme.")
                .IsTrue();

            director.SetScene(null);

            // Whatever Morning ACTUALLY resolves to today — composed if a track is mapped (true since
            // U4), else the synth bed — never a hardcoded assumption about which one that is.
            var expectedBed = AudioDirector.LoadComposedTrackForCensus(DayPhase.Morning)
                ?? (AudioStream)MusicBed.For(DayPhase.Morning);
            var backToTown = director.GetChildren().OfType<AudioStreamPlayer>()
                .Any(p => p.Stream == expectedBed);
            AssertThat(backToTown)
                .OverrideFailureMessage(
                    "Closing the Depths left the Mine theme playing (or nothing) instead of Morning's " +
                    "actual bed. Leaving a scene has to restore the day's bed explicitly — SetPhase " +
                    "early-returns on an unchanged phase.")
                .IsTrue();
        }
        finally
        {
            director.Free();
        }
    }

    [TestCase]
    public void PhasesSoundDifferentFromEachOther()
    {
        var seen = new Dictionary<string, DayPhase>();
        foreach (var phase in AllPhases)
        {
            var pcm = Pcm(MusicBed.For(phase));
            var key = $"{Rms(pcm):0.000000}";

            AssertThat(seen.ContainsKey(key))
                .OverrideFailureMessage(
                    $"The {phase} bed is indistinguishable from " +
                    $"{(seen.TryGetValue(key, out var o) ? o.ToString() : "another phase")} — the time of day " +
                    "is not actually audible.")
                .IsFalse();

            seen[key] = phase;
        }
    }

    /// <summary>Determinism (KTD5 in spirit): the synthesis reads no RNG and no clock, so audio is
    /// byte-identical across builds and machines.</summary>
    [TestCase]
    public void Synthesis_IsDeterministic()
    {
        for (var i = 0; i < 64; i++)
        {
            AssertThat(Synth.Noise(i, seed: 5)).IsEqual(Synth.Noise(i, seed: 5));
        }

        AssertThat(Synth.Noise(10)).IsNotEqual(Synth.Noise(11));
        AssertThat(Synth.Noise(10, seed: 1)).IsNotEqual(Synth.Noise(10, seed: 2));
    }

    [TestCase]
    public void Director_PlaysACueOnAPooledVoice_AndStartsTheBed()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);

            director.Play(Cue.Coin);
            var voices = director.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name.ToString().StartsWith("Voice")).ToList();

            AssertThat(voices.Count).IsEqual(6);
            AssertThat(voices.Any(v => v.Stream is not null))
                .OverrideFailureMessage("Play() left every pooled voice without a stream.")
                .IsTrue();

            director.SetPhase(DayPhase.Evening);
            var music = director.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name.ToString().StartsWith("Music")).ToList();

            AssertThat(music.Any(m => m.Stream is not null))
                .OverrideFailureMessage("SetPhase() never assigned a music stream.")
                .IsTrue();
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>Muting must actually stop sound, and unmuting must bring the bed back — the trap being
    /// that <c>SetPhase</c> early-returns on an unchanged phase, so a naive unmute leaves the music off
    /// until the day happens to move on.</summary>
    [TestCase]
    public void Muting_SilencesEverything_AndUnmutingRestoresTheBed()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            director.SetPhase(DayPhase.Morning);

            director.SetMuted(true);
            AssertThat(director.Muted).IsTrue();
            AssertThat(director.GetChildren().OfType<AudioStreamPlayer>().Any(p => p.Playing))
                .OverrideFailureMessage("Something is still playing after mute.")
                .IsFalse();

            director.SetMuted(false);
            AssertThat(director.GetChildren().OfType<AudioStreamPlayer>()
                    .Where(p => p.Name.ToString().StartsWith("Music"))
                    .Any(p => p.Stream is not null))
                .OverrideFailureMessage(
                    "Unmuting left the music bed off. SetPhase ignores an unchanged phase, so unmute has " +
                    "to clear the remembered phase before re-arming it.")
                .IsTrue();
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// U2 (make-it-visible plan), KTD-B applied to audio: the census rule that already exists for art
    /// (every id the shipped game references must resolve to real content), now covering
    /// <see cref="AudioDirector.ComposedTrackIds"/>. Three composed tracks sat on disk and were never
    /// wired into the game for days before this unit — this is the test that turns any FUTURE repeat
    /// of exactly that ("committed but never referenced") red at PR time instead of silent for days.
    ///
    /// <para>U4 (playtest-three plan) closed the one deliberate gap this table used to state on
    /// purpose (Morning had no composed entry) — floor raised 3 -&gt; 5 to match the five DayPhase
    /// keys the table now carries (Morning, Evening, Camp, Expedition, ExpeditionDeep), so an
    /// accidental drop back toward the old gap is caught here too, not just a full empty-table
    /// regression.</para>
    /// </summary>
    [TestCase]
    public void EveryComposedTrack_LoadsAndLoops()
    {
        var ids = AudioDirector.ComposedTrackIds;

        AssertThat(ids.Count)
            .OverrideFailureMessage(
                "AudioDirector.ComposedTrackIds has fewer than 5 entries. If a composed track was " +
                "deliberately reverted this floor should move with it — but an accidentally emptied " +
                "table (or a silently dropped phase) is exactly the regression this census exists to catch.")
            .IsGreaterEqual(5);

        foreach (var (phase, id) in ids)
        {
            var stream = AudioDirector.LoadComposedTrackForCensus(phase);

            AssertThat(stream)
                .OverrideFailureMessage(
                    $"'{id}' is mapped to {phase} but did not load. Either the file under " +
                    "godot/assets/audio/ is missing or misnamed, or its Git LFS content was never " +
                    "pulled — either way this IS the 'on disk but not in the game' defect, now caught " +
                    "here instead of days later.")
                .IsNotNull();

            var mp3 = stream as AudioStreamMP3;
            AssertThat(mp3)
                .OverrideFailureMessage($"'{id}' loaded as {stream?.GetType().Name}, not an AudioStreamMP3.")
                .IsNotNull();

            AssertThat(mp3!.Loop)
                .OverrideFailureMessage(
                    $"'{id}' ({phase}) is not marked to loop — the track would play once and the bed " +
                    "would fall silent instead of continuing, from either the .import `loop=true` " +
                    "param or the belt-and-suspenders set in AudioDirector.LoadComposed.")
                .IsTrue();
        }
    }

    /// <summary>
    /// U6 (2026-08-02 shell-and-audio plan, R7/KTD-F): "no composed track ships with a positive
    /// TrimDb again" — a boost is the code admitting a generation's own noise floor is wrong, and
    /// boosting it lifts that noise floor right along with the sparse content. This is exactly how
    /// <c>night-still-long</c>'s +5.45dB shipped and the owner heard "loud static randomly at night."
    /// Reads <see cref="AudioDirector.ComposedTrackTrims"/> (the real table, not a copy) so a future
    /// TrimDb edit is caught here regardless of which phase it lands on.
    /// </summary>
    [TestCase]
    public void NoComposedTrack_EverCarriesAPositiveTrimDb()
    {
        foreach (var (phase, trim) in AudioDirector.ComposedTrackTrims)
        {
            AssertThat(trim)
                .OverrideFailureMessage(
                    $"{phase}'s composed track carries a +{trim:0.##}dB TrimDb. A positive trim boosts " +
                    "a quiet generation's own noise floor right along with its content — night-still-long " +
                    "shipped at +5.45dB and the boosted hiss was the owner's 'random loud static.' If a " +
                    "generation needs a boost to reach level, the fix is a better generation (U9), never " +
                    "a positive TrimDb.")
                .IsLessEqual(0f);
        }
    }

    /// <summary>
    /// U-audio-fingerprint (2026-08-09, fix/night-music-is-static): the TrimDb sign guard above only
    /// catches a generation admitting it is too quiet — it says nothing if a phase's FILE is quietly
    /// swapped for a bad take at TrimDb 0, which is exactly how the original defect would have shipped
    /// had night-still-long.mp3 been left at 0dB instead of +5.45dB. This pins exactly which bytes
    /// were already measured and approved, offline, the same way <c>ComposedTrackTrims</c> pins
    /// TrimDb — a byte-identity check, cheap and certain, that complements (not replaces) the live
    /// decoded measurement in <see cref="EveryComposedTrack_StaysUnderItsTruePeakCeiling"/> below,
    /// which is what actually re-derives loudness/peak facts from the shipped audio instead of
    /// trusting whoever last measured it by hand.
    ///
    /// <para>Measured 2026-08-09 with soundfile/pyloudnorm (decode -&gt; integrated LUFS, plus per-second
    /// RMS across the opening 10s to check for real dynamics): day-first-light -13.28 LUFS (10s RMS
    /// spread 53.8dB), town-dusk -15.30 LUFS (spread 42.3dB), quest-wait -14.30 LUFS (spread 14.4dB),
    /// night-still -21.74 LUFS (spread 26.4dB) — all comfortably dynamic. For comparison, the rejected
    /// night-still-long.mp3 measured -27.12 LUFS integrated but only 0.75dB of RMS spread across its
    /// opening 10s: a flat, near-constant noise floor with no musical dynamics at all, the numeric
    /// signature behind "random static noises." That file was deleted from the repo in this same
    /// commit rather than left as an orphan a future edit could re-wire.</para>
    ///
    /// <para><b>Re-measured and re-hashed the same day</b> once true-peak checking (see below) found
    /// day-first-light and town-dusk both clipping on reconstruction — day-first-light.mp3 and
    /// town-dusk.mp3 were re-encoded at a lower level (never boosted) to fix it, which changes their
    /// bytes and therefore their hashes here. quest-wait and night-still were untouched and keep
    /// their original hashes.</para>
    ///
    /// <para>Any future regeneration, re-encode, or newly-wired id fails this test with no approved hash
    /// — which is the point: it forces whoever changes the bytes to re-run the measurement above and
    /// deliberately vouch for the new file before updating <see cref="ApprovedTrackHashes"/>, rather
    /// than letting a bad take ship silently the way night-still-long did.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ApprovedTrackHashes = new(StringComparer.Ordinal)
    {
        ["day-first-light"] = "749315C1653CF6651BADF74032B71D1C986A4D6B8BE68A706C37A9C8986838A3",
        ["town-dusk"] = "54CCC2B31BEEFE56D2CF06ADA034151F1740E93C9CAB42C0690C098879206B75",
        ["quest-wait"] = "891C6842028F358A8C2285C719B8B9A29422395FEB779E8C888EFAFCBE170367",
        ["night-still"] = "E70FFCCDF8ABEEAE015D2FCDA0356118E1C998EA7D4ED48EF75DAA731ED5FDEB",
    };

    [TestCase]
    public void EveryComposedTrack_MatchesItsApprovedLoudnessFingerprint()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var path = ProjectSettings.GlobalizePath($"res://assets/audio/{id}.mp3");

            AssertThat(File.Exists(path))
                .OverrideFailureMessage(
                    $"{phase} maps to composed track '{id}' but {path} does not exist on disk — either " +
                    "the file was removed without updating AudioDirector.ComposedTracks, or it was " +
                    "renamed and this fingerprint census was not updated to match.")
                .IsTrue();

            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var isApproved = ApprovedTrackHashes.TryGetValue(id, out var expectedHash)
                && string.Equals(actualHash, expectedHash, StringComparison.Ordinal);

            AssertThat(isApproved)
                .OverrideFailureMessage(
                    $"{phase}'s composed track '{id}' hashes to {actualHash}, which is not in " +
                    "ApprovedTrackHashes. This is either a brand-new id that was never loudness-checked, " +
                    "or existing bytes silently changed under an id this test already trusted. Before " +
                    "adding/updating the hash: decode the file (soundfile/pyloudnorm or ffmpeg loudnorm), " +
                    "check its integrated LUFS against its neighbours and its per-second RMS spread over " +
                    "the opening ~10s — under ~2dB of spread is a flat noise floor, not music, and is " +
                    "exactly how night-still-long.mp3 shipped as 'random static' at night.")
                .IsTrue();
        }
    }

    /// <summary>
    /// U-audio-peak-guard (2026-08-09, fix/night-music-is-static continued): the fingerprint test
    /// above freezes bytes but says nothing about whether a BRAND NEW file is any good — it would not
    /// have caught town-dusk.mp3 (+1.71 dBTP true peak, inter-sample clipping) or day-first-light.mp3
    /// (+0.03 dBTP) the day either landed, because neither had a prior hash to compare against. This
    /// test decodes every composed track through the SAME <see cref="AudioStream.InstantiatePlayback"/>
    /// / <see cref="AudioStreamPlayback.MixAudio"/> path the game itself plays through — no separate
    /// offline tool that could drift from what actually ships — and measures its peak directly, so a
    /// future regeneration or re-encode that lands hot fails here without anyone needing to remember
    /// to run ffmpeg by hand.
    ///
    /// <para><b>Why a stored sample never being &gt;1.0 is not enough.</b> "True peak" is an OVERSAMPLED
    /// measurement (ITU-R BS.1770) precisely because the reconstructed analog/interpolated waveform
    /// between two adjacent samples can exceed either sample's own value — a file can clip on
    /// playback with no single decoded sample ever touching 0dBFS. This test approximates that with a
    /// cheap 2x linear interpolation between consecutive decoded frames (not the reference polyphase
    /// resampler ffmpeg's <c>ebur128</c>/<c>loudnorm</c> used to find and confirm this defect — see
    /// <see cref="AudioDirector.ComposedTracks"/>'s own doc for those numbers) — not exact, but a
    /// linear interpolant strictly increases sensitivity over a plain sample-peak check, and every
    /// track this PR retuned now lands with &gt;1dB of real margin under the ceiling, comfortably
    /// clear of what a cheap approximation might under- or over-state by a few tenths of a dB.</para>
    ///
    /// <para>Frame count is bounded by the stream's own <see cref="AudioStream.GetLength"/>, not "mix
    /// until a short read" — every composed track loops (<see cref="AudioDirector.LoadComposed"/>), so
    /// an unbounded read would just keep decoding the next lap forever instead of ending.</para>
    /// </summary>
    private const float TruePeakCeilingDbTp = -1.0f;

    [TestCase]
    public void EveryComposedTrack_StaysUnderItsTruePeakCeiling()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var stream = AudioDirector.LoadComposedTrackForCensus(phase);
            AssertThat(stream)
                .OverrideFailureMessage($"{phase}'s composed track '{id}' would not load — cannot peak-check it.")
                .IsNotNull();

            var mixRate = AudioServer.GetMixRate();
            var frameBudget = (int)Math.Ceiling(stream!.GetLength() * mixRate) + (int)mixRate; // +1s margin
            var frames = DecodeUpTo(stream, frameBudget);

            AssertThat(frames.Length)
                .OverrideFailureMessage(
                    $"{phase}'s composed track '{id}' decoded 0 frames via MixAudio — either playback " +
                    "failed to instantiate or this test's assumption about MixAudio no longer holds; " +
                    "either way, a track this test cannot see is a track this guard cannot protect.")
                .IsGreater(0);

            var (samplePeakDb, truePeakDb) = PeakDb(frames);

            AssertThat(truePeakDb)
                .OverrideFailureMessage(
                    $"{phase}'s composed track '{id}' has an (approximate) true peak of {truePeakDb:0.00} " +
                    $"dBTP, sample peak {samplePeakDb:0.00} dBFS — over the {TruePeakCeilingDbTp:0.0} dBTP " +
                    "ceiling. This is the exact shape of the town-dusk.mp3 defect (+1.71 dBTP shipped, " +
                    "heard as 'random static' during the Evening bed the player sits with at day's end): " +
                    "reduce the FILE's own level and re-encode (ffmpeg 'volume=<n>dB' + libmp3lame at the " +
                    "original bitrate), then adjust TrimDb to compensate so effective loudness is " +
                    "unchanged. Never fix this by trimming louder.")
                .IsLessEqual(TruePeakCeilingDbTp);
        }
    }

    /// <summary>Decodes up to <paramref name="frameBudget"/> frames of <paramref name="stream"/> from
    /// its start via <see cref="AudioStreamPlayback.MixAudio"/>, in bounded chunks — the same
    /// decode-without-playing technique a level meter or waveform view would use, and the only way
    /// this codebase can inspect real PCM out of a compressed music asset (SFX get this for free via
    /// raw <see cref="AudioStreamWav.Data"/>; composed tracks are MP3 and have no such shortcut).</summary>
    private static Vector2[] DecodeUpTo(AudioStream stream, int frameBudget)
    {
        var playback = stream.InstantiatePlayback();
        playback.Start(0.0);

        var collected = new List<Vector2>(Math.Min(frameBudget, 1_000_000));
        while (collected.Count < frameBudget)
        {
            var want = Math.Min(4096, frameBudget - collected.Count);
            var chunk = playback.MixAudio(1.0f, want);
            if (chunk.Length == 0)
            {
                break; // decode ended before the budget -- nothing more to read
            }

            collected.AddRange(chunk);
        }

        playback.Stop();
        return collected.ToArray();
    }

    /// <summary>Plain decoded sample peak, and an approximate oversampled ("true") peak via a cheap 2x
    /// linear interpolation between adjacent frames per channel — see this method's caller for why an
    /// approximation is an acceptable trade here.</summary>
    private static (float SamplePeakDb, float TruePeakDb) PeakDb(Vector2[] frames)
    {
        var sampleMax = 0f;
        var trueMax = 0f;

        for (var i = 0; i < frames.Length; i++)
        {
            var l = frames[i].X;
            var r = frames[i].Y;
            var frameMax = Math.Max(Math.Abs(l), Math.Abs(r));
            sampleMax = Math.Max(sampleMax, frameMax);
            trueMax = Math.Max(trueMax, frameMax);

            if (i + 1 < frames.Length)
            {
                var midL = (l + frames[i + 1].X) * 0.5f;
                var midR = (r + frames[i + 1].Y) * 0.5f;
                trueMax = Math.Max(trueMax, Math.Max(Math.Abs(midL), Math.Abs(midR)));
            }
        }

        static float ToDb(float linear) => linear > 0f ? 20f * MathF.Log10(linear) : -120f;
        return (ToDb(sampleMax), ToDb(trueMax));
    }

    /// <summary>
    /// The dev A/B toggle (R3): pressing M must swap composed vs synth for the phase ALREADY
    /// PLAYING, immediately and audibly — not merely flip a flag that only takes effect on the next
    /// phase change, which would make the owner wait an entire day-phase to judge two tracks back to
    /// back. Calls <c>_UnhandledKeyInput</c> directly (this suite's established pattern for
    /// overridden lifecycle methods, e.g. <c>_Process</c> elsewhere in this file) rather than pumping
    /// the input system through a frame.
    ///
    /// <para><b>Why this asserts on the AUDIBLE player, not "any player has stream X".</b> A first
    /// version of this test checked <c>music.Any(p =&gt; p.Stream == X)</c> and failed on the SECOND
    /// press even though the toggle was working correctly. Root cause: <c>AudioStreamPlayer.Stop()</c>
    /// (called by <see cref="AudioDirector._Process"/> once a fade completes) does not clear
    /// <c>.Stream</c> — a fully faded-out, silent player keeps whatever stream it last held. With only
    /// two players round-robining, a THIRD crossfade reassigns the player that was silent two
    /// crossfades ago back to a stream it already held — so "does any player have stream X" can stay
    /// true from a stale, inaudible reference even after the toggle correctly swapped what is actually
    /// playing. The toggle logic itself was never wrong; "any player has this stream" just is not the
    /// same question as "what is currently playing," once more than two crossfades have happened. This
    /// version asks the right question: after letting a crossfade land fully, exactly one player sits
    /// at <c>MusicDb+trim</c> (audible) and the other at <c>SilentDb</c> (silent) — so the loudest one
    /// IS the current bed, and its stream is the only one worth asserting on.</para>
    /// </summary>
    [TestCase]
    public void TheABToggle_SwapsComposedAndSynthLive()
    {
        // C1 (2026-08-09 shell-and-audio-menu plan) gated this hotkey behind a dev flag — off by
        // default now that players own their own mix through Settings. Set BEFORE _Ready reads it
        // (AddChild below, not construction, is what fires _Ready), same idiom as MuteEnvVar's own
        // MuteEnvVar_StillSilencesEverything_WithComposedTracksInTheMix test.
        OS.SetEnvironment(AudioDirector.DevHotkeysEnvVar, "1");
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);

            var music = director.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name.ToString().StartsWith("Music")).ToList();

            // Lets the current crossfade land fully (CrossfadeSeconds), then returns whichever player
            // ended up audible — the one NOT sitting at SilentDb. Stale streams on the other, silent
            // player (see the class doc above) never matter here because we only ever look at this one.
            AudioStreamPlayer CurrentlyAudible()
            {
                director._Process(2.5); // CrossfadeSeconds
                return music.OrderByDescending(p => p.VolumeDb).First();
            }

            director.SetPhase(DayPhase.Evening); // composed 'town-dusk' by default (_preferSynth starts false)
            AssertThat(CurrentlyAudible().Stream == MusicBed.For(DayPhase.Evening))
                .OverrideFailureMessage(
                    "Evening started on the synth bed, not the composed track — the toggle has " +
                    "nothing to prove otherwise against.")
                .IsFalse();

            var keyDown = new InputEventKey { PhysicalKeycode = Key.M, Pressed = true };

            director._UnhandledKeyInput(keyDown);
            AssertThat(CurrentlyAudible().Stream == MusicBed.For(DayPhase.Evening))
                .OverrideFailureMessage("Pressing M did not swap Evening onto the synth bed.")
                .IsTrue();

            director._UnhandledKeyInput(keyDown);
            AssertThat(CurrentlyAudible().Stream == MusicBed.For(DayPhase.Evening))
                .OverrideFailureMessage("Pressing M a second time did not swap Evening back to the composed track.")
                .IsFalse();

            // A toggle that works once is not a toggle — confirm the CYCLE, not just one flip.
            director._UnhandledKeyInput(keyDown);
            AssertThat(CurrentlyAudible().Stream == MusicBed.For(DayPhase.Evening))
                .OverrideFailureMessage("Pressing M a third time did not swap Evening onto the synth bed again — the toggle only flipped once.")
                .IsTrue();
        }
        finally
        {
            director.Free();
            OS.UnsetEnvironment(AudioDirector.DevHotkeysEnvVar);
        }
    }

    /// <summary>
    /// C1 (2026-08-09 shell-and-audio-menu plan): the footgun this unit exists to close. Before this
    /// unit, M silently flipped the composed/synth A/B toggle from ANY screen with no on-screen
    /// explanation — now that players own their own mix through <c>SettingsPanel</c>, that hotkey
    /// must stay inert unless <see cref="AudioDirector.DevHotkeysEnvVar"/> is explicitly set. This is
    /// the mirror image of <see cref="TheABToggle_SwapsComposedAndSynthLive"/> above: same key event,
    /// gate left at its OFF default, and the bed must not move at all.
    /// </summary>
    [TestCase]
    public void MHotkey_DoesNothing_UnlessTheDevFlagIsSet()
    {
        OS.UnsetEnvironment(AudioDirector.DevHotkeysEnvVar); // belt-and-suspenders: prove the default is off
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);

            var music = director.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name.ToString().StartsWith("Music")).ToList();

            director.SetPhase(DayPhase.Evening);
            director._Process(2.5); // let the crossfade land
            var before = music.OrderByDescending(p => p.VolumeDb).First().Stream;

            var keyDown = new InputEventKey { PhysicalKeycode = Key.M, Pressed = true };
            director._UnhandledKeyInput(keyDown);
            director._Process(2.5);

            var after = music.OrderByDescending(p => p.VolumeDb).First().Stream;
            AssertThat(after == before)
                .OverrideFailureMessage(
                    "Pressing M changed the audible bed even though DevHotkeysEnvVar was not set — " +
                    "the hotkey must stay inert by default now that players own the mix in Settings.")
                .IsTrue();
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// C1's mixer: the music PREFERENCE fader must re-level an already-settled bed immediately
    /// (no fade in flight to hide behind), and by EXACTLY its own linear-to-dB conversion — proving
    /// the preference layer stacks additively on top of whatever mastering level (MusicDb+TrimDb)
    /// the bed already had, never replacing or collapsing it (the two-layer contract TrimDb's own
    /// doc comment insists on).
    /// </summary>
    [TestCase]
    public void SetMusicVolume_ReLevelsTheSettledBed_ByExactlyItsOwnGain()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            director.SetPhase(DayPhase.Evening);
            director._Process(2.5); // let the crossfade land — nothing left in flight

            var music = director.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name.ToString().StartsWith("Music")).ToList();
            var before = music.OrderByDescending(p => p.VolumeDb).First().VolumeDb;

            director.SetMusicVolume(0.25f);

            var after = music.OrderByDescending(p => p.VolumeDb).First().VolumeDb;
            AssertThat(after - before)
                .OverrideFailureMessage(
                    "SetMusicVolume(0.25) must re-level the already-settled bed right away by exactly "
                    + "Mathf.LinearToDb(0.25) — a fader is a linear gain stacked on top of the "
                    + "mastering level, never a replacement for it.")
                .IsEqualApprox(Mathf.LinearToDb(0.25f), 0.01f);
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>C1's mixer: the SFX fader scales the very next pooled voice's gain — SFX carry no
    /// other baked-in baseline in this file, so at the Master default (1.0) the voice's dB should
    /// land on exactly the fader's own linear-to-dB conversion.</summary>
    [TestCase]
    public void SetSfxVolume_ScalesThePooledVoiceGain()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            director.SetSfxVolume(0.5f);
            director.Play(Cue.Coin);

            var voice = director.GetChildren().OfType<AudioStreamPlayer>()
                .First(p => p.Name.ToString().StartsWith("Voice") && p.Playing);

            AssertThat(voice.VolumeDb)
                .OverrideFailureMessage(
                    "A cue played at SfxVolume=0.5 (Master at its 1.0 default) should sit at exactly "
                    + "Mathf.LinearToDb(0.5).")
                .IsEqualApprox(Mathf.LinearToDb(0.5f), 0.01f);
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// C1's mixer: zero is a LEGAL, fully-supported narrator setting — the architecture's own rule
    /// (<c>AudioDirector.SpeakNarrator</c>'s doc) is that the narrator carries no information the
    /// screen does not already carry, so a silenced narrator is indistinguishable from a voice
    /// library that was never recorded. This only proves the dedicated player's GAIN actually drops;
    /// <c>SpeakNarrator</c> itself still writes text at every volume, including zero — that half of
    /// the contract is the skipping law, not a mixer number, and is not re-proven here.
    /// </summary>
    [TestCase]
    public void SetNarratorVolume_Zero_ActuallySilencesTheDedicatedVoice()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            var narratorVoice = director.GetChildren().OfType<AudioStreamPlayer>()
                .First(p => p.Name == "NarratorVoice");
            var atFullVolume = narratorVoice.VolumeDb;

            director.SetNarratorVolume(0f);

            AssertThat(narratorVoice.VolumeDb)
                .OverrideFailureMessage(
                    "SetNarratorVolume(0) did not lower the dedicated narrator player's gain — zero "
                    + "must be a real, reachable setting, not a state nothing enforces.")
                .IsLess(atFullVolume);
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// C1's boot-time re-apply: <c>ApplyPersistedMixer</c> is the one glue point between
    /// <c>UiSettings</c>' storage and a live director's state (<c>MainUi.BuildUi</c>'s one real call
    /// site), exercised nowhere else in this suite. Brackets its own
    /// <c>UiSettings.DeleteForTests()</c> before AND after so no sibling test in this process — none
    /// of which expect a persisted mix to exist at all — can inherit what this one wrote.
    /// </summary>
    [TestCase]
    public void ApplyPersistedMixer_PullsEverySavedFaderAndMute_OntoAFreshDirector()
    {
        UiSettings.DeleteForTests();
        try
        {
            UiSettings.SaveMasterVolume(0.6f);
            UiSettings.SaveMusicVolume(0.4f);
            UiSettings.SaveSfxVolume(0.3f);
            UiSettings.SaveNarratorVolume(0f);
            UiSettings.SaveMuted(true);

            var director = new AudioDirector();
            try
            {
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
                director.ApplyPersistedMixer();

                AssertThat(director.MasterVolume).IsEqualApprox(0.6f, 0.001f);
                AssertThat(director.MusicVolume).IsEqualApprox(0.4f, 0.001f);
                AssertThat(director.SfxVolume).IsEqualApprox(0.3f, 0.001f);
                AssertThat(director.NarratorVolume).IsEqualApprox(0f, 0.001f);
                AssertThat(director.Muted)
                    .OverrideFailureMessage("A saved Muted:true was not picked up on boot.")
                    .IsTrue();
            }
            finally
            {
                director.Free();
            }
        }
        finally
        {
            UiSettings.DeleteForTests();
        }
    }

    /// <summary>
    /// U2: composed tracks each carry a per-track <c>TrimDb</c> (see
    /// <c>AudioDirector.ComposedTracks</c>) so a hot composed master does not simply play louder than
    /// the bed it replaced — which means the two music players no longer always fade toward the SAME
    /// target level. The regression this guards against: without per-player trim tracking, retriggering
    /// a crossfade mid-transition would make the OUTGOING player snap to whatever level the NEW
    /// target's trim implies instead of continuing from the level it was actually at — an audible pop
    /// where a fade should be.
    ///
    /// <para>U4 (playtest-three plan) gave Morning a composed entry too, so all five
    /// <see cref="DayPhase"/> values now default to composed — there is no longer a phase-driven
    /// synth transition to exercise here (the dev A/B toggle is the only remaining path to the synth
    /// bed, covered separately by <see cref="TheABToggle_SwapsComposedAndSynthLive"/>). What this test
    /// exercises instead is arguably the harder case: three composed-to-composed handoffs with three
    /// DIFFERENT trims, which is exactly the shape that would expose a trim-tracking bug a same-trim
    /// transition could not.</para>
    /// </summary>
    [TestCase]
    public void CrossfadingBetweenDifferentTrims_NeverJumpsLevel()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);

            void AssertNoJump(DayPhase from, DayPhase to)
            {
                director.SetPhase(from);
                director._Process(2.5); // CrossfadeSeconds — let the fade land fully before recording

                var players = director.GetChildren().OfType<AudioStreamPlayer>()
                    .Where(p => p.Name.ToString().StartsWith("Music")).ToList();
                var before = players.ToDictionary(p => p.Name.ToString(), p => p.VolumeDb);

                director.SetPhase(to);
                director._Process(0.001); // one instant into the NEW fade — should barely move yet

                foreach (var p in players)
                {
                    var jump = MathF.Abs(p.VolumeDb - before[p.Name.ToString()]);
                    AssertThat(jump)
                        .OverrideFailureMessage(
                            $"{p.Name} jumped {jump:0.0}dB in a single instant when {from} handed off " +
                            $"to {to}. A crossfade must continue from the level a player was ACTUALLY " +
                            "at, not snap toward the new target's trim.")
                        .IsLess(1.0f);
                }
            }

            // All three now composed-to-composed (U4), each pair with a different TrimDb — see
            // AudioDirector.ComposedTracks for the current numbers.
            AssertNoJump(DayPhase.Evening, DayPhase.Camp);
            AssertNoJump(DayPhase.Camp, DayPhase.Morning);
            AssertNoJump(DayPhase.Morning, DayPhase.Expedition);
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// U2 update to the mute contract: composed tracks must not be able to sneak past
    /// <see cref="AudioDirector.MuteEnvVar"/> the way the synth bed already could not. Every
    /// automated tool in <c>tools/</c> relies on this env var to keep an unattended run silent
    /// (<see cref="DevToolAudio.Silence"/>), and the composed-track ladder lives entirely inside the
    /// same Muted-gated call path as the synth bed always did (<c>ApplyPhaseBed</c> is only reached
    /// past the Muted check in <c>SetPhase</c>) — proven here structurally, not by trusting the
    /// comment.
    /// </summary>
    [TestCase]
    public void MuteEnvVar_StillSilencesEverything_WithComposedTracksInTheMix()
    {
        DevToolAudio.Silence(); // the exact call every automated tool makes before mounting MainUi
        try
        {
            var director = new AudioDirector();
            try
            {
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
                AssertThat(director.Muted)
                    .OverrideFailureMessage("AudioDirector did not read MuteEnvVar on _Ready.")
                    .IsTrue();

                director.SetPhase(DayPhase.Evening); // a composed-mapped phase, not Morning
                director.Play(Cue.Coin);

                AssertThat(director.GetChildren().OfType<AudioStreamPlayer>().Any(p => p.Playing))
                    .OverrideFailureMessage(
                        "Something is audibly playing while MuteEnvVar is set — an automated run " +
                        "would make noise.")
                    .IsFalse();
            }
            finally
            {
                director.Free();
            }
        }
        finally
        {
            OS.UnsetEnvironment(AudioDirector.MuteEnvVar);
        }
    }

    /// <summary>
    /// U-audio-2's own census, extending the KTD-B idea <see cref="EveryComposedTrack_LoadsAndLoops"/>
    /// already applies to the composed table down onto <see cref="MusicBed"/> itself: every value
    /// <see cref="DayPhase"/> can ever hold must resolve to a real, audible bed. Deliberately reads
    /// <c>Enum.GetValues&lt;DayPhase&gt;()</c> rather than the hand-maintained <see cref="AllPhases"/>
    /// array used by every other test in this file — <c>AllPhases</c> is exactly the kind of list a
    /// future phase addition could be added to the enum and forgotten to add here, which would make
    /// every OTHER test in this file silently skip it while still passing. This test cannot silently
    /// skip a phase because it does not know the list in advance; it asks the enum, not a copy of it.
    /// <see cref="MusicBed.For"/> always resolves something today (<c>MoodFor</c>'s <c>_</c> arm is the
    /// fallback), so this is a regression guard against that ever stopping being true, not a check that
    /// is expected to catch anything right now.
    /// </summary>
    [TestCase]
    public void EveryDayPhase_ResolvesToANonSilentBed()
    {
        foreach (var phase in Enum.GetValues<DayPhase>())
        {
            var wav = MusicBed.For(phase);
            AssertThat(wav)
                .OverrideFailureMessage($"{phase} has no music bed at all — MusicBed.For returned null.")
                .IsNotNull();

            var pcm = Pcm(wav);
            AssertThat(Rms(pcm))
                .OverrideFailureMessage(
                    $"{phase} resolves to a bed but it is effectively silent (RMS {Rms(pcm):0.####}) — " +
                    "the exact 'on disk but nobody hears it' shape this census exists to catch.")
                .IsGreater(0.01f);
        }
    }

    /// <summary>
    /// U-audio-2: "nothing you add may be louder than what it replaces." Each per-venue entrance cue
    /// (<see cref="Cue.EnterForge"/>, <see cref="Cue.EnterTavern"/>, <see cref="Cue.EnterMarket"/>,
    /// <see cref="Cue.EnterMineGate"/>, <see cref="Cue.EnterNoticeboard"/>) replaces
    /// <see cref="Cue.PanelOpen"/> at exactly one Town2D building's entrance
    /// (<c>MainUi.EntranceCueFor</c>) — the owner's complaint was that the generic cue was "too loud and
    /// harsh," so a replacement that is merely DIFFERENT but equally loud would only fix half of it. Peak
    /// is the right comparison here (not RMS/LUFS): these are all sub-half-second transients where what a
    /// player perceives as "loud" is dominated by the peak hit, not the average level.
    /// </summary>
    [TestCase]
    public void TheVenueCues_AreNeverLouderThanPanelOpen()
    {
        var panelOpenPeak = Peak(Pcm(SfxLibrary.Get(Cue.PanelOpen)));
        var venueCues = new[]
        {
            Cue.EnterForge, Cue.EnterTavern, Cue.EnterMarket, Cue.EnterMineGate, Cue.EnterNoticeboard,
        };

        foreach (var cue in venueCues)
        {
            var peak = Peak(Pcm(SfxLibrary.Get(cue)));
            AssertThat(peak)
                .OverrideFailureMessage(
                    $"{cue} peaks at {peak:0.###}, PanelOpen (the generic cue it replaces at its " +
                    $"building) peaks at {panelOpenPeak:0.###}. A per-venue cue that is LOUDER than the " +
                    "sound it replaces only fixes 'identical,' not 'too loud.'")
                .IsLessEqual(panelOpenPeak);
        }
    }

    /// <summary>
    /// U5 (playtest-three plan, R6): "the forge stops sounding like a fault." The building cues
    /// already proved (#327) that a soft attack reads as better — this pins that the forge's own
    /// worst offenders (the two hammer blows and the quench) got the same treatment.
    ///
    /// <para><b>Why this can't just check "sample 0 is near zero."</b> Every cue in the library
    /// already passes that: <c>SfxLibrary.Build</c> unconditionally runs every buffer through
    /// <see cref="Synth.DeClick"/>, a fixed ~4ms fade applied to ALL cues regardless of whether they
    /// asked for a softer attack. A naive zero-crossing check would be satisfied by DeClick's own
    /// floor and prove nothing about whether these three cues got anything extra. Instead this
    /// measures how many samples it actually takes each cue to reach 90% of its own early peak —
    /// <see cref="Cue.Coin"/> has no <c>attack</c> parameter anywhere in its recipe, so its rise is
    /// bounded by DeClick's shared 4ms floor alone; the forge cues asked for an additional 8-12ms
    /// ramp on top of that, so their rise must take measurably longer.</para>
    /// </summary>
    [TestCase]
    public void HammerAndQuenchCues_RiseSlowerThanAnInstantAttackCue()
    {
        int RiseSamples(Cue cue)
        {
            var pcm = Pcm(SfxLibrary.Get(cue));
            var window = Math.Min(pcm.Length, Synth.Samples(0.03f));
            var peak = 0f;
            for (var i = 0; i < window; i++)
            {
                peak = MathF.Max(peak, MathF.Abs(pcm[i]));
            }

            var target = peak * 0.9f;
            for (var i = 0; i < window; i++)
            {
                if (MathF.Abs(pcm[i]) >= target)
                {
                    return i;
                }
            }

            return window;
        }

        var instantRise = RiseSamples(Cue.Coin);

        foreach (var cue in new[] { Cue.HammerOnBeat, Cue.HammerOffBeat, Cue.Quench })
        {
            var rise = RiseSamples(cue);
            AssertThat(rise)
                .OverrideFailureMessage(
                    $"{cue} reaches 90% of its early peak in {rise} samples " +
                    $"({rise / (float)Synth.SampleRate * 1000:0.#}ms) — no slower than Coin's " +
                    $"instant-attack rise of {instantRise} samples ({instantRise / (float)Synth.SampleRate * 1000:0.#}ms). " +
                    "The forge cues need a real 8-12ms attack on top of DeClick's shared 4ms fade, not just " +
                    "DeClick's own floor every cue already has.")
                .IsGreater(instantRise);
        }
    }

    /// <summary>
    /// U8 (2026-08-02 shell-and-audio plan, R8): "Forge mini game noises are bad - too loud and
    /// harsh (particularly the bellows shift since you have to hold)." The held gesture used to
    /// normalize to 0.30 — double the level every venue-entrance cue (<see cref="Cue.EnterForge"/>,
    /// <see cref="Cue.EnterTavern"/>, etc.) already settled on as "ambient, heard constantly, must
    /// not be harsh." A player holding the bellows for a multi-second craft hears this cue far more
    /// than any single venue entrance, so it has no business being louder than one.
    /// </summary>
    [TestCase]
    public void Bellows_IsNoLouderThanAVenueCue()
    {
        var venueCues = new[]
        {
            Cue.EnterForge, Cue.EnterTavern, Cue.EnterMarket, Cue.EnterMineGate, Cue.EnterNoticeboard,
        };
        var venuePeak = venueCues.Max(c => Peak(Pcm(SfxLibrary.Get(c))));
        var bellowsPeak = Peak(Pcm(SfxLibrary.Get(Cue.Bellows)));

        AssertThat(bellowsPeak)
            .OverrideFailureMessage(
                $"Bellows peaks at {bellowsPeak:0.###}; the loudest venue-entrance cue peaks at " +
                $"{venuePeak:0.###}. A gesture the player HOLDS for several seconds must sit at venue-cue " +
                "level at most, not double it.")
            .IsLessEqual(venuePeak);
    }

    /// <summary>
    /// U8 (R8): <c>AudioDirector.StartLoop</c>/<c>StopLoop</c> — the held-bellows API. Asserts on the
    /// dedicated loop voice's <c>Stream</c>/<c>VolumeDb</c>, the same script-owned properties
    /// <see cref="CrossfadingBetweenDifferentTrims_NeverJumpsLevel"/> already proves reliable in this
    /// suite, rather than on <c>AudioStreamPlayer.Playing</c>'s real-time completion state (which no
    /// existing test in this file relies on, and which this suite cannot verify outside the
    /// orchestrator's serial engine run) — so this stays deterministic under
    /// <see cref="AudioDirector._Process"/>'s own accumulated-delta clock, never a frame count.
    /// </summary>
    [TestCase]
    public void StartLoop_ArmsTheLoopVoice_AndStopLoopFadesThenStops()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);

            director.StartLoop(Cue.Bellows);
            var loopVoice = director.GetChildren().OfType<AudioStreamPlayer>()
                .First(p => p.Name.ToString() == "LoopVoice");

            AssertThat(loopVoice.Stream == SfxLibrary.Get(Cue.Bellows))
                .OverrideFailureMessage("StartLoop did not arm the dedicated loop voice with the Bellows stream.")
                .IsTrue();
            AssertThat(loopVoice.VolumeDb)
                .OverrideFailureMessage($"StartLoop left the loop voice at {loopVoice.VolumeDb}dB instead of audible.")
                .IsEqual(0f);

            director.StopLoop(Cue.Bellows);

            // The release must FADE, not cut — an instant silence would click (DeClick only smooths a
            // clip's own edges, never an arbitrary interruption point). One tiny step in: still close
            // to full volume, nowhere near silent yet.
            director._Process(0.01);
            AssertThat(loopVoice.VolumeDb)
                .OverrideFailureMessage(
                    $"StopLoop dropped the loop voice to {loopVoice.VolumeDb}dB after only 10ms of a " +
                    "120ms release — that reads as a cut, not a fade.")
                .IsGreater(-6f);

            // Comfortably past the release window: fully faded AND actually stopped.
            director._Process(1.0);
            AssertThat(loopVoice.Playing)
                .OverrideFailureMessage("The loop voice never stopped once its release fade completed.")
                .IsFalse();
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// U8 (R8): the held loop's own clip is short (a single ~0.3s breath) — a multi-second hold needs
    /// MANY repeats, or the bellows would go silent after the first breath while still held. Rather
    /// than depend on real playback timing to actually reach the clip's end (which this headless
    /// suite cannot verify outside the orchestrator's serial engine run), this fires the loop voice's
    /// own <c>Finished</c> signal directly — a deterministic way to exercise
    /// <c>AudioDirector</c>'s retrigger handler without waiting on the audio server's real clock.
    /// </summary>
    [TestCase]
    public void HeldLoop_RetriggersOnItsOwnClipEnding_UntilReleased()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            director.StartLoop(Cue.Bellows);
            var loopVoice = director.GetChildren().OfType<AudioStreamPlayer>()
                .First(p => p.Name.ToString() == "LoopVoice");

            loopVoice.Stream = null;
            loopVoice.EmitSignal(AudioStreamPlayer.SignalName.Finished);

            AssertThat(loopVoice.Stream == SfxLibrary.Get(Cue.Bellows))
                .OverrideFailureMessage(
                    "The held loop did not retrigger when its own clip finished — a hold longer than " +
                    "one breath would go silent while still held.")
                .IsTrue();

            // Once released, the SAME clip ending must NOT retrigger again.
            director.StopLoop(Cue.Bellows);
            director._Process(1.0); // let the release fade complete and the voice actually stop
            loopVoice.Stream = null;
            loopVoice.EmitSignal(AudioStreamPlayer.SignalName.Finished);

            AssertThat(loopVoice.Stream is null)
                .OverrideFailureMessage("The loop voice retriggered again AFTER StopLoop released it.")
                .IsTrue();
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>
    /// U5 (R6): "the shop cue is byte-untouched." This is a FROZEN COPY of <see cref="Cue.EnterMarket"/>'s
    /// exact synthesis recipe as it existed the moment U5 landed — the same <see cref="Synth"/> static
    /// calls <see cref="SfxLibrary"/> itself uses, called here a second time and asserted byte-for-byte
    /// equal to whatever <see cref="SfxLibrary.Get"/> actually produces. A future edit to EnterMarket's
    /// recipe — even one parameter — makes this frozen copy diverge from production and turns this test
    /// red; the only way it stays green is if nobody touches the shop cue. Deliberately NOT a hardcoded
    /// hash constant: computing that constant would require running the engine suite once to learn it,
    /// which no implementing agent may do (orchestrator-serial rule) — comparing two live computations of
    /// the SAME recipe proves the same thing with no such dependency.
    /// </summary>
    [TestCase]
    public void EnterMarket_IsByteUntouched()
    {
        var reference = new float[Synth.Samples(0.30f)];
        for (var i = 0; i < reference.Length; i++)
        {
            var t = i / (float)Synth.SampleRate;
            reference[i] = Synth.Noise(i, seed: 81) * Synth.Decay(t, 0.05f) * 0.25f;
        }

        Synth.LowPass(reference, 2200f);
        Synth.AddPartial(reference, 1900f, 0.20f, halfLife: 0.06f, attack: 0.006f);
        Synth.AddPartial(reference, 3550f, 0.11f, halfLife: 0.04f, attack: 0.008f);
        Synth.Normalise(reference, 0.22f);
        Synth.DeClick(reference); // SfxLibrary.Build's own final step, applied to every cue

        var referenceBytes = Convert.ToBase64String(Synth.ToStream(reference).Data);
        var actualBytes = Convert.ToBase64String(SfxLibrary.Get(Cue.EnterMarket).Data);

        AssertThat(actualBytes)
            .OverrideFailureMessage(
                "EnterMarket's bytes moved. It is the one cue the owner called good — R6 requires it " +
                "stays byte-for-byte identical while its neighbors (EnterForge/Tavern/MineGate/Noticeboard) " +
                "get quieter around it.")
            .IsEqual(referenceBytes);
    }

    /// <summary>
    /// U-audio-3 (verbs that resolved silently): pins <see cref="MainUi.DeathNoticeCueFor"/> — the
    /// decision that gives a hero's death its own quiet toll instead of sharing the day's ordinary
    /// <see cref="Cue.Bell"/> — directly, rather than scripting an entire expedition to a death just
    /// to prove one <c>switch</c> arm (see that method's own doc for why a pure mapping is the
    /// testable surface here, the same trade <c>AudioDirector.LoadComposedTrackForCensus</c> makes
    /// for composed-track resolution).
    /// </summary>
    [TestCase]
    public void DeathNoticeCueFor_OnlySpeaksForDeathEpitaph()
    {
        // Nullable-enum equality checked via plain bool (AssertBool), not AssertThat().IsEqual —
        // Nullable<Cue> does not satisfy GdUnit4's IComparable-constrained overload.
        AssertBool(MainUi.DeathNoticeCueFor(NarratorVoiceDirector.Trigger.DeathEpitaph) == Cue.DeathToll)
            .OverrideFailureMessage("A hero who did not come back must get a distinct cue, not silence.")
            .IsTrue();

        foreach (var trigger in Enum.GetValues<NarratorVoiceDirector.Trigger>())
        {
            if (trigger == NarratorVoiceDirector.Trigger.DeathEpitaph)
            {
                continue;
            }

            AssertBool(MainUi.DeathNoticeCueFor(trigger) is null)
                .OverrideFailureMessage(
                    $"{trigger} played {MainUi.DeathNoticeCueFor(trigger)} — only DeathEpitaph should " +
                    "ever get a cue here. A proven save or a killing blow is good news; good news does " +
                    "not need a bell of its own on top of the narrator already speaking.")
                .IsTrue();
        }
    }

    /// <summary>
    /// U2 (loud-failures-and-quiet-channels plan): census over every production script under
    /// res://scripts — every <see cref="Cue"/> value must be referenced somewhere OUTSIDE this
    /// file's own definition/switch (<c>SfxLibrary.cs</c>), or it is a synthesized sound nobody can
    /// ever hear. <see cref="Cue.Click"/> was exactly that before this unit: a complete recipe in
    /// <see cref="SfxLibrary.Build"/>, zero call sites anywhere. Mirrors the source-text-scan
    /// technique <c>AgentPlaytestBridgeTests</c> already uses to pin a driver/client contract
    /// against real files rather than a parallel hand-maintained list.
    ///
    /// <para><b>Why a plain substring scan, not a call-syntax regex.</b> A stricter
    /// "must appear inside .Play(" / ".StartLoop(" pattern would MISS several cues that are wired
    /// through a mapping helper instead of a literal call site — <c>MainUi.EntranceCueFor</c>'s
    /// switch composes <see cref="Cue.PanelOpen"/>/<see cref="Cue.EnterForge"/>/
    /// <see cref="Cue.EnterTavern"/>/<see cref="Cue.EnterMarket"/>/<see cref="Cue.EnterMineGate"/>/
    /// <see cref="Cue.EnterNoticeboard"/>, and <c>MainUi.DeathNoticeCueFor</c>'s ternary composes
    /// <see cref="Cue.DeathToll"/>, both later played by their own caller
    /// (<c>Audio.Play(EntranceCueFor(id))</c>) rather than a literal <c>.Play(Cue.X)</c> text — this
    /// scan proves "the id is referenced somewhere in production," the same simple Contains-based
    /// rigor this repo's other census tests already use, not a stronger claim this file cannot
    /// cheaply verify.</para>
    /// </summary>
    [TestCase]
    public void EveryCue_HasAtLeastOneProductionReference()
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "SfxLibrary.cs") // the definition/switch, not a caller
            .ToList();

        AssertThat(files.Count)
            .OverrideFailureMessage($"Found no .cs files under {scriptsDir} — did the scripts folder move?")
            .IsGreater(0);

        var combined = string.Join("\n", files.Select(File.ReadAllText));

        foreach (var cue in AllCues)
        {
            AssertThat(combined.Contains($"Cue.{cue}"))
                .OverrideFailureMessage(
                    $"Cue.{cue} has no reference anywhere under res://scripts outside SfxLibrary.cs's " +
                    "own definition/switch — a synthesized sound with no production call site, " +
                    "exactly Cue.Click's fate before this unit.")
                .IsTrue();
        }
    }
}
#endif
