#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// PA7 (plan 2026-07-21-002, PKD6/PKD8): the stepped Morning counter service played through the
/// real <see cref="CounterPanel"/>/<see cref="ShopPanel"/>/<see cref="ShopStage"/> Controls — bind
/// (an open mid-haggle <see cref="CounterState"/> fixture and the null-Counter arrange-only
/// layout), action fidelity (each button queues exactly the PA1 action it names, and a scripted
/// stepped morning driven through UI signals ONLY matches the same actions applied directly to
/// the sim — the adapter-fidelity pattern this project's other panel suites already pin), faces
/// (the Moonlighter event→EmoteKind map, and the walk-reason prose rendering on the card), and
/// meters (the sim's own integers render 1:1 — no UI-side arithmetic).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CounterPanelTests
{
    private static readonly ItemId ShopItemId = new(501);

    // ── Bind ──────────────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Refresh_MidHaggleCounterStateFixture_RendersCustomerMetersRoundAndOffer_NoError()
    {
        var state = CounterFixture(round: 2, interest: 150, patience: 2, goodwill: -40, standingOffer: 12, presented: ShopItemId);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            AssertThat(text).Contains("Buyer1");
            AssertThat(text).Contains(ClassRegistry.StrikerId);
            AssertThat(text).Contains("Test Blade"); // the presented item
            AssertThat(text).Contains("12g");        // the standing offer
            AssertThat(text).Contains("-40");        // Goodwill
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Refresh_NullCounter_RendersArrangeOnlyLayout_WithOpenCounterEntry()
    {
        var ui = MountMainUi(); // fresh campaign — Counter is null by default (PKD6)
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            AssertThat(text).Contains("Open Counter");
            Find<Button>(ui.Shop, "OpenCounter"); // exists and is reachable — throws if missing

            // Async prep stays live even with no session — the shelf sections still render.
            AssertThat(text).Contains("Your Shelf");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Action fidelity ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void OpenCounterButton_QueuesOpenCounterAction()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");

            AssertThat(ui.Adapter.PendingActions.OfType<OpenCounterAction>().Count()).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EachHaggleControl_QueuesExactlyTheIntendedActionRecord()
    {
        var state = CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: 10, presented: ShopItemId);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");

            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");
            PressEnabled(ui.Shop, $"Suggest_{ShopItemId.Value}");
            PressEnabled(ui.Shop, "Accept");
            PressEnabled(ui.Shop, "HoldFirm");

            Find<SpinBox>(ui.Shop, "CounterPrice").Value = 37;
            PressEnabled(ui.Shop, "Counter");

            PressEnabled(ui.Shop, "CloseCounter");

            var pending = ui.Adapter.PendingActions;
            AssertThat(pending.OfType<PresentItemAction>().Single().Item).IsEqual(ShopItemId);
            AssertThat(pending.OfType<SuggestItemAction>().Single().Item).IsEqual(ShopItemId);

            var haggles = pending.OfType<HaggleResponseAction>().ToList();
            AssertThat(haggles.Count(a => a.Kind == HaggleResponseKind.Accept)).IsEqual(1);
            AssertThat(haggles.Count(a => a.Kind == HaggleResponseKind.HoldFirm)).IsEqual(1);
            var counterAction = haggles.Single(a => a.Kind == HaggleResponseKind.Counter);
            AssertThat(counterAction.Price!.Value).IsEqual(37);

            AssertThat(pending.OfType<CloseCounterAction>().Count()).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ScriptedSteppedMorning_ThroughUiSignalsOnly_MatchesSameActionsAppliedDirectlyToTheSim()
    {
        // Path A: driven entirely through the real CounterPanel Controls/signals.
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        ui.OpenPanel("Shop");

        PressEnabled(ui.Shop, "OpenCounter");
        ui.Adapter.AdvancePhase(); // opens the session; the lone hero becomes the active customer

        PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");
        ui.Adapter.AdvancePhase(); // CounterQueueSystem resolves this tick: Buy verdict opens round 1

        PressEnabled(ui.Shop, "Accept");
        ui.Adapter.AdvancePhase(); // closes the sale at the round-1 standing offer

        var uiState = ui.Adapter.CurrentState;
        Unmount(ui);

        // Path B: the identical action sequence applied straight to a fresh, identically-seeded
        // adapter — no UI involved at all (the adapter-fidelity pattern this project's other
        // panel suites already pin, e.g. ForgeMinigameTests/ShopPanelTests).
        var direct = new SimAdapter(SingleHeroGuaranteedBuyState());
        direct.Queue(new OpenCounterAction());
        direct.AdvancePhase();
        direct.Queue(new PresentItemAction(ShopItemId));
        direct.AdvancePhase();
        direct.Queue(new HaggleResponseAction(HaggleResponseKind.Accept));
        direct.AdvancePhase();
        var directState = direct.CurrentState;

        AssertThat(uiState.Player.Gold).IsEqual(directState.Player.Gold);
        AssertThat(uiState.Heroes[1].Gold).IsEqual(directState.Heroes[1].Gold);
        AssertThat(uiState.Player.Shelf.IsEmpty).IsEqual(directState.Player.Shelf.IsEmpty);
        AssertThat(uiState.EventLog.OfType<CounterSaleClosed>().Count())
            .IsEqual(directState.EventLog.OfType<CounterSaleClosed>().Count());
        AssertThat(uiState.EventLog.OfType<CounterSaleClosed>().Single().Price)
            .IsEqual(directState.EventLog.OfType<CounterSaleClosed>().Single().Price);
    }

    // ── Faces (Moonlighter — pure render of the sim's computed verdict) ─────────────────────────

    [TestCase]
    public void ShopStage_ClassifyCounterSale_PinnedIsHeart_UnpinnedIsSmile()
    {
        AssertThat(ShopStage.ClassifyCounterSale(pinned: true)).IsEqual(ShopStage.EmoteKind.Heart);
        AssertThat(ShopStage.ClassifyCounterSale(pinned: false)).IsEqual(ShopStage.EmoteKind.Smile);
    }

    [TestCase]
    public void ShopStage_ClassifyCounterWalk_PatienceReasonIsFrown_EveryOtherReasonIsShrug()
    {
        AssertThat(ShopStage.ClassifyCounterWalk("the customer's patience ran out"))
            .IsEqual(ShopStage.EmoteKind.Frown);
        AssertThat(ShopStage.ClassifyCounterWalk("the price never met their willingness"))
            .IsEqual(ShopStage.EmoteKind.Shrug);
    }

    [TestCase]
    public void PatienceExhausted_RealWalkThroughTheSim_RendersHeroNameAndReasonProse_OnTheCard()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");

            PressEnabled(ui.Shop, "OpenCounter");
            ui.Adapter.AdvancePhase();
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");
            ui.Adapter.AdvancePhase(); // round 1 opens

            // Three HoldFirms exhaust the InitialPatienceRounds (3) budget — the third emits
            // CustomerWalked with the pinned "patience ran out" reason and closes the session
            // (the lone hero drains the queue).
            for (var i = 0; i < 3; i++)
            {
                PressEnabled(ui.Shop, "HoldFirm");
                ui.Adapter.AdvancePhase();
            }

            AssertThat(ui.Adapter.CurrentState.EventLog.OfType<CustomerWalked>().Count()).IsEqual(1);
            var walked = ui.Adapter.CurrentState.EventLog.OfType<CustomerWalked>().Single();
            AssertThat(walked.Reason).Contains("patience ran out");

            ui.OpenPanel("Shop"); // re-refresh so the just-closed tick's walk renders
            var text = RenderedText(ui.Shop);
            AssertThat(text).Contains("Buyer1");
            AssertThat(text).Contains("patience ran out");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Meters (sim integers render 1:1 — no UI-side arithmetic) ────────────────────────────────

    [TestCase]
    public void Meters_RenderSimIntegers1To1_NoUiSideArithmetic()
    {
        var state = CounterFixture(round: 3, interest: 275, patience: 1, goodwill: -365, standingOffer: 999, presented: ShopItemId);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            AssertThat(text).Contains("275");
            AssertThat(text).Contains("-365");
            AssertThat(text).Contains("999g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static Hero MakeHero(int id, string classId, int gold) => new(
        new HeroId(id), $"Buyer{id}", classId, Level: 1, MaxHp: 24, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item TestBlade() => new(
        ShopItemId, "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>A one-hero, one-item world whose item's value ratio (gain/price) guarantees a
    /// Buy verdict from <c>ShoppingAi.EvaluateItem</c> — the SAME check <c>CounterQueueSystem</c>
    /// gates a haggle round's opening on (mirrors <c>ShopStageTests.GuaranteedSaleState</c>'s
    /// proven fixture shape, adapted to a single hero for a deterministic queue).</summary>
    private static GameState SingleHeroGuaranteedBuyState()
    {
        var hero = MakeHero(1, ClassRegistry.StrikerId, gold: 500);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero);
        var baseState = GameFactory.NewGame(7002, heroes);

        return baseState with
        {
            // GameFactory.NewGame always seeds NextHeroId at 1 regardless of the heroes handed
            // in — bump it past the fixture's own hero id so a later tick's RecruitSystem never
            // collides assigning a "new" id that already exists (this test drives several real
            // AdvancePhase ticks, unlike ShopStageTests' pure-render fixtures of the same shape).
            NextHeroId = 2,
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(ShopItemId.Value, TestBlade()),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(ShopItemId, 8)) },
        };
    }

    /// <summary>A world with a live, mid-haggle <see cref="CounterState"/> already installed —
    /// for Bind/meter/face rendering scenarios that don't need to drive the haggle themselves.</summary>
    private static GameState CounterFixture(
        int round, int interest, int patience, int goodwill, int? standingOffer, ItemId? presented)
    {
        var hero = MakeHero(1, ClassRegistry.StrikerId, gold: 500);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero);
        var baseState = GameFactory.NewGame(7001, heroes);

        var counter = new CounterState(
            Queue: ImmutableList.Create(hero.Id),
            Active: hero.Id,
            Round: round,
            InterestPermille: interest,
            PatienceRounds: patience,
            GoodwillPermille: goodwill,
            Presented: presented,
            StandingOfferGold: standingOffer,
            Served: ImmutableSortedSet<int>.Empty,
            Closed: false);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(ShopItemId.Value, TestBlade()),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(ShopItemId, 8)) },
            Counter = counter,
        };
    }
}
#endif
