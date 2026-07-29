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
    }

    [Fact]
    public void Definition_ActiveModel_RetiredShiftsBecameAssists_MasteryKept()
    {
        // U3b active flip, mirroring the blacksmith/alchemy/tanning PA2/PKD3 remap exactly: the
        // retired quality-shift nodes must not ALSO shift any roll (double-count fix) — they live
        // on as MinigameAssist data the in-sim scorer consumes — and the material-mastery axis
        // is KEPT. The slot specialist (Gadgeteer) is the Trinket analogue of Weapon Specialist,
        // now retired to a Trinket-scoped assist rather than a SlotShift.
        Assert.True(Eng.ActiveCraft);
        Assert.Empty(Eng.Quality.FlatShifts);
        Assert.Empty(Eng.Quality.SlotShifts);
        Assert.Equal(EngineeringProfession.AlloyMastery, Eng.Quality.MaterialMasteryNode);
        Assert.Equal(EngineeringProfession.Salvage, Eng.MaterialEfficiencyNode);

        // 1:1 remap of the four retired nodes, at alchemy's 50/70/80 ladder.
        Assert.Equal(4, Eng.MinigameAssists.Count);
        Assert.Equal(50, Eng.MinigameAssists[EngineeringProfession.Precision].SweetZoneWidthBonus);
        Assert.Equal(70, Eng.MinigameAssists[EngineeringProfession.FineTolerance].DriftRateReduction);
        Assert.Equal(80, Eng.MinigameAssists[EngineeringProfession.MasterMachinist].OffBeatForgiveness);
        Assert.Equal(50, Eng.MinigameAssists[EngineeringProfession.Gadgeteer].SweetZoneWidthBonus);

        // Every assist node is a real talent node; non-quality nodes never leak into the map.
        Assert.All(Eng.MinigameAssists.Keys, nodeId => Assert.True(Eng.TalentNodes.ContainsKey(nodeId)));
        Assert.False(Eng.MinigameAssists.ContainsKey(EngineeringProfession.Salvage));
        Assert.False(Eng.MinigameAssists.ContainsKey(EngineeringProfession.AlloyMastery));
        Assert.False(Eng.MinigameAssists.ContainsKey(EngineeringProfession.Tier2Engineering));
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

    // ---- Active-model distribution pins (deterministic; PKD3 semantics) -----------------

    private static string ActiveRollDistribution(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, int? grade, ulong seed)
    {
        var rng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            counts[(int)QualityRoller.RollActive(recipe, materialGrade, talents, Eng.Quality, rng, grade)]++;
        }

        Assert.Equal(1000, counts.Sum());
        return string.Join(",", counts);
    }

    [Fact]
    public void ActiveRoll_RetiredTalents_ShiftTheRollByExactlyZero()
    {
        // The PKD3 double-count pin, engineering edition (mirrors the blacksmith/alchemy/tanning):
        // the retired quality nodes became scorer assists — unlocking every one of them must move
        // the dominance ROLL's distribution by exactly nothing (only the puzzle grade they forgive
        // moves, and that enters as the grade parameter, not a roll shift).
        var recipe = Eng.Recipes["engineering-utility-multitool"];
        var none = ActiveRollDistribution(recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, grade: 700, seed: 1234);
        var all = ActiveRollDistribution(
            recipe, materialGrade: 1,
            ImmutableSortedSet.Create(
                EngineeringProfession.Precision, EngineeringProfession.FineTolerance,
                EngineeringProfession.MasterMachinist, EngineeringProfession.Gadgeteer),
            grade: 700, seed: 1234);

        Assert.Equal(none, all);
    }

    [Fact]
    public void ActiveRoll_AutoCraft_NeverExceedsSuperior_AndGradeDominates()
    {
        // Auto-craft (null grade, null puzzle) sits at the competent 550 baseline and is
        // hard-capped below Masterwork (PKD4); a perfect in-sim-scored assembly (1000) with
        // above-tier material is Masterwork-reachable — the puzzle is the only road to the top.
        var recipe = Eng.Recipes["engineering-utility-multitool"];
        var auto = ActiveRollDistribution(recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, grade: null, seed: 1234);
        Assert.Equal("0", auto.Split(',')[4]); // zero Masterwork out of 1000 auto-crafts

        var perfect = ActiveRollDistribution(recipe, materialGrade: 2, ImmutableSortedSet<string>.Empty, grade: 1000, seed: 1234);
        Assert.Equal(1000, int.Parse(perfect.Split(',')[4])); // every perfect assembly with grade-2 material is Masterwork
    }
}
