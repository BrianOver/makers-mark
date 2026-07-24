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
            AssertThat(ui.Objective.Reason.Text).IsEqual(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!);
            AssertThat(ui.Objective.Reason.Text).StartsWith("Tutorial 1/5:");
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
    public void EachStep_AdvancesOnlyOnItsOwnMatchingEvent_AcrossASeparateTickPerStep()
    {
        // Scripted day drive-through (plan test scenario): one action per phase-legal tick so
        // each step's own transition is proven independently rather than a same-tick cascade.
        var ui = MountMainUi();
        try
        {
            // Step 1 (BuyMaterial): buy the recipe's base material — Morning only.
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Craft);

            // Step 2 (Craft): CraftAction is phase-unrestricted (ActionLegality mirror).
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase(); // Expedition -> Camp (this tick ALSO departs day 1's party —
                                       // proves a same-tick PartyDeparted never completes early)
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            AssertThat(ui.Tutorial.Completed).IsFalse();

            // Step 3 (Shelve): StockAction is phase-unrestricted; the transition is a state check
            // (no distinct event), proven by advancing purely off the resulting shelf contents.
            var craftedItem = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            ui.Adapter.Queue(new StockAction(craftedItem, 50));
            ui.Adapter.AdvancePhase(); // Camp -> ExpeditionDeep
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.PostBounty);

            ui.Adapter.AdvancePhase(); // ExpeditionDeep -> Evening (nothing posted yet)
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.PostBounty);

            // Step 4 (PostBounty): Morning-or-Evening only — Evening qualifies.
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Evening -> day 2 Morning
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.WatchDeparture);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
            AssertThat(ui.Tutorial.Completed).IsFalse();

            // Step 5 (WatchDeparture): day 2's own muster departs at ITS Expedition-tick completion.
            ui.Adapter.AdvancePhase(); // day 2 Morning -> Expedition (no departure event yet)
            AssertThat(ui.Tutorial.Completed).IsFalse();
            ui.Adapter.AdvancePhase(); // day 2 Expedition -> Camp: PartyDeparted fires
            AssertThat(ui.Tutorial.Completed).IsTrue();
            AssertThat(ui.Tutorial.Active).IsFalse();
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
            AssertThat(ui.Interior.IsOpen).IsTrue();
            AssertThat(ui.Interior.VenueKey).IsEqual("forge");
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
            AssertThat(text).StartsWith("Tutorial 1/5:");
            AssertThat(text).Contains("Forge");
            AssertThat(text).Contains("WASD");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BuyMaterialStep_PhaseForbidsTheVendor_ShowsWaitVariant_ThenRestoresAndAdvancesNextMorning()
    {
        // Playtest F6's core complaint: on a fresh campaign the player can drift into Expedition
        // without ever buying (nothing forces it), and the Morning-only vendor is now closed —
        // the step must swap to a "come back" variant instead of repeating an impossible instruction.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);

            ui.Adapter.AdvancePhase(); // Morning -> Expedition: nothing queued, vendor now closed
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial); // unchanged: no MaterialPurchased event
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var waitText = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            AssertThat(waitText).StartsWith("Tutorial 1/5:");
            AssertThat(waitText).Contains("Morning");
            AssertThat(waitText).NotContains("Walk to"); // the raw actionable instruction must be gone

            // Cycle the rest of day 1 around to day 2's own Morning — the vendor reopens.
            ui.Adapter.AdvancePhase(); // Expedition -> Camp
            ui.Adapter.AdvancePhase(); // Camp -> ExpeditionDeep
            ui.Adapter.AdvancePhase(); // ExpeditionDeep -> Evening
            ui.Adapter.AdvancePhase(); // Evening -> day 2 Morning
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            var restoredText = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            AssertThat(restoredText).StartsWith("Tutorial 1/5:");
            AssertThat(restoredText).Contains("Forge"); // the actionable copy is back, not the wait copy

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition: MaterialPurchased lands, step advances
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Craft);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PostBountyStep_PhaseForbidsTheBoard_ShowsWaitVariant_ThenActionableAtEveningAndAdvances()
    {
        // Same F6 gap on the bounty board: it only takes postings Morning-or-Evening
        // (ActionLegality.IsLegal), so the Expedition/Camp/ExpeditionDeep window in between must
        // show the deferred variant, then hand back the actionable "walk to the Gate" copy once
        // Evening reopens it.
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
            AssertThat(duringExpedition).StartsWith("Tutorial 4/5:");
            AssertThat(duringExpedition).Contains("Morning or Evening");
            AssertThat(duringExpedition).NotContains("Walk to");

            ui.Adapter.AdvancePhase(); // Expedition -> Camp: still forbidden
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!).NotContains("Walk to");

            ui.Adapter.AdvancePhase(); // Camp -> ExpeditionDeep: still forbidden
            AssertThat(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!).NotContains("Walk to");

            ui.Adapter.AdvancePhase(); // ExpeditionDeep -> Evening: the board reopens
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
            var duringEvening = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!;
            AssertThat(duringEvening).StartsWith("Tutorial 4/5:");
            AssertThat(duringEvening).Contains("Gate");

            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Evening -> day 2 Morning: BountyPosted lands, step advances
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.WatchDeparture);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
