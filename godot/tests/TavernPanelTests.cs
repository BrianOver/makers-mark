#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 polish (R14/KTD2/KTD3): the tavern gossip feed rebuilt around one
/// <c>UiKit.Section</c> ("TAVERN GOSSIP") holding a themed <c>Card</c> per line — every
/// scenario proves the same sim read (<see cref="GossipEmitted"/> off <c>state.EventLog</c>,
/// newest-first) the pre-polish panel used still renders, through the real themed Controls,
/// and that the panel is never a blank void on either edge: no gossip yet, or a line whose
/// glyph has no generated art (gossip has no per-line art concept at all).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TavernPanelTests
{
    [TestCase]
    public void GossipLine_Renders_UnderThemedSection_WithDayAndQuoteText()
    {
        var ui = MountMainUi(new SimAdapter(WorldWithGossip()));
        try
        {
            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("TAVERN GOSSIP");
            AssertThat(tavernText).Contains("[day 3]");
            AssertThat(tavernText).Contains("A dagger sold for a fortune.");

            // The section itself renders (themed panel), never a blank void.
            AssertThat(ui.Tavern.FindChildren("*", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshCampaign_NoGossipYet_RendersThemedEmptyState_NotBlankPanel()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.EventLog.IsEmpty).IsTrue();

            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("TAVERN GOSSIP");
            AssertThat(tavernText).Contains("quiet");

            AssertThat(ui.Tavern.FindChildren("*", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GossipLine_HasNoGeneratedArt_RendersArtRectFallback()
    {
        // KTD3 fallback path: a gossip line has no per-line art concept, so ArtRect always
        // misses the manifest and renders the themed placeholder — never a blank hole.
        var ui = MountMainUi(new SimAdapter(WorldWithGossip()));
        try
        {
            var placeholders =
                ui.Tavern.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static GameState WorldWithGossip()
    {
        var baseState = GameFactory.NewGame(9100);
        var gossip = new GossipEmitted(new EventId(1), "A dagger sold for a fortune.")
        {
            Id = new EventId(2),
            Day = 3,
        };
        return baseState with { EventLog = ImmutableList.Create<GameEvent>(gossip) };
    }
}
#endif
