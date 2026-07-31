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

            var player = new HumanPlayer(ui);
            var card = Find<Control>(ui.Shop, $"UnshelvedCard_{itemId.Value}");
            var slot = Find<Control>(ui.Shop, "EmptyShelfSlot_0");

            // Scroll the card into view before grabbing it.
            //
            // The shop's content is taller than the drawer, so on a fresh open the unshelved card sits below
            // the fold — this test used to drag from y=726 in a 648px window and "fail". A player scrolls
            // first, so the harness does too. (It started passing again the moment SimPanel began reserving
            // real height for the nested CounterPanel, which pushed the card down — the fix was correct and
            // this test's fixed coordinates were the thing that was wrong.)
            AssertThat(await player.ScrollIntoView(card))
                .OverrideFailureMessage(
                    "Could not scroll the unshelved craft's card into view at all, so no real gesture can " +
                    "reach it. A player cannot shelve what they cannot get on screen.")
                .IsTrue();

            var from = player.VisiblePartOf(card).GetCenter();
            var to = player.VisiblePartOf(slot).GetCenter();

            // Both ends of the gesture have to be on screen AT THE SAME TIME — drag-to-shelve is impossible
            // otherwise, and that would be a genuine design defect rather than a test problem.
            AssertThat(player.VisiblePartOf(slot).Size.Y)
                .OverrideFailureMessage(
                    $"With the card scrolled into view at {from}, the target shelf slot is no longer visible " +
                    $"({player.VisiblePartOf(slot)}). The drag source and its target cannot be on screen " +
                    "together, so the shop's core verb is unperformable — reorder the sections or shorten " +
                    "what sits between them.")
                .IsGreater(4f);

            await player.Drag(from, to);

            var queued = ui.Adapter.AppliedThisPhase.OfType<StockAction>().ToList();
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

}
#endif
