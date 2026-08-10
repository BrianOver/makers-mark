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

    /// <summary>
    /// Forward-ladder plan 2026-08-10-003 L3, the Verification Contract's "the minigame survives
    /// the ladder" row: BEFORE L3, recipes topped out at Tier 3 while Gloomwood ore (rung 1) is
    /// grade 8-11, so a grade-11 material against a Tier-3 recipe produced shift = 8*(11-3) = +64
    /// — effective = roll+64 is Masterwork (&gt;=99) on any roll &gt;= 35, and stacked with the
    /// (now-retired-for-blacksmith, but still live for any future passive profession) talent flat
    /// shifts it could clear 99 on nearly every roll — "guaranteed Masterwork forever, the forge
    /// minigame dead" (the plan's own words). L3's fix is giving grade 8-11 material a Tier 8-9
    /// HOME: the worst-case gap shrinks from +8 grades to at most +3 (grade 11 vs Tier 8), which
    /// keeps shift bounded enough that Masterwork stays a REAL outcome of the roll (earned), never
    /// a certainty the die can't avoid (guaranteed) — across every grade 8-11 / Tier 8-9 pairing.
    /// </summary>
    [Theory]
    [InlineData(8, 8)]   // shift 0 — grade matches tier exactly (Common-ceiling floor case)
    [InlineData(9, 8)]   // shift +8
    [InlineData(10, 8)]  // shift +16
    [InlineData(11, 8)]  // shift +24 — the worst-case gap in the new band
    [InlineData(8, 9)]   // shift -8 — material below tier (Masterwork must be impossible here)
    [InlineData(9, 9)]   // shift 0
    [InlineData(10, 9)]  // shift +8
    [InlineData(11, 9)]  // shift +16
    public void RungOneMaterialBand_KeepsMasterworkBounded_NotGuaranteed(int materialGrade, int tier)
    {
        var shift = 8 * (materialGrade - tier);
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier), materialGrade, NoTalents, shift, seed: 4242, count: 1000);

        // "Bounded, not guaranteed": Masterwork must stay a fraction of rolls, never all of them —
        // the exact failure mode +64 produced (65% Masterwork on the SAME 1000-roll seed, per
        // MaterialGradeAboveTier_ShiftsDistributionUp_Exactly8PerGrade's shift-24 sibling case,
        // scaled up). At most a small minority of rolls should land Masterwork across this band.
        Assert.True(counts[(int)QualityGrade.Masterwork] < 300,
            $"grade {materialGrade} vs tier {tier} (shift {shift}): Masterwork hit {counts[(int)QualityGrade.Masterwork]}/1000 — no longer bounded");
        Assert.Equal(1000, counts.Sum());
    }

    [Fact]
    public void RungOneMaterialBand_MasterworkStaysEarnable_AtTheWorstCaseGap()
    {
        // Grade 11 vs Tier 8 is the widest legal gap L3's recipes allow (heartwood substituted
        // into the Tier-8 gloomsteel-blade recipe) — Masterwork must still be REACHABLE (earned),
        // not merely bounded-to-zero, or the forge minigame's top band becomes unreachable instead
        // of merely harder — an equally-dead minigame from the other direction.
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 8), materialGrade: 11, NoTalents, shift: 24, seed: 4242, count: 1000);
        Assert.True(counts[(int)QualityGrade.Masterwork] > 0, "grade 11 vs tier 8: Masterwork never landed in 1000 rolls — no longer earnable");
    }

    /// <summary>
    /// Forward-ladder plan 2026-08-10-003 L4, the SAME correctness fix one rung further out:
    /// Emberfall ore (rung 2) is grade 12-16, homed in Tier 12-14 recipes. The worst-case
    /// substitution gap widens slightly from L3's own (+3 grades, grade 11 vs Tier 8) to +4 grades
    /// (grade 16 vs Tier 12 — heartcoal substituted into the Tier-12 cinderforge-blade recipe), a
    /// consequence of the band itself being one grade wider (5 grades, 12-16) than one tier wider
    /// (3 tiers, 12-14) allows to line up perfectly — still nowhere near the pre-ladder +8 gap
    /// (grade 11 vs the old Tier-3 ceiling) that produced "guaranteed forever."
    /// </summary>
    [Theory]
    [InlineData(12, 12)] // shift 0 — grade matches tier exactly (Common-ceiling floor case)
    [InlineData(13, 12)] // shift +8
    [InlineData(14, 12)] // shift +16
    [InlineData(15, 12)] // shift +24
    [InlineData(16, 12)] // shift +32 — the worst-case gap in the new band
    [InlineData(12, 13)] // shift -8 — material below tier (Masterwork must be impossible here)
    [InlineData(13, 13)] // shift 0
    [InlineData(14, 14)] // shift 0
    [InlineData(15, 14)] // shift +8 — emberglass-draught's own baseline (one grade above its tier)
    public void RungTwoMaterialBand_KeepsMasterworkBounded_NotGuaranteed(int materialGrade, int tier)
    {
        var shift = 8 * (materialGrade - tier);
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier), materialGrade, NoTalents, shift, seed: 4242, count: 1000);

        // "Bounded, not guaranteed": even at the widest legal gap (+32), Masterwork stays well under
        // half the rolls — nowhere near the +64 failure mode's 65%. A generous ceiling (the widest
        // gap this band allows, +32, lands well under it) rather than L3's tighter <300 bound, since
        // this band's worst-case gap (+4 grades) is genuinely wider than L3's (+3 grades) by design.
        Assert.True(counts[(int)QualityGrade.Masterwork] < 500,
            $"grade {materialGrade} vs tier {tier} (shift {shift}): Masterwork hit {counts[(int)QualityGrade.Masterwork]}/1000 — no longer bounded");
        Assert.Equal(1000, counts.Sum());
    }

    [Fact]
    public void RungTwoMaterialBand_MasterworkStaysEarnable_AtTheWorstCaseGap()
    {
        // Grade 16 vs Tier 12 is the widest legal gap L4's recipes allow (heartcoal substituted into
        // the Tier-12 cinderforge-blade recipe) — Masterwork must still be REACHABLE (earned), not
        // merely bounded, the same earnability proof L3 established one rung earlier.
        var counts = RollAndVerify(TestRecipe(ItemSlot.Weapon, tier: 12), materialGrade: 16, NoTalents, shift: 32, seed: 4242, count: 1000);
        Assert.True(counts[(int)QualityGrade.Masterwork] > 0, "grade 16 vs tier 12: Masterwork never landed in 1000 rolls — no longer earnable");
    }
}
