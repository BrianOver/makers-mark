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
/// Gloomwood/Sunken-Crypt monsters. Pure projection of <see cref="VenueRegistry.All"/> +
/// <see cref="AssetCatalog.MonsterPortrait"/> — no sim state, no <c>GameState</c>.
///
/// <para>chore/kill-3d-residue: the retired <c>MonsterView3D</c> mesh preview (and its
/// <c>SelectedHasMesh</c> test hook) is replaced by a plain 2D portrait
/// (<see cref="BestiaryPanel.SelectedHasPortrait"/>) with procedural breathe/hover/reveal motion —
/// no <see cref="SubViewport"/>, so no 3D-render-hang concern here at all anymore.</para>
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
    public void OnOpen_AutoSelectsFirstMonsterThatHasAPortrait()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();

            // Venues iterate Id-sorted (emberfall, gloomwood, mine, sunken-crypt); Emberfall's five
            // monsters have no committed portrait, so the first WITH one is Gloomwood F1's Bramble
            // Boar — the viewer is never blank on open.
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsTrue();
            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Bramble Boar");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SelectingVenueMonsterWithPortrait_ShowsPortrait()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            PressEnabled(ui.Bestiary, "Bestiary_crypt-crab"); // Sunken Crypt F1 — has a committed portrait

            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Crypt Crab");
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SelectingMonsterWithoutPortrait_FallsBackGracefully_NoPortrait()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            PressEnabled(ui.Bestiary, "Bestiary_cinder-imp"); // Emberfall — registered, no portrait art yet

            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Cinder Imp");
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsFalse(); // graceful: card, not a crash
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SelectingEmberfallMonster_WithGenMeshButNoPortrait_StillFallsBackGracefully()
    {
        // Slag Hound/Bellows-Mad/Molten Archivist/Undying Forge-Heart each had a retired GLB but
        // were NEVER given a 2D portrait — deleting the mesh tree (chore/kill-3d-residue) drops
        // these four from "shows art" to the same text-only card Cinder Imp always used, since no
        // 2D substitute exists in the repo. Documented art gap (Emberfall isn't in
        // VenueRegistry.LiveRotation yet), not a wiring defect — this pins the graceful-degrade
        // behavior so a future 2D-portrait wave for Emberfall is a pure addition, not a fix.
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            PressEnabled(ui.Bestiary, "Bestiary_slag-hound");

            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Slag Hound");
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void Close_HidesAndClearsThePortrait()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsTrue();

            PressEnabled(ui.Bestiary, "BestiaryClose");

            AssertThat(ui.Bestiary.Visible).IsFalse();
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void Select_StartsFadedIn_ThenProcessRevealsTowardOpaque()
    {
        // The reveal fade-in (PortraitRevealSeconds): a fresh selection starts at alpha 0 and
        // _Process eases it up — never an instant pop, never stuck at 0 (dead sticker) forever.
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll(); // auto-selects Bramble Boar
            var portrait = Find<TextureRect>(ui.Bestiary, "BestiaryPortrait");
            AssertThat(portrait.Modulate.A).IsEqual(0f);

            ui.Bestiary._Process(0.1);
            var firstAlpha = portrait.Modulate.A;
            AssertThat(firstAlpha).IsGreater(0f);

            ui.Bestiary._Process(1.0); // comfortably past PortraitRevealSeconds
            AssertThat(portrait.Modulate.A).IsEqual(1f);
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
