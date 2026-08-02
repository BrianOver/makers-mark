#if GDUNIT_TESTS
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U1 (plan 2026-08-02-002 "playtest three"): the owner's loudest line — "Clicked send them off;
/// not sure whats happening next... WHERE ARE THE VISUALS OF WHAT THEY ARE DOING??" — about a
/// spectating stack (#321: roll call, mine-watch strip, Scrying Mirror) that shipped four days
/// earlier and had never actually been seen. The trace: the PiP dock was the ONLY entry, the dock
/// is suppressed while any drawer/modal owns the screen (<c>MainUi.UpdateEngaged</c>), and the
/// departure camera pan was gated on <c>!Drawer.IsOpen</c> at the exact tick it fired
/// (<c>MainUi.SoundTheTick</c>). A normal Morning ends with a drawer open (craft, then send them
/// off) — the common case, not an edge case — so the pan and the dock both silently no-op every
/// single day.
///
/// <para>The fix (KTD-A): departure now (1) closes the open drawer, (2) defers the pan to whichever
/// modal-close path clears the screen next if a genuine modal (not a drawer) still owns it at the
/// exact tick, and (3) adds a persistent "Watch" control to the bell row so the Mirror is reachable
/// for the entire live span regardless of the dock's own suppression.</para>
///
/// <para>Fixing (1) surfaced a second, latent bug this suite also pins:
/// <c>SimAdapter.Queue</c>'s immediate-action branch (buy/craft/stock/etc., the 2026-07-30 fix)
/// raises the SAME <c>StateChanged</c> event a real Morning-&gt;Expedition boundary does, reporting
/// <c>completedPhase == DayPhase.Morning</c> either way because nothing about the phase argument
/// distinguishes "Morning genuinely just ended" from "still Morning, an immediate action just
/// landed". Without comparing against the POST-event <c>state.Phase</c> too, wiring
/// <c>Drawer.Close()</c> to that same "departing" flag would have slammed the Forge/Shop drawer
/// shut under the player's own Craft/Buy click — a worse regression than the bug being fixed.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SendOffOpensTheShowTests
{
    private const float MinGateDistance = 60f; // mirrors CameraFocusBeatTests' own precondition floor

    [TestCase]
    public async Task SendOff_WithForgeDrawerOpen_ClosesTheDrawer_PansToTheGate_DockBecomesReachable()
    {
        var ui = MountMainUi();
        try
        {
            // MainUi owns a Town2D with a live SubViewport; awaiting frames while one renders is the
            // documented gdUnit headless hang (godot-3d-headless-test-hang) — disable it first.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");

            var gate = ui.Town.FindBuilding("minegate").DoorAnchorGlobal;
            var player = ui.Town.Player.GlobalPosition;
            AssertThat(player.DistanceTo(gate))
                .OverrideFailureMessage("The gate is not far enough from spawn for this test to mean anything.")
                .IsGreater(MinGateDistance);

            // The exact click the owner made: "Send them off" pressed with a drawer open.
            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage(
                    "The Forge drawer is still open after send-off. A normal Morning ends with a " +
                    "drawer open (craft, then send them off) — the departure show can never become " +
                    "reachable behind it. This is the exact reachability defect the owner reported.")
                .IsFalse();

            // Pip.Suppressed is recomputed by UpdateEngaged, which Drawer.Closed fires synchronously
            // from inside the same press. Force one _Process to read the slide-state it drives —
            // no real frame pump happens in this synchronous suite (mirrors PipDockTests).
            ui.Pip._Process(0.0);
            AssertThat(ui.Pip.Suppressed)
                .OverrideFailureMessage("The drawer closed but the PiP dock is still suppressed — UpdateEngaged never re-ran.")
                .IsFalse();
            AssertThat(ui.Pip.Docked)
                .OverrideFailureMessage("The PiP dock is not docked during Expedition even though nothing suppresses it.")
                .IsTrue();

            await SettleLayout(ui); // let the camera's own position-smoothing catch up to the focus beat
            var toGate = ui.Town.Cam.GlobalPosition.DistanceTo(gate);
            var toPlayer = ui.Town.Cam.GlobalPosition.DistanceTo(player);
            AssertThat(toGate < toPlayer)
                .OverrideFailureMessage(
                    $"The camera is {toGate:0.#}px from the gate and {toPlayer:0.#}px from the player " +
                    "after send-off — it never looked at the gate, so the departure is still invisible.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task SendOff_WithNothingOpen_StillPansImmediately_NoRegressionToTheWorkingCase()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var gate = ui.Town.FindBuilding("minegate").DoorAnchorGlobal;
            var player = ui.Town.Player.GlobalPosition;
            AssertThat(player.DistanceTo(gate)).IsGreater(MinGateDistance);

            // Nothing open at all — the plain case that already worked before this unit. The
            // rewritten gate must not regress it while fixing the drawer-open case above.
            PressEnabled(ui, "AdvancePhase");

            await SettleLayout(ui);
            var toGate = ui.Town.Cam.GlobalPosition.DistanceTo(gate);
            var toPlayer = ui.Town.Cam.GlobalPosition.DistanceTo(player);
            AssertThat(toGate < toPlayer)
                .OverrideFailureMessage("The plain no-drawer-no-modal departure pan regressed.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task SendOff_WithLedgerModalOpen_DefersThePan_FiresOnceLedgerCloses()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var gate = ui.Town.FindBuilding("minegate").DoorAnchorGlobal;
            var player = ui.Town.Player.GlobalPosition;
            AssertThat(player.DistanceTo(gate)).IsGreater(MinGateDistance);

            // A manual Ledger peek stands in for "a genuine modal happens to be open at the exact
            // tick the bell is pressed" — the plan's own test scenario (KTD-A move 2). Closing the
            // drawer is never in question here (none is open); the question is whether a MODAL
            // correctly defers the beat instead of either dropping it or panning invisibly behind it.
            ui.Ledger.ShowFor(1);
            AssertThat(ui.Ledger.Visible).IsTrue();

            PressEnabled(ui, "AdvancePhase"); // Morning -> Expedition, bell pressed with Ledger open
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            await SettleLayout(ui);
            var toGateWhileOpen = ui.Town.Cam.GlobalPosition.DistanceTo(gate);
            var toPlayerWhileOpen = ui.Town.Cam.GlobalPosition.DistanceTo(player);
            AssertThat(toGateWhileOpen < toPlayerWhileOpen)
                .OverrideFailureMessage(
                    "The camera panned to the gate WHILE the Ledger still owned the screen — invisible " +
                    "behind a modal, the same bug one layer deeper. The pan must defer until the modal " +
                    "actually closes.")
                .IsFalse();

            ui.Ledger.CloseModal(); // fires OnLedgerVisibilityChanged(Visible: false) -> the deferred fire

            await SettleLayout(ui);
            var toGateAfterClose = ui.Town.Cam.GlobalPosition.DistanceTo(gate);
            var toPlayerAfterClose = ui.Town.Cam.GlobalPosition.DistanceTo(player);
            AssertThat(toGateAfterClose < toPlayerAfterClose)
                .OverrideFailureMessage(
                    "Closing the Ledger did not fire the departure pan armed at send-off time — the " +
                    "beat is lost the moment any modal happened to be open at the tick.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CraftingDuringMorning_ThroughItsRealSignal_DoesNotCloseTheDrawer()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            // Same shape as ClearDuringSignalTests: an immediate BuyMaterialAction, then a real
            // Craft press through its own pressed signal — never AdvancePhase, so this stays in
            // Morning exactly the way a player crafting before ringing the bell does.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.OpenPanel("Forge");
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage(
                    "Crafting advanced the phase — this test's premise (an immediate mid-Morning " +
                    "action) no longer holds; re-check ActionTiming.ResolvesImmediately.")
                .IsEqual(DayPhase.Morning);

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage(
                    "Pressing Craft closed the Forge drawer. SimAdapter.Queue's immediate-action " +
                    "branch raises the same StateChanged event a real Morning->Expedition boundary " +
                    "does, reporting completedPhase==Morning either way — SoundTheTick must tell them " +
                    "apart (completedPhase != state.Phase) before treating one as a send-off.")
                .IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void WatchButton_VisibleOnlyDuringTheThreeLiveExpeditionPhases_AndOpensTheMirror()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var watch = Find<Button>(ui, "WatchButton");
            AssertThat(watch.Visible)
                .OverrideFailureMessage("Watch is visible at Dawn/Morning — nothing to watch yet.")
                .IsFalse();

            AdvanceToPhase(ui, DayPhase.Expedition);
            AssertThat(watch.Visible).IsTrue();
            AssertThat(ui.Mirror.Visible).IsFalse();

            PressEnabled(ui, "WatchButton");
            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("The persistent Watch control did not open the Scrying Mirror.")
                .IsTrue();
            ui.Mirror.CloseMirror();

            AdvanceToPhase(ui, DayPhase.Camp);
            AssertThat(watch.Visible).IsTrue();

            AdvanceToPhase(ui, DayPhase.ExpeditionDeep);
            AssertThat(watch.Visible).IsTrue();

            AdvanceToPhase(ui, DayPhase.Evening);
            AssertThat(watch.Visible)
                .OverrideFailureMessage("Watch stayed visible into Evening — everyone is already home.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
