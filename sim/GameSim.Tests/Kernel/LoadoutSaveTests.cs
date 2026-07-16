using System.Collections.Immutable;
using System.Text.Json.Nodes;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// P2 snapshot-save coverage (KTD4): Pack, ConsumableEffect, and Trinket survive the
/// round trip byte-identically, and saves written BEFORE these fields existed load
/// with safe defaults (empty pack, null trinket, null effect).
/// </summary>
public class LoadoutSaveTests
{
    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Fine,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    private static Item Charm(int id) => new(
        new ItemId(id), "lucky-charm", "Lucky Charm", ItemSlot.Trinket, QualityGrade.Common,
        new ItemStats(2, 3, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    [Fact]
    public void RoundTrip_WithPackEffectAndTrinket_ByteIdentical()
    {
        var salve = Salve(1);
        var charm = Charm(2);
        var state = GameFactory.NewGame(seed: 5);
        var hero = new Hero(
            new HeroId(1), "Torvald", HeroRole.Vanguard, Level: 2, MaxHp: 30, Gold: 40,
            new GearSet(null, null, null, charm.Id), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 1, DiedOnDay: null)
        {
            Pack = ImmutableList.Create(salve.Id),
        };
        state = state with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, salve).Add(2, charm),
            NextItemId = 3,
            NextHeroId = 2,
        };

        var json = SaveCodec.Serialize(state);
        var loaded = SaveCodec.Deserialize(json);

        Assert.Equal(json, SaveCodec.Serialize(loaded)); // byte-identical (KTD4)

        var back = loaded.Heroes[1];
        Assert.Equal(ImmutableList.Create(salve.Id), back.Pack);
        Assert.Equal(charm.Id, back.Gear.Trinket);
        Assert.Equal(new ConsumableEffect(ConsumableKind.Heal, 6), loaded.Items[1].Effect);
        Assert.Null(loaded.Items[2].Effect);
    }

    [Fact]
    public void PreP2Save_WithoutNewFields_LoadsWithSafeDefaults()
    {
        // Simulate a save written before P2 by deleting the new properties from the
        // JSON: Pack defaults empty, Trinket and Effect default null (trailing
        // optional / init-default contract).
        var state = GameFactory.NewGame(seed: 7) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, new Hero(
                new HeroId(1), "Torvald", HeroRole.Vanguard, Level: 1, MaxHp: 30, Gold: 40,
                GearSet.Empty, ImmutableList<ItemMemory>.Empty,
                Alive: true, DeepestFloorReached: 0, DiedOnDay: null)),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, Charm(1) with { Slot = ItemSlot.Weapon }),
        };

        var node = JsonNode.Parse(SaveCodec.Serialize(state))!;
        foreach (var heroNode in node["Heroes"]!.AsObject().Select(kv => kv.Value!.AsObject()))
        {
            heroNode.Remove("Pack");
            heroNode["Gear"]!.AsObject().Remove("Trinket");
        }

        foreach (var itemNode in node["Items"]!.AsObject().Select(kv => kv.Value!.AsObject()))
        {
            itemNode.Remove("Effect");
        }

        var loaded = SaveCodec.Deserialize(node.ToJsonString());

        var hero = loaded.Heroes[1];
        Assert.NotNull(hero.Pack);
        Assert.Empty(hero.Pack);
        Assert.Null(hero.Gear.Trinket);
        Assert.Null(loaded.Items[1].Effect);
    }
}
