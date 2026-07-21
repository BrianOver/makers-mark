using System.Collections.Immutable;
using GameSim.Cli;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Cli;

/// <summary>
/// PA5 (plan 2026-07-21-002): pins the CLI's new 'counter'/'haggle' verbs and 'craft''s optional
/// grade-in-hand suffix the SAME way <see cref="CliWiringTests"/> pins the pre-existing verbs —
/// exercised through the exact composition root Program.cs builds
/// (<see cref="GameComposition.BuildKernel"/>), so a phase gate or a parse helper drifting from
/// what Program.cs actually submits fails here first. "Verb parsing round-trips to the exact
/// action records" (the plan's own words) means: format an action to the line Program.cs would
/// print, split it exactly like Program.cs's switch does, reconstruct the action from those
/// tokens via the SAME <see cref="CliParse"/>/<see cref="CliIds"/> helpers the real verb case
/// uses, and assert the reconstruction is equal to the original record.
/// </summary>
public class CounterCliWiringTests
{
    [Fact]
    public void Accepts_CounterVerbsAreMorningOnly_CraftAcceptsAnyPhase()
    {
        var kernel = GameComposition.BuildKernel();

        Assert.True(kernel.Accepts(new OpenCounterAction(), DayPhase.Morning));
        Assert.False(kernel.Accepts(new OpenCounterAction(), DayPhase.Evening));

        Assert.True(kernel.Accepts(new PresentItemAction(new ItemId(1)), DayPhase.Morning));
        Assert.False(kernel.Accepts(new PresentItemAction(new ItemId(1)), DayPhase.Expedition));

        Assert.True(kernel.Accepts(new SuggestItemAction(new ItemId(1)), DayPhase.Morning));
        Assert.False(kernel.Accepts(new SuggestItemAction(new ItemId(1)), DayPhase.Camp));

        Assert.True(kernel.Accepts(new HaggleResponseAction(HaggleResponseKind.Accept), DayPhase.Morning));
        Assert.False(kernel.Accepts(new HaggleResponseAction(HaggleResponseKind.Accept), DayPhase.ExpeditionDeep));

        Assert.True(kernel.Accepts(new CloseCounterAction(), DayPhase.Morning));
        Assert.False(kernel.Accepts(new CloseCounterAction(), DayPhase.Evening));

        // 'craft ... grade <n>' still routes through the same all-phase CraftingHandlers as the
        // plain two-arg form — the grade suffix is data on the same action type, not a new verb.
        Assert.True(kernel.Accepts(new CraftAction("dagger", "copper", PerformanceGrade: 850), DayPhase.Morning));
        Assert.True(kernel.Accepts(new CraftAction("dagger", "copper", PerformanceGrade: 850), DayPhase.Expedition));
    }

    [Fact]
    public void CounterOpen_RoundTrips_FormatToTokensToAction()
    {
        // "counter open" -> parts ["counter", "open"] -> new OpenCounterAction().
        var original = new OpenCounterAction();
        var parts = CliActionFormat.Format(original)!.Split(' ');

        Assert.Equal(["counter", "open"], parts);
        PlayerAction reparsed = parts[1] switch
        {
            "open" => new OpenCounterAction(),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(original, reparsed);
    }

    [Fact]
    public void CounterPresent_RoundTrips_FormatToTokensToAction()
    {
        // "counter present I9" -> parts ["counter", "present", "I9"] -> the SAME item id, via
        // the exact CliParse.TryItemId helper Program.cs's 'counter present' case calls.
        var original = new PresentItemAction(new ItemId(9));
        var parts = CliActionFormat.Format(original)!.Split(' ');

        Assert.Equal(["counter", "present", "I9"], parts);
        Assert.True(CliParse.TryItemId(parts[2], out var id, out _));
        var reparsed = new PresentItemAction(new ItemId(id));
        Assert.Equal(original, reparsed);
    }

    [Fact]
    public void CounterSuggest_RoundTrips_FormatToTokensToAction()
    {
        var original = new SuggestItemAction(new ItemId(3));
        var parts = CliActionFormat.Format(original)!.Split(' ');

        Assert.Equal(["counter", "suggest", "I3"], parts);
        Assert.True(CliParse.TryItemId(parts[2], out var id, out _));
        Assert.Equal(original, new SuggestItemAction(new ItemId(id)));
    }

    [Fact]
    public void CounterClose_RoundTrips_FormatToTokensToAction()
    {
        var parts = CliActionFormat.Format(new CloseCounterAction())!.Split(' ');

        Assert.Equal(["counter", "close"], parts);
    }

    [Theory]
    [InlineData(HaggleResponseKind.Accept, null, "haggle accept")]
    [InlineData(HaggleResponseKind.HoldFirm, null, "haggle hold")]
    [InlineData(HaggleResponseKind.Counter, 96, "haggle counter 96")]
    public void HaggleResponse_RoundTrips_FormatToTokensToAction(HaggleResponseKind kind, int? price, string expectedLine)
    {
        var original = new HaggleResponseAction(kind, price);
        var line = CliActionFormat.Format(original);
        Assert.Equal(expectedLine, line);

        var parts = line!.Split(' ');
        PlayerAction reparsed = parts[1] switch
        {
            "accept" => new HaggleResponseAction(HaggleResponseKind.Accept),
            "hold" => new HaggleResponseAction(HaggleResponseKind.HoldFirm),
            "counter" => new HaggleResponseAction(HaggleResponseKind.Counter, int.Parse(parts[2])),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(original, reparsed);
    }

    [Fact]
    public void CraftWithGrade_RoundTrips_FormatToTokensToAction()
    {
        // "craft dagger copper grade 850" -> parts[0..4], mirroring Program.cs's
        // parts.Length == 5 && parts[3] == "grade" branch.
        var original = new CraftAction("dagger", "copper", PerformanceGrade: 850);
        var parts = CliActionFormat.Format(original)!.Split(' ');

        Assert.Equal(["craft", "dagger", "copper", "grade", "850"], parts);
        Assert.Equal("grade", parts[3], ignoreCase: true);
        Assert.True(CliParse.TryInt(parts[4], out var grade, out _));
        Assert.Equal(original, new CraftAction(parts[1], parts[2], grade));
    }

    [Fact]
    public void CraftWithoutGrade_StillRoundTrips_TheOriginalThreeTokenForm()
    {
        var original = new CraftAction("dagger", "copper");
        var parts = CliActionFormat.Format(original)!.Split(' ');

        Assert.Equal(["craft", "dagger", "copper"], parts);
        Assert.Equal(original, new CraftAction(parts[1], parts[2]));
    }

    /// <summary>Scoped to exactly the handlers/systems a craft-then-shelve-then-counter script
    /// touches (mirrors <c>Counter/HaggleEconomicsTests.Kernel</c>): the full production
    /// composition also pulls in <c>RivalRestockSystem</c>, which mints a competing rival-shelf
    /// item every Morning and would have the fixture's own single hero buy IT instead on day 1 —
    /// noise this CLI-wiring test has no interest in. No RNG-drawing system beyond the craft roll
    /// itself runs here, so the script is a clean proof that the CLI's action SHAPES reach a real
    /// craft, shelf, and counter sale through the actual handlers, not a hand-rolled stand-in.</summary>
    private static GameKernel CraftAndCounterKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(
            new CraftingHandlers(), new MaterialVendorHandlers(), new ShopHandlers(), new CounterHandlers()));

    [Fact]
    public void SteppedMorningThroughCliConstructedActions_ResolvesAHaggledSale()
    {
        // The exact action SHAPES the CLI's 'craft'/'stock'/'counter'/'haggle' verbs construct,
        // applied through the real handlers — proves the CLI wiring (not just the underlying
        // handler in isolation, already covered by Counter/HaggleEconomicsTests) reaches a real
        // craft, a real shelved item, and a real counter sale.
        var kernel = CraftAndCounterKernel();
        var hero = new Hero(
            new HeroId(1), "Fixture Hero", ClassRegistry.VanguardId, Level: 1, MaxHp: 25, Gold: 200,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var state = GameFactory.NewGame(seed: 321) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
        };

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new BuyMaterialAction("copper", 4))).NewState;
        Assert.Equal(DayPhase.Expedition, state.Phase);

        // 'craft dagger copper grade 850' — the grade-in-hand form 'craft <recipe> [grade <n>]' adds.
        var craftTick = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper", PerformanceGrade: 850)));
        Assert.Empty(craftTick.Rejected);
        state = craftTick.NewState;
        var itemId = Assert.Single(craftTick.Events.OfType<ItemCrafted>()).Item;

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Camp
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // ExpeditionDeep
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new StockAction(itemId, 20))).NewState; // Evening
        Assert.Equal(DayPhase.Morning, state.Phase);
        Assert.Equal(2, state.Day);
        Assert.Single(state.Player.Shelf);

        // 'counter open' then 'counter present I<id>' then 'haggle accept'.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(itemId))).NewState;
        Assert.NotNull(state.Counter?.StandingOfferGold);

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept)));
        Assert.Empty(result.Rejected);
        Assert.Single(result.Events.OfType<CounterSaleClosed>());
    }
}
