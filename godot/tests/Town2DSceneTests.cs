#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U1: <see cref="Town2D"/>'s build/contract smoke tests — the 2.5D twin of
/// <c>Town3DSceneTests</c>. Property-only assertions everywhere (no frame pump): a 2D
/// <see cref="SubViewport"/> render doesn't trip the 3D-headless-render-hang KTD the pivot plan
/// calls out, but there is still no reason to pump frames for state <see cref="Town2D.Build"/>
/// already sets synchronously.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Town2DSceneTests
{
    private static Town2D Mount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 42));
        return town;
    }

    [TestCase]
    public void Town2D_Built_HasForgeBuilding()
    {
        var town = Mount();
        try
        {
            AssertThat(town.FindBuilding("forge"))
                .OverrideFailureMessage("Build() must place a 'forge' venue — TownLayout2D.Venues regressed")
                .IsNotNull();
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_Built_GroundHasPaintedCells()
    {
        var town = Mount();
        try
        {
            AssertThat(town.Ground.GetUsedCells().Count)
                .OverrideFailureMessage("Ground TileMapLayer must have painted cells — BuildGround regressed")
                .IsGreater(0);
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_Built_HeroActorCount_MatchesAliveHeroesInState()
    {
        var town = Mount();
        try
        {
            var expected = town.Adapter!.CurrentState.Heroes.Values.Count(h => h.Alive);

            AssertThat(town.HeroActorCount())
                .OverrideFailureMessage("HeroActorCount must mirror the adapter's alive-hero count — ReconcileHeroes regressed")
                .IsEqual(expected);
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_ForgeRaisePick_FiresBuildingClickedWithForgeKey()
    {
        var town = Mount();
        try
        {
            string? raised = null;
            town.BuildingClicked += key => raised = key;

            town.FindBuilding("forge").RaisePick();

            AssertThat(raised)
                .OverrideFailureMessage("Building2D.Picked('forge') must re-emit as Town2D.BuildingClicked('forge')")
                .IsEqual("forge");
        }
        finally { town.Free(); }
    }
}
#endif
