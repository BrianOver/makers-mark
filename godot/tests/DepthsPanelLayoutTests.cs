#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// visfix4: three fit-and-crop defects a design pass found in one frame of the Depths drawer
/// (runs/shots-2026-09-13/Watch.png) — the once-ever header caption ran flush to the drawer's own
/// left edge (0px inset, wrapping under the border), each venue tile stopped at a fixed 360px
/// width inside the ~600px drawer (a ~240px dead column), and every venue's backdrop thumbnail
/// (a 1024x260 wide banner, not a portrait-ish square) letterboxed down to a ~120x30 sliver that
/// read as a broken image. See <see cref="GodotClient.Panels.DepthsPanel"/>'s own remarks at each
/// fix site for the measurements each change is pinned against.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DepthsPanelLayoutTests
{
    [TestCase]
    public void HeaderCaptionInset_MatchesThePanelsOwnContentMargin()
    {
        var ui = MountMainUi();
        try
        {
            var captionHost = Find<MarginContainer>(ui.Depths, "CaptionInset");

            // Phrased against the property, not a re-typed literal: GameTheme.PanelStyle() is the
            // SAME default "panel" StyleBox every OTHER bare PanelContainer on this drawer picks
            // up for free (the DrawerHeader title strip; each venue tile via UiKit.Card()) — its
            // own ContentMarginLeft/Right IS the drawer's content inset. Before this fix the
            // caption was a bare Label with no host at all, so this node did not exist and the
            // caption ran flush to 0px.
            var panelInset = (int)GameTheme.PanelStyle().ContentMarginLeft;
            AssertThat(captionHost.GetThemeConstant("margin_left")).IsEqual(panelInset);
            AssertThat(captionHost.GetThemeConstant("margin_right")).IsEqual(panelInset);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task VenueTileWidth_TracksTheDrawerWidth_NotAFixedConstant()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Depths");
            await SettleLayout(ui);

            var mineTile = Find<PanelContainer>(ui.Depths, "VenueTile_mine");

            // Before this fix the card's width was a fixed CustomMinimumSize (360px) with no
            // expand flag, so a single-column row inside DrawerHost.DrawerWidth (600px) left a
            // ~240px dead column beside it. Asserted against the drawer's own published width,
            // not a re-typed 360 or 600 literal, so this fails again the moment the card stops
            // tracking the drawer.
            AssertThat(mineTile.Size.X).IsGreater(DrawerHost.DrawerWidth * 0.75f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void VenueBackdropThumbnail_UsesCoverCrop_NotLetterboxedSliver()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Depths");

            var mineTile = Find<PanelContainer>(ui.Depths, "VenueTile_mine");
            var art = Find<TextureRect>(mineTile, "ArtRect");

            // Every committed venue backdrop is a 1024x260 wide banner (checked directly), not a
            // portrait-ish square — KeepAspectCentered (UiKit.ArtRect's default, right for
            // portraits/icons) scales that down to fit the square BackdropSize box and letterboxes
            // it to a ~120x30 sliver with dead space above/below, which reads as a broken image.
            // KeepAspectCovered crops instead, so the box's rendered content always fills the
            // box's own aspect. This is the guard that fails the moment anyone reverts to the
            // letterboxing default.
            AssertThat(art.StretchMode).IsEqual(TextureRect.StretchModeEnum.KeepAspectCovered);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AllLiveVenues_StillRenderNameDenStateAndRecords_AfterTheLayoutFix()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Depths");

            var depthsText = RenderedText(ui.Depths);

            // Negative control: none of the three fit-and-crop fixes above (inset host, expand
            // flag, cover crop) should touch WHAT renders — only how it is sized/cropped. Every
            // live venue's own name still appears, read off VenueRegistry.LiveRotation itself
            // (never a hand-listed id set — that family of guard has gone stale on this exact
            // panel before, see the T1/Emberfall live-rotation flips).
            foreach (var venueId in GameSim.Venues.VenueRegistry.LiveRotation)
            {
                var venue = GameSim.Venues.VenueRegistry.Require(venueId);
                AssertThat(depthsText).Contains(venue.DisplayName);
            }

            // Den state (a fresh campaign's venues are all untouched) and the Mine's own board
            // (empty on a fresh campaign) — the two other per-venue fields BuildVenueTile renders.
            AssertThat(depthsText).Contains("den: quiet");
            AssertThat(depthsText).Contains("no records yet — the Mine awaits");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
