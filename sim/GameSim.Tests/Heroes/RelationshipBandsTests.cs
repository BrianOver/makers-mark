using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// U7/U8 (plan 2026-07-24-003, "Regulars"): relationship bands are PURELY DERIVED from mood +
/// player-shop purchases (no new sim state/event → golden untouched), and a higher band serves
/// ahead at the counter (U8, counter-session order only — never a muster input, PKD7).
/// </summary>
public class RelationshipBandsTests
{
    private static Hero MakeHero(int id, int mood = 0) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: 100,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    {
        MoodPermille = mood,
    };

    private static GameState StateWith(ImmutableList<GameEvent> log, params Hero[] heroes) =>
        GameFactory.NewGame(seed: 801) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            EventLog = log,
        };

    private static ItemSold Bought(int heroId) =>
        new(new ItemId(1), new HeroId(heroId), Price: 10, FromPlayerShop: true);

    [Fact]
    public void FreshHero_NoMoodNoPurchases_IsStranger()
    {
        var state = StateWith(ImmutableList<GameEvent>.Empty, MakeHero(1));
        Assert.Equal(RelationshipBand.Stranger, RelationshipBands.For(new HeroId(1), state));
    }

    [Fact]
    public void OnePurchase_OrWarmMood_IsRegular()
    {
        var byPurchase = StateWith(ImmutableList.Create<GameEvent>(Bought(1)), MakeHero(1));
        Assert.Equal(RelationshipBand.Regular, RelationshipBands.For(new HeroId(1), byPurchase));

        var byMood = StateWith(ImmutableList<GameEvent>.Empty, MakeHero(2, mood: 100));
        Assert.Equal(RelationshipBand.Regular, RelationshipBands.For(new HeroId(2), byMood));
    }

    [Fact]
    public void ThreePurchases_OrHighMood_IsPatron()
    {
        var byPurchase = StateWith(
            ImmutableList.Create<GameEvent>(Bought(1), Bought(1), Bought(1)), MakeHero(1));
        Assert.Equal(RelationshipBand.Patron, RelationshipBands.For(new HeroId(1), byPurchase));
    }

    [Fact]
    public void ManyPurchasesAndDevotedMood_IsSworn()
    {
        var log = ImmutableList.CreateRange<GameEvent>(Enumerable.Range(0, 5).Select(_ => Bought(1)));
        var state = StateWith(log, MakeHero(1, mood: 320));
        Assert.Equal(RelationshipBand.Sworn, RelationshipBands.For(new HeroId(1), state));
    }

    [Fact]
    public void OpenCounter_HigherBandServedFirst_DespiteHigherHeroId()
    {
        // Hero 1 is a Stranger; hero 2 is a Patron (warm mood). Band order must put hero 2 at the
        // head even though hero 1 has the lower id (the old tie-break).
        var stranger = MakeHero(1, mood: 0);
        var patron = MakeHero(2, mood: 250);
        var state = StateWith(ImmutableList<GameEvent>.Empty, stranger, patron);

        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem()),
            ImmutableList.Create<IActionHandler>(new CounterHandlers()));
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));

        Assert.Equal(new[] { new HeroId(2), new HeroId(1) }, result.NewState.Counter!.Queue);
        Assert.Equal(new HeroId(2), result.NewState.Counter.Active);
    }
}
