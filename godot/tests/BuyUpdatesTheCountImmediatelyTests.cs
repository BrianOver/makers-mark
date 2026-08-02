#if GDUNIT_TESTS
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Pressing Buy must change the numbers on screen, now — not next phase.
///
/// <para><b>Why this exists:</b> Brian's playtest (2026-07-30): "since the crafts are queued
/// (shouldn't be), the material list is confusing as it doesn't update — craft a bulker for 2 copper,
/// because it ticks next cycle, still shows 6 copper". Every player action used to wait for
/// <c>GameKernel.Tick</c>, which is also the only thing that advances the phase, so the shop
/// contradicted the player about what they had just done.</para>
///
/// <para>Workshop verbs now resolve through <c>GameKernel.ApplyNow</c> (see <c>ActionTiming</c> for
/// which, and why). These tests press the REAL button and read the ACTUAL gold, with no
/// <c>AdvancePhase</c> anywhere — the absence of that call is the whole point, so do not add one to
/// make a future assertion pass.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BuyUpdatesTheCountImmediatelyTests
{
    [TestCase]
    public async Task PressingBuy_SpendsTheGoldBeforeAnyPhaseTick()
    {
        var ui = MountMainUi();
        try
        {
            // MainUi owns a Town2D with a live SubViewport; awaiting frames while one renders is the
            // documented gdUnit headless hang.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var goldBefore = ui.Adapter.CurrentState.Player.Gold;
            var phaseBefore = ui.Adapter.CurrentState.Phase;
            var dayBefore = ui.Adapter.CurrentState.Day;

            PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");

            AssertThat(ui.Adapter.CurrentState.Player.Gold)
                .OverrideFailureMessage(
                    $"Gold is still {ui.Adapter.CurrentState.Player.Gold} straight after pressing Buy " +
                    $"(was {goldBefore}), with no phase tick in between. The purchase is sitting in the " +
                    "queue, so the shop is telling the player they still have money they have already " +
                    "spent — the exact confusion reported. Check ActionTiming.ResolvesImmediately and " +
                    "SimAdapter.Queue's immediate branch.")
                .IsLess(goldBefore);

            // The other half of the contract: resolving now must NOT cost the player a phase.
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Buying advanced the phase. ApplyNow must never touch the phase machine.")
                .IsEqual(phaseBefore);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(dayBefore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Spending the day's last action slot must DISABLE the vendor rows, with the reason on the
    /// tooltip — not leave them clickable so the press queues an action the handler then rejects.
    ///
    /// <para><b>Why this exists:</b> found by the 2026-08-02 full playtest, once the harness could
    /// finally see engine distress (#343). The run logged 12 rejected <c>BuyMaterialAction</c>s
    /// reading "No action slots left today (0/5)". The vendor row's gate checked phase and gold but
    /// not the action budget, even though its own comment claimed to mirror
    /// <c>MaterialVendorHandlers</c> — which does enforce it. So in Morning, with gold in hand and
    /// zero slots, the button stayed enabled, the click "succeeded", and the feedback line said
    /// "Queued — resolves when Morning ticks" about an action that was already dead. A dead click
    /// that confirms itself is worse than a disabled one, and <c>BountyPanel</c> already got this
    /// right — this pins that the forge vendor matches it.</para>
    ///
    /// <para>Slots are spent through real presses, not by writing state, so the test fails the same
    /// way a player would discover the bug.</para>
    /// </summary>
    [TestCase]
    public async Task SpendingTheLastActionSlot_DisablesTheVendorRow_WithTheReasonOnIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var buttonName = $"BuyMat_{ScriptedSession.CraftMaterial}";

            // Burn the budget the only way a player can: by actually buying.
            for (var i = 0; i < ActionBudget.SlotsPerDay; i++)
            {
                AssertThat(ui.Adapter.CurrentState.ActionSlotsRemaining)
                    .OverrideFailureMessage(
                        $"Ran out of slots after {i} buys, before the budget of {ActionBudget.SlotsPerDay} " +
                        "was spent — this test can no longer prove what it claims to.")
                    .IsGreater(0);
                PressEnabled(ui.Forge, buttonName);
                await SettleLayout(ui);
            }

            AssertThat(ui.Adapter.CurrentState.ActionSlotsRemaining).IsEqual(0);
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Buying should never advance the phase; the gate under test is Morning-only.")
                .IsEqual(DayPhase.Morning);

            var row = Find<Button>(ui.Forge, buttonName);

            AssertThat(row.Disabled)
                .OverrideFailureMessage(
                    "The vendor Buy button is still enabled with 0 action slots left. A player can click "
                    + "it, get told \"Queued — resolves when Morning ticks\", and have the action silently "
                    + "rejected. ForgePanel's vendor-row `legal` must include ActionSlotsRemaining > 0, the "
                    + "way BountyPanel's post gate already does.")
                .IsTrue();

            AssertThat(row.TooltipText)
                .OverrideFailureMessage(
                    $"The button is disabled but says '{row.TooltipText}', which does not tell the player "
                    + "it is the action budget that stopped them. A disabled control with the wrong reason "
                    + "is still a mystery.")
                .Contains("action slots");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// A bounty is a commitment the world has to act on, so it still rides the bell — the split is a
    /// deliberate design line (see <c>ActionTiming</c>), not an oversight, and this pins it so a later
    /// "make everything instant" change has to argue with a test first.
    /// </summary>
    [TestCase]
    public async Task PostingABounty_StillWaitsForTheBell()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            await SettleLayout(ui);

            var before = ui.Adapter.CurrentState.Bounties.Count;
            ui.Adapter.Queue(new PostBountyAction(TargetFloor: 1, RewardGold: 25));

            AssertThat(ui.Adapter.CurrentState.Bounties.Count)
                .OverrideFailureMessage(
                    "Posting a bounty took effect immediately. Heroes have to read the board for a " +
                    "bounty to mean anything, so it belongs to the day's clock — see ActionTiming.")
                .IsEqual(before);
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
