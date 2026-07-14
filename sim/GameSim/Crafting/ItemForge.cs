using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>
/// Pure item minting (R4/R5). Quality multipliers, integer math only (stats * pct / 100,
/// C# integer division — no floats, KTD4):
///
///   Poor 80% | Common 100% | Fine 115% | Superior 135% | Masterwork 160%
///
/// Attack and Defense scale; Weight does NOT — quality changes edge and fit, not mass.
/// Every player craft is stamped <c>MakersMark("You", day)</c> (R5) and starts with an
/// empty history; kills/saves/bearer entries are appended by other modules.
/// </summary>
public static class ItemForge
{
    /// <summary>Quality multiplier in percent.</summary>
    public static int QualityPercent(QualityGrade quality) => quality switch
    {
        QualityGrade.Poor => 80,
        QualityGrade.Common => 100,
        QualityGrade.Fine => 115,
        QualityGrade.Superior => 135,
        QualityGrade.Masterwork => 160,
        _ => 100,
    };

    /// <summary>Mint the item record. Pure: no RNG, no state — callers allocate the id.</summary>
    public static Item Forge(ItemId id, Recipe recipe, QualityGrade quality, int day)
    {
        var pct = QualityPercent(quality);
        var stats = new ItemStats(
            Attack: recipe.BaseStats.Attack * pct / 100,
            Defense: recipe.BaseStats.Defense * pct / 100,
            Weight: recipe.BaseStats.Weight);

        return new Item(
            id,
            recipe.RecipeId,
            recipe.Name,
            recipe.Slot,
            quality,
            stats,
            new MakersMark("You", day),
            ImmutableList<ItemHistoryEntry>.Empty);
    }
}
