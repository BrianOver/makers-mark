#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

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
}
#endif
