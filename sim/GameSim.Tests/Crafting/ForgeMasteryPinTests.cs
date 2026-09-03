using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Crafting;

/// <summary>
/// 2026-09-03 owner ruling — PINNED DESIGN, not a regression guard against a bug.
///
/// <para><b>What was found.</b> <c>HandForgePlayer</c>'s first 20-seed/100-day sweep (PR #690)
/// simulates a constant 50 per-mille tracking/tempo deviation — deliberately equal to
/// <see cref="QualityRoller"/>'s own <c>AutoCraftGrade</c> baseline, see that type's class doc —
/// and every one of 743 hand-forges across all 20 seeds landed at <c>SeedGrade</c> exactly 1000,
/// zero exceptions. The reason is arithmetic, not luck: Master's Touch's <c>DriftRateReduction</c>
/// is 70 per-mille (every <see cref="ForgeScorer"/> zone) and Legendary Craft's
/// <c>OffBeatForgiveness</c> is 80 per-mille (every strike) — <b>each alone already exceeds the
/// simulated 50 per-mille deviation</b> — and <c>BaselinePlayer</c>'s own talent-unlock order
/// (prereq order, one node per morning: keen-eye day 1, master-touch day 2, legendary-craft day
/// 3) has both unlocked well before the craft loop's first legal hand-forge in every sweep seed.
///
/// <para><b>The tension, named honestly.</b> The no-assist ruling elsewhere in this codebase says
/// the player's own hands set the grade — skill should matter, not talent-point accounting. These
/// two talents make that untrue at the deviation levels a scripted policy simulates: once both are
/// unlocked, the SAME simulated "average" hand that scored 800/1000 pre-talent scores a flat 1000,
/// no matter how sloppy the trace is up to the forgiveness ceiling. A future reader will feel this
/// as a contradiction. It is not being resolved by softening the talents or the test below —
/// <b>the owner ruled 2026-09-03: intended. Talents are mastery, and mastery means certainty.</b> A
/// master smith's hands are reliable; forgiving drift and off-beat strikes entirely, once both
/// nodes are earned, is the reward the talent tree exists to pay out — not a loophole to close.
///
/// <para><b>What this pin does NOT claim.</b> The 50 per-mille figure is a chosen constant for an
/// unmeasured "average" simulated hand — it has never been measured against a real player's actual
/// Anvil-Map trace. If a real player's drift or off-beat error routinely runs past 70-80 per-mille
/// (Master's Touch/Legendary Craft's own forgiveness ceilings), these talents are an ordinary
/// reward for a still-meaningful skill check, and the "mastery erases skill" tension above is
/// theoretical, not measured. Nobody should read the 20-seed sweep's 1000-grade finding as proof
/// about human play — only about this policy's constant-deviation model of one.</para>
///
/// <para><b>To make this test fail</b> (a future session should stop and re-report, not "fix" it
/// quietly): shrink Master's Touch's <c>DriftRateReduction</c> or Legendary Craft's
/// <c>OffBeatForgiveness</c> below the deviation this test exercises (see
/// <see cref="Professions.ProfessionRegistry.Blacksmith"/>'s <c>MinigameAssists</c> table), or
/// change <see cref="ForgeScorer"/>'s deviation-to-subscore rule. Either is a deliberate,
/// reviewed balance change to the mastery reward, never an accidental regression — the same
/// discipline CLAUDE.md rule 12 asks of every LAW tripwire.</para>
/// </summary>
public class ForgeMasteryPinTests
{
    private static readonly ProfessionDefinition Blacksmith = ProfessionRegistry.Blacksmith;
    private static readonly ImmutableSortedSet<string> BothMasteryTalents =
        ImmutableSortedSet.Create(TalentTree.MasterTouch, TalentTree.LegendaryCraft);

    private static Recipe Dagger => RecipeTable.All["dagger"]; // tier 1, Weapon

    /// <summary>Builds a trace deviating by exactly <paramref name="deviation"/> per-mille from the
    /// ideal line at every sample and every strike's tempo — the same "constant deviation" shape
    /// <c>HandForgePlayer.BuildTrace</c> uses for its own "average hand" simulation, parameterized
    /// here instead of fixed at 50 so this pin can probe the forgiveness boundary directly.</summary>
    private static ForgeTraceInput DeviatedTrace(Recipe recipe, int pathSeed, int deviation)
    {
        var path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, pathSeed);

        var samples = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < path.Count; i += 2)
        {
            samples.Add(path[i]);
            samples.Add(System.Math.Clamp(path[i + 1] + deviation, 0, 1000));
        }

        var strikes = ImmutableList.Create(400, deviation, 500, deviation, 600, deviation);
        return new ForgeTraceInput(samples.ToImmutable(), strikes, pathSeed);
    }

    /// <summary>The property the ruling pins: with BOTH mastery talents unlocked, any deviation at
    /// or under their forgiveness ceilings (70 per-mille drift, 80 per-mille off-beat) scores the
    /// top grade — 1000/1000, every zone — regardless of how much of that headroom the deviation
    /// actually uses. 0 is the trivial case (already covered by <c>ForgeScorerTests.PerfectTrace_*</c>);
    /// 50 is HandForgePlayer's own simulated "average hand" constant, the exact value the sweep
    /// measured; 70 is Master's Touch's own <c>DriftRateReduction</c> ceiling, the edge of the
    /// forgiven band.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(70)]
    public void BothMasteryTalentsUnlocked_DeviationWithinForgivenessBands_AlwaysScoresTopGrade(int deviation)
    {
        var trace = DeviatedTrace(Dagger, pathSeed: 900, deviation);
        var score = ForgeScorer.Score(Dagger, trace, BothMasteryTalents, Blacksmith);

        Assert.Equal(1000, score.GradePermille);
        Assert.Equal(ImmutableList.Create(1000, 1000, 1000), score.SubScores);
    }

    /// <summary>The boundary is real, not vacuous: a deviation past BOTH ceilings (71 > Master's
    /// Touch's 70; the strikes below also clear Legendary Craft's 80) measurably costs grade even
    /// with both talents unlocked — the pin above is a genuine forgiveness window, not "any grade
    /// counts as top" by construction.</summary>
    [Fact]
    public void BothMasteryTalentsUnlocked_DeviationPastBothCeilings_NoLongerScoresTopGrade()
    {
        var trace = DeviatedTrace(Dagger, pathSeed: 901, deviation: 95);
        var score = ForgeScorer.Score(Dagger, trace, BothMasteryTalents, Blacksmith);

        Assert.True(score.GradePermille < 1000, $"expected a deviation past both forgiveness ceilings to cost grade, got {score.GradePermille}");
    }

    /// <summary>Without either talent, the SAME 50-per-mille deviation HandForgePlayer simulates is
    /// nowhere near the top grade — the mastery reward is what changes, not the scorer's math.
    /// Mirrors <c>HandForgePlayerTests.HandForgedCraft_..._SeedsBatchEchoAt800...</c>'s end-to-end
    /// pin from the other side of the kernel.</summary>
    [Fact]
    public void NoMasteryTalents_SameSimulatedDeviation_DoesNotScoreTopGrade()
    {
        var trace = DeviatedTrace(Dagger, pathSeed: 902, deviation: 50);
        var score = ForgeScorer.Score(Dagger, trace, ImmutableSortedSet<string>.Empty, Blacksmith);

        Assert.Equal(800, score.GradePermille);
    }
}
