#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Threading.Tasks;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// feat/escape-closes-modals: Escape now closes the TOPMOST open modal overlay, and only that one.
///
/// <para><b>Before this.</b> <see cref="GodotClient.Ui.DrawerHost"/>/<c>InteriorStage</c> were the
/// only two overlays wired for Escape. Two others — the Scrying Mirror and the Camp slate — shipped
/// as UNRECOVERABLE SOFTLOCKS (their close button could grow off the bottom of the window as content
/// grew, and Escape did nothing either). <c>WholeGameSweepTests.Dismiss</c> separately recorded that
/// the Ledger, Forecast, Commissions, and Legends modals "survived Escape and only closed via their
/// own ✕". None of that is asserted THERE (deliberately, per that suite's own remarks, since fixing
/// it was a design call). This suite asserts the fix.</para>
///
/// <para><b>What these three cases actually guard.</b> The shared mechanism (<see
/// cref="ModalEscape"/>) is a static helper each TRUE overlay's own <c>_Input</c> calls directly —
/// never a <c>SimPanel</c> base-class override, because <c>SimPanel</c> is ALSO the base for ordinary
/// DRAWER CONTENT (<c>ForgePanel</c>/<c>ShopPanel</c>/...) that must never intercept Escape itself (a
/// blanket handler there would silently break <c>DrawerHost</c>'s own Escape-close — see
/// <see cref="ModalEscape"/>'s doc). A shared mechanism like this can fail in exactly three ways, and
/// each test below is aimed at one: closing more than one stacked overlay per press, a minigame's
/// Escape bypassing its <c>Cancel()</c> contract (bare-hide would still let a later call finish the
/// craft), and Escape stealing focus/dismissing a modal out from under someone mid-keystroke in a
/// text field.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EscapeClosesModalsTests
{
    // ── 1. One Escape closes the TOPMOST overlay only — never everything stacked open. ──────────

    [TestCase]
    public async Task Escape_ClosesOnlyTheTopmostOfTwoStackedModals()
    {
        var ui = MountMainUi();
        try
        {
            // Camp and Mirror are both TRUE full-rect modal overlays (SimPanel-based) mounted as
            // MainUi siblings. MainUi.BuildUi adds Mirror AFTER Camp, so Mirror sits later in the
            // tree and — per Godot's reverse-tree-order _Input dispatch this whole feature relies
            // on (children/later-siblings before parents/earlier-siblings) — sees Escape FIRST.
            ui.Camp.ShowModal();
            ui.Mirror.ShowMirror();
            AssertThat(ui.Camp.Visible).IsTrue();
            AssertThat(ui.Mirror.Visible).IsTrue();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);
            await player.Frames(3);

            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("The topmost overlay (Mirror, added later) should have closed on Escape.")
                .IsFalse();
            AssertThat(ui.Camp.Visible)
                .OverrideFailureMessage(
                    "One Escape press closed BOTH stacked overlays. It must close only the TOPMOST one — " +
                    "the overlay underneath (Camp) must survive untouched, exactly like a real stack of " +
                    "windows where closing the front one never also closes the one behind it.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    // ── 2. A minigame's Escape goes through Cancel() — never a bare hide, never a queued craft. ──

    [TestCase]
    public async Task Escape_CancelsAnInProgressMinigame_ThroughCancel_NeverQueuesTheCraft()
    {
        var forge = new ForgeMinigame { Name = "ForgeMinigame" };
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(forge);
            forge.Configure(
                ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId],
                ScriptedSession.CraftMaterial,
                ProfessionRegistry.Blacksmith,
                ImmutableSortedSet<string>.Empty,
                day: 0);

            var player = new HumanPlayer(forge);
            await player.Frames(2); // ClaimKeyboard's focus grab is deferred — see UiKit.ClaimKeyboard

            // Land at least one real strike first, so a mid-run cancel is actually meaningful (not
            // just cancelling an untouched, already-clean overlay).
            player.Tap(Key.Space);
            await player.Frames(1);
            AssertThat(forge.ShapeXPermille)
                .OverrideFailureMessage("Setup failed: the strike never advanced the shape, so the run was never in progress.")
                .IsGreater(0);

            player.Tap(Key.Escape);
            await player.Frames(2);

            var shapingDoneFired = false;
            forge.ShapingDone += _ => shapingDoneFired = true;

            AssertThat(forge.WasCancelled)
                .OverrideFailureMessage("Escape must cancel the run through Cancel() — a bare node-hide would leave WasCancelled false.")
                .IsTrue();
            AssertThat(forge.Completed)
                .OverrideFailureMessage("Escape must never mark the run Completed — Act 1 only completes by reaching its own finish line.")
                .IsFalse();

            // Cancel() must queue NOTHING (PKD8 single-action contract): Act 1 never builds a
            // CraftAction at all — only Act 2 (QuenchMinigame) does, on ITS OWN Plunge — so driving
            // this cancelled overlay further must never raise ShapingDone either.
            forge.Advance(1.0);
            player.Tap(Key.Space);
            await player.Frames(1);
            AssertThat(shapingDoneFired)
                .OverrideFailureMessage("A cancelled Act 1 must never fire ShapingDone, even if driven further after Escape.")
                .IsFalse();
        }
        finally { forge.Free(); }
    }

    // ── 3. Escape while a text field has focus must reach the FIELD, never the modal around it. ──

    [TestCase]
    public void ModalEscape_TryClose_DoesNothing_WhileALineEditHasFocus()
    {
        var root = new Control { Name = "EscapeTypingGuardHost" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(root);
        try
        {
            var field = new LineEdit { Name = "SomeTextField" };
            root.AddChild(field);
            field.GrabFocus();

            var closed = false;
            var handled = ModalEscape.TryClose(
                new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true },
                root.GetViewport(), isOpen: true, () => closed = true);

            AssertThat(handled).IsFalse();
            AssertThat(closed)
                .OverrideFailureMessage(
                    "Escape closed the modal while a LineEdit had focus — a player correcting a typo would watch " +
                    "the whole panel vanish out from under them instead of the key reaching the field.")
                .IsFalse();
        }
        finally
        {
            root.GetParent()?.RemoveChild(root);
            root.Free();
        }
    }

    /// <summary>Control case for the guard above: same call, no focused text field — proves the
    /// guard is actually gating on focus rather than the helper being broken outright (a helper that
    /// always returns false would make the test above pass for the wrong reason).</summary>
    [TestCase]
    public void ModalEscape_TryClose_Closes_WhenNoTextFieldHasFocus()
    {
        var root = new Control { Name = "EscapeTypingGuardControlHost" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(root);
        try
        {
            var closed = false;
            var handled = ModalEscape.TryClose(
                new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true },
                root.GetViewport(), isOpen: true, () => closed = true);

            AssertThat(handled).IsTrue();
            AssertThat(closed).IsTrue();
        }
        finally
        {
            root.GetParent()?.RemoveChild(root);
            root.Free();
        }
    }
}
#endif
