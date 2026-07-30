#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The camera must follow the player, or moving walks you off the edge of the world.
///
/// <para><b>Why this exists:</b> <c>Town2D.Build</c> set <c>Cam.GlobalPosition = forgeDoor</c> once
/// and nothing ever moved it again — there was no follow code anywhere in the file. In a top-down
/// game whose only control is WASD, that is the whole game broken: you walk, and you leave the
/// frame. Found 2026-07-30 by rendering the game and looking at it.</para>
///
/// <para><b>Why nothing caught it:</b> every existing check reads sim state or asserts on node
/// properties immediately after <c>Build</c>, when the camera IS on the player — the spawn point is
/// the one position where a frozen camera looks correct. Nothing had ever moved the player and then
/// asked where the view was pointing. So these tests move first: real physics steps, then an
/// assertion about the camera. Same lesson as <c>PlayerCanInteractTests</c> — a check that only
/// looks at the state a bug leaves intact cannot see the bug.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CameraFollowTests
{
    /// <summary>Enough physics steps at <c>PlayerController2D.Speed</c> (90 px/s, 60Hz) to clear a
    /// couple of tiles — far enough that a frozen camera is unambiguously wrong, short enough to
    /// stay inside the town and finish instantly.</summary>
    private const int WalkFrames = 20;

    [TestCase]
    public async System.Threading.Tasks.Task WalkingRight_DragsTheCameraAlong()
    {
        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));

            // MUST precede any frame await: Town2D owns a live SubViewport, and pumping frames while
            // one renders is the documented gdUnit headless hang (see PlayerCanInteractTests, which
            // took CI from 502 passing tests to 68 by omitting this line). Physics is unaffected.
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var startPlayer = town.Player.GlobalPosition;
            var startCam = town.Cam.GlobalPosition;

            town.Player.SetDirectInput(Vector2.Right);
            await AwaitPhysicsFrames(WalkFrames);

            var walked = town.Player.GlobalPosition.X - startPlayer.X;
            AssertThat(walked)
                .OverrideFailureMessage(
                    $"The player did not move right at all ({walked:0.##}px) — this test cannot say " +
                    "anything about the camera until movement itself works. Check MoveAndSlide, the " +
                    "player's CollisionShape2D, and whether something is blocking the spawn tile.")
                .IsGreater(1f);

            var cameraMoved = town.Cam.GlobalPosition.X - startCam.X;
            AssertThat(cameraMoved)
                .OverrideFailureMessage(
                    $"The player walked {walked:0.##}px right but the camera moved {cameraMoved:0.##}px. " +
                    "A camera that does not follow means walking leaves the visible frame — the game " +
                    "is unplayable. Town2D._Process must keep Cam on the player every frame.")
                // Tolerance is one frame of walking (Speed/60 = 1.5px): the follow runs in _Process
                // and movement in _PhysicsProcess, so the camera can legitimately be a single frame
                // behind. Anything larger than that is a real failure to track, not ordering.
                .IsGreaterEqual(walked - 2f);
        }
        finally
        {
            town.Player.SetDirectInput(null);
            town.Free();
        }
    }

    /// <summary>
    /// The vertical companion, and the one that pins the HUD bias: the camera tracks Y too, and sits
    /// a measured distance ABOVE the player rather than dead-centre, because MainUi's opaque header
    /// covers the top of the same full-rect viewport the world renders into. Asserting the offset
    /// (not just "it moved") is what stops the bias silently reverting to 0 or drifting to a
    /// hand-tuned constant.
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task WithATopObstruction_TheCameraSitsAboveThePlayerByHalfOfIt()
    {
        const float obstructionPx = 150f;

        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            town.TopObstructionPx = obstructionPx;
            town.Player.SetDirectInput(Vector2.Down);
            await AwaitPhysicsFrames(WalkFrames);

            var offset = town.Player.GlobalPosition.Y - town.Cam.GlobalPosition.Y;

            // Half the hidden band, converted from screen px to world px by the canvas upscale.
            // Derived here the same way Town2D derives it rather than hardcoded, so the two cannot
            // disagree about the shrink factor while both looking correct.
            var expected = obstructionPx / 2f / Town2D.CanvasShrink;

            AssertThat(offset)
                .OverrideFailureMessage(
                    $"The camera sits {offset:0.##}px above the player; expected {expected:0.##}px " +
                    "(half the HUD-covered band, in world pixels). At 0 the player is centered in a " +
                    "viewport whose top quarter is behind the HUD, which hides whatever building they " +
                    "are standing at — the forge, at spawn.")
                // Same one-frame _Process/_PhysicsProcess allowance as the horizontal test.
                .IsEqualApprox(expected, 2f);
        }
        finally
        {
            town.Player.SetDirectInput(null);
            town.Free();
        }
    }

    private static void AddNodeToTree(Node node) => ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);

    private static async System.Threading.Tasks.Task AwaitPhysicsFrames(int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }
}
#endif
