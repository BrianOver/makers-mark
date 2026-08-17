#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The census for <see cref="MixBudget"/> (U-T4-2). Every shipped cue and every composed bed, measured
/// against the target table that unit writes down — not retuned here (that is later T4 units'
/// job), only weighed and, where it is already known to miss, named in
/// <see cref="MixBudget.PendingExemptions"/>.
///
/// <para><b>Why this cannot be a hand-listed array.</b> Both loops below iterate the real manifests —
/// <see cref="Enum.GetValues{TEnum}"/> over <see cref="Cue"/>, and
/// <see cref="AudioDirector.ComposedTrackIds"/> — the same lesson <c>AudioTests</c> and
/// <c>NarratorAudioTests</c> already learned: a literal id array stops covering the family the moment
/// someone adds a member to it, and 128 new assets once shipped untested under exactly that shape of
/// green suite.</para>
///
/// <para><b>Decode paths are borrowed, not reinvented.</b> SFX cues decode through
/// <c>SfxLibrary.Get(cue).Data</c>, the same 16-bit PCM <c>AudioTests.Pcm</c> already decodes.
/// Composed beds decode through <see cref="AudioStreamPlayback.MixAudio"/>, the same technique
/// <c>AudioTests.EveryComposedTrack_StaysUnderItsTruePeakCeiling</c> already proved reachable and
/// correct for this exact asset family.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MixBudgetCensusTests
{
    /// <summary>Default fader gain at every mixer slider left at <see cref="AudioDirector.DefaultVolume"/>
    /// (1.0 = unity = 0 dB — see <c>AudioDirector.MixGainDb</c>). The census measures the mix a fresh
    /// install actually ships with, not a player's own later adjustment.</summary>
    private const float DefaultFaderDb = 0f;

    /// <summary>Decodes 16-bit mono PCM back to floats in [-1, 1] — identical to <c>AudioTests.Pcm</c>,
    /// duplicated here rather than shared because that method is private to its own suite.</summary>
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

    /// <summary>Decodes up to <paramref name="frameBudget"/> frames of <paramref name="stream"/> from its
    /// start via <see cref="AudioStreamPlayback.MixAudio"/> — the same decode-without-playing technique
    /// <c>AudioTests.DecodeUpTo</c> uses for the true-peak census, duplicated here for the same reason
    /// as <see cref="Pcm"/> above.</summary>
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

    /// <summary>Combines a stereo frame into one magnitude sample via mean channel POWER
    /// (<c>sqrt((L² + R²) / 2)</c>), never a raw L+R sum. A plain sum is exactly the mono-sum artefact
    /// this unit's brief warns about: the "+1.63 dBFS, 11,133 clipped samples" reading that looked like
    /// a clipping defect was that artefact, not a real one — every shipped bed peaks at or below
    /// −1.30 dBFS on its own channels, so a clipped sample was arithmetically impossible.</summary>
    private static float[] CombineStereo(Vector2[] frames)
    {
        var mono = new float[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            var l = frames[i].X;
            var r = frames[i].Y;
            mono[i] = MathF.Sqrt((l * l + r * r) * 0.5f);
        }

        return mono;
    }

    /// <summary>
    /// Every <see cref="Cue"/>, decoded through the exact path the game plays
    /// (<see cref="SfxLibrary.Get"/>), measured with <see cref="MixBudget.ActiveWindowRms"/>, and
    /// levelled up to what a fresh install actually outputs: source dBFS plus the cue's bus
    /// (<see cref="AudioBuses.SfxBusDb"/>, or <see cref="AudioBuses.SfxLoopBusDb"/> stacked on top of
    /// it for the one <see cref="MixBudget.Category.HeldLoop"/> cue) plus <see cref="DefaultFaderDb"/>.
    ///
    /// <para>A cue outside its band is legal ONLY if <see cref="MixBudget.PendingExemptions"/> already
    /// names it — and if it does, this test also demands the cue is ACTUALLY out of band, so a fixed
    /// cue whose exemption nobody removed goes red here rather than sitting as a silently-stale entry
    /// forever.</para>
    /// </summary>
    [TestCase]
    public void EveryCue_LandsInItsBudgetBand()
    {
        foreach (Cue cue in Enum.GetValues<Cue>())
        {
            var pcm = Pcm(SfxLibrary.Get(cue));
            var sourceDb = MixBudget.ActiveWindowRms(pcm, Synth.SampleRate);

            var category = MixBudget.CategoryFor(cue);
            var busDb = category == MixBudget.Category.HeldLoop
                ? AudioBuses.SfxLoopBusDb + AudioBuses.SfxBusDb
                : AudioBuses.SfxBusDb;
            var effective = sourceDb + busDb + DefaultFaderDb;

            var budget = MixBudget.Budgets[category];
            var inBand = MathF.Abs(effective - budget.TargetRmsDbfs) <= budget.ToleranceDb;
            var key = $"Cue.{cue}";
            var exempted = MixBudget.PendingExemptions.Contains(key);

            AssertBool(inBand || exempted)
                .OverrideFailureMessage(
                    $"{cue} ({category}) measures {effective:0.00} dBFS effective — outside "
                    + $"{budget.TargetRmsDbfs:0.0}±{budget.ToleranceDb:0.0} dB and not listed in "
                    + "MixBudget.PendingExemptions. Either this cue drifted and needs an exemption "
                    + "(with a bumped pin), or it is a brand-new cue with no budget assignment.")
                .IsTrue();

            if (exempted)
            {
                AssertBool(inBand)
                    .OverrideFailureMessage(
                        $"'{key}' is listed in MixBudget.PendingExemptions but now measures "
                        + $"{effective:0.00} dBFS, inside its {budget.TargetRmsDbfs:0.0}±"
                        + $"{budget.ToleranceDb:0.0} band. It was fixed — remove it from "
                        + "PendingExemptions and lower the pinned count.")
                    .IsFalse();
            }
        }
    }

    /// <summary>
    /// Every composed bed <see cref="AudioDirector.ComposedTrackIds"/> currently wires, decoded through
    /// the same <see cref="AudioStreamPlayback.MixAudio"/> path
    /// <c>AudioTests.EveryComposedTrack_StaysUnderItsTruePeakCeiling</c> already proved reachable,
    /// measured over its combined stereo channels (see <see cref="CombineStereo"/>), and levelled up by
    /// its own <see cref="AudioDirector.ComposedTrackTrims"/> entry plus
    /// <see cref="AudioBuses.MusicBusDb"/> — the full stack nobody had added together end-to-end before
    /// this unit. Same exemption/staleness contract as <see cref="EveryCue_LandsInItsBudgetBand"/>.
    /// </summary>
    [TestCase]
    public void EveryComposedBed_LandsInItsBudgetBand()
    {
        var mixRate = (int)AudioServer.GetMixRate();
        var budget = MixBudget.Budgets[MixBudget.Category.MusicBed];

        foreach (var (phase, id) in AudioDirector.ComposedTrackIds)
        {
            var stream = AudioDirector.LoadComposedTrackForCensus(phase);
            AssertThat(stream)
                .OverrideFailureMessage($"{phase}'s composed track '{id}' would not load — cannot budget-check it.")
                .IsNotNull();

            var frameBudget = (int)Math.Ceiling(stream!.GetLength() * mixRate) + mixRate; // +1s margin
            var frames = DecodeUpTo(stream, frameBudget);
            AssertInt(frames.Length)
                .OverrideFailureMessage(
                    $"{phase}'s composed track '{id}' decoded 0 frames via MixAudio — a track this test "
                    + "cannot see is a track this budget cannot protect.")
                .IsGreater(0);

            var mono = CombineStereo(frames);
            var sourceDb = MixBudget.ActiveWindowRms(mono, mixRate);
            var trimDb = AudioDirector.ComposedTrackTrims[phase];
            var effective = sourceDb + trimDb + AudioBuses.MusicBusDb;

            var inBand = MathF.Abs(effective - budget.TargetRmsDbfs) <= budget.ToleranceDb;
            var key = $"Track.{id}";
            var exempted = MixBudget.PendingExemptions.Contains(key);

            AssertBool(inBand || exempted)
                .OverrideFailureMessage(
                    $"{phase}'s composed track '{id}' measures {effective:0.00} dBFS effective "
                    + $"(source {sourceDb:0.00} + trim {trimDb:+0.0;-0.0} + bus {AudioBuses.MusicBusDb:0.0}) "
                    + $"— outside {budget.TargetRmsDbfs:0.0}±{budget.ToleranceDb:0.0} dB and not listed in "
                    + "MixBudget.PendingExemptions.")
                .IsTrue();

            if (exempted)
            {
                AssertBool(inBand)
                    .OverrideFailureMessage(
                        $"'{key}' is listed in MixBudget.PendingExemptions but now measures "
                        + $"{effective:0.00} dBFS, inside its {budget.TargetRmsDbfs:0.0}±"
                        + $"{budget.ToleranceDb:0.0} band. It was fixed — remove it from "
                        + "PendingExemptions and lower the pinned count.")
                    .IsFalse();
            }
        }
    }

    /// <summary>
    /// The receipt-cannot-lie guard, ConstitutionTests-style: <see cref="MixBudget.PendingExemptions"/>
    /// is pinned at an exact count, so a future PR silently growing the exemption set (papering over a
    /// new regression instead of fixing it) or silently shrinking it (claiming a re-level without the
    /// diff to prove it) both go red here and need a deliberate, reviewed edit to this number.
    ///
    /// <para>U-T4-3 dropped this 20 -&gt; 4: every one-shot cue (5 ceremonial + 11 UI Cue.* entries) now
    /// lands in its <see cref="MixBudget.Budgets"/> band via <see cref="Synth.NormaliseRms"/> — see that
    /// unit's PR body for the before/after table. Only the 4 composed-bed Track.* entries remained,
    /// unowned by that unit.</para>
    ///
    /// <para>U-T4-6 raised this 4 -&gt; 13: 9 of the 49 mastered narrator lines are genuinely
    /// peak-limited (crest factor too wide to reach −20 dBFS RMS without breaching the −1.5 dBTP
    /// ceiling) — see <see cref="MixBudget.PendingExemptions"/>'s own doc for the full measurement.
    /// </para>
    /// </summary>
    private const int PinnedExemptionCount = 13;

    [TestCase]
    public void ThePendingExemptionCount_IsThePinnedNumber()
    {
        AssertInt(MixBudget.PendingExemptions.Count)
            .OverrideFailureMessage(
                $"MixBudget.PendingExemptions has {MixBudget.PendingExemptions.Count} entries; this test "
                + $"pins {PinnedExemptionCount}. If a later unit re-levelled a cue or bed into band, remove "
                + "its entry and lower this number in the same PR. If something newly drifted out of band, "
                + "add its entry and raise this number — never let the set change silently.")
            .IsEqual(PinnedExemptionCount);
    }

    /// <summary>Typo/staleness guard the other direction: every exemption must name a cue or composed
    /// track that actually exists right now, so a rename that leaves a stale string behind is caught
    /// here instead of quietly exempting nothing.</summary>
    [TestCase]
    public void EveryPendingExemption_NamesARealCueOrComposedTrack()
    {
        var validKeys = new HashSet<string>(Enum.GetValues<Cue>().Select(c => $"Cue.{c}"), StringComparer.Ordinal);
        foreach (var id in AudioDirector.ComposedTrackIds.Values)
        {
            validKeys.Add($"Track.{id}");
        }

        foreach (var audioId in NarratorLines.AllAudioIds)
        {
            validKeys.Add($"NarratorLine.{audioId}");
        }

        var unknown = MixBudget.PendingExemptions.Where(key => !validKeys.Contains(key)).ToList();
        AssertInt(unknown.Count)
            .OverrideFailureMessage(
                "MixBudget.PendingExemptions names ids that do not exist: " + string.Join(", ", unknown))
            .IsEqual(0);
    }

    /// <summary>Approximate ("true") peak via a cheap 2x linear interpolation between consecutive
    /// decoded frames — the SAME technique <c>AudioTests.PeakDb</c> uses for composed tracks (see its
    /// own doc for why an approximation is an acceptable trade), narrowed to one channel: narrator
    /// lines are recorded/committed MONO, and Godot's <see cref="AudioStreamPlayback.MixAudio"/> always
    /// returns stereo frames with the mono content duplicated onto both channels, so reading <see
    /// cref="Vector2.X"/> alone is the real signal with no information lost (unlike <see
    /// cref="CombineStereo"/>'s mean-channel-POWER combine above, which would discard sign and corrupt
    /// the inter-sample interpolation this measurement depends on).</summary>
    private static (float SamplePeakDb, float TruePeakDb) MonoPeakDb(Vector2[] frames)
    {
        var sampleMax = 0f;
        var trueMax = 0f;

        for (var i = 0; i < frames.Length; i++)
        {
            var v = frames[i].X;
            var absV = Math.Abs(v);
            sampleMax = Math.Max(sampleMax, absV);
            trueMax = Math.Max(trueMax, absV);

            if (i + 1 < frames.Length)
            {
                var mid = (v + frames[i + 1].X) * 0.5f;
                trueMax = Math.Max(trueMax, Math.Abs(mid));
            }
        }

        static float ToDb(float linear) => linear > 0f ? 20f * MathF.Log10(linear) : -120f;
        return (ToDb(sampleMax), ToDb(trueMax));
    }

    /// <summary>The narrator's own true-peak ceiling (§11.14.6, U-T4-6) — tighter than the composed-
    /// track ceiling (<c>AudioTests.TruePeakCeilingDbTp</c>, −1.0) because the Narrator bus carries no
    /// headroom of its own (<see cref="AudioBuses.NarratorBusDb"/> is 0 dB, so source and effective are
    /// the same number) and speech has less margin against inter-sample overs than a mixed composition
    /// does. Unconditional — no <see cref="MixBudget.PendingExemptions"/> escape hatch, unlike the RMS
    /// band check below: a line that cannot reach −20 dBFS without breaching this gets mastered to the
    /// loudest SAFE gain instead (see <see cref="MixBudget.PendingExemptions"/>'s own doc for the 9
    /// lines that landed here), so a real ceiling breach reaching this test is always a genuine, fresh
    /// regression, never a known, accepted trade.
    ///
    /// <para><b>Master to a MARGIN, never to this number, and measure with THIS gate.</b> The first
    /// mastering pass bisected each line to the largest gain whose true peak "cleared" −1.5, measured
    /// with a separate Python port of <see cref="MixBudget.ActiveWindowRms"/>/<c>MonoPeakDb</c>. Fourteen
    /// lines then failed here, because two correct implementations of the same measurement straddle a
    /// boundary: the port and this gate disagreed by ~0.05 dB on most lines and by <b>0.7 dB</b> on
    /// <c>climax-reached-01</c> (ported −1.65, measured here −0.95). A target with no margin turns that
    /// ordinary disagreement into a red build.</para>
    ///
    /// <para><b>And gain alone cannot hit a true-peak target at this bitrate.</b> Measured while fixing
    /// the above: at 45 kbps mono Vorbis the encoded true peak is <b>not a monotone function of input
    /// gain</b> — cutting <c>killing-blow-02</c> by a further 1.0 dB moved its measured peak from −1.51
    /// to −1.46, the wrong way, because codec ringing dominates. Three lines could not be brought under
    /// the ceiling by scaling at all. What works is a real true-peak limiter before encoding
    /// (<c>alimiter=limit=0.63:level=disabled</c> — <c>level=disabled</c> matters, since ffmpeg's
    /// alimiter applies auto make-up gain by default and without it the peak went to <i>+0.3 dBFS</i>),
    /// which tames the peak while leaving RMS inside the band. That is why nine lines need an RMS
    /// exemption and not twelve: limiting keeps loudness where scaling would have sacrificed it.</para></summary>
    private const float NarratorTruePeakCeilingDbTp = -1.5f;

    /// <summary>
    /// U-T4-6: "the first gate that has ever looked at [the narrator lines'] level." Iterates <see
    /// cref="NarratorLines.AllAudioIds"/> — the real registry <see cref="NarratorVoiceDirector.Lines"/>
    /// backs, not a hand-listed id array — decodes each committed Ogg Vorbis line through the same
    /// <see cref="AudioStreamPlayback.MixAudio"/> path <see cref="EveryComposedBed_LandsInItsBudgetBand"/>
    /// already uses, and checks BOTH halves of the unit's own brief: the −20±1.0 dBFS
    /// <see cref="MixBudget.Category.Narrator"/> RMS band (source == effective, since <see
    /// cref="AudioBuses.NarratorBusDb"/> is 0 dB) with the same exemption/staleness contract every other
    /// census in this file uses, AND the unconditional <see cref="NarratorTruePeakCeilingDbTp"/> true-
    /// peak ceiling.
    /// </summary>
    [TestCase]
    public void EveryNarratorLine_LandsInItsBudgetBand_AndUnderItsTruePeakCeiling()
    {
        var mixRate = (int)AudioServer.GetMixRate();
        var budget = MixBudget.Budgets[MixBudget.Category.Narrator];
        var checkedCount = 0;

        foreach (var audioId in NarratorLines.AllAudioIds)
        {
            var path = NarratorLines.ResourcePath(audioId);
            AssertBool(ResourceLoader.Exists(path))
                .OverrideFailureMessage($"narrator line '{audioId}' has no committed audio at {path}.")
                .IsTrue();

            var stream = GD.Load<AudioStream>(path);
            AssertThat(stream)
                .OverrideFailureMessage($"narrator line '{audioId}' would not load from {path}.")
                .IsNotNull();

            var frameBudget = (int)Math.Ceiling(stream!.GetLength() * mixRate) + mixRate; // +1s margin
            var frames = DecodeUpTo(stream, frameBudget);
            AssertInt(frames.Length)
                .OverrideFailureMessage(
                    $"narrator line '{audioId}' decoded 0 frames via MixAudio — a line this test cannot "
                    + "see is a line this budget cannot protect.")
                .IsGreater(0);

            var mono = new float[frames.Length];
            for (var i = 0; i < frames.Length; i++)
            {
                mono[i] = frames[i].X;
            }

            var sourceDb = MixBudget.ActiveWindowRms(mono, mixRate);
            var effective = sourceDb + AudioBuses.NarratorBusDb + DefaultFaderDb;

            var inBand = MathF.Abs(effective - budget.TargetRmsDbfs) <= budget.ToleranceDb;
            var key = $"NarratorLine.{audioId}";
            var exempted = MixBudget.PendingExemptions.Contains(key);

            AssertBool(inBand || exempted)
                .OverrideFailureMessage(
                    $"narrator line '{audioId}' measures {effective:0.00} dBFS effective — outside "
                    + $"{budget.TargetRmsDbfs:0.0}±{budget.ToleranceDb:0.0} dB and not listed in "
                    + "MixBudget.PendingExemptions. Either this line drifted and needs an exemption "
                    + "(with a bumped pin), or it is a brand-new line with no mastering pass yet.")
                .IsTrue();

            if (exempted)
            {
                AssertBool(inBand)
                    .OverrideFailureMessage(
                        $"'{key}' is listed in MixBudget.PendingExemptions but now measures "
                        + $"{effective:0.00} dBFS, inside its {budget.TargetRmsDbfs:0.0}±"
                        + $"{budget.ToleranceDb:0.0} band. It was fixed — remove it from "
                        + "PendingExemptions and lower the pinned count.")
                    .IsFalse();
            }

            var (_, truePeakDb) = MonoPeakDb(frames);
            AssertFloat(truePeakDb)
                .OverrideFailureMessage(
                    $"narrator line '{audioId}' has an (approximate) true peak of {truePeakDb:0.00} dBTP "
                    + $"— over the {NarratorTruePeakCeilingDbTp:0.0} dBTP ceiling. This ceiling has no "
                    + "exemption: re-master the file with art/pipeline (or the equivalent narrator "
                    + "mastering pass) to a lower gain, never widen this constant.")
                .IsLessEqual(NarratorTruePeakCeilingDbTp);

            checkedCount++;
        }

        // Vacuous-green guard: this test's entire value is that it iterates the real registry, so an
        // enumeration collapsing to (near-)nothing must fail here rather than pass by checking nothing.
        AssertInt(checkedCount)
            .OverrideFailureMessage("no narrator lines were checked — NarratorLines.AllAudioIds enumerated empty")
            .IsGreaterEqual(49);
    }

}
#endif
