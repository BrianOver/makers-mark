using System;
using System.Collections.Generic;
using Godot;

namespace GodotClient.Audio;

/// <summary>The game's sound cues, named by what HAPPENED rather than by what they sound like — so a
/// call site reads as intent and the sound can be retuned without touching it.</summary>
public enum Cue
{
    /// <summary>A drawer/panel opening.</summary>
    PanelOpen,

    /// <summary>A drawer/panel closing.</summary>
    PanelClose,

    /// <summary>Any ordinary button press that isn't one of the specific cues below.</summary>
    Click,

    /// <summary>Coin changing hands — buying material, paying a reward.</summary>
    Coin,

    /// <summary>An item placed on the shop shelf.</summary>
    Shelve,

    /// <summary>A craft finished, whatever its grade.</summary>
    CraftDone,

    /// <summary>The day's bell — the phase advancing.</summary>
    Bell,

    /// <summary>A bounty nailed to the board.</summary>
    BountyPost,

    /// <summary>The party leaving for the mine.</summary>
    PartyDepart,

    /// <summary>An action the sim refused.</summary>
    Rejected,
}

/// <summary>
/// Every cue, synthesized once on first use and cached.
///
/// <para>Built lazily rather than in a static initialiser: a headless test that never plays a sound
/// should not pay to synthesize ten buffers, and a static constructor doing real work is awkward to
/// reason about when a test asserts on generation. Each cue is a handful of milliseconds to build.</para>
///
/// <para><b>Design intent.</b> These are all short, dry, and quiet. A blacksmith sim is a calm game the
/// player sits in for a long stretch, and the fastest way to make that unbearable is a bright loud UI
/// click. Nothing here is a synth beep if it can help it: struck things get inharmonic partials with
/// staggered decays, wooden things get filtered noise with a thump under it, and the "coin" is two
/// bright partials a not-quite-octave apart because a real coin is never a single pitch.</para>
/// </summary>
public static class SfxLibrary
{
    private static readonly Dictionary<Cue, AudioStreamWav> Cache = new();

    /// <summary>The stream for <paramref name="cue"/>, synthesized on first request.</summary>
    public static AudioStreamWav Get(Cue cue)
    {
        if (!Cache.TryGetValue(cue, out var stream))
        {
            stream = Build(cue);
            Cache[cue] = stream;
        }

        return stream;
    }

    private static AudioStreamWav Build(Cue cue) => cue switch
    {
        Cue.Click => Build(0.05f, buf =>
        {
            // A dry tick: one short mid partial, no tail. Deliberately the quietest cue in the set
            // (peak 0.35) because it is the one that fires most often.
            Synth.AddPartial(buf, 1180f, 0.5f, halfLife: 0.012f);
            Synth.AddPartial(buf, 2360f, 0.2f, halfLife: 0.006f);
            Synth.Normalise(buf, 0.35f);
        }),

        Cue.PanelOpen => Build(0.28f, buf =>
        {
            // Wood and cloth: filtered noise swelling, with a low body under it. Rising, because it
            // is the sound of something being revealed.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i) * MathF.Min(1f, t * 6f) * Synth.Decay(t, 0.10f);
            }

            Synth.LowPass(buf, 900f);
            Synth.AddPartial(buf, 150f, 0.35f, halfLife: 0.09f);
            Synth.Normalise(buf, 0.5f);
        }),

        Cue.PanelClose => Build(0.22f, buf =>
        {
            // The same material, falling: a soft thump with the noise decaying faster.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 7) * Synth.Decay(t, 0.05f);
            }

            Synth.LowPass(buf, 700f);
            Synth.AddPartial(buf, 110f, 0.45f, halfLife: 0.07f);
            Synth.Normalise(buf, 0.5f);
        }),

        Cue.Coin => Build(0.42f, buf =>
        {
            // Two bright partials a shade off an octave, plus a third high one — the beating between
            // them is what stops it sounding like a synth bell and starts it sounding like metal.
            Synth.AddPartial(buf, 2100f, 0.5f, halfLife: 0.10f);
            Synth.AddPartial(buf, 4130f, 0.32f, halfLife: 0.07f);
            Synth.AddPartial(buf, 5600f, 0.18f, halfLife: 0.04f);
            Synth.Normalise(buf, 0.5f);
        }),

        Cue.Shelve => Build(0.20f, buf =>
        {
            // Wood on wood: a low thunk with a very short bright transient on top.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 3) * Synth.Decay(t, 0.012f) * 0.8f;
            }

            Synth.LowPass(buf, 1600f);
            Synth.AddPartial(buf, 196f, 0.5f, halfLife: 0.05f);
            Synth.AddPartial(buf, 293f, 0.25f, halfLife: 0.035f);
            Synth.Normalise(buf, 0.6f);
        }),

        Cue.CraftDone => Build(0.75f, buf =>
        {
            // A rising three-note figure (root, fifth, octave) — the one unambiguously musical cue in
            // the set, because finishing a craft is the moment the whole game is about.
            AddNote(buf, 392f, at: 0.00f, length: 0.30f, amplitude: 0.42f);
            AddNote(buf, 587f, at: 0.10f, length: 0.30f, amplitude: 0.38f);
            AddNote(buf, 784f, at: 0.20f, length: 0.50f, amplitude: 0.40f);
            Synth.Normalise(buf, 0.6f);
        }),

        Cue.Bell => Build(1.60f, buf =>
        {
            // A real bell's partials are not a harmonic series — the minor-third-ish 1.19 and the 2.76
            // hum are what make it read as a bell rather than an organ. Long half-lives, and the high
            // partials die first, exactly as they do in bronze.
            Synth.AddPartial(buf, 220f, 0.40f, halfLife: 0.90f);
            Synth.AddPartial(buf, 262f, 0.28f, halfLife: 0.70f);
            Synth.AddPartial(buf, 607f, 0.22f, halfLife: 0.40f);
            Synth.AddPartial(buf, 880f, 0.14f, halfLife: 0.22f);
            Synth.AddPartial(buf, 1290f, 0.08f, halfLife: 0.12f);
            Synth.Normalise(buf, 0.55f);
        }),

        Cue.BountyPost => Build(0.26f, buf =>
        {
            // Paper, then the nail: a noise rustle followed by a hard short tap ~60ms in.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 11) * Synth.Decay(t, 0.03f) * 0.5f;
            }

            Synth.LowPass(buf, 3000f);
            var tap = Synth.Samples(0.06f);
            for (var i = tap; i < buf.Length; i++)
            {
                var t = (i - tap) / (float)Synth.SampleRate;
                buf[i] += MathF.Sin(2f * MathF.PI * 1400f * t) * 0.5f * Synth.Decay(t, 0.010f);
            }

            Synth.Normalise(buf, 0.55f);
        }),

        Cue.PartyDepart => Build(1.10f, buf =>
        {
            // A soft two-note horn call, fifth up — a send-off, not a fanfare.
            AddNote(buf, 175f, at: 0.00f, length: 0.55f, amplitude: 0.45f, harmonics: 3);
            AddNote(buf, 262f, at: 0.35f, length: 0.70f, amplitude: 0.40f, harmonics: 3);
            Synth.Normalise(buf, 0.5f);
        }),

        Cue.Rejected => Build(0.24f, buf =>
        {
            // Low, dull, slightly detuned pair — "no" without being shrill about it. Quiet on purpose:
            // being told off should not be the loudest thing in the game.
            Synth.AddPartial(buf, 138f, 0.5f, halfLife: 0.09f);
            Synth.AddPartial(buf, 146f, 0.4f, halfLife: 0.09f);
            Synth.LowPass(buf, 800f);
            Synth.Normalise(buf, 0.4f);
        }),

        _ => Build(0.05f, buf => Synth.AddPartial(buf, 440f, 0.4f, halfLife: 0.02f)),
    };

    /// <summary>A plucked note with a few harmonics, added into <paramref name="buffer"/> starting at
    /// <paramref name="at"/> seconds. Harmonics fall off as 1/n and decay faster the higher they are,
    /// which is roughly what a struck string does.</summary>
    private static void AddNote(float[] buffer, float hz, float at, float length, float amplitude, int harmonics = 2)
    {
        var start = Synth.Samples(at);
        var end = Math.Min(buffer.Length, start + Synth.Samples(length));
        for (var h = 1; h <= harmonics; h++)
        {
            var amp = amplitude / h;
            var halfLife = length * 0.35f / h;
            var step = 2f * MathF.PI * hz * h / Synth.SampleRate;
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (float)Synth.SampleRate;
                buffer[i] += MathF.Sin(step * (i - start)) * amp * Synth.Decay(t, halfLife);
            }
        }
    }

    private static AudioStreamWav Build(float seconds, Action<float[]> fill)
    {
        var buffer = new float[Synth.Samples(seconds)];
        fill(buffer);
        Synth.DeClick(buffer);
        return Synth.ToStream(buffer);
    }
}
