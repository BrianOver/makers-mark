using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>Integer combat stats. No floats in the sim (cross-OS determinism).</summary>
public readonly record struct ItemStats(int Attack, int Defense, int Weight);

/// <summary>
/// Phase C U-C1 craft-modifier family. Slot-exclusive (Vagrant-Story trinity + Hades slot
/// exclusivity): an item carries at most one modifier per family, so a fully-composed masterwork
/// holds up to one <see cref="QuenchOil"/> + one <see cref="Rune"/> + one <see cref="Fitting"/>.
/// </summary>
public enum ModifierFamily { QuenchOil, Rune, Fitting }

/// <summary>
/// Phase C U-C1: a craft modifier stamped onto an item at the forge. DATA only — the integer
/// effects are resolved from <c>GameSim.Crafting.CraftModifiers</c> by <see cref="Id"/>, never
/// stored here, so the resolver reads one registry table and add-on modifiers ride the same path
/// (the <see cref="ConsumableEffect"/> precedent). <see cref="Tier"/> (1..2, material-tier capped)
/// scales the registry effect. No stat-only modifiers: every registered effect is an integer delta
/// or flag on a hero-AI decision threshold, never a passive Attack/Defense bump.
/// </summary>
public sealed record CraftModifier(string Id, ModifierFamily Family, int Tier);

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

    /// <summary>Phase C U-C1: survival quench-oil treatment on this item, or null. Slot-exclusive
    /// per <see cref="ModifierFamily"/>. Trailing init member (save-compat — old saves have no
    /// property → null → no modifier). DATA; effects resolve through
    /// <c>GameSim.Crafting.CraftModifiers</c>.</summary>
    public CraftModifier? QuenchOil { get; init; } = null;

    /// <summary>Phase C U-C1: combat rune on this item, or null. Trailing init (save-compat).</summary>
    public CraftModifier? Rune { get; init; } = null;

    /// <summary>Phase C U-C1: movement/economy fitting on this item, or null. Trailing init
    /// (save-compat).</summary>
    public CraftModifier? Fitting { get; init; } = null;

    /// <summary>Enumerates the item's non-null modifier slots (forge composition + resolver aggregation).</summary>
    public IEnumerable<CraftModifier> Modifiers
    {
        get
        {
            if (QuenchOil is { } q) yield return q;
            if (Rune is { } r) yield return r;
            if (Fitting is { } f) yield return f;
        }
    }

    public bool PlayerCrafted => Mark is not null;
}
