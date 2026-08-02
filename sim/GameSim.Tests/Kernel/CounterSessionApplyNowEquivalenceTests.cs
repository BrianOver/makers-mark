using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// THE proving test for the 2026-08-02 loop-legibility plan's U1 (KTD-A): the counter session
/// (Open/Present/Suggest/Haggle/Close) moved to <see cref="ActionTiming"/>'s immediate lane, and this
/// pins that an entire session driven through <see cref="GameKernel.ApplyNow"/> — never touching
/// <see cref="GameKernel.Tick"/> until the day actually needs to move — reaches the SAME substantive
/// state as the identical script driven the old bell-stepped way (one action per <see cref="GameKernel.Tick"/>
/// call, the shape every existing Counter test in this repo already uses).
///
/// <para><b>Why this had to be written and run BEFORE the phase-collapse rule landed.</b> The original
/// <see cref="ActionTiming"/> author's own comment warned that resolving the counter session immediately
/// "would race that state machine" — and a naive reclassification WOULD have: <see cref="PresentItemAction"/>'s
/// verdict (walk vs. open a haggle round) used to resolve ONLY in <see cref="CounterQueueSystem.Process"/>,
/// a phase SYSTEM that <see cref="GameKernel.ApplyNow"/> never runs by contract. Without the accompanying
/// fix in <see cref="CounterHandlers.Apply"/> (present now resolves synchronously via
/// <see cref="CounterQueueSystem.ResolvePresentedItem"/>, not the systems pass), this exact test would
/// have failed at the Haggle step with "No standing offer to respond to — present an item first."
/// forever, because Present would apply-and-do-nothing under ApplyNow. This test is the mutation check
/// for that fix: revert <see cref="CounterHandlers"/>'s resolve-on-present call (or revert the
/// <see cref="ActionTiming"/> reclassification) and this goes red.</para>
/// </summary>
public class CounterSessionApplyNowEquivalenceTests
{
    private static Hero MakeHero(int id, int gold) => new(
        new HeroId(id), $"Lc{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, string name) => new(
        new ItemId(id), "test-recipe", name, ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(6, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>Scoped to the counter's own systems, in production order (mirrors
    /// CounterQueueSystemTests.Kernel/HaggleEconomicsTests.Kernel) — isolates the session/fallback seam
    /// from RNG-drawing/id-allocating Morning systems that would collide with hand-picked fixture ids.</summary>
    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    private static GameState Fresh()
    {
        var hero1 = MakeHero(1, gold: 100);
        var hero2 = MakeHero(2, gold: 100);
        var itemA = MakeItem(1, "Counter Sword A");
        var itemB = MakeItem(2, "Shelf Sword B"); // hero2's atomic-fallback pickup after Close

        return GameFactory.NewGame(seed: 9001) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero1).Add(2, hero2),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, itemA).Add(2, itemB),
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(itemA.Id, 25), new ShelfEntry(itemB.Id, 25)),
            },
        };
    }

    /// <summary>The old bell-stepped shape: one action, one <see cref="GameKernel.Tick"/> call — exactly
    /// what every pre-existing Counter test in this repo already does, and (pre-U1) exactly what
    /// <c>SimAdapter.Queue</c> did for every one of these five verbs since none resolved immediately.</summary>
    private static GameState RunBellStepped()
    {
        var kernel = Kernel();
        var state = Fresh();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(new ItemId(1)))).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept))).NewState;
        // Close via Tick advances the day out of Morning in this SAME tick (CounterPhaseHoldTests
        // precedent) — hero2 (unserved) falls back to the atomic pass for itemB in that same tick.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CloseCounterAction())).NewState;

        return state;
    }

    /// <summary>The new live shape: the whole conversation resolves through <see cref="GameKernel.ApplyNow"/>
    /// — zero <see cref="GameKernel.Tick"/> calls — then ONE real tick lets the day catch up to the SAME
    /// phase boundary the bell-stepped run's own Close-tick crossed (the "modulo timing" the plan names:
    /// ApplyNow resolves the player's own part now, but the day still only actually moves at a real tick).</summary>
    private static GameState RunLiveViaApplyNow()
    {
        var kernel = Kernel();
        var state = Fresh();

        Assert.True(ActionTiming.ResolvesImmediately(new OpenCounterAction()));
        Assert.True(ActionTiming.ResolvesImmediately(new PresentItemAction(new ItemId(1))));
        Assert.True(ActionTiming.ResolvesImmediately(new HaggleResponseAction(HaggleResponseKind.Accept)));
        Assert.True(ActionTiming.ResolvesImmediately(new CloseCounterAction()));

        state = kernel.ApplyNow(state, new OpenCounterAction()).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase); // ApplyNow never advances the phase

        state = kernel.ApplyNow(state, new PresentItemAction(new ItemId(1))).NewState;
        // The proof that Present actually resolved (not just recorded intent): a round is open with
        // a standing offer, the SAME tick it was shown — no systems pass ever ran to get here.
        Assert.True(state.Counter!.Round > 0);
        Assert.NotNull(state.Counter.StandingOfferGold);

        state = kernel.ApplyNow(state, new HaggleResponseAction(HaggleResponseKind.Accept)).NewState;
        Assert.Contains(state.EventLog.OfType<CounterSaleClosed>(), e => e.Hero == new HeroId(1));

        state = kernel.ApplyNow(state, new CloseCounterAction()).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase); // still Morning — ApplyNow doesn't ring the bell
        Assert.True(state.Counter!.Closed);

        // The day only actually moves at a real tick — exactly one, empty (nothing left to submit),
        // to cross the SAME boundary the bell-stepped run's Close-tick crossed in one step.
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;

        return state;
    }

    [Fact]
    public void FullOpenPresentHaggleClose_ViaApplyNow_MatchesBellSteppedTick_ModuloActionLogShape()
    {
        var bellStepped = RunBellStepped();
        var liveApplyNow = RunLiveViaApplyNow();

        // Both scripts actually closed hero1's sale and ran hero2's atomic fallback — not a vacuous
        // "both empty" pass.
        Assert.Equal(new ItemId(1), bellStepped.Heroes[1].Gear.Weapon);
        Assert.Equal(new ItemId(2), bellStepped.Heroes[2].Gear.Weapon);
        Assert.Equal(new ItemId(1), liveApplyNow.Heroes[1].Gear.Weapon);
        Assert.Equal(new ItemId(2), liveApplyNow.Heroes[2].Gear.Weapon);

        // Same phase/day boundary reached either way.
        Assert.Equal((bellStepped.Day, bellStepped.Phase), (liveApplyNow.Day, liveApplyNow.Phase));
        Assert.Null(bellStepped.Counter);
        Assert.Null(liveApplyNow.Counter);

        // ActionLog SHAPE legitimately differs (4 single-action Tick batches vs. 4 ApplyNow log
        // entries + one trailing empty catch-up-tick batch) — that is the timing artifact the plan's
        // "modulo timing" carve-out names, not a semantic difference. Strip it before comparing the
        // rest of the world byte-for-byte (same idiom HaggleEconomicsTests.MoodPermille_... uses to
        // zero the one field a test is allowed to differ on before a SaveCodec comparison).
        static GameState WithoutActionLog(GameState state) => state with { ActionLog = ImmutableList<LoggedBatch>.Empty };

        Assert.Equal(
            SaveCodec.Serialize(WithoutActionLog(bellStepped)),
            SaveCodec.Serialize(WithoutActionLog(liveApplyNow)));
    }
}
