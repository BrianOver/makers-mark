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

    [TestCase]
    public void Town3D_Built_PlacesGenProps()
    {
        var town = Mount();
        try
        {
            // Anti-scatter smoke check: prove BuildProps' gen-asset wiring actually RAN in the
            // assembled town (not merely that the GLBs load in isolation, which GenAssetCoverageTests
            // covers). A regression that dropped the AddGenProp calls fails here. Node names are set
            // in Town3D.AddGenProp ("Gen_<file>"); property-only, no frame pump.
            foreach (var name in new[] { "Gen_well", "Gen_ore-cart", "Gen_market-stall", "Gen_bounty-board", "Gen_barrel", "Gen_signpost", "Gen_haybale", "Gen_statue", "Gen_lamp-post", "Gen_tree-stump", "Gen_trough" })
            {
                AssertThat(town.FindChild(name, recursive: true, owned: false))
                    .OverrideFailureMessage($"gen prop '{name}' is missing from the built town — BuildProps wiring regressed")
                    .IsNotNull();
            }
        }
        finally { town.Free(); }
    }
}
#endif
