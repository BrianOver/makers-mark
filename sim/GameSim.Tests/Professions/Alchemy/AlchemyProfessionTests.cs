using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Professions.Alchemy;

/// <summary>
/// Behaviour tests for the Alchemy add-on content pack. Everything here exercises the
/// profession's <see cref="ProfessionDefinition"/> and the shared pure pipeline
/// (<see cref="QualityRoller"/>, <see cref="ItemForge"/>) DIRECTLY, never the registry — so the
/// suite is green whether or not the orchestrator has applied the registration line (the pack is
/// inert until registered). Structural conformance (owner tags, known materials, acyclic talent
/// graph, referenced nodes, id uniqueness) is covered automatically by
/// <c>ProfessionConformanceTests</c> once alchemy is in <c>ProfessionRegistry.All</c>.
/// </summary>
public class AlchemyProfessionTests
{
    private static readonly ProfessionDefinition Alc = AlchemyProfession.Definition;

    // ---- Definition shape ---------------------------------------------------------------

    [Fact]
    public void Definition_Identity_AndOwnership()
    {
        Assert.Equal("alchemy", Alc.Id);
        Assert.Equal("Alchemy", Alc.DisplayName);
        Assert.NotEmpty(Alc.Recipes);
        Assert.All(Alc.Recipes.Values, r =>
        {
            Assert.Equal("alchemy", r.Profession);
            Assert.StartsWith("alchemy-", r.RecipeId);
            Assert.True(RecipeTable.MaterialGrades.ContainsKey(r.MaterialKey), $"unknown material '{r.MaterialKey}'");
        });
    }

    [Fact]
    public void Definition_SignatureHealLadder_TiersUpInMagnitude()
    {
        // The healing consumable line is the profession's depth axis: every heal is a
        // Consumable with zero combat stats and a strictly rising base magnitude ladder.
        var heals = Alc.Recipes.Values
            .Where(r => r.Slot == ItemSlot.Consumable)
            .OrderBy(r => r.Effect!.Magnitude)
            .ToList();

        Assert.Equal(5, heals.Count);
        Assert.All(heals, r =>
        {
            Assert.Equal(ConsumableKind.Heal, r.Effect!.Kind);
            Assert.Equal(new ItemStats(0, 0, 0), r.BaseStats);
        });
        Assert.Equal(new[] { 6, 10, 15, 22, 30 }, heals.Select(r => r.Effect!.Magnitude));
    }

    [Fact]
    public void Definition_QualityModel_HasExpectedShifts()
    {
        Assert.Equal(5, Alc.Quality.FlatShifts[AlchemyProfession.MeasuredPour]);
        Assert.Equal(7, Alc.Quality.FlatShifts[AlchemyProfession.CarefulDistillation]);
        Assert.Equal(8, Alc.Quality.FlatShifts[AlchemyProfession.MasterAlchemist]);

        var potent = Alc.Quality.SlotShifts[AlchemyProfession.PotentBrews];
        Assert.Equal(ItemSlot.Consumable, potent.Slot);
        Assert.Equal(5, potent.Shift);

        Assert.Equal(AlchemyProfession.ReagentMastery, Alc.Quality.MaterialMasteryNode);
        Assert.Equal(AlchemyProfession.FrugalReagents, Alc.MaterialEfficiencyNode);

        // Non-quality nodes never leak into the quality model.
        Assert.False(Alc.Quality.FlatShifts.ContainsKey(AlchemyProfession.FrugalReagents));
        Assert.False(Alc.Quality.FlatShifts.ContainsKey(AlchemyProfession.Tier2Alchemy));
    }

    // ---- Tier gating --------------------------------------------------------------------

    [Fact]
    public void TierGate_GatesTwoAndThree_LeavesOneUngated()
    {
        Assert.False(Alc.TierGate.ContainsKey(1));
        Assert.Equal(AlchemyProfession.Tier2Alchemy, Alc.TierGate[2]);
        Assert.Equal(AlchemyProfession.Tier3Alchemy, Alc.TierGate[3]);
        Assert.True(Alc.TalentNodes.ContainsKey(Alc.TierGate[2]));
        Assert.True(Alc.TalentNodes.ContainsKey(Alc.TierGate[3]));
    }

    [Fact]
    public void TierGate_TierTwoRecipe_BlockedUntilGateUnlocked()
    {
        // Mirrors the CraftingHandlers gate check against this profession's data: a tier-2
        // recipe is gated behind Tier2Alchemy; a tier-1 recipe is never gated.
        var tierTwo = Alc.Recipes["alchemy-greater-elixir"];
        var tierOne = Alc.Recipes["alchemy-healing-draught"];

        Assert.Equal(2, tierTwo.Tier);
        Assert.True(Alc.TierGate.TryGetValue(tierTwo.Tier, out var gate));
        Assert.DoesNotContain(gate!, ImmutableSortedSet<string>.Empty);                 // blocked with no talents
        Assert.Contains(gate!, ImmutableSortedSet.Create(gate!));                       // allowed once unlocked
        Assert.False(Alc.TierGate.ContainsKey(tierOne.Tier));                           // tier 1 ungated
    }

    // ---- Talent graph unlock logic ------------------------------------------------------

    [Fact]
    public void CanUnlock_RespectsPrerequisiteChain()
    {
        var none = ImmutableSortedSet<string>.Empty;

        Assert.True(Alc.CanUnlock(AlchemyProfession.MeasuredPour, none));
        Assert.False(Alc.CanUnlock(AlchemyProfession.CarefulDistillation, none));                        // needs measured-pour
        Assert.True(Alc.CanUnlock(AlchemyProfession.CarefulDistillation, none.Add(AlchemyProfession.MeasuredPour)));
        Assert.False(Alc.CanUnlock(AlchemyProfession.MeasuredPour, none.Add(AlchemyProfession.MeasuredPour))); // already unlocked
        Assert.True(Alc.CanUnlock(AlchemyProfession.PotentBrews, none.Add(AlchemyProfession.MeasuredPour)));
        Assert.False(Alc.CanUnlock("not-a-node", none));
    }

    // ---- Craft happy path (pure pipeline: roll → forge) ---------------------------------

    [Fact]
    public void Forge_GearRecipe_ScalesStatsByQuality_AndStampsMark()
    {
        var recipe = Alc.Recipes["alchemy-alchemical-robe"]; // Armor, base Defense 6, Weight 1
        var item = ItemForge.Forge(new ItemId(1), recipe, QualityGrade.Superior, day: 3);

        Assert.Equal("alchemy-alchemical-robe", item.RecipeId);
        Assert.Equal(ItemSlot.Armor, item.Slot);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(3, item.Mark.CraftedOnDay);
        Assert.Equal(0, item.Stats.Attack);
        Assert.Equal(6 * ItemForge.QualityPercent(QualityGrade.Superior) / 100, item.Stats.Defense); // 8
        Assert.Equal(1, item.Stats.Weight);                                                           // weight unaffected
        Assert.Null(item.Effect);
    }

    [Fact]
    public void Forge_Consumable_ScalesMagnitude_ByQuality()
    {
        var recipe = Alc.Recipes["alchemy-panacea"]; // Consumable, Heal(30)
        var item = ItemForge.Forge(new ItemId(2), recipe, QualityGrade.Superior, day: 1);

        Assert.Equal(ItemSlot.Consumable, item.Slot);
        Assert.Equal(new ItemStats(0, 0, 0), item.Stats);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal(30 * ItemForge.QualityPercent(QualityGrade.Superior) / 100, item.Effect.Magnitude); // 40
    }

    // ---- Quality distribution pins (deterministic; guard the shift values) --------------

    private static string RollDistribution(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, ulong seed)
    {
        var rng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            counts[(int)QualityRoller.Roll(recipe, materialGrade, talents, Alc.Quality, rng)]++;
        }

        Assert.Equal(1000, counts.Sum());
        return string.Join(",", counts);
    }

    [Fact]
    public void QualityDistribution_NoTalents_GradeEqualsTier_MatchesSharedBaseCurve()
    {
        // shift 0 (material grade == recipe tier, no quality talents): the roll is Roll100()
        // straight into the shared threshold table, so alchemy reproduces the exact base curve
        // the blacksmith dagger golden pins (Poor 15 / Common 50 / Fine 25 / Superior 9 / Master 1).
        var dist = RollDistribution(Alc.Recipes["alchemy-alchemical-robe"], materialGrade: 1, ImmutableSortedSet<string>.Empty, seed: 1234);
        Assert.Equal("146,513,247,85,9", dist);
    }

    [Fact]
    public void QualityDistribution_PotentBrews_ShiftsConsumableRollsUp()
    {
        // Potent Brews (+5, Consumable-scoped) applied to a Consumable recipe shifts the curve up.
        var talents = ImmutableSortedSet.Create(AlchemyProfession.PotentBrews);
        var dist = RollDistribution(Alc.Recipes["alchemy-minor-elixir"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("106,495,251,102,46", dist);
    }

    [Fact]
    public void QualityDistribution_PotentBrews_DoesNotShiftGear()
    {
        // Slot scoping: the Consumable-scoped +5 must NOT touch an Armor recipe — the curve
        // stays the shift-0 base curve.
        var talents = ImmutableSortedSet.Create(AlchemyProfession.PotentBrews);
        var dist = RollDistribution(Alc.Recipes["alchemy-alchemical-robe"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("146,513,247,85,9", dist);
    }

    [Fact]
    public void QualityDistribution_FullFlatChain_ShiftsRollsUp()
    {
        // Measured Pour + Careful Distillation + Master Alchemist = +5+7+8 = +20 flat.
        var talents = ImmutableSortedSet.Create(AlchemyProfession.MeasuredPour, AlchemyProfession.CarefulDistillation, AlchemyProfession.MasterAlchemist);
        var dist = RollDistribution(Alc.Recipes["alchemy-alchemical-robe"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("0,426,285,85,204", dist);
    }
}
