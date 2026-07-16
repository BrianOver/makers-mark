using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// The P2 reference consumable: the blacksmith's field-salve recipe, living in the SAME
/// table as gear (one pipeline, distinguished purely by <see cref="Recipe.Effect"/> data),
/// forged through the standard pipeline with the Heal magnitude scaled by the same
/// quality table as combat stats.
/// </summary>
public class ConsumableRecipeTests
{
    [Fact]
    public void FieldSalve_MatchesTheDesignContract()
    {
        var recipe = RecipeTable.All["field-salve"];

        Assert.Equal("Field Salve", recipe.Name);
        Assert.Equal(RecipeTable.BlacksmithProfession, recipe.Profession);
        Assert.Equal(ItemSlot.Consumable, recipe.Slot);
        Assert.Equal(1, recipe.Tier);
        Assert.Equal("copper", recipe.MaterialKey);
        Assert.Equal(2, recipe.MaterialQuantity);
        Assert.Equal(new ItemStats(0, 0, 0), recipe.BaseStats);
        Assert.Equal(new ConsumableEffect(ConsumableKind.Heal, 6), recipe.Effect);
        // Zero new material keys: the salve rides the existing copper economy.
        Assert.True(RecipeTable.MaterialGrades.ContainsKey(recipe.MaterialKey));
    }

    [Fact]
    public void FieldSalve_ReachableThroughTheProfessionRegistry_LikeAnyRecipe()
    {
        // The add-on modularity proof: the consumable rides the P1 profession pipeline —
        // present in the blacksmith definition and the global registry lookup, no side table.
        Assert.True(ProfessionRegistry.AllRecipes.ContainsKey("field-salve"));
        Assert.True(ProfessionRegistry.Blacksmith.Recipes.ContainsKey("field-salve"));
    }

    [Fact]
    public void GearRecipes_CarryNoEffect_ConsumablesCarryOne()
    {
        Assert.All(RecipeTable.All.Values.Where(r => r.Slot != ItemSlot.Consumable), r => Assert.Null(r.Effect));
        Assert.All(RecipeTable.All.Values.Where(r => r.Slot == ItemSlot.Consumable), r => Assert.NotNull(r.Effect));
    }

    [Theory]
    [InlineData(QualityGrade.Poor, 4)]        // 6 * 80 / 100 = 4
    [InlineData(QualityGrade.Common, 6)]      // 6 * 100 / 100 = 6
    [InlineData(QualityGrade.Fine, 6)]        // 6 * 115 / 100 = 6 (integer division)
    [InlineData(QualityGrade.Superior, 8)]    // 6 * 135 / 100 = 8
    [InlineData(QualityGrade.Masterwork, 9)]  // 6 * 160 / 100 = 9
    public void Forge_ScalesHealMagnitude_ByTheQualityTable(QualityGrade quality, int expectedMagnitude)
    {
        var recipe = RecipeTable.All["field-salve"];
        var item = ItemForge.Forge(new ItemId(1), recipe, quality, day: 3);

        Assert.Equal(ItemSlot.Consumable, item.Slot);
        Assert.Equal(new ItemStats(0, 0, 0), item.Stats);
        Assert.Equal(new ConsumableEffect(ConsumableKind.Heal, expectedMagnitude), item.Effect);
        Assert.True(item.PlayerCrafted);
    }

    [Fact]
    public void Kernel_CraftsFieldSalve_ThroughTheStandardPipeline()
    {
        var kernel = new GameKernel(
            ImmutableList<IPhaseSystem>.Empty,
            ImmutableList.Create<IActionHandler>(new CraftingHandlers()));
        var state = GameFactory.NewGame(seed: 42);
        state = state with { Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", 5) } };

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("field-salve", "copper")));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal("field-salve", item.RecipeId);
        Assert.Equal(ItemSlot.Consumable, item.Slot);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(3, result.NewState.Player.Materials["copper"]); // consumes 2
        Assert.Single(result.Events.OfType<ItemCrafted>());
    }
}
