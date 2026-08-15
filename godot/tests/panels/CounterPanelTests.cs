#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using GodotClient.Panels;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// PA7 (plan 2026-07-21-002, PKD6/PKD8): the stepped Morning counter service played through the
/// real <see cref="CounterPanel"/>/<see cref="ShopPanel"/> Controls, and (U5, world-and-interiors
/// plan) <see cref="MarketLife2D"/>'s market-room choreography, which absorbed the deleted
/// <c>ShopStage</c>'s classification statics verbatim — bind
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

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<OpenCounterAction>().Count()).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EachHaggleControl_QueuesExactlyTheIntendedActionRecord()
    {
        // U1 (loop-legibility widening): every counter verb now resolves the INSTANT it's pressed
        // (ActionTiming.ResolvesImmediately), so the old shape of this test — mash Present, Suggest,
        // Accept, HoldFirm, Counter, CloseCounter in one straight run, then inspect the queue — has
        // nothing left to inspect: PendingActions is empty by construction (nothing queues anymore),
        // and by the time a mashed-through press reaches "HoldFirm" the round it was aimed at has
        // already resolved and moved on (Accept closes the round-1 sale and promotes the NEXT
        // customer, whose fresh round has no "HoldFirm" to hold — the button briefly wasn't even
        // there under the old assumption). Driving one response per round against whatever the
        // session is ACTUALLY offering — across three real customers, so a closing verb (Accept,
        // Counter) never steps on a later control's target item — is what keeps this test's
        // original intent (each control produces exactly the record it names) both real and true.
        var itemA = new ItemId(601); // Accept target
        var itemB = new ItemId(602); // Presented, then abandoned mid-round by itemC's Present — the
                                      // HoldFirm target; never sold, stays on the shelf throughout.
        var itemC = new ItemId(603); // Counter target
        var state = ThreeCustomerGuaranteedBuyState(itemA, itemB, itemC);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter"); // hero 1 becomes the active customer

            // ── Hero 1: Present, Suggest, Accept — closes the sale, promotes hero 2. ────────────
            PressEnabled(ui.Shop, $"Present_{itemA.Value}"); // guaranteed Buy verdict opens round 1
            PressEnabled(ui.Shop, $"Suggest_{itemA.Value}");
            PressEnabled(ui.Shop, "Accept");

            // ── Hero 2: Present, HoldFirm — round advances, hero 2 stays active (no promotion). ─
            PressEnabled(ui.Shop, $"Present_{itemB.Value}");
            PressEnabled(ui.Shop, "HoldFirm");

            // Still hero 2: presenting a DIFFERENT item abandons the held round cleanly (CounterHandlers.
            // ApplyPresent's own documented behaviour) and opens a fresh one — the real path to a second
            // haggle control on the SAME customer without exhausting patience to force a walk.
            PressEnabled(ui.Shop, $"Present_{itemC.Value}");
            Find<CoinStack>(ui.Shop, "CounterPrice").SetValue(37);
            PressEnabled(ui.Shop, "Counter"); // closes hero 2's sale, promotes hero 3 (queue not empty yet)

            // Hero 3 is now active and the session is still open — CloseCounter here exercises a real
            // early close (hero 3 goes unserved), not a press against an already-closed session.
            PressEnabled(ui.Shop, "CloseCounter");

            var applied = ui.Adapter.AppliedThisPhase;

            var presents = applied.OfType<PresentItemAction>().ToList();
            AssertThat(presents.Count).IsEqual(3);
            AssertThat(presents.Any(a => a.Item == itemA)).IsTrue();
            AssertThat(presents.Any(a => a.Item == itemB)).IsTrue();
            AssertThat(presents.Any(a => a.Item == itemC)).IsTrue();

            AssertThat(applied.OfType<SuggestItemAction>().Single().Item).IsEqual(itemA);

            var haggles = applied.OfType<HaggleResponseAction>().ToList();
            AssertThat(haggles.Count(a => a.Kind == HaggleResponseKind.Accept)).IsEqual(1);
            AssertThat(haggles.Count(a => a.Kind == HaggleResponseKind.HoldFirm)).IsEqual(1);
            var counterAction = haggles.Single(a => a.Kind == HaggleResponseKind.Counter);
            AssertThat(counterAction.Price!.Value).IsEqual(37);

            AssertThat(applied.OfType<CloseCounterAction>().Count()).IsEqual(1);

            // The sales that actually closed (Accept, Counter) really did leave the shelf; the
            // abandoned HoldFirm round never sold anything — Present alone never does.
            var shelf = ui.Adapter.CurrentState.Player.Shelf.Select(e => e.Item).ToImmutableHashSet();
            AssertThat(shelf.Contains(itemA)).IsFalse();
            AssertThat(shelf.Contains(itemB)).IsTrue();
            AssertThat(shelf.Contains(itemC)).IsFalse();
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

    // ── U2 desk physicality (plan 2026-07-28-002, design doc §B5) ───────────────────────────────
    // CounterDesk is a private nested Control (mirrors AlchemyBrewPuzzle's BrewCanvas), so every
    // test below finds it as a plain Control by name and drives it ONLY through the GuiInput
    // signal (EmitSignal) — the same seam UiTestSupport.Click and AlchemyBrewPuzzleTests already
    // rely on, since CounterDesk subscribes via `GuiInput +=`, never a `_GuiInput` override.

    [TestCase]
    public void DragDropPresent_OverTheMat_QueuesTheIdenticalPresentActionAsTheButton()
    {
        // Path A: the real drag-drop gesture through CounterDesk's GuiInput event.
        ItemId dragResult;
        var uiDrag = MountMainUi(new SimAdapter(
            CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: null, presented: null)));
        try
        {
            uiDrag.OpenPanel("Shop");
            var desk = Find<Control>(uiDrag.Shop, "CounterDesk");

            var shelfPos = new Vector2(20f, 20f); // the only shelved item's icon (shelf slot 0)
            var matPos = new Vector2(244f, 97f);  // inside the counter mat

            desk.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = shelfPos });
            desk.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = matPos });
            desk.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = matPos });

            dragResult = uiDrag.Adapter.AppliedThisPhase.OfType<PresentItemAction>().Single().Item;
        }
        finally
        {
            Unmount(uiDrag);
        }

        // Path B: the existing Present button, on an identically-seeded fresh session.
        ItemId buttonResult;
        var uiButton = MountMainUi(new SimAdapter(
            CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: null, presented: null)));
        try
        {
            uiButton.OpenPanel("Shop");
            PressEnabled(uiButton.Shop, $"Present_{ShopItemId.Value}");
            buttonResult = uiButton.Adapter.AppliedThisPhase.OfType<PresentItemAction>().Single().Item;
        }
        finally
        {
            Unmount(uiButton);
        }

        AssertThat(dragResult).IsEqual(buttonResult);
        AssertThat(dragResult).IsEqual(ShopItemId);
    }

    [TestCase]
    public void DragDropPresent_ReleasedOffTheMat_ShelvesHarmlessly_QueuesNoAction()
    {
        var ui = MountMainUi(new SimAdapter(
            CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: null, presented: null)));
        try
        {
            ui.OpenPanel("Shop");
            var desk = Find<Control>(ui.Shop, "CounterDesk");

            var shelfPos = new Vector2(20f, 20f);
            desk.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = shelfPos });
            desk.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = Vector2.Zero }); // off the mat

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<PresentItemAction>().Count()).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CoinComposedCounterOffer_QueuesHaggleResponseCounter_WithExactlyTheComposedTotal()
    {
        var state = CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: 10, presented: ShopItemId);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            var coinStack = Find<CoinStack>(ui.Shop, "CounterPrice");

            // Compose 133g by stacking coins from the floor (MinValue = 1): one 100, three 10s,
            // two 1s on top of that starting 1 — the SAME AddCoins seam a click on each
            // denomination stack calls (CoinStack's own KTD-A contract).
            coinStack.SetValue(1);
            coinStack.AddCoins(100);
            coinStack.AddCoins(10);
            coinStack.AddCoins(10);
            coinStack.AddCoins(10);
            coinStack.AddCoins(1);
            coinStack.AddCoins(1);

            PressEnabled(ui.Shop, "Counter");

            var counterAction = ui.Adapter.AppliedThisPhase.OfType<HaggleResponseAction>()
                .Single(a => a.Kind == HaggleResponseKind.Counter);
            AssertThat(counterAction.Price!.Value).IsEqual(133);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Handshake_OneClick_QueuesTheIdenticalAcceptActionAsTheButton()
    {
        // Path A: the real handshake click through CounterDesk's GuiInput event.
        int handshakeAccepts;
        var uiHandshake = MountMainUi(new SimAdapter(
            CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: 10, presented: ShopItemId)));
        try
        {
            uiHandshake.OpenPanel("Shop");
            var desk = Find<Control>(uiHandshake.Shop, "CounterDesk");
            var handshakePos = new Vector2(519f, 109f); // inside the handshake affordance

            desk.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = handshakePos });

            handshakeAccepts = uiHandshake.Adapter.AppliedThisPhase.OfType<HaggleResponseAction>()
                .Count(a => a.Kind == HaggleResponseKind.Accept);
        }
        finally
        {
            Unmount(uiHandshake);
        }

        // Path B: the existing Accept button, on an identically-seeded fresh session.
        int buttonAccepts;
        var uiButton = MountMainUi(new SimAdapter(
            CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: 10, presented: ShopItemId)));
        try
        {
            uiButton.OpenPanel("Shop");
            PressEnabled(uiButton.Shop, "Accept");
            buttonAccepts = uiButton.Adapter.AppliedThisPhase.OfType<HaggleResponseAction>()
                .Count(a => a.Kind == HaggleResponseKind.Accept);
        }
        finally
        {
            Unmount(uiButton);
        }

        AssertThat(handshakeAccepts).IsEqual(1);
        AssertThat(buttonAccepts).IsEqual(1);
    }

    [TestCase]
    public void DeskPosture_AcrossMoodAndPatienceBuckets_NeverQueuesAnAction()
    {
        var happy = CounterFixture(round: 1, interest: 900, patience: 3, goodwill: 900, standingOffer: 10, presented: ShopItemId);
        var wary = CounterFixture(round: 1, interest: 900, patience: 1, goodwill: -900, standingOffer: 10, presented: ShopItemId);

        foreach (var fixture in new[] { happy, wary })
        {
            var ui = MountMainUi(new SimAdapter(fixture));
            try
            {
                ui.OpenPanel("Shop");
                AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0);

                // Idle hover (no press) over the desk — exercises the posture/tapping-foot
                // presentation path repeatedly; must never touch the action queue.
                var desk = Find<Control>(ui.Shop, "CounterDesk");
                for (var i = 0; i < 4; i++)
                {
                    desk.EmitSignal(Control.SignalName.GuiInput,
                        new InputEventMouseMotion { Position = new Vector2(2 + i, 2 + i) });
                }

                AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0);
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    [TestCase]
    public void SpeechBubbleAndDeskRender_DuringARealWalkAway_QueueNoExtraActions()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            ui.Adapter.AdvancePhase();
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");
            ui.Adapter.AdvancePhase();

            for (var i = 0; i < 3; i++)
            {
                PressEnabled(ui.Shop, "HoldFirm");
                ui.Adapter.AdvancePhase();
            }

            AssertThat(ui.Adapter.CurrentState.EventLog.OfType<CustomerWalked>().Count()).IsEqual(1);
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0); // every queued action already resolved

            ui.OpenPanel("Shop"); // re-refresh: paints the walk-away speech bubble + a no-customer desk
            AssertThat(RenderedText(ui.Shop)).Contains("patience ran out"); // still a real, readable Label
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0); // presentation queued nothing
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Faces (Moonlighter — pure render of the sim's computed verdict) ─────────────────────────

    [TestCase]
    public void MarketLife_ClassifyCounterSale_PinnedIsHeart_UnpinnedIsSmile()
    {
        AssertThat(MarketLife2D.ClassifyCounterSale(pinned: true)).IsEqual(MarketLife2D.EmoteKind.Heart);
        AssertThat(MarketLife2D.ClassifyCounterSale(pinned: false)).IsEqual(MarketLife2D.EmoteKind.Smile);
    }

    [TestCase]
    public void MarketLife_ClassifyCounterWalk_PatienceReasonIsFrown_EveryOtherReasonIsShrug()
    {
        AssertThat(MarketLife2D.ClassifyCounterWalk("the customer's patience ran out"))
            .IsEqual(MarketLife2D.EmoteKind.Frown);
        AssertThat(MarketLife2D.ClassifyCounterWalk("the price never met their willingness"))
            .IsEqual(MarketLife2D.EmoteKind.Shrug);
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

    // ── Next-step legibility + verb consequences (owner playtest, 2026-08-04) ───────────────────
    // "Counter worked - person buying but really unsure WHAt to do after?" and "i hit suggest and
    // interest went up but nothing happened lol" — the counter used to move a bare number with no
    // statement of what it meant or what to do next. Every assertion below reads ONLY visible
    // on-screen text (CounterFeedback / the panel's rendered body), never internal state.

    [TestCase]
    public void NextStep_RoundOpen_NamesTheClosingVerbsAndTheRealPatienceGap_BeforeAnyPress()
    {
        var state = CounterFixture(round: 1, interest: 0, patience: 3, goodwill: 0, standingOffer: 10, presented: ShopItemId);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            // Stated BEFORE any button is pressed — the panel does not wait for a press to explain
            // itself. No invented "interest needed" threshold: the real closing rule (Accept/Counter
            // close it, Hold Firm risks the customer's patience) is named honestly instead.
            AssertThat(text).Contains("Next step");
            AssertThat(text).Contains("10g");
            AssertThat(text).Contains("Test Blade");
            AssertThat(text).Contains("Accept to close the sale now");
            AssertThat(text).Contains("Hold Firm");
            AssertThat(text).Contains("3 patience rounds left");
            AssertThat(text).Contains("walks away with nothing bought");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NextStep_NoRoundYetOpen_InstructsPresentingAnItem_BeforeAnyPress()
    {
        var state = CounterFixture(round: 0, interest: 0, patience: 3, goodwill: 0, standingOffer: null, presented: null);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            AssertThat(text).Contains("Next step");
            AssertThat(text).Contains("present an item from the shelf to Buyer1");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Suggest_ComplementaryEmptySlot_ReportsInterestRoseAndThatItDoesNotTouchThisRound()
    {
        // Buyer1's Gear is empty (MakeHero) and Test Blade is a Weapon — a genuine complementary
        // empty slot (HaggleResolver.IsComplementaryEmptySlot), so the upsell bonus really lands.
        var state = CounterFixture(round: 0, interest: 0, patience: 3, goodwill: 0, standingOffer: null, presented: null);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Suggest_{ShopItemId.Value}");

            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback).Contains("Suggested Test Blade");
            // The owner-reported confusion, answered directly: the number moved AND why nothing
            // else visibly changed (it affects a FUTURE round/presentment, not this one).
            AssertThat(feedback).Contains("interest rose 0 to 80");
            AssertThat(feedback).Contains("not this one");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Suggest_SlotAlreadyEquipped_ReportsInterestHeldAndWhy()
    {
        var baseState = CounterFixture(round: 0, interest: 0, patience: 3, goodwill: 0, standingOffer: null, presented: null);
        var occupiedHero = baseState.Heroes[1] with { Gear = baseState.Heroes[1].Gear.WithSlot(ItemSlot.Weapon, new ItemId(9999)) };
        var state = baseState with { Heroes = baseState.Heroes.SetItem(1, occupiedHero) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Suggest_{ShopItemId.Value}"); // Weapon slot — already filled, so no bonus

            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback).Contains("Suggested Test Blade");
            AssertThat(feedback).Contains("interest held at 0");
            AssertThat(feedback).Contains("isn't what Buyer1 needs right now");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HoldFirm_RoundAdvances_ReportsTheNewStandingOfferAndPatienceRemaining()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}"); // round 1 opens (immediate)
            PressEnabled(ui.Shop, "HoldFirm"); // patience 3 -> 2; not exhausted

            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback).Contains("Held firm");
            AssertThat(feedback).Contains("reconsider");
            AssertThat(feedback).Contains("new standing offer");
            AssertThat(feedback).Contains("2 patience rounds left");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HoldFirm_PatienceExhausted_ReportsTheWalkAwayAndTheNextCustomer()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");

            PressEnabled(ui.Shop, "HoldFirm");
            PressEnabled(ui.Shop, "HoldFirm");
            PressEnabled(ui.Shop, "HoldFirm"); // 3rd HoldFirm exhausts InitialPatienceRounds (3)

            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback).Contains("patience ran out");
            AssertThat(feedback).Contains("walked away with nothing bought");
            // The lone hero in this fixture empties the queue — the honest "what's next" answer.
            AssertThat(feedback).Contains("that was the last customer this morning");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Accept_ReportsTheItemPriceHeroAndTheNextCustomer()
    {
        // Two heroes queued so Accept's "what's next" clause names a REAL next customer, not just
        // a closed session — answers "person buying but unsure what to do after".
        var hero1 = MakeHero(1, ClassRegistry.StrikerId, gold: 500);
        var hero2 = MakeHero(2, ClassRegistry.StrikerId, gold: 500);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero1.Id.Value, hero1).Add(hero2.Id.Value, hero2);
        var baseState = GameFactory.NewGame(7010, heroes);
        var counter = new CounterState(
            Queue: ImmutableList.Create(hero1.Id, hero2.Id),
            Active: hero1.Id,
            Round: 1,
            InterestPermille: 0,
            PatienceRounds: 3,
            GoodwillPermille: 0,
            Presented: ShopItemId,
            StandingOfferGold: 45,
            Served: ImmutableSortedSet<int>.Empty,
            Closed: false);
        var state = baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(ShopItemId.Value, TestBlade()),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(ShopItemId, 60)) },
            Counter = counter,
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "Accept");

            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback).Contains("Sold Test Blade to Buyer1 for 45g");
            AssertThat(feedback).Contains("Buyer2 is up next");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CounterVerb_PriceAboveTheRoundCeiling_ReportsTheFleeceAndTheNextCustomer()
    {
        // Deterministic fleece: Striker's class factor is neutral (1000 permille) and this fixture
        // starts every meter at 0/fresh, so round-1's ceiling is 980 permille of true willingness —
        // true willingness equals the list price itself here (8g), so ceiling = 7g. Countering at
        // the full 8g list price is therefore ALWAYS above ceiling — a real, provable fleece, not a
        // coin flip (WillingnessModel.Band/ResolveCounter, sim/GameSim/Counter).
        var itemA = new ItemId(801);
        var itemB = new ItemId(802);
        var itemC = new ItemId(803);
        var state = ThreeCustomerGuaranteedBuyState(itemA, itemB, itemC);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            PressEnabled(ui.Shop, $"Present_{itemA.Value}"); // round 1 opens for Buyer1

            Find<CoinStack>(ui.Shop, "CounterPrice").SetValue(8); // the shelf list price itself
            PressEnabled(ui.Shop, "Counter");

            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback).Contains("Countered at 8g");
            AssertThat(feedback).Contains("sold Test Blade 801 to Buyer1 for 8g");
            AssertThat(feedback).Contains("felt like a fleece");
            AssertThat(feedback).Contains("goodwill dropped");
            AssertThat(feedback).Contains("Buyer2 is up next");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Customer voice (U2, plan 2026-08-03-001-feat-loop-structure-plan.md, KTD-B) ─────────────
    // Owner playtest: "Counter worked - person buying but really unsure WHAt to do after?" and
    // "i hit suggest and interest went up but nothing happened lol" — the customer never said
    // anything. Every assertion below reads ONLY visible on-screen text (never CustomerVoice
    // internals directly — those are pinned separately in CustomerVoiceTests).

    [TestCase]
    public void ActiveCustomer_OpensWithAStatedWant_NamingTheirGearGapAndTheirOwnGold()
    {
        // Buyer1's Gear is GearSet.Empty (MakeHero) — RaidForecast.MissingItemSlots reports Weapon
        // first — and their gold is the fixture's own 500, never a rounded or invented figure.
        var state = CounterFixture(round: 0, interest: 0, patience: 3, goodwill: 0, standingOffer: null, presented: null);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            AssertThat(text).Contains("Buyer1");
            AssertThat(text).Contains("Looking for a weapon");
            AssertThat(text).Contains("500g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NoActiveCustomer_RendersNoWantLineBubble()
    {
        var ui = MountMainUi(); // fresh campaign — Counter is null (PKD6), nobody is at the counter
        try
        {
            ui.OpenPanel("Shop");
            var text = RenderedText(ui.Shop);

            AssertThat(text.Contains("Looking for")).IsFalse();
            AssertThat(text.Contains("Just browsing")).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PresentingTheWantedSlot_AtAFairPrice_OpensARound_NotAWalk_AndSpeaksInterest()
    {
        // SingleHeroGuaranteedBuyState's shelved item IS a weapon — the same slot the want line
        // (Weapon first, gear empty) names — presented at its own fair list price.
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            var wantText = RenderedText(ui.Shop);
            AssertThat(wantText).Contains("Looking for a weapon");

            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");
            var text = RenderedText(ui.Shop);

            // A round opened (a real standing offer, not the "—" empty placeholder) — never a walk.
            AssertThat(ui.Adapter.CurrentState.Counter!.Round).IsGreater(0);
            AssertThat(ui.Adapter.CurrentState.Counter.StandingOfferGold is not null).IsTrue();
            AssertThat(ui.Adapter.CurrentState.EventLog.OfType<CustomerWalked>().Count()).IsEqual(0);
            AssertThat(text.Contains("passed (")).IsFalse(); // the walk-away consequence phrase

            // The customer's own spoken reply to the Buy verdict (CustomerVoice.PresentReply).
            AssertThat(text).Contains("Test Blade? I could use that.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Suggest_FittingEmptySlot_RendersTheInterestedSpokenReply_AndTheInterestChipMovesSameRefresh()
    {
        var state = CounterFixture(round: 0, interest: 0, patience: 3, goodwill: 0, standingOffer: null, presented: null);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Suggest_{ShopItemId.Value}");

            var text = RenderedText(ui.Shop);
            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;

            // The spoken reply (owner playtest: give the meter movement a voice)...
            AssertThat(feedback).Contains("Test Blade? ...I do lack one.");
            // ...and the Interest chip itself moved, in the SAME refresh (no bare number with no
            // comment, and no comment with no number either).
            AssertThat(text).Contains("80");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U2 (loud-failures-and-quiet-channels plan): press feedback + no double-play ────────────
    // Before this unit CounterPanel had ZERO Cue.Play call sites. Mirrors
    // ImmediateActionsDoNotReplayThePhaseTests' own technique (a real button press, then read
    // AudioDirector.RecentCues) rather than trusting the queued action alone.

    [TestCase]
    public void PresentButton_PlaysClickCue()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"Pressing Present played [{string.Join(", ", audio.RecentCues)}] — Click was " +
                    "never among them. CounterPanel had zero Cue.Play call sites before this unit.")
                .Contains(Cue.Click);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AcceptButton_PlaysClickCue()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            PressEnabled(ui.Shop, "Accept");

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"Pressing Accept played [{string.Join(", ", audio.RecentCues)}] — Click was " +
                    "never among them.")
                .Contains(Cue.Click);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HoldFirmButton_PlaysClickCue()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            PressEnabled(ui.Shop, "HoldFirm");

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"Pressing Hold Firm played [{string.Join(", ", audio.RecentCues)}] — Click was " +
                    "never among them.")
                .Contains(Cue.Click);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CounterButton_PlaysClickCue()
    {
        var state = CounterFixture(round: 1, interest: 100, patience: 3, goodwill: 0, standingOffer: 10, presented: ShopItemId);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            PressEnabled(ui.Shop, "Counter");

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"Pressing Counter played [{string.Join(", ", audio.RecentCues)}] — Click was " +
                    "never among them.")
                .Contains(Cue.Click);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The regression pin for the double-play risk the packet flagged: <c>MarketLife2D.EndJudging</c>
    /// already plays <see cref="Cue.Coin"/> for a closed counter sale (its own remarks — "covers a
    /// stepped counter sale as well as a shelf sale"), driven by <c>Town2D.Refresh</c> feeding
    /// <c>MarketLife2D.QueueDay</c> the SAME <see cref="CounterSaleClosed"/> event this Accept press
    /// resolves. CounterPanel's OWN new press feedback (<see cref="Cue.Click"/>, this same unit)
    /// must never ALSO key a <see cref="Cue.Coin"/> off that event — this proves the sale, end to
    /// end through the real <c>MainUi</c>/<c>Town2D</c>/<c>MarketLife2D</c> wiring, plays it exactly
    /// once, not zero and not two.
    /// </summary>
    [TestCase]
    public void CompletedCounterSale_PlaysCoinExactlyOnce()
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroGuaranteedBuyState()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, "OpenCounter");
            PressEnabled(ui.Shop, $"Present_{ShopItemId.Value}");

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            PressEnabled(ui.Shop, "Accept"); // closes the sale — an immediate verb (U1, loop-legibility)

            // Same technique MarketLifeTests.Advance_SoldRun_PlaysTheCoinCue_WhenTheSaleLands uses,
            // INCLUDING its first small Advance: QueueDay only STAGES the run behind a start delay,
            // so ActiveCustomerCount is still 0 the instant Accept returns. Without this step the
            // loop below sees 0 and exits without ever advancing, and the test then reports "Coin
            // played 0 times" as though the game were silent — which is exactly how it failed the
            // first time. Capped so a stuck machine fails this test instead of looping forever.
            var marketLife = ui.Town.MarketLife!;
            marketLife.Advance(0.01); // crosses the start delay — spawns the counter customer

            AssertThat(marketLife.ActiveCustomerCount)
                .OverrideFailureMessage(
                    "Precondition: closing the counter sale staged no customer at all, so this test " +
                    "could not have observed the coin either way. Town2D.Refresh feeds MarketLife2D." +
                    "QueueDay the tick's CounterSaleClosed event; if that stopped happening, the " +
                    "stepped counter sale is silent in the real game and U2's KTD5 premise is void.")
                .IsGreater(0);

            for (var i = 0; i < 200 && marketLife.ActiveCustomerCount > 0; i++)
            {
                marketLife.Advance(0.1);
            }

            var coinCount = audio.RecentCues.Count(c => c == Cue.Coin);
            AssertThat(coinCount)
                .OverrideFailureMessage(
                    $"A completed counter sale played Coin {coinCount} times " +
                    $"(cues: [{string.Join(", ", audio.RecentCues)}]) — exactly once is correct " +
                    "(MarketLife2D.EndJudging). CounterPanel's own new press feedback must never add " +
                    "a second Coin for the same sale.")
                .IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Per-hero identity at the counter (U4, owner playtest 2026-08-15: "the hero buying at the
    // counter didn't match the heroes outside") ────────────────────────────────────────────────

    [TestCase]
    public void ActiveCustomerIcon_MatchesTownAssets2D_ForHeroClassAndId()
    {
        var hero = MakeHero(7, ClassRegistry.StrikerId, gold: 500);
        var ui = MountMainUi(new SimAdapter(SingleHeroCounterFixture(hero)));
        try
        {
            ui.OpenPanel("Shop");
            var icon = Find<TextureRect>(ui.Shop, "CustomerIcon");

            AssertThat(icon.Texture)
                .OverrideFailureMessage(
                    "the counter customer's icon does not equal TownAssets2D.ForHero(classId, " +
                    "heroId) — the same call Town2D.ReconcileHeroes uses to draw this hero in the " +
                    "plaza. A hero must not change species walking from the plaza to the counter.")
                .IsEqual(TownAssets2D.ForHero(hero.ClassId, hero.Id.Value));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Two different heroes of the SAME class must each resolve through <see
    /// cref="TownAssets2D.ForHero"/> individually (never a bare class-only lookup) — and, once the
    /// class's variant pool actually has depth &gt; 1, that resolution diverges between them. Today
    /// every <c>town2d-hero-*</c> pool is depth 1 (no committed <c>-v2</c> siblings yet — see
    /// <c>ArtVariants.PoolFor</c>), so the divergence half is conditional rather than assumed; the
    /// per-hero call itself is what's pinned unconditionally, and the moment a pool gains depth this
    /// same test starts proving distinctness with no rewrite needed.</summary>
    [TestCase]
    public void TwoHeroesOfTheSameClass_EachTrackTownAssets2D_DivergingOncePoolDepthAllows()
    {
        var heroA = MakeHero(11, ClassRegistry.StrikerId, gold: 500);
        var heroB = MakeHero(12, ClassRegistry.StrikerId, gold: 500);

        var texA = ResolveCounterIconTexture(heroA);
        var texB = ResolveCounterIconTexture(heroB);

        AssertThat(texA).IsEqual(TownAssets2D.ForHero(heroA.ClassId, heroA.Id.Value));
        AssertThat(texB).IsEqual(TownAssets2D.ForHero(heroB.ClassId, heroB.Id.Value));

        var poolDepth = ArtVariants.PoolFor($"town2d-hero-{ClassRegistry.StrikerId}").Count;
        if (poolDepth > 1)
        {
            AssertThat(texA)
                .OverrideFailureMessage(
                    $"striker's variant pool has depth {poolDepth} but hero 11 and hero 12 still " +
                    "resolve the identical counter texture — the per-hero pick stopped varying.")
                .IsNotEqual(texB);
        }
    }

    private static Texture2D ResolveCounterIconTexture(Hero hero)
    {
        var ui = MountMainUi(new SimAdapter(SingleHeroCounterFixture(hero)));
        try
        {
            ui.OpenPanel("Shop");
            return Find<TextureRect>(ui.Shop, "CustomerIcon").Texture;
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static GameState SingleHeroCounterFixture(Hero hero)
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero);
        var baseState = GameFactory.NewGame((ulong)(7050 + hero.Id.Value), heroes);

        var counter = new CounterState(
            Queue: ImmutableList.Create(hero.Id),
            Active: hero.Id,
            Round: 0,
            InterestPermille: 0,
            PatienceRounds: 3,
            GoodwillPermille: 0,
            Presented: null,
            StandingOfferGold: null,
            Served: ImmutableSortedSet<int>.Empty,
            Closed: false);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(ShopItemId.Value, TestBlade()),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(ShopItemId, 8)) },
            Counter = counter,
        };
    }

    // ── Census: no panel bypasses the per-hero art ladder (U4, scenario 6) ──────────────────────
    // CounterPanel used to call IconRegistry.Sprite(classId) directly (a class-only, 48x64 flat SVG
    // primitive on a contract gen_town_sprites.py:19-27 records retired) — every other panel only
    // ever reaches it as the LAST rung of UiKit.ArtRect/PortraitFrame's fallback ladder
    // (HeroesPanel.cs, LedgerModal.cs, TavernPanel.cs). This is the regression guard: scan every
    // .cs file under the REAL res://scripts/panels directory (never a hand-listed file array — a
    // literal list stops covering a family the moment it grows, the exact failure shape KTD-E in
    // docs/design/MAKERS-MARK.md §11.12 names) for a direct IconRegistry.Sprite( call whose
    // enclosing statement does not also open an ArtRect(/PortraitFrame( call.
    [TestCase]
    public void NoPanelCallsIconRegistrySprite_OutsideAnArtRectOrPortraitFrameFallback()
    {
        var panelsDir = ProjectSettings.GlobalizePath("res://scripts/panels");
        var files = Directory.GetFiles(panelsDir, "*.cs", SearchOption.AllDirectories);

        AssertThat(files.Length)
            .OverrideFailureMessage($"found no .cs files under {panelsDir} — did the panels folder move?")
            .IsGreater(0);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var searchStart = 0;
            while (true)
            {
                var hit = text.IndexOf("IconRegistry.Sprite(", searchStart, StringComparison.Ordinal);
                if (hit < 0)
                {
                    break;
                }

                // Skip a match sitting inside a `//`/`///` comment on its own line first (this
                // file's own doc comments name "IconRegistry.Sprite(" in prose describing the
                // historical defect — a real call site never has a `//` earlier on the same
                // physical line). Same simple Contains-based rigor
                // AudioTests.EveryCue_HasAtLeastOneProductionReference already uses, not a full C#
                // parser/tokenizer.
                var lineStart = text.LastIndexOf('\n', hit) + 1;
                var linePrefix = text[lineStart..hit];

                if (linePrefix.Contains("//", StringComparison.Ordinal))
                {
                    searchStart = hit + 1;
                    continue;
                }

                // Walk back to the nearest statement boundary before this call and check whether
                // an ArtRect/PortraitFrame call opened somewhere inside that statement.
                var boundary = -1;
                for (var i = hit - 1; i >= 0; i--)
                {
                    if (text[i] is ';' or '{' or '}')
                    {
                        boundary = i;
                        break;
                    }
                }

                var statement = text[(boundary + 1)..hit];

                if (!statement.Contains("ArtRect(", StringComparison.Ordinal)
                    && !statement.Contains("PortraitFrame(", StringComparison.Ordinal))
                {
                    var line = text[..hit].Count(c => c == '\n') + 1;
                    violations.Add($"{Path.GetFileName(file)}:{line}");
                }

                searchStart = hit + 1;
            }
        }

        AssertThat(violations)
            .OverrideFailureMessage(
                "IconRegistry.Sprite called outside an ArtRect/PortraitFrame fallback argument at: " +
                string.Join(", ", violations) + " — every other panel resolves a hero through the " +
                "ArtRect/PortraitFrame ladder (fallbackIcon), with IconRegistry.Sprite only ever the " +
                "last rung; a direct call bypasses per-hero art resolution the way CounterPanel's " +
                "pre-U4 defect did.")
            .IsEmpty();
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
    /// gates a haggle round's opening on (mirrors <c>DayAdvanceHudTests.GuaranteedSaleState</c>'s
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

    /// <summary>Three identical strikers (so any of the three items below is a guaranteed Buy for
    /// whichever one is active — mirrors <see cref="SingleHeroGuaranteedBuyState"/>'s ratio, just not
    /// hero-specific) queued behind three shelved items, one per haggle-control target — for
    /// <see cref="EachHaggleControl_QueuesExactlyTheIntendedActionRecord"/>, which needs independent
    /// customers/rounds so a sale that actually closes (Accept, Counter) never removes an item a
    /// LATER control in the same test still needs on the shelf. No AdvancePhase in that test (every
    /// counter verb is immediate under U1), so unlike <see cref="SingleHeroGuaranteedBuyState"/> there
    /// is no real Tick for RecruitSystem to collide a NextHeroId against.</summary>
    private static GameState ThreeCustomerGuaranteedBuyState(ItemId itemA, ItemId itemB, ItemId itemC)
    {
        var heroes = new[]
            {
                MakeHero(1, ClassRegistry.StrikerId, gold: 500),
                MakeHero(2, ClassRegistry.StrikerId, gold: 500),
                MakeHero(3, ClassRegistry.StrikerId, gold: 500),
            }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var baseState = GameFactory.NewGame(7003, heroes);

        Item Blade(ItemId id) => new(
            id, "test-recipe", $"Test Blade {id.Value}", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(itemA.Value, Blade(itemA))
                .Add(itemB.Value, Blade(itemB))
                .Add(itemC.Value, Blade(itemC)),
            Player = baseState.Player with
            {
                Shelf = ImmutableList.Create(
                    new ShelfEntry(itemA, 8), new ShelfEntry(itemB, 8), new ShelfEntry(itemC, 8)),
            },
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
