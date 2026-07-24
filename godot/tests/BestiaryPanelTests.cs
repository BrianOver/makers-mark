#if GDUNIT_TESTS
using System.Linq;
using GameSim.Venues;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Town;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Gate-b flag 3: <see cref="BestiaryPanel"/> is the venue-independent surface for the parked
/// Gloomwood/Sunken-Crypt monster meshes. Pure projection of <see cref="VenueRegistry.All"/> +
/// <see cref="AssetCatalog.MonsterModelFile"/> — no sim state, no <c>GameState</c>. Property-only:
/// <see cref="MonsterView3D"/> stays render-disabled under the headless driver, so a selection
/// loads/fits the mesh (asserted via <see cref="MonsterView3D.HasMonster"/>) without a render.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BestiaryPanelTests
{
    [TestCase]
    public void ShowAll_ListsEveryRegisteredVenueMonster()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();

            AssertThat(ui.Bestiary.Visible).IsTrue();
            var expected = VenueRegistry.All.Values.Sum(v => v.Floors.Length);
            AssertThat(ui.Bestiary.MonsterCount).IsEqual(expected);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void OnOpen_AutoSelectsFirstMonsterThatHasAMesh()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();

            // Venues iterate Id-sorted (emberfall, gloomwood, mine, sunkencrypt); the first monster
            // with a gen model is Gloomwood F1's Bramble Boar, so the viewer is never blank on open.
            AssertThat(ui.Bestiary.SelectedHasMesh).IsTrue();
            AssertThat(ui.Bestiary.SelectedKind).IsNotNull();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SelectingVenueMonsterWithModel_ShowsRealMesh()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            PressEnabled(ui.Bestiary, "Bestiary_crypt-crab"); // Sunken Crypt F1 — has a gen model

            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Crypt Crab");
            AssertThat(ui.Bestiary.SelectedHasMesh).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SelectingMonsterWithoutModel_FallsBackGracefully_NoMesh()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            PressEnabled(ui.Bestiary, "Bestiary_the-forgeworm"); // Mine F5 — no gen model

            AssertThat(ui.Bestiary.SelectedKind).IsEqual("The Forgeworm");
            AssertThat(ui.Bestiary.SelectedHasMesh).IsFalse(); // graceful: card, not a crash
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void Close_HidesAndClearsTheMesh()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            AssertThat(ui.Bestiary.SelectedHasMesh).IsTrue();

            PressEnabled(ui.Bestiary, "BestiaryClose");

            AssertThat(ui.Bestiary.Visible).IsFalse();
            AssertThat(ui.Bestiary.SelectedHasMesh).IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TavernInterior_CarriesABestiaryHotspot_RoutingToTheBestiaryAction()
    {
        // The reachability contract: the Tavern's declarative hotspot table offers "Bestiary",
        // whose action MainUi routes to BestiaryPanel.ShowAll (see OnInteriorHotspotActivated).
        var tavern = InteriorStage.Venues["tavern"];
        var bestiary = tavern.Hotspots.Single(h => h.Action == "Bestiary");
        AssertThat(bestiary.Label).IsEqual("Bestiary");
    }
}
#endif
