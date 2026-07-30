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
/// Drags a craft onto a shelf with an ACTUAL mouse gesture — press, move, release.
///
/// <para><b>Why this exists:</b> Brian's playtest (2026-07-30): "the shop doesn't let me drag
/// anything on the shelves". Stocking is a core verb, and the existing coverage
/// (<c>ShopPanelTests.DropOnEmptyShelfSlot_...</c>) calls <c>_GetDragData</c> and <c>_DropData</c>
/// directly — those are seams. They prove the PAYLOAD and the DROP HANDLER are correct while saying
/// nothing about whether Godot will ever invoke them from a real gesture, which depends on things a
/// direct call cannot see: whether an intermediate container's <c>MouseFilter</c> swallows the press
/// before the drag source sees it, whether the card is visible, and whether the drag threshold is
/// ever crossed. Exactly the shape of the two bugs that already shipped past this suite.</para>
///
/// <para>So this pushes real <see cref="InputEventMouseButton"/>/<see cref="InputEventMouseMotion"/>
/// events through the viewport and asserts a <see cref="StockAction"/> was queued.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RealDragOntoShelfTests
{
    /// <summary>Godot needs the pointer to travel a few pixels with the button held before it treats
    /// the gesture as a drag rather than a click; stepped so the motion is unambiguous.</summary>
    private const int DragSteps = 6;

    [TestCase]
    public async Task DraggingAnUnshelvedCraftOntoAnEmptySlot_WithARealMouseGesture_QueuesTheStock()
    {
        var ui = MountMainUi();
        try
        {
            // MainUi owns a Town2D, and Town2D owns a live SubViewport. Awaiting frames while one
            // renders is the documented gdUnit headless hang — kill the render target before the
            // first await. Input and layout are unaffected.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);

            var itemId = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            ui.OpenPanel("Shop");
            await SettleLayout(ui);

            var card = Find<Control>(ui.Shop, $"UnshelvedCard_{itemId.Value}");
            var slot = Find<Control>(ui.Shop, "EmptyShelfSlot_0");

            var from = card.GetGlobalRect().GetCenter();
            var to = slot.GetGlobalRect().GetCenter();

            AssertThat(card.IsVisibleInTree())
                .OverrideFailureMessage(
                    "The unshelved craft's card is not visible, so no real gesture can reach it — " +
                    "this test cannot say anything about dragging until the card is on screen.")
                .IsTrue();

            await DragMouse(ui, from, to);

            var queued = ui.Adapter.PendingActions.OfType<StockAction>().ToList();
            AssertThat(queued.Count)
                .OverrideFailureMessage(
                    $"Dragging the card from {from} to the empty shelf slot at {to} with a real mouse " +
                    "gesture queued no StockAction. The player cannot shelve anything by dragging, " +
                    "which is the shop's core verb. The payload/drop seams are covered elsewhere and " +
                    "pass — so suspect the gesture layer: an intermediate container's MouseFilter " +
                    "eating the press before DragHandle sees it, or the drag threshold never being " +
                    "crossed.")
                .IsEqual(1);
            AssertThat(queued[0].Item).IsEqual(itemId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Press at <paramref name="from"/>, walk the pointer to <paramref name="to"/> with the
    /// left button held (so Godot's own drag machinery starts the drag and tracks the target), then
    /// release. Frames are pumped between events because Godot resolves drag start and drop-target
    /// hovering during GUI processing, not inside PushInput.</summary>
    private static async Task DragMouse(Node context, Vector2 from, Vector2 to)
    {
        var viewport = context.GetViewport();

        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = from,
            GlobalPosition = from,
        });
        await SettleLayout(context);

        for (var step = 1; step <= DragSteps; step++)
        {
            var at = from.Lerp(to, step / (float)DragSteps);
            viewport.PushInput(new InputEventMouseMotion
            {
                Position = at,
                GlobalPosition = at,
                Relative = (to - from) / DragSteps,
                ButtonMask = MouseButtonMask.Left, // held — without this it is a hover, not a drag
            });
            await SettleLayout(context);
        }

        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = to,
            GlobalPosition = to,
        });
        await SettleLayout(context);
    }
}
#endif
