using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// One line of the rival vendor's stock. <see cref="Price"/> is the fixed asking
/// price: statSum * 2 — cheap-ish on purpose, the baseline the player undercuts on
/// value or beats on quality (R16). Pure data, no RNG.
/// </summary>
public sealed record RivalCatalogEntry(
    string RecipeId,
    string Name,
    ItemSlot Slot,
    int Tier,
    ItemStats Stats)
{
    /// <summary>Fixed shelf price: (Attack + Defense) * 2.</summary>
    public int Price => (Stats.Attack + Stats.Defense) * 2;
}

/// <summary>
/// The rival vendor's generic flat-quality catalog (A3): one item per slot per tier,
/// tiers 1-2 ONLY, all <see cref="QualityGrade.Common"/>, never a maker's mark.
///
/// HARD CONSTRAINT (AE3/R9): stats must never exceed Attack 20 (weapon), Defense 16
/// (shield), Defense 18 (armor) — Common tier-2 equivalents. The best possible
/// all-rival loadout scores 20+16+18 = 54, far below MonsterTable.Gate(5) = 100, so
/// rival-only parties structurally cannot clear Floor 5. RivalCatalogTests asserts
/// these caps; raising any stat past them is a product-breaking change.
///
/// Roles: mystics carry at most weight 4 (ShoppingAi.MysticMaxWeight) and never use
/// shields — the Traveler's Sword (weight 4) and Padded Jerkin (weight 3) are the
/// catalog's mystic-wearable lines.
///
/// Entry order is declaration order and is part of the determinism contract:
/// RivalRestockSystem mints missing lines in this order, so reordering entries
/// changes item-id allocation for every seed.
/// </summary>
public static class RivalCatalog
{
    /// <summary>AE3 ceiling: no rival weapon may exceed this Attack.</summary>
    public const int MaxWeaponAttack = 20;

    /// <summary>AE3 ceiling: no rival shield may exceed this Defense.</summary>
    public const int MaxShieldDefense = 16;

    /// <summary>AE3 ceiling: no rival armor may exceed this Defense.</summary>
    public const int MaxArmorDefense = 18;

    /// <summary>The full stock list: 3 slots x tiers 1-2. Fixed data, no RNG anywhere.</summary>
    public static readonly ImmutableList<RivalCatalogEntry> Entries = ImmutableList.Create(
        // Weapons — tier-2 line sits exactly at the AE3 attack cap.
        new RivalCatalogEntry("rival-blade-1", "Traveler's Sword", ItemSlot.Weapon, Tier: 1,
            new ItemStats(Attack: 9, Defense: 0, Weight: 4)), // mystic-wearable (weight <= 4)
        new RivalCatalogEntry("rival-blade-2", "Soldier's Longsword", ItemSlot.Weapon, Tier: 2,
            new ItemStats(Attack: MaxWeaponAttack, Defense: 0, Weight: 5)),

        // Shields — vanguards only (role rule lives in ShoppingAi).
        new RivalCatalogEntry("rival-shield-1", "Pine Buckler", ItemSlot.Shield, Tier: 1,
            new ItemStats(Attack: 0, Defense: 6, Weight: 2)),
        new RivalCatalogEntry("rival-shield-2", "Banded Kite Shield", ItemSlot.Shield, Tier: 2,
            new ItemStats(Attack: 0, Defense: MaxShieldDefense, Weight: 6)),

        // Armor — tier-2 line sits exactly at the AE3 defense cap.
        new RivalCatalogEntry("rival-armor-1", "Padded Jerkin", ItemSlot.Armor, Tier: 1,
            new ItemStats(Attack: 0, Defense: 6, Weight: 3)), // mystic-wearable
        new RivalCatalogEntry("rival-armor-2", "Riveted Hauberk", ItemSlot.Armor, Tier: 2,
            new ItemStats(Attack: 0, Defense: MaxArmorDefense, Weight: 9)));

    /// <summary>
    /// Mint one rival item instance. Always Common, never marked (Mark = null — rival
    /// goods carry no maker's mark, R5), empty history. Callers allocate the id.
    /// </summary>
    public static Item Mint(ItemId id, RivalCatalogEntry entry) => new(
        id,
        entry.RecipeId,
        entry.Name,
        entry.Slot,
        QualityGrade.Common,
        entry.Stats,
        Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);
}
