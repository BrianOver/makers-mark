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
    public void Definition_ActiveModel_RetiredShiftsBecameAssists_MasteryKept()
    {
        // U3b active flip, mirroring the blacksmith/alchemy PA2/PKD3 remap exactly: the retired
        // quality-shift nodes must not ALSO shift any roll (double-count fix) — they live on as
        // MinigameAssist data the in-sim scorer consumes — and the material-mastery axis is KEPT.
        Assert.True(Tan.ActiveCraft);
        Assert.Empty(Tan.Quality.FlatShifts);
        Assert.Empty(Tan.Quality.SlotShifts);
        Assert.Equal(TanningProfession.HideMastery, Tan.Quality.MaterialMasteryNode);
        Assert.Equal(TanningProfession.Thrift, Tan.MaterialEfficiencyNode);

        // 1:1 remap of the four retired nodes, at alchemy's 50/70/80 ladder.
        Assert.Equal(4, Tan.MinigameAssists.Count);
        Assert.Equal(50, Tan.MinigameAssists[TanningProfession.SteadyHand].SweetZoneWidthBonus);
        Assert.Equal(70, Tan.MinigameAssists[TanningProfession.SuppleWork].DriftRateReduction);
        Assert.Equal(80, Tan.MinigameAssists[TanningProfession.MasterTanner].OffBeatForgiveness);
        Assert.Equal(50, Tan.MinigameAssists[TanningProfession.Armorer].SweetZoneWidthBonus);

        // Every assist node is a real talent node; non-quality nodes never leak into the map.
        Assert.All(Tan.MinigameAssists.Keys, nodeId => Assert.True(Tan.TalentNodes.ContainsKey(nodeId)));
        Assert.False(Tan.MinigameAssists.ContainsKey(TanningProfession.Thrift));
        Assert.False(Tan.MinigameAssists.ContainsKey(TanningProfession.HideMastery));
        Assert.False(Tan.MinigameAssists.ContainsKey(TanningProfession.Tier2Tanning));
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

    // ---- Active-model distribution pins (deterministic; PKD3 semantics) -----------------

    private static string ActiveRollDistribution(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, int? grade, ulong seed)
    {
        var rng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            counts[(int)QualityRoller.RollActive(recipe, materialGrade, talents, Tan.Quality, rng, grade)]++;
        }

        Assert.Equal(1000, counts.Sum());
        return string.Join(",", counts);
    }

    [Fact]
    public void ActiveRoll_RetiredTalents_ShiftTheRollByExactlyZero()
    {
        // The PKD3 double-count pin, tanning edition (mirrors the blacksmith/alchemy): the
        // retired quality nodes became scorer assists — unlocking every one of them must move
        // the dominance ROLL's distribution by exactly nothing (only the puzzle grade they
        // forgive moves, and that enters as the grade parameter, not a roll shift).
        var recipe = Tan.Recipes["tanning-hide-jerkin"];
        var none = ActiveRollDistribution(recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, grade: 700, seed: 1234);
        var all = ActiveRollDistribution(
            recipe, materialGrade: 1,
            ImmutableSortedSet.Create(
                TanningProfession.SteadyHand, TanningProfession.SuppleWork,
                TanningProfession.MasterTanner, TanningProfession.Armorer),
            grade: 700, seed: 1234);

        Assert.Equal(none, all);
    }

    [Fact]
    public void ActiveRoll_AutoCraft_NeverExceedsSuperior_AndGradeDominates()
    {
        // Auto-craft (null grade, null puzzle) sits at the competent 550 baseline and is
        // hard-capped below Masterwork (PKD4); a perfect in-sim-scored scrape (1000) with
        // above-tier material is Masterwork-reachable — the puzzle is the only road to the top.
        var recipe = Tan.Recipes["tanning-hide-jerkin"];
        var auto = ActiveRollDistribution(recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, grade: null, seed: 1234);
        Assert.Equal("0", auto.Split(',')[4]); // zero Masterwork out of 1000 auto-crafts

        var perfect = ActiveRollDistribution(recipe, materialGrade: 2, ImmutableSortedSet<string>.Empty, grade: 1000, seed: 1234);
        Assert.Equal(1000, int.Parse(perfect.Split(',')[4])); // every perfect scrape with grade-2 material is Masterwork
    }
}
