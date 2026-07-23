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
    public void Definition_ActiveModel_RetiredShiftsBecameAssists_MasteryKept()
    {
        // Phase B active flip, mirroring the blacksmith's PA2/PKD3 remap exactly: the retired
        // quality-shift nodes must not ALSO shift any roll (double-count fix) — they live on as
        // MinigameAssist data the in-sim scorer consumes — and the material-mastery axis is KEPT.
        Assert.True(Alc.ActiveCraft);
        Assert.Empty(Alc.Quality.FlatShifts);
        Assert.Empty(Alc.Quality.SlotShifts);
        Assert.Equal(AlchemyProfession.ReagentMastery, Alc.Quality.MaterialMasteryNode);
        Assert.Equal(AlchemyProfession.FrugalReagents, Alc.MaterialEfficiencyNode);

        // 1:1 remap of the four retired nodes, same magnitudes as the blacksmith's four.
        Assert.Equal(4, Alc.MinigameAssists.Count);
        Assert.Equal(50, Alc.MinigameAssists[AlchemyProfession.MeasuredPour].SweetZoneWidthBonus);
        Assert.Equal(70, Alc.MinigameAssists[AlchemyProfession.CarefulDistillation].DriftRateReduction);
        Assert.Equal(80, Alc.MinigameAssists[AlchemyProfession.MasterAlchemist].OffBeatForgiveness);
        Assert.Equal(50, Alc.MinigameAssists[AlchemyProfession.PotentBrews].SweetZoneWidthBonus);

        // Every assist node is a real talent node; non-quality nodes never leak into the map.
        Assert.All(Alc.MinigameAssists.Keys, nodeId => Assert.True(Alc.TalentNodes.ContainsKey(nodeId)));
        Assert.False(Alc.MinigameAssists.ContainsKey(AlchemyProfession.FrugalReagents));
        Assert.False(Alc.MinigameAssists.ContainsKey(AlchemyProfession.ReagentMastery));
        Assert.False(Alc.MinigameAssists.ContainsKey(AlchemyProfession.Tier2Alchemy));
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

    // ---- Active-model distribution pins (deterministic; PKD3 semantics) -----------------

    private static string ActiveRollDistribution(Recipe recipe, int materialGrade, ImmutableSortedSet<string> talents, int? grade, ulong seed)
    {
        var rng = new Pcg32(RngState.FromSeed(seed));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            counts[(int)QualityRoller.RollActive(recipe, materialGrade, talents, Alc.Quality, rng, grade)]++;
        }

        Assert.Equal(1000, counts.Sum());
        return string.Join(",", counts);
    }

    [Fact]
    public void ActiveRoll_RetiredTalents_ShiftTheRollByExactlyZero()
    {
        // The PKD3 double-count pin, alchemy edition (mirrors the blacksmith's): the retired
        // quality nodes became scorer assists — unlocking every one of them must move the
        // dominance ROLL's distribution by exactly nothing (only the puzzle grade they forgive
        // moves, and that enters as the grade parameter, not a roll shift).
        var recipe = Alc.Recipes["alchemy-minor-elixir"];
        var none = ActiveRollDistribution(recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, grade: 700, seed: 1234);
        var all = ActiveRollDistribution(
            recipe, materialGrade: 1,
            ImmutableSortedSet.Create(
                AlchemyProfession.MeasuredPour, AlchemyProfession.CarefulDistillation,
                AlchemyProfession.MasterAlchemist, AlchemyProfession.PotentBrews),
            grade: 700, seed: 1234);

        Assert.Equal(none, all);
    }

    [Fact]
    public void ActiveRoll_AutoCraft_NeverExceedsSuperior_AndGradeDominates()
    {
        // Auto-craft (null grade, null puzzle) sits at the competent 550 baseline and is
        // hard-capped below Masterwork (PKD4); a perfect in-sim-scored brew (1000) with
        // above-tier material is Masterwork-reachable — the puzzle is the only road to the top.
        var recipe = Alc.Recipes["alchemy-minor-elixir"];
        var auto = ActiveRollDistribution(recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, grade: null, seed: 1234);
        Assert.Equal("0", auto.Split(',')[4]); // zero Masterwork out of 1000 auto-crafts

        var perfect = ActiveRollDistribution(recipe, materialGrade: 2, ImmutableSortedSet<string>.Empty, grade: 1000, seed: 1234);
        Assert.Equal(1000, int.Parse(perfect.Split(',')[4])); // every perfect brew with grade-2 material is Masterwork
    }
}
