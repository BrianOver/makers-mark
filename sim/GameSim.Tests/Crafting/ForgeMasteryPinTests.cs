using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Crafting;

/// <summary>
/// 2026-09-03 owner ruling (<c>docs/design/MAKERS-MARK.md</c> §11.7.11) — PINNED DESIGN, not a
/// regression guard against a bug. <b>This file previously pinned the OPPOSITE ruling of the same
/// date ("talents are mastery, and mastery means certainty" — any deviation inside the forgiveness
/// band scored a flat 1000). That ruling is superseded and the pin is rewritten, not softened.</b>
/// The prior file said in as many words that changing <see cref="ForgeScorer"/>'s
/// deviation-to-subscore rule was "a deliberate, reviewed balance change to the mastery reward,
/// never an accidental regression" — this is that change, and its measurement is in the PR body.
///
/// <para><b>What was measured, and why the collapse was structural.</b> Across 20 seeds x 100 days
/// under two OPPOSITE talent-pacing policies — <c>HandForgePlayer</c> (greedy: unlock in prereq
/// order, one node per morning from day one) and <c>LateMasteryPlayer</c> (deferred: both mastery
/// nodes held back behind every other node the tree allows) — the forge minigame's accuracy stopped
/// changing the grade by about day 6 under BOTH. Two opposite pacing orders producing the same
/// collapse is what rules out a policy artifact and makes the cause arithmetic: every forgiveness
/// axis was SUBTRACTIVE with a zero floor (<c>max(0, dev - forgiveness)</c>), so every deviation at
/// or under the accumulated forgiveness scored IDENTICALLY to a flawless one. Talents were not
/// compressing the skill range, they were DELETING the bottom of it, and once Master's Touch (70
/// per-mille, every zone) and Legendary Craft (80 per-mille, every strike) were both unlocked the
/// dead zone swallowed the entire realistic error band.</para>
///
/// <para><b>What the ruling asks for, and what this file pins.</b> Talents raise your FLOOR;
/// accuracy still determines your CEILING. Forgiveness now scales the penalty instead of erasing it
/// (<see cref="ForgeScorer"/>'s class doc carries the formula), which yields the four properties
/// below — strict ordering at every talent level, no dead zone anywhere, talents still clearly
/// worth unlocking, and a late-campaign sloppy swing that costs you without being punishing.
/// A future session should read a failure here as "the balance of the mastery reward moved," stop,
/// and re-report — never quietly re-pin.</para>
/// </summary>
public class ForgeMasteryPinTests
{
    private static readonly ProfessionDefinition Blacksmith = ProfessionRegistry.Blacksmith;
    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    /// <summary>The two nodes whose subtractive forgiveness produced the measured collapse.</summary>
    private static readonly ImmutableSortedSet<string> BothMasteryTalents =
        ImmutableSortedSet.Create(TalentTree.MasterTouch, TalentTree.LegendaryCraft);

    /// <summary>Every assist-bearing blacksmith node at once — the widest safety net the tree can
    /// ever grant on a Weapon recipe (Weapon Specialist is slot-scoped), i.e. the hardest case for
    /// "accuracy still matters".</summary>
    private static readonly ImmutableSortedSet<string> EveryTalent = ImmutableSortedSet.Create(
        TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft, TalentTree.WeaponSpecialist);

    private static Recipe Dagger => RecipeTable.All["dagger"]; // tier 1, Weapon

    private static readonly string[] AssistNodes =
    {
        TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft, TalentTree.WeaponSpecialist,
    };

    /// <summary>All 16 unlock states of the four assist nodes — "every talent level" in the ruling
    /// is enumerated, never sampled, so no ordering claim below rests on a lucky subset. Yielded as
    /// a plain int bitmask (xUnit-serializable, so each case gets a stable name) and expanded by
    /// <see cref="TalentsFor"/>.</summary>
    public static IEnumerable<object[]> AllTalentSubsets() =>
        Enumerable.Range(0, 1 << 4).Select(mask => new object[] { mask });

    private static ImmutableSortedSet<string> TalentsFor(int mask)
    {
        var set = ImmutableSortedSet<string>.Empty;
        for (var bit = 0; bit < AssistNodes.Length; bit++)
        {
            if ((mask & (1 << bit)) != 0)
            {
                set = set.Add(AssistNodes[bit]);
            }
        }

        return set;
    }

    /// <summary>
    /// Builds a trace deviating by exactly <paramref name="deviation"/> per-mille from the ideal
    /// line at every sample and every strike's tempo.
    ///
    /// <para>The deviation is applied AWAY from whichever rail has more room (down from a hot
    /// target, up from a cool one) rather than always upward. The old helper always added, then
    /// clamped to [0,1000] — so past a certain size a "bigger" requested deviation silently stopped
    /// being a bigger ACTUAL deviation on the hot vertices, and any ordering theory built on it
    /// would have been testing the clamp, not the scorer. With this direction rule, every
    /// <paramref name="deviation"/> up to 500 is a genuine, unclamped deviation of exactly that
    /// size, which is the whole range the scorer resolves (it floors at 1000/DevScale = 250).</para>
    /// </summary>
    private static ForgeTraceInput DeviatedTrace(Recipe recipe, int pathSeed, int deviation)
    {
        var path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, pathSeed);

        var samples = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < path.Count; i += 2)
        {
            var target = path[i + 1];
            samples.Add(path[i]);
            samples.Add(target >= 500 ? target - deviation : target + deviation);
        }

        var strikes = ImmutableList.Create(400, deviation, 500, deviation, 600, deviation);
        return new ForgeTraceInput(samples.ToImmutable(), strikes, pathSeed);
    }

    private static int GradeAt(int deviation, ImmutableSortedSet<string> talents, int pathSeed = 900) =>
        ForgeScorer.Score(Dagger, DeviatedTrace(Dagger, pathSeed, deviation), talents, Blacksmith).GradePermille;

    // =====================================================================================
    // 1. Strict ordering — the constraint the ruling is not negotiable about
    // =====================================================================================

    /// <summary>The core property: for a FIXED talent set, a strictly worse swing never scores
    /// better, and — while the score is still off the floor — scores strictly worse. Swept over
    /// every deviation from 0 to 250 (the full range the scorer resolves) at all 16 talent levels.
    /// Under the superseded subtractive rule this failed outright: with both mastery talents
    /// unlocked, deviations 0 through 70 all scored an identical 1000.</summary>
    [Theory]
    [MemberData(nameof(AllTalentSubsets))]
    public void StrictOrdering_WorseSwingNeverScoresBetter_AtEveryTalentLevel(int talentMask)
    {
        var talents = TalentsFor(talentMask);
        var previous = GradeAt(0, talents);
        for (var deviation = 1; deviation <= 250; deviation++)
        {
            var current = GradeAt(deviation, talents);

            Assert.True(
                current <= previous,
                $"talents [{string.Join(",", talents)}]: deviation {deviation} scored {current}, better than {deviation - 1}'s {previous}");

            // Strictness only binds while there is still grade left to lose; once a swing has
            // bottomed out at 0 the floor is legitimately flat.
            if (current > 0)
            {
                Assert.True(
                    current < previous,
                    $"talents [{string.Join(",", talents)}]: deviation {deviation} scored {current}, IDENTICAL to {deviation - 1} — a dead zone");
            }

            previous = current;
        }
    }

    /// <summary>The dead zone specifically, at the exact deviations that used to be erased. With
    /// both mastery talents unlocked, 0/10/30/50/70 per-mille all scored a flat 1000 under the old
    /// rule (70 is Master's Touch's own former forgiveness ceiling). They must now be five strictly
    /// decreasing grades.</summary>
    [Fact]
    public void NoDeadZone_TheDeviationsTheOldRuleErased_AreNowStrictlyOrdered()
    {
        int[] deviations = { 0, 10, 30, 50, 70 };
        var grades = deviations.Select(d => GradeAt(d, BothMasteryTalents)).ToArray();

        for (var i = 1; i < grades.Length; i++)
        {
            Assert.True(
                grades[i] < grades[i - 1],
                $"deviation {deviations[i]} scored {grades[i]}; deviation {deviations[i - 1]} scored {grades[i - 1]} — these must differ");
        }
    }

    /// <summary>The same, against the widest possible safety net: every assist node unlocked, on the
    /// Weapon recipe that also collects the slot-scoped Weapon Specialist width. If accuracy still
    /// moves the grade here, it moves it anywhere.</summary>
    [Fact]
    public void NoDeadZone_EvenWithEveryTalentUnlocked_AccuracyStillMovesTheGrade()
    {
        Assert.True(
            GradeAt(0, EveryTalent) > GradeAt(20, EveryTalent),
            "a flawless swing must beat a near-flawless one even at full mastery");
        Assert.True(
            GradeAt(20, EveryTalent) > GradeAt(120, EveryTalent),
            "a near-flawless swing must beat a sloppy one even at full mastery");
    }

    // =====================================================================================
    // 2. Talents must stay clearly worth unlocking
    // =====================================================================================

    /// <summary>A master visibly out-earns a novice on the IDENTICAL swing, across the whole
    /// realistic error range. This is the constraint that stops "accuracy keeps mattering" from
    /// being bought by making talents worthless.</summary>
    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(120)]
    [InlineData(160)]
    public void TalentsRaiseTheFloor_MasterOutscoresNoviceOnTheSameSwing(int deviation)
    {
        var novice = GradeAt(deviation, NoTalents);
        var master = GradeAt(deviation, EveryTalent);

        Assert.True(master > novice, $"deviation {deviation}: master scored {master}, novice {novice}");
    }

    /// <summary>And the reward is shaped the way a safety net should be: the worse the swing, the
    /// more mastery is worth. This is the honest reading of "talents raise your floor" — the master
    /// is not merely uniformly better, they are specifically protected on their bad days.</summary>
    [Fact]
    public void TalentAdvantage_WidensAsTheSwingGetsWorse()
    {
        var cleanEdge = GradeAt(50, EveryTalent) - GradeAt(50, NoTalents);
        var sloppyEdge = GradeAt(120, EveryTalent) - GradeAt(120, NoTalents);

        Assert.True(sloppyEdge > cleanEdge, $"mastery is worth {sloppyEdge} on a sloppy swing but {cleanEdge} on a clean one — it should be worth more when it is needed more");
    }

    /// <summary>"Do not make late crafting punishing." A fully-talented smith having a genuinely
    /// sloppy day still lands at or above the Fine band floor (<c>QualityRoller.RollActive</c>'s
    /// 550 per-mille), and still comfortably above the novice floor at the same deviation. Skill
    /// keeps mattering; the game does not get harder.</summary>
    [Fact]
    public void LateCampaignSloppySwing_CostsGradeButIsNotPunishing()
    {
        var master = GradeAt(120, EveryTalent);

        Assert.True(master >= 550, $"a master's sloppy swing scored {master}, below the Fine-equivalent floor — the curve has overshot into punishing");
        Assert.True(master < GradeAt(20, EveryTalent), "a sloppy swing must still cost a master real grade");
    }

    // =====================================================================================
    // 3. The untalented smith is untouched — the change is an identity at zero forgiveness
    // =====================================================================================

    /// <summary>Zero forgiveness leaves the retained penalty share at 1000, so the proportional rule
    /// collapses to exactly the old <c>dev * DevScale</c> slope. The pinned 800 below is the SAME
    /// value this file asserted before the ruling, and is the reason every zero-talent pin across
    /// the suite (and the whole 100-day Balance gate, whose policies never hand-forge) is unmoved by
    /// this change: it is arithmetic identity, not a coincidence that happened to survive.</summary>
    [Fact]
    public void NoTalents_SameSimulatedDeviation_ScoresExactlyAsBeforeTheRuling()
    {
        Assert.Equal(800, GradeAt(50, NoTalents, pathSeed: 902));
    }

    /// <summary>Locked nodes still contribute nothing, and a talent can never make a swing worse —
    /// forgiveness is monotone in the talent set.</summary>
    [Theory]
    [MemberData(nameof(AllTalentSubsets))]
    public void AnyTalentSet_NeverScoresBelowNoTalents_OnTheSameSwing(int talentMask)
    {
        var talents = TalentsFor(talentMask);
        foreach (var deviation in new[] { 0, 40, 90, 150, 220 })
        {
            Assert.True(
                GradeAt(deviation, talents) >= GradeAt(deviation, NoTalents),
                $"talents [{string.Join(",", talents)}] scored below no-talents at deviation {deviation}");
        }
    }
}
