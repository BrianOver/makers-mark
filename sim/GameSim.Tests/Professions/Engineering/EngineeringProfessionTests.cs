using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Professions.Engineering;

/// <summary>
/// Behaviour tests for the Engineering add-on content pack. Everything here exercises the
/// profession's <see cref="ProfessionDefinition"/> and the shared pure pipeline
/// (<see cref="QualityRoller"/>, <see cref="ItemForge"/>) DIRECTLY, never the registry — so the
/// suite is green whether or not the orchestrator has applied the registration line (the pack is
/// inert until registered). Structural conformance (owner tags, known materials, acyclic talent
/// graph, referenced nodes, id uniqueness) is covered automatically by
/// <c>ProfessionConformanceTests</c> once engineering is in <c>ProfessionRegistry.All</c>.
///
/// Engineering is the FIRST profession to ship <see cref="ItemSlot.Trinket"/> content (the slot is
/// fully wired by the P2 contract but had zero content until now); its Gadgeteer node is a
/// Trinket-scoped quality specialist, mirroring the blacksmith's Weapon-scoped Weapon Specialist.
/// </summary>
public class EngineeringProfessionTests
{
    private static readonly ProfessionDefinition Eng = EngineeringProfession.Definition;

    // ---- Definition shape ---------------------------------------------------------------

    [Fact]
    public void Definition_Identity_AndOwnership()
    {
        Assert.Equal("engineering", Eng.Id);
        Assert.Equal("Engineering", Eng.DisplayName);
        Assert.NotEmpty(Eng.Recipes);
        Assert.All(Eng.Recipes.Values, r =>
        {
            Assert.Equal("engineering", r.Profession);
            Assert.StartsWith("engineering-", r.RecipeId);
            Assert.True(RecipeTable.MaterialGrades.ContainsKey(r.MaterialKey), $"unknown material '{r.MaterialKey}'");
        });
    }

    [Fact]
    public void Definition_ShipsTrinketContent_FirstProfessionToUseTheSlot()
    {
        // The Trinket slot is fully wired (P2 contract) but had no content; Engineering is first.
        var trinkets = Eng.Recipes.Values.Where(r => r.Slot == ItemSlot.Trinket).ToList();
        Assert.NotEmpty(trinkets);
        Assert.Contains(trinkets, r => r.RecipeId == "engineering-utility-multitool");
        Assert.Contains(trinkets, r => r.RecipeId == "engineering-targeting-monocle");

        // The slot specialist is Trinket-scoped — the Trinket analogue of Weapon Specialist.
        var gadgeteer = Eng.Quality.SlotShifts[EngineeringProfession.Gadgeteer];
        Assert.Equal(ItemSlot.Trinket, gadgeteer.Slot);
    }

    [Fact]
    public void Definition_QualityModel_HasExpectedShifts()
    {
        Assert.Equal(5, Eng.Quality.FlatShifts[EngineeringProfession.Precision]);
        Assert.Equal(7, Eng.Quality.FlatShifts[EngineeringProfession.FineTolerance]);
        Assert.Equal(8, Eng.Quality.FlatShifts[EngineeringProfession.MasterMachinist]);

        var gadgeteer = Eng.Quality.SlotShifts[EngineeringProfession.Gadgeteer];
        Assert.Equal(ItemSlot.Trinket, gadgeteer.Slot);
        Assert.Equal(5, gadgeteer.Shift);

        Assert.Equal(EngineeringProfession.AlloyMastery, Eng.Quality.MaterialMasteryNode);
        Assert.Equal(EngineeringProfession.Salvage, Eng.MaterialEfficiencyNode);

        // Non-quality nodes never leak into the quality model.
        Assert.False(Eng.Quality.FlatShifts.ContainsKey(EngineeringProfession.Salvage));
        Assert.False(Eng.Quality.FlatShifts.ContainsKey(EngineeringProfession.Tier2Engineering));
    }

    // ---- Tier gating --------------------------------------------------------------------

    [Fact]
    public void TierGate_GatesTwoAndThree_LeavesOneUngated()
    {
        Assert.False(Eng.TierGate.ContainsKey(1));
        Assert.Equal(EngineeringProfession.Tier2Engineering, Eng.TierGate[2]);
        Assert.Equal(EngineeringProfession.Tier3Engineering, Eng.TierGate[3]);
        Assert.True(Eng.TalentNodes.ContainsKey(Eng.TierGate[2]));
        Assert.True(Eng.TalentNodes.ContainsKey(Eng.TierGate[3]));
    }

    [Fact]
    public void TierGate_TierTwoRecipe_BlockedUntilGateUnlocked()
    {
        // Mirrors the CraftingHandlers gate check against this profession's data: a tier-2
        // recipe is gated behind Tier2Engineering; a tier-1 recipe is never gated.
        var tierTwo = Eng.Recipes["engineering-clockwork-glaive"];
        var tierOne = Eng.Recipes["engineering-bolt-thrower"];

        Assert.Equal(2, tierTwo.Tier);
        Assert.True(Eng.TierGate.TryGetValue(tierTwo.Tier, out var gate));
        Assert.DoesNotContain(gate!, ImmutableSortedSet<string>.Empty);                 // blocked with no talents
        Assert.Contains(gate!, ImmutableSortedSet.Create(gate!));                       // allowed once unlocked
        Assert.False(Eng.TierGate.ContainsKey(tierOne.Tier));                           // tier 1 ungated
    }

    // ---- Talent graph unlock logic ------------------------------------------------------

    [Fact]
    public void CanUnlock_RespectsPrerequisiteChain()
    {
        var none = ImmutableSortedSet<string>.Empty;

        Assert.True(Eng.CanUnlock(EngineeringProfession.Precision, none));
        Assert.False(Eng.CanUnlock(EngineeringProfession.FineTolerance, none));                        // needs precision
        Assert.True(Eng.CanUnlock(EngineeringProfession.FineTolerance, none.Add(EngineeringProfession.Precision)));
        Assert.False(Eng.CanUnlock(EngineeringProfession.Precision, none.Add(EngineeringProfession.Precision))); // already unlocked
        Assert.True(Eng.CanUnlock(EngineeringProfession.Gadgeteer, none.Add(EngineeringProfession.Precision)));
        Assert.False(Eng.CanUnlock("not-a-node", none));
    }

    // ---- Craft happy path (pure pipeline: roll → forge) ---------------------------------

    [Fact]
    public void Forge_GearRecipe_ScalesStatsByQuality_AndStampsMark()
    {
        var recipe = Eng.Recipes["engineering-powered-vest"]; // Armor, base Defense 7, Weight 3
        var item = ItemForge.Forge(new ItemId(1), recipe, QualityGrade.Fine, day: 3);

        Assert.Equal("engineering-powered-vest", item.RecipeId);
        Assert.Equal(ItemSlot.Armor, item.Slot);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(3, item.Mark.CraftedOnDay);
        Assert.Equal(0, item.Stats.Attack);
        Assert.Equal(7 * ItemForge.QualityPercent(QualityGrade.Fine) / 100, item.Stats.Defense); // 8
        Assert.Equal(3, item.Stats.Weight);                                                       // weight unaffected
        Assert.Null(item.Effect);
    }

    [Fact]
    public void Forge_TrinketRecipe_ScalesStats_AndCarriesTrinketSlot()
    {
        var recipe = Eng.Recipes["engineering-targeting-monocle"]; // Trinket, base Attack 6
        var item = ItemForge.Forge(new ItemId(2), recipe, QualityGrade.Superior, day: 5);

        Assert.Equal(ItemSlot.Trinket, item.Slot);
        Assert.Equal(6 * ItemForge.QualityPercent(QualityGrade.Superior) / 100, item.Stats.Attack); // 8
        Assert.Equal(1, item.Stats.Weight);                                                          // weight unaffected
        Assert.Null(item.Effect);
    }

    [Fact]
    public void Forge_Consumable_ScalesMagnitude_ByQuality()
    {
        var recipe = Eng.Recipes["engineering-field-repair-kit"]; // Consumable, Heal(5) reskin
        var item = ItemForge.Forge(new ItemId(3), recipe, QualityGrade.Superior, day: 1);

        Assert.Equal(ItemSlot.Consumable, item.Slot);
        Assert.Equal(new ItemStats(0, 0, 0), item.Stats);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal(5 * ItemForge.QualityPercent(QualityGrade.Superior) / 100, item.Effect.Magnitude); // 6
    }

    // ---- Quality distribution pins (deterministic; guard the shift values) --------------
    // The PCG roll stream depends only on (seed, draw count), not the recipe, so a given shift
    // reproduces the shared seed-1234 golden curve — the same anchors the blacksmith dagger and
    // tanning goldens pin. shift 0 == base curve; +5 and +20 shift it up identically.

    private static string RollDistribution(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, ulong seed)
    {
        var rng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            counts[(int)QualityRoller.Roll(recipe, materialGrade, talents, Eng.Quality, rng)]++;
        }

        Assert.Equal(1000, counts.Sum());
        return string.Join(",", counts);
    }

    [Fact]
    public void QualityDistribution_NoTalents_GradeEqualsTier_MatchesSharedBaseCurve()
    {
        // shift 0 (material grade == recipe tier, no quality talents): the roll is Roll100()
        // straight into the shared threshold table, so engineering reproduces the exact base curve
        // the blacksmith dagger golden pins (Poor 15 / Common 50 / Fine 25 / Superior 9 / Master 1).
        var dist = RollDistribution(Eng.Recipes["engineering-utility-multitool"], materialGrade: 1, ImmutableSortedSet<string>.Empty, seed: 1234);
        Assert.Equal("146,513,247,85,9", dist);
    }

    [Fact]
    public void QualityDistribution_Gadgeteer_ShiftsTrinketRollsUp()
    {
        // Gadgeteer (+5, Trinket-scoped) applied to a Trinket recipe shifts the curve up vs the
        // base — proving the newly-wired Trinket slot shift routes through the shared roller.
        var talents = ImmutableSortedSet.Create(EngineeringProfession.Gadgeteer);
        var dist = RollDistribution(Eng.Recipes["engineering-utility-multitool"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("106,495,251,102,46", dist);
    }

    [Fact]
    public void QualityDistribution_FullFlatChain_ShiftsRollsUp()
    {
        // Precision + Fine Tolerance + Master Machinist = +5+7+8 = +20 flat.
        var talents = ImmutableSortedSet.Create(EngineeringProfession.Precision, EngineeringProfession.FineTolerance, EngineeringProfession.MasterMachinist);
        var dist = RollDistribution(Eng.Recipes["engineering-utility-multitool"], materialGrade: 1, talents, seed: 1234);
        Assert.Equal("0,426,285,85,204", dist);
    }
}
