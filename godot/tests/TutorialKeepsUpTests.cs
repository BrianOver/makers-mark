#if GDUNIT_TESTS
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The tutorial card must react to what the player just DID, not to the bell.
///
/// <para><b>Why.</b> Brian's playtest, twice: "The tutorial isn't updating despite entering the forge", and
/// then "i auto crafter the buckler and the heroes auto depoarted? skipped to tutorial step 2". Those are
/// one symptom seen from both ends — the card sits still while you work, then jumps several steps at once
/// when the phase ticks.</para>
///
/// <para><b>Why the existing suite cannot see it.</b> <c>TutorialFlowTests</c> drives
/// <c>TutorialFlow.Advance(state)</c> only indirectly, through <c>SimAdapter.Queue</c>/<c>AdvancePhase</c>
/// on a mounted <c>MainUi</c>, which proves the step machine's transitions are right and says nothing about
/// whether the game ever CALLS it at the moment the player acts. That distinction is the whole bug — and it
/// is the same seam-shaped blind spot as the dead keyboard and the cut-off menus.</para>
///
/// <para>So these drive the real client through <see cref="HumanPlayer"/> and assert on the text actually on
/// screen.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialKeepsUpTests
{
    /// <summary>
    /// Buy material with a real click; the tutorial's on-screen line must move off step 1 immediately,
    /// without ringing the bell.
    /// </summary>
    [TestCase]
    public async Task BuyingMaterial_MovesTheTutorialOn_WithoutRingingTheBell()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            await player.Frames(4);

            var before = ui.Tutorial.Step;
            AssertThat(before)
                .OverrideFailureMessage("The tutorial did not start at its first step, so this test proves nothing.")
                .IsEqual(GodotClient.Ui.TutorialStep.BuyMaterial);

            ui.OpenPanel("Forge");
            await player.WaitForLayout(ui.Drawer.CurrentContent!);

            // Any enabled vendor buy button will do — the claim is about the tutorial reacting to a
            // purchase, not about which ore it was.
            var buy = player.ClickableButtons(ui.Drawer.CurrentContent)
                .FirstOrDefault(b => b.Name.ToString().StartsWith("BuyMat_"));

            AssertThat(buy)
                .OverrideFailureMessage(
                    "No affordable material button was reachable in the Forge on day 1, so the tutorial's own " +
                    $"first instruction cannot be followed. On screen: [{string.Join(" | ", player.ClickableLabels(ui.Drawer.CurrentContent))}]")
                .IsNotNull();

            // Described before clicking: buying rebuilds the vendor rows, which frees this very button, and
            // reading .Text/.Name afterwards throws ObjectDisposedException. The disposal itself is proof the
            // click landed, so it is caught and ignored rather than guarded against.
            var described = $"Forge \"{buy!.Text}\" ({buy.Name})";
            try
            {
                await player.ClickControl(buy, described);
            }
            catch (System.ObjectDisposedException)
            {
                // The click worked so well it deleted its own button.
            }

            await player.Frames(4);

            // The purchase really happened — otherwise the tutorial is right to sit still.
            AssertThat(ui.Adapter.LastEvents.OfType<MaterialPurchased>().Any())
                .OverrideFailureMessage(
                    "The click did not produce a MaterialPurchased event, so nothing is being asserted about " +
                    $"the tutorial. Rejections: [{string.Join("; ", ui.Adapter.LastRejections.Select(r => r.Reason))}]")
                .IsTrue();

            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    $"The player bought material and the tutorial is still on {ui.Tutorial.Step}. Buying is " +
                    "an IMMEDIATE action now (ActionTiming.ResolvesImmediately), so the card has to keep up " +
                    "with it rather than waiting for the bell — otherwise it reads as broken while you work " +
                    "and then jumps several steps at once when the phase ticks.")
                .IsNotEqual(GodotClient.Ui.TutorialStep.BuyMaterial);
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U5 (loop-legibility plan): the overlay must keep up the same way the checklist/top-slot
    /// text do — a pulse still lit on the forge after the step has moved on to Shelve is just as
    /// confusing as stale text, and this suite's whole premise (see class doc) is proving the
    /// REAL client reacts, not a state injection.
    /// </summary>
    [TestCase]
    public async Task BuyingAndCrafting_MovesTheOverlaysPulse_FromForgeToMarket()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            await player.Frames(4);

            // U-T9-5 (§11.14.13): BuyMaterial still DECLARES Station("forge","shelf"), but a fresh
            // mount is the player out in the town, and a station pulse resolves inside the interior
            // room — invisible from out here. Outside, the aim is the forge building; the shelf takes
            // over on entry. This test's own subject (does the pulse MOVE off the forge and onto the
            // market when the step advances) is untouched by that.
            AssertThat(ui.Overlay.PulsingBuildingKey)
                .OverrideFailureMessage(
                    "On a fresh mount the player is outside the forge, so the pulse must be on the "
                    + "forge building itself.")
                .IsEqual("forge");

            ui.OpenPanel("Forge");
            await player.WaitForLayout(ui.Drawer.CurrentContent!);

            // The queued CraftAction below needs ScriptedSession.CopperNeeded (2) units of
            // ScriptedSession.CraftMaterial specifically ("dagger" is a copper recipe) — the
            // Forge's own buy button is "Buy 1" per press (ForgePanel.OnBuyMaterialPressed), so
            // this clicks it CopperNeeded times, re-finding the button by name after each press
            // (buying rebuilds the vendor rows, freeing the prior Button reference — same
            // ObjectDisposedException shape BuyingMaterial_MovesTheTutorialOn_WithoutRingingTheBell
            // already guards above). A single click (the bug this test shipped with, never
            // actually run before CI) leaves the player one copper short, so the queued CraftAction
            // is silently rejected and Step never leaves Craft.
            for (var i = 0; i < ScriptedSession.CopperNeeded; i++)
            {
                var buy = player.ClickableButtons(ui.Drawer.CurrentContent)
                    .FirstOrDefault(b => b.Name.ToString() == $"BuyMat_{ScriptedSession.CraftMaterial}");
                AssertThat(buy)
                    .OverrideFailureMessage(
                        $"No affordable \"{ScriptedSession.CraftMaterial}\" buy button was reachable in the " +
                        $"Forge on day 1 (buy {i + 1}/{ScriptedSession.CopperNeeded}). On screen: " +
                        $"[{string.Join(" | ", player.ClickableLabels(ui.Drawer.CurrentContent))}]")
                    .IsNotNull();

                var described = $"Forge \"{buy!.Text}\" ({buy.Name})";
                try
                {
                    await player.ClickControl(buy, described);
                }
                catch (System.ObjectDisposedException)
                {
                    // The click worked so well it deleted its own button.
                }

                await player.Frames(4);
            }

            AssertThat(ui.Adapter.LastEvents.OfType<MaterialPurchased>().Any())
                .OverrideFailureMessage("The click did not produce a MaterialPurchased event.")
                .IsTrue();
            var copperHeld = ui.Adapter.CurrentState.Player.Materials.TryGetValue(ScriptedSession.CraftMaterial, out var held) ? held : 0;
            AssertThat(copperHeld)
                .OverrideFailureMessage(
                    $"Bought {ScriptedSession.CopperNeeded} rounds of {ScriptedSession.CraftMaterial} by real " +
                    $"click but the player holds {copperHeld} — the queued CraftAction below would reject silently.")
                .IsGreaterEqual(ScriptedSession.CopperNeeded);

            // Craft (immediate) to reach Shelve — the claim under test is the OVERLAY's own
            // reaction to a step change, not a second click-path proof of buying (that is
            // BuyingMaterial_MovesTheTutorialOn_WithoutRingingTheBell above). Deliberately does
            // NOT also queue a StockAction here: Shelve's own IsDone is `Shelf.Count > 0`, so
            // stocking immediately would race the chain straight past Shelve to PostBounty
            // BEFORE this assertion ever observes it — same "OnPhaseCompleted fires again on
            // EVERY immediate action" mechanic Advance's own doc describes — which would point
            // the overlay at "noticeboard", not "market", and fail this test's own stated claim.
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Tutorial.Step).IsEqual(GodotClient.Ui.TutorialStep.Shelve);

            // Wait on the CONDITION, never a frame count — even though SimAdapter.Queue's
            // immediate branch refreshes the HUD synchronously, this is the honest idiom this
            // suite already uses everywhere else.
            var moved = await player.WaitUntil(() => ui.Overlay.PulsingBuildingKey == "market");
            AssertThat(moved)
                .OverrideFailureMessage(
                    $"The tutorial moved to Shelve but the overlay is still pulsing " +
                    $"\"{ui.Overlay.PulsingBuildingKey ?? "(nothing)"}\" instead of the market.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The card must never tell the player to walk somewhere — that was the root of "The tutorial isn't
    /// updating despite entering the forge": step 1 completes on the PURCHASE, not the arrival, but the OLD
    /// instruction read "Walk to the Forge and click it — Buy 2 copper", so doing the first clause and seeing
    /// the line unchanged read as stuck.
    ///
    /// <para>P2-ONBOARD-06 (§11.15), deletion #2 removed the WHERE clause entirely rather than patching its
    /// staleness: <see cref="Ui.TutorialOverlay"/>'s own pulse (already shipped, T10) is now the ONLY place
    /// on screen that answers "where," so the card's own text is WHAT-only and cannot go stale by opening or
    /// closing a drawer — there is no location claim left in it to disagree with reality. This test proves
    /// that directly: the same action-only text, readable and non-overlapping, whether the drawer is closed
    /// or open.</para>
    /// </summary>
    [TestCase]
    public async Task TheCardNeverNamesAWalkDestination_ClosedOrOpen()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            await player.Frames(4);

            var closed = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState, null);
            AssertThat(closed!.Contains("Walk to"))
                .OverrideFailureMessage($"With the drawer closed the card still names a walk destination: \"{closed}\".")
                .IsFalse();
            AssertThat(closed.Contains("You're at"))
                .OverrideFailureMessage($"With the drawer closed the card still claims arrival: \"{closed}\".")
                .IsFalse();

            ui.OpenPanel("Forge");
            await player.WaitForLayout(ui.Drawer.CurrentContent!);

            var open = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState, ui.Drawer.CurrentPanelId);

            AssertThat(open!.Contains("Walk to"))
                .OverrideFailureMessage($"The Forge is open and the card names a walk destination: \"{open}\".")
                .IsFalse();
            AssertThat(open.Contains("You're at"))
                .OverrideFailureMessage($"The Forge is open and the card still claims arrival: \"{open}\".")
                .IsFalse();

            // No WHERE claim left to go stale — closed and open must read identically.
            AssertThat(open)
                .OverrideFailureMessage(
                    $"The action-only card changed text between closed (\"{closed}\") and open (\"{open}\") — " +
                    "it should no longer depend on drawer state at all.")
                .IsEqual(closed);

            // The card must reach the SCREEN on open, not wait for the next state change.
            var tracker = ui.FindChild("ObjectiveTracker", recursive: true, owned: false) as Control;
            var trackerLines = tracker is null
                ? "(no ObjectiveTracker)"
                : string.Join("\n      ", tracker
                    .FindChildren("*", "Label", recursive: true, owned: false)
                    .OfType<Label>()
                    .Select(l => $"[vis={l.IsVisibleInTree()}] {l.Text}"));

            AssertThat(player.Sees("Buy"))
                .OverrideFailureMessage(
                    "The tutorial's own action text is not readable on screen with the Forge open. Either the " +
                    "HUD was not refreshed on open, or the card is hidden while a drawer is up.\n" +
                    $"Tracker labels:\n      {trackerLines}\n\nOn screen:\n{player.Screen()}")
                .IsTrue();

            // And it must not simply be overlapping the drawer instead — that was the original reason it got
            // hidden in the first place (it sat on top of the Forge's "Buy copper" row).
            var card = tracker!.GetGlobalRect();
            var drawer = ui.Drawer.CurrentContent!.GetGlobalRect();
            AssertThat(card.Intersects(drawer))
                .OverrideFailureMessage(
                    $"The tutorial card at {card} overlaps the open drawer at {drawer}. Keeping it readable " +
                    "must not mean putting it back on top of the panel's own buttons — dock it to the free " +
                    "left-hand side instead (MainUi.DockObjectiveHorizontally).")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The card's text on screen must match the step the flow is actually on.
    ///
    /// <para>Separate from the step assertion above because they fail for different reasons: the step can
    /// advance while the rendered line stays stale (nothing refreshed the tracker), which to the player is
    /// indistinguishable from the tutorial not advancing at all.</para>
    /// </summary>
    [TestCase]
    public async Task TheTutorialLineOnScreen_MatchesTheStepTheFlowIsOn()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            await player.Frames(4);

            var expected = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState);
            AssertThat(expected)
                .OverrideFailureMessage("The tutorial produced no text at all on a fresh campaign.")
                .IsNotNull();

            // Compare the STEP COUNTER, not the whole line. TopSlotText carries `**bold**` markers that the
            // tracker strips before rendering, so a whole-string match fails on markup and reports a
            // rendering bug that isn't there — my first version of this test did exactly that. The counter is
            // the part the player actually uses to know whether they progressed.
            //
            // U-T2-1: the counter is act-scoped now ("The Hand-Off · 2/4"), not a global
            // "Tutorial N/Total" — matches the "· N/M" join every act's own prefix shares. The
            // slash form is not cosmetic: the prose form pushed a fresh Morning-1 objective card to
            // 270px against its hard 260px pin, and unclamped copy grows the chip off screen rather
            // than trimming.
            var counter = System.Text.RegularExpressions.Regex.Match(expected!, @"· \d+/\d+");
            AssertThat(counter.Success)
                .OverrideFailureMessage($"The tutorial line \"{expected}\" carries no \"Act · N/M\" counter.")
                .IsTrue();

            // Name the reason, not just the absence: "not on screen" has three causes with three fixes
            // (never refreshed / hidden / laid out somewhere unreachable) and they look identical to a player.
            var tracker = ui.FindChild("ObjectiveTracker", recursive: true, owned: false) as Control;
            var diagnosis = tracker is null
                ? "there is no ObjectiveTracker node in the tree at all"
                : $"ObjectiveTracker is visible={tracker.IsVisibleInTree()} rect={tracker.GetGlobalRect()} " +
                  $"window={ui.GetViewport().GetVisibleRect()}; its own labels read: " +
                  $"[{string.Join(" | ", tracker.FindChildren("*", "Label", recursive: true, owned: false).OfType<Label>().Select(l => l.Text))}]";

            AssertThat(player.Sees(counter.Value))
                .OverrideFailureMessage(
                    $"The tutorial is on step \"{counter.Value}\" but that counter is not readable anywhere on " +
                    $"screen, so the player cannot tell where they are.\nDiagnosis: {diagnosis}\n\n" +
                    $"On screen:\n{player.Screen()}")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }
}
#endif
