#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U23 (world-rework plan, R5/R10/R13) — the first-run tutorial chain, the earn-2nd-profession
/// affordance, and the R5 quick-travel unlock. Drives the SAME seed-2026 campaign + "dagger"/
/// "copper" recipe every other U11 engine suite uses (<see cref="ScriptedSession"/>) so this
/// suite's action batches are proven-legal by the rest of the suite, not a one-off guess.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialFlowTests
{
    [TestCase]
    public void FreshCampaign_TutorialActive_OverridesTopSlot_SecondAffordancesHidden()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Active).IsTrue();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            // The rendered label carries the tutorial copy with markdown emphasis STRIPPED — a Godot
            // Label has no markup parser, so the raw string rendered literally as "Walk to the
            // **Forge**" on screen (caught by the 2026-07-29 playtest screenshots). The assertion
            // still pins the copy itself; it just compares through the same transformation the panel
            // applies, so this cannot drift from what the player actually reads.
            AssertThat(ui.Objective.Reason.Text)
                .IsEqual(ObjectiveTracker.Plain(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!));
            // U-T2-1: act-scoped numbering — "The Mark · 1/1", not "Tutorial 1/10".
            AssertThat(ui.Objective.Reason.Text).StartsWith("The Mark · 1/1:");
            AssertThat(ui.Objective.TutorialDismiss.Visible).IsTrue();

            AssertThat(ui.Tutorial.SecondProfessionButton.Visible).IsFalse();
            AssertThat(ui.Tutorial.QuickTravelRow.Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StarterKitCraft_SkipsTheBuyStep_JumpsStraightFromBuyMaterialToShelve()
    {
        // Class-doc softlock guard: GameFactory.StarterCopper already covers a tier-1 craft's
        // material cost for a CHOSEN profession (unlike the plain MountMainUi() default, which
        // starts with zero materials — see EachStep_... below, which buys first). A player who
        // crafts straight off the starter kit without ever buying must still advance past step 1.
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, ProfessionRegistry.BlacksmithId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            AssertThat(ui.Adapter.CurrentState.Player.Materials[ScriptedSession.CraftMaterial])
                .IsGreaterEqual(ScriptedSession.CopperNeeded); // starter kit already covers it

            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition: no MaterialPurchased event at all

            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve); // skipped Craft-as-a-wait, straight to Shelve
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BuyThenCraft_AdvancesTheCounterByExactlyOne_NeverSkippingANumber()
    {
        // Regression for Brian's playtest: "crafted the first buckler [...] tutorial went from
        // 1/5 to 3/5". BuyMaterial and Craft used to be separately NUMBERED steps (1 and 2), so a
        // player who satisfied both in one compound "get your first item made" beat watched the
        // counter jump two numbers at once — confusing even though the step machine itself was
        // correct. They now share display slot 1 (StepIndex) AND an act (TutorialAct.Mark), so the
        // on-screen counter can only ever read the SAME "The Mark · 1/1", whatever combination
        // of Buy/Craft the player actually did — the shared-slot guarantee this test exists to pin,
        // read through U-T2-1's act-scoped label instead of the old global "N/10" one.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!).StartsWith("The Mark · 1/1:");

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Craft); // internal step moved...
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!)
                .StartsWith("The Mark · 1/1:"); // ...but the ON-SCREEN number did not (same display slot)

            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            // Shelve is a DIFFERENT act (The Hand-Off) — its own numbering starts fresh at 1, which
            // is the correct per-act behavior, not a skipped or repeated number within Mark's own
            // one-slot ladder.
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!)
                .StartsWith("The Hand-Off · 1/4:");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PartyDeparting_OpensDay2Capstone_EvenWhenShelveAndPostBountyNeverHappened()
    {
        // Regression for Brian's playtest, the other half of the SAME report: "the heroes lined up
        // and left" right after that first craft, and "posting the bounty doesn't do anything &
        // doesn't update the tutorial" afterwards. A party's muster departs on its OWN
        // Expedition-phase tick (day 1's own, here — see MusterSystem/ExpeditionSystem), a beat the
        // player does not control. The old ladder only ever completed the chain on
        // `Step == WatchDeparture && partyDeparted` THAT EXACT tick, so if Shelve/PostBounty had not
        // caught up yet — exactly this scenario, a player who crafted and then just watched the
        // party leave — Step was stranded behind forever: the departure event was gone the instant
        // the tick ended, and no later bounty post could ever satisfy a ladder that was still
        // waiting on Shelve. TutorialFlow.Advance's day-1 check is UNCONDITIONAL on Step for exactly
        // this reason. U7 retargets the destination from Complete() to LookIn (day 1's new
        // capstone, "look in on them") — the chain keeps going into day 2/3 now, but the guarantee
        // that nothing strands the card mid-day-1 is unchanged.
        // Explicit profession-selecting campaign (like StarterKitCraft above) — the plain
        // MountMainUi() default starts with EMPTY materials (see ExplicitBlacksmith_... in
        // NewCampaignSeedingTests), so crafting straight off the starter kit needs the
        // GameFactory.StarterCopper seeding that only the chosen-profession constructor gives.
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, ProfessionRegistry.BlacksmithId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            // Craft straight from the starter kit (no Buy at all) — Brian's exact sequence.
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition: no departure yet (see class doc — a
                                       // day's OWN Expedition-phase systems run on ITS tick, not the
                                       // tick that merely transitions INTO it).
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            AssertThat(ui.Tutorial.Completed).IsFalse();

            ui.Adapter.AdvancePhase(); // Expedition -> Camp: day 1's own party departs THIS tick,
                                       // with Shelve/PostBounty never having happened.
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "The party departed but the tutorial did not open its day-1 capstone — this is " +
                    "the exact dead end Brian's playtest hit: Shelve/PostBounty never caught up, and " +
                    "a departure event only ever fires on its own tick, so nothing could ever advance " +
                    "this chain from here.")
                .IsEqual(TutorialStep.LookIn);
            AssertThat(ui.Tutorial.Active)
                .OverrideFailureMessage("U7: departure opens day 2 (LookIn) — it no longer ends the chain outright.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BountyPostedBeforeCraftingIsNotWasted_CreditedOnceShelveCatchesUp()
    {
        // Regression for "posting the bounty doesn't do anything & doesn't update the tutorial" —
        // a bounty posted OUT OF ORDER (here, before the player has crafted or shelved anything at
        // all) used to land on a tick where Step was nowhere near PostBounty, so the old ladder's
        // own gate silently dropped that BountyPosted event: the sim kept the bounty, but the
        // tutorial had no memory of it once that tick ended. Advance now reads
        // `state.EventLog.OfType<BountyPosted>().Any()` — a durable "has this ever happened" fact —
        // so the early post is banked and credited the moment Shelve finally catches up, with no
        // second bounty required.
        //
        // Explicit profession-selecting campaign (mirrors StarterKitCraft/PartyDeparting_...
        // above) — the plain MountMainUi() default starts with EMPTY materials (see
        // ExplicitBlacksmith_... in NewCampaignSeedingTests), and by the time this test reaches
        // Craft it has already advanced past Morning (the vendor's only open phase), so there is
        // no legal way to buy in this sequence at all. Only the chosen-profession constructor's
        // GameFactory.StarterCopper seeding lets Craft (phase-unrestricted) succeed here.
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, ProfessionRegistry.BlacksmithId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition: the queued post lands NOW, well
                                       // before Craft/Shelve exist.
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("An out-of-order bounty post should not move a step it isn't for yet.")
                .IsEqual(TutorialStep.BuyMaterial);

            // Craft (off the starter kit — no Buy needed or even possible, Morning has passed)
            // and shelve, same as any normal run — both immediate, no bell needed.
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Adapter.LastRejections.Count)
                .OverrideFailureMessage(
                    $"The craft was rejected: [{string.Join("; ", ui.Adapter.LastRejections.Select(r => r.Reason))}] " +
                    "— this test's whole premise needs it to actually resolve.")
                .IsEqual(0);
            var craftedItem = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            ui.Adapter.Queue(new StockAction(craftedItem, 50));

            // The already-posted bounty is credited the SAME instant Shelve completes — no re-post.
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "Shelve just completed and a bounty was already posted earlier — the chain should " +
                    "have cascaded straight to WatchDeparture instead of sitting on PostBounty waiting " +
                    "for a SECOND post that the player has no reason to make.")
                .IsEqual(TutorialStep.WatchDeparture);
            AssertThat(ui.Tutorial.Completed).IsFalse(); // the party hasn't departed yet
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BatchedMorningSubmission_CascadesThroughEveryStep_SameTick()
    {
        // A player who queues buy+craft+stock+post-bounty in ONE Morning batch (all four are
        // legal that same phase — PostBountyAction included, per ActionLegality's own Morning-
        // or-Evening gate) must cascade every step this SAME tick regardless of the kernel's own
        // internal event ordering (the ladder-of-ifs contract, see TutorialFlow.Advance).
        var ui = MountMainUi();
        try
        {
            // The crafted item's id is deterministic (CraftingHandlers assigns state.NextItemId)
            // — precomputed so the StockAction can be queued in the SAME batch as the CraftAction
            // that will create it.
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition: all four land in one tick

            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.WatchDeparture);
            AssertThat(ui.Tutorial.Completed).IsFalse(); // PartyDeparted needs the NEXT tick
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// §11.13 amendment (U5, R12 ruled yes): PRE-EXISTED this amendment encoding the OLD one-press
    /// semantics (✕ dismissed on the spot) — updated to the new two-press flow rather than loosened,
    /// the same correction shape as U1's own six tests. The ✕ deliberately no longer dismisses by
    /// itself: it arms a confirm naming the warrant's cost, and only the confirm's "End it" press
    /// submits ConcludeApprenticeshipAction + calls Dismiss() atomically. The behavior this test
    /// actually exists to protect — a REAL dismissal persists across a remount and never re-prompts
    /// — is unchanged and still the point of the back half below; only how many presses it now
    /// takes to reach that dismissal moved.
    /// </summary>
    [TestCase]
    public void Dismiss_MidChain_PersistsAndNeverReprompts_AfterRemount()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Active).IsTrue();
            Press(ui, "ObjectiveTutorialDismiss");
            AssertThat(ui.Tutorial.Dismissed)
                .OverrideFailureMessage("The ✕ alone must arm a confirm, not dismiss — R12's whole point.")
                .IsFalse();

            Press(ui, "ObjectiveTutorialDismissConfirmYes");
            AssertThat(ui.Tutorial.Dismissed).IsTrue();
            AssertThat(ui.Tutorial.Active).IsFalse();
            AssertThat(ui.Objective.TutorialDismiss.Visible).IsFalse();

            // Remount (a fresh MainUi instance == "after restart") — the user:// flag must carry
            // over WITHOUT calling Unmount first (Unmount wipes the very file being proven here).
            var ui2 = MountMainUi();
            try
            {
                AssertThat(ui2.Tutorial.Dismissed).IsTrue();
                AssertThat(ui2.Tutorial.Active).IsFalse();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U7 ("playtest three" plan): the 3-day extension ─────────────────────────────────────────
    //
    // Shared helpers, then one full end-to-end drive (every step, its real event, in day order),
    // then a focused test per new step proving its OWN day-gate + completion fact — the mutation-
    // check shape: each of these fails if its corresponding Advance()/hook change is reverted.

    /// <summary>Day 1, driven for REAL through the Adapter (mirrors BatchedMorningSubmission_...'s
    /// own shape): batches every Morning-legal day-1 action, then ticks through the party's own
    /// departure — leaving <see cref="TutorialStep.LookIn"/> current, exactly where U1's Watch/
    /// Mirror entry picks the story back up.</summary>
    private static void DriveDay1ToLookIn(MainUi ui)
    {
        var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
        ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
        ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
        ui.Adapter.Queue(new StockAction(craftedItemId, 50));
        ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
        ui.Adapter.AdvancePhase(); // Morning -> Expedition
        ui.Adapter.AdvancePhase(); // Expedition -> Camp: PartyDeparted -> Step becomes LookIn
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.LookIn);
    }

    /// <summary>U7 day 2/3: hands <see cref="TutorialFlow.Advance"/> a state matching the Adapter's
    /// CURRENT facts except for an overridden <see cref="GameState.Day"/> and extra events appended
    /// to the log — the same "hand a modified state to a pure method" idiom
    /// <c>SecondProfessionAffordance_...</c> above already uses for
    /// <see cref="TutorialFlow.RefreshAffordances"/>. Lets these tests pin each new step's OWN
    /// completion fact and day gate without scripting a full, RNG-shaped counter haggle or camp
    /// muster just to produce that fact for real — day 1 (every step upstream of these) is still
    /// driven for real, through the actual Adapter, in <see cref="DriveDay1ToLookIn"/>.</summary>
    private static void CraftedAdvance(MainUi ui, int day, params GameEvent[] extraEvents)
    {
        var crafted = ui.Adapter.CurrentState with
        {
            Day = day,
            EventLog = ui.Adapter.CurrentState.EventLog.AddRange(extraEvents),
        };
        ui.Tutorial.Advance(crafted);
    }

    /// <summary>Same idiom as <see cref="CraftedAdvance"/>, for the Commission step — Accept/Decline
    /// emit no distinct <see cref="GameEvent"/> at all (<c>CommissionHandlers</c>'s own doc), so the
    /// durable fact to append is a <see cref="GameState.ActionLog"/> batch instead.</summary>
    private static void CraftedAdvanceWithAction(MainUi ui, int day, PlayerAction action)
    {
        var crafted = ui.Adapter.CurrentState with
        {
            Day = day,
            ActionLog = ui.Adapter.CurrentState.ActionLog.Add(
                new LoggedBatch(day, ui.Adapter.CurrentState.Phase, ImmutableList.Create(action))),
        };
        ui.Tutorial.Advance(crafted);
    }

    /// <summary>Same idiom as <see cref="CraftedAdvanceWithAction"/>, generalized to more than one
    /// action in a SINGLE batch (order preserved within it) — U1 (§11.13) needs this for the
    /// counter step, whose own completion fact now needs an <see cref="OpenCounterAction"/> AND a
    /// later answering action in the SAME scan, mirroring a real player submitting both in one
    /// Morning batch (the exact shape <c>BatchedMorningSubmission_...</c> already exercises for
    /// buy/craft/stock/post-bounty).</summary>
    private static void CraftedAdvanceWithActions(MainUi ui, int day, params PlayerAction[] actions)
    {
        var crafted = ui.Adapter.CurrentState with
        {
            Day = day,
            ActionLog = ui.Adapter.CurrentState.ActionLog.Add(
                new LoggedBatch(day, ui.Adapter.CurrentState.Phase, ImmutableList.Create(actions))),
        };
        ui.Tutorial.Advance(crafted);
    }

    /// <summary>U1 (§11.13): the shared "answer the counter" step every later-day helper below
    /// needs to get from <see cref="TutorialStep.OpenCounter"/> to <see cref="TutorialStep.Vigil"/>
    /// — Present, then Close (the customer walks, no sale) rather than a <see
    /// cref="CounterSaleClosed"/> event, because the step's own completion no longer reads that
    /// event at all (see <c>TutorialFlow.CounterAnsweredAtLeastOnce</c>'s own doc). Presenting and
    /// walking away is the LEAST favorable case the fix has to cover that still counts as an ANSWER
    /// — U-T2-14 tightened the predicate so Open+Close ALONE (no Present/Suggest/Haggle between
    /// them) no longer completes the step at all — so using it here (rather than a scripted sale) is
    /// the stronger proof for every test that merely needs to get PAST this step to reach a later
    /// one.</summary>
    private static void AnswerCounterAndAdvance(MainUi ui, int day) =>
        CraftedAdvanceWithActions(
            ui, day, new OpenCounterAction(), new PresentItemAction(new ItemId(1)), new CloseCounterAction());

    /// <summary>
    /// U2 (tutorial-revamp plan, §11.13)'s own OpenCounter helper, KEPT and fixed to match the
    /// predicate the merge actually shipped (<c>TutorialFlow.CounterAnsweredAtLeastOnce</c>, U1's
    /// ActionLog-only version — see the registry row's own comment): needs an <see
    /// cref="OpenCounterAction"/> in the SAME batch, not just a <see cref="CloseCounterAction"/>,
    /// or the predicate's own `openedCounter` latch never trips. U-T2-14 tightened it FURTHER — a
    /// <see cref="PresentItemAction"/> (or Suggest/Haggle) must sit between Open and Close too, or
    /// the tightened predicate never flips `answered`. <c>CustomerApproached</c> stays in the
    /// EventLog deliberately — it proves the completion fact does not care whether a customer ever
    /// showed up or what they decided, only that the player opened the counter and answered. This
    /// shared helper exists only so tests further down the chain can get PAST OpenCounter to test a
    /// LATER step, mirroring <see cref="CraftedAdvance"/>'s own "hand a modified state to a pure
    /// method" idiom.
    /// </summary>
    private static void CraftedAdvanceOpenCounterComplete(MainUi ui, int day)
    {
        var state = ui.Adapter.CurrentState;
        var crafted = state with
        {
            Day = day,
            EventLog = state.EventLog.Add(new CustomerApproached(new HeroId(1))),
            ActionLog = state.ActionLog.Add(
                new LoggedBatch(day, state.Phase,
                    ImmutableList.Create<PlayerAction>(
                        new OpenCounterAction(), new PresentItemAction(new ItemId(1)), new CloseCounterAction()))),
        };
        ui.Tutorial.Advance(crafted);
    }

    /// <summary>The whole arc, every step in day order, each through its REAL completion path
    /// (<c>MainUi.Mirror.ShowMirror</c>/<see cref="MainUi.OpenPanel"/> for the two UI-only steps,
    /// <see cref="CraftedAdvance"/> for the sim-fact ones) — the "scripted 3-day drive" the U7 plan
    /// asks for. Shared by the full-arc test below and <c>QuickTravel_...FunctionalAfter</c>, which
    /// both need a REAL <see cref="TutorialFlow.Completed"/> rather than the old one-tick shortcut.</summary>
    private static void DriveWholeArcToCompletion(MainUi ui)
    {
        DriveDay1ToLookIn(ui);

        ui.Mirror.ShowMirror(); // LookIn -> OpenCounter, via MainUi's real OnMirrorVisibilityChanged hook
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);
        ui.Mirror.CloseMirror();

        // U1 (§11.13): the counter step now completes on the PLAYER's own answer, not the
        // customer's decision — a walk-away (no sale) proves the fix at least as strongly as a
        // scripted sale would.
        AnswerCounterAndAdvance(ui, day: 2);
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

        // U1 (§11.13): Vigil now completes the moment the camp card is SHOWN — the real hook
        // MainUi.SyncCampModal calls the instant the slate appears, independent of which (if any)
        // of the three verbs the player goes on to press.
        ui.Tutorial.NotifyCampCardShown();
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.EveningClose);

        CraftedAdvance(ui, day: 3); // evening closing IS the day rolling over — no event needed
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.MeetHeroes);

        ui.OpenPanel("Tavern"); // MeetHeroes -> Commission, via MainUi's real OpenPanel hook
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Commission);

        CraftedAdvanceWithAction(ui, day: 3, new AcceptCommissionAction(new HeroId(1)));
    }

    [TestCase]
    public void FullThreeDayArc_AdvancesEveryStepOnItsRealEvent_AndHandsHudBackToTheLiveAdvisor()
    {
        var ui = MountMainUi();
        try
        {
            DriveWholeArcToCompletion(ui);

            AssertThat(ui.Tutorial.Completed).IsTrue();
            AssertThat(ui.Tutorial.Active).IsFalse();

            // The final step completes via CraftedAdvanceWithAction, which calls Tutorial.Advance
            // directly (bypassing MainUi's own OnPhaseCompleted->RefreshAll pipeline) — refresh the
            // HUD explicitly so the objective chip actually re-renders against the now-Completed
            // chain, exactly as a real completing tick would.
            ui.RefreshAll();
            var liveAdvisorText = ui.Objective.Reason.Text;
            AssertThat(liveAdvisorText).NotContains("Tutorial");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Checklist_MarksEveryPriorSlotDone_CurrentSlotCurrent_LaterSlotsNeither()
    {
        // U5 (loop-legibility plan): the registry conversion's own regression guard — the
        // checklist is a fresh surface (TutorialFlow.Checklist), not something the pre-U5 suite
        // could have pinned. Proves it tracks a REAL drive, not just a single injected state.
        var ui = MountMainUi();
        try
        {
            var opening = ui.Tutorial.Checklist(ui.Adapter.CurrentState);
            AssertThat(opening.Count).IsEqual(TutorialFlow.TotalSteps);
            AssertThat(opening.Single(r => r.Current).DisplayIndex).IsEqual(1);
            AssertThat(opening.Count(r => r.Done)).IsEqual(0);
            AssertThat(ui.Tutorial.CurrentAnchor.Kind)
                .OverrideFailureMessage("A fresh Day-1 mount's current step has no anchor to point at.")
                .IsNotEqual(TutorialAnchorKind.None);

            DriveDay1ToLookIn(ui); // -> LookIn, display slot 5
            var midChain = ui.Tutorial.Checklist(ui.Adapter.CurrentState);
            var currentRow = midChain.Single(r => r.Current);
            AssertThat(currentRow.DisplayIndex).IsEqual(5);

            foreach (var row in midChain)
            {
                if (row.DisplayIndex < 5)
                {
                    AssertThat(row.Done)
                        .OverrideFailureMessage($"Slot {row.DisplayIndex} should read Done once slot 5 is current.")
                        .IsTrue();
                }
                else if (row.DisplayIndex > 5)
                {
                    AssertThat(row.Done)
                        .OverrideFailureMessage($"Slot {row.DisplayIndex} should not read Done yet.")
                        .IsFalse();
                    AssertThat(row.Current).IsFalse();
                }
            }

            // Every real step in the arc has a concrete on-screen target — the registry itself is
            // pinned by TutorialRegistryConformanceTests; this pins that the LIVE property tracks
            // it correctly through an actual drive, not just at construction.
            AssertThat(ui.Tutorial.CurrentAnchor.Kind).IsNotEqual(TutorialAnchorKind.None);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U5 (§11.14.14, "teaching surfaces render their own copy"): the reported defect was
    /// a completed checklist row showing its tick with no label — a player reviewing what they had
    /// already done would see a column of checkmarks and nothing saying what any of them were for.
    /// Reads the RENDERED tree (<see cref="ObjectiveTracker.TutorialChecklist"/>), not just <see
    /// cref="TutorialFlow.Checklist"/>'s own data (already pinned Done/Current/Neither by <see
    /// cref="Checklist_MarksEveryPriorSlotDone_CurrentSlotCurrent_LaterSlotsNeither"/> above) — a
    /// row's own <see cref="ChecklistRow.Label"/> being non-empty proves nothing about what actually
    /// reaches the screen if the render path ever drops it for a Done row specifically.</summary>
    [TestCase]
    public void Checklist_RendersBothTheGlyphAndTheLabel_ForADoneRow()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui); // -> LookIn, display slot 5; slots 1-4 now read Done
            var doneRow = ui.Tutorial.Checklist(ui.Adapter.CurrentState).First(r => r.Done);

            var rendered = RenderedText(ui.Objective.TutorialChecklist);
            AssertThat(rendered.Contains("✓", System.StringComparison.Ordinal))
                .OverrideFailureMessage("No Done row's glyph reached the rendered checklist at all.")
                .IsTrue();
            AssertThat(rendered.Contains(ObjectiveTracker.Plain(doneRow.Label), System.StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"A Done checklist row (slot {doneRow.DisplayIndex}, \"{doneRow.Label}\") is missing its " +
                    "own label text from the rendered tree — only the glyph would be showing on screen.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── The 2026-08-09 owner report's tutorial half ──────────────────────────────────────────────
    //
    //  "i clicked send them off and it auto jumped to night???? yet this is still on tutorial 5???"
    //  "Tutorial 4 - HOW to watch them depart??"
    //
    // Step 4 named a sight and never the press that produces it; step 5 named a control that Night
    // does not have, and kept naming it after the day had moved on.

    [TestCase]
    public void WatchDepartureStep_NamesTheBellPressThatStartsIt_NotJustTheThingToLookAt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(ScriptedSession.CraftedItem(ui.Adapter.CurrentState), 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.WatchDeparture);

            var copy = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            // U-T2-1: WatchDeparture is The Dark's 2nd of 2 beats (PostBounty, WatchDeparture).
            AssertThat(copy).StartsWith("The Dark · 2/3:");
            AssertThat(copy)
                .OverrideFailureMessage(
                    $"Step 4 must name the press, not only the sight — the owner's question was literally " +
                    $"\"HOW to watch them depart??\". Copy was: \"{copy}\"")
                .Contains("Send them off");
            AssertThat(copy)
                .OverrideFailureMessage("Step 4 should still say where to look once the bell is rung.")
                .Contains("Mine Gate");

            // ...and the words it tells the player to hunt for are the words actually on the button.
            AssertThat(Find<Button>(ui, "AdvancePhase").Text).IsEqual("Send them off");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LookInStep_WithNobodyInTheMine_StopsNamingAWatchButtonThatIsNotOnScreen()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);

            // While a party IS out, the step names the control — which is on screen.
            AssertThat(Find<Button>(ui, "WatchButton").Visible).IsTrue();
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!).Contains("Watch");

            // Hurry the day past it (the player's own legal skip) and the control goes away with the
            // party. The chain must move on rather than sit on a step naming a button Night does not
            // have — the "still on tutorial 5" half of the report.
            for (var guard = 0; guard < 8 && ui.Conductor.Current != RaidConductor.Beat.Idle; guard++)
            {
                ui.Conductor.Hurry();
            }

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
            AssertThat(Find<Button>(ui, "WatchButton").Visible).IsFalse();
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "The day reached Night and the chain was still sitting on step 5, pointing at a Watch " +
                    "control that only exists while a party is out.")
                .IsEqual(TutorialStep.OpenCounter);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LookInStep_ResumedOutsideTheRaidWindow_ReadsAsAWaitNeverAsABlankCard()
    {
        // The one way LookIn can still be current with no party out: Step is persisted (U5), so a
        // campaign quit mid-chain and continued the next Morning lands exactly here. The copy must
        // say what is true and name the way back — never point at the missing button, and never
        // (WaitText's old `_ => string.Empty` fallthrough) render an empty tutorial card.
        //
        // Read through the same "hand a modified state to a pure method" idiom CraftedAdvance uses:
        // TopSlotText/Checklist are pure projections of whatever state they are given.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.LookIn);

            var resumedAtMorning = ui.Adapter.CurrentState with { Day = 2, Phase = DayPhase.Morning };
            var copy = ui.Tutorial.TopSlotText(resumedAtMorning)!;

            AssertThat(string.IsNullOrWhiteSpace(copy))
                .OverrideFailureMessage("The tutorial card rendered BLANK — the surface whose whole job is saying what to do.")
                .IsFalse();
            // U21 (§11.14.14): LookIn stays in Dark — on day 1 nothing is proved yet, so a Proof
            // heading here would lie. See the registry row's own comment.
            // chapter heading instead of sharing Dark's with the send-off.
            AssertThat(copy).StartsWith("The Dark · 3/3:");
            AssertThat(copy)
                .OverrideFailureMessage($"The step is still naming the Watch control with nobody out. Copy was: \"{copy}\"")
                .NotContains("👁 Watch");
            AssertThat(copy)
                .OverrideFailureMessage("The wait copy must name the way back into the raid window.")
                .Contains("Send them off");

            var row = ui.Tutorial.Checklist(resumedAtMorning).Single(r => r.Current);
            AssertThat(row.GatingNote)
                .OverrideFailureMessage("A gated current step owes the checklist a short reason.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LookInStep_CompletesOnlyOnMirrorShown_NeverBeforeItIsCurrent()
    {
        var ui = MountMainUi();
        try
        {
            // Showing the Mirror before day 1 has even reached LookIn must be a no-op — the step
            // machine, not the Mirror's own visibility, decides what counts.
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            ui.Mirror.CloseMirror();

            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Opening the Scrying Mirror while LookIn was current did not advance the chain.")
                .IsEqual(TutorialStep.OpenCounter);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U1 (tutorial-revamp plan §11.13): steps 6/7 re-gated onto what the player caused ───────
    //
    // "Tutorial 6 doesn't make sense. explain WHY and what is happening. do i press 'Snuff the
    // lanterns?' i have no idea." / "Tutorial 7 makes no sense. Why the fuck are we talking about
    // camp when we were just selling something?"

    [TestCase]
    public void Step6_Completes_WhenTheCustomerWalks_AfterAGenuinePresent()
    {
        // The exact case that stalled the owner: a player who opens the counter, presents
        // something, and gets walked on (no CounterSaleClosed anywhere in this state at all) must
        // still advance. The old predicate required the customer's OWN accept, which the customer
        // can refuse forever. U-T2-14 tightened the OTHER direction too (Open+Close ALONE, with no
        // Present/Suggest/Haggle between them, no longer completes — see
        // Step6_DoesNotComplete_OnOpenThenCloseAlone_WithNoAnswerBetween below) — Present-then-walk
        // is the least favorable case that still counts as a genuine answer.
        //
        // U-T2-16 also dropped OpenCounter's own MinDay from 2 to 1 (the real gate was always the
        // counter's own Morning-only legality, never the calendar) — proven here on DAY 1 itself,
        // the moment the party's own day-1 send-off has opened the step at all, rather than the OLD
        // "must wait for Day 2" shape this test used to pin.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            CraftedAdvanceWithActions(
                ui, day: 1, new OpenCounterAction(), new PresentItemAction(new ItemId(1)), new CloseCounterAction());
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "The customer walked after a genuine Present on DAY 1 (no CounterSaleClosed event exists " +
                    "anywhere in this state) and the step did not advance — a player who does everything right " +
                    "and gets no sale would watch this step repeat itself forever, which is the owner's exact " +
                    "complaint, and U-T2-16 dropped MinDay to 1 specifically so this need not wait for Day 2.")
                .IsEqual(TutorialStep.Vigil);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step6_AlsoCompletes_OnAClosedSale()
    {
        // The happy path, unchanged: Present -> Accept still counts as an answer.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();

            CraftedAdvanceWithActions(
                ui, day: 2, new OpenCounterAction(), new PresentItemAction(new ItemId(1)),
                new HaggleResponseAction(HaggleResponseKind.Accept));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step6_DoesNotComplete_OnOpenAlone()
    {
        // Opening and abandoning — no Present/Suggest/Haggle/Close ever submitted — is not an
        // answer: the player has to actually DO something with the customer, even if that
        // something is closing the counter on them.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();

            CraftedAdvanceWithActions(ui, day: 2, new OpenCounterAction());
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Opening the counter alone, with no answer at all, completed the step.")
                .IsEqual(TutorialStep.OpenCounter);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U-T2-14 (#162 defect 1) — the CORE regression this unit exists to fix: "Open the counter,
    /// close it, the step ticks. The player can complete the game's flagship channel without
    /// hearing a want, presenting an item, or haggling once." Before this fix,
    /// <c>Step6_Completes_WhenTheCustomerWalks_AfterAGenuinePresent</c>'s OWN Open+Close-only body
    /// (with no Present in between) already proved this step complete — this test pins the OPPOSITE
    /// claim directly: Open then Close, nothing between them, must NOT complete the step.
    /// </summary>
    [TestCase]
    public void Step6_DoesNotComplete_OnOpenThenCloseAlone_WithNoAnswerBetween()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            CraftedAdvanceWithActions(ui, day: 2, new OpenCounterAction(), new CloseCounterAction());
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "Opening the counter and closing it again — with no Present/Suggest/Haggle between them — " +
                    "completed the step. This is #162 defect 1: the flagship channel completable by opening " +
                    "and closing a door.")
                .IsEqual(TutorialStep.OpenCounter);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U2 (tutorial-revamp plan, §11.13): the exact case that stalled the owner — the customer's
    /// want is fixed before the player shows anything, and on a starter shelf a hero who WALKS with
    /// nothing bought is the modal case, not bad luck. The step must complete on this outcome, not
    /// only on a closed sale (Test scenario 2 from the unit's own brief). Kept alongside
    /// <see cref="Step6_Completes_WhenTheCustomerWalks_AfterAGenuinePresent"/> (same claim, proven
    /// the other way — a realistic EventLog with CustomerApproached/CustomerWalked rather than a
    /// bare ActionLog construction) rather than merged into it: carries an <see
    /// cref="OpenCounterAction"/> AND a <see cref="PresentItemAction"/> in the ActionLog batch,
    /// which <c>TutorialFlow.CounterAnsweredAtLeastOnce</c> (U-T2-14 tightened it) requires.
    /// </summary>
    [TestCase]
    public void OpenCounterStep_Completes_WhenTheCustomerWalks_WithoutASale()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            var state = ui.Adapter.CurrentState;
            var walked = state with
            {
                Day = 2,
                EventLog = state.EventLog
                    .Add(new CustomerApproached(new HeroId(1)))
                    .Add(new CustomerWalked(new HeroId(1), null, "no want met")),
                ActionLog = state.ActionLog.Add(
                    new LoggedBatch(2, state.Phase,
                        ImmutableList.Create<PlayerAction>(
                            new OpenCounterAction(), new PresentItemAction(new ItemId(1)), new CloseCounterAction()))),
            };
            ui.Tutorial.Advance(walked);

            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "A hero walking with nothing bought, after a genuine Present, did not complete OpenCounter.")
                .IsEqual(TutorialStep.Vigil);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The happy path still works too (Test scenario 3 from the unit's own brief) — same
    /// relationship to <see cref="Step6_AlsoCompletes_OnAClosedSale"/> as the walk-away test above
    /// has to its own sibling.</summary>
    [TestCase]
    public void OpenCounterStep_AlsoCompletes_OnAClosedSale()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();

            var state = ui.Adapter.CurrentState;
            var sold = state with
            {
                Day = 2,
                EventLog = state.EventLog
                    .Add(new CustomerApproached(new HeroId(1)))
                    .Add(new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false)),
                ActionLog = state.ActionLog.Add(
                    new LoggedBatch(2, state.Phase,
                        ImmutableList.Create<PlayerAction>(
                            new OpenCounterAction(), new PresentItemAction(new ItemId(1)), new CloseCounterAction()))),
            };
            ui.Tutorial.Advance(sold);

            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U-T2-14's own named trap: <see cref="PresentItemAction"/>/<see cref="SuggestItemAction"/>
    /// both need a shelved item, and <see cref="HaggleResponseAction"/> needs a standing offer only
    /// Present/Suggest ever create (<c>CounterHandlers</c>'s own guard) — so a player who opens the
    /// counter with an EMPTY shelf has no legal way to answer the customer at all, and the tightened
    /// <c>CounterAnsweredAtLeastOnce</c> would otherwise strand them here silently (no more free pass
    /// on Open+Close alone). The checklist's gating note must say so BEFORE the player presses Open
    /// Counter, never after.
    /// </summary>
    [TestCase]
    public void OpenCounterStep_GatingNote_WarnsOfTheEmptyShelfTrap_NeverSilently()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui); // stocks one item along the way
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            var stillStocked = ui.Adapter.CurrentState with { Day = 2, Phase = DayPhase.Morning };
            AssertThat(stillStocked.Player.Shelf.Count)
                .OverrideFailureMessage("Test setup drifted — DriveDay1ToLookIn should have shelved one item.")
                .IsGreaterEqual(1);
            AssertThat(ui.Tutorial.Checklist(stillStocked).Single(r => r.Current).GatingNote)
                .OverrideFailureMessage("A stocked shelf should carry no gating note at all in the Morning.")
                .IsNull();

            var emptyShelf = stillStocked with { Player = stillStocked.Player with { Shelf = ImmutableList<ShelfEntry>.Empty } };
            var row = ui.Tutorial.Checklist(emptyShelf).Single(r => r.Current);
            AssertThat(row.GatingNote)
                .OverrideFailureMessage(
                    "An empty shelf leaves the player able to open the counter with no legal way to answer it, " +
                    "and no gating note warned them before they walked in.")
                .IsNotNull();
            AssertThat(row.GatingNote!.Contains("shelf", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The gating note does not mention the shelf at all: \"{row.GatingNote}\"")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step7_Completes_OnSeeingTheCampCard_WithNoVerbPressed()
    {
        // Vigil completes the moment the winch-house slate is actually SHOWN — not on a specific
        // verb, and never on whether a party ever camps at all (staging a stop is the UNCOMMON
        // case, RaidConductor's own doc).
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            // The real hook MainUi.SyncCampModal calls the instant CampPanel.ShowModal fires — no
            // Send/Recall/SendDeeper press anywhere in this test.
            ui.Tutorial.NotifyCampCardShown();

            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Seeing the camp card alone did not advance Vigil.")
                .IsEqual(TutorialStep.EveningClose);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U2 (tutorial-revamp plan, §11.13): the same claim as
    /// <see cref="Step7_Completes_OnSeeingTheCampCard_WithNoVerbPressed"/>, reached via
    /// <see cref="CraftedAdvanceOpenCounterComplete"/> (fixed, see that helper's own doc) instead
    /// of <see cref="AnswerCounterAndAdvance"/> — kept as independent coverage rather than merged
    /// into its sibling.
    /// </summary>
    [TestCase]
    public void VigilStep_CompletesOnSeeingTheCampCard_WithNoVerbPressed()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            CraftedAdvanceOpenCounterComplete(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            // Mirrors MainUi.SyncCampModal's own call site — fired the instant CampPanel.ShowModal
            // is, before any of the three camp verbs could ever be pressed.
            ui.Tutorial.NotifyCampCardShown();

            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Seeing the camp card did not complete Vigil — completion still waits on a specific verb.")
                .IsEqual(TutorialStep.EveningClose);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step7_Completes_OnSendDeeper()
    {
        // "Send them deeper" (CampPanel.SendDeeperRequested) leaves no durable sim fact at all — no
        // PlayerAction, no GameEvent, wired straight to RaidConductor.ResolveVigil. The card must
        // always be shown before the button is even reachable, so NotifyCampCardShown (fired the
        // instant the real slate appears) is what actually completes Vigil; this pins that
        // pressing the REAL button afterward changes nothing about that — the fix does not depend
        // on knowing which of the three verbs (if any) the player goes on to choose.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            // Mirrors what MainUi.SyncCampModal does together the instant a party parks.
            ui.Camp.ShowModal();
            ui.Tutorial.NotifyCampCardShown();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.EveningClose);

            PressEnabled(ui, "CampDeeper");
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Pressing Send Deeper after the card had already been answered moved the step again.")
                .IsEqual(TutorialStep.EveningClose);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step7_GatingNote_SaysNoStopIsComing_WhenEveryPartyTargetsFloor1()
    {
        // Read straight off MusterPlan.Compute — a fresh roster's first trip always targets floor 1
        // (DeepestFloorReached starts at 0), so the honest answer on an ordinary early day is "no
        // stop coming," never a promise the vigil "opens" once some day number arrives.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            var row = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.Current);
            AssertThat(row.GatingNote)
                .OverrideFailureMessage($"Vigil's gating note on a floor-1-only day should say no stop is coming, was: \"{row.GatingNote}\"")
                .Contains("No stop today");

            var copy = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            AssertThat(copy).Contains("No stop today");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U2: regression pin on the exact defect the owner's playtest caught — "it opens once Day 2
    /// begins" stated a day gate for a condition (whether today's muster even reaches the
    /// checkpoint) that was never about the day at all. A quiet world (no heroes at all — MusterPlan
    /// forms no parties) must read as "not today", never as a day promise. Kept alongside its
    /// sibling above as a second angle on the same claim: this one reads the checklist row's
    /// GatingNote directly against an emptied-out Heroes/Bounties state, rather than TopSlotText
    /// against the live campaign.
    /// </summary>
    [TestCase]
    public void VigilStep_GatingNote_SaysNoPartyIsStaged_WhenEveryPartyTargetsFloor1()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            CraftedAdvanceOpenCounterComplete(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            var quiet = ui.Adapter.CurrentState with
            {
                Heroes = ImmutableSortedDictionary<int, Hero>.Empty,
                Bounties = ImmutableList<Bounty>.Empty,
            };
            var row = ui.Tutorial.Checklist(quiet).Single(r => r.Current);

            AssertThat(row.GatingNote)
                .OverrideFailureMessage("Vigil's checklist row carried no gating note for a day nobody is staged to camp.")
                .IsNotNull();
            AssertThat(row.GatingNote!.Contains("Day", System.StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"Step7_NeverClaimsADayGate regression: Vigil's gating note still names a day gate — \"{row.GatingNote}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step7_NeverClaimsADayGate_ForAConditionThatIsNotDayGated()
    {
        // Regression pin on the exact lie: the old copy said "the vigil is a Day 2 lesson... it
        // opens once Day 2 begins" — a day gate for a condition (a party staging a stop) that is
        // NOT day-gated at all. Day 1's own copy must read the SAME muster truth as any later day.
        var ui = MountMainUi();
        try
        {
            var day1 = ui.Adapter.CurrentState;
            var waitCopy = ObjectiveTracker.Plain(ui.Tutorial.CopyFor(TutorialStep.Vigil, day1));
            AssertThat(waitCopy.Contains("opens once Day", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage(
                    $"Vigil's wait copy still promises a day gate for a condition that is not day-gated: \"{waitCopy}\"")
                .IsFalse();
            AssertThat(waitCopy)
                .OverrideFailureMessage($"Vigil's day-1 copy should read the same muster truth as any other day: \"{waitCopy}\"")
                .Contains("No stop today");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Design-collision note (merge of §11.13 U1 + U2/U3): this test was originally named
    /// "..._GatedToDay2" and asserted that a SupplyDelivered on Day 1 must NOT complete Vigil — a
    /// premise that assumed Vigil's own MinDay was still 2. The merge keeps U2/U3's MinDay 2→1 (the
    /// real precondition was never the day at all — AnyPartyStagedForCheckpointToday's own doc), so
    /// Day 1 no longer blocks anything here; a hand-built Day-1 SupplyDelivered would now correctly
    /// complete the step, and asserting otherwise would just be pinning a stale, rejected design.
    /// What still matters, and is still worth pinning, is that the DAY ROLLING OVER alone (with no
    /// SupplyDelivered/PartyRecalled fact at all) never completes it — see
    /// <see cref="ChainReachesMeetHeroes_OnDay3_WhenNoPartyEverCamped"/> for the day-1-shaped half
    /// of that same claim already covered as a byproduct there. This test now checks it directly.
    /// </summary>
    [TestCase]
    public void VigilStep_CompletesOnSupplyDelivered_NotOnTheDayAlone()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            CraftedAdvance(ui, day: 2); // no SupplyDelivered/PartyRecalled event at all
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Advancing with no SupplyDelivered/PartyRecalled event completed Vigil — IsDone is checking the wrong fact.")
                .IsEqual(TutorialStep.Vigil);

            CraftedAdvance(ui, day: 2, new SupplyDelivered(new HeroId(1), new ItemId(1), 9));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.EveningClose);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void VigilStep_AlsoCompletesOnPartyRecalled_NotOnlyOnSupplySent()
    {
        // The vigil's own copy offers TWO verbs ("send a supply or ring the recall bell") — a
        // mutation that dropped the PartyRecalled half of the OR would still pass the sibling test
        // above (which only ever exercises SupplyDelivered); this one exists to catch exactly that.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            CraftedAdvance(ui, day: 2, new PartyRecalled(ImmutableList.Create(new HeroId(1))));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.EveningClose);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveningCloseStep_CompletesWhenDayRollsToThree_NoEventNeeded()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            CraftedAdvance(ui, day: 2, new SupplyDelivered(new HeroId(1), new ItemId(1), 9));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.EveningClose);

            CraftedAdvance(ui, day: 2); // still day 2 — evening has not closed yet
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("EveningClose advanced before day 3 arrived.")
                .IsEqual(TutorialStep.EveningClose);

            CraftedAdvance(ui, day: 3);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.MeetHeroes);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void MeetHeroesStep_CompletesOnTavernOrHeroCardsOpened_NotOnAnUnrelatedPanel()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            CraftedAdvance(ui, day: 2, new SupplyDelivered(new HeroId(1), new ItemId(1), 9));
            CraftedAdvance(ui, day: 3);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.MeetHeroes);

            ui.OpenPanel("Forge"); // an unrelated panel must not advance a step it isn't for
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Opening an unrelated panel (Forge) advanced MeetHeroes.")
                .IsEqual(TutorialStep.MeetHeroes);

            ui.OpenPanel("HeroCards");
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Opening Hero Cards did not advance MeetHeroes.")
                .IsEqual(TutorialStep.Commission);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CommissionStep_CompletesOnAcceptOrDecline_GatedToDay3_AndEndsTheChain()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            CraftedAdvance(ui, day: 2, new SupplyDelivered(new HeroId(1), new ItemId(1), 9));
            CraftedAdvance(ui, day: 3);
            ui.OpenPanel("Tavern");
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Commission);

            CraftedAdvanceWithAction(ui, day: 2, new DeclineCommissionAction(new HeroId(1)));
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("A commission decision on Day 2 completed the Day-3 final step.")
                .IsFalse();

            CraftedAdvanceWithAction(ui, day: 3, new DeclineCommissionAction(new HeroId(1)));
            AssertThat(ui.Tutorial.Completed).IsTrue();
            AssertThat(ui.Tutorial.QuickTravelUnlocked).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Backstop_ClosesTheChain_EvenWhenAUiOnlyStepIsNeverPerformed()
    {
        // LookIn/MeetHeroes key off UI navigation Advance() cannot see at all — a player (or a
        // driven test, like TutorialAllProfessionsTests) who never opens the Mirror must still not
        // be stranded forever. Day 1 -> LookIn for real, then never touch the Mirror again.
        //
        // U-T2-2: the chain's own backstop is now day 8 (ChainBackstopDay), not day 4 — the split
        // that separated "the warrant ends" from "the chain force-closes" (see that unit's own doc).
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);

            CraftedAdvance(ui, day: 7);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("The backstop fired before its own grace day.")
                .IsFalse();

            CraftedAdvance(ui, day: 8);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("Stuck on LookIn (Mirror never opened) past the backstop day — the chain never closed.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U-T2 Wave E ("the HUD chips including quick travel, which unlocks silently today",
    /// the long tail): the tick that flips <see cref="TutorialFlow.Completed"/> — and with it
    /// <see cref="TutorialFlow.QuickTravelUnlocked"/> — true must teach the player a new HUD chip
    /// just appeared. Reuses the exact backstop-completion fixture above; a real
    /// <see cref="SimAdapter.AdvancePhase"/> after <see cref="CraftedAdvance"/> flips
    /// <c>Tutorial.Completed</c> is what actually runs <c>MainUi.RefreshHud</c> (<c>CraftedAdvance</c>
    /// itself calls <see cref="TutorialFlow.Advance"/> directly, bypassing the HUD tick entirely).</summary>
    [TestCase]
    public void TutorialCompleting_TeachesThatQuickTravelJustUnlocked()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            CraftedAdvance(ui, day: 8);
            AssertThat(ui.Tutorial.Completed).IsTrue();

            // Clear whatever unrelated lesson DriveDay1ToLookIn's own real ticks may have already
            // shown (e.g. the mark-read lesson) -- this test is about quick-travel specifically, not
            // about which lesson happens to be on screen first.
            ui.Mentor.Dismiss();

            ui.Adapter.AdvancePhase(); // the real tick that runs MainUi.RefreshHud

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Quick travel unlocked silently -- no lesson showed on the tick Completed flipped true.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("quick-travel");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U1 (§11.13) superseded this unit's own earlier fix: the Vigil step's own completion fact
    /// (<c>SupplyDelivered</c>/<c>PartyRecalled</c>) can only ever fire if a party actually parks —
    /// and <c>ExpeditionSystem.CheckpointFor</c> means EVERY hero's first-ever trip (day 1, and any
    /// later day where nobody has yet cleared floor 1) is structurally unstaged, so a real campaign
    /// can plausibly reach the Vigil step's Day-2 gate and still never see the slate open. The OLD
    /// fix rode the generic backstop (now ChainBackstopDay, day 8) all the way to Completed with
    /// nothing in between — riding silently past Vigil, EveningClose, MeetHeroes and Commission in
    /// one jump. The chain now moves
    /// past an unanswered Vigil the moment Day 3 arrives (EveningClose's own AdvanceFrom, the SAME
    /// unconditional-sweep idiom WatchDeparture already uses across day 1), so it no longer needs to
    /// wait that long at all.
    /// </summary>
    [TestCase]
    public void ChainReachesMeetHeroes_OnDay3_WhenNoPartyEverCamped()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror(); // LookIn -> OpenCounter
            AnswerCounterAndAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            // Day 2 arrives, the Vigil step is current — and NO SupplyDelivered/PartyRecalled event
            // ever fires, and NotifyCampCardShown is never called (the real-world case whenever
            // nobody parks). Advancing the day alone must not complete it early...
            CraftedAdvance(ui, day: 2);
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("The day rolling over completed the Vigil step without its own event — IsDone is checking the wrong fact.")
                .IsEqual(TutorialStep.Vigil);
            AssertThat(ui.Tutorial.Completed).IsFalse();

            // ...but Day 3 must carry an unanswered Vigil straight through to MeetHeroes rather than
            // stranding it until ChainBackstopDay's much later, much blunter close.
            CraftedAdvance(ui, day: 3);
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "Day 3 arrived and an unanswered Vigil did not move past it — the chain would otherwise " +
                    "ride the silent ChainBackstopDay=8 close all the way to Completed with nothing in between.")
                .IsEqual(TutorialStep.MeetHeroes);
            AssertThat(ui.Tutorial.Completed).IsFalse(); // MeetHeroes/Commission still need their own answer
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SkippedRow_RendersTheDidntComeUpState_NeverAFalseTick()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            CraftedAdvance(ui, day: 3); // Vigil never answered -> the anti-stranding sweep carries it past
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.MeetHeroes);

            var vigilRow = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.DisplayIndex == 7);
            AssertThat(vigilRow.Skipped)
                .OverrideFailureMessage("An unanswered Vigil row that the sweep carried the chain past should read Skipped.")
                .IsTrue();
            AssertThat(vigilRow.Done)
                .OverrideFailureMessage("A skipped row must not ALSO read Done — that is the false tick this unit exists to stop.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AnsweredVigilRow_ReadsDone_NeverSkipped_OnceTheChainMovesOn()
    {
        // The other direction of the same guard: a genuinely answered Vigil (the card was shown)
        // must read as an ordinary completed row, never as Skipped.
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AnswerCounterAndAdvance(ui, day: 2);
            ui.Tutorial.NotifyCampCardShown();
            CraftedAdvance(ui, day: 3);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.MeetHeroes);

            var vigilRow = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.DisplayIndex == 7);
            AssertThat(vigilRow.Skipped).IsFalse();
            AssertThat(vigilRow.Done).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SecondProfessionAffordance_AbsentBeforeMilestone_PresentAfter_SubmittingYieldsTwoProfessions()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.SecondProfessionButton.Visible).IsFalse();

            // The milestone (class doc: first BountyPaid) is a persistent state fact — injected
            // directly rather than simulating a full accept-and-return cycle, mirroring
            // DayAdvanceHudTests.GuaranteedSaleState's own fixture-injection convention.
            var paidBounty = new Bounty(new BountyId(1), TargetFloor: 1, RewardGold: 5,
                PostedOnDay: 1, AcceptedBy: null, Paid: true);
            ui.Tutorial.RefreshAffordances(ui.Adapter.CurrentState with
            {
                Bounties = ImmutableList.Create(paidBounty),
            });

            AssertThat(ui.Tutorial.SecondProfessionButton.Visible).IsTrue();
            AssertThat(TutorialFlow.SecondProfessionMilestoneReached(
                ui.Adapter.CurrentState with { Bounties = ImmutableList.Create(paidBounty) })).IsTrue();

            var before = ui.Adapter.CurrentState.Player.SelectedProfessions;
            AssertThat(before.Count).IsEqual(1);
            var second = TanningProfession.Id;
            AssertThat(before.Contains(second)).IsFalse();

            string? picked = null;
            ui.Tutorial.SecondProfessionPicked += id => picked = id;
            var button = Find<Button>(ui.Tutorial.ProfessionPicker, $"SecondProfession_{second}");
            button.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(picked).IsEqual(second);

            ui.Adapter.AdvancePhase(); // the queued SetProfessionsAction lands this tick
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions.Count).IsEqual(2);
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions.Contains(second)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void QuickTravel_AbsentBeforeCompletion_FunctionalAfter()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.QuickTravelUnlocked).IsFalse();
            ui.QuickTravel("Forge");
            AssertThat(ui.Town.InteriorActive).IsFalse(); // locked — no-op before completion

            // U7: the real 3-day path to Completed — DriveWholeArcToCompletion's own tests already
            // pin every intermediate step, so this only needs the end state.
            DriveWholeArcToCompletion(ui);
            AssertThat(ui.Tutorial.Completed).IsTrue();

            // The final step completes via CraftedAdvanceWithAction, bypassing MainUi's own
            // RefreshAll — refresh explicitly so QuickTravelRow's visibility (driven by
            // RefreshAffordances) reflects the now-Completed chain.
            ui.RefreshAll();
            AssertThat(ui.Tutorial.QuickTravelUnlocked).IsTrue();
            AssertThat(ui.Tutorial.QuickTravelRow.Visible).IsTrue();

            // DriveWholeArcToCompletion's own Commission step opens the Tavern drawer
            // (ui.OpenPanel("Tavern")) and never closes it — that is correct behavior for THAT
            // step, but it leaves an unrelated drawer open here. Close it explicitly so the
            // assertions below test quick-travel's OWN effect from a clean slate, not whatever the
            // arc happened to leave on screen.
            ui.Drawer.Close();

            ui.QuickTravel("Forge");
            // U1 (painted-interiors plan): quick-travel now enters the walkable forge room, same
            // as a walked arrival would (content parity, R9) — no drawer opens directly.
            AssertThat(ui.Town.InteriorActive).IsTrue();
            AssertThat(ui.Town.InteriorVenueKey).IsEqual("forge");
            AssertThat(ui.Drawer.IsOpen).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Step1Copy_NamesTheForge_AndIncludesAMovementHint()
    {
        // Playtest F6: the very first line a player ever sees must say WHERE to go (the Forge)
        // and HOW to get there (walk/click-to-move) — not just the raw "buy 2 copper" verb.
        var ui = MountMainUi();
        try
        {
            var text = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            AssertThat(text).StartsWith("The Mark · 1/1:");
            AssertThat(text).Contains("Forge");
            AssertThat(text).Contains("WASD");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PostBountyStep_NamesTheBountiesBoard_NotTheGate_AndAcknowledgesArrival()
    {
        // Pre-existing dead end fixed in passing (StepBuilding's own doc): the step used to send the
        // player to the "Gate" building, which opens the Depths panel (the mine), not the bounty
        // board — bounties live at the separately labelled "Bounties" noticeboard. A player who
        // followed the old copy literally landed on the wrong panel and could never satisfy this
        // step from there.
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            var craftedItem = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            ui.Adapter.Queue(new StockAction(craftedItem, 50));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.PostBounty);

            var closed = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState, openPanelId: null)!;
            AssertThat(closed)
                .OverrideFailureMessage($"PostBounty copy still names the Gate (the mine), not the bounty board: \"{closed}\"")
                .Contains("Bounties");
            AssertThat(closed).NotContains("mine gate");

            // Standing at the REAL panel this step means (per MainUi.OnTownBuildingClicked,
            // "noticeboard"/"Bounties" -> "Bounties") must read as arrival, not as still walking.
            var atBoard = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState, openPanelId: "Bounties")!;
            AssertThat(atBoard)
                .OverrideFailureMessage($"Standing at the Bounties board is not acknowledged: \"{atBoard}\"")
                .Contains("You're at");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BuyMaterialStep_PhaseForbidsTheVendor_ShowsWaitVariant()
    {
        // Playtest F6's core complaint: on a fresh campaign the player can drift into Expedition
        // without ever buying (nothing forces it), and the Morning-only vendor is now closed —
        // the step must swap to a "come back" variant instead of repeating an impossible instruction.
        //
        // Truncated to the Expedition tick (this test used to keep cycling all the way to day 2's
        // own Morning to prove the vendor reopens): the VERY NEXT AdvancePhase call is day 1's own
        // party departing, which now completes the whole chain unconditionally (see
        // PartyDeparting_CompletesTheChain_... above) — cycling further would only be asserting on
        // a card that has already gone Active=false, which proves nothing about the vendor's gate.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);

            ui.Adapter.AdvancePhase(); // Morning -> Expedition: nothing queued, vendor now closed
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial); // unchanged: no MaterialPurchased event
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var waitText = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            AssertThat(waitText).StartsWith("The Mark · 1/1:");
            AssertThat(waitText).Contains("Morning");
            AssertThat(waitText).NotContains("Walk to"); // the raw actionable instruction must be gone
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PostBountyStep_PhaseForbidsTheBoard_ShowsWaitVariant()
    {
        // Same F6 gap on the bounty board: it only takes postings Morning-or-Evening
        // (ActionLegality.IsLegal), so the Expedition window right after Morning must show the
        // deferred variant instead of the raw "walk to the Gate" instruction.
        //
        // Truncated to the Expedition tick — see BuyMaterialStep_..._ShowsWaitVariant's own note:
        // the VERY NEXT AdvancePhase call is day 1's own party departing, which now completes the
        // chain unconditionally, so there is no later "Evening reopens the board" moment left to
        // observe within this same day.
        var ui = MountMainUi();
        try
        {
            // Reach PostBounty without ever posting: buy+craft+stock in one Morning batch.
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.PostBounty);

            var duringExpedition = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            // U21 (§11.14.14): The Dark now totals 2 beats (PostBounty, WatchDeparture) — LookIn
            AssertThat(duringExpedition).StartsWith("The Dark · 1/3:");
            AssertThat(duringExpedition).Contains("Morning or Evening");
            AssertThat(duringExpedition).NotContains("Walk to");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Root-cause fix: owner playtest, verbatim — "The tutorial is missing?" ──────────────────
    //
    // TutorialFlow.Load() runs unconditionally on EVERY MainUi mount, New Game and Continue
    // alike, because a same-campaign restart is supposed to keep exactly the Dismissed/Completed/
    // Step it left with — Dismiss_MidChain_PersistsAndNeverReprompts_AfterRemount above proves
    // that on purpose. Nothing distinguished "reload the SAME campaign" from "start a genuinely
    // NEW one": user://tutorial_flow.json outlives every campaign, so a tutorial finished or
    // dismissed on any earlier run (including one abandoned mid-session) silently suppressed the
    // WHOLE chain — Active=false, no on-screen sign why — on every New Game after it. That is
    // "the tutorial is missing": correct-looking code, gated by a flag from a campaign the player
    // can no longer see. The fix: NewGameSelect.OnBeginPressed now calls
    // TutorialFlow.ResetForNewGame() in the same spot it already calls CampaignSave.Clear().
    //
    // These two tests drive the REAL front door (NewGameSelect -> New Game -> Pick -> Begin), not
    // a direct GameComposition.NewCampaign call — the bug lived in that wiring, not in
    // TutorialFlow's own step machine, so a test that bypasses NewGameSelect cannot see it.

    private static NewGameSelect MountNewGameSelect()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen);
        return screen;
    }

    private static void UnmountNewGameSelect(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null; // never leak a picked campaign into a later suite
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>Same path <c>TutorialFlow.SavePath</c> writes to (private there) — hardcoded here
    /// the same way <c>shot_harness.gd</c> already does, and the same raw-JSON-write idiom
    /// <c>NewGameSelectTests</c> uses for a hand-built <c>CampaignSave</c> envelope.</summary>
    private const string TutorialSavePath = "user://tutorial_flow.json";

    [TestCase("blacksmith")]
    [TestCase("tanning")]
    [TestCase("engineering")]
    [TestCase("alchemy")]
    public void NewGame_AfterAnEarlierCampaignDismissedTheTutorial_Step1StillShowsUp_OnTheOrdinaryStartingScreen(
        string professionId)
    {
        // Simulate the owner's actual situation: an EARLIER campaign on this install dismissed the
        // tutorial. A bare, untethered TutorialFlow instance writes to the SAME shared user://
        // file real play uses (TutorialRegistryConformanceTests.MidTutorialProgress_..."s own
        // "second independent instance reading the same file" idiom, used here to WRITE instead).
        var stale = new TutorialFlow();
        stale.Build();
        try
        {
            stale.Dismiss();
        }
        finally
        {
            stale.Free();
        }

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { }; // never tear down the test tree (NewGameSelectTests precedent)
        try
        {
            Press(screen, "NewGame");
            Press(screen, $"Pick_{professionId}");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            AssertThat(adapter)
                .OverrideFailureMessage($"{professionId}: Begin did not build a campaign — this test proves nothing.")
                .IsNotNull();

            var ui = MountMainUi(adapter);
            try
            {
                AssertThat(ui.Tutorial.Dismissed)
                    .OverrideFailureMessage(
                        $"{professionId}: New Game inherited a PRIOR campaign's DISMISSED tutorial flag — " +
                        "exactly the owner's report, \"The tutorial is missing?\": correct-looking code, " +
                        "suppressed by a stale user:// flag from a campaign the player can no longer see.")
                    .IsFalse();
                AssertThat(ui.Tutorial.Completed).IsFalse();
                AssertThat(ui.Tutorial.Active).IsTrue();
                AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);

                // "The ordinary starting view... without the player having to open anything."
                AssertThat(ui.Drawer.IsOpen).IsFalse();
                AssertThat(ui.Town.InteriorActive).IsFalse();

                AssertThat(ui.Objective.Reason.Text)
                    .OverrideFailureMessage(
                        $"{professionId}: the tutorial's first step is not readable on screen in the ordinary " +
                        $"starting view. Rendered: \"{ui.Objective.Reason.Text}\"")
                    .StartsWith("The Mark · 1/1:");

                var tracker = ui.FindChild("ObjectiveTracker", recursive: true, owned: false) as Control;
                AssertThat(tracker)
                    .OverrideFailureMessage("No ObjectiveTracker node in the tree at all.")
                    .IsNotNull();
                AssertThat(tracker!.IsVisibleInTree())
                    .OverrideFailureMessage("The tutorial's text exists but the tracker rendering it is not visible.")
                    .IsTrue();
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }
    }

    [TestCase]
    public void NewGame_AfterAnEarlierCampaignCompletedTheWholeChain_Step1StillShowsUp_NotHiddenAsDone()
    {
        // The OTHER persisted terminal flag (Completed, distinct from Dismissed — class doc) —
        // a fix that only cleared one would still leave this half of the bug live. Written as raw
        // JSON (Complete() itself is private) matching TutorialFlow.PersistedData's shape; Step
        // serializes as its underlying int (System.Text.Json default for an enum with no
        // converter) — Commission is the terminal step, the exact state a finished 3-day chain
        // would have saved.
        using (var file = Godot.FileAccess.Open(TutorialSavePath, Godot.FileAccess.ModeFlags.Write))
        {
            file.StoreString(
                $"{{\"Completed\":true,\"Dismissed\":false,\"HasSeenLedgerTip\":false,\"Step\":{(int)TutorialStep.Commission}}}");
        }

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            AssertThat(adapter)
                .OverrideFailureMessage("Begin did not build a campaign — this test proves nothing.")
                .IsNotNull();

            var ui = MountMainUi(adapter);
            try
            {
                AssertThat(ui.Tutorial.Completed)
                    .OverrideFailureMessage(
                        "New Game inherited a PRIOR campaign's COMPLETED tutorial flag — the owner's exact " +
                        "report (\"the tutorial is missing\") for the Completed half of the persisted state, " +
                        "not just Dismissed.")
                    .IsFalse();
                AssertThat(ui.Tutorial.Active).IsTrue();
                AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
                AssertThat(ui.Objective.Reason.Text).StartsWith("The Mark · 1/1:");
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }
    }

    // ── U8a (loop-legibility plan): the general profession-switch surface, ProgressionPanel's own
    // "YOUR PROFESSIONS" header. TutorialFlow's own picker (SecondProfessionAffordance_... above)
    // is add-only and gated behind a milestone + "fewer than 2 selected" — this is the fix for the
    // gap that leaves: once a save already has 2 professions (exactly what that picker nudges a
    // player into), there is no other path to change either one, ever. These tests drive the NEW
    // surface directly through ProgressionPanel's real Controls, never GameSim.Professions state. ──

    /// <summary>Scenarios 1, 5, 6 (U8a brief) in one round trip: the surface exists and works in a
    /// state that has moved past the tutorial entirely (<see cref="DriveWholeArcToCompletion"/>,
    /// this file's own real-completion helper), a genuine click reaches the kernel
    /// (<see cref="PressEnabled"/> — fails loudly if Confirm ever renders Disabled here), and the
    /// switch is judged by the ACTUAL craftable set afterward, not merely by whether the submit was
    /// accepted.</summary>
    [TestCase]
    public void ChangeProfessionsSurface_ReachableAfterTutorialCompletes_RealConfirmClick_ChangesCraftableRecipes()
    {
        var ui = MountMainUi();
        try
        {
            DriveWholeArcToCompletion(ui);
            AssertThat(ui.Tutorial.Completed).IsTrue();

            // Starting point: blacksmith only (PlayerState.NewGame's own default) — the Forge lists
            // blacksmith's dagger and nothing from tanning.
            ui.OpenPanel("Forge");

            // U-T1-10: pick the two tanning recipes by whether the profession's OWN TierGate covers
            // their tier, not by alphabetical order.
            //
            // This test used to take `Recipes.Keys.OrderBy(Ordinal).First()`, which is
            // "tanning-dragonhide-armor" — TIER 3, the highest gate in the game, purely by accident of
            // spelling. It passed only because ForgePanel rendered a full five-button card for every
            // recipe regardless of tier, while `CraftingHandlers.ApplyCraft` had ALWAYS rejected a
            // tier-gated craft. So the old assertion was green over a real defect: it proved a card
            // existed whose Craft button the sim would refuse. U-T1-10 is the fix, and it is what turned
            // this test red — correctly.
            //
            // Deriving from `TierGate` rather than hardcoding "Tier == 1" keeps this honest if the gate
            // table ever moves: whatever the profession says is gated is what must render as a row.
            var tanning = ProfessionRegistry.All[TanningProfession.Id];
            var tanningOpenRecipeId = tanning.Recipes.Values
                .Where(r => !tanning.TierGate.ContainsKey(r.Tier))
                .OrderBy(r => r.RecipeId, System.StringComparer.Ordinal)
                .First().RecipeId;
            var tanningGatedRecipeId = tanning.Recipes.Values
                .Where(r => tanning.TierGate.ContainsKey(r.Tier))
                .OrderBy(r => r.RecipeId, System.StringComparer.Ordinal)
                .First().RecipeId;

            AssertThat(ui.Forge.FindChild($"RecipeCard_{ScriptedSession.CraftRecipeId}", recursive: true, owned: false))
                .OverrideFailureMessage("Precondition failed: blacksmith's own dagger recipe should render before any switch.")
                .IsNotNull();
            AssertThat(ui.Forge.FindChild($"RecipeCard_{tanningOpenRecipeId}", recursive: true, owned: false)).IsNull();

            // Switch blacksmith -> tanning through the general surface (never the tutorial's own
            // add-only picker, which this state has already left behind).
            ui.OpenPanel("Progress");
            Find<Button>(ui.Progress, $"ProfessionToggle_{ProfessionRegistry.BlacksmithId}").ButtonPressed = false;
            Find<Button>(ui.Progress, $"ProfessionToggle_{TanningProfession.Id}").ButtonPressed = true;
            ui.Progress.Refresh(); // recompute Confirm's gate off the freshly-poked toggles, same as a live re-render would

            PressEnabled(ui.Progress, "ConfirmProfessions"); // scenario 6: a real click, not just an enabled flag

            // Still a bell-rider: the submit alone changes nothing yet.
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions)
                .IsEqual(ImmutableSortedSet.Create(ProfessionRegistry.BlacksmithId));

            ui.Adapter.AdvancePhase(); // rings the bell
            AssertThat(ui.Adapter.LastRejections.Count)
                .OverrideFailureMessage("A legal 1-profession pick was rejected at the bell — this proves nothing below.")
                .IsEqual(0);
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions)
                .IsEqual(ImmutableSortedSet.Create(TanningProfession.Id));

            // The actual craftable set, not just the accepted submission (U8a brief, scenario 1).
            ui.OpenPanel("Forge");
            AssertThat(ui.Forge.FindChild($"RecipeCard_{ScriptedSession.CraftRecipeId}", recursive: true, owned: false))
                .OverrideFailureMessage("Blacksmith's dagger recipe still renders after switching away from blacksmith.")
                .IsNull();
            AssertThat(ui.Forge.FindChild($"RecipeCard_{tanningOpenRecipeId}", recursive: true, owned: false))
                .OverrideFailureMessage(
                    $"Tanning's ungated recipe '{tanningOpenRecipeId}' never rendered as a card after "
                    + "switching to tanning — the profession switch did not change the craftable set.")
                .IsNotNull();

            // U-T1-10's other half, asserted here for the first time: a tier-gated recipe is GREYED WITH
            // A NAMED REASON, never hidden (SurfaceUnlocks' doctrine — the player sees what is coming and
            // why it is closed). Both halves in one test, because "the card is gone" and "a row explains
            // why" are separate claims and only the pair of them is the feature.
            AssertThat(ui.Forge.FindChild($"RecipeCard_{tanningGatedRecipeId}", recursive: true, owned: false))
                .OverrideFailureMessage(
                    $"Tanning's tier-gated recipe '{tanningGatedRecipeId}' rendered a full five-button "
                    + "card. Its Craft button would be an enabled control the sim then rejects — the "
                    + "exact defect U-T1-10 exists to close.")
                .IsNull();
            AssertThat(ui.Forge.FindChild($"Locked_{tanningGatedRecipeId}", recursive: true, owned: false))
                .OverrideFailureMessage(
                    $"Tanning's tier-gated recipe '{tanningGatedRecipeId}' rendered NEITHER a card nor a "
                    + "locked row — it was hidden outright, which is the one thing the greyed-with-a-reason "
                    + "doctrine forbids.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Scenario 2 (U8a brief): Confirm submits a real <see cref="SetProfessionsAction"/>,
    /// which is a bell-rider (<c>ActionTiming.ResolvesImmediately</c> false) — it must land on the
    /// bell tray under its existing <see cref="PendingVerbVocab"/> string, the same tray/vocab every
    /// other bell-rider uses (<c>BellTrayTests</c> covers the vocab table itself; this proves THIS
    /// surface's submit path actually reaches it).</summary>
    [TestCase]
    public void ConfirmProfessions_QueuesAsABellRider_AndAddsTheExistingTrayChip()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Progress");
            Find<Button>(ui.Progress, $"ProfessionToggle_{TanningProfession.Id}").ButtonPressed = true;
            ui.Progress.Refresh();

            var tray = Find<HBoxContainer>(ui, "BellTray");
            AssertThat(tray.GetChildCount()).IsEqual(0);

            PressEnabled(ui.Progress, "ConfirmProfessions");

            AssertThat(tray.GetChildCount()).IsEqual(1);
            var chip = tray.GetChild(0);
            AssertThat(Find<Label>(chip, "Verb").Text)
                .IsEqual(PendingVerbVocab.DisplayName(new SetProfessionsAction(ImmutableSortedSet.Create("blacksmith"))));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Scenario 3 (U8a brief): 3 professions is out of range
    /// (<c>ProfessionHandlers.MaxSelected</c> = 2) — Confirm must render Disabled (the KEY
    /// CONSTRAINT's enabled-state parity with <c>ActionLegality.SetProfessionsLegal</c>), and a
    /// forced press (CampPanel.OnSend's own "a real click can't reach a Disabled button, but this
    /// suite's Press deliberately bypasses that" precedent) must still resolve through the kernel's
    /// OWN typed rejection at the bell, changing nothing.</summary>
    [TestCase]
    public void ConfirmProfessions_ThreeSelected_ConfirmDisabled_ForcedSubmitRejectedByTheKernel_NoStateChange()
    {
        var ui = MountMainUi();
        try
        {
            var before = ui.Adapter.CurrentState.Player.SelectedProfessions;
            ui.OpenPanel("Progress");
            Find<Button>(ui.Progress, $"ProfessionToggle_{ProfessionRegistry.BlacksmithId}").ButtonPressed = true;
            Find<Button>(ui.Progress, $"ProfessionToggle_{TanningProfession.Id}").ButtonPressed = true;
            Find<Button>(ui.Progress, $"ProfessionToggle_{EngineeringProfession.Id}").ButtonPressed = true;
            ui.Progress.Refresh();

            AssertThat(Find<Button>(ui.Progress, "ConfirmProfessions").Disabled)
                .OverrideFailureMessage("Confirm must mirror ActionLegality.SetProfessionsLegal and disable on a 3-profession pick.")
                .IsTrue();

            Press(ui.Progress, "ConfirmProfessions");
            ui.Adapter.AdvancePhase();

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            AssertThat(ui.Adapter.LastRejections[0].Reason).StartsWith("Cannot select more than");
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions).IsEqual(before);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Scenario 4 (U8a brief): the same contract as the 3-profession case, at the other
    /// edge — 0 professions selected.</summary>
    [TestCase]
    public void ConfirmProfessions_ZeroSelected_ConfirmDisabled_ForcedSubmitRejectedByTheKernel_NoStateChange()
    {
        var ui = MountMainUi();
        try
        {
            var before = ui.Adapter.CurrentState.Player.SelectedProfessions;
            ui.OpenPanel("Progress");
            Find<Button>(ui.Progress, $"ProfessionToggle_{ProfessionRegistry.BlacksmithId}").ButtonPressed = false;
            ui.Progress.Refresh();

            AssertThat(Find<Button>(ui.Progress, "ConfirmProfessions").Disabled)
                .OverrideFailureMessage("Confirm must mirror ActionLegality.SetProfessionsLegal and disable on a 0-profession pick.")
                .IsTrue();

            Press(ui.Progress, "ConfirmProfessions");
            ui.Adapter.AdvancePhase();

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            AssertThat(ui.Adapter.LastRejections[0].Reason).StartsWith("Must select at least one profession");
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions).IsEqual(before);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── §11.13 amendment (U5/U6): the apprenticeship warrant's dawn beat, the graduation confirm's
    // own copy, and the dormant loss act's lifecycle ─────────────────────────────────────────────

    [TestCase]
    public void WarrantEndBeat_NeverFires_BeforeItsOwnDay()
    {
        var ui = MountMainUi(); // fresh campaign, day 1 — well before LastGraceDay + 1
        try
        {
            AssertThat(ui.Tutorial.ConsumeWarrantEndBeat(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Root cause of a real failure this test used to have: mounting DIRECTLY at
    /// Day == LastGraceDay + 1 lets MainUi's own boot sequence (BuildUi's unconditional
    /// RefreshHud -> RefreshSurfaceUnlocks -> ConsumeWarrantEndBeat call) consume the once-ever
    /// beat before this test's own explicit call ever ran — a real save loaded for the first time
    /// already past the threshold SHOULD fire the beat immediately at boot (the same "first time
    /// this session sees it" contract ConsumeLedgerTip already has), so that is not a bug to
    /// suppress. The fix is test isolation, not product code: mount BEFORE the threshold (the
    /// default fresh campaign, day 1 — boot's own call is legitimately a no-op there) and hand the
    /// later-day state directly to the method under test, so this test proves ConsumeWarrantEndBeat's
    /// OWN once-ever contract without racing the exact wiring the day-1 sibling test above already
    /// covers.
    /// </summary>
    [TestCase]
    public void WarrantEndBeat_FiresOnceEver_OnTheFirstMorningAfterTheWarrant()
    {
        var ui = MountMainUi(); // fresh campaign, day 1 — boot's own RefreshHud call is a no-op here
        try
        {
            var afterWarrant = ui.Adapter.CurrentState with { Day = ApprenticeWarrant.LastGraceDay + 1 };
            var beat = ui.Tutorial.ConsumeWarrantEndBeat(afterWarrant);
            AssertThat(beat).IsNotNull();
            AssertThat(beat!).Contains("warrant ended at dawn");

            AssertThat(ui.Tutorial.ConsumeWarrantEndBeat(afterWarrant)).IsNull(); // once-ever
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// An early graduate already heard the news in <see cref="TutorialFlow.DismissConfirmCopy"/>'s
    /// own confirm at press time — the dawn beat must stay silent so the tutorial's own voice never
    /// restates news the player already answered. Mounted BEFORE the threshold day, same isolation
    /// as the sibling test above — this test used to mount already-past-threshold too, which meant
    /// MainUi's own boot-time RefreshHud call consumed the beat before Queue(Conclude) even ran, so
    /// it was passing without ever actually exercising the Concluded branch it claims to pin.
    /// </summary>
    [TestCase]
    public void WarrantEndBeat_NeverFires_AfterAnEarlyGraduation()
    {
        var ui = MountMainUi(); // fresh campaign, day 1 — boot's own RefreshHud call is a no-op here
        try
        {
            ui.Adapter.Queue(new ConcludeApprenticeshipAction());
            var afterWarrant = ui.Adapter.CurrentState with { Day = ApprenticeWarrant.LastGraceDay + 1 };
            AssertThat(ui.Tutorial.ConsumeWarrantEndBeat(afterWarrant)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DismissConfirmCopy_NamesTheWarrant_WhileItHolds()
    {
        var state = GameComposition.NewCampaign(ScriptedSession.Seed) with { Day = 1 };
        AssertThat(TutorialFlow.DismissConfirmCopy(state)).Contains("the Mine keeps what it takes");
    }

    /// <summary>Law 7 read honestly: the confirm must never state a cost the sim would not
    /// actually charge (§11.4's own rule, applied to this specific dialog).</summary>
    [TestCase]
    public void DismissConfirmCopy_CarriesNoMortalityClause_AfterTheWarrantEnded()
    {
        var state = GameComposition.NewCampaign(ScriptedSession.Seed) with { Day = ApprenticeWarrant.LastGraceDay + 1 };
        AssertThat(TutorialFlow.DismissConfirmCopy(state)).NotContains("Mine keeps what it takes");
    }

    /// <summary>Pressing the ✕ arms the confirm row rather than dismissing on the spot — "no timers
    /// on decisions" (law): nothing happens until the player's own second press.</summary>
    [TestCase]
    public void DismissButton_ArmsTheConfirmRow_RatherThanDismissingImmediately()
    {
        var ui = MountMainUi();
        try
        {
            Press(ui.Objective, "ObjectiveTutorialDismiss");

            AssertThat(ui.Tutorial.Dismissed)
                .OverrideFailureMessage("The ✕ dismissed the chain on ONE press — it must arm a confirm instead.")
                .IsFalse();
            AssertThat(Find<Control>(ui.Objective, "ObjectiveTutorialDismissConfirm").Visible).IsTrue();

            Press(ui.Objective, "ObjectiveTutorialDismissConfirmYes");

            AssertThat(ui.Tutorial.Dismissed).IsTrue();
            AssertThat(ApprenticeWarrant.Concluded(ui.Adapter.CurrentState))
                .OverrideFailureMessage("Confirming must submit ConcludeApprenticeshipAction atomically alongside Dismiss().")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DismissConfirm_KeepGoing_NeitherDismissesNorConcludes()
    {
        var ui = MountMainUi();
        try
        {
            Press(ui.Objective, "ObjectiveTutorialDismiss");
            Press(ui.Objective, "ObjectiveTutorialDismissConfirmNo");

            AssertThat(ui.Tutorial.Dismissed).IsFalse();
            AssertThat(ApprenticeWarrant.Concluded(ui.Adapter.CurrentState)).IsFalse();
            AssertThat(Find<Control>(ui.Objective, "ObjectiveTutorialDismissConfirm").Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static GameState HeroDiedState(int day) => GameComposition.NewCampaign(ScriptedSession.Seed) with
    {
        Day = day,
        EventLog = ImmutableList.Create<GameEvent>(
            new HeroDied(new HeroId(1), Floor: 1, Cause: "slain by a Crypt Crab", WornGear: GearSet.Empty)),
    };

    [TestCase]
    public void LossAct_RendersNothing_WhileArmed()
    {
        var ui = MountMainUi(); // no death has ever happened
        try
        {
            AssertThat(ui.Tutorial.LossActRow(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LossAct_NeverWakes_WhenTheChainWasDismissed()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5)));
        try
        {
            ui.Tutorial.Dismiss();
            AssertThat(ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState)).IsNull();
            AssertThat(ui.Tutorial.LossActRow(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LossStep_CompletesOnHonorMemorialAction_APlayerCausedDurableFact()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5)));
        try
        {
            ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);
            var row = ui.Tutorial.LossActRow(ui.Adapter.CurrentState);
            AssertThat(row).IsNotNull();
            AssertThat(row!.Value.Done).IsFalse();

            ui.Adapter.Queue(new HonorMemorialAction(new HeroId(1)));

            var afterHonor = ui.Tutorial.LossActRow(ui.Adapter.CurrentState);
            AssertThat(afterHonor).IsNotNull();
            AssertThat(afterHonor!.Value.Done).IsTrue();
            AssertThat(afterHonor.Value.Skipped).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"One night, one day, then an honest retire" (KTD-H) — never done, the row shows
    /// Skipped on the day after, then vanishes entirely at the second dawn (never a false tick).</summary>
    [TestCase]
    public void LossStep_RetiresAtTheSecondDawn_AsSkipped_NeverAFalseTick()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5)));
        try
        {
            ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState); // arms at day 5

            var nightOf = ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 5 });
            AssertThat(nightOf).IsNotNull();
            AssertThat(nightOf!.Value.Skipped).IsFalse();

            var dayAfter = ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 6 });
            AssertThat(dayAfter)
                .OverrideFailureMessage("Never honored by the day after — must render Skipped, not vanish silently.")
                .IsNotNull();
            AssertThat(dayAfter!.Value.Skipped).IsTrue();
            AssertThat(dayAfter.Value.Done).IsFalse();

            var secondDawn = ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 7 });
            AssertThat(secondDawn)
                .OverrideFailureMessage("The row must retire (vanish) at the second dawn — KTD-H's anti-nag rule.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The tutorial owns the FIRST loss only — a second death raises no NEW tutorial
    /// surface, whatever day it lands on.</summary>
    [TestCase]
    public void SecondDeath_RaisesNoTutorialSurface()
    {
        var firstDeathState = HeroDiedState(day: 5);
        var ui = MountMainUi(new SimAdapter(firstDeathState));
        try
        {
            ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);

            var secondDeathState = firstDeathState with
            {
                Day = 20,
                EventLog = firstDeathState.EventLog.Add(
                    new HeroDied(new HeroId(2), Floor: 2, Cause: "slain by a Cave Rat", WornGear: GearSet.Empty)),
            };
            AssertThat(ui.Tutorial.ConsumeFirstLossBlock(secondDeathState)).IsNull();
            AssertThat(ui.Tutorial.LossActRow(secondDeathState)).IsNull(); // day 20 is long past the first loss's own window
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LossLessonText_IsReadableInLessons_AfterTheActCloses()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5)));
        try
        {
            AssertThat(ui.Tutorial.LossLessonText).IsNull();

            ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);
            AssertThat(ui.Tutorial.LossLessonText).IsNotNull();

            // Still readable long after the two-day checklist window closed (KTD-H: the Lessons
            // book, unlike the checklist row, never retires).
            var afterRetire = ui.Adapter.CurrentState with { Day = 40 };
            AssertThat(ui.Tutorial.LossActRow(afterRetire)).IsNull();
            AssertThat(ui.Tutorial.LossLessonText).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U29 (§11.14.14, R21/R22): the two-act-voice-per-night budget ────────────────────────────
    //
    // ResolveTonightsActVoices is a pure static allocator (no GameState, no clock, no persisted
    // read) — most of the tests below call it directly rather than driving a mounted campaign
    // through several in-game days, the same "measure the mechanism, not the whole harness" shape
    // ActVoiceBudgetCensusTests (sim/GameSim.Tests/Presentation) already uses for the census this
    // unit answers. The two REAL dormant acts today (HeroDeath/ConsumeFirstLossBlock,
    // WarrantEnded/ConsumeWarrantEndBeat) are covered separately, further down, against the real
    // mounted campaign.

    /// <summary>Every pairwise comparison the enum's own declared order claims, proven against the
    /// ALLOCATOR (<c>budget: 1</c> forces a single winner out of exactly two candidates) rather than
    /// re-reading the enum's declaration a second time — "not implied by list order" (the task's own
    /// phrase): a reordering of the enum that broke one of these six lines would be caught here even
    /// if some OTHER test happened to eyeball the full list and see nothing obviously wrong.</summary>
    [TestCase]
    public void Precedence_EachAdjacentPair_HigherRankWinsTheOnlySlot()
    {
        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.HeroDeath, TutorialFlow.ActVoiceKind.Proof], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.HeroDeath);

        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.Proof, TutorialFlow.ActVoiceKind.Graduation], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.Proof);

        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.Graduation, TutorialFlow.ActVoiceKind.WarrantEnded], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.Graduation);

        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.WarrantEnded, TutorialFlow.ActVoiceKind.ActAdvance], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.WarrantEnded);

        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.ActAdvance, TutorialFlow.ActVoiceKind.CommissionFulfilled], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.ActAdvance);

        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.CommissionFulfilled, TutorialFlow.ActVoiceKind.RankUp], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.CommissionFulfilled);
    }

    /// <summary>R21's own corollary, and the one plain top-2-by-precedence would get wrong: a death
    /// and the proof never share a night, even though both fit the real budget of two. Proof loses
    /// its slot to the exclusion, not to a numbers crunch — this is what distinguishes the corollary
    /// from ordinary precedence, and <see cref="Precedence_EachAdjacentPair_HigherRankWinsTheOnlySlot"/>
    /// above (budget 1) cannot exercise it, since that pair would resolve the same way either way.</summary>
    [TestCase]
    public void DeathAndProof_NeverShareANight_EvenThoughBothWouldFitTheBudget()
    {
        var resolved = TutorialFlow.ResolveTonightsActVoices(
            [TutorialFlow.ActVoiceKind.HeroDeath, TutorialFlow.ActVoiceKind.Proof]); // real budget: 2

        AssertThat(resolved).Contains(TutorialFlow.ActVoiceKind.HeroDeath);
        AssertThat(resolved)
            .OverrideFailureMessage("R21's own corollary: death and the proof never share a night.")
            .NotContains(TutorialFlow.ActVoiceKind.Proof);
        AssertThat(resolved.Count).IsEqual(1); // the freed slot is NOT silently handed to anyone else
    }

    /// <summary>
    /// The measured worst night, verbatim from the census this unit answers
    /// (<c>ActVoiceBudgetCensusTests</c>' own FINDING doc, sim/GameSim.Tests/Presentation): day 4
    /// carries the attribution beat, a fulfilled commission and the Act II advance on every one of
    /// twelve seeds, the warrant's end (deterministically day 4 — <see cref="WarrantEndDay"/>), and
    /// on 4 of 12 seeds the campaign's first hero death too — five candidates. R21's own approach
    /// text: "this unit therefore has to defer three voices on the worst night, not one." Delivers
    /// the death first (highest precedence), the warrant's end second (proof excluded by the death
    /// corollary, so the warrant's end is the strongest of what remains), and defers the other three
    /// — proof included, and explicitly checked absent rather than merely "not the only survivor.".
    /// </summary>
    [TestCase]
    public void WorstMeasuredNight_DeathAndWarrantEndSpeak_ProofAndTwoOthersDefer()
    {
        TutorialFlow.ActVoiceKind[] worstNight =
        [
            TutorialFlow.ActVoiceKind.HeroDeath,
            TutorialFlow.ActVoiceKind.Proof,
            TutorialFlow.ActVoiceKind.WarrantEnded,
            TutorialFlow.ActVoiceKind.ActAdvance,
            TutorialFlow.ActVoiceKind.CommissionFulfilled,
        ];

        var speaksTonight = TutorialFlow.ResolveTonightsActVoices(worstNight);

        AssertThat(speaksTonight).ContainsExactly(
            TutorialFlow.ActVoiceKind.HeroDeath, TutorialFlow.ActVoiceKind.WarrantEnded);

        var deferred = worstNight.Where(k => !speaksTonight.Contains(k)).ToList();
        AssertThat(deferred.Count)
            .OverrideFailureMessage("R21's own approach text: defer THREE on the worst night, not one.")
            .IsEqual(3);
        AssertThat(deferred).Contains(TutorialFlow.ActVoiceKind.Proof);
        AssertThat(deferred).Contains(TutorialFlow.ActVoiceKind.ActAdvance);
        AssertThat(deferred).Contains(TutorialFlow.ActVoiceKind.CommissionFulfilled);
    }

    /// <summary>No beat is ever lost, and none arrives twice: simulate the worst night's own
    /// cascade across successive in-game nights the way a real campaign would — a kind that loses
    /// its slot stays a candidate (a dormant act's own Consume method never commits its arm-day
    /// field on a loss, see <c>ConsumeFirstLossBlock</c>/<c>ConsumeWarrantEndBeat</c>'s own remarks),
    /// so it re-competes fresh the next night rather than vanishing or re-queueing behind a growing
    /// backlog. Every one of the seven kinds is accounted for in EXACTLY one night by the end, and
    /// the budget is never exceeded on any single night along the way.</summary>
    [TestCase]
    public void NoBeatIsEverLost_TheWorstNightsCascadeAccountsForEveryKindExactlyOnce()
    {
        var waiting = new HashSet<TutorialFlow.ActVoiceKind>(Enum.GetValues<TutorialFlow.ActVoiceKind>());
        var spoken = new HashSet<TutorialFlow.ActVoiceKind>();

        for (var night = 1; waiting.Count > 0; night++)
        {
            AssertThat(night)
                .OverrideFailureMessage("Seven candidates at budget 2 must clear within 4 nights -- a beat is stuck.")
                .IsLessEqual(4);

            var tonight = TutorialFlow.ResolveTonightsActVoices(waiting);
            AssertThat(tonight.Count)
                .OverrideFailureMessage($"Night {night} exceeded the two-act-voice budget (R21).")
                .IsLessEqual(2);

            foreach (var kind in tonight)
            {
                // HashSet.Add returns false if the kind is already present -- the direct way to
                // catch "arrived twice" the instant it would happen, not after the fact.
                AssertThat(spoken.Add(kind))
                    .OverrideFailureMessage($"{kind} spoke on more than one simulated night.")
                    .IsTrue();
                waiting.Remove(kind);
            }
        }

        AssertThat(spoken.Count).IsEqual(Enum.GetValues<TutorialFlow.ActVoiceKind>().Length); // none lost
    }

    /// <summary>"Nothing in the deferral path reads a wall clock or expires on one" (law 2): the
    /// allocator is pure, so the SAME candidates must resolve to the SAME answer every single call,
    /// with no dependency on how many times it has been asked before or how much real time passed
    /// between calls.</summary>
    [TestCase]
    public void ResolveTonightsActVoices_IsDeterministic_AcrossRepeatedCalls()
    {
        TutorialFlow.ActVoiceKind[] candidates =
        [
            TutorialFlow.ActVoiceKind.HeroDeath, TutorialFlow.ActVoiceKind.Proof,
            TutorialFlow.ActVoiceKind.WarrantEnded, TutorialFlow.ActVoiceKind.ActAdvance,
        ];

        var first = TutorialFlow.ResolveTonightsActVoices(candidates);
        for (var i = 0; i < 25; i++)
        {
            AssertThat(TutorialFlow.ResolveTonightsActVoices(candidates).SetEquals(first))
                .OverrideFailureMessage("Repeated calls with identical input must return an identical set -- any drift means a hidden clock or counter.")
                .IsTrue();
        }
    }

    /// <summary>The two REAL dormant acts today share a night for real (day 4:
    /// <see cref="ApprenticeWarrant.LastGraceDay"/> + 1 is deterministically 4, and this death also
    /// lands day 4) — budget 2 comfortably fits both, so this is the honest regression check that
    /// wiring the gate into <see cref="ConsumeFirstLossBlock"/>/<see cref="ConsumeWarrantEndBeat"/>
    /// did not suppress a real fact neither one actually needed to lose. Forcing an actual DEFERRAL
    /// of one of these two would need a third real contender, which does not exist until U30-U33
    /// land theirs (see this section's own class doc) — the pure-allocator tests above already prove
    /// the deferral path itself.
    ///
    /// <para>Mounted at day 1 (<see cref="WarrantEndBeat_FiresOnceEver_OnTheFirstMorningAfterTheWarrant"/>'s
    /// own isolation fix) rather than directly at day 4 — <c>MainUi</c>'s own boot sequence calls
    /// <see cref="ConsumeWarrantEndBeat"/> unconditionally, and mounting already past the threshold
    /// lets THAT call consume the once-ever beat before this test's own explicit call ever runs, which
    /// this test would otherwise misread as the budget wrongly dropping it.</para></summary>
    [TestCase]
    public void RealDeathAndRealWarrantEnd_BothFire_TheSameNight_BudgetOfTwoFitsBoth()
    {
        var ui = MountMainUi(); // fresh campaign, day 1 — boot's own ConsumeWarrantEndBeat call is a no-op here
        try
        {
            var state = HeroDiedState(day: ApprenticeWarrant.LastGraceDay + 1);
            var loss = ui.Tutorial.ConsumeFirstLossBlock(state);
            var warrant = ui.Tutorial.ConsumeWarrantEndBeat(state);

            AssertThat(loss).IsNotNull();
            AssertThat(warrant).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// A beat armed late gets its FULL window, never a remainder. <see cref="LossActRow"/>'s own
    /// window is a day-offset read off <c>_firstLossDay</c> — the day <see
    /// cref="ConsumeFirstLossBlock"/> actually COMMITS, not the day the underlying death happened —
    /// so a loss the tutorial does not get a chance to consume until day 6, even though the death
    /// itself is already two days old in the event log, still opens a full two-day window starting
    /// day 6, exactly as if the death had happened that night. This is the load-bearing property
    /// U29's arming rule depends on (a deferred kind commits on whatever LATER day it finally wins
    /// its slot — see <c>ResolveTonightsActVoices</c>'s own doc); the only kind wired to both a real
    /// Consume method AND a real window today is the loss act, so this proves the property directly
    /// against it rather than only against the pure allocator above.
    /// </summary>
    [TestCase]
    public void LossArmedLate_StillGetsTheFullTwoDayWindow_NotARemainder()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 4))); // the death is already "old" in the log
        try
        {
            // Nothing consumed it on day 4 or day 5 (the campaign never ticked through that reveal —
            // stands in for two nights lost to budget contention, until U30-U33 add a real one).
            AssertThat(ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 4 })).IsNull();
            AssertThat(ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 5 })).IsNull();

            var day6 = ui.Adapter.CurrentState with { Day = 6 };
            AssertThat(ui.Tutorial.ConsumeFirstLossBlock(day6)).IsNotNull(); // commits on day 6, not day 4

            var nightOf = ui.Tutorial.LossActRow(day6);
            AssertThat(nightOf).IsNotNull();
            AssertThat(nightOf!.Value.Skipped).IsFalse(); // day 6: full window, night one

            var dayAfter = ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 7 });
            AssertThat(dayAfter).IsNotNull(); // day 7: still inside the FULL window, night two
            AssertThat(dayAfter!.Value.Skipped).IsTrue(); // never honored -- day two reads Skipped

            AssertThat(ui.Tutorial.LossActRow(ui.Adapter.CurrentState with { Day = 8 }))
                .OverrideFailureMessage("A window armed on day 6 must retire on day 8 (day 6 + 2), not any earlier -- a remainder window would retire sooner.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>No beat is lost, and none arrives twice, across a quit and reload. Nothing about a
    /// deferred beat is committed to <c>user://tutorial_flow.json</c> until the moment it actually
    /// wins a slot (<c>ConsumeFirstLossBlock</c>/<c>ConsumeWarrantEndBeat</c> only call <c>Save()</c>
    /// on the branch that fires) — so a quit mid-deferral persists nothing new at all, and the sim's
    /// own save (the death event, the day counter) is the only durable record, exactly as it already
    /// was before this unit. A fresh <see cref="TutorialFlow"/> loading that same sim save simply
    /// re-asks the identical question fresh and, once it wins a slot, fires the once-ever text
    /// exactly one time — this reconstructs a fresh instance mid-test to prove the reload path
    /// itself, rather than only trusting that Save/Load were never called.</summary>
    [TestCase]
    public void DeferredLoss_SurvivesAQuitAndReload_AndStillFiresExactlyOnce()
    {
        TutorialFlow.DeleteForTests(); // start from a clean user://tutorial_flow.json, same as Unmount leaves it
        var state = HeroDiedState(day: 5);

        // Simulates a quit before this campaign's tutorial ever got a chance to consume the death
        // (the sim itself has already durably logged it) -- nothing for THIS class to persist yet,
        // since ConsumeFirstLossBlock was never called.
        var reloaded = new TutorialFlow();
        reloaded.Build();
        reloaded.Load(); // no _firstLossDay written yet -- reads back as 0, same as a fresh campaign

        try
        {
            AssertThat(reloaded.LossActRow(state)).IsNull(); // not armed yet

            var firstFire = reloaded.ConsumeFirstLossBlock(state);
            AssertThat(firstFire).IsNotNull();
            AssertThat(reloaded.ConsumeFirstLossBlock(state)).IsNull(); // once-ever, same instance

            // A SECOND reload (the "quit again right after" case) must not re-fire the text either --
            // Save() inside ConsumeFirstLossBlock already persisted _firstLossDay by the time it fired.
            var reloadedAgain = new TutorialFlow();
            reloadedAgain.Build();
            reloadedAgain.Load();
            try
            {
                AssertThat(reloadedAgain.ConsumeFirstLossBlock(state)).IsNull();
            }
            finally
            {
                reloadedAgain.Free();
            }
        }
        finally
        {
            reloaded.Free();
            TutorialFlow.DeleteForTests(); // leave a clean file for the next test, same discipline as Unmount
        }
    }

    // ── U30 (§11.14.14): the Proof act's dormant row ─────────────────────────────────────────────

    private static GameState AttributionBeatState(int day) => GameComposition.NewCampaign(ScriptedSession.Seed) with
    {
        Day = day,
        EventLog = ImmutableList.Create<GameEvent>(
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), Floor: 1, Detail: "test beat")),
    };

    [TestCase]
    public void ProofBeat_RendersNothing_UntilTheFirstAttributionBeat()
    {
        var ui = MountMainUi(); // no beat has ever happened
        try
        {
            AssertThat(ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState)).IsNull();
            AssertThat(ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState)).IsNull();
            AssertThat(ui.Tutorial.ProofLessonText).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ProofBeat_Arms_OnTheFirstAttributionBeatEvent()
    {
        var ui = MountMainUi(new SimAdapter(AttributionBeatState(day: 4)));
        try
        {
            var line = ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState);
            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("sim", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage("Bryn is a townsfolk who has never heard the word \"sim\".")
                .IsFalse();

            var row = ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState);
            AssertThat(row).IsNotNull();
            AssertThat(row!.Value.Done).IsFalse();
            AssertThat(row.Value.Skipped).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The course owns the FIRST beat only — a second one raises no new surface, same
    /// precedent <see cref="SecondDeath_RaisesNoTutorialSurface"/> already set for the loss act.</summary>
    [TestCase]
    public void SecondAttributionBeat_RaisesNoNewProofSurface()
    {
        var firstBeatState = AttributionBeatState(day: 4);
        var ui = MountMainUi(new SimAdapter(firstBeatState));
        try
        {
            ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState);

            var secondBeatState = firstBeatState with
            {
                Day = 20,
                EventLog = firstBeatState.EventLog.Add(
                    new AttributionBeatEvent(BeatType.LethalSave, new ItemId(2), new HeroId(2), Floor: 3, Detail: "a second beat")),
            };
            AssertThat(ui.Tutorial.ConsumeProofBeat(secondBeatState)).IsNull();
            AssertThat(ui.Tutorial.ProofBeatRow(secondBeatState)).IsNull(); // day 20 is long past the first beat's own window
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ProofBeatRow_Done_OnceTheLedgerHasBeenOpened()
    {
        var ui = MountMainUi(new SimAdapter(AttributionBeatState(day: 4)));
        try
        {
            ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState);
            var beforeOpen = ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState);
            AssertThat(beforeOpen).IsNotNull();
            AssertThat(beforeOpen!.Value.Done).IsFalse();

            ui.Tutorial.NotifyLedgerOpened();

            var afterOpen = ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState);
            AssertThat(afterOpen).IsNotNull();
            AssertThat(afterOpen!.Value.Done).IsTrue();
            AssertThat(afterOpen.Value.Skipped).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"One night, one day, then an honest retire" (KTD-H) — the identical shape <see
    /// cref="LossStep_RetiresAtTheSecondDawn_AsSkipped_NeverAFalseTick"/> already proves for the
    /// loss act.</summary>
    [TestCase]
    public void ProofBeatRow_RetiresAtTheSecondDawn_AsSkipped_NeverAFalseTick()
    {
        var ui = MountMainUi(new SimAdapter(AttributionBeatState(day: 4)));
        try
        {
            ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState); // arms at day 4, never opened

            var nightOf = ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState with { Day = 4 });
            AssertThat(nightOf).IsNotNull();
            AssertThat(nightOf!.Value.Skipped).IsFalse();

            var dayAfter = ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState with { Day = 5 });
            AssertThat(dayAfter)
                .OverrideFailureMessage("Never opened by the day after — must render Skipped, not vanish silently.")
                .IsNotNull();
            AssertThat(dayAfter!.Value.Skipped).IsTrue();
            AssertThat(dayAfter.Value.Done).IsFalse();

            var secondDawn = ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState with { Day = 6 });
            AssertThat(secondDawn)
                .OverrideFailureMessage("The row must retire (vanish) at the second dawn — KTD-H's anti-nag rule.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ProofLessonText_IsReadableInLessons_AfterTheRowRetires()
    {
        var ui = MountMainUi(new SimAdapter(AttributionBeatState(day: 4)));
        try
        {
            AssertThat(ui.Tutorial.ProofLessonText).IsNull();

            ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState);
            AssertThat(ui.Tutorial.ProofLessonText).IsNotNull();

            var afterRetire = ui.Adapter.CurrentState with { Day = 40 };
            AssertThat(ui.Tutorial.ProofBeatRow(afterRetire)).IsNull();
            AssertThat(ui.Tutorial.ProofLessonText).IsNotNull(); // the lesson never retires
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>KTD5: Bryn asserts the mechanism, never the hero — the beat may land on someone
    /// other than the thread hero, and a line naming the wrong person lies (this class's own hard
    /// rule for <see cref="TutorialFlow.ThreadHero"/>).</summary>
    [TestCase]
    public void ProofBeatCopy_NamesNoHero()
    {
        var ui = MountMainUi(new SimAdapter(AttributionBeatState(day: 4)));
        try
        {
            var line = ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState)!;
            foreach (var hero in ui.Adapter.CurrentState.Heroes.Values)
            {
                AssertThat(line.Contains(hero.Name, StringComparison.Ordinal))
                    .OverrideFailureMessage($"Bryn's proof line named a hero (\"{hero.Name}\") — it must assert the mechanism only.")
                    .IsFalse();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>R21's death/proof exclusion, proven against the REAL Consume methods rather than
    /// only the pure allocator: a night with both a death and the campaign's first attribution beat
    /// speaks the death and defers the proof to the next morning, at its own full window.</summary>
    [TestCase]
    public void Proof_DefersToTheNextMorning_WhenDeathTakesTonightsSlot()
    {
        var bothState = HeroDiedState(day: 4) with
        {
            EventLog = HeroDiedState(day: 4).EventLog.Add(
                new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(2), Floor: 1, Detail: "test beat")),
        };
        var ui = MountMainUi(new SimAdapter(bothState));
        try
        {
            AssertThat(ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState)).IsNotNull(); // death wins the slot
            AssertThat(ui.Tutorial.ConsumeProofBeat(ui.Adapter.CurrentState))
                .OverrideFailureMessage("R21's own corollary: death and the proof never share a night.")
                .IsNull();
            AssertThat(ui.Tutorial.ProofBeatRow(ui.Adapter.CurrentState)).IsNull(); // not armed yet

            var tomorrow = ui.Adapter.CurrentState with { Day = 5 };
            var deferred = ui.Tutorial.ConsumeProofBeat(tomorrow);
            AssertThat(deferred).IsNotNull(); // wins the slot the next night, nothing else contending

            var row = ui.Tutorial.ProofBeatRow(tomorrow);
            AssertThat(row).IsNotNull();
            AssertThat(row!.Value.Skipped)
                .OverrideFailureMessage("A deferred beat must open its own FULL window, never a remainder.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Pure/static — the identical "point at the way in while closed" rule <see
    /// cref="AimAnchor"/> already enforces for every PanelControl row, proven directly against
    /// <see cref="TutorialFlow.ProofBeatAnchor"/> with no campaign needed.</summary>
    [TestCase]
    public void ProofBeatAnchor_PointsAtTheWayIn_WhileTheLedgerIsClosed()
    {
        var closed = TutorialFlow.ProofBeatAnchor(openPanelId: null);
        AssertThat(closed).IsEqual(TutorialAnchor.ForHud("OpenLedger"));
    }

    [TestCase]
    public void ProofBeatAnchor_PointsAtTheBeatCard_WhileTheLedgerIsOpen()
    {
        var open = TutorialFlow.ProofBeatAnchor(openPanelId: "Ledger");
        AssertThat(open).IsEqual(TutorialAnchor.ForPanelControl("Ledger", "LedgerCard_0"));
    }

    // ── U31 (§11.14.14): the loss act gets a voice ───────────────────────────────────────────────

    private static GameState HeroDiedCarryingPlayerWorkState(int day)
    {
        var baseState = GameComposition.NewCampaign(ScriptedSession.Seed);
        var item = new Item(
            new ItemId(9201), "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 3, Defense: 0, Weight: 1), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Day = day,
            Items = baseState.Items.Add(item.Id.Value, item),
            EventLog = ImmutableList.Create<GameEvent>(
                new HeroDied(new HeroId(1), Floor: 1, Cause: "slain by a Crypt Crab",
                    WornGear: GearSet.Empty with { Weapon = item.Id })),
        };
    }

    [TestCase]
    public void LossVoiceLine_IsNull_UntilTheActArms()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5))); // the fact exists, but never consumed
        try
        {
            AssertThat(ui.Tutorial.LossVoiceLine(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>KTD5's split, proven both ways: the variant is chosen on whether the FALLEN hero
    /// carried the player's own work, read off the sim's own recorded <see
    /// cref="HeroDied.WornGear"/> — never a guess.</summary>
    [TestCase]
    public void LossVoiceLine_NamesTheirWork_WhenTheFallenHeroCarriedIt()
    {
        var state = HeroDiedCarryingPlayerWorkState(day: 5);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);
            var line = ui.Tutorial.LossVoiceLine(ui.Adapter.CurrentState);
            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("had your work on them", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LossVoiceLine_SaysNothingOfYours_WhenTheFallenHeroCarriedNone()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5))); // GearSet.Empty — carries nothing at all
        try
        {
            ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);
            var line = ui.Tutorial.LossVoiceLine(ui.Adapter.CurrentState);
            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("Nothing of yours went down with them", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Three hard constraints on Bryn's own line (§11.14.14's own test scenario): no
    /// survival math, no instruction, and pronouns that never hardcode "she"/"he" (no <see
    /// cref="Hero"/> in this sim carries a recorded gender).</summary>
    [TestCase]
    public void LossVoiceLine_CarriesNoSurvivalMathNoInstructionNoGenderedPronoun()
    {
        var carried = HeroDiedCarryingPlayerWorkState(day: 5);
        var carriedUi = MountMainUi(new SimAdapter(carried));
        try
        {
            carriedUi.Tutorial.ConsumeFirstLossBlock(carriedUi.Adapter.CurrentState);
            AssertNoMathNoOrderNoGender(carriedUi.Tutorial.LossVoiceLine(carriedUi.Adapter.CurrentState)!);
        }
        finally
        {
            Unmount(carriedUi);
        }

        var none = HeroDiedState(day: 5);
        var noneUi = MountMainUi(new SimAdapter(none));
        try
        {
            noneUi.Tutorial.ConsumeFirstLossBlock(noneUi.Adapter.CurrentState);
            AssertNoMathNoOrderNoGender(noneUi.Tutorial.LossVoiceLine(noneUi.Adapter.CurrentState)!);
        }
        finally
        {
            Unmount(noneUi);
        }
    }

    private static void AssertNoMathNoOrderNoGender(string line)
    {
        foreach (var digit in "0123456789")
        {
            AssertThat(line.Contains(digit))
                .OverrideFailureMessage($"Bryn's loss line carries a digit (\"{line}\") — no survival math.")
                .IsFalse();
        }

        foreach (var pronoun in new[] { " she ", " he ", " her ", " him ", " his ", "She ", "He " })
        {
            AssertThat(line.Contains(pronoun, StringComparison.Ordinal))
                .OverrideFailureMessage($"Bryn's loss line hardcodes a gendered pronoun (\"{pronoun}\") — no hero here carries a recorded gender.")
                .IsFalse();
        }
    }

    /// <summary>The roster-refills clause is gone — the line used to sit on the most solemn beat
    /// in the game reading like inventory bookkeeping.</summary>
    [TestCase]
    public void FirstLossBlock_NoLongerMentionsTheRosterRefilling()
    {
        var ui = MountMainUi(new SimAdapter(HeroDiedState(day: 5)));
        try
        {
            var block = ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);
            AssertThat(block).IsNotNull();
            AssertThat(block!.Contains("roster refills", StringComparison.OrdinalIgnoreCase)).IsFalse();
            AssertThat(block.Contains("permadeath", StringComparison.OrdinalIgnoreCase)).IsTrue();
            AssertThat(block.Contains("the rite is yours", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
