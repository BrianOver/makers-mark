using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// P2-PEOPLE-28, sim half: an earmark hides a stocked piece from every hero but the one it is held
/// for, at both shelf readers (ordinary shopping and commission fulfilment), and the hero it is held
/// for still decides. The Godot verb and the harness hand are the unit's second half.
/// </summary>
public class EarmarkTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item Sword(int id, int attack = 8) => new(
        new ItemId(id), "longsword", "Longsword", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 3), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState Shelved(ShelfEntry entry, params Hero[] heroes)
    {
        var item = Sword(entry.Item.Value);
        return GameFactory.NewGame(seed: 42) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(item.Id.Value, item),
            NextItemId = item.Id.Value + 1,
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(entry) },
        };
    }

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(GameState state, PlayerAction action)
    {
        var sink = new TestSink();
        var (next, rejected) = new ShopHandlers().Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    private static (GameState State, List<GameEvent> Events) Shop(GameState state)
    {
        var sink = new TestSink();
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    [Fact]
    public void Earmark_SetsTheHeroOnTheEntry_KeepsPriceAndStockedDay_AndEmits()
    {
        var torvald = MakeHero(7, gold: 100);
        var state = Shelved(new ShelfEntry(new ItemId(1), Price: 30, StockedDay: 4), torvald);

        var (next, rejected, events) = Apply(state, new EarmarkAction(new ItemId(1), torvald.Id));

        Assert.Null(rejected);
        var entry = Assert.Single(next.Player.Shelf);
        Assert.Equal(torvald.Id, entry.EarmarkedFor);
        Assert.Equal(30, entry.Price);
        Assert.Equal(4, entry.StockedDay);
        var earmarked = Assert.Single(events.OfType<ShelfEarmarked>());
        Assert.Equal((new ItemId(1), (HeroId?)torvald.Id), (earmarked.Item, earmarked.Hero));
    }

    [Fact]
    public void Earmark_NullClears_AndAClearOnAnOpenPieceIsRejected()
    {
        var torvald = MakeHero(7, gold: 100);
        var held = Shelved(new ShelfEntry(new ItemId(1), 30, 4, torvald.Id), torvald);

        var (cleared, rejected, events) = Apply(held, new EarmarkAction(new ItemId(1), null));
        Assert.Null(rejected);
        Assert.Null(Assert.Single(cleared.Player.Shelf).EarmarkedFor);
        Assert.Null(Assert.Single(events.OfType<ShelfEarmarked>()).Hero);

        var (_, rejectedAgain, _) = Apply(cleared, new EarmarkAction(new ItemId(1), null));
        Assert.NotNull(rejectedAgain);
    }

    [Fact]
    public void Earmark_Rejects_NotOnShelf_UnknownHero_DeadHero_AndSameHeroTwice()
    {
        var torvald = MakeHero(7, gold: 100);
        var fallen = MakeHero(8, gold: 100, alive: false);
        var state = Shelved(new ShelfEntry(new ItemId(1), 30), torvald, fallen);

        Assert.NotNull(Apply(state, new EarmarkAction(new ItemId(99), torvald.Id)).Rejected);
        Assert.NotNull(Apply(state, new EarmarkAction(new ItemId(1), new HeroId(42))).Rejected);
        Assert.NotNull(Apply(state, new EarmarkAction(new ItemId(1), fallen.Id)).Rejected);

        var (held, _, _) = Apply(state, new EarmarkAction(new ItemId(1), torvald.Id));
        Assert.NotNull(Apply(held, new EarmarkAction(new ItemId(1), torvald.Id)).Rejected);
    }

    [Fact]
    public void Legality_MirrorsTheHandler_ForEveryEarmarkShape()
    {
        var torvald = MakeHero(7, gold: 100);
        var fallen = MakeHero(8, gold: 100, alive: false);
        var open = Shelved(new ShelfEntry(new ItemId(1), 30), torvald, fallen);
        var held = Shelved(new ShelfEntry(new ItemId(1), 30, 0, torvald.Id), torvald, fallen);

        foreach (var phase in Enum.GetValues<DayPhase>())
        {
            foreach (var (state, action) in new (GameState, EarmarkAction)[]
            {
                (open, new(new ItemId(1), torvald.Id)),
                (open, new(new ItemId(1), null)),
                (open, new(new ItemId(1), fallen.Id)),
                (open, new(new ItemId(1), new HeroId(42))),
                (open, new(new ItemId(99), torvald.Id)),
                (held, new(new ItemId(1), torvald.Id)),
                (held, new(new ItemId(1), null)),
                (held, new(new ItemId(1), fallen.Id)),
            })
            {
                var legal = ActionLegality.IsLegal(state, action, phase);
                var accepted = Apply(state, action).Rejected is null;
                Assert.True(legal == accepted, $"{action} in {phase}: legal={legal} accepted={accepted}");
            }
        }
    }

    [Fact]
    public void HeldPiece_IsInvisibleToEveryOtherHero_NoPurchase_NoPassEvent()
    {
        var torvald = MakeHero(7, gold: 100);
        var other = MakeHero(9, gold: 100);
        var state = Shelved(new ShelfEntry(new ItemId(1), 30, 0, torvald.Id), other);

        var (after, events) = Shop(state);

        Assert.Single(after.Player.Shelf);
        Assert.Equal(100, after.Heroes[other.Id.Value].Gold);
        Assert.DoesNotContain(events, e => e is ItemSold);
        Assert.DoesNotContain(events, e => e is HeroPassedOnItem);
    }

    [Fact]
    public void HeldPiece_IsOnTheShelfForTheHeroItIsHeldFor()
    {
        var torvald = MakeHero(7, gold: 100);
        var state = Shelved(new ShelfEntry(new ItemId(1), 30, 0, torvald.Id), torvald);

        var (after, events) = Shop(state);

        var sale = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal((torvald.Id, new ItemId(1)), (sale.Buyer, sale.Item));
        Assert.Empty(after.Player.Shelf);
        Assert.Equal(new ItemId(1), after.Heroes[torvald.Id.Value].Gear.Weapon);
    }

    [Fact]
    public void OpenPiece_ShopsExactlyAsBefore()
    {
        var other = MakeHero(9, gold: 100);
        var state = Shelved(new ShelfEntry(new ItemId(1), 30), other);

        var (_, events) = Shop(state);

        Assert.Single(events.OfType<ItemSold>());
    }
}
