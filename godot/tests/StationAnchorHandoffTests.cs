#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T9-5 (§11.14.13): a Station anchor has to point at something the player can actually see.
///
/// <para><b>The defect.</b> <c>Town2D.FindStation</c> resolves through
/// <c>FindInteriorRoom(venueKey).Stations</c> — a node inside the building. Steps 1, 2 and 7 all
/// say "Walk to the {building}, then press E" and all three anchored straight to a station inside
/// it, so while the player was still out in the town the only pulse in the game was behind a wall
/// they had not walked through. Steps 1 and 2 are the player's first two actions in the whole game.
/// Steps 3/4/5 use Building anchors and have always worked — the pulse mechanism was right and the
/// aim was wrong.</para>
///
/// <para><b>Why no new data was needed.</b> A Station anchor's own <c>Key</c> IS the venue key —
/// that is how <c>NotifyEnteredBuilding</c>'s "✓ Arrived" ratchet already works — so the town
/// building was already named in the anchor the whole time. <c>AnchorFor</c> reads the player's live
/// location in the same vocabulary <c>IsAtAnchor</c> does, so the pulse and the card's own "You're
/// at the ..." acknowledgement cannot disagree about where the player is standing.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class StationAnchorHandoffTests
{
    /// <summary>Every registry step whose declared anchor is a Station, so this covers the family
    /// rather than a hand-listed two or three. A hand-listed set stops covering the day someone adds
    /// a fourth Station step — which is exactly how this defect reached three steps in the first
    /// place.</summary>
    private static string[] StationAnchoredVenues() =>
        TutorialFlow.Registry
            .Where(def => def.Anchor.Kind == TutorialAnchorKind.Station)
            .Select(def => def.Anchor.Key!)
            .Distinct()
            .ToArray();

    [TestCase]
    public void EveryStationAnchoredStep_PointsAtTheBuildingWhileThePlayerIsOutside()
    {
        var ui = MountMainUi();
        try
        {
            var venues = StationAnchoredVenues();
            AssertThat(venues.Length)
                .OverrideFailureMessage("Fixture guard: no step declares a Station anchor, so this test proves nothing.")
                .IsGreater(0);

            foreach (var def in TutorialFlow.Registry.Where(d => d.Anchor.Kind == TutorialAnchorKind.Station))
            {
                // null openPanelId == out in the town, not inside any venue and no panel open.
                var aimed = TutorialFlow.AimAnchor(def.Anchor, null);

                AssertThat(aimed.Kind)
                    .OverrideFailureMessage(
                        $"Step {def.Step} tells the player to walk to a building and then pulses a "
                        + "station inside it. While they are still outside, the pulse has to be on "
                        + "the building — a highlight behind a wall is not a highlight.")
                    .IsEqual(TutorialAnchorKind.Building);
                AssertThat(aimed.Key)
                    .OverrideFailureMessage("The building pulsed must be the station's own venue, not some other building.")
                    .IsEqual(def.Anchor.Key);
            }
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void OnceInside_TheAnchorHandsOffToTheStationItself()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var def in TutorialFlow.Registry.Where(d => d.Anchor.Kind == TutorialAnchorKind.Station))
            {
                var venue = def.Anchor.Key!;
                var aimed = TutorialFlow.AimAnchor(def.Anchor, TutorialFlow.PanelIdForVenue(venue));

                AssertThat(aimed.Kind)
                    .OverrideFailureMessage(
                        $"Step {def.Step}: once the player is inside {venue}, the station is the thing "
                        + "worth pointing at — the whole point of the Station kind. Handing them a "
                        + "building-wide pulse in a room they are standing in says nothing.")
                    .IsEqual(TutorialAnchorKind.Station);
                AssertThat(aimed.Key).IsEqual(venue);
                AssertThat(aimed.StationId)
                    .OverrideFailureMessage("The station id must survive the handoff untouched.")
                    .IsEqual(def.Anchor.StationId);
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>The live path, on the one step a test can reach without forcing anything: day 1
    /// opens on BuyMaterial, whose declared anchor is a Station in the forge. Standing in the town,
    /// the pulse must be on the forge building — this is the exact frame a new player sees first.</summary>
    [TestCase]
    public void OnDayOne_StandingInTheTown_ThePulseIsOnTheForgeItself()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            AssertThat(ui.Tutorial.CurrentAnchor.Kind)
                .OverrideFailureMessage("Fixture guard: step 1 is supposed to declare a Station anchor.")
                .IsEqual(TutorialAnchorKind.Station);

            var aimed = ui.Tutorial.AnchorFor(ui.Adapter.CurrentState, null);

            AssertThat(aimed.Kind)
                .OverrideFailureMessage(
                    "The very first instruction in the game says \"Walk to the Forge\" and the only "
                    + "pulse was inside the forge. Outside, it must be the building.")
                .IsEqual(TutorialAnchorKind.Building);
            AssertThat(aimed.Key).IsEqual("forge");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The handoff must not touch the kinds that were already correct. Building anchors
    /// point at buildings and Hud anchors at controls, inside or outside, location irrelevant.</summary>
    [TestCase]
    public void NonStationAnchors_AreUnchangedByTheHandoff()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var def in TutorialFlow.Registry.Where(d => d.Anchor.Kind != TutorialAnchorKind.Station))
            {
                foreach (var location in new string?[] { null, "Forge", "Shop" })
                {
                    AssertThat(TutorialFlow.AimAnchor(def.Anchor, location))
                        .OverrideFailureMessage(
                            $"Step {def.Step} declares a {def.Anchor.Kind} anchor and the handoff "
                            + "rewrote it. Only Station anchors have two phases.")
                        .IsEqual(def.Anchor);
                }
            }
        }
        finally { Unmount(ui); }
    }
}
#endif
