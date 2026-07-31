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

            // Let the beat expire. Frames, not wall-clock: the timer is accumulated delta.
            await AwaitFrames(90);

            var backOnPlayer = town.Cam.GlobalPosition.DistanceTo(town.Player.GlobalPosition);
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
            await AwaitFrames(20);

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
