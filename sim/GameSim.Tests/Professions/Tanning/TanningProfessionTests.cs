using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Professions.Tanning;

/// <summary>
/// Behaviour tests for the Tanning add-on content pack. Everything here exercises the
/// profession's <see cref="ProfessionDefinition"/> and the shared pure pipeline
/// (<see cref="QualityRoller"/>, <see cref="ItemForge"/>) DIRECTLY, never the registry — so the
/// suite is green whether or not the orchestrator has applied the registration line (the pack is
/// inert until registered). Structural conformance (owner tags, known materials, acyclic talent
/// graph, referenced nodes, id uniqueness) is covered automatically by
/// <c>ProfessionConformanceTests</c> once tanning is in <c>ProfessionRegistry.All</c>.
/// </summary>
public class TanningProfessionTests
{
    private static readonly ProfessionDefinition Tan = TanningProfession.Definition;

    // ---- Definition shape ---------------------------------------------------------------

    [Fact]
    public void Definition_Identity_AndOwnership()
    {
        Assert.Equal("tanning", Tan.Id);
        Assert.Equal("Tanning", Tan.DisplayName);
        Assert.NotEmpty(Tan.Recipes);
        Assert.All(Tan.Recipes.Values, r =>
        {
            Assert.Equal("tanning", r.Profession);
            Assert.StartsWith("tanning-", r.RecipeId);
            Assert.True(RecipeTable.MaterialGrades.ContainsKey(r.MaterialKey), $"unknown material '{r.MaterialKey}'");
        });
    }

    [Fact]
    public void Definition_QualityModel_HasExpectedShifts()
    {
        Assert.Equal(5, Tan.Quality.FlatShifts[TanningProfession.SteadyHand]);
        Assert.Equal(7, Tan.Quality.FlatShifts[TanningProfession.SuppleWork]);
        Assert.Equal(8, Tan.Quality.FlatShifts[TanningProfession.MasterTanner]);

        var armorer = Tan.Quality.SlotShifts[TanningProfession.Armorer];
        Assert.Equal(ItemSlot.Armor, armorer.Slot);
        Assert.Equal(5, armorer.Shift);

        Assert.Equal(TanningProfession.HideMastery, Tan.Quality.MaterialMasteryNode);
        Assert.Equal(TanningProfession.Thrift, Tan.MaterialEfficiencyNode);

        // Non-quality nodes never leak into the quality model.
        Assert.False(Tan.Quality.FlatShifts.ContainsKey(TanningProfession.Thrift));
        Assert.False(Tan.Quality.FlatShifts.ContainsKey(TanningProfession.Tier2Tanning));
    }

    // ---- Tier gating --------------------------------------------------------------------

    [Fact]
    public void TierGate_GatesTwoAndThree_LeavesOneUngated()
    {
        Assert.False(Tan.TierGate.ContainsKey(1));
        Assert.Equal(TanningProfession.Tier2Tanning, Tan.TierGate[2]);
        Assert.Equal(TanningProfession.Tier3Tanning, Tan.TierGate[3]);
        Assert.True(Tan.TalentNodes.ContainsKey(Tan.TierGate[2]));
        Assert.True(Tan.TalentNodes.ContainsKey(Tan.TierGate[3]));
    }

    [Fact]
    public void TierGate_TierTwoRecipe_BlockedUntilGateUnlocked()
    {
        // Mirrors the CraftingHandlers gate check against this profession's data: a tier-2
        // recipe is gated behind Tier2Tanning; a tier-1 recipe is never gated.
        var tierTwo = Tan.Recipes["tanning-studded-leather"];
        var tierOne = Tan.Recipes["tanning-hide-jerkin"];

        Assert.Equal(2, tierTwo.Tier);
        Assert.True(Tan.TierGate.TryGetValue(tierTwo.Tier, out var gate));
        Assert.DoesNotContain(gate!, ImmutableSortedSet<string>.Empty);                 // blocked with no talents
        Assert.Contains(gate!, ImmutableSortedSet.Create(gate!));                       // allowed once unlocked
        Assert.False(Tan.TierGate.ContainsKey(tierOne.Tier));                           // tier 1 ungated
    }

    // ---- Talent graph unlock logic ------------------------------------------------------

    [Fact]
    public void CanUnlock_RespectsPrerequisiteChain()
    {
        var none = ImmutableSortedSet<string>.Empty;

        Assert.True(Tan.CanUnlock(TanningProfession.SteadyHand, none));
        Assert.False(Tan.CanUnlock(TanningProfession.SuppleWork, none));                        // needs steady-hand
        Assert.True(Tan.CanUnlock(TanningProfession.SuppleWork, none.Add(TanningProfession.SteadyHand)));
        Assert.False(Tan.CanUnlock(TanningProfession.SteadyHand, none.Add(TanningProfession.SteadyHand))); // already unlocked
        Assert.True(Tan.CanUnlock(TanningProfession.Armorer, none.Add(TanningProfession.SteadyHand)));
        Assert.False(Tan.CanUnlock("not-a-node", none));
    }

    // ---- Craft happy path (pure pipeline: roll → forge) ---------------------------------

    [Fact]
    public void Forge_GearRecipe_ScalesStatsByQuality_AndStampsMark()
    {
        var recipe = Tan.Recipes["tanning-hide-jerkin"]; // Armor, base Defense 7, Weight 3
        var item = ItemForge.Forge(new ItemId(1), recipe, QualityGrade.Fine, day: 3);

        Assert.Equal("tanning-hide-jerkin", item.RecipeId);
        Assert.Equal(ItemSlot.Armor, item.Slot);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(3, item.Mark.CraftedOnDay);
        Assert.Equal(0, item.Stats.Attack);
        Assert.Equal(7 * ItemForge.QualityPercent(QualityGrade.Fine) / 100, item.Stats.Defense); // 8
        Assert.Equal(3, item.Stats.Weight);                                                       // weight unaffected
        Assert.Null(item.Effect);
    }

    [Fact]
    public void Forge_Consumable_ScalesMagnitude_ByQuality()
    {
        var recipe = Tan.Recipes["tanning-field-poultice"]; // Consumable, Heal(5)
        var item = ItemForge.Forge(new ItemId(2), recipe, QualityGrade.Superior, day: 1);

        Assert.Equal(ItemSlot.Consumable, item.Slot);
        Assert.Equal(new ItemStats(0, 0, 0), item.Stats);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal(5 * ItemForge.QualityPercent(QualityGrade.Superior) / 100, item.Effect.Magnitude); // 6
    }

    // ---- Quality distribution pins (deterministic; guard the shift values) --------------

    private static string RollDistribution(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, ulong seed)
    {
        var rng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            counts[(int)QualityRoller.Roll(recipe, materialGrade, talents, Tan.Quality, rng)]++;
        }

        Assert.Equal(1000, counts.Sum());
        return string.Join(",", counts);
    }

    [Fact]
    public void QualityDistribution_NoTalents_GradeEqualsTier_MatchesSharedBaseCurve()
    {
        // shift 0 (material grade == recipe tier, no quality talents): the roll is Roll100()
        // straight into the shared threshold table, so tanning reproduces the exact base curve
        // the blacksmith dagger golden pins (Poor 15 / Common 50 / Fine 25 / Superior 9 / Master 1).
        var dist = RollDistribution(Tan.Recipes["tanning-hide-jerkin"], materialGrade: 1, ImmutableSortedSet<string>.Empty, seed: 1234);
        Assert.Equal("146,513,247,85,9", dist);
    }

    [Fact]
    public void QualityDistribution_Armorer_ShiftsArmorRollsUp()
    {
        // Armorer (+5, Armor-scoped) applied to an Armor recipe shifts the curve up vs the base.
        var talents = ImmutableSortedSet.Create(TanningProfession.Armorer);
        var dist = RollDistribution(Tan.Recipes["tanning-hide-jerkin"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("106,495,251,102,46", dist);
    }

    [Fact]
    public void QualityDistribution_FullFlatChain_ShiftsRollsUp()
    {
        // Steady Hand + Supple Work + Master Tanner = +5+7+8 = +20 flat.
        var talents = ImmutableSortedSet.Create(TanningProfession.SteadyHand, TanningProfession.SuppleWork, TanningProfession.MasterTanner);
        var dist = RollDistribution(Tan.Recipes["tanning-hide-jerkin"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("0,426,285,85,204", dist);
    }
}
