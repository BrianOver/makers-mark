using System;

namespace GameSim.Crafting;

/// <summary>
/// Phase C (U-C2, active-craft depth): the PURE heat-band / condition-window / durability
/// model behind the player's forge minigame — "a rotation-policy ceiling over a heat-band
/// input" (FFXIV-style seeded condition windows layered on a RuneScape-style heat-band
/// strike). Every member here is integer math with ZERO RNG (KTD2) — this file never touches
/// <c>IDeterministicRng</c> at all. The one place RNG enters this depth (the per-strike
/// condition-window draw) lives in <see cref="QualityRoller.SimulateActiveForge"/>, which is
/// one of the sim's exactly-three permitted RNG call sites; this file only classifies an
/// ALREADY-DRAWN <c>Roll100</c> value and folds strikes into a final grade, so it can never
/// grow a fourth draw site by accident.
///
/// <para><b>The rotation-policy contract:</b> a "policy" is a caller-supplied sequence of
/// heat-band hit/miss decisions (one bool per strike attempt — <see langword="true"/> = the
/// strike landed inside the working heat band). Timing the strike into the band is entirely
/// the player's/policy's skill and carries NO randomness; only the condition window
/// (Normal/Good/Perfect) that decorates each strike is seeded. This mirrors
/// <c>Harness/BaselinePlayer</c>'s scripted-policy pattern one level down: same policy + same
/// seed replays to the same grade, forever.</para>
/// </summary>
public static class HeatBandForge
{
    /// <summary>Progress earned for a strike landed inside the heat band.</summary>
    public const int InBandProgress = 2;

    /// <summary>Progress earned for a strike landed outside the heat band.</summary>
    public const int OutOfBandProgress = 1;

    /// <summary>Quality multiplier for a Normal-condition strike (no window).</summary>
    public const int NormalQualityMultiplier = 1;

    /// <summary>Quality multiplier for a Good-condition strike.</summary>
    public const int GoodQualityMultiplier = 2;

    /// <summary>Quality multiplier for a Perfect-condition strike.</summary>
    public const int PerfectQualityMultiplier = 4;

    /// <summary>Perfect-window width on a Roll100 draw (0..99): rolls &lt; this are Perfect —
    /// exactly 5%.</summary>
    public const int PerfectWindowRoll100Threshold = 5;

    /// <summary>Good-window ceiling on a Roll100 draw, stacked directly above the Perfect
    /// band: rolls in [<see cref="PerfectWindowRoll100Threshold"/>, this) are Good — exactly
    /// 25% width, so one draw resolves to exactly one of {Perfect 5%, Good 25%, Normal 70%}.</summary>
    public const int GoodWindowRoll100Threshold = 30;

    /// <summary>Pity (PA/PKD-style): once this many consecutive strikes have passed without a
    /// Good-or-better window, the NEXT strike is forced to at least Good regardless of its
    /// roll — guarantees a Good within every 4 strikes. Short-term random, long-term
    /// deterministic: the expected grade over a long policy is exactly computable.</summary>
    public const int PityStrikeLimit = 4;

    /// <summary>Base durability budget at the lowest material grade (1): the finite number of
    /// strikes the piece can absorb before it's spent.</summary>
    private const int BaseDurabilityBudget = 6;

    /// <summary>Extra strikes granted per material grade above 1 — better material forgives
    /// more attempts before the piece gives out.</summary>
    private const int DurabilityPerGradeStep = 2;

    /// <summary>Hard ceiling so an arbitrarily high registered material grade can never make a
    /// forge effectively endless.</summary>
    private const int MaxDurabilityBudget = 40;

    /// <summary>The condition-window classification for one strike.</summary>
    public enum ConditionWindow
    {
        Normal,
        Good,
        Perfect,
    }

    /// <summary>
    /// Durability budget for a material grade: the finite number of strikes a piece of this
    /// material can take before it's spent. Grade is floored at 1 (a below-range/invalid grade
    /// never yields a negative or zero budget); each grade step above 1 adds
    /// <see cref="DurabilityPerGradeStep"/> strikes, capped at <see cref="MaxDurabilityBudget"/>.
    /// Pure, total, never throws.
    /// </summary>
    public static int DurabilityBudget(int materialGrade)
    {
        var grade = Math.Max(materialGrade, 1);
        var budget = BaseDurabilityBudget + (grade - 1) * DurabilityPerGradeStep;
        return Math.Min(budget, MaxDurabilityBudget);
    }

    /// <summary>
    /// Classify an ALREADY-DRAWN Roll100 value (0..99) into a condition window, honoring pity:
    /// when <paramref name="strikesSincePityWindow"/> has already reached
    /// <see cref="PityStrikeLimit"/> - 1 (i.e. this strike is the 4th in a row without a
    /// Good-or-better), the window is forced to Good regardless of the roll. Otherwise Perfect
    /// wins the bottom <see cref="PerfectWindowRoll100Threshold"/> rolls, Good the next band up
    /// to <see cref="GoodWindowRoll100Threshold"/>, and everything else is Normal. Pure, total —
    /// an out-of-[0,100) roll is clamped defensively rather than throwing.
    /// </summary>
    public static ConditionWindow ClassifyWindow(int roll100, int strikesSincePityWindow)
    {
        if (strikesSincePityWindow >= PityStrikeLimit - 1)
        {
            return ConditionWindow.Good;
        }

        var roll = Math.Clamp(roll100, 0, 99);
        if (roll < PerfectWindowRoll100Threshold)
        {
            return ConditionWindow.Perfect;
        }

        if (roll < GoodWindowRoll100Threshold)
        {
            return ConditionWindow.Good;
        }

        return ConditionWindow.Normal;
    }

    /// <summary>Whether a classified window counts toward resetting the pity counter (Good or
    /// Perfect both reset it — only a Normal strike advances the drought).</summary>
    public static bool ResetsPity(ConditionWindow window) => window != ConditionWindow.Normal;

    /// <summary>Quality multiplier for a classified window (THE table this whole depth adds:
    /// Normal x1, Good x2, Perfect x4).</summary>
    public static int MultiplierFor(ConditionWindow window) => window switch
    {
        ConditionWindow.Perfect => PerfectQualityMultiplier,
        ConditionWindow.Good => GoodQualityMultiplier,
        _ => NormalQualityMultiplier,
    };

    /// <summary>Progress earned for one strike from whether it landed in the heat band (THE
    /// other table: in-band 2, out-of-band 1).</summary>
    public static int ProgressFor(bool inHeatBand) => inHeatBand ? InBandProgress : OutOfBandProgress;

    /// <summary>
    /// Fold a finished forge's accumulated quality-weighted progress into a per-mille execution
    /// grade [0, 1000] — the same per-mille scale <see cref="QualityRoller.RollActive"/> already
    /// consumes as <c>performanceGrade</c>. <paramref name="qualityWeightedProgress"/> is the
    /// running sum of <c>ProgressFor(inBand) * MultiplierFor(window)</c> across every strike
    /// actually thrown; <paramref name="strikesThrown"/>/<paramref name="budget"/> bound the
    /// theoretical ceiling (every strike in-band AND Perfect). A forge that threw no strikes
    /// (or has no budget) grades 0 — pure, total, never throws.
    /// </summary>
    public static int ExecutionGradePermille(int qualityWeightedProgress, int strikesThrown, int budget)
    {
        if (strikesThrown <= 0 || budget <= 0)
        {
            return 0;
        }

        var ceiling = Math.Min(strikesThrown, budget) * InBandProgress * PerfectQualityMultiplier;
        if (ceiling <= 0)
        {
            return 0;
        }

        var grade = qualityWeightedProgress * 1000 / ceiling;
        return Math.Clamp(grade, 0, 1000);
    }
}
