using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// PA3 HIGH-RISK seam (plan 2026-07-21-002, PKD5): <see cref="GameKernel.Tick"/> holds the day at
/// Morning while a counter session is open and unfinished, and advances (exactly once, never
/// double) the instant the session closes — by explicit <see cref="CloseCounterAction"/> or by the
/// queue running dry. <c>GameSim.Tests.Counter.AtomicEquivalenceTests</c> pins the OTHER half: a
/// run that never opens the counter never triggers this branch at all.
/// </summary>
public class CounterPhaseHoldTests
{
    private static Hero MakeHero(int id, int gold = 100) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>Scoped to the counter's own systems, in production order — the full
    /// GameComposition pulls in RNG-drawing/id-allocating systems (RecruitSystem,
    /// RivalRestockSystem, ...) that are irrelevant to the phase-hold seam and would collide with
    /// these tests' hand-picked fixture ids.</summary>
    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    [Fact]
    public void OpenCounter_HoldsMorningAcrossMultipleEmptyTicks_DayNeverDoubleAdvances()
    {
        var heroes = new[] { MakeHero(1), MakeHero(2), MakeHero(3) }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var state = GameFactory.NewGame(seed: 701) with { Heroes = heroes };
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Equal((1, DayPhase.Morning), (state.Day, state.Phase));

        // Several ticks with NO player action at all — nobody presented, nobody haggled, nobody
        // closed. The session stays open (queue non-empty, Closed false) so the phase must hold.
        for (var i = 0; i < 5; i++)
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            Assert.Equal((1, DayPhase.Morning), (state.Day, state.Phase)); // never double-advances
            Assert.NotNull(state.Counter);
            Assert.False(state.Counter!.Closed);
        }
    }

    [Fact]
    public void CloseCounter_AdvancesToExpedition_SameTick_SessionTornDown()
    {
        var heroes = new[] { MakeHero(1), MakeHero(2) }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var state = GameFactory.NewGame(seed: 702) with { Heroes = heroes };
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Equal((1, DayPhase.Morning), (state.Day, state.Phase));

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CloseCounterAction())).NewState;

        Assert.Equal((1, DayPhase.Expedition), (state.Day, state.Phase)); // day never double-advances
        Assert.Null(state.Counter); // session reset the instant Morning actually advances
    }

    [Fact]
    public void QueueExhaustion_AutoCloses_AdvancesToExpedition_SameTick()
    {
        var hero = MakeHero(1);
        var sword = new Item(
            new ItemId(1), "test-recipe", "Iron Sword", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(6, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var state = GameFactory.NewGame(seed: 703) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, sword),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 10)) },
        };
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Equal((1, DayPhase.Morning), (state.Day, state.Phase));

        // The lone customer resolves (buys) — the queue is now empty, so the session auto-closes
        // and the SAME tick advances (nobody had to submit CloseCounterAction by hand).
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;

        Assert.Equal((1, DayPhase.Expedition), (state.Day, state.Phase));
        Assert.Null(state.Counter);
        Assert.Equal(sword.Id, state.Heroes[1].Gear.Weapon); // the sale really happened first
    }

    [Fact]
    public void NoCounterOpened_MorningAdvancesNormally_NeverHolds()
    {
        // Sanity companion to the atomic-equivalence pin: with Counter untouched (null), the
        // Morning->Expedition edge fires on the very first tick, exactly as before PA3.
        var state = GameFactory.NewGame(seed: 704);
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;

        Assert.Equal((1, DayPhase.Expedition), (state.Day, state.Phase));
    }
}
