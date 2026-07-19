#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U8 (R15 backstop): the cross-screen render-smoke harness the plan's origin doc
/// explicitly demands ("no test drives the UI loop or asserts label layout"). Data-driven over
/// EVERY tab (<see cref="MainUi.Tabs"/>'s pinned seven — Town/Forge/Shop/Heroes/Tavern/Depths/
/// Bounties, see <see cref="AllTabNames"/>): after a full simulated day, each panel must
/// (1) resolve the U1 Theme cascade at a legible size (<see cref="ThemeReachesPanel"/>),
/// (2) hold at least one child <see cref="Control"/>, and (3) lay out to a real, non-zero
/// footprint (<see cref="HasNonDegenerateLayout"/>) — the general panel-level guard against the
/// "one-character-per-line collapse" *class* <c>LayoutTests</c> hunts at the individual-label
/// level. No pixel snapshots anywhere here — layout non-degeneracy only, per the plan's explicit
/// scope boundary.
///
/// <para>The suite runs twice. Once over the default fresh campaign — whatever mix of committed/
/// absent art that implies (as of art wave 2, Depths' "mine-backdrop" is committed too, so this
/// run no longer exercises a Depths fallback). And once over
/// <see cref="ArtAbsentWorld"/>: a fixture whose Shop shelf holds one item carrying a recipe id no
/// art pipeline ever generated — the "unknown-key probe" this plan's execution note calls for in
/// place of any filesystem trick (committed assets under <c>godot/assets/art/</c> are never
/// deleted/moved). The fixture keeps every hero on a REAL, registered class (unlike
/// <c>HeroRosterTests.FallbackPortraitWorld</c>) specifically so <see cref="AdvanceDay"/> stays
/// tick-safe — an unregistered class only needs to survive a Refresh, never a full day of
/// autonomous party-formation/combat resolution.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UiRenderSmokeTests
{
    private static readonly string[] AllTabNames =
        { "Town", "Forge", "Shop", "Heroes", "Tavern", "Depths", "Bounties" };

    [TestCase]
    public async Task EveryTab_ArtPresent_RendersThemed_WithChildren_NonDegenerateLayout()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);
            foreach (var tabName in AllTabNames)
            {
                await AssertRenderSmoke(ui, tabName);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task EveryTab_ArtAbsent_StillRendersThemed_WithChildren_NonDegenerateLayout()
    {
        var ui = MountMainUi(new SimAdapter(ArtAbsentWorld()));
        try
        {
            AdvanceDay(ui);
            foreach (var tabName in AllTabNames)
            {
                await AssertRenderSmoke(ui, tabName);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ArtAbsentWorld_ForcesArtRectFallback_OnShop()
    {
        // KTD3 evidence: the unknown-key probe this fixture creates (Shop's Mystery Blade recipe
        // id) actually reaches the ArtRect fallback path, not just a panel that happens to still
        // lay out for some unrelated reason. Depths dropped out of this probe in art wave 2 —
        // "mine-backdrop" is now committed, so Depths' ArtRect resolves real art under every
        // fixture (VenueHubTests.VenueBackdropArt_Present_RendersRealArt_NotFallback covers that).
        var ui = MountMainUi(new SimAdapter(ArtAbsentWorld()));
        try
        {
            var panel = PanelFor(ui, "Shop");
            var placeholders =
                panel.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────

    private static async Task AssertRenderSmoke(MainUi ui, string tabName)
    {
        var panel = PanelFor(ui, tabName);
        ui.Tabs.CurrentTab = ui.Tabs.GetTabIdxFromControl(panel); // a hidden tab page never lays out
        await SettleLayout(ui);

        AssertThat(ThemeReachesPanel(panel)).IsTrue();
        AssertThat(panel.GetChildCount() > 0).IsTrue();
        AssertThat(HasNonDegenerateLayout(panel)).IsTrue();
    }

    private static Control PanelFor(MainUi ui, string tabName) => tabName switch
    {
        "Town" => ui.Town,
        "Forge" => ui.Forge,
        "Shop" => ui.Shop,
        "Heroes" => ui.Heroes,
        "Tavern" => ui.Tavern,
        "Depths" => ui.Depths,
        "Bounties" => ui.Bounties,
        _ => throw new ArgumentOutOfRangeException(nameof(tabName), tabName, "no such MainUi tab"),
    };

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static readonly ItemId MysteryItemId = new(501);
    private const int MysteryPrice = 15;

    /// <summary>A fresh campaign — real, registered-class heroes only (tick-safe for
    /// <see cref="AdvanceDay"/>, unlike an unregistered-class fixture) — whose shelf carries one
    /// item with a recipe id no art pipeline ever generated (mirrors
    /// <c>ShopPanelTests.ShelfWithUncommittedArt</c>): the KTD3 unknown-key probe for the Shop
    /// tab. Depths no longer needs (or gets) an override here — "mine-backdrop" is committed as of
    /// art wave 2 (<c>art/specs/mine/MineSpecs.cs</c>), so Depths' ArtRect resolves real art under
    /// this fixture too; see <c>VenueHubTests.VenueBackdropArt_Present_RendersRealArt_NotFallback</c>.</summary>
    private static GameState ArtAbsentWorld()
    {
        var baseState = GameFactory.NewGame(9500);
        var item = new Item(
            MysteryItemId, "no-such-recipe-in-any-manifest", "Mystery Blade", ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(5, 0, 3), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, MysteryPrice)) },
        };
    }
}
#endif
