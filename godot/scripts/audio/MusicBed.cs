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
/// <para><b>U2 update: it landed, for three of five phases. U4 closed the rest.</b>
/// <c>AudioDirector</c>'s composed-track table preferred a real track over <see cref="For"/> for
/// Evening, Camp, and both Expedition phases first (U2), then for Morning too once a generated
/// Morning track existed to replace it (U4: "day music is not a track, it's the synthesized Morning
/// bed" — the owner rejected this class's own Morning mood a third time, which is what finally
/// generated one instead of retuning again). This class did not change to make room for either
/// landing; it stayed exactly what its own header always said it would become: the thing being
/// replaced. It remains the ONLY voice for <see cref="Underground"/> (no mine track this round) and
/// is still the ladder's fallback everywhere else — a missing or not-yet-pulled composed asset
/// degrades here, not to silence.</para>
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
    /// <para>Was 24s, then 60s, and the owner said it again on the very next playtest: "the track repeats
    /// to[o] quickly, expand". 60s was already 2.5x the original and still not enough — the honest reading
    /// is that HE is the one who gets to say when a loop is long enough, not a single doubling. 120s is a
    /// further 2x (5x the original 24s), and because <see cref="MelodySlots"/> scales with it, that is 5x
    /// the distinct notes in the tune, not the same figure stretched thinner.</para>
    ///
    /// <para>Cost was the reason it did not go further already ("tripling this triples a hitch"), but that
    /// claim had never actually been measured against real numbers — it was a guess dressed as a
    /// constraint. Measured with a throwaway console harness that renders the exact same synthesis code
    /// this file ships (see the U-audio-2 PR body for the harness): a cold-process worst case at 120s is
    /// ~300ms, and even 180s only reaches ~440ms — both comfortably inside the 2.5s
    /// <see cref="AudioDirector.CrossfadeSeconds"/> window that hides the hitch, and the in-process JIT-warm
    /// cost during actual play (this is not the first synth call the process makes) is expected to be
    /// lower still. 120s is chosen over 180s anyway: it is already double what shipped, doubling again on
    /// the strength of a synthetic benchmark alone felt like the same mistake in the other direction.
    /// If "still too short" comes back a third time, the number to move next is this one, with room to
    /// spare on the measured budget.</para>
    /// </summary>
    public const float LoopSeconds = 120f;

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

        // Cavern air (U7, R7: "vigil music good but too much background static" — this layer IS
        // static ON PURPOSE, just tuned too hot). Factored into UndergroundAirLayer() below so
        // AudioTests can measure its energy SHARE of the full theme straight off this real buffer —
        // never a frozen side-copy of the amplitude/cutoff that would stop tracking a future change
        // here.
        var air = UndergroundAirLayer();
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] += air[i];
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

    /// <summary>
    /// U7 (2026-08-02 shell-and-audio plan, R7/R8): the Underground bed's own "cavern air" noise
    /// layer — low-passed noise breathing on one slow cycle per loop, pulled out of
    /// <see cref="Underground"/> into its own method so <c>AudioTests</c> can measure its RMS share
    /// of the full theme directly off a REAL buffer, not a re-typed copy of the amplitude/cutoff that
    /// would silently stop reacting the next time either number moves.
    ///
    /// <para>Amplitude 0.13 -&gt; 0.07 and low-pass 340Hz -&gt; 260Hz: darker AND quieter — measured
    /// (throwaway console harness, same method <see cref="LoopSeconds"/>'s own doc used) to roughly
    /// halve this layer's RMS share of the whole theme (~7.2% -&gt; ~3.3%). Drips/pulse/drones are
    /// untouched: he said "good but," not "replace" (U7's own Approach).</para>
    /// </summary>
    public static float[] UndergroundAirLayer()
    {
        var length = Synth.Samples(LoopSeconds);
        var air = new float[length];
        for (var i = 0; i < length; i++)
        {
            air[i] = Synth.Noise(i, seed: 909);
        }

        Synth.LowPass(air, 260f); // U7: 340 -> 260Hz, darker

        for (var i = 0; i < length; i++)
        {
            var breath = 0.55f + 0.45f * MathF.Sin(2f * MathF.PI * i / length); // one slow cycle
            air[i] *= 0.07f * breath; // U7: 0.13 -> 0.07, the actual cut
        }

        return air;
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
        //    Weighted UP the harmonic series, not down, and pushed further this pass. The FIRST fix moved
        //    the fundamental from 0.34 (louder than the fifth) down to 0.17 and called that "lower the
        //    base" — but the owner's next playtest said "too loud" about the bed again, so 0.17 was still
        //    the loudest partial in the chord and still read as bass-forward. Root is down again to 0.115
        //    (from 0.17: measured 15-20% relative drop in the sub-150Hz share across all five phases, see
        //    the U-audio-2 PR body) and the octave picks up the difference (0.19 -> 0.225, scaled by
        //    Brightness same as before) so the chord and the perceived pitch are unchanged — only the
        //    chest weight moves. AudioTests pins the low-band share so this cannot silently drift back,
        //    and its ceiling was tightened alongside this change rather than left loose enough to hide a
        //    future regression back toward the old numbers. ──
        AddDrone(buffer, SnapToLoop(mood.RootHz), 0.115f);
        AddDrone(buffer, SnapToLoop(mood.RootHz * 1.5f), 0.20f);
        AddDrone(buffer, SnapToLoop(mood.RootHz * 2f), 0.225f * mood.Brightness);
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

        // 0.5 -> 0.42 (-1.5dB): the second "too loud" report was about this bed specifically, not just
        // the sub-150Hz content above, so the fix is both a spectral rebalance AND a straight level cut,
        // not one standing in for the other. Measured: every phase's integrated LUFS dropped ~1.4-1.9 LU
        // from this change plus the drone rebalance together (see PR body) — never louder than before it,
        // which AudioDirector.MusicDb's own headroom assumes.
        Synth.Normalise(buffer, 0.42f); // a bed, deliberately well below the SFX peak
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
