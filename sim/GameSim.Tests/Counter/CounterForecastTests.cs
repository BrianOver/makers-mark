using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// U1 (§11.11, "tomorrow's asks, in front of tonight's shelf"): <see cref="CounterForecast"/> is
/// the pure projection <see cref="CounterHandlers"/>'s ApplyOpen builds when the counter actually
/// opens, and the same projection <c>CustomerVoice.WantLine</c> (godot/scripts/ui/CustomerVoice.cs)
/// reads for its spoken line. These pin that BOTH agree with it BY CONSTRUCTION — one function, two
/// callers — never merely by two independent implementations happening to agree today.
/// </summary>
public class CounterForecastTests
{
    private static Hero MakeHero(int id, string classId, int gold, GearSet gear, int moodPermille = 0, bool alive = true) => new(
        new HeroId(id), $"Ct{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        gear, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null)
    {
        MoodPermille = moodPermille,
    };

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    /// <summary>Same scoped kernel as <c>CounterQueueSystemTests.Kernel</c> — CounterQueueSystem +
    /// HeroShoppingSystem only, so the fixture's hand-picked ids/state never collide with the RNG
    /// draws and fresh-id allocation the rest of the Morning pipeline performs.</summary>
    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    private static GameState BaseState(ImmutableSortedDictionary<int, Hero> heroes, params Item[] items) =>
        GameFactory.NewGame(seed: 6601) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    [Fact]
    public void Queue_MatchesApplyOpensOwnOrdering_AcrossRelationshipBands()
    {
        // Band order must beat HeroId order: hero 3 (higher id, but bumped to Regular via mood)
        // must be projected AND actually seated ahead of hero 1 (lower id, Stranger).
        var strangerLowId = MakeHero(1, "vanguard", gold: 50, GearSet.Empty);
        var regularHighId = MakeHero(3, "vanguard", gold: 60, GearSet.Empty, moodPermille: RelationshipBands.RegularMinMood);
        var state = BaseState(Roster(strangerLowId, regularHighId));

        var projected = CounterForecast.Queue(state);
        Assert.Equal(new HeroId(3), projected[0].Hero);
        Assert.Equal(new HeroId(1), projected[1].Hero);

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));

        Assert.Empty(result.Rejected);
        Assert.Equal(projected.Select(a => a.Hero).ToList(), result.NewState.Counter!.Queue);
        var approached = Assert.Single(result.Events.OfType<CustomerApproached>());
        Assert.Equal(projected[0].Hero, approached.Hero);
    }

    [Fact]
    public void Queue_IsEmpty_WhenNoHeroIsAlive()
    {
        var dead = MakeHero(1, "vanguard", gold: 10, GearSet.Empty, alive: false);
        var state = BaseState(Roster(dead));

        Assert.Empty(CounterForecast.Queue(state));

        // The projection's emptiness must match what ApplyOpen actually does with it: a valid,
        // Active-null open session (PKD6, "the player is only arranging"), never a rejection.
        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));
        Assert.Empty(result.Rejected);
        Assert.NotNull(result.NewState.Counter);
        Assert.Null(result.NewState.Counter!.Active);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void Queue_CarriesEachHerosOwnGoldAndFirstMissingSlot()
    {
        var hero = MakeHero(1, "vanguard", gold: 123, GearSet.Empty);
        var state = BaseState(Roster(hero));

        var entry = Assert.Single(CounterForecast.Queue(state));
        Assert.Equal(new HeroId(1), entry.Hero);
        Assert.Equal(123, entry.Gold);
        Assert.Equal(ItemSlot.Weapon, entry.WantSlot); // GearSet.Empty -> Weapon is first in fixed order
    }

    [Fact]
    public void Wants_FullLoadoutHero_NamesTheLargestGenuineShelfUpgrade_NeverAnItemTheSimWouldRefuse()
    {
        var weakWeapon = MakeItem(11, ItemSlot.Weapon, attack: 0, defense: 0, weight: 1, name: "Rusty Knife");
        var weakShield = MakeItem(12, ItemSlot.Shield, attack: 0, defense: 0, weight: 1, name: "Cracked Buckler");
        var weakArmor = MakeItem(13, ItemSlot.Armor, attack: 0, defense: 0, weight: 1, name: "Ragged Coat");
        var upgrade = MakeItem(14, ItemSlot.Weapon, attack: 8, defense: 0, weight: 2, name: "Fine Blade");

        var hero = MakeHero(1, "vanguard", gold: 200, new GearSet(weakWeapon.Id, weakShield.Id, weakArmor.Id));
        var state = BaseState(Roster(hero), weakWeapon, weakShield, weakArmor, upgrade) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(upgrade.Id, 20)),
            },
        };

        // Confirm the sim itself calls this a Buy before trusting the forecast agrees with it.
        var verdict = ShoppingAi.EvaluateItem(hero, upgrade, 20, state.Items);
        Assert.Equal(ShoppingVerdictKind.Buy, verdict.Kind);

        Assert.Equal(ItemSlot.Weapon, CounterForecast.Wants(hero, state));
    }

    [Fact]
    public void Wants_FullLoadoutHero_NoShelfUpgrade_ReturnsNull_NeverInventsAWant()
    {
        var bestWeapon = MakeItem(1, ItemSlot.Weapon, attack: 9, defense: 0, weight: 1, name: "Masterwork Blade");
        var worseWeapon = MakeItem(2, ItemSlot.Weapon, attack: 1, defense: 0, weight: 1, name: "Dull Blade");
        var hero = MakeHero(1, "vanguard", gold: 60, new GearSet(bestWeapon.Id, new ItemId(998), new ItemId(999)));
        var state = BaseState(Roster(hero), bestWeapon, worseWeapon) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(worseWeapon.Id, 5)),
            },
        };

        var verdict = ShoppingAi.EvaluateItem(hero, worseWeapon, 5, state.Items);
        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind); // strictly worse — no gear-score gain

        Assert.Null(CounterForecast.Wants(hero, state));
    }
}
