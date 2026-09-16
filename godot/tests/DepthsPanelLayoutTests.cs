#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient;
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

    /// <summary>
    /// Owner ruling, 2026-09-15 ("Two fold budgets the 481px ruling could not close, with the
    /// arithmetic", <c>MAKERS-MARK.md</c>): with a party underground, <see
    /// cref="Panels.MineWatch"/>'s 260px strip stays exactly as it is (declined: not this
    /// panel's to shrink) — but the once-ever "read-only-surfaces" caption's own ~79px was being
    /// reserved FOREVER once shown, on every later visit, not just the one that earned it. This
    /// pins the reclaim: a first visit still shows the caption (unchanged), and a second visit
    /// has the Mine's own tile higher by exactly the caption's own MEASURED height — never a
    /// hard-coded 79, so a different font/platform re-measuring a different caption height still
    /// pins the right relationship.
    /// </summary>
    [TestCase]
    public async Task OnceEverCaption_RetiresOnSecondVisit_ReclaimingItsHeightForTheFirstTile()
    {
        var ui = MountMainUi(new SimAdapter(PartyDeepInTheMine()));
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            ui.RefreshAll(); // MineWatch is refreshed centrally by MainUi, "regardless of host" (U9)

            // First visit: a fresh campaign has never consumed "read-only-surfaces" — the
            // caption must still fire and reserve real height, exactly as before this fix.
            ui.OpenPanel("Depths");
            await SettleLayout(ui);

            var caption = Find<Label>(ui.Depths, "OnceEverCaption");
            AssertThat(caption.Visible)
                .OverrideFailureMessage(
                    "first visit: the once-ever caption did not show at all — first view must stay " +
                    "unchanged by this fix.")
                .IsTrue();
            AssertThat(caption.Text).IsNotEmpty();

            var captionHeight = caption.GetGlobalRect().Size.Y;
            AssertThat(captionHeight)
                .OverrideFailureMessage(
                    "the caption measured zero height while visible — this test proves nothing without a " +
                    "real reserved height to reclaim.")
                .IsGreater(0f);

            var firstTileYWithCaption = Find<PanelContainer>(ui.Depths, "VenueTile_mine").GetGlobalRect().Position.Y;

            // Leave (a real navigation — RefreshAll ticks Depths.Refresh() every frame it stays
            // open, live party or not, so only an actual re-open counts as "come back") and
            // return.
            ui.OpenPanel("Heroes");
            await SettleLayout(ui);
            ui.OpenPanel("Depths");
            await SettleLayout(ui);

            AssertThat(caption.Visible)
                .OverrideFailureMessage(
                    "second visit: the once-ever caption is STILL reserving space. Owner ruling " +
                    "2026-09-15 is to retire it once it has been read, freeing its height for the venue " +
                    "tile below it.")
                .IsFalse();

            var firstTileYWithoutCaption =
                Find<PanelContainer>(ui.Depths, "VenueTile_mine").GetGlobalRect().Position.Y;

            const float tolerancePx = 1f;
            AssertThat(Mathf.Abs((firstTileYWithCaption - captionHeight) - firstTileYWithoutCaption) <= tolerancePx)
                .OverrideFailureMessage(
                    $"first tile sat at y={firstTileYWithCaption} with the caption showing " +
                    $"({captionHeight}px tall) and y={firstTileYWithoutCaption} on the second visit — " +
                    "expected it to rise by exactly the caption's own measured height, not some other " +
                    "amount.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A real party camped in the Mine (<see cref="DayPhase.Camp"/> + a live <see
    /// cref="InFlightExpedition"/>) — the "party underground" half of the 260+79=339px
    /// arithmetic the 2026-09-14 ruling could not close (<c>MAKERS-MARK.md</c>, "Two fold
    /// budgets"), built off the default campaign's own heroes so <see cref="Panels.MineWatch"/>'s
    /// Hp-driven rendering has real data to read.</summary>
    private static GameState PartyDeepInTheMine()
    {
        var baseState = GameComposition.NewCampaign(seed: 9146);
        var party = baseState.Heroes.Values.Take(3).Select(h => h.Id).ToImmutableList();
        var camp = new InFlightExpedition(
            Party: party,
            TargetFloor: 2,
            CheckpointFloor: 1,
            VenueId: "mine",
            Hp: party.ToImmutableSortedDictionary(id => id.Value, id => baseState.Heroes[id.Value].MaxHp),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);

        return baseState with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };
    }
}
#endif
