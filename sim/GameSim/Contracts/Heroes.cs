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
    string ClassId,
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

    /// <summary>
    /// The hero's mood toward the player's shop, per-mille, signed (0 = neutral; the pin-bonus /
    /// fleece-memory target — PKD6). A counter "pin" (countering near true willingness) nudges it up;
    /// an over-ceiling fleece nudges it down; PA4 willingness math and the gossip surface read it.
    /// STRICTLY influence, never orders (PKD7): mood NEVER touches party formation, floor choice, or
    /// expedition resolution. Non-positional init member (the <see cref="Pack"/> pattern) — old saves
    /// and existing constructors default to 0.
    /// </summary>
    public int MoodPermille { get; init; } = 0;

    /// <summary>
    /// Career experience points (Phase B, B0). Accrues at the Evening reveal and crosses the
    /// <see cref="GameSim.Heroes.HeroRank"/> ladder's thresholds, which (Phase C, U-C6) now ALSO
    /// derives the real <see cref="Level"/> (<c>CombatMath</c> reads Level into Attack/Defense), so
    /// XP growth mechanically strengthens the hero — the deferred flip from KTD-B2, landed. Non-
    /// positional init member (the <see cref="MoodPermille"/> pattern) — old saves and existing
    /// constructors default to 0.
    /// </summary>
    public int Xp { get; init; } = 0;

    /// <summary>
    /// The forward ladder (owner ruling 2026-08-10, plan 2026-08-10-003 L0): how many dungeons
    /// this hero has GRADUATED — beaten the bottom floor of — starting at 0 (the Mine tier).
    /// MONOTONIC BY CONTRACT: it only ever increments, on a bottom-floor clear by a surviving
    /// party member whose rank equals the venue's, and nothing may ever decrement it. That
    /// monotonicity is the whole §11.8 fix — routing keyed on a signal that cannot regress cannot
    /// oscillate, unlike the power high-water latch it replaces. Distinct from
    /// <see cref="GameSim.Heroes.HeroRank"/> (the XP/career ladder): a hero can be a decorated
    /// veteran and still rank 0 here if her parties never felled a bottom-floor boss. Non-
    /// positional init member (the <see cref="MoodPermille"/> pattern) — old saves and existing
    /// constructors default to 0, which is exactly correct: every pre-ladder hero starts at the
    /// Mine tier. Nothing writes this field until L1.
    /// </summary>
    public int LadderRank { get; init; } = 0;

    /// <summary>
    /// Simple additive gear score used by shopping (<see cref="GameSim.Heroes.ShoppingAi"/>) —
    /// NOT floor gates. This doc previously claimed "and floor gates" and was wrong: the floor-gate
    /// number is <c>CombatMath.EffectivePower</c>, a separate formula this method has no part in
    /// (P2-HONEST-11 correction, owner ruling 2026-09-03).
    ///
    /// <para>Weapon/Shield/Armor only: <see cref="GearSet.Trinket"/> is deliberately excluded.
    /// P2-HONEST-11 (P2-OQ7 resolved honesty over teeth) declared the trinket the modifier-only
    /// slot — its Attack/Defense never reach combat (<c>CombatMath.HeroAttack</c>/<c>HeroDefense</c>
    /// read Weapon and Shield+Armor respectively, never Trinket), so a hero's willingness to buy one
    /// must not be driven by stats that do nothing underground. Summing Trinket here used to make
    /// heroes pay real gold for that illusion; a trinket's only real value is its craft modifier,
    /// which an integer stat sum cannot see. Integer math only.</para>
    /// </summary>
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
