#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Opening a craft overlay the way PRODUCTION opens it must leave it playable.
///
/// <para><b>The defect this exists for.</b> Every active-craft minigame was unplayable by keyboard in
/// the shipped game, and had been for its whole life. <c>UiKit.ClaimKeyboard</c> defers its
/// <c>GrabFocus</c> behind an <c>IsVisibleInTree()</c> guard, and each overlay claimed focus once,
/// from its own <c>EnsureBuilt</c> — which production runs at boot with the overlay HIDDEN. The
/// deferred grab found an invisible node, silently skipped, and the open path never asked again.</para>
///
/// <para>The consequence was not subtle: the click that opened the overlay left focus on the "Work
/// the forge" <c>Button</c> behind it, a focused Godot Button eats Space, and the overlay's own label
/// says "Hammer (Space)" — so pressing Space re-pressed the button underneath, which re-ran
/// <c>Configure</c> and reset the run to zero. Shift for the bellows reached nothing, so heat stayed
/// floored, and strike advance scales with heat: the craft could not be completed at all. The owner's
/// session logs recorded it twice as two "open" rows seconds apart and then silence.</para>
///
/// <para><b>Why the existing keyboard tests passed anyway.</b> <c>MinigameKeyboardWorksTests</c>
/// constructs the overlay VISIBLE and mounts it straight under the root — the single arrangement in
/// which the deferred grab succeeds, and one production never uses. A test that builds the object
/// differently from the way the game builds it is not testing the game. So the rule this file adds is
/// deliberately narrow and awkward on purpose: drive the real panel, press the real button, and read
/// the viewport's actual focus owner.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MinigameOpensPlayableTests
{
    [TestCase]
    public async System.Threading.Tasks.Task PressingWorkForge_LeavesTheKeyboardInsideTheOverlay()
    {
        var ui = MountMainUi();
        try
        {
            // Town2D owns a live SubViewport, and pumping frames while one renders is the documented
            // gdUnit headless hang this repo has been bitten by. The focus grab is deferred, so a
            // frame MUST be pumped — disable the render target first, exactly as
            // PlayerCanInteractTests does. Focus routing is unaffected by the render target.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            // CRITICAL, and the thing the first version of this test got wrong: drain the boot
            // frames BEFORE opening anything. EnsureBuilt's own ClaimKeyboard is deferred, so if the
            // mount and the open happen in one frame, that boot-time grab lands when the overlay is
            // already visible and succeeds BY ACCIDENT — which made this test pass with the fix
            // removed. Production defers it at boot, while the overlay is hidden, where it fails.
            // Pumping here reproduces the real ordering: boot grab fires, finds a hidden node, skips.
            await AwaitFrame();
            await AwaitFrame();

            OpenAnvilMap(ui);
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            AssertThat(overlay.Visible).IsTrue();

            // The open path's own ClaimKeyboard is deferred too, so it lands next frame.
            await AwaitFrame();

            var focused = overlay.GetViewport().GuiGetFocusOwner();
            var focusIsInsideOverlay = focused is not null
                && (focused == overlay || overlay.IsAncestorOf(focused));

            AssertThat(focusIsInsideOverlay)
                .OverrideFailureMessage(
                    "After pressing 'Work the forge' the keyboard is NOT inside the overlay — focus "
                    + $"owner is '{focused?.Name.ToString() ?? "<null>"}'. Space will press whatever "
                    + "button still holds focus instead of striking the billet, which re-runs "
                    + "Configure and resets the run; the minigame is unplayable by keyboard.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Hiding the panel (the ✕, Escape, or a click on the drawer's dim veil all hide it) must CANCEL
    /// a running overlay. Orphaning it left the run ticking, kept it driving the town's furnace glow,
    /// left it covering the panel on the next open, and — because cancel is what writes the session
    /// log's abandon row — erased the single strongest "this is not fun" signal the game can record.
    /// </summary>
    [TestCase]
    public void HidingThePanelWithARunOpen_CancelsIt_RatherThanLeavingItRunning()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            OpenAnvilMap(ui);
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            AssertThat(overlay.Visible).IsTrue();
            AssertThat(overlay.WasCancelled).IsFalse();

            ui.Forge.Visible = false;

            AssertThat(overlay.WasCancelled)
                .OverrideFailureMessage(
                    "Hiding the forge panel left the craft overlay running. It keeps ticking, keeps "
                    + "driving the town's furnace glow, and writes no 'cancel' row — so walking out "
                    + "of a craft is invisible to the session log.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static void OpenAnvilMap(GodotClient.MainUi ui)
    {
        ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
        ui.Adapter.AdvancePhase();
        ui.OpenPanel("Forge");
        PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
    }

    private static async System.Threading.Tasks.Task AwaitFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
#endif
