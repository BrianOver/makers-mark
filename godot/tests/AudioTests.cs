#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Audio;
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

    private static float Rms(float[] s) => s.Length == 0 ? 0f : MathF.Sqrt(s.Sum(v => v * v) / s.Length);

    private static float Peak(float[] s) => s.Length == 0 ? 0f : s.Max(MathF.Abs);

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
    }

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

        // Brightness: the share of energy ABOVE the low band. A ringing anvil keeps its upper partials; a
        // dull mistimed thud does not. Same 4-pole cascade the music's bass test uses, inverted.
        float HighShare(float[] pcm)
        {
            var low = (float[])pcm.Clone();

            // Four poles (-24dB/octave). One pole is far too gentle to separate bands — a single pass at
            // 900Hz leaves a 1.2kHz partial barely attenuated, so it would measure the whole cue and report
            // a confident wrong number.
            for (var pole = 0; pole < 4; pole++)
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

    /// <summary>A scene takes the music and gives it back. The trap: <c>SetPhase</c> ignores an unchanged
    /// phase, so if closing a scene did not explicitly restore the bed the game would fall silent until
    /// the day happened to move on.</summary>
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
            var backToTown = director.GetChildren().OfType<AudioStreamPlayer>()
                .Any(p => p.Stream == MusicBed.For(DayPhase.Morning));
            AssertThat(backToTown)
                .OverrideFailureMessage(
                    "Closing the Depths left the Mine theme playing (or nothing). Leaving a scene has to " +
                    "restore the day's bed explicitly — SetPhase early-returns on an unchanged phase.")
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
}
#endif
