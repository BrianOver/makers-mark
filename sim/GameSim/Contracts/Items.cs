using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>Integer combat stats. No floats in the sim (cross-OS determinism).</summary>
public readonly record struct ItemStats(int Attack, int Defense, int Weight);

/// <summary>Who forged the item. Player-crafted items carry the mark; rival-vendor goods do not (R5).</summary>
public sealed record MakersMark(string CrafterName, int CraftedOnDay);

/// <summary>One appended entry in an item's lifetime history (R5): kills, saves, bearer changes.</summary>
public sealed record ItemHistoryEntry(int Day, string Kind, string Detail);

/// <summary>
/// What a consumable item does when used (P2): the resolver and shopping key off THIS
/// DATA, never off recipe or profession ids, so add-on consumables ride the same path
/// as the reference Field Salve with zero mechanism edits (see docs/addon-guide.md).
/// </summary>
public sealed record ConsumableEffect(ConsumableKind Kind, int Magnitude);

/// <summary>
/// A concrete item instance. Player crafts and rival stock both use this shape;
/// <see cref="Mark"/> is null for rival-vendor goods. <see cref="Effect"/> is null for
/// everything but consumables (trailing optional — old saves deserialize null).
/// </summary>
public sealed record Item(
    ItemId Id,
    string RecipeId,
    string Name,
    ItemSlot Slot,
    QualityGrade Quality,
    ItemStats Stats,
    MakersMark? Mark,
    ImmutableList<ItemHistoryEntry> History,
    ConsumableEffect? Effect = null)
{
    /// <summary>
    /// The three forge-beat sub-scores (smelt, forge, quench) captured when this item was crafted
    /// via the active minigame, per-mille, in beat order — stored verbatim for Evening ledger flavor
    /// ("the edge quenched brittle"). DATA, never rules: no sim system keys off it. Empty for
    /// auto-crafted, rival, or pre-Phase-A items. Non-positional init member (save-compat).
    /// </summary>
    public ImmutableList<int> CraftSubScores { get; init; } = ImmutableList<int>.Empty;

    /// <summary>Wave 4 (named artifacts / "Signed Works"): the legend name a rare craft earns, or
    /// null for ordinary gear. When set, this item is a Signed Work — its <see cref="History"/> +
    /// attribution deeds read as its growing inscription, and it outlives its bearer. DATA, never
    /// rules: no sim system keys off it beyond presentation. Trailing init member (save-compat —
    /// old saves have no property → null → unsigned).</summary>
    public string? SignedName { get; init; } = null;

    /// <summary>True once this item has been signed into a named artifact (Wave 4).</summary>
    public bool IsSigned => SignedName is not null;

    /// <summary>Wave 4c (U20, heirloom reforge): the legend-line an item inherits when it is reforged
    /// from a fallen hero's worn gear ("forged from the blade of Sera Deepfall"), or null for ordinary
    /// stock. When set, this item carries the dead forward — the dead persist as inheritance (R6).
    /// DATA, never rules: no sim system keys off it beyond presentation/history. Trailing init member
    /// (save-compat — old saves have no property → null → not an heirloom).</summary>
    public string? HeirloomLineage { get; init; } = null;

    /// <summary>True once this item was reforged from a fallen hero's gear (Wave 4c).</summary>
    public bool IsHeirloom => HeirloomLineage is not null;

    public bool PlayerCrafted => Mark is not null;
}
