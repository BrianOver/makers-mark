#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

[TestSuite]
[RequireGodotRuntime]
public class PlayerController3DTests
{
    private static Town3D Mount()
    {
        var town = new Town3D { Name = "Town3D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(2026));
        return town;
    }

    /// <summary>
    /// T3 learning (mandatory for T4+): pumping frames while the 3D <see cref="SubViewport"/> is
    /// rendering hangs the headless gdUnit runner. Disabling render right after mount lets
    /// physics still advance, so movement can be exercised without ever pumping a render frame.
    /// Capped and throws on exhaustion like every other tick-loop helper in this suite.
    /// </summary>
    private static async Task PumpPhysicsOnly(Node ctx, int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await ctx.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    [TestCase]
    public async Task Wasd_Right_MovesPlayerPositive()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var player = town.Player;
            var start = player.GlobalPosition;
            player.SetDirectInput(new Vector2(1, 0)); // deterministic; no OS input dependency
            try
            {
                await PumpPhysicsOnly(town, 20);
            }
            finally
            {
                player.SetDirectInput(Vector2.Zero);
            }

            AssertThat(player.GlobalPosition.DistanceTo(start) > 0.5f)
                .OverrideFailureMessage($"no move: {start} -> {player.GlobalPosition}").IsTrue();
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>
    /// T6 (KTD12): clicking a building must never open it instantly — <see
    /// cref="PlayerController.MoveToAndInteract"/> queues a navmesh walk, and only arrival within
    /// 1.2 units of <see cref="Building3D.DoorAnchorGlobal"/> raises <see
    /// cref="Town3D.BuildingClicked"/> (re-emitted from <see
    /// cref="PlayerController.ArrivedAtBuilding"/>). Render is disabled right after mount (T3
    /// learning) so <see cref="WalkUntilArrived3D"/> can pump real physics frames without hanging
    /// the headless runner.
    /// </summary>
    [TestCase]
    public async Task ClickForge_WalksThenOpens_NeverInstant()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            string? raised = null;
            town.BuildingClicked += k => raised = k;
            var forge = town.FindBuilding("forge");
            town.Player.GlobalPosition = new Vector3(0, 0, 10);
            town.Player.MoveToAndInteract(forge);
            AssertThat(raised).IsNull().OverrideFailureMessage("opened instantly — must walk first (KTD12)");
            await WalkUntilArrived3D(town, town.Player, forge.DoorAnchorGlobal, 600);

            // WalkUntilArrived3D's own 1.2-unit tolerance is looser than the agent's
            // TargetDesiredDistance (1.0f) that gates NavigationAgent3D.IsNavigationFinished — so
            // a few more physics ticks of travel are needed after it returns before FollowNav's
            // own arrival check (also 1.2, but gated on IsNavigationFinished) actually fires.
            // Capped like every other tick-loop in this suite: throws rather than silently
            // leaving `raised` null if arrival genuinely never settles.
            var settleFrames = 0;
            while (raised == null && settleFrames < 120)
            {
                await PumpPhysicsOnly(town, 1);
                settleFrames++;
            }

            AssertThat(raised).IsEqual("Forge");
        }
        finally
        {
            town.QueueFree();
        }
    }

    /// <summary>
    /// T6: a real player grabbing WASD mid-click-move wins outright — <see
    /// cref="PlayerController.IsClickMoving"/> must drop to <c>false</c> the same frame nonzero
    /// WASD input appears, rather than fighting the nav path for control of <see
    /// cref="CharacterBody3D.Velocity"/>.
    /// </summary>
    [TestCase]
    public async Task ClickMove_ThenWasd_CancelsClickMove()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var player = town.Player;
            player.GlobalPosition = new Vector3(0, 0, 10);
            player.MoveTo(new Vector3(0, 0, -10));
            await PumpPhysicsOnly(town, 3);
            AssertThat(player.IsClickMoving)
                .OverrideFailureMessage("MoveTo did not start a click-move").IsTrue();

            player.SetDirectInput(new Vector2(1, 0));
            try
            {
                await PumpPhysicsOnly(town, 5);
            }
            finally
            {
                player.SetDirectInput(Vector2.Zero);
            }

            AssertThat(player.IsClickMoving)
                .OverrideFailureMessage("WASD input mid-click-move must cancel IsClickMoving").IsFalse();
        }
        finally
        {
            town.QueueFree();
        }
    }

    /// <summary>
    /// T6: mirrors the old drawer-veil guard — while <see
    /// cref="Town3D.SetWorldInputEnabled"/> is off (T8 drives this from an open drawer/interior/
    /// modal), a click must not start a click-move at all. Calls <see
    /// cref="WorldInput3D._UnhandledInput"/> directly rather than through real OS input (headless
    /// click-picking is unproven, G1) — this exercises the <c>Enabled</c> gate itself, which
    /// short-circuits before any raycast/HUD-priority logic runs.
    /// </summary>
    [TestCase]
    public async Task WorldInputDisabled_ClickDoesNotStartMove()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            town.SetWorldInputEnabled(false);

            town.WorldInputNode._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(400, 300),
            });
            await PumpPhysicsOnly(town, 3);

            AssertThat(town.Player.IsClickMoving)
                .OverrideFailureMessage("a click while WorldInput is disabled must not start a click-move").IsFalse();
        }
        finally
        {
            town.QueueFree();
        }
    }
}
#endif
