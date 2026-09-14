#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-22 ("the east field remembers", link 5 — "the outcome becomes the town's memory,
/// with your name in it"): the outdoor memorial wall in the town's open east field. Three
/// properties pinned here: the lantern count is the sim's own fallen-hero record (<see
/// cref="DramaState.Memorials"/>), never a UI-side guess (LAW 4 — show only what the sim decided);
/// the wall's placement stays clear of every other venue/prop/path on today's layout; and
/// "E · Legends" opens the SAME <see cref="GodotClient.Panels.LegendsWall"/> the tavern's
/// "storywall" interior station already opens — never a second book (no participation-credit-style
/// duplicate surface).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MemorialWallTests
{
    private static GameState WorldWithFallenHeroes(int count)
    {
        var state = GameFactory.NewGame(90210);
        var memorials = Enumerable.Range(1, count)
            .Select(i => new Memorial(new HeroId(200 + i), $"Fallen {i}", Day: i, GearNamed: "a worn blade"))
            .ToImmutableList();
        return state with { Drama = state.Drama with { Memorials = memorials } };
    }

    // ── Property: lantern count == the sim's own fallen-hero record, for any roster ────────────

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(6)]
    public void LanternCount_MatchesTheSimsFallenHeroRecord_ForAnyRoster(int fallenCount)
    {
        var ui = MountMainUi(new SimAdapter(WorldWithFallenHeroes(fallenCount)));
        try
        {
            AssertThat(ui.Town.MemorialWall)
                .OverrideFailureMessage("The memorial wall must exist regardless of roster.")
                .IsNotNull();
            AssertThat(ui.Town.MemorialLanternCount)
                .OverrideFailureMessage(
                    $"The wall showed {ui.Town.MemorialLanternCount} lantern(s) against " +
                    $"{fallenCount} recorded deaths (DramaState.Memorials) — the lantern count must " +
                    "be the sim's own record, never a UI-side guess.")
                .IsEqual(fallenCount);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ZeroDeaths_TheWallStillStands_EmptyAndHonest()
    {
        var ui = MountMainUi(new SimAdapter(WorldWithFallenHeroes(0)));
        try
        {
            AssertThat(ui.Town.MemorialWall)
                .OverrideFailureMessage("An empty memorial is honest — the wall must not be hidden when nobody has died yet.")
                .IsNotNull();
            AssertThat(ui.Town.MemorialLanternCount).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Placement: inside the grid, clear of every existing venue/prop/path ────────────────────

    [TestCase]
    public void MemorialWall_SitsInsideTheGrid_ClearOfEveryVenueAndProp()
    {
        var wallRect = Rect(TownLayout2D.TileToWorld(TownLayout2D.MemorialWallTile), TownAssets2D.ForVenue(TownLayout2D.MemorialWallSpriteId).GetSize());

        var gridWidthPx = TownLayout2D.GridWidth * TownLayout2D.TileSize;
        var gridHeightPx = TownLayout2D.GridHeight * TownLayout2D.TileSize;
        AssertThat(wallRect.Left >= 0f && wallRect.Top >= 0f
                && wallRect.Right <= gridWidthPx && wallRect.Bottom <= gridHeightPx)
            .OverrideFailureMessage(
                $"Memorial wall rect ({wallRect.Left},{wallRect.Top})..({wallRect.Right},{wallRect.Bottom}) " +
                $"falls outside the {TownLayout2D.GridWidth}x{TownLayout2D.GridHeight}-tile grid.")
            .IsTrue();

        var offenders = new List<string>();

        foreach (var venue in TownLayout2D.Venues)
        {
            var rect = Rect(TownLayout2D.TileToWorld(venue.Tile), TownAssets2D.ForVenue(venue.SpriteId).GetSize());
            if (Overlap(wallRect, rect))
            {
                offenders.Add($"venue:{venue.Key}");
            }
        }

        var placementIndex = 0;
        foreach (var prop in TownLayout2D.Props)
        {
            var rect = Rect(TownLayout2D.TileToWorld(prop.Tile), TownAssets2D.ForProp(prop.SpriteId, placementIndex).GetSize());
            if (Overlap(wallRect, rect))
            {
                offenders.Add($"prop:{prop.SpriteId}@({prop.Tile.X},{prop.Tile.Y})");
            }

            placementIndex++;
        }

        foreach (var path in TownLayout2D.PathRects)
        {
            var pathRect = (
                Left: path.Position.X * (float)TownLayout2D.TileSize,
                Top: path.Position.Y * (float)TownLayout2D.TileSize,
                Right: (path.Position.X + path.Size.X) * (float)TownLayout2D.TileSize,
                Bottom: (path.Position.Y + path.Size.Y) * (float)TownLayout2D.TileSize);
            if (Overlap(wallRect, pathRect))
            {
                offenders.Add($"path:({path.Position.X},{path.Position.Y},{path.Size.X},{path.Size.Y})");
            }
        }

        AssertThat(string.Join(", ", offenders))
            .OverrideFailureMessage($"Memorial wall overlaps: {string.Join(", ", offenders)}")
            .IsEqual(string.Empty);
    }

    private static (float Left, float Top, float Right, float Bottom) Rect(Vector2 bottomCenter, Vector2 size) =>
        (bottomCenter.X - size.X / 2f, bottomCenter.Y - size.Y, bottomCenter.X + size.X / 2f, bottomCenter.Y);

    private static bool Overlap((float Left, float Top, float Right, float Bottom) a, (float Left, float Top, float Right, float Bottom) b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    // ── "E · Legends" opens the SAME book, never a duplicate ───────────────────────────────────

    [TestCase]
    public void MemorialWallPress_OpensTheSameLegendsWall_TheTavernStorywallOpens()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("tavern").RaisePick();
            var storywall = ui.Town.FindInteriorRoom("tavern").Stations.First(s => s.Key == "storywall");
            storywall.RaisePick();

            var openedByTavern = ui.Legends;
            AssertThat(openedByTavern.Visible).IsTrue();
            openedByTavern.Close();

            ui.Town.MemorialWall!.RaisePick();

            AssertThat(ui.Legends.Visible)
                .OverrideFailureMessage("E at the memorial wall must open the Legends book.")
                .IsTrue();
            AssertThat(ReferenceEquals(ui.Legends, openedByTavern))
                .OverrideFailureMessage(
                    "The memorial wall opened a DIFFERENT Legends surface than the tavern's " +
                    "storywall — this must reuse the one existing book, never a duplicate.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void MemorialWallPress_OpensLegends_NotADrawerPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.MemorialWall!.RaisePick();

            AssertThat(ui.Legends.Visible).IsTrue();
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Legends is a code-built modal, not a drawer panel.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
