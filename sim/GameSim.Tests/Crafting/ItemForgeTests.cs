using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

/// <summary>
/// ItemForge quality multipliers (integer math, stats * pct / 100, C# integer division):
/// Poor 80, Common 100, Fine 115, Superior 135, Masterwork 160. Weight is NOT scaled —
/// quality changes edge and fit, not mass.
/// </summary>
public class ItemForgeTests
{
    [Theory]
    [InlineData(QualityGrade.Poor, 6)]        // 8 * 80 / 100 = 6
    [InlineData(QualityGrade.Common, 8)]      // 8 * 100 / 100 = 8
    [InlineData(QualityGrade.Fine, 9)]        // 8 * 115 / 100 = 9 (integer division)
    [InlineData(QualityGrade.Superior, 10)]   // 8 * 135 / 100 = 10
    [InlineData(QualityGrade.Masterwork, 12)] // 8 * 160 / 100 = 12
    public void WeaponAttack_ScalesByQualityMultiplier(QualityGrade quality, int expectedAttack)
    {
        var recipe = RecipeTable.All["dagger"]; // base attack 8, weight 2
        var item = ItemForge.Forge(new ItemId(1), recipe, quality, day: 3);

        Assert.Equal(expectedAttack, item.Stats.Attack);
        Assert.Equal(0, item.Stats.Defense);
        Assert.Equal(recipe.BaseStats.Weight, item.Stats.Weight); // weight never scales
    }

    [Theory]
    [InlineData(QualityGrade.Poor, 12)]       // 16 * 80 / 100 = 12
    [InlineData(QualityGrade.Common, 16)]
    [InlineData(QualityGrade.Fine, 18)]       // 16 * 115 / 100 = 18
    [InlineData(QualityGrade.Superior, 21)]   // 16 * 135 / 100 = 21
    [InlineData(QualityGrade.Masterwork, 25)] // 16 * 160 / 100 = 25
    public void ShieldDefense_ScalesByQualityMultiplier(QualityGrade quality, int expectedDefense)
    {
        var recipe = RecipeTable.All["kite-shield"]; // base defense 16
        var item = ItemForge.Forge(new ItemId(4), recipe, quality, day: 9);

        Assert.Equal(0, item.Stats.Attack);
        Assert.Equal(expectedDefense, item.Stats.Defense);
        Assert.Equal(recipe.BaseStats.Weight, item.Stats.Weight);
    }

    [Fact]
    public void ForgedItem_CarriesMakersMark_AndEmptyHistory()
    {
        var recipe = RecipeTable.All["longsword"];
        var item = ItemForge.Forge(new ItemId(7), recipe, QualityGrade.Fine, day: 12);

        Assert.Equal(new ItemId(7), item.Id);
        Assert.Equal(recipe.RecipeId, item.RecipeId);
        Assert.Equal(recipe.Name, item.Name);
        Assert.Equal(recipe.Slot, item.Slot);
        Assert.Equal(QualityGrade.Fine, item.Quality);
        Assert.NotNull(item.Mark);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(12, item.Mark.CraftedOnDay);
        Assert.True(item.PlayerCrafted);
        Assert.Empty(item.History);
    }

    [Fact]
    public void HistoryAppend_IsOrdered_AndImmutable()
    {
        var item = ItemForge.Forge(new ItemId(1), RecipeTable.All["dagger"], QualityGrade.Common, day: 1);

        var first = new ItemHistoryEntry(2, "kill", "Slew a cave rat on floor 1");
        var second = new ItemHistoryEntry(3, "save", "Turned a lethal blow on floor 2");

        var withOne = item with { History = item.History.Add(first) };
        var withTwo = withOne with { History = withOne.History.Add(second) };

        // Ordered: entries come back in append order.
        Assert.Equal(new[] { first, second }, withTwo.History);

        // Immutable: earlier snapshots are untouched by later appends.
        Assert.Empty(item.History);
        Assert.Equal(new[] { first }, withOne.History);
    }
}
