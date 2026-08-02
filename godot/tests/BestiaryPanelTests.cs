#if GDUNIT_TESTS
using GameSim.Venues;
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Panels;
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

            // Venues iterate Id-sorted (emberfall, gloomwood, mine, sunken-crypt). Task #80
            // committed the Emberfall Foundry's backdrop + all five monster portraits, so the
            // first monster WITH a portrait is now Emberfall F1's Cinder Imp — the viewer is
            // never blank on open.
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsTrue();
            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Cinder Imp");
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
    public void SelectingEmberfallMonster_ShowsPortrait()
    {
        // Task #80 closed the gap these two tests used to pin (pre-#80, EVERY Emberfall monster
        // fell back to the text-only card — see git history for
        // `SelectingMonsterWithoutPortrait_FallsBackGracefully_NoPortrait` and
        // `SelectingEmberfallMonster_WithGenMeshButNoPortrait_StillFallsBackGracefully`, both
        // retired here since their premise — a registered monster with no committed portrait —
        // no longer exists anywhere in VenueRegistry.All). Cinder Imp F1 now resolves the same
        // way Gloomwood/Sunken Crypt's monsters already did.
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            PressEnabled(ui.Bestiary, "Bestiary_cinder-imp");

            AssertThat(ui.Bestiary.SelectedKind).IsEqual("Cinder Imp");
            AssertThat(ui.Bestiary.SelectedHasPortrait).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void UnregisteredMonsterKind_StillFallsBackGracefully_NoPortrait()
    {
        // The graceful-degrade contract itself (never a crash on a kind with no art) still needs
        // a live pin now that every REGISTERED monster has a committed portrait (task #80 closed
        // the last gap) — no button in ShowAll's own venue/floor iteration can reach an unknown
        // kind any more, so this calls the same resolver the panel's Select() does directly
        // (AssetCatalogTests.UnknownConcept_HasFalseAndNullReturn_NoThrow pins the non-Godot half
        // of this same contract; this is the Godot-runtime half).
        AssertThat(AssetCatalog.MonsterPortrait("no-such-monster")).IsNull();
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
            ui.Bestiary.ShowAll(); // auto-selects Cinder Imp (see OnOpen_AutoSelectsFirstMonsterThatHasAPortrait)
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

    // U4 (painted-interiors plan): TavernInterior_CarriesABestiaryHotspot_RoutingToTheBestiaryAction
    // deleted — it asserted a row in town.InteriorStage.Venues, the pre-2.5D-pivot staged-overlay
    // table this plan's U4 deletes. It was not covering anything reachable in the live 2.5D game:
    // nothing has routed a click through InteriorStage since the pivot, so the Tavern's Bestiary
    // hotspot was already unreachable before this deletion (R9: slice 1 is the Forge room only —
    // Tavern has no InteriorLayout2D row yet). MainUi.OnInteriorHotspotActivated's own "Bestiary" ->
    // Bestiary.ShowAll() routing stays live and correct for whenever slice 2's Tavern room lands.
}
#endif
