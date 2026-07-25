using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Phase B (B1b, R-B2): the advisor's hero-shopping forecast. The forecast-exactness contract:
/// "as the shelf stands" must equal what <see cref="HeroShoppingSystem"/> actually does the next
/// time it runs against that same state — proven here by calling the forecast, THEN actually
/// running the real system, and checking they agree (not by construction alone, even though
/// <see cref="HeroForecast"/> shares <see cref="HeroShoppingSystem.EvaluateGearCandidates"/>
/// internally — this test would catch a future edit that broke that sharing).
/// </summary>
public class HeroForecastTests
{
    private static Hero MakeHero(int id, string classId, int gold) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name) => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState BaseState(Hero hero, params Item[] items) =>
        GameFactory.NewGame(seed: 5) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    [Fact]
    public void Forecast_PredictsTheExactItemTheRealSystemThenBuys()
    {
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 8, defense: 0, weight: 3, name: "Iron Sword");
        var hero = MakeHero(1, "vanguard", gold: 100);
        var state = BaseState(hero, sword) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 20)) },
        };

        var forecast = HeroForecast.ForShelfAsItStands(state, hero.Id);
        Assert.True(forecast.WouldBuy);
        Assert.Equal("Iron Sword", forecast.ItemName);

        // Now actually run the real system against the SAME (unmutated) state and confirm it
        // bought exactly what the forecast said it would.
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), new TestSink());
        Assert.Equal(sword.Id, after.Heroes[1].Gear.Weapon);
    }

    [Fact]
    public void Forecast_PredictsNoBuy_WhenTheRealSystemAlsoPasses()
    {
        var expensiveSword = MakeItem(1, ItemSlot.Weapon, attack: 8, defense: 0, weight: 3, name: "Iron Sword");
        var hero = MakeHero(1, "vanguard", gold: 5); // can't afford anything
        var state = BaseState(hero, expensiveSword) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(expensiveSword.Id, 50)) },
        };

        var forecast = HeroForecast.ForShelfAsItStands(state, hero.Id);
        Assert.False(forecast.WouldBuy);

        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), new TestSink());
        Assert.Null(after.Heroes[1].Gear.Weapon);
    }

    [Fact]
    public void Forecast_MutatesNothing_InTheRealState()
    {
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 8, defense: 0, weight: 3, name: "Iron Sword");
        var hero = MakeHero(1, "vanguard", gold: 100);
        var state = BaseState(hero, sword) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 20)) },
        };
        var before = SaveCodec.Serialize(state);

        HeroForecast.ForShelfAsItStands(state, hero.Id);

        Assert.Equal(before, SaveCodec.Serialize(state)); // byte-identical — nothing changed, no event stamped
    }

    [Fact]
    public void Forecast_UnknownOrDeadHero_ReportsNotPresent()
    {
        var hero = MakeHero(1, "vanguard", gold: 100) with { Alive = false, DiedOnDay = 3 };
        var state = BaseState(hero);

        var deadForecast = HeroForecast.ForShelfAsItStands(state, hero.Id);
        Assert.False(deadForecast.WouldBuy);

        var unknownForecast = HeroForecast.ForShelfAsItStands(state, new HeroId(999));
        Assert.False(unknownForecast.WouldBuy);
    }

    private sealed class TestSink : IEventSink
    {
        public void Emit(GameEvent gameEvent)
        {
        }
    }
}
