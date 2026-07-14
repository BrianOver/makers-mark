using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

public class RecipeTableTests
{
    [Fact]
    public void Table_Has15Recipes_FiveTimesEachSlot_AcrossThreeTiers()
    {
        Assert.Equal(15, RecipeTable.All.Count);

        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            Assert.Equal(5, RecipeTable.All.Values.Count(r => r.Slot == slot));
        }

        Assert.Equal(new[] { 1, 2, 3 }, RecipeTable.All.Values.Select(r => r.Tier).Distinct().OrderBy(t => t));
    }

    [Fact]
    public void EveryRecipe_IsWellFormed()
    {
        foreach (var (key, recipe) in RecipeTable.All)
        {
            Assert.Equal(key, recipe.RecipeId);
            Assert.False(string.IsNullOrWhiteSpace(recipe.Name));
            Assert.InRange(recipe.Tier, 1, 3);
            Assert.True(recipe.MaterialQuantity >= 1);
            Assert.True(RecipeTable.MaterialGrades.ContainsKey(recipe.MaterialKey), $"{key}: unknown material '{recipe.MaterialKey}'");
            Assert.True(recipe.BaseStats.Weight >= 1);

            if (recipe.Slot == ItemSlot.Weapon)
            {
                Assert.True(recipe.BaseStats.Attack > 0);
                Assert.Equal(0, recipe.BaseStats.Defense);
            }
            else
            {
                Assert.Equal(0, recipe.BaseStats.Attack);
                Assert.True(recipe.BaseStats.Defense > 0);
            }
        }
    }

    [Fact]
    public void MaterialGrades_MatchTheSpec()
    {
        Assert.Equal(5, RecipeTable.MaterialGrades.Count);
        Assert.Equal(1, RecipeTable.MaterialGrades["copper"]);
        Assert.Equal(2, RecipeTable.MaterialGrades["iron"]);
        Assert.Equal(3, RecipeTable.MaterialGrades["steel"]);
        Assert.Equal(4, RecipeTable.MaterialGrades["mithril"]);
        Assert.Equal(5, RecipeTable.MaterialGrades["adamant"]);
    }

    [Fact]
    public void Stats_ScaleUpWithTier_PerSlot()
    {
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            for (var tier = 1; tier < 3; tier++)
            {
                var maxThisTier = RecipeTable.All.Values
                    .Where(r => r.Slot == slot && r.Tier == tier)
                    .Max(r => r.BaseStats.Attack + r.BaseStats.Defense);
                var minNextTier = RecipeTable.All.Values
                    .Where(r => r.Slot == slot && r.Tier == tier + 1)
                    .Min(r => r.BaseStats.Attack + r.BaseStats.Defense);

                Assert.True(minNextTier > maxThisTier, $"{slot}: tier {tier + 1} min ({minNextTier}) must exceed tier {tier} max ({maxThisTier})");
            }
        }
    }

    [Fact]
    public void TwoHandedAndHeavyRecipes_WeighMoreThanTheirTierPeers()
    {
        // Two-handed weapons outweigh one-handers of the same tier.
        Assert.True(RecipeTable.All["greataxe"].BaseStats.Weight > RecipeTable.All["longsword"].BaseStats.Weight);
        // Heavy shield outweighs the standard shield of the same tier.
        Assert.True(RecipeTable.All["tower-shield"].BaseStats.Weight > RecipeTable.All["kite-shield"].BaseStats.Weight);
        // Heavy armor outweighs the standard armor of the same tier.
        Assert.True(RecipeTable.All["half-plate"].BaseStats.Weight > RecipeTable.All["hauberk"].BaseStats.Weight);
    }

    [Fact]
    public void TryGet_FindsKnown_RejectsUnknown()
    {
        Assert.True(RecipeTable.TryGet("dagger", out var recipe));
        Assert.Equal("dagger", recipe!.RecipeId);
        Assert.False(RecipeTable.TryGet("excalibur", out _));
    }
}
