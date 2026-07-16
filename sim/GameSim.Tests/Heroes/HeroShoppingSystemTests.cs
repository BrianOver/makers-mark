using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Covers R7 (gear-score-driven shopping), R8 (legible passes), and the Morning
/// half of R16 (heroes comparison-shop player vs rival on quality and price).
/// </summary>
public class HeroShoppingSystemTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, string classId, int gold, bool alive = true, GearSet? gear = null) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

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
    public void Hero_BuysBetterOfTwoAffordableItems_ByGearScorePerGold()
    {
        // Item 1: +6 score for 30g (5g/point). Item 2: +8 score for 20g (2.5g/point) — better value.
        var lowValue = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Bronze Sword");
        var highValue = MakeItem(2, ItemSlot.Weapon, attack: 8, defense: 0, weight: 3, name: "Iron Sword");
        var hero = MakeHero(1, "vanguard", gold: 100);
        var state = BaseState(Roster(hero), lowValue, highValue) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(lowValue.Id, 30), new ShelfEntry(highValue.Id, 20)),
            },
        };

        var (after, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(highValue.Id, sold.Item);
        Assert.Equal(hero.Id, sold.Buyer);
        Assert.Equal(80, after.Heroes[1].Gold);
        Assert.Equal(highValue.Id, after.Heroes[1].Gear.Weapon);

        // The losing player-shelf item still gets a legible pass (R8).
        var pass = Assert.Single(events.OfType<HeroPassedOnItem>());
        Assert.Equal(lowValue.Id, pass.Item);
        Assert.False(string.IsNullOrWhiteSpace(pass.Reason));
    }

    [Fact]
    public void EqualValueItems_TieBreaksToLowerItemId()
    {
        // Identical score and price; the higher id is shelved first so a first-seen
        // bug would buy it — the tie must go to the lower ItemId.
        var higherId = MakeItem(5, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Twin Blade B");
        var lowerId = MakeItem(3, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Twin Blade A");
        var hero = MakeHero(1, "vanguard", gold: 100);
        var state = BaseState(Roster(hero), higherId, lowerId) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(higherId.Id, 30), new ShelfEntry(lowerId.Id, 30)),
            },
        };

        var (_, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(lowerId.Id, sold.Item);
    }

    [Fact]
    public void OverBudgetHero_Passes_WithAffordabilityReason()
    {
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");
        var hero = MakeHero(1, "vanguard", gold: 30);
        var state = BaseState(Roster(hero), sword) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 45)) },
        };

        var (after, events) = Run(state);

        Assert.Empty(events.OfType<ItemSold>());
        var pass = Assert.Single(events.OfType<HeroPassedOnItem>());
        Assert.Equal(sword.Id, pass.Item);
        Assert.Contains("45g", pass.Reason);
        Assert.Contains("30g", pass.Reason);
        Assert.Equal(30, after.Heroes[1].Gold);
        Assert.Single(after.Player.Shelf);
    }

    [Fact]
    public void PlayerShelfPurchase_CreditsPlayerGold_AndRemovesShelfEntry()
    {
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");
        var hero = MakeHero(1, "vanguard", gold: 50);
        var state = BaseState(Roster(hero), sword) with
        {
            Player = PlayerState.NewGame(100) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 25)) },
        };

        var (after, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.True(sold.FromPlayerShop);
        Assert.Equal(25, sold.Price);
        Assert.Equal(125, after.Player.Gold);
        Assert.Empty(after.Player.Shelf);
        Assert.Equal(25, after.Heroes[1].Gold);
        Assert.Equal(sword.Id, after.Heroes[1].Gear.Weapon);
    }

    [Fact]
    public void RivalPurchase_DoesNotCreditPlayerGold_AndBeatsWorsePlayerValue()
    {
        // Cross-shop comparison: rival item is better gear-score-per-gold, so the hero
        // buys rival; the player item gets a pass event, player gold is untouched.
        var playerItem = MakeItem(1, ItemSlot.Weapon, attack: 4, defense: 0, weight: 3, name: "Player Sword");
        var rivalItem = MakeItem(2, ItemSlot.Weapon, attack: 8, defense: 0, weight: 3, name: "Rival Sword");
        var hero = MakeHero(1, "vanguard", gold: 60);
        var state = BaseState(Roster(hero), playerItem, rivalItem) with
        {
            Player = PlayerState.NewGame(100) with { Shelf = ImmutableList.Create(new ShelfEntry(playerItem.Id, 20)) },
            RivalShelf = ImmutableList.Create(new ShelfEntry(rivalItem.Id, 20)),
        };

        var (after, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(rivalItem.Id, sold.Item);
        Assert.False(sold.FromPlayerShop);
        Assert.Equal(100, after.Player.Gold);
        Assert.Empty(after.RivalShelf);
        Assert.Single(after.Player.Shelf);
        Assert.Equal(40, after.Heroes[1].Gold);

        var pass = Assert.Single(events.OfType<HeroPassedOnItem>());
        Assert.Equal(playerItem.Id, pass.Item);
    }

    [Fact]
    public void RivalShelfPasses_EmitNoEvents()
    {
        // Cap: pass events are emitted only for player-shelf items (avoids event spam).
        var shield = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 5, weight: 3, name: "Rival Shield");
        var striker = MakeHero(1, "striker", gold: 100);
        var state = BaseState(Roster(striker), shield) with
        {
            RivalShelf = ImmutableList.Create(new ShelfEntry(shield.Id, 10)),
        };

        var (_, events) = Run(state);

        Assert.Empty(events);
    }

    [Fact]
    public void DeadHeroes_NeverShop()
    {
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 9, defense: 0, weight: 3, name: "Iron Sword");
        var dead = MakeHero(1, "vanguard", gold: 100, alive: false);
        var state = BaseState(Roster(dead), sword) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 10)) },
        };

        var (after, events) = Run(state);

        Assert.Empty(events);
        Assert.Single(after.Player.Shelf);
        Assert.Equal(100, after.Heroes[1].Gold);
        Assert.Null(after.Heroes[1].Gear.Weapon);
    }

    [Fact]
    public void HeroesShopInHeroIdOrder_FirstIdWinsContestedItem()
    {
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 9, defense: 0, weight: 3, name: "The Only Sword");
        var first = MakeHero(1, "vanguard", gold: 100);
        var second = MakeHero(2, "vanguard", gold: 100);
        var state = BaseState(Roster(second, first), sword) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 10)) },
        };

        var (after, events) = Run(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(new HeroId(1), sold.Buyer);
        Assert.Equal(sword.Id, after.Heroes[1].Gear.Weapon);
        Assert.Null(after.Heroes[2].Gear.Weapon);
    }

    [Fact]
    public void SameState_TwoRuns_IdenticalSerializedStateAndEventSequence()
    {
        var items = new[]
        {
            MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword"),
            MakeItem(2, ItemSlot.Shield, attack: 0, defense: 4, weight: 3, name: "Oak Shield"),
            MakeItem(3, ItemSlot.Armor, attack: 0, defense: 5, weight: 4, name: "Chain Mail"),
            MakeItem(4, ItemSlot.Weapon, attack: 3, defense: 0, weight: 1, name: "Willow Wand"),
        };
        var start = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 7)) with
        {
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            Player = PlayerState.NewGame(100) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 20), new ShelfEntry(new ItemId(2), 15)),
            },
            RivalShelf = ImmutableList.Create(new ShelfEntry(new ItemId(3), 25), new ShelfEntry(new ItemId(4), 10)),
        };
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new HeroShoppingSystem()),
            ImmutableList<IActionHandler>.Empty);

        var runA = kernel.Tick(start, ImmutableList<PlayerAction>.Empty);
        var runB = kernel.Tick(start, ImmutableList<PlayerAction>.Empty);

        Assert.Equal(SaveCodec.Serialize(runA.NewState), SaveCodec.Serialize(runB.NewState));
        Assert.Equal(runA.Events, runB.Events);
        Assert.NotEmpty(runA.Events.OfType<ItemSold>()); // the run actually shopped
    }
}
