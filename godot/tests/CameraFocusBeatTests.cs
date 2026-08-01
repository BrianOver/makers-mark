#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The camera must be able to look away from the player for a beat, and must always give it back.
///
/// <para><b>Why:</b> the HUD promises "watch them go" when a party marches for the Mine, and the player
/// could not — the camera is glued to them, the gate sits at the far north edge of the town, and the
/// rally marker is off screen the moment it appears. Brian's playtest: "after sending off the party, the
/// little floor thing is off the screen — where are the visuals to follow their adventure??"</para>
///
/// <para>The risk in a feature like this is the opposite failure: a camera that borrows control and never
/// returns it, or that freezes the player. So the tests here assert BOTH halves — it actually leaves, and
/// it actually comes back — plus that input was never touched.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CameraFocusBeatTests
{
    [TestCase]
    public async System.Threading.Tasks.Task FocusOnMineGate_MovesTheCameraOffThePlayer_ThenReturnsIt()
    {
        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));
            // Must precede any frame await — the documented SubViewport headless hang.
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var gate = town.FindBuilding("minegate").DoorAnchorGlobal;
            var player = town.Player.GlobalPosition;

            // The whole point only exists if the gate is genuinely far from the player — otherwise this
            // test would pass on a camera that never moved at all.
            AssertThat(player.DistanceTo(gate))
                .OverrideFailureMessage("The gate is not far enough from spawn for this test to mean anything.")
                .IsGreater(60f);

            town.FocusOnMineGate(seconds: 0.5f);
            await AwaitFrames(3);

            var duringFocus = town.Cam.GlobalPosition.DistanceTo(gate);
            var toPlayerDuring = town.Cam.GlobalPosition.DistanceTo(player);
            AssertThat(duringFocus < toPlayerDuring)
                .OverrideFailureMessage(
                    $"During the beat the camera is {duringFocus:0.#}px from the gate and " +
                    $"{toPlayerDuring:0.#}px from the player — it never looked at the gate, so the " +
                    "departure is still invisible.")
                .IsTrue();

            // Let the beat expire, waiting on the CONDITION rather than a frame count.
            //
            // This used to be `await AwaitFrames(90)`, which silently assumed a frame rate. The beat is
            // half a SECOND of accumulated delta, so 90 frames only outlasts it below ~180fps. That held
            // locally (~60fps) and broke on CI, where this suite disables SubViewport rendering and
            // frames come far faster than real time: 90 frames elapsed in under 0.5s, the beat was still
            // running, and the camera was correctly still on the gate — 176px from the player. The test
            // reported "a camera that borrows control and never returns it" about a camera doing exactly
            // its job. Waiting for the camera to come back cannot make that mistake in either direction,
            // and it still fails for the real bug (a camera that never returns) via the frame ceiling.
            var backOnPlayer = await SettleOnPlayer(town);
            AssertThat(backOnPlayer)
                .OverrideFailureMessage(
                    $"The beat expired and the camera is still {backOnPlayer:0.#}px from the player. A " +
                    "camera that borrows control and never returns it is worse than one that never " +
                    "moved — the player is left driving something off screen.")
                .IsLess(40f);
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>A focus beat is presentation only: the player must still be drivable throughout, because
    /// taking the controls away mid-day would be a far worse bug than the one this feature fixes.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task DuringAFocusBeat_ThePlayerCanStillWalk()
    {
        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            town.FocusOnMineGate(seconds: 5f);
            var before = town.Player.GlobalPosition;

            town.Player.SetDirectInput(Vector2.Right);

            // Same hazard as the sibling test, pointing the other way: walking distance is delta-based, so
            // a FAST frame rate covers less ground per frame, and a fixed 20-frame wait can leave the
            // player short of the 1px this asserts through no fault of the game. Pump until they have
            // actually moved, with a ceiling so a player who genuinely cannot walk still fails.
            var tree = (SceneTree)Engine.GetMainLoop();
            for (var frame = 0; frame < 600 && town.Player.GlobalPosition.X - before.X <= 1f; frame++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            AssertThat(town.Player.GlobalPosition.X - before.X)
                .OverrideFailureMessage("The player could not walk during a focus beat — the camera stole the controls.")
                .IsGreater(1f);
        }
        finally
        {
            town.Player.SetDirectInput(null);
            town.Free();
        }
    }

    /// <summary>Zero or negative duration is a no-op, not an indefinite hijack.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task AZeroLengthFocus_DoesNothing()
    {
        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            town.FocusOn(new Vector2(9999f, 9999f), seconds: 0f);
            await AwaitFrames(3);

            AssertThat(town.Cam.GlobalPosition.DistanceTo(town.Player.GlobalPosition))
                .OverrideFailureMessage("A zero-length focus moved the camera anyway.")
                .IsLess(40f);
        }
        finally
        {
            town.Free();
        }
    }

    private static void AddNodeToTree(Node node) => ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);

    /// <summary>
    /// Pumps frames until the camera has come back to the player, and returns the final distance so the
    /// caller still asserts (and still fails) on the real thing.
    /// <para>
    /// The ceiling is deliberately generous — this is not a performance budget, it is an escape hatch so a
    /// camera that genuinely never returns fails the test instead of hanging the suite. At the frame rates
    /// this suite actually runs at, 0.5s of beat is a small fraction of it.
    /// </para>
    /// </summary>
    private static async System.Threading.Tasks.Task<float> SettleOnPlayer(
        Town2D town, float within = 40f, int maxFrames = 1200)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var distance = town.Cam.GlobalPosition.DistanceTo(town.Player.GlobalPosition);

        for (var frame = 0; frame < maxFrames && distance >= within; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            distance = town.Cam.GlobalPosition.DistanceTo(town.Player.GlobalPosition);
        }

        return distance;
    }

    private static async System.Threading.Tasks.Task AwaitFrames(int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }
}
#endif
