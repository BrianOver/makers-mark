using System;
using System.Collections.Generic;

namespace GodotClient.Audio;

/// <summary>
/// The mix's target table, and the census's own measuring stick — U-T4-2, the unit that makes every
/// later audio fix falsifiable.
///
/// <para><b>Why this file exists.</b> The owner has had four failed audio fixes across three rounds,
/// and every one moved a runtime constant with no target to move it TOWARD, so none of them could
/// fail: −3.5 dB and −1.5 dB whole-file gain cuts chasing a clipping defect that does not exist (the
/// "+1.63 dBFS, 11,133 clipped samples" reading was an L+R mono-sum artefact — every shipped bed peaks
/// at or below −1.30 dBFS, so a clipped sample is arithmetically impossible); a bellows
/// <c>Normalise</c> target moved 0.15 → 0.12, which measures −1.92 dB, at or below the threshold of
/// audible change; and an MP3→OGG transcode that made the SAMPLE continuous while the CONTENT still
/// jumps 33 dB every loop. This file writes the target down as a compiled table with tolerances, so
/// the next fix has something to aim at and something that can catch it missing.</para>
///
/// <para><b>Land it red-safely.</b> This unit re-levels nothing — <see cref="PendingExemptions"/> is
/// every cue and composed-bed id this build already knows sits outside its band, with a pinned count
/// (<c>MixBudgetCensusTests.ThePendingExemptionCount_IsThePinnedNumber</c>) so a future removal is a
/// red-then-reviewed diff in a compiled file, the same discipline <c>ConstitutionTests</c> already
/// holds law exceptions to. The receipt can lie about how much got fixed; the pinned count cannot.</para>
/// </summary>
public static class MixBudget
{
    /// <summary>
    /// The five buckets every sound in the game falls into. <see cref="CategoryFor"/> maps every
    /// <see cref="Cue"/> onto one of <see cref="CeremonialOneShot"/>, <see cref="UiOneShot"/>, or
    /// <see cref="HeldLoop"/> — <see cref="Narrator"/> covers the recorded voice lines
    /// (<c>NarratorLines</c>, not a <see cref="Cue"/>) and <see cref="MusicBed"/> covers the composed
    /// tracks (<c>AudioDirector.ComposedTrackIds</c>, also not a <see cref="Cue"/>), so neither has a
    /// <see cref="Cue"/> mapping of its own — they are measured by
    /// <c>MixBudgetCensusTests.EveryComposedBed_LandsInItsBudgetBand</c> and a later T4 unit's narrator
    /// census respectively.
    /// </summary>
    public enum Category
    {
        Narrator,
        CeremonialOneShot,
        UiOneShot,
        MusicBed,
        HeldLoop,
    }

    /// <summary>One category's target and how far it may drift before the census calls it out.</summary>
    public readonly record struct Budget(float TargetRmsDbfs, float ToleranceDb);

    /// <summary>
    /// The budget table itself. Every number here is the source's own <see cref="ActiveWindowRms"/>
    /// EFFECTIVE loudness — after its bus's <c>VolumeDb</c> and the player's default-fader gain (1.0 on
    /// every fader, which is 0 dB — see <c>AudioDirector.MixGainDb</c>) are added on top of the decoded
    /// PCM. That is deliberate: the census's job is "what does the player actually hear right now,"
    /// not "what does the source file measure in isolation," and a source that is perfectly targeted
    /// but sitting behind the wrong bus attenuation is still a defect this table must catch.
    ///
    /// <list type="table">
    /// <listheader><term>category</term><description>source target / bus / effective / tolerance</description></listheader>
    /// <item><term>Narrator</term><description>−20 / Narrator 0 dB / <b>−20</b> / ±1.0</description></item>
    /// <item><term>CeremonialOneShot</term><description>−23 / Sfx 0 dB / <b>−23</b> / ±2.0</description></item>
    /// <item><term>UiOneShot</term><description>−27 / Sfx 0 dB / <b>−27</b> / ±2.0</description></item>
    /// <item><term>MusicBed</term><description>−12 / Music −20 dB / <b>−32</b> / ±1.5</description></item>
    /// <item><term>HeldLoop</term><description>−32 / SfxLoop −3 dB / <b>−35</b> / ±1.5</description></item>
    /// </list>
    ///
    /// <para>Spread goes from the 47 dB actually measured across every shipped cue/bed (see the PR body
    /// for U-T4-2) down to a 15 dB target spread top-to-bottom. Before this table existed, the narrator
    /// — the game's emotional payload, and the reason a hero's fate lands as anything more than a
    /// number — measured quieter than most of the game's own UI clicks, because nobody had ever written
    /// down what "quieter than the narrator" or "louder than a click" was supposed to MEAN in dBFS.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<Category, Budget> Budgets = new Dictionary<Category, Budget>
    {
        [Category.Narrator] = new Budget(-20f, 1.0f),
        [Category.CeremonialOneShot] = new Budget(-23f, 2.0f),
        [Category.UiOneShot] = new Budget(-27f, 2.0f),
        [Category.MusicBed] = new Budget(-32f, 1.5f),
        [Category.HeldLoop] = new Budget(-35f, 1.5f),
    };

    /// <summary>
    /// Every <see cref="Cue"/>'s budget bucket. <b>Deliberately exhaustive with no discard arm</b>: a
    /// future <see cref="Cue"/> added to that enum without an entry here is a compiler warning
    /// (CS8509, "the switch expression does not handle all possible values") the moment this file
    /// builds, and a <see cref="SwitchExpressionException"/> the moment anything — including
    /// <c>MixBudgetCensusTests.EveryCue_LandsInItsBudgetBand</c>, which iterates
    /// <see cref="Enum.GetValues{TEnum}"/> — actually asks for its category. A silent pass was exactly
    /// how a mix could drift 47 dB in the first place: nobody had to notice a new cue was uncategorised
    /// because nothing ever asked.
    ///
    /// <para><b>The five ceremonial ids and the fifteen UI ids below are counted straight off this
    /// unit's brief.</b> One entry in the brief's own Ceremonial bucket — "the 5 grade stings" — had no
    /// <see cref="Cue"/> at the time this file was written; only one craft-completion cue (<see
    /// cref="Audio.Cue.CraftDone"/> ) existed for every grade, and ForgePanel's own grade sting bypassed
    /// <see cref="AudioDirector"/> (and this table) entirely. U-T4-5 closed that gap: <see
    /// cref="Cue.GradeStingPoor"/> through <see cref="Cue.GradeStingMasterwork"/> now exist and are
    /// mapped below, so the census this file's tests run finally weighs them too.</para>
    /// </summary>
    public static Category CategoryFor(Cue cue) => cue switch
    {
        // Ceremonial one-shots: the moments the whole game is FOR (a hero's fate, a craft finishing,
        // a death, a memorial). Rare, so they may sit louder than an ordinary UI click.
        Cue.Bell => Category.CeremonialOneShot,
        Cue.PartyDepart => Category.CeremonialOneShot,
        Cue.CraftDone => Category.CeremonialOneShot,
        Cue.DeathToll => Category.CeremonialOneShot,
        Cue.MemorialHonor => Category.CeremonialOneShot,
        Cue.GradeStingPoor => Category.CeremonialOneShot,
        Cue.GradeStingCommon => Category.CeremonialOneShot,
        Cue.GradeStingFine => Category.CeremonialOneShot,
        Cue.GradeStingSuperior => Category.CeremonialOneShot,
        Cue.GradeStingMasterwork => Category.CeremonialOneShot,

        // UI one-shots: fire constantly, must sit well under the ceremonial cues so the rare moments
        // still read as rare.
        Cue.Click => Category.UiOneShot,
        Cue.PanelOpen => Category.UiOneShot,
        Cue.PanelClose => Category.UiOneShot,
        Cue.Shelve => Category.UiOneShot,
        Cue.Coin => Category.UiOneShot,
        Cue.Rejected => Category.UiOneShot,
        Cue.BountyPost => Category.UiOneShot,
        Cue.Quench => Category.UiOneShot,
        Cue.HammerOnBeat => Category.UiOneShot,
        Cue.HammerOffBeat => Category.UiOneShot,
        Cue.EnterForge => Category.UiOneShot,
        Cue.EnterTavern => Category.UiOneShot,
        Cue.EnterMarket => Category.UiOneShot,
        Cue.EnterMineGate => Category.UiOneShot,
        Cue.EnterNoticeboard => Category.UiOneShot,

        // Held/looping: the bellows, sustained for seconds at a time under whatever bed is playing.
        Cue.Bellows => Category.HeldLoop,
    };

    /// <summary>Width of the sliding window this budget measures loudness over.</summary>
    private const float WindowSeconds = 0.050f;

    /// <summary>How far below the loudest 50 ms window a quieter window may sit and still count as part
    /// of the source's active content, rather than trailing silence/noise-floor.</summary>
    private const float ActiveRangeDb = 40f;

    /// <summary>Reported for a buffer with no measurable signal at all, rather than negative infinity.</summary>
    private const float SilenceFloorDb = -120f;

    /// <summary>
    /// Unweighted RMS in dBFS over a source's ACTIVE window: the span from the first to the last 50 ms
    /// sliding window whose RMS sits within 40 dB of that source's own loudest 50 ms window. Silence (or
    /// noise-floor hiss) more than 40 dB under the loudest moment, at either end, is excluded; genuine
    /// quiet PASSAGES in the middle of a track are not, because they still sit within range of windows
    /// either side of them that are.
    ///
    /// <para><b>Why unweighted RMS and not integrated LUFS, on purpose.</b> LUFS (ITU-R BS.1770) applies
    /// an absolute gate (blocks under −70 LUFS are dropped) and a relative gate (blocks more than 10 LU
    /// under the ungated mean are dropped too), which is exactly the property that makes it BLIND to
    /// dead air: the Evening bed (<c>town-dusk.ogg</c>) measures a respectable −19.6 LUFS integrated
    /// while its own content is silent for roughly 53 of its 60 seconds — the gates simply throw the
    /// silent majority away and average only what is left. That is precisely how it passed every prior
    /// review. This unweighted, ungated measurement counts the dead air as part of the source, which is
    /// what a player sitting through it actually experiences. Integrated LUFS is still worth recording
    /// ALONGSIDE this number as documentation (see the PR body's measurement table) — just never as the
    /// thing a budget is judged against.</para>
    ///
    /// <para><b>Signature note.</b> The unit's own brief describes this as <c>ActiveWindowRms(float[]
    /// pcm)</c>; a second <paramref name="sampleRate"/> parameter is added here because the two sources
    /// this budget measures decode at two different rates — synthesized SFX at
    /// <see cref="Synth.SampleRate"/> (22050 Hz) and composed beds at the engine's own
    /// <c>AudioServer.GetMixRate()</c> — and "50 ms" is meaningless without knowing how many samples
    /// that is. Flagged here rather than silently diverging from the brief.</para>
    ///
    /// <para>Implemented as a prefix sum of squares so an arbitrary hop (a quarter of the window, for
    /// finer boundary resolution than a strictly non-overlapping scan) costs O(1) per window rather than
    /// O(window) — a 60 s composed bed at 48 kHz is millions of samples, and this runs inside a test.</para>
    /// </summary>
    public static float ActiveWindowRms(float[] pcm, int sampleRate)
    {
        var sampleCount = pcm.Length;
        if (sampleCount == 0)
        {
            return SilenceFloorDb;
        }

        var window = Math.Max(1, (int)MathF.Round(sampleRate * WindowSeconds));
        var hop = Math.Max(1, window / 4);

        // prefix[i] = sum of squares of pcm[0..i) — lets any window's energy be read in O(1).
        var prefix = new double[sampleCount + 1];
        for (var i = 0; i < sampleCount; i++)
        {
            prefix[i + 1] = prefix[i] + (double)pcm[i] * pcm[i];
        }

        var windowStarts = new List<int>();
        var windowEnds = new List<int>();
        var windowRms = new List<double>();
        for (var start = 0; start < sampleCount; start += hop)
        {
            var end = Math.Min(sampleCount, start + window);
            if (end <= start)
            {
                break;
            }

            var sumSq = prefix[end] - prefix[start];
            windowStarts.Add(start);
            windowEnds.Add(end);
            windowRms.Add(Math.Sqrt(sumSq / (end - start)));

            if (end == sampleCount)
            {
                break;
            }
        }

        if (windowRms.Count == 0)
        {
            return SilenceFloorDb;
        }

        var loudest = 0.0;
        foreach (var rms in windowRms)
        {
            loudest = Math.Max(loudest, rms);
        }

        if (loudest <= 1e-9)
        {
            return SilenceFloorDb;
        }

        var loudestDb = 20.0 * Math.Log10(loudest);
        var thresholdDb = loudestDb - ActiveRangeDb;

        var firstActive = -1;
        var lastActive = -1;
        for (var i = 0; i < windowRms.Count; i++)
        {
            var db = ToDb(windowRms[i]);
            if (db >= thresholdDb)
            {
                if (firstActive < 0)
                {
                    firstActive = i;
                }

                lastActive = i;
            }
        }

        var activeStart = windowStarts[firstActive];
        var activeEnd = windowEnds[lastActive];
        var activeSumSq = prefix[activeEnd] - prefix[activeStart];
        var activeRms = Math.Sqrt(activeSumSq / (activeEnd - activeStart));
        return ToDb(activeRms);
    }

    private static float ToDb(double linear) => linear > 1e-9 ? (float)(20.0 * Math.Log10(linear)) : SilenceFloorDb;

    /// <summary>
    /// Every cue and composed-bed id this build already knows sits outside its <see cref="Budgets"/>
    /// band, keyed <c>"Cue.&lt;name&gt;"</c> or <c>"Track.&lt;id&gt;"</c>.
    /// <c>MixBudgetCensusTests.ThePendingExemptionCount_IsThePinnedNumber</c> pins
    /// <see cref="PendingExemptions"/>.Count so a future unit removing an entry (because it re-levelled
    /// that cue or bed into band) is a red-then-reviewed diff here, never a silent shrink nobody has to
    /// account for — the same discipline <c>ConstitutionTests</c> holds law exceptions to.
    ///
    /// <para><b>U-T4-3 emptied the Cue.* half of this table.</b> All 16 exempted cues (5 ceremonial + 11
    /// UI) are now levelled by <see cref="Synth.NormaliseRms"/> against this file's own
    /// <see cref="Budgets"/> table instead of an ad hoc peak, and every one measures in band — see that
    /// unit's PR body for the full before/after table. <c>Cue.EnterMarket</c> was deliberately left off
    /// the conversion (an R6-pinned byte-freeze test, <c>AudioTests.EnterMarket_IsByteUntouched</c>,
    /// protects its exact recipe) but was never exempted here either — it already landed in band under
    /// its old peak-based <c>Normalise</c> call, tanh or not. Only the 4 composed-bed entries remain,
    /// still owned by a later T4 unit.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> PendingExemptions = new HashSet<string>(StringComparer.Ordinal)
    {
        // ---- Music beds: all 4 sit far QUIETER than the −32 ±1.5 target once each track's own
        //      active-window RMS, its ComposedTrack.TrimDb, and the new AudioBuses.MusicBusDb
        //      (−20 dB) are added together — the exact stacking nobody had computed end-to-end
        //      before U-T4-2. See that unit's PR body for the full per-track arithmetic. ----
        "Track.day-first-light", // −41.64 dBFS effective
        "Track.town-dusk",       // −61.41 dBFS effective — the Evening dead-air bed
        "Track.night-still",     // −45.84 dBFS effective
        "Track.quest-wait",      // −45.99 dBFS effective
    };
}
