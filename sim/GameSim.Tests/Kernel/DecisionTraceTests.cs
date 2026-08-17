using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Crafting;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Kernel;

/// <summary>
/// U-T6 (register #164, §11.14.8): proves <see cref="TickResult.Traces"/> actually fills for the
/// three producers this unit wires — <c>HaggleResolver</c> (haggle-band/haggle-counter),
/// <c>QualityRoller</c> (quality-roll, both the passive and active models), and
/// <c>HeirloomHandlers</c> (the same quality-roll path a reforge draws) — end to end through the
/// REAL <see cref="GameKernel"/>, never a handler called directly with a hand-rolled sink. Per
/// <see cref="GameSim.Kernel.ITraceSink"/>'s own doc, that distinction matters: a test-local
/// <c>IEventSink</c> stub that isn't the kernel's own <c>EventCollector</c> silently drops every
/// <c>Trace</c> call, so only a kernel-routed test can prove the wiring reaches
/// <see cref="TickResult.Traces"/> rather than just proving the producer CALLS the sink.
/// </summary>
public class DecisionTraceTests
{
    // ---- Crafting: quality-roll, both models ------------------------------------------------

    private static readonly GameKernel CraftKernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState StateWith(params (string Key, int Qty)[] materials)
    {
        var state = GameFactory.NewGame(seed: 42);
        var stores = state.Player.Materials;
        foreach (var (key, qty) in materials)
        {
            stores = stores.SetItem(key, qty);
        }

        return state with { Player = state.Player with { Materials = stores } };
    }

    [Fact]
    public void Craft_ActiveModel_ThroughKernel_RecordsQualityRollTrace_MatchingTheCraftedItem()
    {
        // Blacksmith (the "dagger"/"copper" recipe's owner) is ActiveCraft:true, so a captured
        // PerformanceGrade routes through QualityRoller.RollActive.
        var state = StateWith(("copper", 5));
        var result = CraftKernel.Tick(state,
            ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper", PerformanceGrade: 700)));

        Assert.Empty(result.Rejected);
        var trace = Assert.Single(result.Traces);
        Assert.Equal("quality-roll", trace.What);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(item.Quality.ToString(), trace.Chosen);
        Assert.Contains("performanceGrade=700", trace.Detail);
    }

    [Fact]
    public void Craft_AutoCraft_ThroughKernel_RecordsQualityRollTrace_NamingTheCeiling()
    {
        // No PerformanceGrade -> the null-grade auto-craft branch, hard-capped at Superior.
        var state = StateWith(("copper", 5));
        var result = CraftKernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        var trace = Assert.Single(result.Traces);
        Assert.Equal("quality-roll", trace.What);
        Assert.Contains("isAutoCraft=True", trace.Detail);
    }

    [Fact]
    public void Traces_AreNeverPersisted_TickWithNoDecisions_HasEmptyTraces()
    {
        // A tick that never touches a haggle/craft/reforge path has nothing to explain — the
        // empty default TickResult.Traces documents itself, not a producer bug.
        var state = StateWith(("copper", 5));
        var result = CraftKernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        Assert.Empty(result.Traces);
    }

    [Fact]
    public void Traces_NeverAppearInTheSerializedSave_NotPartOfGameState()
    {
        // TickResult.Traces lives on the kernel's RETURN value, never on GameState — so it can
        // never move the golden-replay hash or leak into a save file. Belt-and-braces on top of
        // the type system already enforcing this: a save with a stray "quality-roll" string would
        // mean someone routed a trace into persisted state by hand later.
        var state = StateWith(("copper", 5));
        var result = CraftKernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper", PerformanceGrade: 700)));

        Assert.NotEmpty(result.Traces); // sanity: this run DID produce a trace...
        Assert.DoesNotContain("quality-roll", SaveCodec.Serialize(result.NewState)); // ...but never in the save.
    }

    // ---- Heirloom reforge: the SAME quality-roll path -----------------------------------------

    private static readonly GameKernel ReforgeKernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new HeirloomHandlers()));

    [Fact]
    public void Reforge_ThroughKernel_RecordsQualityRollTrace()
    {
        var hero = new Hero(new HeroId(1), "Fallen", "vanguard",
            Level: 1, MaxHp: 20, Gold: 0, GearSet.Empty, ImmutableList<ItemMemory>.Empty,
            Alive: false, DeepestFloorReached: 1, DiedOnDay: 1);
        var wornSword = new Item(new ItemId(1), "shortsword", "Shortsword", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(4, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

        var state = StateWith(("copper", 5)) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero with { Gear = new GearSet(wornSword.Id, null, null) }),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(wornSword.Id.Value, wornSword),
            EventLog = ImmutableList.Create<GameEvent>(new HeroDied(hero.Id, 1, "test", new GearSet(wornSword.Id, null, null))),
            NextItemId = 100, // wornSword already occupies id 1 — the reforged item must mint elsewhere.
        };

        var result = ReforgeKernel.Tick(state,
            ImmutableList.Create<PlayerAction>(new ReforgeHeirloomAction(wornSword.Id, "dagger", "copper")));

        Assert.Empty(result.Rejected);
        var trace = Assert.Single(result.Traces);
        Assert.Equal("quality-roll", trace.What);
    }

    // ---- Haggle: haggle-band (offer opened) and haggle-counter (offer resolved) --------------

    private static Hero MakeHero(int id, string classId, int gold, int mood = 0) => new(
        new HeroId(id), $"Lc{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    {
        MoodPermille = mood,
    };

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    // Scoped to the counter's own systems, same as HaggleEconomicsTests.Kernel — the full
    // production composition pulls in RNG-drawing/id-allocating systems irrelevant here.
    private static GameKernel HaggleKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    private static GameState HaggleBaseState(ImmutableSortedDictionary<int, Hero> heroes, params Item[] items) =>
        GameFactory.NewGame(seed: 900) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    private static GameState WithShelf(GameState state, int gold, params ShelfEntry[] shelf) =>
        state with { Player = PlayerState.NewGame(gold) with { Shelf = shelf.ToImmutableList() } };

    [Fact]
    public void OpenCounter_PresentItem_RecordsRoundOneHaggleBandTrace()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(HaggleBaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = HaggleKernel();

        var opened = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));
        var presented = kernel.Tick(opened.NewState, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id)));

        var trace = Assert.Single(presented.Traces);
        Assert.Equal("haggle-band", trace.What);
        Assert.Contains("round 1", trace.Chosen);
    }

    [Fact]
    public void HoldFirm_RecordsRoundTwoHaggleBandTrace()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(HaggleBaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = HaggleKernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        var heldFirm = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)));

        var trace = Assert.Single(heldFirm.Traces);
        Assert.Equal("haggle-band", trace.What);
        Assert.Contains("round 2", trace.Chosen);
    }

    [Fact]
    public void HaggleCounter_ExceedsCeiling_RecordsFleecedTrace()
    {
        // Same fixture as HaggleEconomicsTests.HoldFirm_Round2BandAccepts_WhatRound1Refused:
        // countered 100 exceeds round 1's ceiling of 98 -> fleeced.
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(HaggleBaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = HaggleKernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 100)));

        Assert.Single(result.Events.OfType<CounterSaleClosed>());
        var trace = Assert.Single(result.Traces, t => t.What == "haggle-counter");
        Assert.Equal("fleeced", trace.Chosen);
    }

    [Fact]
    public void HaggleCounter_WithinPinWindow_RecordsPinnedTrace()
    {
        // Same fixture, HoldFirm once first: round 2's wider ceiling (107) turns the SAME
        // countered 100 into a pin (HaggleEconomicsTests' own pinned case).
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(HaggleBaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = HaggleKernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 100)));

        Assert.Single(result.Events.OfType<CounterSaleClosed>());
        var trace = Assert.Single(result.Traces, t => t.What == "haggle-counter");
        Assert.Equal("pinned", trace.Chosen);
    }

    [Fact]
    public void HaggleCounter_InsideBandOutsidePinWindow_RecordsPlainSaleTrace()
    {
        // Same fixture and round-2 band as the pinned case above; sweeps counter prices for one
        // that lands inside [floor, ceiling] but OUTSIDE the pin window around true willingness —
        // the third of HaggleResolver's three outcomes, deterministic (no RNG in the haggle path
        // at all), so the sweep is over PRICE, not seed.
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);

        for (var price = 80; price <= 106; price++)
        {
            var state = WithShelf(HaggleBaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
            var kernel = HaggleKernel();
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
            var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, price)));

            if (result.Events.OfType<CounterSaleClosed>().FirstOrDefault() is { Pinned: false } sale)
            {
                var trace = Assert.Single(result.Traces, t => t.What == "haggle-counter");
                Assert.Equal("plain sale", trace.Chosen);
                return; // proven
            }
        }

        Assert.Fail("No plain-sale (inside band, outside pin window) price found in [80,106] — scenario needs retuning.");
    }
}
