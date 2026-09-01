using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Crafting;

/// <summary>
/// Pure quality roll (R4), now profession-parameterized (P1): the talent-driven shifts are
/// read from the profession's <see cref="ProfessionQualityModel"/> instead of hardcoded
/// blacksmith node ids. The universal quality math — the ±8-per-grade material step and the
/// grade threshold table — is shared by every profession and stays here. Integer math only,
/// exactly ONE <see cref="IDeterministicRng.Roll100"/> draw per craft — draw count is part of
/// the determinism contract (KTD4). Iterating the shift maps consumes no RNG and, being
/// integer addition, is order-independent, so the distribution is byte-identical.
///
/// THE THRESHOLD TABLE (tests assert this exact table — change both together):
///
///   effective = Roll100() + shift          // Roll100 is uniform in [0, 100)
///
///   shift = 8 * (materialGrade + (material-mastery node unlocked ? 1 : 0) - recipe.Tier)
///         + sum of quality.FlatShifts[node] for each unlocked flat node
///         + sum of quality.SlotShifts[node].Shift for each unlocked slot node whose slot matches
///
///   grade:  effective &lt;= 14   → Poor
///           15 .. 64          → Common
///           65 .. 89          → Fine
///           90 .. 98          → Superior
///           effective &gt;= 99   → Masterwork
///
/// For the blacksmith the model reproduces the exact original numbers (keen-eye +5,
/// master-touch +7, legendary-craft +8, weapon-specialist +5 on weapons, material-mastery
/// +1 grade). Base odds at shift 0 (material grade == recipe tier, no quality talents):
/// Poor 15%, Common 50%, Fine 25%, Superior 9%, Masterwork 1%. Each material grade above
/// (below) the recipe tier shifts the roll +8 (-8). Nodes not in the quality model (material
/// efficiency, tier unlocks) never touch the roll, and locked nodes contribute nothing: only
/// ids present in <paramref name="unlockedTalents"/> count.
/// </summary>
public static class QualityRoller
{
    /// <summary>Half-width of the performance-grade shift band (M3): grade 0 → −8, 500 → 0,
    /// 1000 → +8 — deliberately the weight of ONE material-grade step, so a perfect minigame
    /// equals one grade of better ore, never dominating the roll.</summary>
    private const int PerformanceShiftMax = 8;

    public static QualityGrade Roll(
        Recipe recipe, int materialGrade, ImmutableSortedSet<string> unlockedTalents, ProfessionQualityModel quality,
        IDeterministicRng rng, int? performanceGrade = null, ITraceSink? traceSink = null)
    {
        var masteryGrade = quality.MaterialMasteryNode is { } mastery && unlockedTalents.Contains(mastery) ? 1 : 0;
        var effectiveGrade = materialGrade + masteryGrade;
        var shift = 8 * (effectiveGrade - recipe.Tier);

        if (performanceGrade is { } grade)
        {
            // M3 seam: clamp to per-mille, center on 500, scale to ±PerformanceShiftMax.
            // Integer math; null (no minigame) adds nothing — byte-identical to pre-M3.
            var clamped = grade < 0 ? 0 : grade > 1000 ? 1000 : grade;
            shift += (clamped - 500) * PerformanceShiftMax * 2 / 1000;
        }

        foreach (var (nodeId, amount) in quality.FlatShifts)
        {
            if (unlockedTalents.Contains(nodeId))
            {
                shift += amount;
            }
        }

        foreach (var (nodeId, slotShift) in quality.SlotShifts)
        {
            if (recipe.Slot == slotShift.Slot && unlockedTalents.Contains(nodeId))
            {
                shift += slotShift.Shift;
            }
        }

        var effective = rng.Roll100() + shift;
        var resultGrade = effective switch
        {
            <= 14 => QualityGrade.Poor,
            <= 64 => QualityGrade.Common,
            <= 89 => QualityGrade.Fine,
            <= 98 => QualityGrade.Superior,
            _ => QualityGrade.Masterwork,
        };

        // U-T6: the shift and the effective roll it produced are the entire reason this recipe
        // landed on this grade — CraftingHandlers/HeirloomHandlers only ever kept the bare enum.
        traceSink?.Trace(new DecisionTrace(
            "quality-roll", resultGrade.ToString(), $"roll+shift {effective} against the passive threshold table",
            $"shift={shift} effective={effective} materialGrade={materialGrade} recipeTier={recipe.Tier}"));

        return resultGrade;
    }

    // ==================================================================================
    // ACTIVE MODEL (PA2/PKD2/PKD3/PKD4) — blacksmith only in Phase A. Skill (the captured
    // PerformanceGrade) DOMINATES quality; RNG shrinks to a floor jitter that can never skip
    // a whole band; material sets a hard ceiling; talents no longer shift this roll at all
    // (they became MinigameAssist data instead — see ProfessionDefinition). The PASSIVE
    // Roll() method above is completely untouched by this addition.
    // ==================================================================================

    /// <summary>Half-width of the active-model jitter band, per-mille (PKD3): the single
    /// <see cref="IDeterministicRng.Roll100"/> draw maps to exactly [-25, +25].</summary>
    private const int ActiveJitterMax = 25;

    /// <summary>Auto-craft's competent-but-capped grade (PKD4): a null <c>PerformanceGrade</c>
    /// (and, once Phase B lands puzzle-scored professions, a null <c>CraftPuzzleInput</c> too —
    /// Phase A's puzzle is always null) resolves here rather than at the player's real skill.</summary>
    private const int AutoCraftGrade = 800;

    /// <summary>
    /// The active-model dominance roll (PA2/PKD3): <paramref name="performanceGrade"/> (a
    /// per-mille [0..1000] captured minigame result; <see langword="null"/> = auto-craft,
    /// PKD4) is clamped and jittered by the SINGLE <see cref="IDeterministicRng.Roll100"/> draw
    /// — draw count is unchanged from the passive path (KTD4). Grade bands are read off the
    /// jittered value (THE TABLE, tests pin these exact numbers):
    ///
    ///   effective = clamp(performanceGrade ?? AutoCraftGrade, 0, 1000) + jitter   // AutoCraftGrade = 800
    ///   jitter    = Roll100() * 51 / 100 - 25                     // maps [0,99] -> [-25, +25]
    ///
    ///   band:  effective &lt;  200  → Poor
    ///          effective &lt;  550  → Common
    ///          effective &lt;  780  → Fine
    ///          effective &lt;  930  → Superior
    ///          effective &gt;= 930  → Masterwork
    ///
    /// Every band (excluding the two open ends) is at least 150 per-mille wide — comfortably
    /// wider than the 50-wide jitter swing — so a single roll can shift the result into an
    /// ADJACENT band at a seam but can never SKIP an entire band on its own.
    ///
    /// Material sets a hard ceiling from <c>materialGrade + mastery − recipe.Tier</c>: at or
    /// below -1 caps Fine; exactly 0 caps Superior; +1 or above is uncapped. Talents no longer
    /// shift this roll at all (<see cref="ProfessionQualityModel.FlatShifts"/>/<see
    /// cref="ProfessionQualityModel.SlotShifts"/> are never consulted here — PKD3's
    /// double-count fix) — only <see cref="ProfessionQualityModel.MaterialMasteryNode"/>
    /// still matters, and only for the ceiling, not the roll.
    ///
    /// Auto-craft (<paramref name="performanceGrade"/> is <see langword="null"/>) additionally
    /// hard-caps at Superior regardless of jitter or future constant drift — belt and braces:
    /// the minigame is the only road to Masterwork (PKD4).
    /// </summary>
    public static QualityGrade RollActive(
        Recipe recipe, int materialGrade, ImmutableSortedSet<string> unlockedTalents, ProfessionQualityModel quality,
        IDeterministicRng rng, int? performanceGrade = null, ITraceSink? traceSink = null)
    {
        var isAutoCraft = performanceGrade is null;
        var grade = performanceGrade ?? AutoCraftGrade;
        var clamped = grade < 0 ? 0 : grade > 1000 ? 1000 : grade;

        // Exactly one Roll100 draw, same as the passive path (KTD4).
        var roll = rng.Roll100();
        var jitter = (roll * (ActiveJitterMax * 2 + 1) / 100) - ActiveJitterMax;
        var effective = clamped + jitter;

        var band = BandFor(effective);
        var preCeilingBand = band;

        var masteryGrade = quality.MaterialMasteryNode is { } mastery && unlockedTalents.Contains(mastery) ? 1 : 0;
        var materialStep = materialGrade + masteryGrade - recipe.Tier;
        if (MaterialCeiling(materialStep) is { } ceiling && band > ceiling)
        {
            band = ceiling;
        }

        if (isAutoCraft && band > QualityGrade.Superior)
        {
            band = QualityGrade.Superior;
        }

        // U-T6: the jittered roll AND whether a material/auto-craft ceiling clipped it are the
        // whole story of why this craft landed where it did — CraftingHandlers/HeirloomHandlers
        // only ever kept the bare enum, so a Fine that got capped from a would-be Masterwork read
        // identically to a Fine the roll actually earned.
        traceSink?.Trace(new DecisionTrace(
            "quality-roll", band.ToString(),
            band == preCeilingBand
                ? $"jittered roll {effective} landed in-band, no ceiling applied"
                : $"jittered roll {effective} would have been {preCeilingBand} before the material/auto-craft ceiling capped it",
            $"performanceGrade={grade} jitter={jitter} effective={effective} materialStep={materialStep} isAutoCraft={isAutoCraft}"));

        return band;
    }

    private static QualityGrade BandFor(int effective) => effective switch
    {
        < 200 => QualityGrade.Poor,
        < 550 => QualityGrade.Common,
        < 780 => QualityGrade.Fine,
        < 930 => QualityGrade.Superior,
        _ => QualityGrade.Masterwork,
    };

    /// <summary>The material-grade ceiling (PKD3): <see langword="null"/> = uncapped.</summary>
    private static QualityGrade? MaterialCeiling(int materialStep) => materialStep switch
    {
        <= -1 => QualityGrade.Fine,
        0 => QualityGrade.Superior,
        _ => null,
    };

    // ==================================================================================
    // ACTIVE-CRAFT DEPTH (Phase C, U-C2): "a rotation-policy ceiling over a heat-band
    // input" — heat-band strikes + seeded condition windows + a finite durability budget +
    // pity, folded into the SAME per-mille <c>performanceGrade</c> scale <see cref="RollActive"/>
    // already consumes. The heat-band/condition-window/durability MATH lives in the pure,
    // RNG-free <see cref="HeatBandForge"/> (this file just classifies its draws) — every
    // <see cref="IDeterministicRng.Roll100"/> call for this depth happens HERE, keeping the
    // sim's RNG surface at exactly three files.
    // ==================================================================================

    /// <summary>
    /// Simulate a rotation POLICY — a caller-supplied sequence of heat-band strike decisions
    /// (a scripted harness policy for balance-gate replay, or eventually the player's captured
    /// minigame input) — against the seeded condition-window draws and the material's finite
    /// durability budget (<see cref="HeatBandForge.DurabilityBudget"/>). Exactly ONE
    /// <see cref="IDeterministicRng.Roll100"/> draw per strike ACTUALLY thrown (the policy is
    /// truncated at the budget) — draw count scales with strikes, not a fixed KTD4 count, which
    /// is safe ONLY because this method is reachable exclusively from the player's active-craft
    /// minigame path: it is never called for auto-craft, so the idle golden trace never reaches
    /// it and the RNG stream it perturbs is never the one <c>BaselinePlayer</c> exercises.
    ///
    /// <para>Pity resets on any Good-or-better window (natural or forced); it does not reset on
    /// a Normal strike. Returns the per-mille execution grade — feed it straight into
    /// <see cref="RollActive"/> as <c>performanceGrade</c>; that call's own existing jitter/
    /// material-ceiling logic is untouched by this method.</para>
    /// </summary>
    public static int SimulateActiveForge(ImmutableList<bool> strikePolicy, int materialGrade, IDeterministicRng rng)
    {
        var policy = strikePolicy ?? ImmutableList<bool>.Empty;
        var budget = HeatBandForge.DurabilityBudget(materialGrade);

        var strikesThrown = 0;
        var qualityWeightedProgress = 0;
        var strikesSincePity = 0;

        foreach (var inHeatBand in policy)
        {
            if (strikesThrown >= budget)
            {
                break;
            }

            strikesThrown++;

            var roll = rng.Roll100();
            var window = HeatBandForge.ClassifyWindow(roll, strikesSincePity);
            strikesSincePity = HeatBandForge.ResetsPity(window) ? 0 : strikesSincePity + 1;

            qualityWeightedProgress += HeatBandForge.ProgressFor(inHeatBand) * HeatBandForge.MultiplierFor(window);
        }

        return HeatBandForge.ExecutionGradePermille(qualityWeightedProgress, strikesThrown, budget);
    }
}
