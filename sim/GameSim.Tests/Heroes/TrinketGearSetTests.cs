using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Tests.Heroes;

/// <summary>
/// The P2 Trinket gear slot: Slot/WithSlot address it like any other slot, but
/// <see cref="Hero.GearScore"/> does NOT count its Attack + Defense (P2-HONEST-11, owner ruling
/// 2026-09-03) — the trinket is the modifier-only slot; its stats never reach combat, so the
/// shopping score must not pay for them either.
/// </summary>
public class TrinketGearSetTests
{
    private static Item Charm(int id, int attack, int defense) => new(
        new ItemId(id), "lucky-charm", "Lucky Charm", ItemSlot.Trinket, QualityGrade.Common,
        new ItemStats(attack, defense, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    [Fact]
    public void SlotAndWithSlot_HandleTrinket()
    {
        var id = new ItemId(7);
        var gear = GearSet.Empty.WithSlot(ItemSlot.Trinket, id);

        Assert.Equal(id, gear.Trinket);
        Assert.Equal(id, gear.Slot(ItemSlot.Trinket));
        Assert.Null(gear.Weapon);
        Assert.Null(gear.Shield);
        Assert.Null(gear.Armor);
    }

    [Fact]
    public void WithSlot_Consumable_IsANoOp_ConsumablesAreNotWorn()
    {
        var gear = GearSet.Empty.WithSlot(ItemSlot.Consumable, new ItemId(7));

        Assert.Equal(GearSet.Empty, gear);
        Assert.Null(gear.Slot(ItemSlot.Consumable));
    }

    [Fact]
    public void GearScore_ExcludesTrinketAttackAndDefense()
    {
        // P2-HONEST-11 (owner ruling 2026-09-03): a trinket's Attack/Defense never reach
        // CombatMath, so GearScore — the number that drives what heroes are willing to buy —
        // must stop paying for them too. Equipping a stat-carrying trinket must not move the
        // score at all, empty slot or not.
        var charm = Charm(1, attack: 2, defense: 3);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, charm);

        Assert.Equal(0, Hero.GearScore(GearSet.Empty, items));
        Assert.Equal(0, Hero.GearScore(GearSet.Empty.WithSlot(ItemSlot.Trinket, charm.Id), items));
    }
}
