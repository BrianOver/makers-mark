#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U6 (R12/R11/R15/KTD3): the Depths board reframed as a venue-map hub — one backdrop
/// <c>UiKit.ArtRect</c> tile (the sim's one venue-of-record, see <c>DepthsPanel</c>'s type
/// remarks for why exactly one) holding the SAME <see cref="DramaState.DepthsBoard"/> read/
/// ordering the pre-rethink flat board used. Every scenario proves that survives: standings
/// text, the themed "no records yet" empty state, and the KTD3 art-fallback guarantee.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class VenueHubTests
{
    [TestCase]
    public void MidGame_WithStandings_RendersMineTile_DeepestFirst()
    {
        var ui = MountMainUi(new SimAdapter(BoardWorld()));
        try
        {
            var depthsText = RenderedText(ui.Depths);
            AssertThat(depthsText).Contains("The Mine");
            AssertThat(depthsText).Contains("floor 5 — Delver1");
            AssertThat(depthsText).Contains("floor 3 — Delver2");

            // Deepest-first ordering preserved (the pre-rethink DepthsPanel contract).
            var floor5At = depthsText.IndexOf("floor 5 — Delver1", StringComparison.Ordinal);
            var floor3At = depthsText.IndexOf("floor 3 — Delver2", StringComparison.Ordinal);
            AssertThat(floor5At >= 0 && floor3At > floor5At).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshCampaign_EmptyBoard_RendersThemedNoRecordsTile_NotBlankPanel()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Drama.DepthsBoard.IsEmpty).IsTrue();

            var depthsText = RenderedText(ui.Depths);
            AssertThat(depthsText).Contains("The Mine");
            AssertThat(depthsText).Contains("no records yet — the Mine awaits");

            // The tile itself still renders — never a blank panel.
            AssertThat(ui.Depths.FindChildren("VenueTile_mine", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void VenueBackdropArt_Present_RendersRealArt_NotFallback()
    {
        // "mine-backdrop" is committed as of art wave 2 (art/specs/mine/MineSpecs.cs) — the
        // KTD3 fallback path this test used to exercise no longer applies to the Mine tile;
        // UiRenderSmokeTests.ArtAbsentWorld_ForcesArtRectFallback_OnShop still proves the
        // fallback contract itself via a genuinely-unregistered recipe id.
        var ui = MountMainUi();
        try
        {
            var realArt = ui.Depths.FindChildren("ArtRect", "TextureRect", recursive: true, owned: false);
            AssertThat(realArt.Count > 0).IsTrue();
            var placeholders = ui.Depths.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count == 0).IsTrue();
            AssertThat(RenderedText(ui.Depths)).Contains("The Mine");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Hero Delver(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameState BoardWorld()
    {
        var heroes = new[] { Delver(1, "Delver1"), Delver(2, "Delver2") }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var board = ImmutableSortedDictionary<int, int>.Empty.Add(1, 5).Add(2, 3);
        return GameFactory.NewGame(9010) with
        {
            Heroes = heroes,
            Drama = DramaState.Empty with { DepthsBoard = board },
        };
    }
}
#endif
