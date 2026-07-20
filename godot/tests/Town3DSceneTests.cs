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
public class Town3DSceneTests
{
    private static Town3D Mount()
    {
        var town = new Town3D { Name = "Town3D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(2026));
        return town;
    }

    [TestCase]
    public async Task Town3D_Built_HasPickingViewportAndCurrentCamera()
    {
        var town = Mount();
        try
        {
            // Property-only assertions: everything here is set synchronously in Build(); no frame
            // pump (pumping a rendering 3D SubViewport hangs the headless gdUnit runner).
            AssertThat(town.Viewport.PhysicsObjectPicking)
                .OverrideFailureMessage("SubViewport.PhysicsObjectPicking must be ON (U25) or 3D clicks die").IsTrue();
            AssertThat(town.Viewport.HandleInputLocally).IsTrue();
            AssertThat(town.Camera.GetNode<Camera3D>("Camera3D").Current).IsTrue();
            AssertThat(town.World).IsNotNull();
        }
        finally { town.Free(); }
    }
}
#endif
