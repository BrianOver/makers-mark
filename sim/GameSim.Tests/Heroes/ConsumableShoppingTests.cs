using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// The P2 consumable shopping pass: after the gear pass, a hero with an empty Pack
/// buys the single cheapest affordable Heal item — player shelf wins price ties —
/// at most once per Morning, with legible pass reasons (R8/AE4).
/// </summary>
public class ConsumableShoppingTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold, params ItemId[] pack) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    {
        Pack = pack.ToImmutableList(),
    };

    private static Item Salve(int id, bool marked = true, string name = "Field Salve") => new(
        new ItemId(id), "field-salve", name, ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), marked ? new MakersMark("You", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    private static Item Sword(int id) => new(
        new ItemId(id), "shortsword", "Iron Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(8, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState BaseState(ImmutableSortedDictionary<int, Hero> heroes, params Item[] items) =>
        GameFactory.NewGame(seed: 42) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var system = new HeroShoppingSystem();
        var sink = new TestSink();
        var after = system.Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    [Fact]
    public void EmptyPackHero_BuysCheapestSalve_IntoPack_WithLegiblePassOnTheLoser()
    {
        var pricey = Salve(1, name: "Pricey Salve");
        var cheap = Salve(2, name: "Cheap Salve");
        var hero = MakeHero(1, gold: 100);
        var state = BaseState(Roster(hero), pricey, cheap) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(pricey.Id, 10), new ShelfEntry(cheap.Id, 6)),
            },
        };

        var (after, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(cheap.Id, sold.Item);
        Assert.True(sold.FromPlayerShop);
        Assert.Equal(6, sold.Price);
        Assert.Equal(ImmutableList.Create(cheap.Id), after.Heroes[1].Pack);
        Assert.Null(after.Heroes[1].Gear.Weapon); // pack, not gear
        Assert.Equal(94, after.Heroes[1].Gold);
        Assert.Equal(6, after.Player.Gold);
        Assert.Single(after.Player.Shelf); // the pricey one stays

        var pass = Assert.Single(events.OfType<HeroPassedOnItem>());
        Assert.Equal(pricey.Id, pass.Item);
        Assert.False(string.IsNullOrWhiteSpace(pass.Reason));
    }

    [Fact]
    public void OneConsumablePurchase_PerHeroPerMorning()
    {
        // Rich hero, two affordable salves: exactly one is bought.
        var salveA = Salve(1);
        var salveB = Salve(2);
        var hero = MakeHero(1, gold: 500);
        var state = BaseState(Roster(hero), salveA, salveB) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(salveA.Id, 5), new ShelfEntry(salveB.Id, 5)),
            },
        };

        var (after, events) = Run(state);

        Assert.Single(events.OfType<ItemSold>());
        Assert.Single(after.Heroes[1].Pack);
        Assert.Single(after.Player.Shelf);
    }

    [Fact]
    public void StockedPack_SkipsThePass_NoPurchase_NoEvents()
    {
        var carried = Salve(1);
        var shelved = Salve(2);
        var hero = MakeHero(1, gold: 100, carried.Id);
        var state = BaseState(Roster(hero), carried, shelved) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(shelved.Id, 5)) },
        };

        var (after, events) = Run(state);

        Assert.Empty(events);
        Assert.Equal(ImmutableList.Create(carried.Id), after.Heroes[1].Pack);
        Assert.Single(after.Player.Shelf);
        Assert.Equal(100, after.Heroes[1].Gold);
    }

    [Fact]
    public void CannotAfford_PassesWithReason_NamingBothAmounts()
    {
        var salve = Salve(1);
        var hero = MakeHero(1, gold: 3);
        var state = BaseState(Roster(hero), salve) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(salve.Id, 10)) },
        };

        var (after, events) = Run(state);

        Assert.Empty(events.OfType<ItemSold>());
        var pass = Assert.Single(events.OfType<HeroPassedOnItem>());
        Assert.Equal(salve.Id, pass.Item);
        Assert.Contains("10g", pass.Reason);
        Assert.Contains("3g", pass.Reason);
        Assert.Empty(after.Heroes[1].Pack);
    }

    [Fact]
    public void PriceTie_PrefersThePlayerShelf()
    {
        // The rival salve carries the LOWER ItemId, so a bare lower-id tie-break would
        // pick it — the shelf preference must win first.
        var rivalSalve = Salve(1, marked: false, name: "Rival Salve");
        var playerSalve = Salve(2, name: "Player Salve");
        var hero = MakeHero(1, gold: 100);
        var state = BaseState(Roster(hero), rivalSalve, playerSalve) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(playerSalve.Id, 8)) },
            RivalShelf = ImmutableList.Create(new ShelfEntry(rivalSalve.Id, 8)),
        };

        var (after, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(playerSalve.Id, sold.Item);
        Assert.True(sold.FromPlayerShop);
        Assert.Equal(ImmutableList.Create(playerSalve.Id), after.Heroes[1].Pack);
        Assert.Equal(8, after.Player.Gold);
    }

    [Fact]
    public void RivalShelfConsumablePasses_EmitNoEvents()
    {
        // Same cap as the gear pass: only player-shelf passes get events.
        var rivalSalve = Salve(1, marked: false);
        var broke = MakeHero(1, gold: 0);
        var state = BaseState(Roster(broke), rivalSalve) with
        {
            RivalShelf = ImmutableList.Create(new ShelfEntry(rivalSalve.Id, 10)),
        };

        var (_, events) = Run(state);

        Assert.Empty(events);
    }

    [Fact]
    public void GearAndSalve_BothBought_InOneMorning_SalveSeesPostGearPurse()
    {
        // The consumable pass runs after the gear pass: one hero buys a sword AND a
        // salve, and the salve spends what the sword left behind.
        var sword = Sword(1);
        var salve = Salve(2);
        var hero = MakeHero(1, gold: 30);
        var state = BaseState(Roster(hero), sword, salve) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 20), new ShelfEntry(salve.Id, 8)),
            },
        };

        var (after, events) = Run(state);

        Assert.Equal(2, events.OfType<ItemSold>().Count());
        Assert.Equal(sword.Id, after.Heroes[1].Gear.Weapon);
        Assert.Equal(ImmutableList.Create(salve.Id), after.Heroes[1].Pack);
        Assert.Equal(2, after.Heroes[1].Gold); // 30 - 20 - 8
        Assert.Empty(after.Player.Shelf);
        // The salve never entered the gear pass: no "no gear-score improvement" noise.
        Assert.Empty(events.OfType<HeroPassedOnItem>());
    }

    [Fact]
    public void SoldConsumable_CanNeverBeRestocked()
    {
        // P2 integrity rule (ShopHandlers): once a consumable sells, it lives in a
        // pack until drunk and never returns to the shelf — re-stocking it would
        // duplicate the physical item.
        var salve = Salve(1);
        var hero = MakeHero(1, gold: 100);
        var start = BaseState(Roster(hero), salve) with
        {
            Player = PlayerState.NewGame(0),
        };
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new HeroShoppingSystem()),
            ImmutableList.Create<IActionHandler>(new ShopHandlers()));

        // Morning 1: stock the salve; the hero buys it into the pack.
        var morning = kernel.Tick(start, ImmutableList.Create<PlayerAction>(new StockAction(salve.Id, 5)));
        Assert.Empty(morning.Rejected);
        Assert.Single(morning.Events.OfType<ItemSold>());
        Assert.Equal(ImmutableList.Create(salve.Id), morning.NewState.Heroes[1].Pack);

        // Re-stocking the sold salve is refused with a typed reason.
        var restock = kernel.Tick(morning.NewState, ImmutableList.Create<PlayerAction>(new StockAction(salve.Id, 5)));
        var rejection = Assert.Single(restock.Rejected);
        Assert.Contains("already sold", rejection.Reason);
        Assert.Empty(restock.NewState.Player.Shelf);
    }
}
