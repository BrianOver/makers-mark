using System;
using Godot;

namespace GodotClient.Audio;

/// <summary>
/// Small synthesis kit: build a float sample buffer, then hand it to <see cref="ToStream"/>.
///
/// <para><b>Why synthesized rather than committed audio files.</b> The project shipped with ZERO audio
/// assets — Brian's playtest asked "Music is not loaded?" and the honest answer was that there was no
/// audio in the game at all, anywhere. Generating it in code follows the precedent
/// <c>ForgePanel.MakeTone</c> already set, and it buys several things a folder of WAVs would not: no
/// repo weight (a minute of stereo PCM is ~10MB and Git LFS here only covers PNG/GLB), nothing to
/// license-track, and it works on a fresh clone with no asset pipeline step.</para>
///
/// <para><b>Deterministic (KTD5-flavoured).</b> Nothing here reads the engine RNG or a clock —
/// <see cref="Noise"/> is a pure hash of its sample index, so every build produces byte-identical
/// audio. That matters for the tests below it, and it means a cue never surprises you by being
/// quieter on one launch than another.</para>
///
/// <para>Everything is 16-bit mono at <see cref="SampleRate"/>. Mono because these are UI and world
/// cues with no spatialisation, and 22050 because none of this material has content above ~10kHz —
/// spending twice the memory on silence above the top harmonic would be pure waste.</para>
/// </summary>
public static class Synth
{
    /// <summary>Sample rate for every generated stream — matches <c>ForgePanel</c>'s existing SFX.</summary>
    public const int SampleRate = 22050;

    /// <summary>Deterministic value noise in [-1, 1] — a pure integer hash of <paramref name="index"/>,
    /// never an RNG. Two runs, two machines, and two builds all produce the same samples.</summary>
    public static float Noise(int index, int seed = 0)
    {
        unchecked
        {
            var h = (uint)(index * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    /// <summary>Number of samples for a duration in seconds (at least 1, so a zero-length cue still
    /// produces a valid stream rather than an empty buffer Godot would refuse).</summary>
    public static int Samples(float seconds) => Math.Max(1, (int)(SampleRate * seconds));

    /// <summary>Exponential decay from 1 toward 0 — the shape a struck or plucked thing actually has.
    /// <paramref name="halfLife"/> is in seconds.</summary>
    public static float Decay(float t, float halfLife) => MathF.Pow(0.5f, t / MathF.Max(halfLife, 1e-4f));

    /// <summary>A short linear fade at both ends of a buffer. Any buffer that starts or ends on a
    /// non-zero sample clicks audibly when played or looped; this is the cheapest way to guarantee it
    /// cannot. Applied in place.</summary>
    public static void DeClick(float[] buffer, float seconds = 0.004f)
    {
        var n = Math.Min(Samples(seconds), buffer.Length / 2);
        for (var i = 0; i < n; i++)
        {
            var gain = i / (float)n;
            buffer[i] *= gain;
            buffer[buffer.Length - 1 - i] *= gain;
        }
    }

    /// <summary>One-pole low-pass, applied in place — turns raw noise into something with a body
    /// instead of a hiss. <paramref name="cutoffHz"/> is approximate; this is flavour, not filter
    /// design.</summary>
    public static void LowPass(float[] buffer, float cutoffHz)
    {
        var dt = 1f / SampleRate;
        var rc = 1f / (2f * MathF.PI * MathF.Max(cutoffHz, 1f));
        var alpha = dt / (rc + dt);
        var last = 0f;
        for (var i = 0; i < buffer.Length; i++)
        {
            last += alpha * (buffer[i] - last);
            buffer[i] = last;
        }
    }

    /// <summary>Adds a sine partial across the whole buffer with an exponential decay envelope. The
    /// building block for struck metal, bells, and plucked strings — a handful of partials at
    /// inharmonic ratios with different half-lives is most of what makes something sound like an
    /// object rather than a beep.
    ///
    /// <para><b><paramref name="attack"/>, added for the town's per-venue cues.</b> Every existing
    /// caller passes 0 (the default), which reproduces the exact prior behaviour byte-for-byte — a
    /// partial at full envelope from sample 0. That instant onset is correct for struck-metal cues
    /// (the anvil, the coin) where the transient IS the sound. It reads as harsh on softer cues: the
    /// owner's "buildings sound too loud and harsh" turned out to include entering the tavern/market/
    /// noticeboard through the SAME zero-attack partials the forge uses. A short linear ramp (a few
    /// milliseconds) before the decay envelope takes over softens exactly those onsets without
    /// touching a single existing cue, because nothing else passes a non-zero value.</para>
    /// </summary>
    public static void AddPartial(float[] buffer, float hz, float amplitude, float halfLife, float phase = 0f, float attack = 0f)
    {
        var step = 2f * MathF.PI * hz / SampleRate;
        for (var i = 0; i < buffer.Length; i++)
        {
            var t = i / (float)SampleRate;
            var env = Decay(t, halfLife);
            if (attack > 0f)
            {
                env *= MathF.Min(1f, t / attack);
            }

            buffer[i] += MathF.Sin(step * i + phase) * amplitude * env;
        }
    }

    /// <summary>Scales <paramref name="buffer"/> down if it is louder than <paramref name="peak"/>
    /// (never boosts — this is a ceiling, not a target). A one-shot still wanting a raw peak ceiling
    /// (today, only <see cref="Cue.Bellows"/> and <see cref="Cue.EnterMarket"/> — see
    /// <c>SfxLibrary</c>'s own notes at those two cues) is the caller for this; every other one-shot
    /// targets loudness directly via <see cref="NormaliseRms"/> instead.
    ///
    /// <para><b>U-T4-3: no longer soft-clips.</b> This used to run every sample through
    /// <c>MathF.Tanh</c> unconditionally — including well BELOW peak, since gating the tanh call to
    /// "only when scaling down" does not help: <c>Normalise(buf, 0.55)</c> produces a post-gain peak of
    /// exactly 0.55, and <c>tanh(0.55)</c> is already 2.4% into the curve. A mathematically pure sine at
    /// that level measured 2.35% THD with the third harmonic at −32.6 dBc — audible grit on every pure
    /// tone in the game, and level alone could never fix it. Nothing here ever needed the soft limiter:
    /// <see cref="ToStream"/> already hard-clamps to ±1.0, and <c>AudioBuses</c> now carries a −1.0 dBTP
    /// limiter on Master, which is where a safety ceiling belongs — not silently warping every quiet
    /// cue on the way there.</para>
    /// </summary>
    public static void Normalise(float[] buffer, float peak = 0.85f)
    {
        var max = 0f;
        foreach (var s in buffer)
        {
            max = MathF.Max(max, MathF.Abs(s));
        }

        if (max > 1e-6f)
        {
            var gain = MathF.Min(1f, peak / max);
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= gain;
            }
        }
    }

    /// <summary>Below this, <see cref="MixBudget.ActiveWindowRms"/> is reporting "no measurable signal"
    /// (its own <c>SilenceFloorDb</c> is −120) rather than a real, quiet level — scaling toward a target
    /// against that reading would divide by (functionally) silence and produce a meaningless gain.</summary>
    private const float SilenceGuardDbfs = -100f;

    /// <summary>Scales <paramref name="buffer"/> so its own <see cref="MixBudget.ActiveWindowRms"/>
    /// lands at <paramref name="targetRmsDbfs"/> — a target, not a ceiling: unlike <see cref="Normalise"/>
    /// this may boost as freely as it cuts, because RMS-targeting for perceived loudness has no reason to
    /// only go one direction.
    ///
    /// <para><b>U-T4-3: the unit that spends the budget <c>MixBudget</c> (U-T4-2) wrote down.</b> Every
    /// one-shot cue used to target a raw PEAK (0.14 to 0.6, chosen by ear, cue by cue, with no shared
    /// scale) — which is exactly how <c>Bell</c> ended up 22 to 42 dB louder than the bed it rang over:
    /// nothing ever measured what "peak 0.55" actually sounds like relative to "peak 0.26." Levelling to
    /// the SAME measurement the census checks (<see cref="MixBudget.ActiveWindowRms"/>, not a plain
    /// whole-buffer RMS) means the gain this applies and the number
    /// <c>MixBudgetCensusTests.EveryCue_LandsInItsBudgetBand</c> checks against are the same idea of
    /// "loud," not two that happen to usually agree.</para>
    ///
    /// <para>No soft-clipping here either, for the same reason <see cref="Normalise"/> lost its own: a
    /// boost large enough to threaten <see cref="ToStream"/>'s ±1.0 hard clamp would mean a cue's peak
    /// target and its category's RMS target had drifted wildly apart, which is a design defect in the
    /// cue's own recipe, not something a limiter should quietly paper over.</para>
    /// </summary>
    public static void NormaliseRms(float[] buffer, float targetRmsDbfs)
    {
        var currentDb = MixBudget.ActiveWindowRms(buffer, SampleRate);
        if (currentDb <= SilenceGuardDbfs)
        {
            return; // no measurable signal to level toward a target -- leave it as silence, not noise.
        }

        var gain = MathF.Pow(10f, (targetRmsDbfs - currentDb) / 20f);
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= gain;
        }
    }

    /// <summary>Packs a float buffer into a 16-bit mono <see cref="AudioStreamWav"/>. When
    /// <paramref name="loop"/> is set the whole stream is marked as a forward loop — used by the music
    /// bed, whose buffer is built so its end meets its beginning.</summary>
    public static AudioStreamWav ToStream(float[] buffer, bool loop = false)
    {
        var data = new byte[buffer.Length * 2];
        for (var i = 0; i < buffer.Length; i++)
        {
            var s16 = (short)(Math.Clamp(buffer[i], -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(s16 & 0xFF);
            data[i * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? buffer.Length : 0,
        };
    }
}
