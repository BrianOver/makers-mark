#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-04 (§11.15): <c>MainUi.OverlaySurfaces()</c> is now a projection over <see
/// cref="SurfaceArbiter.Discover"/> instead of a hand-written eight-row array that was missing
/// exactly one real full-rect modal — <c>ChronicleScroll</c>, so the campaign's ending ceremony ran
/// with the clock live, world input open, and PiP undimmed. This suite is the runtime proof the unit
/// body's own test scenarios ask for: opening the Chronicle now holds the clock, blocks world input,
/// suppresses PiP, and hides the objective card; and a nested <see cref="ProvenanceCard"/> opened
/// over a real <see cref="SurfaceRegion.FullScreenModal"/> host never reads as that host releasing
/// its own claim on the screen.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ModalOwnershipArbiterTests
{
    private static readonly ItemId SignedItemId = new(901);

    private static Item SignedItem() => new(
        SignedItemId, "recipe-signed", "Longsword", ItemSlot.Weapon, QualityGrade.Masterwork,
        new ItemStats(20, 0, 5), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
    {
        SignedName = "Emberfall",
    };

    /// <summary>A world with one Signed Work — the minimal fixture <see
    /// cref="LegendsWallTests"/>'s own <c>PopulatedWorld</c> already proves opens a legend row's
    /// <see cref="ProvenanceCard"/>.</summary>
    private static GameState SignedItemWorld() =>
        GameFactory.NewGame(9401) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(SignedItemId.Value, SignedItem()),
        };

    [TestCase]
    public void OpeningChronicle_HoldsTheClock_BlocksWorldInput_SuppressesPip_AndHidesTheObjectiveCard()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Clock.Engaged).IsFalse();
            AssertThat(ui.Town.WorldInputNode.Enabled).IsTrue();
            AssertThat(ui.Pip.Suppressed).IsFalse();

            ui.Chronicle.ShowFor(new CampaignEnded(
                DeepestFloorReached: 5, MemorialCount: 1, HonoredMemorialCount: 1,
                AttributionBeatCount: 3, GossipHighlightCount: 2, LegendaryHeroCount: 1));

            AssertThat(ui.Chronicle.Visible)
                .OverrideFailureMessage("setup check: ShowFor must actually open the scroll.")
                .IsTrue();

            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage(
                    "Opening the Chronicle did not hold the clock — OverlaySurfaces() still omits it.")
                .IsTrue();
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("Opening the Chronicle left world input live.")
                .IsFalse();
            AssertThat(ui.Pip.Suppressed)
                .OverrideFailureMessage("Opening the Chronicle left the PiP dock undimmed.")
                .IsTrue();
            AssertThat(ui.Objective.Visible)
                .OverrideFailureMessage("Opening the Chronicle left the objective card drawing over it.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ClosingChronicle_ReleasesTheClock_WorldInput_AndPip()
    {
        var ui = MountMainUi();
        try
        {
            ui.Chronicle.ShowFor(new CampaignEnded(1, 0, 0, 0, 0, 0));
            AssertThat(ui.Clock.Engaged).IsTrue(); // setup check

            ui.Chronicle.CloseScroll();

            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("Closing the Chronicle left the clock latched.")
                .IsFalse();
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("Closing the Chronicle left world input blocked.")
                .IsTrue();
            AssertThat(ui.Pip.Suppressed)
                .OverrideFailureMessage("Closing the Chronicle left the PiP dock suppressed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Proof requirement 2: a <see cref="ProvenanceCard"/> open over its host does NOT
    /// release the host's own ownership. Legends is a real <see cref="SurfaceRegion.FullScreenModal"/>
    /// claim (unlike three of ProvenanceCard's other four hosts, which are drawer panels with no
    /// arbiter claim of their own) — the host this test can actually prove the invariant against.</summary>
    [TestCase]
    public void ProvenanceCardOverLegends_DoesNotReleaseTheHostsOwnClaim()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(SignedItemWorld());
            AssertThat(ui.Legends.Visible)
                .OverrideFailureMessage("setup check: the Signed Work fixture must open the wall.")
                .IsTrue();
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("setup check: opening Legends must already hold the clock.")
                .IsTrue();

            PressEnabled(ui.Legends, $"Legend_{SignedItemId.Value}");

            var card = Find<ProvenanceCard>(ui.Legends, "ProvenanceCard");
            AssertThat(card.Visible)
                .OverrideFailureMessage("setup check: the History press must open the provenance card.")
                .IsTrue();

            // The host's OWN claim must still be discovered, and still visible — opening the card
            // never touched Legends.Visible, and nothing in the arbiter should read the presence of
            // a higher-precedence ChildModal claim as the host's own claim disappearing.
            var legendsClaim = SurfaceArbiter.Discover(ui.GetTree())
                .FirstOrDefault(c => c.Claim.Id == "Legends");
            AssertThat(legendsClaim.Surface)
                .OverrideFailureMessage("The Legends claim vanished from Discover() while its ProvenanceCard was open.")
                .IsNotNull();
            AssertThat(legendsClaim.Surface!.Visible)
                .OverrideFailureMessage("Legends reads as closed while its own ProvenanceCard is open over it.")
                .IsTrue();

            var cardClaim = SurfaceArbiter.Discover(ui.GetTree())
                .FirstOrDefault(c => c.Claim.Id == "ProvenanceCard" && c.Surface == card);
            AssertThat(cardClaim.Surface).IsNotNull();
            AssertThat(cardClaim.Claim.Region).IsEqual(SurfaceRegion.ChildModal);

            // AnOverlayOwnsTheScreen()'s own effects must still hold too — the card sitting on top
            // changes nothing about the fact the screen is owned.
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("The clock released while a ProvenanceCard sat open over Legends.")
                .IsTrue();
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("World input re-enabled while a ProvenanceCard sat open over Legends.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
