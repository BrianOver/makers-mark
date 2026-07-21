using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Asserts <see cref="QualityRoller.Roll"/> — the PASSIVE threshold-table roll every
/// non-active profession uses — against its documented table (U4 spec table, UNTOUCHED by
/// PA2/PKD2/PKD3):
///
///   effective = Roll100() + shift        (exactly ONE Roll100 draw per craft)
///   shift = 8 * (materialGrade + (material-mastery ? 1 : 0) - recipe.Tier)
///         + sum of quality.FlatShifts[node] for each unlocked flat node
///         + sum of quality.SlotShifts[node].Shift for each unlocked slot node whose slot matches
///   grade: effective &lt;= 14 Poor | 15..64 Common | 65..89 Fine | 90..98 Superior | &gt;= 99 Masterwork
///
/// PA2 flips the BLACKSMITH to the active dominance model (see <see cref="ActiveQualityModelTests"/>
/// and <see cref="PerformanceGradeTests"/>), so this file uses a SYNTHETIC quality model + recipes
/// instead of <c>ProfessionRegistry.Blacksmith</c> — fully decoupled from any one profession's data,
/// so it keeps proving the shared passive math regardless of which registered profession uses it.
/// The mirror function below re-implements that table independently; any drift between
/// implementation and documentation fails these tests deterministically.
/// </summary>
public class QualityRollerTests
{
    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    // ---- Synthetic passive fixture (decoupled from any registered profession) --------------
    private const string FlatA = "test-flat-a";   // +5
    private const string FlatB = "test-flat-b";   // +7 (stacks with FlatA)
    private const string FlatC = "test-flat-c";   // +8 (stacks with the chain)
    private const string WeaponNode = "test-weapon-specialist"; // Weapon slot only, +5
    private const string MaterialEfficiencyNode = "test-material-efficiency";
    private const string MasteryNode = "test-material-mastery"; // material +1 grade
    private const string Tier2Node = "test-tier-2";
    private const string Tier3Node = "test-tier-3";

    private static readonly ProfessionQualityModel TestQuality = new(
        FlatShifts: new Dictionary<string, int>
        {
            [FlatA] = 5,
            [FlatB] = 7,
            [FlatC] = 8,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        SlotShifts: new Dictionary<string, SlotShift>
        {
            [WeaponNode] = new SlotShift(ItemSlot.Weapon, 5),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal),
        MaterialMasteryNode: MasteryNode);

    private static Recipe TestRecipe(ItemSlot slot, int tier) =>
        new($"test-{slot}-{tier}", "Test Recipe", "test-profession", slot, tier, "copper", MaterialQuantity: 1, new ItemStats(0, 0, 0));

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
            var actual = QualityRoller.Roll(recipe, materialGrade, talents, TestQuality, rollerRng);
            var expected = ExpectedGrade(mirrorRng.Roll100(), shift);
            Assert.Equal(expected, actual);
            counts[(int)actual]++;
        }

        // Lockstep snapshots prove the roller consumed exactly one Roll100 per craft.
        Assert.Equal(mirrorRng.Snapshot(), rollerRng.Snapshot());
        return counts;
    }

    [Fact]
    public void BaseDistribution_Tier1Weapon_1000Rolls_ExactCounts()
    {
        // Tier 1 recipe + grade-1 material, no talents: shift = 8 * (1 - 1) = 0.
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 1), materialGrade: 1, NoTalents, shift: 0, seed: 1234, count: 1000);

        // Golden counts for seed 1234 — deterministic forever (Pcg32 known-answer + table).
        // Base odds: Poor 15%, Common 50%, Fine 25%, Superior 9%, Masterwork 1%.
        Assert.Equal("146,513,247,85,9", string.Join(",", counts));
        Assert.Equal(1000, counts.Sum());
    }

    [Fact]
    public void MaterialGradeAboveTier_ShiftsDistributionUp_Exactly8PerGrade()
    {
        // Material grade 4 on a tier-1 recipe: shift = 8 * (4 - 1) = +24.
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 1), materialGrade: 4, NoTalents, shift: 24, seed: 1234, count: 1000);

        // +24 kills Poor entirely (roll would need to be < -9).
        Assert.Equal(0, counts[(int)QualityGrade.Poor]);
        Assert.Equal("0,389,276,88,247", string.Join(",", counts));
    }

    [Fact]
    public void MaterialGradeBelowTier_ShiftsDistributionDown()
    {
        // Material grade 1 on a tier-3 recipe: shift = 8 * (1 - 3) = -16 → Masterwork impossible (max effective 99 - 16 = 83).
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 3), materialGrade: 1, NoTalents, shift: -16, seed: 1234, count: 1000);

        Assert.Equal(0, counts[(int)QualityGrade.Masterwork]);
        Assert.Equal(0, counts[(int)QualityGrade.Superior]);
    }

    [Fact]
    public void QualityShiftTalents_StackExactlyAsDocumented()
    {
        var recipe = TestRecipe(ItemSlot.Weapon, tier: 1);

        // FlatA alone: +5.
        RollAndVerify(recipe, materialGrade: 1, Talents(FlatA), shift: 5, seed: 77, count: 500);

        // FlatA + FlatB: +12.
        RollAndVerify(recipe, materialGrade: 1, Talents(FlatA, FlatB), shift: 12, seed: 77, count: 500);

        // FlatA + FlatB + FlatC: +20.
        RollAndVerify(recipe, materialGrade: 1, Talents(FlatA, FlatB, FlatC), shift: 20, seed: 77, count: 500);
    }

    [Fact]
    public void WeaponSpecialist_AppliesToWeaponsOnly()
    {
        var talents = Talents(FlatA, WeaponNode);

        // Weapon: FlatA +5 and the weapon-slot node +5 → +10.
        RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 1), materialGrade: 1, talents, shift: 10, seed: 99, count: 500);

        // Shield: the weapon-slot node contributes nothing → +5.
        RollAndVerify(TestRecipe(ItemSlot.Shield, tier: 1), materialGrade: 1, talents, shift: 5, seed: 99, count: 500);
    }

    [Fact]
    public void MaterialMastery_TreatsGradeAsOneHigher()
    {
        // material-mastery: grade counts as +1 → shift = 8 * (1 + 1 - 1) = +8.
        RollAndVerify(
            TestRecipe(ItemSlot.Weapon, tier: 1),
            materialGrade: 1,
            Talents(MaterialEfficiencyNode, MasteryNode),
            shift: 8,
            seed: 55,
            count: 500);
    }

    [Fact]
    public void NonQualityTalents_HaveNoEffectOnTheRoll()
    {
        // material-efficiency and the tier unlocks are not quality nodes: shift stays 0.
        var talents = Talents(MaterialEfficiencyNode, Tier2Node, Tier3Node);
        RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 1), materialGrade: 1, talents, shift: 0, seed: 1234, count: 1000);
    }

    [Fact]
    public void LockedTalents_HaveNoEffect_OnlyTheUnlockedSetCounts()
    {
        // Nodes exist in the model but are NOT in the unlocked set → base distribution.
        RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 1), materialGrade: 1, NoTalents, shift: 0, seed: 2026, count: 500);
    }
}
