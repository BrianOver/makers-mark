using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Asserts the QualityRoller against its documented threshold table (U4 spec table):
///
///   effective = Roll100() + shift        (exactly ONE Roll100 draw per craft)
///   shift = 8 * (materialGrade + (material-mastery ? 1 : 0) - recipe.Tier)
///         + (keen-eye ? 5 : 0)
///         + (master-touch ? 7 : 0)
///         + (legendary-craft ? 8 : 0)
///         + (weapon-specialist and Slot == Weapon ? 5 : 0)
///   grade: effective &lt;= 14 Poor | 15..64 Common | 65..89 Fine | 90..98 Superior | &gt;= 99 Masterwork
///
/// The mirror function below re-implements that table independently; any drift between
/// implementation and documentation fails these tests deterministically.
/// </summary>
public class QualityRollerTests
{
    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    private static ImmutableSortedSet<string> Talents(params string[] ids) =>
        ImmutableSortedSet.CreateRange(ids);

    /// <summary>Independent mirror of the documented threshold table.</summary>
    private static QualityGrade ExpectedGrade(int roll, int shift)
    {
        var effective = roll + shift;
        if (effective <= 14)
        {
            return QualityGrade.Poor;
        }

        if (effective <= 64)
        {
            return QualityGrade.Common;
        }

        if (effective <= 89)
        {
            return QualityGrade.Fine;
        }

        if (effective <= 98)
        {
            return QualityGrade.Superior;
        }

        return QualityGrade.Masterwork;
    }

    /// <summary>Rolls <paramref name="count"/> times and returns per-grade counts, asserting each roll matches the mirror table.</summary>
    private static int[] RollAndVerify(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, int shift, ulong seed, int count)
    {
        var rollerRng = new Pcg32(RngState.FromSeed(seed));
        var mirrorRng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];

        for (var i = 0; i < count; i++)
        {
            var actual = QualityRoller.Roll(recipe, materialGrade, talents, ProfessionRegistry.Blacksmith.Quality, rollerRng);
            var expected = ExpectedGrade(mirrorRng.Roll100(), shift);
            Assert.Equal(expected, actual);
            counts[(int)actual]++;
        }

        // Lockstep snapshots prove the roller consumed exactly one Roll100 per craft.
        Assert.Equal(mirrorRng.Snapshot(), rollerRng.Snapshot());
        return counts;
    }

    [Fact]
    public void BaseDistribution_Tier1Copper_1000Rolls_ExactCounts()
    {
        // Tier 1 recipe + copper (grade 1), no talents: shift = 8 * (1 - 1) = 0.
        var counts = RollAndVerify(RecipeTable.All["dagger"], materialGrade: 1, NoTalents, shift: 0, seed: 1234, count: 1000);

        // Golden counts for seed 1234 — deterministic forever (Pcg32 known-answer + table).
        // Base odds: Poor 15%, Common 50%, Fine 25%, Superior 9%, Masterwork 1%.
        Assert.Equal("146,513,247,85,9", string.Join(",", counts));
        Assert.Equal(1000, counts.Sum());
    }

    [Fact]
    public void MaterialGradeAboveTier_ShiftsDistributionUp_Exactly8PerGrade()
    {
        // Mithril (grade 4) on a tier-1 recipe: shift = 8 * (4 - 1) = +24.
        var counts = RollAndVerify(RecipeTable.All["dagger"], materialGrade: 4, NoTalents, shift: 24, seed: 1234, count: 1000);

        // +24 kills Poor entirely (roll would need to be < -9).
        Assert.Equal(0, counts[(int)QualityGrade.Poor]);
        Assert.Equal("0,389,276,88,247", string.Join(",", counts));
    }

    [Fact]
    public void MaterialGradeBelowTier_ShiftsDistributionDown()
    {
        // Copper (grade 1) on a tier-3 recipe: shift = 8 * (1 - 3) = -16 → Masterwork impossible (max effective 99 - 16 = 83).
        var counts = RollAndVerify(RecipeTable.All["greatsword"], materialGrade: 1, NoTalents, shift: -16, seed: 1234, count: 1000);

        Assert.Equal(0, counts[(int)QualityGrade.Masterwork]);
        Assert.Equal(0, counts[(int)QualityGrade.Superior]);
    }

    [Fact]
    public void QualityShiftTalents_StackExactlyAsDocumented()
    {
        var recipe = RecipeTable.All["dagger"]; // tier 1, weapon

        // keen-eye alone: +5.
        RollAndVerify(recipe, materialGrade: 1, Talents(TalentTree.KeenEye), shift: 5, seed: 77, count: 500);

        // keen-eye + master-touch: +12.
        RollAndVerify(recipe, materialGrade: 1, Talents(TalentTree.KeenEye, TalentTree.MasterTouch), shift: 12, seed: 77, count: 500);

        // keen-eye + master-touch + legendary-craft: +20.
        RollAndVerify(
            recipe,
            materialGrade: 1,
            Talents(TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft),
            shift: 20,
            seed: 77,
            count: 500);
    }

    [Fact]
    public void WeaponSpecialist_AppliesToWeaponsOnly()
    {
        var talents = Talents(TalentTree.KeenEye, TalentTree.WeaponSpecialist);

        // Weapon: keen-eye +5 and weapon-specialist +5 → +10.
        RollAndVerify(RecipeTable.All["dagger"], materialGrade: 1, talents, shift: 10, seed: 99, count: 500);

        // Shield: weapon-specialist contributes nothing → +5.
        RollAndVerify(RecipeTable.All["buckler"], materialGrade: 1, talents, shift: 5, seed: 99, count: 500);
    }

    [Fact]
    public void MaterialMastery_TreatsGradeAsOneHigher()
    {
        // material-mastery: grade counts as +1 → shift = 8 * (1 + 1 - 1) = +8.
        RollAndVerify(
            RecipeTable.All["dagger"],
            materialGrade: 1,
            Talents(TalentTree.MaterialEfficiency, TalentTree.MaterialMastery),
            shift: 8,
            seed: 55,
            count: 500);
    }

    [Fact]
    public void NonQualityTalents_HaveNoEffectOnTheRoll()
    {
        // material-efficiency and the tier unlocks are not quality nodes: shift stays 0.
        var talents = Talents(TalentTree.MaterialEfficiency, TalentTree.Tier2Smithing, TalentTree.Tier3Smithing);
        RollAndVerify(RecipeTable.All["dagger"], materialGrade: 1, talents, shift: 0, seed: 1234, count: 1000);
    }

    [Fact]
    public void LockedTalents_HaveNoEffect_OnlyTheUnlockedSetCounts()
    {
        // Nodes exist in the tree but are NOT in the unlocked set → base distribution.
        RollAndVerify(RecipeTable.All["dagger"], materialGrade: 1, NoTalents, shift: 0, seed: 2026, count: 500);
    }
}
