#if GDUNIT_TESTS
using System.Collections.Immutable;
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
            AssertThat(ui.Objective.Reason.Text).StartsWith("Tutorial 1/4:");
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
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!).StartsWith("Tutorial 1/4:");

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Craft); // internal step moved...
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!)
                .StartsWith("Tutorial 1/4:"); // ...but the ON-SCREEN number did not (same display slot)

            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!)
                .StartsWith("Tutorial 2/4:"); // exactly one number further, never a jump to 3
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PartyDeparting_CompletesTheChain_EvenWhenShelveAndPostBountyNeverHappened()
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
        // waiting on Shelve. TutorialFlow.Advance's final check is now UNCONDITIONAL on Step for
        // exactly this reason: the party leaving ends the day-1 chain no matter what the card was
        // still asking for.
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
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage(
                    "The party departed but the tutorial did not complete — this is the exact dead " +
                    "end Brian's playtest hit: Shelve/PostBounty never caught up, and a departure " +
                    "event only ever fires on its own tick, so nothing could ever finish this chain " +
                    "from here.")
                .IsTrue();
            AssertThat(ui.Tutorial.Active).IsFalse();
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
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition: the queued post lands NOW, well
                                       // before Craft/Shelve exist.
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("An out-of-order bounty post should not move a step it isn't for yet.")
                .IsEqual(TutorialStep.BuyMaterial);

            // Craft and shelve, same as any normal run — both immediate, no bell needed.
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
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

    [TestCase]
    public void CompletedChain_HandsHudBackToTheLiveAdvisor()
    {
        var ui = MountMainUi();
        try
        {
            // Fastest legal path to Completed: batch all four Morning-legal actions (mirrors
            // BatchedMorningSubmission_... above), then let day 1's own departure land.
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.WatchDeparture);

            ui.Adapter.AdvancePhase(); // Expedition -> Camp: day 1's own PartyDeparted lands
            AssertThat(ui.Tutorial.Completed).IsTrue();

            var liveAdvisorText = ui.Objective.Reason.Text; // Refresh already ran this tick via RefreshAll
            AssertThat(liveAdvisorText).NotContains("Tutorial");
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
            // InteriorStageTests.GuaranteedSaleState's own fixture-injection convention.
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
            AssertThat(ui.Interior.IsOpen).IsFalse(); // locked — no-op before completion

            // Fastest legal path to Completed (mirrors CompletedChain_... above).
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: PartyDeparted completes the chain
            AssertThat(ui.Tutorial.Completed).IsTrue();

            AssertThat(ui.Tutorial.QuickTravelUnlocked).IsTrue();
            AssertThat(ui.Tutorial.QuickTravelRow.Visible).IsTrue();

            ui.QuickTravel("Forge");
            // 2.5D pivot: quick-travel opens the venue's drawer directly now (no staged interior).
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
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
            AssertThat(text).StartsWith("Tutorial 1/4:");
            AssertThat(text).Contains("Forge");
            AssertThat(text).Contains("WASD");
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
            AssertThat(waitText).StartsWith("Tutorial 1/4:");
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
            AssertThat(duringExpedition).StartsWith("Tutorial 3/4:");
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
