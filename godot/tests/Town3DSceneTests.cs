#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Kernel;
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

    /// <summary>U6: a world whose <see cref="DramaState.Memorials"/> carries two named-hero
    /// memorials (distinct name/day/gear each), used to prove the memorial stone's Label3D +
    /// epitaph surface the sim's data and don't leak between stones.</summary>
    private static Town3D MountWithMemorials()
    {
        var state = GameFactory.NewGame(2026) with
        {
            Drama = GameFactory.NewGame(2026).Drama with
            {
                Memorials = ImmutableList.Create(
                    new Memorial(new HeroId(11), "Fallen11", 3, "iron sword"),
                    new Memorial(new HeroId(12), "Fallen12", 7, "leather boots")),
            },
        };

        var town = new Town3D { Name = "Town3D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(state));
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

    /// <summary>U6: memorial stones must name the dead — the Label3D text carries the hero's
    /// name and the day they fell, and a sibling epitaph Label3D carries the gear they died
    /// wearing. Property-only: everything is set synchronously in Build(); no frame pump.</summary>
    [TestCase]
    public void Town3D_MemorialStone_LabelHasHeroNameAndDay_EpitaphHasGearNamed()
    {
        var town = MountWithMemorials();
        try
        {
            var stone = town.MemorialPlot.GetChildren().OfType<Node3D>()
                .Single(n => n.Name == "Memorial_11");

            var label = stone.GetNode<Label3D>("Label3D");
            AssertThat(label.Text).Contains("Fallen11");
            AssertThat(label.Text).Contains("3"); // Day 3

            var epitaph = stone.GetNode<Label3D>("EpitaphLabel3D");
            AssertThat(epitaph.Text).Contains("iron sword");
        }
        finally { town.Free(); }
    }

    /// <summary>U6: two memorials in the same town must not leak text between stones — each
    /// stone's Label3D/EpitaphLabel3D carries only its own hero's name/day/gear.</summary>
    [TestCase]
    public void Town3D_MemorialStone_MultipleMemorials_RenderDistinctLabels_NoLeakage()
    {
        var town = MountWithMemorials();
        try
        {
            AssertThat(town.MemorialStoneCount).IsEqual(2);

            var stone11 = town.MemorialPlot.GetChildren().OfType<Node3D>().Single(n => n.Name == "Memorial_11");
            var stone12 = town.MemorialPlot.GetChildren().OfType<Node3D>().Single(n => n.Name == "Memorial_12");

            var label11 = stone11.GetNode<Label3D>("Label3D").Text;
            var epitaph11 = stone11.GetNode<Label3D>("EpitaphLabel3D").Text;
            var label12 = stone12.GetNode<Label3D>("Label3D").Text;
            var epitaph12 = stone12.GetNode<Label3D>("EpitaphLabel3D").Text;

            AssertThat(label11).Contains("Fallen11");
            AssertThat(label11).NotContains("Fallen12");
            AssertThat(epitaph11).Contains("iron sword");
            AssertThat(epitaph11).NotContains("leather boots");

            AssertThat(label12).Contains("Fallen12");
            AssertThat(label12).NotContains("Fallen11");
            AssertThat(epitaph12).Contains("leather boots");
            AssertThat(epitaph12).NotContains("iron sword");
        }
        finally { town.Free(); }
    }
}
#endif
