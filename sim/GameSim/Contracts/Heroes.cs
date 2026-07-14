using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>What a hero has equipped. Slots hold item ids resolvable in <see cref="GameState.Items"/>.</summary>
public sealed record GearSet(ItemId? Weapon, ItemId? Shield, ItemId? Armor)
{
    public static readonly GearSet Empty = new(null, null, null);

    public ItemId? Slot(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => Weapon,
        ItemSlot.Shield => Shield,
        ItemSlot.Armor => Armor,
        _ => null,
    };

    public GearSet WithSlot(ItemSlot slot, ItemId? id) => slot switch
    {
        ItemSlot.Weapon => this with { Weapon = id },
        ItemSlot.Shield => this with { Shield = id },
        ItemSlot.Armor => this with { Armor = id },
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
    /// <summary>Simple additive gear score used by shopping and floor gates. Integer math only.</summary>
    public static int GearScore(GearSet gear, ImmutableSortedDictionary<int, Item> items)
    {
        var score = 0;
        foreach (var slot in new[] { gear.Weapon, gear.Shield, gear.Armor })
        {
            if (slot is { } id && items.TryGetValue(id.Value, out var item))
            {
                score += item.Stats.Attack + item.Stats.Defense;
            }
        }

        return score;
    }
}
