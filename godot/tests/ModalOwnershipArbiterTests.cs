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
    private static readonly ItemId ManifestItemId = new(902);
    private static readonly HeroId ManifestHeroId = new(21);

    private static Item ManifestItem() => new(
        ManifestItemId, "recipe-manifest", "Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(10, 0, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>A staged party wearing one player-marked weapon — the minimal fixture <see
    /// cref="ScryingMirror"/>'s own Manifest section renders unconditionally (a roster fact, not a
    /// beat — see that panel's own doc), giving a <c>ManifestLine_</c> button the instant the
    /// Mirror opens, with no tick and no reveal wait (the <c>JourneyStreamTests</c>
    /// <c>Camp_Phase_StagedParty_ManifestAlsoPresent...</c> precedent: <c>InFlight</c> + <c>Phase
    /// == Camp</c> is enough, <c>Adapter.LastEvents</c> can stay empty).</summary>
    private static GameState ManifestWorld()
    {
        var hero = new Hero(
            ManifestHeroId, "Wearer", "vanguard", Level: 3, MaxHp: 40, Gold: 10,
            new GearSet(ManifestItemId, null, null), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(ManifestHeroId),
            TargetFloor: 2,
            CheckpointFloor: 1,
            VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(ManifestHeroId.Value, 40),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);

        return GameFactory.NewGame(9402) with
        {
            Phase = DayPhase.Camp,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(ManifestHeroId.Value, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(ManifestItemId.Value, ManifestItem()),
            InFlight = ImmutableList.Create(inFlight),
        };
    }

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
    /// release the host's own ownership. P2-MEMORY-11 moved <c>LegendsWall</c>'s own item rows off
    /// this popup and onto the book's own pages instead (<c>LegendsWall.ShowItemPage</c>), so this
    /// invariant needs a different live host to prove itself against — the Mirror is the other real
    /// <see cref="SurfaceRegion.FullScreenModal"/> claim among ProvenanceCard's remaining hosts
    /// (unlike Shop/Heroes/Tavern, drawer panels with no arbiter claim of their own).</summary>
    [TestCase]
    public void ProvenanceCardOverMirror_DoesNotReleaseTheHostsOwnClaim()
    {
        var ui = MountMainUi(new SimAdapter(ManifestWorld()));
        try
        {
            ui.Mirror.Refresh(); // pure read off Adapter.CurrentState — no tick needed
            ui.Mirror.ShowMirror();
            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("setup check: the staged-party fixture must open the mirror.")
                .IsTrue();
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("setup check: opening the Mirror must already hold the clock.")
                .IsTrue();

            PressEnabled(ui.Mirror, $"ManifestLine_{ManifestItemId.Value}_{ManifestHeroId.Value}");

            var card = Find<ProvenanceCard>(ui.Mirror, "ProvenanceCard");
            AssertThat(card.Visible)
                .OverrideFailureMessage("setup check: the manifest press must open the provenance card.")
                .IsTrue();

            // The host's OWN claim must still be discovered, and still visible — opening the card
            // never touched Mirror.Visible, and nothing in the arbiter should read the presence of
            // a higher-precedence ChildModal claim as the host's own claim disappearing.
            var mirrorClaim = SurfaceArbiter.Discover(ui.GetTree())
                .FirstOrDefault(c => c.Claim.Id == "Mirror");
            AssertThat(mirrorClaim.Surface)
                .OverrideFailureMessage("The Mirror claim vanished from Discover() while its ProvenanceCard was open.")
                .IsNotNull();
            AssertThat(mirrorClaim.Surface!.Visible)
                .OverrideFailureMessage("Mirror reads as closed while its own ProvenanceCard is open over it.")
                .IsTrue();

            var cardClaim = SurfaceArbiter.Discover(ui.GetTree())
                .FirstOrDefault(c => c.Claim.Id == "ProvenanceCard" && c.Surface == card);
            AssertThat(cardClaim.Surface).IsNotNull();
            AssertThat(cardClaim.Claim.Region).IsEqual(SurfaceRegion.ChildModal);

            // AnOverlayOwnsTheScreen()'s own effects must still hold too — the card sitting on top
            // changes nothing about the fact the screen is owned.
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("The clock released while a ProvenanceCard sat open over Mirror.")
                .IsTrue();
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("World input re-enabled while a ProvenanceCard sat open over Mirror.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
