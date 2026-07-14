using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>Integer combat stats. No floats in the sim (cross-OS determinism).</summary>
public readonly record struct ItemStats(int Attack, int Defense, int Weight);

/// <summary>Who forged the item. Player-crafted items carry the mark; rival-vendor goods do not (R5).</summary>
public sealed record MakersMark(string CrafterName, int CraftedOnDay);

/// <summary>One appended entry in an item's lifetime history (R5): kills, saves, bearer changes.</summary>
public sealed record ItemHistoryEntry(int Day, string Kind, string Detail);

/// <summary>
/// A concrete item instance. Player crafts and rival stock both use this shape;
/// <see cref="Mark"/> is null for rival-vendor goods.
/// </summary>
public sealed record Item(
    ItemId Id,
    string RecipeId,
    string Name,
    ItemSlot Slot,
    QualityGrade Quality,
    ItemStats Stats,
    MakersMark? Mark,
    ImmutableList<ItemHistoryEntry> History)
{
    public bool PlayerCrafted => Mark is not null;
}
