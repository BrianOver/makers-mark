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

    /// <summary>
    /// Every venue's sprite id must resolve to committed art, never to the flat-colour placeholder.
    ///
    /// <para>This exists because of a real, embarrassing miss: the <c>town2d-*</c> pixel buildings
    /// were committed and imported, and the town went on drawing the older SDXL-era set for weeks
    /// because <see cref="TownLayout2D.Venues"/> still named the bare ids. Nothing failed —
    /// <c>TownAssets2D.ForVenue</c> is deliberately null-tolerant, so a wrong id silently degrades
    /// to a coloured box, and a coloured box in a stylised town does not announce itself. The owner
    /// found it by noticing the Forge roof was magenta.</para>
    ///
    /// <para><b>Resolving is not enough, which is the trap in the first version of this test.</b>
    /// The old ids resolved perfectly — <c>forge.png</c> is committed and manifested too — so an
    /// "art is not null" assertion would have passed happily through the entire bug. The invariant
    /// that actually holds is which SET the town draws, so that is what is pinned: every venue id
    /// is in the <c>town2d-</c> pixel family AND resolves.</para>
    /// </summary>
    [TestCase]
    public void EveryVenueSpriteId_IsInThePixelSet_AndResolvesToCommittedArt()
    {
        foreach (var venue in TownLayout2D.Venues)
        {
            AssertThat(venue.SpriteId.StartsWith("town2d-"))
                .OverrideFailureMessage(
                    $"venue '{venue.Key}' draws sprite '{venue.SpriteId}', which is not in the "
                    + "town2d-* pixel set. The pre-pivot SDXL buildings are still committed and still "
                    + "resolve, so pointing a venue back at one is invisible at runtime — that is "
                    + "exactly how the magenta-roofed Forge survived for weeks.")
                .IsTrue();

            AssertThat(IconRegistry.Art(venue.SpriteId))
                .OverrideFailureMessage(
                    $"venue '{venue.Key}' asks for sprite '{venue.SpriteId}' and got no art, so it "
                    + "draws a flat placeholder box with nothing warning about it — either the id is "
                    + "wrong or the PNG/manifest entry is missing")
                .IsNotNull();
        }
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

    [TestCase]
    public void Town2D_Built_PlazaTileIsCobbled()
    {
        var town = Mount();
        try
        {
            // Plaza center (see TownLayout2D.PathRects' plaza rect) must be cobble, not grass — the
            // cozy-village cluster reads as a paved square, not a gap in a grass field. The rich
            // pixel-art ground atlas (town2d-ground-atlas, always imported in the engine-test env)
            // places cobble at atlas coord (3,0); the code-built flat fallback used (1,0).
            var cell = town.Ground.GetCellAtlasCoords(new Vector2I(20, 15));

            AssertThat(cell)
                .OverrideFailureMessage("Plaza tile (20,15) must be cobbled — TownLayout2D.PathRects or the ground atlas regressed")
                .IsEqual(new Vector2I(3, 0));
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_Built_PlacesEveryConfiguredProp()
    {
        var town = Mount();
        try
        {
            var propNodes = town.YSort.GetChildren()
                .Count(child => child.Name.ToString().StartsWith("Prop_"));

            AssertThat(propNodes)
                .OverrideFailureMessage("Every TownLayout2D.Props entry must mount a Prop_* node under YSort — BuildProps regressed")
                .IsEqual(TownLayout2D.Props.Length);
        }
        finally { town.Free(); }
    }
}
#endif
