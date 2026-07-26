using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Asserts Phase C's U-C2 active-craft depth: <see cref="HeatBandForge"/> (the pure heat-band /
/// condition-window / durability model) and <see cref="QualityRoller.SimulateActiveForge"/>
/// (the single place its per-strike condition-window RNG draw lives). Covers every "Tests"
/// bullet from the U-C2 plan: expected grade for a fixed policy, the pity floor, heat-band
/// progress, and harness policy replay.
/// </summary>
public class HeatBandForgeTests
{
    // ---- Durability budget (pure, no RNG) ---------------------------------------------------

    [Theory]
    [InlineData(1, 6)]   // base budget at the lowest grade
    [InlineData(2, 8)]   // +2 per grade step
    [InlineData(5, 14)]  // grade 5 (adamant): 6 + 4*2
    [InlineData(0, 6)]   // floored at grade 1
    [InlineData(-3, 6)]  // never negative/zero
    public void DurabilityBudget_ScalesWithMaterialGrade(int materialGrade, int expectedBudget)
    {
        Assert.Equal(expectedBudget, HeatBandForge.DurabilityBudget(materialGrade));
    }

    [Fact]
    public void DurabilityBudget_HardCapsAtMax()
    {
        Assert.Equal(40, HeatBandForge.DurabilityBudget(materialGrade: 1000));
    }

    // ---- Heat-band progress table (pure) ----------------------------------------------------

    [Fact]
    public void HeatBand_InBandStrike_Earns2Progress_OutOfBandEarns1()
    {
        Assert.Equal(2, HeatBandForge.ProgressFor(inHeatBand: true));
        Assert.Equal(1, HeatBandForge.ProgressFor(inHeatBand: false));
    }

    // ---- Condition-window classification (pure, given an already-drawn roll) ---------------

    [Theory]
    [InlineData(0, HeatBandForge.ConditionWindow.Perfect)]
    [InlineData(4, HeatBandForge.ConditionWindow.Perfect)]
    [InlineData(5, HeatBandForge.ConditionWindow.Good)]
    [InlineData(29, HeatBandForge.ConditionWindow.Good)]
    [InlineData(30, HeatBandForge.ConditionWindow.Normal)]
    [InlineData(99, HeatBandForge.ConditionWindow.Normal)]
    public void ClassifyWindow_Roll100Bands_MatchTheDocumentedSplit(int roll, HeatBandForge.ConditionWindow expected)
    {
        Assert.Equal(expected, HeatBandForge.ClassifyWindow(roll, strikesSincePityWindow: 0));
    }

    [Fact]
    public void ClassifyWindow_Multiplier_Table_NormalGoodPerfect()
    {
        Assert.Equal(1, HeatBandForge.MultiplierFor(HeatBandForge.ConditionWindow.Normal));
        Assert.Equal(2, HeatBandForge.MultiplierFor(HeatBandForge.ConditionWindow.Good));
        Assert.Equal(4, HeatBandForge.MultiplierFor(HeatBandForge.ConditionWindow.Perfect));
    }

    // ---- Pity floor: a guaranteed Good within 4 strikes -------------------------------------

    [Fact]
    public void ClassifyWindow_Pity_ForcesGood_OnThe4thConsecutiveNormalStrike()
    {
        // Strikes 1-3 (strikesSincePityWindow 0,1,2) roll a high, definitely-Normal value (90)
        // and stay Normal. Strike 4 (strikesSincePityWindow == 3 == PityStrikeLimit - 1) is
        // FORCED to Good even though the roll is still 90 — the guarantee.
        Assert.Equal(HeatBandForge.ConditionWindow.Normal, HeatBandForge.ClassifyWindow(90, strikesSincePityWindow: 0));
        Assert.Equal(HeatBandForge.ConditionWindow.Normal, HeatBandForge.ClassifyWindow(90, strikesSincePityWindow: 1));
        Assert.Equal(HeatBandForge.ConditionWindow.Normal, HeatBandForge.ClassifyWindow(90, strikesSincePityWindow: 2));
        Assert.Equal(HeatBandForge.ConditionWindow.Good, HeatBandForge.ClassifyWindow(90, strikesSincePityWindow: 3));
    }

    [Fact]
    public void ResetsPity_TrueForGoodAndPerfect_FalseForNormal()
    {
        Assert.False(HeatBandForge.ResetsPity(HeatBandForge.ConditionWindow.Normal));
        Assert.True(HeatBandForge.ResetsPity(HeatBandForge.ConditionWindow.Good));
        Assert.True(HeatBandForge.ResetsPity(HeatBandForge.ConditionWindow.Perfect));
    }

    [Fact]
    public void SimulateActiveForge_PityFires_WithinEveryFourStrikes_UnderAnAlwaysNormalRoll()
    {
        // FixedRoll(90) never naturally lands Good/Perfect (90 >= GoodWindowRoll100Threshold),
        // so WITHOUT pity every strike would be Normal (x1) forever. With pity, every 4th
        // in-band strike is forced Good (x2) — verify via the exact quality-weighted total.
        var rng = new FixedRoll(90);
        var policy = ImmutableList.CreateRange(Enumerable.Repeat(true, 8));

        var grade = QualityRoller.SimulateActiveForge(policy, materialGrade: 5, rng); // budget 14, all 8 strikes thrown

        // Strikes 1-3 Normal (x1), strike 4 pity-Good (x2), strikes 5-7 Normal (x1), strike 8 pity-Good (x2).
        // qualityWeighted = 2*(1+1+1+2+1+1+1+2) = 2*10 = 20. Ceiling = min(8,14)*2*4 = 64.
        // grade = 20*1000/64 = 312 (integer division).
        Assert.Equal(312, grade);
    }

    // ---- Expected grade for a fixed strike policy (deterministic, lockstep with a mirror) ---

    [Fact]
    public void SimulateActiveForge_FixedPolicy_ProducesTheExactMirroredGrade()
    {
        var seed = 4242UL;
        var policy = ImmutableList.Create(true, true, false, true, true, false, true, true, false, true);

        var rollerRng = new Pcg32(RngState.FromSeed(seed));
        var grade = QualityRoller.SimulateActiveForge(policy, materialGrade: 2, rollerRng); // budget 8

        // Independent mirror: replay the exact same policy/rng rules by hand.
        var mirrorRng = new Pcg32(RngState.FromSeed(seed));
        var budget = HeatBandForge.DurabilityBudget(2);
        Assert.Equal(8, budget);

        var strikesThrown = 0;
        var qualityWeighted = 0;
        var sincePity = 0;
        foreach (var inBand in policy)
        {
            if (strikesThrown >= budget)
            {
                break;
            }

            strikesThrown++;
            var window = HeatBandForge.ClassifyWindow(mirrorRng.Roll100(), sincePity);
            sincePity = HeatBandForge.ResetsPity(window) ? 0 : sincePity + 1;
            qualityWeighted += HeatBandForge.ProgressFor(inBand) * HeatBandForge.MultiplierFor(window);
        }

        var expectedGrade = HeatBandForge.ExecutionGradePermille(qualityWeighted, strikesThrown, budget);

        Assert.Equal(expectedGrade, grade);
        Assert.Equal(mirrorRng.Snapshot(), rollerRng.Snapshot()); // lockstep: exactly one draw per strike thrown
    }

    [Fact]
    public void SimulateActiveForge_PolicyLongerThanBudget_TruncatesAndDrawsNoMoreThanBudget()
    {
        var seed = 9UL;
        var policy = ImmutableList.CreateRange(Enumerable.Repeat(true, 50)); // far longer than any budget

        var rollerRng = new Pcg32(RngState.FromSeed(seed));
        QualityRoller.SimulateActiveForge(policy, materialGrade: 1, rollerRng); // budget 6

        var mirrorRng = new Pcg32(RngState.FromSeed(seed));
        for (var i = 0; i < 6; i++)
        {
            mirrorRng.Roll100(); // exactly 6 draws, never 50
        }

        Assert.Equal(mirrorRng.Snapshot(), rollerRng.Snapshot());
    }

    // ---- Harness policy replay: same policy + same seed = same grade, forever --------------

    [Fact]
    public void SimulateActiveForge_HarnessPolicyReplay_SameSeedSamePolicy_SameGrade()
    {
        var policy = ImmutableList.Create(true, false, true, true, false, true, true, true);

        var run1 = QualityRoller.SimulateActiveForge(policy, materialGrade: 3, new Pcg32(RngState.FromSeed(777)));
        var run2 = QualityRoller.SimulateActiveForge(policy, materialGrade: 3, new Pcg32(RngState.FromSeed(777)));

        Assert.Equal(run1, run2);
    }

    [Fact]
    public void SimulateActiveForge_EmptyPolicy_GradesZero()
    {
        var rng = new Pcg32(RngState.FromSeed(1));
        var grade = QualityRoller.SimulateActiveForge(ImmutableList<bool>.Empty, materialGrade: 3, rng);
        Assert.Equal(0, grade);
    }

    [Fact]
    public void SimulateActiveForge_AllInBandAllPerfect_GradesTheFullCeiling()
    {
        // Force every strike Perfect: roll < PerfectWindowRoll100Threshold always.
        var rng = new FixedRoll(0);
        var policy = ImmutableList.CreateRange(Enumerable.Repeat(true, 6)); // budget-1 material -> 6 strikes

        var grade = QualityRoller.SimulateActiveForge(policy, materialGrade: 1, rng);

        Assert.Equal(1000, grade); // every strike in-band (x2) and Perfect (x4) == the ceiling itself
    }

    /// <summary>An <see cref="IDeterministicRng"/> whose <c>Roll100</c> always returns a fixed value.</summary>
    private sealed class FixedRoll(int value) : IDeterministicRng
    {
        public int Roll100() => value;

        public int NextInt(int minInclusive, int maxExclusive) => throw new NotSupportedException();

        public uint NextUInt() => throw new NotSupportedException();
    }
}
