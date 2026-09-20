using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

/// <summary>
/// P2-PEOPLE-28 ("hold it for Torvald"): <see cref="EarmarkQuery"/> reads the log to answer "who was
/// this piece held for" even after the sale removes the shelf entry that would have said so directly
/// — state-in/state-out, no tick, no RNG. Every case builds the GameState directly, mirroring
/// <see cref="RivalSaleQueryTests"/>'s own idiom.
/// </summary>
public class EarmarkQueryTests
{
    private static Item Weapon(int id, string name = "Shortsword") => new(
        new ItemId(id), "recipe", name, ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(Attack: 5, Defense: 0, Weight: 1),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState BaseState(int day, params GameEvent[] log) =>
        GameFactory.NewGame(seed: 555) with
        {
            Day = day,
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, Weapon(1)),
            EventLog = log.ToImmutableList(),
        };

    // ---- ForDay ("<Name> came for the <item> you held for them") ----

    [Fact]
    public void SaleToTheHeldHero_YieldsOneMatch()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var sale = new ItemSold(new ItemId(1), new HeroId(9), Price: 40, FromPlayerShop: true)
        {
            Id = new EventId(2),
            Day = 5,
        };
        var state = BaseState(day: 5, earmark, sale);

        var match = Assert.Single(EarmarkQuery.ForDay(state, day: 5));
        Assert.Equal(new HeroId(9), match.Hero);
        Assert.Equal(new ItemId(1), match.Item);
    }

    [Fact]
    public void SaleWithNoPriorEarmark_YieldsNothing()
    {
        var sale = new ItemSold(new ItemId(1), new HeroId(9), Price: 40, FromPlayerShop: true)
        {
            Id = new EventId(1),
            Day = 5,
        };
        var state = BaseState(day: 5, sale);

        Assert.Empty(EarmarkQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void SaleAfterTheHoldWasCleared_YieldsNothing()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 2 };
        var cleared = new ShelfEarmarked(new ItemId(1), null) { Id = new EventId(2), Day = 3 };
        var sale = new ItemSold(new ItemId(1), new HeroId(9), Price: 40, FromPlayerShop: true)
        {
            Id = new EventId(3),
            Day = 5,
        };
        var state = BaseState(day: 5, earmark, cleared, sale);

        Assert.Empty(EarmarkQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void RivalSale_NeverCounts_EvenIfEverEarmarked()
    {
        // The rival never earmarks -- FromPlayerShop guards this the same way it guards
        // RivalSaleQuery in the other direction.
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var sale = new ItemSold(new ItemId(1), new HeroId(9), Price: 40, FromPlayerShop: false)
        {
            Id = new EventId(2),
            Day = 5,
        };
        var state = BaseState(day: 5, earmark, sale);

        Assert.Empty(EarmarkQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void SaleOnADifferentDay_IsExcluded()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var sale = new ItemSold(new ItemId(1), new HeroId(9), Price: 40, FromPlayerShop: true)
        {
            Id = new EventId(2),
            Day = 5,
        };
        var state = BaseState(day: 5, earmark, sale);

        Assert.Empty(EarmarkQuery.ForDay(state, day: 4));
    }

    [Fact]
    public void ReEarmarkedToADifferentHero_UsesTheMostRecentHold()
    {
        var first = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 2 };
        var second = new ShelfEarmarked(new ItemId(1), new HeroId(11)) { Id = new EventId(2), Day = 4 };
        var sale = new ItemSold(new ItemId(1), new HeroId(11), Price: 40, FromPlayerShop: true)
        {
            Id = new EventId(3),
            Day = 5,
        };
        var state = BaseState(day: 5, first, second, sale);

        var match = Assert.Single(EarmarkQuery.ForDay(state, day: 5));
        Assert.Equal(new HeroId(11), match.Hero);
    }

    // ---- WaitingTonight ("<item> waits for <Name>") ----

    [Fact]
    public void HeldSinceBeforeToday_StillOnTheShelf_YieldsOneWaiting()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var state = BaseState(day: 5, earmark) with
        {
            Player = GameFactory.NewGame(555).Player with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 40, StockedDay: 1, EarmarkedFor: new HeroId(9))),
            },
        };

        var waiting = Assert.Single(EarmarkQuery.WaitingTonight(state, day: 5));
        Assert.Equal(new ItemId(1), waiting.Item);
        Assert.Equal(new HeroId(9), waiting.Hero);
    }

    [Fact]
    public void HeldTheSameDay_YieldsNothing()
    {
        // The honest predicate, mirrored from RivalSaleQuery: a hold placed today is today's own
        // decision, not yet a story about waiting.
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 5 };
        var state = BaseState(day: 5, earmark) with
        {
            Player = GameFactory.NewGame(555).Player with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 40, StockedDay: 1, EarmarkedFor: new HeroId(9))),
            },
        };

        Assert.Empty(EarmarkQuery.WaitingTonight(state, day: 5));
    }

    [Fact]
    public void NotEarmarked_YieldsNothing()
    {
        var state = BaseState(day: 5) with
        {
            Player = GameFactory.NewGame(555).Player with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 40, StockedDay: 1)),
            },
        };

        Assert.Empty(EarmarkQuery.WaitingTonight(state, day: 5));
    }

    // ---- ReleasedForDead (P2-PEOPLE-31, "a hold for the dead is released at the wake") ----

    [Fact]
    public void HoldClearedTheSameNightItsHolderDied_IsAReleasedHold()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var died = new HeroDied(new HeroId(9), Floor: 4, Cause: "lost to the Mine", WornGear: new GearSet(null, null, null))
        {
            Id = new EventId(2),
            Day = 5,
        };
        var cleared = new ShelfEarmarked(new ItemId(1), null) { Id = new EventId(3), Day = 5 };
        var state = BaseState(day: 5, earmark, died, cleared);

        var released = Assert.Single(EarmarkQuery.ReleasedForDead(state, day: 5));
        Assert.Equal(new ItemId(1), released.Item);
        Assert.Equal(new HeroId(9), released.Hero);
    }

    [Fact]
    public void ClearedHoldWhoseHolderIsAlive_IsNotAReleasedHold()
    {
        // An ordinary player-initiated release (EarmarkAction with a null hero) also emits a
        // null-Hero ShelfEarmarked -- only a same-night HeroDied for the PRIOR holder makes it one.
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var cleared = new ShelfEarmarked(new ItemId(1), null) { Id = new EventId(2), Day = 5 };
        var state = BaseState(day: 5, earmark, cleared);

        Assert.Empty(EarmarkQuery.ReleasedForDead(state, day: 5));
    }

    [Fact]
    public void ReleaseOnADifferentDayThanTheDeath_IsExcluded()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var died = new HeroDied(new HeroId(9), Floor: 4, Cause: "lost to the Mine", WornGear: new GearSet(null, null, null))
        {
            Id = new EventId(2),
            Day = 5,
        };
        var cleared = new ShelfEarmarked(new ItemId(1), null) { Id = new EventId(3), Day = 6 };
        var state = BaseState(day: 6, earmark, died, cleared);

        Assert.Empty(EarmarkQuery.ReleasedForDead(state, day: 5));
    }

    [Fact]
    public void NoDeathsTonight_YieldsNothingEvenWithAnUnrelatedClear()
    {
        var earmark = new ShelfEarmarked(new ItemId(1), new HeroId(9)) { Id = new EventId(1), Day = 3 };
        var cleared = new ShelfEarmarked(new ItemId(1), null) { Id = new EventId(2), Day = 5 };
        var state = BaseState(day: 5, earmark, cleared);

        Assert.Empty(EarmarkQuery.ReleasedForDead(state, day: 5));
    }
}
