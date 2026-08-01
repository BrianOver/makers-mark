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
/// <para><b>U2 update: it landed, for three of five phases.</b> <c>AudioDirector</c>'s
/// composed-track table now prefers a real track over <see cref="For"/> for Evening, Camp, and both
/// Expedition phases — this class did not change to make room for that; it stayed exactly what its
/// own header always said it would become for those phases: the thing being replaced. It remains the
/// ONLY voice for Morning (no composed track exists yet) and for <see cref="Underground"/>, and it is
/// still the ladder's fallback everywhere else — a missing or not-yet-pulled composed asset degrades
/// here, not to silence. The three-tracks-of-five gap is deliberate (KTD-C: no new generation in this
/// unit), not an oversight.</para>
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
    /// <summary>
    /// Loop length.
    ///
    /// <para>Was 24s, which the owner heard immediately: "its a little loud and loops too quickly".
    /// 24 seconds is short enough that the ear memorises the figure and then hears the seam as an event,
    /// which is worse than a plain drone. 60s is 2.5x the cycle and — because <see cref="MelodySlots"/>
    /// scales with it — 2.5x the number of distinct notes, so the tune itself is longer rather than the
    /// same tune stretched.</para>
    ///
    /// <para>Cost is the reason it is not longer still: a bed is ~1.3M samples per 24s and is synthesized
    /// on the main thread the first time its phase comes up, so tripling this triples a hitch the player
    /// can feel. It lands inside the 2.5s crossfade that requested it, which is what makes 60s safe and
    /// 180s not.</para>
    /// </summary>
    public const float LoopSeconds = 60f;

    /// <summary>
    /// Melody slots per loop, scaled to hold note DENSITY constant as <see cref="LoopSeconds"/> changes.
    ///
    /// <para>A fixed slot count would have turned the longer loop into the same number of notes spread
    /// thinner — a sparser bed, not a longer one. Tying the two together means "make the track longer" is a
    /// single-constant change and cannot silently alter the pacing.</para>
    /// </summary>
    private static int MelodySlots => (int)MathF.Round(LoopSeconds / 0.75f);

    private static readonly Dictionary<DayPhase, AudioStreamWav> Cache = new();

    private static AudioStreamWav? _underground;

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

    /// <summary>
    /// The Mine's own theme, for while the player is WATCHING the raid rather than standing in the town
    /// — see <c>AudioDirector.SetScene</c>.
    ///
    /// <para>Brian's playtest: "where are the visuals to follow their adventure??" and "unclear what to
    /// do during the expedition phase". Part of why the Depths read as inert is that it sounds exactly
    /// like the town does. This is the same construction as a phase bed, pitched a fourth below the
    /// deepest of them, with the melody removed entirely and replaced by a slow irregular drip and a
    /// low pulse. No tune, because down here nobody is playing one — the point is that it feels like a
    /// different PLACE, not a different mood.</para>
    /// </summary>
    public static AudioStreamWav Underground()
    {
        if (_underground is not null)
        {
            return _underground;
        }

        var buffer = new float[Synth.Samples(LoopSeconds)];
        const float root = 73.42f; // D2 — a fourth under the Deep bed's 98Hz

        // Same weighting change as the phase beds, and it matters most here: at D2 a 0.40 fundamental was
        // the single boomiest thing in the game. The octave carries the pitch instead.
        AddDrone(buffer, SnapToLoop(root), 0.19f);
        AddDrone(buffer, SnapToLoop(root * 1.5f), 0.16f);
        AddDrone(buffer, SnapToLoop(root * 2f), 0.17f);
        // A tritone, very quiet: the one deliberately unsettled interval in the whole score. It never
        // resolves because the Mine never does.
        AddDrone(buffer, SnapToLoop(root * 1.414f), 0.07f);

        // Cavern air: darker and heavier than the town's, and breathing more slowly.
        var air = new float[buffer.Length];
        for (var i = 0; i < air.Length; i++)
        {
            air[i] = Synth.Noise(i, seed: 909);
        }

        Synth.LowPass(air, 340f);
        for (var i = 0; i < buffer.Length; i++)
        {
            var breath = 0.55f + 0.45f * MathF.Sin(2f * MathF.PI * i / buffer.Length); // one slow cycle
            buffer[i] += air[i] * 0.13f * breath;
        }

        // Water, somewhere. Deterministically irregular spacing — an exactly periodic drip reads as a
        // metronome, and a metronome reads as music, which is what this is avoiding.
        // Count scales with the loop so the drips stay ~2.5s apart instead of thinning out to one every
        // six seconds as the loop lengthens.
        var drips = (int)MathF.Round(LoopSeconds / 2.5f);
        for (var d = 0; d < drips; d++)
        {
            var jitter = (Synth.Noise(d, seed: 4242) + 1f) * 0.5f; // 0..1
            var at = (d + jitter) * (LoopSeconds / (drips + 1));
            AddDrip(buffer, at, 1180f + jitter * 600f);
        }

        // A pulse on the beat of a slow heart — the only thing down here with a tempo. Kept at one every
        // two seconds as the loop lengthens, rather than a fixed count that would slow to a crawl.
        var pulses = (int)MathF.Round(LoopSeconds / 2f);
        for (var p = 0; p < pulses; p++)
        {
            AddPulse(buffer, at: p * (LoopSeconds / pulses), hz: root);
        }

        Synth.Normalise(buffer, 0.5f);
        _underground = Synth.ToStream(buffer, loop: true);
        return _underground;
    }

    /// <summary>A single water drop: a short bright partial with a fast pitch drop, which is what makes
    /// a sine read as a droplet instead of a bleep.</summary>
    private static void AddDrip(float[] buffer, float at, float hz)
    {
        var start = Synth.Samples(at);
        var end = Math.Min(buffer.Length, start + Synth.Samples(0.18f));
        for (var i = start; i < end; i++)
        {
            var t = (i - start) / (float)Synth.SampleRate;
            var sweep = hz * (1f - t * 1.6f); // falls away as it decays
            buffer[i] += MathF.Sin(2f * MathF.PI * MathF.Max(sweep, 40f) * t) * 0.10f * Synth.Decay(t, 0.035f);
        }
    }

    /// <summary>
    /// A soft low thud.
    ///
    /// <para>Quieter than it was (0.16 -> 0.09), and callers now pass the root rather than half of it. At
    /// D2 halved that was a ~37Hz tone: below most speakers' useful range, so on small ones it is inaudible
    /// wasted headroom and on large ones it is pure chest thump with no pitch. Neither is what "a slow
    /// heartbeat under the mine" needed.</para>
    /// </summary>
    private static void AddPulse(float[] buffer, float at, float hz)
    {
        var start = Synth.Samples(at);
        var end = Math.Min(buffer.Length, start + Synth.Samples(0.45f));
        for (var i = start; i < end; i++)
        {
            var t = (i - start) / (float)Synth.SampleRate;
            buffer[i] += MathF.Sin(2f * MathF.PI * hz * t) * 0.09f * Synth.Decay(t, 0.11f);
        }
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
        //    whole number of cycles across the loop so the waveform continues cleanly at the seam.
        //
        //    Weighted UP the harmonic series, not down. The owner's note was "lower the base" — with the
        //    fundamental as the loudest partial (it was 0.34, nearly double the fifth) the bed read as
        //    boom rather than tone, and low frequencies are exactly what a small speaker reproduces
        //    worst and a big one reproduces overwhelmingly. Moving that energy to the octave keeps the
        //    same chord and the same perceived pitch while taking the weight out of it: the ear infers
        //    the fundamental from the upper partials, so this sounds like the same note, quieter in the
        //    chest. AudioTests pins the low-band share so this cannot silently drift back. ──
        AddDrone(buffer, SnapToLoop(mood.RootHz), 0.17f);
        AddDrone(buffer, SnapToLoop(mood.RootHz * 1.5f), 0.20f);
        AddDrone(buffer, SnapToLoop(mood.RootHz * 2f), 0.19f * mood.Brightness);
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

        // 520Hz rather than 420Hz, and quieter: low-passed noise is broadband weight, so it was adding
        // to the same rumble the drone was. Opening the filter moves it from "rumble" to "air", which is
        // what it was for.
        Synth.LowPass(air, 520f);
        for (var i = 0; i < buffer.Length; i++)
        {
            var breath = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * 2f * i / buffer.Length); // 2 cycles/loop
            buffer[i] += air[i] * 0.11f * breath;
        }

        // ── Melody: a pentatonic figure over a fixed slot grid, deterministically thinned. Pentatonic
        //    because every note of it consonates with a droning fifth, so no slot choice can ever land
        //    on a sour interval — which matters when the notes are picked by a hash rather than an ear. ──
        var scale = new[] { 0f, 2f, 4f, 7f, 9f, 12f, 14f, 16f };
        var slots = MelodySlots;
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
