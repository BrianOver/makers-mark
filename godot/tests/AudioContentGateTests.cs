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
/// U-T4-7: five content gates on the four composed beds, replacing the byte-identity SHA-256
/// fingerprint pin deleted from <c>AudioTests</c> (<c>ApprovedTrackHashes</c> /
/// <c>EveryComposedTrack_MatchesItsApprovedLoudnessFingerprint</c>) in the same PR.
///
/// <para><b>Why the hash pin had to die, not just be extended.</b> A hash census encodes "these exact
/// bytes were vouched for" — it proves a file has not changed, and says nothing about whether the file
/// is any good. Worse, it actively BLOCKS the repair this program needs: a fixed file has new bytes by
/// definition, so the fingerprint test would fail the moment U-T4-8 trims and re-folds night-still and
/// quest-wait, for the wrong reason (bytes changed) rather than the right one (do the new bytes measure
/// clean). These five gates measure the actual documented defects instead — mostly-silence, a dead
/// tail, a loop-seam level lurch, an isolated transient, and an impulse train with no midrange tone —
/// so a repair is provably an improvement, not just a byte diff nobody can evaluate.</para>
///
/// <para><b>Calibrated against the real shipped tracks, never invented.</b> Every ceiling below sits
/// strictly between the measured value of a clean/repaired track and the measured value of the actual
/// defect it targets — see each gate's own doc for the numbers, and
/// <see cref="PendingContentExemptions"/> for exactly which track fails which gate today and why. That
/// exemption set is pinned (<see cref="ThePendingContentExemptionCount_IsThePinnedNumber"/>), the same
/// discipline <c>MixBudgetCensusTests</c> holds its own exemptions to: a future PR removing an entry
/// (because U-T4-8 repaired that track) is a red-then-reviewed diff, never a silent shrink.</para>
///
/// <para><b>Unweighted, windowed RMS — never integrated LUFS.</b> LUFS gates absolute- and relative-
/// loudness blocks, which is exactly what makes it blind to dead air: town-dusk measures a respectable
/// integrated LUFS while sitting silent for the bulk of its 60 seconds. Every gate here instead windows
/// the RMS directly (the mostly-silence gate reuses <see cref="MixBudget.ActiveWindowRms"/>'s own
/// 50ms/40dB active-range definition) or measures one specific short EVENT (a loop edge, a transient) —
/// never a single number averaged over the whole file.</para>
///
/// <para><b>The spectral gate uses one-pole filters, not an FFT.</b> <see cref="Synth.LowPass"/>
/// already exists and uses exactly this construction for the game's own synthesized SFX, but it is
/// hardcoded to <see cref="Synth.SampleRate"/> (22050Hz, for synthesized one-shots) — wrong for a
/// composed bed decoding at the engine's own <see cref="AudioServer.GetMixRate"/> (48kHz), so this
/// duplicates the same one-pole formula parameterised by the actual sample rate rather than silently
/// misapplying <c>Synth.LowPass</c> at the wrong cutoff.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AudioContentGateTests
{
    // ---- decode once per track, cached for the whole run (four short files; cheap either way,
    //      but five gates each iterating four tracks would otherwise decode the same file 20 times) --

    private static readonly Dictionary<string, float[]> DecodedCache = new(StringComparer.Ordinal);

    private static int SampleRate => (int)AudioServer.GetMixRate();

    private static float[] Decode(DayPhase phase, string id)
    {
        if (DecodedCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var stream = AudioDirector.LoadComposedTrackForCensus(phase);
        AssertThat(stream)
            .OverrideFailureMessage($"{phase}'s composed track '{id}' would not load — cannot content-gate it.")
            .IsNotNull();

        var sampleRate = SampleRate;
        var nominalSamples = (int)Math.Round(stream!.GetLength() * sampleRate);
        var frameBudget = nominalSamples + sampleRate; // +1s margin against UNDER-decoding only — see below
        var frames = DecodeUpTo(stream, frameBudget);
        AssertThat(frames.Length)
            .OverrideFailureMessage($"{phase}'s composed track '{id}' decoded 0 frames via MixAudio.")
            .IsGreater(0);

        var mono = CombineStereo(frames);

        // Every composed track ships with AudioDirector.LoadComposed's ogg.Loop = true (the same
        // stream the game actually plays), so a decode request for MORE than the track's own nominal
        // length does not return silence or stop early -- it WRAPS to loop_offset (0) and keeps
        // producing audio, i.e. the head played again. The +1s margin above exists only as insurance
        // against a decode that comes up slightly SHORT of nominal length (container rounding); it
        // must never be allowed to run past nominal length, or every gate that looks at a track's own
        // TAIL is silently measuring wrapped HEAD content instead. This is exactly how a real engine
        // run once measured town-dusk's loop-seam lurch at 0.0dB (tail == head, because "tail" was
        // literally re-decoded head) when the track's true, un-wrapped tail sits 34.5dB below its
        // head -- a green gate over a defect that is still fully present. Truncating here is the fix:
        // decode with margin to guard against undershoot, then hard-cap at the stream's own length so
        // every gate below only ever sees ONE clean pass through the real content.
        if (mono.Length > nominalSamples)
        {
            mono = mono[..nominalSamples];
        }

        DecodedCache[id] = mono;
        return mono;
    }

    /// <summary>Same decode-without-playing technique as <c>AudioTests.DecodeUpTo</c> /
    /// <c>MixBudgetCensusTests.DecodeUpTo</c>, duplicated here for the same reason those two duplicate
    /// each other's copy: each suite's helpers are private to itself.</summary>
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
                break;
            }

            collected.AddRange(chunk);
        }

        playback.Stop();
        return collected.ToArray();
    }

    /// <summary>Mean-channel-POWER combine (<c>sqrt((L²+R²)/2)</c>), never a raw L+R sum — a plain sum
    /// is the exact mono-sum artefact that produced this project's false "+1.63 dBFS clipping" reading
    /// (see <c>MixBudgetCensusTests.CombineStereo</c>, duplicated here for the same reason as
    /// <see cref="DecodeUpTo"/> above).</summary>
    private static float[] CombineStereo(Vector2[] frames)
    {
        var mono = new float[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            mono[i] = MathF.Sqrt((frames[i].X * frames[i].X + frames[i].Y * frames[i].Y) * 0.5f);
        }

        return mono;
    }

    private static double ToDb(double linear) => linear > 1e-9 ? 20.0 * Math.Log10(linear) : -120.0;

    /// <summary>Non-overlapping-by-hop windowed RMS in dB across the whole signal — the shared building
    /// block every gate below specialises with its own window/hop, mirroring
    /// <see cref="MixBudget.ActiveWindowRms"/>'s own sliding-window construction (a hop smaller than the
    /// window for finer boundary resolution) but returning the FULL profile rather than the single
    /// active-window summary that method reduces to.</summary>
    private static (double[] Times, double[] Db) WindowedRmsDb(float[] pcm, double windowSeconds, double hopSeconds)
    {
        var sampleRate = SampleRate;
        var window = Math.Max(1, (int)Math.Round(sampleRate * windowSeconds));
        var hop = Math.Max(1, (int)Math.Round(sampleRate * hopSeconds));

        var starts = new List<int>();
        for (var s = 0; s + window <= pcm.Length; s += hop)
        {
            starts.Add(s);
        }

        if (starts.Count == 0)
        {
            starts.Add(0);
        }

        var times = new double[starts.Count];
        var db = new double[starts.Count];
        for (var i = 0; i < starts.Count; i++)
        {
            var s = starts[i];
            var end = Math.Min(pcm.Length, s + window);
            double sumSq = 0;
            for (var j = s; j < end; j++)
            {
                sumSq += (double)pcm[j] * pcm[j];
            }

            var rms = Math.Sqrt(sumSq / Math.Max(1, end - s));
            times[i] = s / (double)sampleRate;
            db[i] = ToDb(rms);
        }

        return (times, db);
    }

    private static double RmsDbOfRange(float[] pcm, int start, int end)
    {
        start = Math.Max(0, start);
        end = Math.Min(pcm.Length, end);
        double sumSq = 0;
        for (var i = start; i < end; i++)
        {
            sumSq += (double)pcm[i] * pcm[i];
        }

        return ToDb(Math.Sqrt(sumSq / Math.Max(1, end - start)));
    }

    /// <summary>One-pole low-pass — the exact construction <see cref="Synth.LowPass"/> uses for the
    /// game's own synthesized SFX, duplicated (not called) here because that method hardcodes
    /// <see cref="Synth.SampleRate"/> (22050Hz) and a composed bed decodes at the engine's own,
    /// different mix rate; applying it unmodified would silently miscalibrate the cutoff. Returns a new
    /// array (the original signal is still needed alongside it for the highpass-residual gate below).</summary>
    private static float[] OnePoleLowPass(float[] buffer, float cutoffHz, int sampleRate)
    {
        var dt = 1f / sampleRate;
        var rc = 1f / (2f * MathF.PI * cutoffHz);
        var alpha = dt / (rc + dt);
        var output = new float[buffer.Length];
        var last = 0f;
        for (var i = 0; i < buffer.Length; i++)
        {
            last += alpha * (buffer[i] - last);
            output[i] = last;
        }

        return output;
    }

    // ---- the pending exemptions --------------------------------------------------------------

    private enum Gate
    {
        MostlySilent,
        DeadTail,
        LoopSeamLevelLurch,
        IsolatedTransient,
        ImpulseTrainNoTone,
    }

    /// <summary>
    /// Every (track, gate) pair this build already knows fails, and why — the same
    /// <c>MixBudget.PendingExemptions</c> discipline applied to these five gates instead of the RMS
    /// budget. <see cref="ThePendingContentExemptionCount_IsThePinnedNumber"/> pins the count.
    ///
    /// <para><b>day-first-light (Dawn) and town-dusk (Evening) are both unrepairable by trim+fold</b> —
    /// U-T4-9 (regeneration, not this unit) is their only fix. Measured (this build's own numbers,
    /// PCM decoded through the exact path the game plays through):</para>
    /// <list type="bullet">
    /// <item>day-first-light: 92.1% of its energy sits below 150Hz and 0.055% above 6kHz — an impulse
    /// train of low thuds with no midrange tone at all. <see cref="Gate.ImpulseTrainNoTone"/>.</item>
    /// <item>town-dusk: 96.8% of its 50ms windows sit ≥40dB under its own loudest window (58.1 of 60.0
    /// seconds effectively silent) — <see cref="Gate.MostlySilent"/>. Its loop wrap jumps +34.5dB
    /// (head 1s at -32.5dBFS, tail 1s at -67.0dBFS) — <see cref="Gate.LoopSeamLevelLurch"/> — but this
    /// track is NOT a U-T4-8 repair target: with only ~7s of real content in 60s, trimming plus a 4s
    /// fold would leave a ~2.5s loop, not a fix. It also carries a broadband click at t≈57.3s, 16.1dB
    /// above its own local neighbourhood (3.7% of that transient's own energy sits above 6kHz, versus
    /// 0.0–0.01% for every other track's loudest moment — a genuinely "full-bandwidth" click, not a
    /// musical hit) — <see cref="Gate.IsolatedTransient"/>.</item>
    /// </list>
    ///
    /// <para><b>night-still (Camp) and quest-wait (Expedition/ExpeditionDeep) WERE this program's
    /// U-T4-8 repair targets, and are now fixed.</b> Both trimmed (51.5s and 49.5s respectively) with a
    /// 4s equal-power (<c>ffmpeg acrossfade</c>, <c>qsin</c>/<c>qsin</c> curves — sin²+cos²=1, constant
    /// power through the whole fold, never a linear fade's ~3dB mid-fade dip) head-to-tail fold, so the
    /// wrap is level-continuous BY CONSTRUCTION rather than by inspection. Before repair: night-still's
    /// last 8.8s sat ≥30dB under its own loudest 0.5s window (<see cref="Gate.DeadTail"/>) and its wrap
    /// jumped +33.1dB (head -31.3dBFS, tail -64.4dBFS, <see cref="Gate.LoopSeamLevelLurch"/>); quest-
    /// wait's last 10.5s were dead and its wrap jumped +23.1dB (head -41.7dBFS, tail -64.8dBFS) — note
    /// 60.0 − 51.5 = 8.5s and 60.0 − 49.5 = 10.5s, both matching their own measured dead tail almost
    /// exactly, which is exactly why those two trim points were chosen. After repair: night-still's
    /// dead tail is 0.0s and its wrap lurch is 3.4dB; quest-wait's dead tail is 0.0s and its wrap lurch
    /// is 4.5dB — both now comfortably inside every gate's ceiling, and their four exemption entries
    /// are removed here, dropping the pinned count from 8 to 4.</para>
    /// </summary>
    private static readonly HashSet<(string Track, Gate Gate)> PendingContentExemptions = new()
    {
    };

    private const int PinnedContentExemptionCount = 0;

    [TestCase]
    public void ThePendingContentExemptionCount_IsThePinnedNumber()
    {
        AssertInt(PendingContentExemptions.Count)
            .OverrideFailureMessage(
                $"PendingContentExemptions has {PendingContentExemptions.Count} entries; this test pins " +
                $"{PinnedContentExemptionCount}. U-T4-9 (regenerating day-first-light/town-dusk) must " +
                "remove its entries and lower this number in the same PR; a newly-drifted failure adds " +
                "an entry and raises it. Never let the set change silently.")
            .IsEqual(PinnedContentExemptionCount);
    }

    /// <summary>Staleness guard the other direction, <c>MixBudgetCensusTests</c>-style: every exemption
    /// must name a track id that actually exists in <see cref="AudioDirector.ComposedTrackIds"/> right
    /// now, so a rename leaves no stale entry quietly exempting nothing.</summary>
    [TestCase]
    public void EveryPendingContentExemption_NamesARealComposedTrack()
    {
        var validIds = new HashSet<string>(
            AudioDirector.ComposedTrackIds.Values.Select(id => $"Track.{id}"), StringComparer.Ordinal);

        var unknown = PendingContentExemptions.Where(e => !validIds.Contains(e.Track)).ToList();
        AssertInt(unknown.Count)
            .OverrideFailureMessage(
                "PendingContentExemptions names tracks that do not exist: " +
                string.Join(", ", unknown.Select(e => $"{e.Track}/{e.Gate}")))
            .IsEqual(0);
    }

    private void AssertGate(DayPhase phase, string id, Gate gate, bool inBand, string failureDetail)
    {
        var key = $"Track.{id}";
        var exempted = PendingContentExemptions.Contains((key, gate));

        AssertBool(inBand || exempted)
            .OverrideFailureMessage(
                $"{phase}'s '{id}' fails {gate}: {failureDetail} — and is not listed in " +
                "PendingContentExemptions. Either this track regressed and needs a new (pinned) " +
                "exemption, or it is a brand-new track never gated before.")
            .IsTrue();

        if (exempted)
        {
            AssertBool(inBand)
                .OverrideFailureMessage(
                    $"'{key}' is listed in PendingContentExemptions for {gate} but now passes ({failureDetail}). " +
                    "It was fixed — remove the entry and lower ThePendingContentExemptionCount.")
                .IsFalse();
        }
    }

    // ---- Gate 1: mostly-silence -----------------------------------------------------------------

    /// <summary>
    /// Ceiling 50% of a track's 50ms windows may sit ≥40dB under its own loudest window (the same
    /// "active" definition <see cref="MixBudget.ActiveWindowRms"/> uses, just counting the inactive
    /// side) before it counts as mostly-silent. Measured: day-first-light 36.2%, night-still 22.0%,
    /// quest-wait 19.3% — all comfortably under. town-dusk measures 96.8% (58.1 of 60.0 seconds) and
    /// is the one entry in <see cref="PendingContentExemptions"/> for this gate.
    /// </summary>
    private const double SilenceFractionCeiling = 0.50;

    [TestCase]
    public void NoComposedTrack_IsMostlySilent()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var pcm = Decode(phase, id);
            var (_, db) = WindowedRmsDb(pcm, windowSeconds: 0.050, hopSeconds: 0.0125);
            var loudest = db.Max();
            var threshold = loudest - 40.0;
            var silentFraction = db.Count(d => d < threshold) / (double)db.Length;

            AssertGate(phase, id, Gate.MostlySilent, silentFraction < SilenceFractionCeiling,
                $"{silentFraction * 100:0.0}% of windows sit below {threshold:0.0}dB (ceiling {SilenceFractionCeiling * 100:0}%)");
        }
    }

    // ---- Gate 2: dead tail -----------------------------------------------------------------------

    /// <summary>
    /// Ceiling 3.0s of trailing content may sit ≥30dB under the track's own loudest 0.5s window before
    /// it counts as a dead tail. Measured: day-first-light 1.2s, town-dusk 2.5s — both under. Before
    /// U-T4-8's repair, night-still measured 8.8s and quest-wait 10.5s; both now measure 0.0s — the
    /// trim itself removes the dead tail outright, the 4s equal-power fold is what fixes the level lurch
    /// at the new, shorter wrap point (see <see cref="Gate.LoopSeamLevelLurch"/>'s own doc).
    /// </summary>
    private const double DeadTailCeilingSeconds = 3.0;

    [TestCase]
    public void NoComposedTrack_HasADeadTail()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var pcm = Decode(phase, id);
            var (times, db) = WindowedRmsDb(pcm, windowSeconds: 0.5, hopSeconds: 0.25);
            var loudest = db.Max();
            var deadThreshold = loudest - 30.0;
            var duration = pcm.Length / (double)SampleRate;

            var deadSeconds = 0.0;
            for (var i = db.Length - 1; i >= 0; i--)
            {
                if (db[i] < deadThreshold)
                {
                    deadSeconds = duration - times[i];
                }
                else
                {
                    break;
                }
            }

            AssertGate(phase, id, Gate.DeadTail, deadSeconds < DeadTailCeilingSeconds,
                $"its last {deadSeconds:0.0}s sit ≥30dB under its loudest 0.5s window (ceiling {DeadTailCeilingSeconds:0.0}s)");
        }
    }

    // ---- Gate 3: loop-seam level lurch -----------------------------------------------------------

    /// <summary>
    /// Ceiling 6.0dB between the RMS of a track's first second and its last second — a loop that wraps
    /// cleanly should meet itself at roughly the same level. Measured before repair: day-first-light
    /// 0.0dB (both edges happen to sit in that track's own dead space — not a meaningful "clean wrap",
    /// just two quiet passages). town-dusk jumps 34.5dB, night-still 33.1dB, quest-wait 23.1dB.
    ///
    /// <para><b>The ceiling is calibrated against the REPAIRED files, not guessed.</b> U-T4-8's 4s
    /// equal-power fold does not make the two 1-second windows byte-identical — a real musical bed
    /// still has short-term dynamics inside either second, and the crossfade only blends the LAST 4s of
    /// the file, not the first 4s it blends toward — so a small residual remains: night-still measures
    /// 3.4dB and quest-wait 4.5dB after repair (down from 33.1dB and 23.1dB — a 7–10x reduction, and
    /// nowhere near the double-digit jump a listener actually hears as a lurch). 6.0dB sits comfortably
    /// above both repaired numbers and stays 4–6x tighter than either original defect; town-dusk (still
    /// 34.5dB, not a U-T4-8 target — see <see cref="PendingContentExemptions"/>'s own doc) stays
    /// exempted.</para>
    /// </summary>
    private const double LoopSeamLurchCeilingDb = 6.0;
    private const double LoopSeamEdgeSeconds = 1.0;

    [TestCase]
    public void NoComposedTrack_HasALoopSeamLevelLurch()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var pcm = Decode(phase, id);
            var edgeSamples = (int)(LoopSeamEdgeSeconds * SampleRate);
            var headDb = RmsDbOfRange(pcm, 0, Math.Min(edgeSamples, pcm.Length));
            var tailDb = RmsDbOfRange(pcm, Math.Max(0, pcm.Length - edgeSamples), pcm.Length);
            var lurch = Math.Abs(headDb - tailDb);

            AssertGate(phase, id, Gate.LoopSeamLevelLurch, lurch < LoopSeamLurchCeilingDb,
                $"head({LoopSeamEdgeSeconds:0}s)={headDb:0.0}dB vs tail({LoopSeamEdgeSeconds:0}s)={tailDb:0.0}dB, a {lurch:0.0}dB jump (ceiling {LoopSeamLurchCeilingDb:0.0}dB)");
        }
    }

    // ---- Gate 4: isolated transient ---------------------------------------------------------------

    /// <summary>
    /// Ceiling 13.0dB between a track's loudest 5ms window and the RMS of its own ±0.5s local
    /// neighbourhood (excluding a 20ms guard band immediately around the spike, so the spike does not
    /// inflate the very average it is compared against). A real musical hit sits IN its own
    /// neighbourhood's dynamic range; an isolated click stands out from otherwise near-silent
    /// surroundings. Measured: day-first-light 10.6dB, night-still 10.6dB, quest-wait 5.8dB — all
    /// under. town-dusk measures 16.1dB (its click at t≈57.3s) and is exempted.
    /// </summary>
    private const double IsolatedTransientCeilingDb = 13.0;
    private const double TransientSpikeSeconds = 0.005;
    private const double TransientNeighbourhoodSeconds = 0.5;
    private const double TransientGuardSeconds = 0.020;

    [TestCase]
    public void NoComposedTrack_HasAnIsolatedTransient()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var pcm = Decode(phase, id);
            var sampleRate = SampleRate;
            var spikeSamples = Math.Max(1, (int)(TransientSpikeSeconds * sampleRate));
            var hop = Math.Max(1, spikeSamples / 2);

            var bestIndex = 0;
            var bestRms = 0.0;
            for (var s = 0; s + spikeSamples <= pcm.Length; s += hop)
            {
                double sumSq = 0;
                for (var j = s; j < s + spikeSamples; j++)
                {
                    sumSq += (double)pcm[j] * pcm[j];
                }

                var rms = Math.Sqrt(sumSq / spikeSamples);
                if (rms > bestRms)
                {
                    bestRms = rms;
                    bestIndex = s;
                }
            }

            var peakDb = ToDb(bestRms);

            var guardSamples = (int)(TransientGuardSeconds * sampleRate);
            var neighbourhoodSamples = (int)(TransientNeighbourhoodSeconds * sampleRate);
            var lo = Math.Max(0, bestIndex - neighbourhoodSamples);
            var hi = Math.Min(pcm.Length, bestIndex + spikeSamples + neighbourhoodSamples);
            var leftEnd = Math.Max(lo, bestIndex - guardSamples);
            var rightStart = Math.Min(hi, bestIndex + spikeSamples + guardSamples);

            double nbSumSq = 0;
            var nbCount = 0;
            for (var j = lo; j < leftEnd; j++)
            {
                nbSumSq += (double)pcm[j] * pcm[j];
                nbCount++;
            }

            for (var j = rightStart; j < hi; j++)
            {
                nbSumSq += (double)pcm[j] * pcm[j];
                nbCount++;
            }

            var neighbourhoodDb = nbCount > 0 ? ToDb(Math.Sqrt(nbSumSq / nbCount)) : -120.0;
            var outlierDb = peakDb - neighbourhoodDb;
            var atSeconds = bestIndex / (double)sampleRate;

            AssertGate(phase, id, Gate.IsolatedTransient, outlierDb < IsolatedTransientCeilingDb,
                $"loudest 5ms window is {peakDb:0.0}dB at t={atSeconds:0.0}s, its own ±0.5s neighbourhood " +
                $"averages {neighbourhoodDb:0.0}dB — {outlierDb:0.0}dB above it (ceiling {IsolatedTransientCeilingDb:0.0}dB)");
        }
    }

    // ---- Gate 5: impulse train with no midrange tone -----------------------------------------------

    /// <summary>
    /// Fails only when BOTH hold: more than 90% of a track's energy sits below 150Hz, AND less than
    /// 0.5% sits above 6kHz — a signature no ordinary bed matches (a real mix always has SOME midrange
    /// or high content), but a pure low-frequency impulse train does by construction. Measured
    /// (fraction of energy, via <see cref="OnePoleLowPass"/> and a two-pole 6kHz cascade for the
    /// highpass residual — see <see cref="Synth.Bellows"/>' own two-pole choice for why one pole rolls
    /// off too gently to isolate a band cleanly): day-first-light 92.1% below 150Hz / 0.055% above
    /// 6kHz — fails both conditions, exempted (U-T4-9's target). town-dusk 70.5%/13.0%, night-still
    /// 83.1%/2.0%, quest-wait 86.9%/1.0% — none crosses the 90% floor, all pass.
    /// </summary>
    private const double LowFreqFractionFloor = 0.90;
    private const double HighFreqFractionCeiling = 0.005;
    private const float LowCutoffHz = 150f;
    private const float HighCutoffHz = 6000f;

    [TestCase]
    public void NoComposedTrack_IsAnImpulseTrainWithNoTone()
    {
        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var pcm = Decode(phase, id);
            var sampleRate = SampleRate;

            var low = OnePoleLowPass(pcm, LowCutoffHz, sampleRate);
            var highLowpassResidualBase = OnePoleLowPass(pcm, HighCutoffHz, sampleRate);
            var highLowpass = OnePoleLowPass(highLowpassResidualBase, HighCutoffHz, sampleRate); // two-pole cascade

            double total = 0, lowEnergy = 0, highEnergy = 0;
            for (var i = 0; i < pcm.Length; i++)
            {
                total += (double)pcm[i] * pcm[i];
                lowEnergy += (double)low[i] * low[i];
                var highResidual = pcm[i] - highLowpass[i];
                highEnergy += (double)highResidual * highResidual;
            }

            var lowFraction = lowEnergy / total;
            var highFraction = highEnergy / total;
            var isImpulseTrain = lowFraction > LowFreqFractionFloor && highFraction < HighFreqFractionCeiling;

            AssertGate(phase, id, Gate.ImpulseTrainNoTone, !isImpulseTrain,
                $"{lowFraction * 100:0.0}% of its energy sits below {LowCutoffHz:0}Hz and {highFraction * 100:0.000}% " +
                $"above {HighCutoffHz:0}Hz (fails only if both >{LowFreqFractionFloor * 100:0}% and <{HighFreqFractionCeiling * 100:0.0}%)");
        }
    }
}
#endif
