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
/// zone additionally folds in strike tempo accuracy — a forge with no strikes at all scores
/// poorly there by construction, mirroring <see cref="ForgeTraceInput.Strikes"/>'s own contract.
/// The three sub-scores fold 300/400/300 into the final grade.</para>
///
/// <para><b>Talent assists</b> mirror the blacksmith's <see cref="ProfessionDefinition.MinigameAssists"/>
/// exactly as documented on <see cref="Professions.ProfessionRegistry.Blacksmith"/>: Keen Eye
/// widens the smelt/quench sweet zone (forgives deviation there), Master's Touch slows drift
/// (forgives deviation everywhere), Legendary Craft forgives off-beat forge strikes, and Weapon
/// Specialist adds its own sweet-zone width but ONLY on Weapon recipes — the same slot-scoping
/// pattern as the alchemist's Potent Brews.</para>
/// </summary>
public static class ForgeScorer
{
    private const int SmeltZoneEnd = ForgePath.SmeltZoneEnd; // 333
    private const int ForgeZoneEnd = ForgePath.ForgeZoneEnd; // 666

    /// <summary>Deviation-to-score slope: a sub-score hits 0 once effective deviation reaches
    /// 1000/DevScale = 250 per-mille.</summary>
    private const int DevScale = 4;

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

        var samples = trace.Samples ?? ImmutableList<int>.Empty;
        var strikes = trace.Strikes ?? ImmutableList<int>.Empty;

        var samplePairCount = samples.Count / 2; // a trailing odd int is defensively dropped

        var smeltSum = 0;
        var smeltCount = 0;
        var forgeSampleSum = 0;
        var forgeSampleCount = 0;
        var quenchSum = 0;
        var quenchCount = 0;
        var quenchDevSum = 0;
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
            var dev = Math.Abs(y - target);

            if (x <= SmeltZoneEnd)
            {
                var devEff = Math.Max(0, dev - sweetZoneBonus - driftReduction);
                smeltSum += SubscoreFor(devEff);
                smeltCount++;
                TrackOneHeat(y, ref wasBelowEntry, ref risingEdges);
            }
            else if (x <= ForgeZoneEnd)
            {
                var devEff = Math.Max(0, dev - driftReduction);
                forgeSampleSum += SubscoreFor(devEff);
                forgeSampleCount++;

                if (y < CrackThreshold)
                {
                    touchedCrackOrScorch = true;
                }

                TrackOneHeat(y, ref wasBelowEntry, ref risingEdges);
            }
            else
            {
                var devEff = Math.Max(0, dev - sweetZoneBonus - driftReduction);
                quenchSum += SubscoreFor(devEff);
                quenchCount++;
                quenchDevSum += devEff;
                quenchDevCount++;
            }
        }

        var strikePairCount = strikes.Count / 2; // a trailing odd int is defensively dropped
        var forgeStrikeSum = 0;
        var forgeStrikeCount = 0;
        for (var i = 0; i < strikePairCount; i++)
        {
            var x = strikes[i * 2];
            var tempoError = strikes[i * 2 + 1];

            if (x <= SmeltZoneEnd || x > ForgeZoneEnd)
            {
                continue; // only forge-beat strikes count — the same zone the samples fold into
            }

            var penalty = Math.Max(0, tempoError - offBeatForgiveness);
            forgeStrikeSum += SubscoreFor(penalty);
            forgeStrikeCount++;
        }

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

    private static int SubscoreFor(int devEff) => Math.Clamp(1000 - devEff * DevScale, 0, 1000);

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
