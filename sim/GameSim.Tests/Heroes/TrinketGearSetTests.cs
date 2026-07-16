using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Tests.Heroes;

/// <summary>
/// The P2 Trinket gear slot (contract only — content arrives with later add-ons):
/// Slot/WithSlot address it and GearScore counts its Attack + Defense.
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
    public void GearScore_IncludesTrinketAttackAndDefense()
    {
        var charm = Charm(1, attack: 2, defense: 3);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, charm);

        Assert.Equal(0, Hero.GearScore(GearSet.Empty, items));
        Assert.Equal(5, Hero.GearScore(GearSet.Empty.WithSlot(ItemSlot.Trinket, charm.Id), items));
    }
}
