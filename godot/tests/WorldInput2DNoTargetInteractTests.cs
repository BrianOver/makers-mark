#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// fix/pressing-E-at-nothing-says-something: a verified-good 700-turn scripted playtest on main
/// produced 9 `no-response-press` friction entries, 7 of the same shape — the pilot walked to a
/// station, pressed "interact", and the screen read BYTE-IDENTICAL before and after (three dead
/// presses in a row, standing as little as 42px from the target, before the pilot gave up). Root
/// cause: <see cref="Town2d.WorldInput2D._PhysicsProcess"/> only ever called
/// <c>ActiveTarget.RaisePick()</c> when a target existed — a press with <c>ActiveTarget == null</c>
/// produced no sound, no prompt, no screen change at all, a direct hit on the repo's own law 3
/// ("every verb changes an outcome or reveals the player's stake").
///
/// <para>The fix adds <see cref="Town2d.WorldInput2D.NoTargetInteract"/>, re-emitted by
/// <see cref="Town2d.Town2D.NoTargetInteract"/> and shown by <c>MainUi</c> through its existing
/// <c>ShowBellToast</c> — the SAME reused rejection-toast banner every other transient one-liner in
/// this codebase already shows (<c>OnStationActivated</c>'s flavor/copy toasts, the bell-action
/// notices), so this is not a new UI affordance and fires at most once per keypress
/// (<c>IsActionJustPressed</c>), never per held-key frame.</para>
///
/// <para>Two tests, mirroring <c>AgentPlaytestBridgeTests.KeyInteract_AtForgeDoorAtSpawn_EntersTheForge</c>'s
/// own drive-it-for-real shape (the exact bridge/harness that produced the original finding): one
/// proves the miss now says something true and the screen changes, the other proves a real in-range
/// press is completely unaffected — the regression this fix must not introduce.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WorldInput2DNoTargetInteractTests
{
    [TestCase]
    public async Task KeyInteract_WithNoStationInRange_ShowsAnHonestToast_AndTheScreenChanges()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var messages = new List<string>();
            ui.Town.WorldInputNode.NoTargetInteract += messages.Add;

            // TownLayout2D's whole grid is 40x28 tiles at 16px (640x448) — this is nowhere near any
            // building's Interact zone by construction, not by tuning a magic distance.
            ui.Town.Player.GlobalPosition = new Vector2(-2000f, -2000f);
            await PumpWorldFrames(ui, 4); // let the moved position's physics overlap resolve to none

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage(
                    "Setup check: ActiveTarget is not null after moving the player 2000px off the " +
                    "town grid — this test would prove nothing about the no-target path.")
                .IsNull();

            var player = new HumanPlayer(ui);
            var before = player.Screen();

            var bridge = new AgentPlaytestBridge(ui);
            var outcome = await bridge.Apply(
                ui, new AgentCommand("key", "interact", Why: "pilot: pressing E with nothing nearby"));

            var after = player.Screen();

            AssertThat(messages.Count)
                .OverrideFailureMessage(
                    $"WorldInput2D.NoTargetInteract never fired for a no-target 'interact' press " +
                    $"(bridge outcome: '{outcome}'). The dead-press bug is back.")
                .IsEqual(1);
            AssertThat(messages[0])
                .OverrideFailureMessage($"Expected an honest 'move closer' message, got '{messages[0]}'.")
                .Contains("move closer");

            AssertThat(after)
                .OverrideFailureMessage(
                    "The screen read byte-identical before and after a no-target 'interact' press — " +
                    "this is the exact verified playtest finding this fix exists to close.\n" +
                    $"before:\n{before}\nafter:\n{after}")
                .IsNotEqual(before);
            AssertThat(after.Contains("move closer", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The toast message never reached the screen. Screen after:\n{after}")
                .IsTrue();

            AssertThat(ui.ToastRemaining > 0)
                .OverrideFailureMessage("ShowBellToast did not leave the toast banner showing.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The regression risk: an in-range press must keep behaving exactly as before — same shape as
    /// <c>AgentPlaytestBridgeTests.KeyInteract_AtForgeDoorAtSpawn_EntersTheForge</c> (the forge is 8px
    /// away and InRange at spawn), plus the one new assertion that matters here — the no-target path
    /// never fires alongside a real target.
    /// </summary>
    [TestCase]
    public async Task KeyInteract_WithAStationInRange_StillEntersIt_AndNeverFiresTheNoTargetToast()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var messages = new List<string>();
            ui.Town.WorldInputNode.NoTargetInteract += messages.Add;

            var bridge = new AgentPlaytestBridge(ui);
            var before = bridge.BuildDigest(ui, 1, "(start)");
            var forge = before.Nearby.FirstOrDefault(n => n.Key.Equals("forge", StringComparison.OrdinalIgnoreCase));
            AssertThat(forge)
                .OverrideFailureMessage($"Setup check: forge not in nearby list at spawn. Nearby: {string.Join(", ", before.Nearby.Select(n => n.Key))}.")
                .IsNotNull();
            AssertThat(forge!.InRange)
                .OverrideFailureMessage($"Setup check: forge reported {forge.Distance}px away, not InRange, at spawn.")
                .IsTrue();

            var outcome = await bridge.Apply(ui, new AgentCommand("key", "interact", Why: "pilot: entering Forge"));

            var player = new HumanPlayer(ui);
            var entered = await player.WaitUntil(() => ui.Drawer.IsOpen || ui.Town.InteriorActive);

            AssertThat(entered)
                .OverrideFailureMessage(
                    $"key:interact at the forge doorstep did not enter anything: outcome='{outcome}'. " +
                    "This is the pre-existing in-range path, which this fix must not touch.")
                .IsTrue();

            AssertThat(messages)
                .OverrideFailureMessage(
                    "NoTargetInteract fired for an in-range press that DID have a target " +
                    $"(messages: [{string.Join(", ", messages)}]). The null check regressed.")
                .IsEmpty();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
