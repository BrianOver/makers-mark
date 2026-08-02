#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
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
            AssertThat(ui.Objective.Reason.Text).StartsWith("Tutorial 1/10:");
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
        // correct. They now share display slot 1 (StepIndex), so the on-screen counter can only
        // ever move by exactly one, whatever combination of Buy/Craft the player actually did.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!).StartsWith("Tutorial 1/10:");

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Craft); // internal step moved...
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!)
                .StartsWith("Tutorial 1/10:"); // ...but the ON-SCREEN number did not (same display slot)

            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!)
                .StartsWith("Tutorial 2/10:"); // exactly one number further, never a jump to 3
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

    [TestCase]
    public void Dismiss_MidChain_PersistsAndNeverReprompts_AfterRemount()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Active).IsTrue();
            Press(ui, "ObjectiveTutorialDismiss");
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

        CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
        AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

        CraftedAdvance(ui, day: 2, new SupplyDelivered(new HeroId(1), new ItemId(1), 9));
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

    [TestCase]
    public void OpenCounterStep_CompletesOnCounterSaleClosed_GatedToDay2_NotDay1()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            // A sale closing while the real day is still 1 (entirely plausible — LookIn itself
            // becomes current on day 1) must NOT instantly complete a Day-2 lesson.
            CraftedAdvance(ui, day: 1, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("A counter sale on Day 1 completed a Day-2 step — StepMinDay gate is missing or wrong.")
                .IsEqual(TutorialStep.OpenCounter);

            CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void VigilStep_CompletesOnSupplyDelivered_GatedToDay2()
    {
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);
            ui.Mirror.ShowMirror();
            CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            CraftedAdvance(ui, day: 1, new SupplyDelivered(new HeroId(1), new ItemId(1), 9));
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("A supply delivery on Day 1 completed a Day-2 step.")
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
            CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
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
            CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
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
            CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
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
            CraftedAdvance(ui, day: 2, new CounterSaleClosed(new HeroId(1), new ItemId(1), 10, Pinned: false));
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
        var ui = MountMainUi();
        try
        {
            DriveDay1ToLookIn(ui);

            CraftedAdvance(ui, day: 3);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("The backstop fired before its own grace day.")
                .IsFalse();

            CraftedAdvance(ui, day: 4);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("Stuck on LookIn (Mirror never opened) past the backstop day — the chain never closed.")
                .IsTrue();
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
            // ShopStageTests.GuaranteedSaleState's own fixture-injection convention.
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
            AssertThat(text).StartsWith("Tutorial 1/10:");
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
            AssertThat(waitText).StartsWith("Tutorial 1/10:");
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
            AssertThat(duringExpedition).StartsWith("Tutorial 3/10:");
            AssertThat(duringExpedition).Contains("Morning or Evening");
            AssertThat(duringExpedition).NotContains("Walk to");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
