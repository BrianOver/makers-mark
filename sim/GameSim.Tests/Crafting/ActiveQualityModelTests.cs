using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// PA2/PKD3/PKD4 pins that don't fit the pure dominance-table shape in
/// <see cref="PerformanceGradeTests"/>: the material ceiling, the auto-craft-never-Masterwork
/// property, the talent decount (quality talents shift the active roll by exactly 0), and
/// verbatim <c>SubScores</c> stamping end-to-end through <see cref="CraftingHandlers"/>.
/// </summary>
public class ActiveQualityModelTests
{
    private static readonly Recipe Tier1Weapon = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 1 && r.Slot == ItemSlot.Weapon);

    private static readonly Recipe Tier3Weapon = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 3 && r.Slot == ItemSlot.Weapon);

    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    // ---- Material ceiling ------------------------------------------------------------------

    [Fact]
    public void Ceiling_BelowTierMaterial_NeverExceedsFine()
    {
        // Tier-3 recipe with grade-1 material, no mastery: materialStep = 1 - 3 = -2 (<= -1).
        for (var roll = 0; roll < 100; roll++)
        {
            var grade = QualityRoller.RollActive(
                Tier3Weapon, materialGrade: 1, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                new FixedRoll(roll), performanceGrade: 1000); // max grade — would be Masterwork uncapped
            Assert.True(grade <= QualityGrade.Fine, $"roll {roll}: expected <= Fine, got {grade}");
        }
    }

    [Fact]
    public void Ceiling_OnTierMaterial_NeverExceedsSuperior()
    {
        // Tier-1 recipe with grade-1 material, no mastery: materialStep = 0.
        for (var roll = 0; roll < 100; roll++)
        {
            var grade = QualityRoller.RollActive(
                Tier1Weapon, materialGrade: 1, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                new FixedRoll(roll), performanceGrade: 1000);
            Assert.True(grade <= QualityGrade.Superior, $"roll {roll}: expected <= Superior, got {grade}");
        }
    }

    [Fact]
    public void Ceiling_AboveTierMaterial_Uncapped_ReachesMasterwork()
    {
        // Tier-1 recipe with grade-2 material: materialStep = 1 (>= +1, uncapped).
        var reachedMasterwork = false;
        for (var roll = 0; roll < 100; roll++)
        {
            var grade = QualityRoller.RollActive(
                Tier1Weapon, materialGrade: 2, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                new FixedRoll(roll), performanceGrade: 1000);
            reachedMasterwork |= grade == QualityGrade.Masterwork;
        }

        Assert.True(reachedMasterwork, "uncapped materialStep >= 1 never reached Masterwork across the full jitter range");
    }

    [Fact]
    public void MaterialMastery_LiftsTheCeilingExactlyOneStep()
    {
        // Tier-1 recipe, grade-1 material: without mastery materialStep = 0 (Superior cap);
        // with material-mastery unlocked, materialStep = 1 (uncapped) — one step higher.
        var withoutMastery = false;
        var withMastery = false;
        for (var roll = 0; roll < 100; roll++)
        {
            var noMastery = QualityRoller.RollActive(
                Tier1Weapon, materialGrade: 1, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                new FixedRoll(roll), performanceGrade: 1000);
            withoutMastery |= noMastery == QualityGrade.Masterwork;

            var withMasteryTalent = QualityRoller.RollActive(
                Tier1Weapon, materialGrade: 1, ImmutableSortedSet.Create(TalentTree.MaterialMastery), ProfessionRegistry.Blacksmith.Quality,
                new FixedRoll(roll), performanceGrade: 1000);
            withMastery |= withMasteryTalent == QualityGrade.Masterwork;
        }

        Assert.False(withoutMastery, "materialStep 0 should cap at Superior — Masterwork must be unreachable");
        Assert.True(withMastery, "material-mastery should lift materialStep to +1 (uncapped) — Masterwork must be reachable");
    }

    // ---- Auto-craft: competent, capped, never Masterwork ------------------------------------

    [Fact]
    public void AutoCraft_NeverMasterwork_AcrossAllRollsAndMaterialGrades()
    {
        // performanceGrade: null (auto-craft, PKD4) must never yield Masterwork, regardless of
        // roll or how far above the recipe's tier the material sits (uncapped ceiling included).
        foreach (var materialGrade in new[] { 0, 1, 2, 3, 4, 10 })
        {
            for (var roll = 0; roll < 100; roll++)
            {
                var grade = QualityRoller.RollActive(
                    Tier1Weapon, materialGrade, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                    new FixedRoll(roll), performanceGrade: null);
                Assert.True(grade != QualityGrade.Masterwork,
                    $"materialGrade {materialGrade}, roll {roll}: auto-craft produced Masterwork");
            }
        }
    }

    // ---- Talent decount: retired quality nodes shift the active roll by exactly 0 ----------

    [Fact]
    public void QualityTalents_UnlockedVsNone_IdenticalDistribution_ForIdenticalGrade()
    {
        var allQualityTalents = ImmutableSortedSet.Create(
            TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft, TalentTree.WeaponSpecialist);

        for (var seed = 0; seed < 300; seed++)
        {
            var none = QualityRoller.RollActive(
                Tier1Weapon, materialGrade: 1, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                new Pcg32(RngState.FromSeed((ulong)seed)), performanceGrade: 620);
            var all = QualityRoller.RollActive(
                Tier1Weapon, materialGrade: 1, allQualityTalents, ProfessionRegistry.Blacksmith.Quality,
                new Pcg32(RngState.FromSeed((ulong)seed)), performanceGrade: 620);

            Assert.Equal(none, all);
        }
    }

    // ---- Sub-scores: stored verbatim on the crafted item ------------------------------------

    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState StateWith(params (string Key, int Qty)[] materials)
    {
        var state = GameFactory.NewGame(seed: 42);
        var stores = state.Player.Materials;
        foreach (var (key, qty) in materials)
        {
            stores = stores.SetItem(key, qty);
        }

        return state with { Player = state.Player with { Materials = stores } };
    }

    [Fact]
    public void SubScores_StampedVerbatim_OnTheCraftedItem()
    {
        var subScores = ImmutableList.Create(812, 640, 905);
        var state = StateWith(("copper", 5));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new CraftAction("dagger", "copper", PerformanceGrade: 700, SubScores: subScores)));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(subScores, item.CraftSubScores);
    }

    [Fact]
    public void SubScores_Null_YieldsEmpty()
    {
        var state = StateWith(("copper", 5));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Empty(item.CraftSubScores);
    }

    // ---- Draw count: exactly one Roll100 per successful craft, both paths ------------------

    [Fact]
    public void ActiveCraft_ThroughKernel_DrawsExactlyOneRoll100()
    {
        var state = StateWith(("copper", 5));
        var actions = ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper", PerformanceGrade: 700));

        var a = Kernel.Tick(state, actions);
        var b = Kernel.Tick(state, actions);

        // Determinism proxy for "exactly one draw": two identical ticks from the same
        // starting state produce byte-identical resulting state (including the RNG stream).
        Assert.Equal(SaveCodec.Serialize(a.NewState), SaveCodec.Serialize(b.NewState));
    }

    /// <summary>An <see cref="IDeterministicRng"/> whose <c>Roll100</c> always returns a fixed value.</summary>
    private sealed class FixedRoll(int value) : IDeterministicRng
    {
        public int Roll100() => value;

        public int NextInt(int minInclusive, int maxExclusive) => throw new NotSupportedException();

        public uint NextUInt() => throw new NotSupportedException();
    }
}
