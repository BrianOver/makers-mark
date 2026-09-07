#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Pushes an ACTUAL mouse click at an ACTUAL screen position and asserts a building reacts.
///
/// <para><b>Why this exists:</b> every other check of the click path drives
/// <c>Building2D.RaisePick()</c> or asserts on node properties — seams that skip the two layers that
/// have each broken the game once (the <see cref="SubViewport"/>'s input forwarding, and physics
/// picking). This test owns the whole chain: root viewport -> <see cref="SubViewportContainer"/>
/// (which must divide the position by its <c>StretchShrink</c>) -> <see cref="SubViewport"/> ->
/// physics pick -> <c>Area2D.InputEvent</c> -> <c>Building2D.Picked</c>. If any link is wrong, no
/// building can be opened and the game ends at the town.</para>
///
/// <para>It exists specifically because <c>StretchShrink</c> became non-1 (see <c>Town2D.Build</c>):
/// that adds a coordinate transform between where the player clicks and where the world thinks they
/// clicked, and property assertions cannot see a transform being wrong.</para>
///
/// <para><b>Verified it can fail</b> (2026-07-30) — the whole point of writing it. Dropping the
/// <c>* CanvasShrink</c> term below turns it red, so the coordinate chain really is under test rather
/// than incidentally satisfied. What it does NOT discriminate: it passes with
/// <c>WorldViewport.HandleInputLocally</c> set either way, so despite what that property's comment in
/// <c>Town2D.Build</c> claimed, the flag is not what makes clicking work — see the corrected note
/// there.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RealClickReachesBuildingTests
{
    [TestCase]
    public async System.Threading.Tasks.Task ClickingTheForgeOnScreen_RaisesItsPickedSignal()
    {
        var town = new Town2D { Name = "Town2D" };
        var picked = string.Empty;
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));
            town.BuildingClicked += key => picked = key;

            // Physics picking needs the viewport processing input, but NOT drawing — and drawing is
            // the documented headless hang (see PlayerCanInteractTests).
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var forge = town.FindBuilding("forge");

            // Put the camera on the forge so it is unambiguously on screen, then wait for the canvas
            // transform below to actually reflect that camera position.
            //
            // Diagnosed 2026-09-04 (#723, OffCameraPointerTests): this is the identical camera-move ->
            // canvas-transform shape that flaked in CI twice in one day under a fixed frame-count
            // pump, because "N frames" is a GUESSED duration, not the condition itself — CI is slower
            // on wall-clock but FASTER per-frame (rendering disabled), so a fixed count can elapse
            // before the transform has actually caught up. Poll the transform itself instead of
            // guessing how long it takes. Budget 30 frames — ~15x the 2 frames this used to guess,
            // generous enough to absorb CI scheduling jitter without ever being the bottleneck when
            // the camera move is behaving correctly (this clears on frame 1 in every observed run).
            var staleTransform = town.WorldViewport.GetCanvasTransform();
            town.Cam.GlobalPosition = forge.GlobalPosition;
            town.Cam.ResetSmoothing();
            await UiTestSupport.SettleUntil(
                town,
                () => town.WorldViewport.GetCanvasTransform() != staleTransform,
                frameBudget: 30,
                conditionDescription: "WorldViewport.GetCanvasTransform() to reflect the camera's move onto the forge");

            // Aim at the centre of the interact shape itself, not the building's origin: the origin
            // is the door ROW (the y-sort line at the building's foot), which sits on the shape's
            // bottom edge and is exactly the kind of off-by-one-pixel target that would make this
            // test fail for a reason that is not the bug it is looking for.
            var target = forge.Interact.GetChild<CollisionShape2D>(0).GlobalPosition;

            // World -> canvas (the camera's transform) -> screen. The last step is the StretchShrink
            // this test is here to police: the container renders a canvas 1/shrink of its own size,
            // so a canvas point maps back to the screen by MULTIPLYING by shrink. The container is
            // full-rect at the origin, so there is no further offset to add.
            var canvasPoint = town.WorldViewport.GetCanvasTransform() * target;
            // The town's LIVE shrink — derived from window size, so never hardcode it here.
            var screenPoint = canvasPoint * town.CanvasShrink;

            PushClick(town, screenPoint);

            // Same shape as the camera-transform wait above: a fixed count guesses how long the
            // pushed input takes to travel through physics picking to Building2D.Picked ->
            // BuildingClicked. Poll the actual result instead. Same 30-frame budget/reasoning.
            await UiTestSupport.SettleUntil(
                town,
                () => picked != string.Empty,
                frameBudget: 30,
                conditionDescription: "BuildingClicked to fire after the pushed screen click reaches the forge's physics pick");

            AssertThat(picked)
                .OverrideFailureMessage(
                    $"A left click at screen {screenPoint} — the forge's own on-screen position — did " +
                    "not reach the forge. Nothing in the town can be opened by clicking. Check " +
                    "SubViewport.HandleInputLocally (must be false), PhysicsObjectPicking (must be " +
                    "true), the container's StretchShrink coordinate transform, and that Building2D's " +
                    "Interact area is InputPickable.")
                .IsEqual("forge");
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>A press AND a release, at the same spot, through the root viewport — the same path a
    /// real mouse takes. <c>Viewport.PushInput</c> rather than the global <c>Input</c> singleton:
    /// synthesising through the singleton headlessly is a recorded dead end in this project.</summary>
    private static void PushClick(Node context, Vector2 screenPoint)
    {
        var viewport = context.GetViewport();
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = screenPoint,
            GlobalPosition = screenPoint,
        });
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = screenPoint,
            GlobalPosition = screenPoint,
        });
    }

    private static void AddNodeToTree(Node node) => ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
}
#endif
