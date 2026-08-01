#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using GodotClient.Tools;
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
    }

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
    /// The loop must be long enough that the ear does not memorise it.
    ///
    /// <para>"loops too quickly" was a real defect at 24 seconds: short enough to learn, after which the
    /// seam stops being seamless and starts being an event. Pinned as a floor rather than an exact value so
    /// the length stays free to grow.</para>
    ///
    /// <para>Also checks that the melody grid scaled WITH the loop. Lengthening <c>LoopSeconds</c> while
    /// leaving a fixed slot count would have produced the same number of notes spread thinner — a sparser
    /// bed rather than a longer one, which is not what was asked for and would be easy to ship by accident.</para>
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
        var onsets = 0;
        for (var i = 1; i < pcm.Length; i++)
        {
            // A pluck attack is a fast rise; the drone underneath changes far more slowly than this.
            if (MathF.Abs(pcm[i]) - MathF.Abs(pcm[i - 1]) > 0.06f)
            {
                onsets++;
                i += Synth.SampleRate / 20; // debounce one attack, don't count its ripple
            }
        }

        AssertThat(onsets)
            .OverrideFailureMessage(
                $"Only {onsets} note attacks across {MusicBed.LoopSeconds}s. The melody grid did not scale " +
                "with the loop length, so the track got longer by getting emptier rather than by having " +
                "more music in it.")
            .IsGreater(12);
    }

    /// <summary>Where "bass" starts, for <see cref="TheMusicBeds_AreNotBassHeavy"/>. Above the beds' roots
    /// (98-131Hz) so the fundamental itself counts as bass, which is the point.</summary>
    private const float BassCutoffHz = 150f;

    /// <summary>Poles in the measurement filter. One is far too gentle to separate a fundamental from the
    /// fifth above it — see the comment at the cascade.</summary>
    private const int BassFilterPoles = 4;

    /// <summary>Ceiling on the low-band energy share. Set from the measured post-fix values with headroom,
    /// not from taste — see the git history of MusicBed for the before/after.</summary>
    private const float MaxBassShare = 0.45f;

    /// <summary>The Mine's allowance. Higher because it is deliberately the lowest-pitched thing in the
    /// game (a fourth under the deepest town bed) — see the note at the comparison. Still a ceiling: it
    /// caught the original 0.40-amplitude D2 fundamental and the ~37Hz sub-bass pulse under it.</summary>
    private const float MaxUndergroundBassShare = 0.55f;

    /// <summary>Floor on loop length. 24s was the reported defect; this is comfortably above it.</summary>
    private const float MinLoopSeconds = 45f;

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

    /// <summary>
    /// U2 (make-it-visible plan), KTD-B applied to audio: the census rule that already exists for art
    /// (every id the shipped game references must resolve to real content), now covering
    /// <see cref="AudioDirector.ComposedTrackIds"/>. Three composed tracks sat on disk and were never
    /// wired into the game for days before this unit — this is the test that turns any FUTURE repeat
    /// of exactly that ("committed but never referenced") red at PR time instead of silent for days.
    /// </summary>
    [TestCase]
    public void EveryComposedTrack_LoadsAndLoops()
    {
        var ids = AudioDirector.ComposedTrackIds;

        AssertThat(ids.Count)
            .OverrideFailureMessage(
                "AudioDirector.ComposedTrackIds is empty. If every composed track was deliberately " +
                "reverted this floor should move with them — but an accidentally emptied table is " +
                "exactly the silent regression this census exists to catch.")
            .IsGreaterEqual(3);

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
        }
    }

    /// <summary>
    /// U2: composed tracks each carry a per-track <c>TrimDb</c> (see
    /// <c>AudioDirector.ComposedTracks</c>) so a hot composed master does not simply play louder than
    /// the bed it replaced — which means the two music players no longer always fade toward the SAME
    /// target level. The regression this guards against: without per-player trim tracking, retriggering
    /// a crossfade mid-transition would make the OUTGOING player snap to whatever level the NEW
    /// target's trim implies instead of continuing from the level it was actually at — an audible pop
    /// where a fade should be. Exercised across composed-to-composed (different trims),
    /// synth-to-composed, and composed-to-synth.
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

            AssertNoJump(DayPhase.Evening, DayPhase.Camp); // composed(-5dB) -> composed(0dB)
            AssertNoJump(DayPhase.Camp, DayPhase.Morning); // composed(0dB) -> synth(0dB)
            AssertNoJump(DayPhase.Morning, DayPhase.Expedition); // synth(0dB) -> composed(-4dB)
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
}
#endif
