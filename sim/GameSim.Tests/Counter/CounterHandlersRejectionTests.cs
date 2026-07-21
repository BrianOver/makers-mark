using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// PA3 (plan 2026-07-21-002): wrong-phase/wrong-state counter actions come back a typed
/// <see cref="RejectedAction"/>, never a silent drop or a state mutation, and draw ZERO RNG
/// (checked via the RNG stream being byte-identical before/after every rejection here).
/// </summary>
public class CounterHandlersRejectionTests
{
    private static Hero MakeHero(int id, int gold = 100) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id) => new(
        new ItemId(id), "test-recipe", "Iron Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(6, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>Scoped to the counter's own systems (zero RNG either way) — the full production
    /// composition isn't used here because RecruitSystem/RivalRestockSystem etc. draw RNG and
    /// allocate ids that would collide with these tests' hand-picked fixtures and break the
    /// "zero RNG drawn" assertions (mirrors <c>CounterQueueSystemTests.Kernel</c>).</summary>
    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    [Fact]
    public void PresentItem_NoSessionOpen_RejectedTyped_ZeroRng_StateUnchanged()
    {
        // Gold 0: Counter is null here (no OpenCounterAction ever landed), so the DEFAULT atomic
        // HeroShoppingSystem pass still runs this tick (that's the whole point of the
        // atomic-equivalence pin) — a broke hero can't buy anything, isolating this test to just
        // the rejected PresentItemAction's own zero-effect.
        var hero = MakeHero(1, gold: 0);
        var item = MakeItem(1);
        var state = GameFactory.NewGame(seed: 801) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
        };

        // No OpenCounterAction ever landed — Counter is null.
        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(item.Id)));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<PresentItemAction>(rejected.Action);
        Assert.Contains("No counter session", rejected.Reason);
        Assert.Null(result.NewState.Counter);
        Assert.Single(result.NewState.Player.Shelf); // untouched
        Assert.Equal(0, result.NewState.Heroes[1].Gold);
        // No COUNTER event fired — the atomic HeroShoppingSystem pass still ran this tick (Counter
        // null is the default path) and logged its own unrelated pass-reason event, which is fine.
        Assert.Empty(result.Events.OfType<CustomerApproached>());
        Assert.Empty(result.Events.OfType<CounterSaleClosed>());
        Assert.Empty(result.Events.OfType<CustomerWalked>());
    }

    [Fact]
    public void PresentItem_NotMorning_RejectedTyped_ZeroRng()
    {
        var hero = MakeHero(1);
        var item = MakeItem(1);
        var state = GameFactory.NewGame(seed: 802) with
        {
            Phase = DayPhase.Expedition, // CounterHandlers is Morning-only (CanHandle)
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
        };
        var beforeRng = state.Rng;

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(item.Id)));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<PresentItemAction>(rejected.Action);
        Assert.Contains("No handler accepts", rejected.Reason); // kernel's own phase-legality gate
        Assert.Equal(beforeRng, result.NewState.Rng); // zero RNG drawn
    }

    [Fact]
    public void PresentItem_UnshelvedItem_RejectedTyped_ZeroRng()
    {
        var hero = MakeHero(1);
        var item = MakeItem(1); // exists in the catalog but never shelved
        var state = GameFactory.NewGame(seed: 803) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
        };
        state = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        var beforeRng = state.Rng;

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(item.Id)));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<PresentItemAction>(rejected.Action);
        Assert.Contains("not on the shelf", rejected.Reason);
        Assert.Equal(beforeRng, result.NewState.Rng);
        Assert.Null(result.NewState.Counter!.Presented); // nothing recorded
    }

    [Fact]
    public void PresentItem_NoActiveCustomer_RejectedTyped_ZeroRng()
    {
        var item = MakeItem(1);
        // No heroes at all: OpenCounterAction yields a valid open session with Active null
        // (the "player is only arranging" state — PKD6).
        var state = GameFactory.NewGame(seed: 804) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
        };
        state = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Null(state.Counter!.Active);
        var beforeRng = state.Rng;

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(item.Id)));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<PresentItemAction>(rejected.Action);
        Assert.Contains("No active customer", rejected.Reason);
        Assert.Equal(beforeRng, result.NewState.Rng);
    }

    [Fact]
    public void OpenCounter_AlreadyOpen_RejectedTyped_ZeroRng()
    {
        var hero = MakeHero(1);
        var state = GameFactory.NewGame(seed: 805) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
        };
        state = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        var beforeRng = state.Rng;
        var openedQueue = state.Counter;

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<OpenCounterAction>(rejected.Action);
        Assert.Contains("already open", rejected.Reason);
        Assert.Equal(beforeRng, result.NewState.Rng);
        Assert.Equal(openedQueue, result.NewState.Counter); // untouched, not re-initialized
    }

    [Fact]
    public void CloseCounter_NoSessionOpen_RejectedTyped_ZeroRng()
    {
        var state = GameFactory.NewGame(seed: 806);
        var beforeRng = state.Rng;

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new CloseCounterAction()));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<CloseCounterAction>(rejected.Action);
        Assert.Contains("No counter session", rejected.Reason);
        Assert.Equal(beforeRng, result.NewState.Rng);
        Assert.Null(result.NewState.Counter);
    }
}
