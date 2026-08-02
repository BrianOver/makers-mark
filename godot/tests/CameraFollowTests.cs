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
    /// The vertical companion — and, since U2 (shell-and-audio plan, KTD-C), the regression net
    /// for the retired HUD-bias term this test used to pin.
    ///
    /// <para><b>History:</b> the camera used to sit a measured distance ABOVE the player rather
    /// than dead-centre, because MainUi's opaque header painted over the top of the same
    /// full-rect viewport the world rendered into — <c>Town2D.TopObstructionPx</c> fed
    /// <c>FollowPlayer</c> a half-band bias so the visible strip (not the whole viewport) centred
    /// on the player. U2 restructured <c>MainUi.BuildUi</c> so the header sits in LAYOUT FLOW and
    /// Town2D only ever occupies the region below it — the header can no longer occlude any part
    /// of what Town2D reports, so there is no hidden band left to correct for, and
    /// <c>TopObstructionPx</c>/the bias term are gone rather than retuned a third time (the exact
    /// PT19 overcorrection this plan was warned not to repeat). This test now pins the OPPOSITE
    /// fact: with nothing set, the camera sits EXACTLY on the player, not offset at all — a
    /// reintroduced bias (even a well-intentioned one) would turn this red immediately.</para>
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task NothingObstructsTheViewport_TheCameraSitsExactlyOnThePlayer()
    {
        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            town.Player.SetDirectInput(Vector2.Down);
            await AwaitPhysicsFrames(WalkFrames);

            var offset = town.Player.GlobalPosition.Y - town.Cam.GlobalPosition.Y;

            AssertThat(offset)
                .OverrideFailureMessage(
                    $"The camera sits {offset:0.##}px away from the player on Y; expected 0 — Town2D " +
                    "no longer has any concept of a hidden band to correct for (U2 removed " +
                    "TopObstructionPx because the world can no longer be occluded by construction). " +
                    "A nonzero reading means a bias term crept back in.")
                .IsEqualApprox(0f, 2f);
        }
        finally
        {
            town.Player.SetDirectInput(null);
            town.Free();
        }
    }

    /// <summary>
    /// The framing must hold the same amount of WORLD on screen at any window size.
    ///
    /// <para>The shrink used to be a hardcoded 3, so the canvas was window/3 and a bigger monitor simply
    /// showed MORE WORLD rather than the same world larger — 24 tiles across at 1152, 40 at 1080p, 53 at
    /// 1440p, everything looking progressively tinier the better the display. The first framing fix was
    /// verified at 1152x648 and nowhere else, which is very likely why "buildings are too small, world
    /// limited" survived it.</para>
    ///
    /// <para>Checks the pure ladder, so no window has to be resized. The tolerance is what an INTEGER
    /// shrink can achieve: fractional would resample pixel art and shimmer, a worse trade than a tile or
    /// two of drift.</para>
    /// </summary>
    [TestCase]
    public void TheFraming_ShowsTheSameAmountOfWorld_AtEveryWindowSize()
    {
        const int tile = 16;

        // Bounds DERIVED from the design constant, not written out. This test used to hardcode "near 24
        // tiles" and went red the moment the framing was retuned for "world is a little... too zoomed in
        // now" — which is a test asserting a magic number rather than the invariant it cares about. The
        // invariant is only ever "the monitor must not change how much world you see"; what that amount IS
        // belongs to Town2D. The +-25% band is the slack an integer-only shrink ladder forces.
        var target = Town2D.TargetVisibleWorldWidth / (float)tile;
        var floor = target * 0.75f;
        var ceiling = target * 1.25f;

        foreach (var width in new float[] { 1152, 1280, 1600, 1920, 2560, 3840 })
        {
            var shrink = Town2D.ShrinkFor(width);
            var tilesVisible = width / shrink / tile;

            AssertThat(tilesVisible > floor && tilesVisible < ceiling)
                .OverrideFailureMessage(
                    $"At {width}px wide the town shows {tilesVisible:0.#} tiles (shrink {shrink}). The " +
                    $"framing should stay near {target:0.#} tiles at every resolution; drifting outside " +
                    $"{floor:0.#}-{ceiling:0.#} means the world changes size purely because of the monitor.")
                .IsTrue();
        }

        AssertThat(Town2D.ShrinkFor(320))
            .OverrideFailureMessage("A tiny window must still upscale by at least 2 — shrink 1 draws hairline pixels.")
            .IsGreaterEqual(2);
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
