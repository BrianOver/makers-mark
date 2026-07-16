using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>
/// What a hero has equipped. Slots hold item ids resolvable in <see cref="GameState.Items"/>.
/// <see cref="Trinket"/> is the P2 fourth slot (trailing optional — old saves deserialize null;
/// trinket CONTENT arrives with later add-ons).
/// </summary>
public sealed record GearSet(ItemId? Weapon, ItemId? Shield, ItemId? Armor, ItemId? Trinket = null)
{
    public static readonly GearSet Empty = new(null, null, null);

    public ItemId? Slot(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => Weapon,
        ItemSlot.Shield => Shield,
        ItemSlot.Armor => Armor,
        ItemSlot.Trinket => Trinket,
        _ => null,
    };

    public GearSet WithSlot(ItemSlot slot, ItemId? id) => slot switch
    {
        ItemSlot.Weapon => this with { Weapon = id },
        ItemSlot.Shield => this with { Shield = id },
        ItemSlot.Armor => this with { Armor = id },
        ItemSlot.Trinket => this with { Trinket = id },
        _ => this,
    };
}

/// <summary>A hero's memory of a specific item's performance — feeds gossip and shopping (R7, R14).</summary>
public sealed record ItemMemory(ItemId Item, int Kills, int Saves);

/// <summary>
/// An autonomous adventurer (A2). Permadeath: <see cref="Alive"/> flips once, never back (R7).
/// </summary>
public sealed record Hero(
    HeroId Id,
    string Name,
    HeroRole Role,
    int Level,
    int MaxHp,
    int Gold,
    GearSet Gear,
    ImmutableList<ItemMemory> Memories,
    bool Alive,
    int DeepestFloorReached,
    int? DiedOnDay)
{
    /// <summary>
    /// Carried consumables (P2), in purchase order — the resolver quaffs the FIRST
    /// matching item, so list order is part of the determinism contract. Persists
    /// across days until used. Non-positional init member (same shape as
    /// <see cref="GameEvent.Id"/>) so old saves and existing constructors default to empty.
    /// </summary>
    public ImmutableList<ItemId> Pack { get; init; } = ImmutableList<ItemId>.Empty;

    /// <summary>Simple additive gear score used by shopping and floor gates. Integer math only.</summary>
    public static int GearScore(GearSet gear, ImmutableSortedDictionary<int, Item> items)
    {
        var score = 0;
        foreach (var slot in new[] { gear.Weapon, gear.Shield, gear.Armor, gear.Trinket })
        {
            if (slot is { } id && items.TryGetValue(id.Value, out var item))
            {
                score += item.Stats.Attack + item.Stats.Defense;
            }
        }

        return score;
    }
}
