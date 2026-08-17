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
    /// unit's PR body for the before/after table. Only the 4 composed-bed Track.* entries remain,
    /// unowned by this unit.</para>
    /// </summary>
    private const int PinnedExemptionCount = 4;

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

        var unknown = MixBudget.PendingExemptions.Where(key => !validKeys.Contains(key)).ToList();
        AssertInt(unknown.Count)
            .OverrideFailureMessage(
                "MixBudget.PendingExemptions names ids that do not exist: " + string.Join(", ", unknown))
            .IsEqual(0);
    }
}
#endif
