using System;
using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Audio;

/// <summary>
/// The ambient music bed: one seamless synthesized loop per time of day.
///
/// <para><b>Why generated and not a track.</b> There was no music at all — Brian's playtest asked
/// "Music is not loaded?" and nothing had ever been loaded. The model-based route (#46 P4) has been
/// blocked on tooling for a while, and a minute of stereo audio is ~10MB of repo weight per mood. This
/// synthesizes a loop per phase at startup for zero bytes on disk and nothing to license-track, which
/// is the same trade <see cref="SfxLibrary"/> makes. It is a bed, not a soundtrack: if a real
/// composed track ever lands, this is what it replaces.</para>
///
/// <para><b>Why it changes with the phase.</b> The town already has a purple-dusk colour arc
/// (<c>DayPhaseTint</c>) and answering "what time is it" in sound as well as light is most of what makes
/// a place feel lived-in. Dawn sits on a bright major fifth; the two Quest phases drop to a bare open
/// fifth with the melody thinned out, because that is when the player is waiting and the music should
/// get out of the way; Night falls to a minor colour and the lowest drone.</para>
///
/// <para><b>Seamless looping.</b> Every drone frequency is snapped so that a whole number of cycles fits
/// the loop length (<see cref="LoopSeconds"/>), and melody notes are placed so their decay finishes
/// before the end. The loop point is therefore a genuine zero-crossing continuation rather than a
/// crossfade, so there is no seam and no thump — see <see cref="SnapToLoop"/>.</para>
/// </summary>
public static class MusicBed
{
    /// <summary>Loop length. Long enough not to feel like a 4-bar jingle on repeat, short enough that
    /// generating five of them at startup stays imperceptible (~1.3M samples each).</summary>
    public const float LoopSeconds = 24f;

    private static readonly Dictionary<DayPhase, AudioStreamWav> Cache = new();

    /// <summary>The loop for <paramref name="phase"/>, synthesized on first request and cached.</summary>
    public static AudioStreamWav For(DayPhase phase)
    {
        if (!Cache.TryGetValue(phase, out var stream))
        {
            stream = Build(phase);
            Cache[phase] = stream;
        }

        return stream;
    }

    /// <summary>The musical character of one phase. Root is the drone's fundamental; <c>Third</c> is what
    /// makes it read major or minor; <c>Sparseness</c> is how many melody slots stay silent.</summary>
    private readonly record struct Mood(float RootHz, float ThirdSemitones, float Brightness, int Sparseness);

    private static Mood MoodFor(DayPhase phase) => phase switch
    {
        // Dawn: warmest and highest, major third, busiest melody — the day is opening.
        DayPhase.Morning => new Mood(RootHz: 130.81f, ThirdSemitones: 4f, Brightness: 1.0f, Sparseness: 2),

        // The party is out and the player is waiting. No third at all — a bare open fifth is neither
        // happy nor sad, which is the honest colour for "we do not know yet".
        DayPhase.Expedition => new Mood(RootHz: 116.54f, ThirdSemitones: 0f, Brightness: 0.75f, Sparseness: 4),
        DayPhase.Camp => new Mood(RootHz: 110.00f, ThirdSemitones: 0f, Brightness: 0.6f, Sparseness: 5),

        // Deepest and thinnest: they are as far down as they go.
        DayPhase.ExpeditionDeep => new Mood(RootHz: 98.00f, ThirdSemitones: 0f, Brightness: 0.5f, Sparseness: 6),

        // Night: minor third, lowest drone, sparse.
        DayPhase.Evening => new Mood(RootHz: 103.83f, ThirdSemitones: 3f, Brightness: 0.55f, Sparseness: 4),

        _ => new Mood(RootHz: 130.81f, ThirdSemitones: 4f, Brightness: 0.9f, Sparseness: 3),
    };

    private static AudioStreamWav Build(DayPhase phase)
    {
        var mood = MoodFor(phase);
        var buffer = new float[Synth.Samples(LoopSeconds)];

        // ── Drone: root, fifth, octave, and the mood's third when it has one. Each is snapped to a
        //    whole number of cycles across the loop so the waveform continues cleanly at the seam. ──
        AddDrone(buffer, SnapToLoop(mood.RootHz), 0.34f);
        AddDrone(buffer, SnapToLoop(mood.RootHz * 1.5f), 0.20f);
        AddDrone(buffer, SnapToLoop(mood.RootHz * 2f), 0.12f * mood.Brightness);
        if (mood.ThirdSemitones > 0f)
        {
            AddDrone(buffer, SnapToLoop(mood.RootHz * MathF.Pow(2f, mood.ThirdSemitones / 12f)), 0.14f);
        }

        // ── Air: very quiet low-passed noise, slowly breathing. Without it the drone sounds synthetic;
        //    with it the whole bed sits back and reads as a room rather than an oscillator. ──
        var air = new float[buffer.Length];
        for (var i = 0; i < air.Length; i++)
        {
            air[i] = Synth.Noise(i, seed: 101);
        }

        Synth.LowPass(air, 420f);
        for (var i = 0; i < buffer.Length; i++)
        {
            var breath = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * 2f * i / buffer.Length); // 2 cycles/loop
            buffer[i] += air[i] * 0.16f * breath;
        }

        // ── Melody: a pentatonic figure over a fixed slot grid, deterministically thinned. Pentatonic
        //    because every note of it consonates with a droning fifth, so no slot choice can ever land
        //    on a sour interval — which matters when the notes are picked by a hash rather than an ear. ──
        var scale = new[] { 0f, 2f, 4f, 7f, 9f, 12f, 14f, 16f };
        const int slots = 32;
        var slotSeconds = LoopSeconds / slots;
        for (var s = 0; s < slots; s++)
        {
            // Deterministic: same phase always yields the same tune. Uses the phase in the seed so the
            // five beds are melodically distinct rather than the same figure at different pitches.
            var pick = (int)((Synth.Noise(s, seed: 500 + (int)phase) + 1f) * 0.5f * 1000f);
            if (pick % 10 < mood.Sparseness)
            {
                continue; // a rest — silence is most of what makes a sparse bed feel unhurried
            }

            var degree = scale[pick % scale.Length];
            var hz = mood.RootHz * 4f * MathF.Pow(2f, degree / 12f);
            var at = s * slotSeconds;

            // Notes late in the loop are shortened so their tail cannot run past the seam.
            var maxLength = LoopSeconds - at - 0.05f;
            AddPluck(buffer, hz, at, MathF.Min(slotSeconds * 2.4f, maxLength), 0.11f * mood.Brightness);
        }

        Synth.Normalise(buffer, 0.5f); // a bed, deliberately well below the SFX peak
        return Synth.ToStream(buffer, loop: true);
    }

    /// <summary>
    /// Rounds <paramref name="hz"/> to the nearest frequency that completes a whole number of cycles in
    /// <see cref="LoopSeconds"/>. This is what makes the loop seamless: a drone left at an arbitrary
    /// frequency is mid-cycle when the buffer ends, so the jump back to sample 0 is a discontinuity —
    /// an audible click every loop, forever. The shift is at most ~0.02Hz here, far below anything the
    /// ear can detect as out of tune.
    /// </summary>
    private static float SnapToLoop(float hz) => MathF.Max(1f, MathF.Round(hz * LoopSeconds) / LoopSeconds);

    /// <summary>A drone partial: steady amplitude with a slow tremolo, no decay envelope. The tremolo
    /// rate is also an integer number of cycles per loop, for the same seam reason.</summary>
    private static void AddDrone(float[] buffer, float hz, float amplitude)
    {
        var step = 2f * MathF.PI * hz / Synth.SampleRate;
        for (var i = 0; i < buffer.Length; i++)
        {
            var tremolo = 0.85f + 0.15f * MathF.Sin(2f * MathF.PI * 3f * i / buffer.Length);
            buffer[i] += MathF.Sin(step * i) * amplitude * tremolo;
        }
    }

    /// <summary>A soft plucked note — fundamental plus a quiet second harmonic, exponential decay.</summary>
    private static void AddPluck(float[] buffer, float hz, float at, float length, float amplitude)
    {
        if (length <= 0.01f)
        {
            return;
        }

        var start = Synth.Samples(at);
        var end = Math.Min(buffer.Length, start + Synth.Samples(length));
        var halfLife = length * 0.30f;
        for (var i = start; i < end; i++)
        {
            var t = (i - start) / (float)Synth.SampleRate;
            var env = Synth.Decay(t, halfLife) * MathF.Min(1f, t * 200f); // tiny attack, no click
            buffer[i] += (MathF.Sin(2f * MathF.PI * hz * t) + 0.3f * MathF.Sin(4f * MathF.PI * hz * t))
                * amplitude * env;
        }
    }
}
