using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Crafting;

/// <summary>
/// The distinct "moments" a forge can earn (Wave 5, U23b) — small narrative flourishes the
/// presentation layer may surface, computed purely from the trace (no extra RNG draw, KTD4).
/// A bitflag set, not mutually exclusive: a forge can earn several at once.
/// APPEND ONLY if this ever needs new members (values may ride serialized telemetry later).
/// </summary>
[Flags]
public enum ForgeMoment
{
    None = 0,

    /// <summary>The heat entered the working band exactly once and never fell back below it
    /// before the quench — no cool-then-reheat cycle.</summary>
    ForgedInOneHeat = 1,

    /// <summary>No sample anywhere in the trace crossed the scorch threshold.</summary>
    NeverScorched = 2,

    /// <summary>The tail (quench-zone) samples tracked the plunge tightly.</summary>
    PerfectQuench = 4,

    /// <summary>The trace touched a crack (a hard cool-down mid-forge) or a scorch, yet the
    /// craft still finished at Fine-equivalent grade or better.</summary>
    RecoveredFromTheBrink = 8,
}

/// <summary>The scorer's verdict for one forge, all per-mille integers except
/// <paramref name="Moments"/> (an <see cref="ForgeMoment"/> bitflag set cast to int).
/// <paramref name="SubScores"/> is EXACTLY smelt, forge, quench, in that order — the wiring
/// unit stamps these onto <c>Item.CraftSubScores</c>, and <see cref="ArtifactSigning.Qualifies"/>
/// requires exactly 3 entries each &gt;= <see cref="ArtifactSigning.SubScoreThreshold"/> to sign.</summary>
public readonly record struct ForgeScore(int GradePermille, ImmutableList<int> SubScores, int Moments);

/// <summary>
/// Wave 5 (U23b, tactile forge / "Anvil Map"): the blacksmith's PURE in-sim puzzle scorer
/// (PKD1 dual-mode seam), the blacksmith analog of <c>AlchemyPuzzleScorer</c>. Regenerates the
/// SAME target line the Godot overlay drew (<see cref="ForgePath"/>) and grades the player's
/// captured cursor/strike trace against it. Integer-only, RNG-free, wall-clock-free,
/// transcendental-<c>Math.*</c>-free (KTD2/KTD4) — same trace in, same grade out, forever.
///
/// <para><b>The rule:</b> every sample's deviation from the target heat
/// (<c>|y - ForgePath.HeatAt(path, x)|</c>) is bucketed into one of three zones by its x
/// (smelt/forge/quench, the same thirds <see cref="ForgePath"/> shapes) and converted to a
/// per-mille sub-score (1000 = zero deviation, falling off linearly, floored at 0). The forge
/// zone additionally folds in strike tempo accuracy, scored over EVERY strike in the trace and
/// averaged by the TOTAL strike count (U6, Wave "verify by playing") — not gated to strikes whose
/// x happens to land inside the forge x-window. A strike's tempo error is already a self-contained
/// off-beat measure; gating it by x additionally coupled the scorer to how many total strikes a
/// craft used (fewer strikes -&gt; fewer of them randomly land in the window -&gt; the average is
/// drawn from a shrinking, noisier sample) instead of to how well the player actually kept tempo.
/// A forge with no strikes at all still scores poorly there by construction, mirroring
/// <see cref="ForgeTraceInput.Strikes"/>'s own contract. The three sub-scores fold 300/400/300
/// into the final grade.</para>
///
/// <para><b>Talent assists</b> mirror the blacksmith's <see cref="ProfessionDefinition.MinigameAssists"/>
/// exactly as documented on <see cref="Professions.ProfessionRegistry.Blacksmith"/>: Keen Eye
/// widens the smelt/quench sweet zone (forgives deviation there), Master's Touch slows drift
/// (forgives deviation everywhere), Legendary Craft forgives off-beat forge strikes, and Weapon
/// Specialist adds its own sweet-zone width but ONLY on Weapon recipes — the same slot-scoping
/// pattern as the alchemist's Potent Brews.</para>
///
/// <para><b>Forgiveness SCALES the penalty; it never erases it (owner ruling 2026-09-03,
/// <c>MAKERS-MARK.md</c> §11.7.11) — this supersedes the "mastery means certainty" ruling of the
/// same date.</b> Every forgiveness axis used to be SUBTRACTIVE with a zero floor
/// (<c>max(0, dev - forgiveness)</c>), and that shape has a dead zone: every deviation at or under
/// the accumulated forgiveness scored IDENTICALLY to a flawless one. Talents therefore did not
/// compress the skill range, they DELETED the bottom of it, and as talents stacked the dead zone
/// swallowed the whole realistic error band — measured across 20 seeds x 100 days under two
/// opposite talent-pacing policies, accuracy stopped changing the grade at all by about day 6.
/// The rule is now proportional: <c>penalty = dev * retained * DevScale / 1000</c>, where
/// <c>retained</c> is the share of the penalty that survives forgiveness
/// (see <see cref="RetainedPermille"/>). The consequences are the point —
/// <list type="bullet">
///   <item><description><b>Ordering is total and permanent.</b> A strictly worse swing always
///   scores strictly worse, at EVERY talent level, because the slope is never zero (see
///   <see cref="MaxForgivenessPermille"/>). Accuracy sets the ceiling forever.</description></item>
///   <item><description><b>Talents raise the floor instead.</b> The same mistake costs a master
///   less than a novice, and the gap WIDENS as the swing gets worse — which is exactly when a
///   safety net should be worth something. Talents stay clearly worth unlocking.</description></item>
///   <item><description><b>An untalented smith is scored exactly as before.</b> Zero forgiveness
///   leaves <c>retained</c> at 1000 and the formula collapses to the old <c>dev * DevScale</c> —
///   a mathematical identity, which is why every zero-talent pin in the suite is unchanged.</description></item>
/// </list></para>
/// </summary>
public static class ForgeScorer
{
    private const int SmeltZoneEnd = ForgePath.SmeltZoneEnd; // 333
    private const int ForgeZoneEnd = ForgePath.ForgeZoneEnd; // 666

    /// <summary>Deviation-to-score slope: a sub-score hits 0 once effective deviation reaches
    /// 1000/DevScale = 250 per-mille.</summary>
    private const int DevScale = 4;

    /// <summary>
    /// Converts one per-mille point of a talent's <see cref="MinigameAssist"/> width into
    /// PROPORTIONAL penalty relief — each point scales the deviation penalty down by this many
    /// per-mille, rather than cancelling that many per-mille of deviation outright. Chosen against
    /// the measured sweep (see the class doc): it leaves a fully-talented smith's average swing
    /// clearly ahead of an untalented one on the same trace, while keeping a sloppy swing visibly
    /// worse than a clean one at every talent level.
    /// </summary>
    private const int ForgivenessGain = 3;

    /// <summary>
    /// The hard ceiling on accumulated proportional relief, in per-mille. At least 250 per-mille of
    /// EVERY deviation therefore always survives, at every talent level, forever. This is the
    /// structural guarantee that the dead zone cannot return: the deviation-to-score slope can
    /// never reach zero, so a worse swing can never score equal-or-better through talent alone. A
    /// future talent that pushes accumulated forgiveness past this cap buys nothing further — which
    /// is deliberate, and is the invariant <c>ForgeScorerTests</c>' ordering theory pins.
    /// </summary>
    private const int MaxForgivenessPermille = 750;

    /// <summary>Any sample above this y anywhere in the trace counts as scorched.</summary>
    private const int ScorchThreshold = 900;

    /// <summary>A forge-zone sample below this y counts as a crack risk (heat dropped hard
    /// while the piece was already being shaped).</summary>
    private const int CrackThreshold = 400;

    /// <summary>The working-band entry threshold the one-heat tracker watches for.</summary>
    private const int OneHeatEntryY = 650;

    /// <summary>Average quench-zone effective deviation must fall under this to count as a
    /// perfect quench — a tight tail-tracking tolerance.</summary>
    private const int PerfectQuenchDevThreshold = 50;

    private const int SmeltWeight = 300;
    private const int ForgeWeight = 400;
    private const int QuenchWeight = 300;

    /// <summary>The active-model Fine-band floor (<see cref="QualityRoller.RollActive"/>'s
    /// per-mille threshold table) — the bar <see cref="ForgeMoment.RecoveredFromTheBrink"/>
    /// requires a brink-touching forge to still clear.</summary>
    private const int FineEquivalentThreshold = 550;

    /// <summary>
    /// Score one forge. Pure and total: any trace value (null/empty/odd-length lists, out-of-
    /// range coordinates) maps to a valid <see cref="ForgeScore"/> — never a throw.
    /// </summary>
    public static ForgeScore Score(
        Recipe recipe, ForgeTraceInput trace, ImmutableSortedSet<string> unlockedTalents, ProfessionDefinition profession)
    {
        var path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, trace.PathSeed);
        var (sweetZoneBonus, driftReduction, offBeatForgiveness) = AssistBonuses(profession, unlockedTalents, recipe.Slot);

        // Forgiveness is loop-invariant, so each axis's retained-penalty multiplier is resolved
        // ONCE here rather than per sample. Smelt and quench share an axis (sweet zone + drift),
        // the forge sample axis sees drift only, and strikes see off-beat forgiveness only — the
        // same three-axis mapping the subtractive rule used, now expressed as a proportion.
        var smeltQuenchRetained = RetainedPermille(sweetZoneBonus + driftReduction);
        var forgeRetained = RetainedPermille(driftReduction);
        var strikeRetained = RetainedPermille(offBeatForgiveness);

        var samples = trace.Samples ?? ImmutableList<int>.Empty;
        var strikes = trace.Strikes ?? ImmutableList<int>.Empty;

        var samplePairCount = samples.Count / 2; // a trailing odd int is defensively dropped

        var smeltSum = 0;
        var smeltCount = 0;
        var forgeSampleSum = 0;
        var forgeSampleCount = 0;
        var quenchSum = 0;
        var quenchCount = 0;
        var quenchDevSum = 0L;
        var quenchDevCount = 0;

        var maxY = int.MinValue;
        var touchedCrackOrScorch = false;
        var risingEdges = 0;
        var wasBelowEntry = true; // nothing seen yet — the first crossing into-band counts

        for (var i = 0; i < samplePairCount; i++)
        {
            var x = samples[i * 2];
            var y = samples[i * 2 + 1];

            if (y > maxY)
            {
                maxY = y;
            }

            if (y > ScorchThreshold)
            {
                touchedCrackOrScorch = true;
            }

            var target = ForgePath.HeatAt(path, x);
            // Widened to long BEFORE the subtraction: a hostile out-of-range y can overflow
            // `y - target` as ints, and Math.Abs(int.MinValue) throws — this scorer is
            // contractually total for ANY trace value (see Score's own doc comment).
            var dev = Math.Abs((long)y - target);

            if (x <= SmeltZoneEnd)
            {
                smeltSum += SubscoreFor(dev, smeltQuenchRetained);
                smeltCount++;
                TrackOneHeat(y, ref wasBelowEntry, ref risingEdges);
            }
            else if (x <= ForgeZoneEnd)
            {
                forgeSampleSum += SubscoreFor(dev, forgeRetained);
                forgeSampleCount++;

                if (y < CrackThreshold)
                {
                    touchedCrackOrScorch = true;
                }

                TrackOneHeat(y, ref wasBelowEntry, ref risingEdges);
            }
            else
            {
                quenchSum += SubscoreFor(dev, smeltQuenchRetained);
                quenchCount++;
                quenchDevSum += ScaledDev(dev, smeltQuenchRetained);
                quenchDevCount++;
            }
        }

        // Every strike counts, regardless of where along the shape axis it landed (U6): a
        // strike's tempoError is already a self-contained off-beat measure, so gating it by x
        // only shrank the averaged sample as total strike count fell — see the class doc.
        var strikePairCount = strikes.Count / 2; // a trailing odd int is defensively dropped
        var forgeStrikeSum = 0;
        for (var i = 0; i < strikePairCount; i++)
        {
            var tempoError = strikes[i * 2 + 1];
            forgeStrikeSum += SubscoreFor(Math.Max(0L, tempoError), strikeRetained);
        }

        var forgeStrikeCount = strikePairCount;

        var smeltScore = smeltCount > 0 ? smeltSum / smeltCount : 0;
        var forgeSampleAvg = forgeSampleCount > 0 ? forgeSampleSum / forgeSampleCount : 0;
        // No strikes at all scores the strike axis at floor — a forge with no strikes simply
        // scores poorly (the same contract ForgeTraceInput.Strikes documents).
        var forgeStrikeAvg = forgeStrikeCount > 0 ? forgeStrikeSum / forgeStrikeCount : 0;
        var forgeScore = (forgeSampleAvg + forgeStrikeAvg) / 2;
        var quenchScore = quenchCount > 0 ? quenchSum / quenchCount : 0;

        var grade = (smeltScore * SmeltWeight + forgeScore * ForgeWeight + quenchScore * QuenchWeight) / 1000;
        grade = Math.Clamp(grade, 0, 1000);

        var perfectQuench = quenchDevCount > 0 && quenchDevSum / quenchDevCount < PerfectQuenchDevThreshold;
        var neverScorched = maxY <= ScorchThreshold; // maxY stays int.MinValue (<=) when no samples
        var oneHeat = risingEdges <= 1;
        var recovered = touchedCrackOrScorch && grade >= FineEquivalentThreshold;

        var moments = ForgeMoment.None;
        if (oneHeat)
        {
            moments |= ForgeMoment.ForgedInOneHeat;
        }

        if (neverScorched)
        {
            moments |= ForgeMoment.NeverScorched;
        }

        if (perfectQuench)
        {
            moments |= ForgeMoment.PerfectQuench;
        }

        if (recovered)
        {
            moments |= ForgeMoment.RecoveredFromTheBrink;
        }

        var subScores = ImmutableList.Create(smeltScore, forgeScore, quenchScore);
        return new ForgeScore(grade, subScores, (int)moments);
    }

    /// <summary>Tracks whether the heat has crossed INTO the working band from below; each such
    /// crossing is a "heating event". More than one means the piece cooled off and had to be
    /// reheated — not a one-heat forge.</summary>
    private static void TrackOneHeat(int y, ref bool wasBelowEntry, ref int risingEdges)
    {
        if (wasBelowEntry && y >= OneHeatEntryY)
        {
            risingEdges++;
            wasBelowEntry = false;
        }
        else if (y < OneHeatEntryY)
        {
            wasBelowEntry = true;
        }
    }

    /// <summary>
    /// The share of a deviation's penalty that SURVIVES <paramref name="forgiveness"/>, in
    /// per-mille — the whole of the proportional rule (see the class doc). Zero forgiveness returns
    /// 1000, so an untalented smith is scored by exactly the pre-ruling <c>dev * DevScale</c> slope;
    /// the cap keeps the return value at or above 250, so the slope is never flat and the dead zone
    /// can never re-form.
    /// </summary>
    private static int RetainedPermille(int forgiveness) =>
        1000 - Math.Clamp(forgiveness * ForgivenessGain, 0, MaxForgivenessPermille);

    /// <summary>
    /// One sample's per-mille sub-score. The division comes LAST, on purpose: scaling
    /// <paramref name="dev"/> first and rounding to an integer would truncate small deviations to
    /// the same value and re-introduce a (much smaller, but real) dead zone at high forgiveness.
    /// Ordering is only ever as fine-grained as this arithmetic, so it keeps the full precision.
    /// </summary>
    private static int SubscoreFor(long dev, int retainedPermille) =>
        (int)Math.Clamp(1000L - (dev * retainedPermille * DevScale / 1000L), 0L, 1000L);

    /// <summary>The forgiveness-scaled deviation itself rather than its score — what
    /// <see cref="ForgeMoment.PerfectQuench"/> averages against
    /// <see cref="PerfectQuenchDevThreshold"/>.</summary>
    private static long ScaledDev(long dev, int retainedPermille) => dev * retainedPermille / 1000L;

    /// <summary>
    /// Sums the unlocked blacksmith talents' <see cref="MinigameAssist"/> fields into the three
    /// forgiveness axes the scorer applies. Weapon Specialist's contribution is scoped to
    /// <see cref="ItemSlot.Weapon"/> recipes only, mirroring the alchemist's Consumable-scoped
    /// Potent Brews — a locked or wrongly-scoped node contributes nothing.
    /// </summary>
    private static (int SweetZoneBonus, int DriftReduction, int OffBeatForgiveness) AssistBonuses(
        ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, ItemSlot recipeSlot)
    {
        var sweetZone = 0;
        var drift = 0;
        var offBeat = 0;

        foreach (var (nodeId, assist) in profession.MinigameAssists)
        {
            if (!unlockedTalents.Contains(nodeId))
            {
                continue;
            }

            if (nodeId == TalentTree.WeaponSpecialist && recipeSlot != ItemSlot.Weapon)
            {
                continue;
            }

            sweetZone += assist.SweetZoneWidthBonus;
            drift += assist.DriftRateReduction;
            offBeat += assist.OffBeatForgiveness;
        }

        return (sweetZone, drift, offBeat);
    }
}
