#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-03 (§11.15): the live-mount half of the discovery proof. <see
/// cref="GameSim.Tests.Presentation.SurfaceClaimDiscoveryCensusTests"/> (fast lane) is a text scan —
/// it proves the SOURCE declares a claim for every surface shaped like a claimable modal. This suite
/// proves the DISCOVERED set actually matches at runtime: every one of the full-rect modal
/// surfaces <c>MainUi.BuildUi</c> constructs is found by <see cref="SurfaceArbiter.Discover"/> once
/// mounted — the exact row <c>MainUi.OverlaySurfaces()</c>'s own hand-written array used to miss.
/// P2-LONG-18 added a ninth claim (<c>Pledge</c>); P2-MEMORY-14 later retired the original
/// <c>Chronicle</c> claim (its duty moved onto <c>Legends</c>), leaving eight today.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SurfaceArbiterDiscoveryTests
{
    [TestCase]
    public void Discover_FindsAllEightFullScreenModalClaims()
    {
        var ui = MountMainUi();
        try
        {
            // P2-SCREEN-04: filtered to FullScreenModal first — MountMainUi now also constructs
            // CompanionDock's own HudDock claim and up to four ChildModal ProvenanceCard claims
            // (one per hosting panel, all built eagerly at boot — P2-MEMORY-11 dropped LegendsWall
            // from that list), so an unfiltered Discover() is no longer exactly eight. The EIGHT
            // full-rect modals this test pins are unaffected by either.
            var claims = SurfaceArbiter.Discover(ui.GetTree())
                .Where(c => c.Claim.Region == SurfaceRegion.FullScreenModal)
                .ToList();
            var ids = claims.Select(c => c.Claim.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

            string[] expected =
            [
                "Camp", "Commissions", "Forecast", "Ledger", "Legends",
                "Mirror", "Pledge", "SystemMenu",
            ];
            AssertThat(ids.Count).IsEqual(expected.Length);
            foreach (var id in expected)
            {
                AssertThat(ids.Contains(id))
                    .OverrideFailureMessage($"Expected \"{id}\" among discovered claims: [{string.Join(", ", ids)}]")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>P2-SCREEN-04: the "required property, all rows must answer" invariant — every
    /// FullScreenModal claim (the eight surfaces <see cref="MainUi.OverlaySurfaces"/> now projects)
    /// answers <see cref="SurfaceClaim.OwnsScreen"/> true. Renamed from the P2-SCREEN-03 original
    /// (<c>EveryDiscoveredClaim_IsInTheFullScreenModalRegion</c>), which is no longer true of every
    /// DISCOVERED claim now that <c>CompanionDock</c> (<see cref="SurfaceRegion.HudDock"/>) and
    /// <c>ProvenanceCard</c> (<see cref="SurfaceRegion.ChildModal"/>) also register themselves.</summary>
    [TestCase]
    public void EveryFullScreenModalClaim_OwnsTheScreen()
    {
        var ui = MountMainUi();
        try
        {
            var claims = SurfaceArbiter.Discover(ui.GetTree())
                .Where(c => c.Claim.Region == SurfaceRegion.FullScreenModal)
                .ToList();
            AssertThat(claims.Count).IsGreater(0);
            foreach (var (claim, _) in claims)
            {
                AssertThat(claim.OwnsScreen)
                    .OverrideFailureMessage($"\"{claim.Id}\" is a FullScreenModal claim with OwnsScreen: false.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>P2-SCREEN-04: <c>CompanionDock</c>'s own declared exclusion — a stated
    /// <c>OwnsScreen: false</c> in its own region, not a silent absence from any list (the exact
    /// defect shape this unit fixes for Chronicle).</summary>
    [TestCase]
    public void Docket_DeclaresItDoesNotOwnTheScreen()
    {
        var ui = MountMainUi();
        try
        {
            var docket = SurfaceArbiter.Discover(ui.GetTree())
                .FirstOrDefault(c => c.Claim.Id == "Docket");

            AssertThat(docket.Surface)
                .OverrideFailureMessage("Expected a \"Docket\" claim to be discovered.")
                .IsNotNull();
            AssertThat(docket.Claim.Region).IsEqual(SurfaceRegion.HudDock);
            AssertThat(docket.Claim.OwnsScreen).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>P2-SCREEN-04 proof requirement: a provenance card opened from any of its hosts
    /// registers a claim. All four (<c>ShopPanel</c>/<c>HeroesPanel</c>/<c>TavernPanel</c>/
    /// <c>ScryingMirror</c> — P2-MEMORY-11 moved <c>LegendsWall</c>'s own item rows onto book pages
    /// instead of this popup, so it is no longer one of them) build their own
    /// <c>ProvenanceCard</c> child eagerly at boot (mirroring the eight modals' own eager
    /// construction), so all four claims are discoverable the instant <c>MainUi</c> mounts — proof
    /// that the claim lives on <c>ProvenanceCard</c>'s own constructor rather than on any one host,
    /// the fix for a class that "cannot be hand-listed, being instantiated per hosting panel" (unit
    /// body).</summary>
    [TestCase]
    public void ProvenanceCard_RegistersAClaim_FromEveryOneOfItsHosts()
    {
        var ui = MountMainUi();
        try
        {
            var cardClaims = SurfaceArbiter.Discover(ui.GetTree())
                .Where(c => c.Claim.Id == "ProvenanceCard")
                .ToList();

            AssertThat(cardClaims.Count)
                .OverrideFailureMessage(
                    $"Expected 4 ProvenanceCard claims (one per host), found {cardClaims.Count}.")
                .IsEqual(4);
            foreach (var (claim, _) in cardClaims)
            {
                AssertThat(claim.Region).IsEqual(SurfaceRegion.ChildModal);
                AssertThat(claim.OwnsScreen).IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DiscoveredSurface_IsTheExactSameInstanceMainUiExposes()
    {
        var ui = MountMainUi();
        try
        {
            var claims = SurfaceArbiter.Discover(ui.GetTree());
            var ledgerEntry = claims.FirstOrDefault(c => c.Claim.Id == "Ledger");

            AssertThat(ledgerEntry.Surface).IsEqual(ui.Ledger);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Observing-only proof: mounting and immediately unmounting must not throw and must
    /// leave no dangling claim behind (the metadata dies with the freed node — see
    /// <see cref="SurfaceArbiter.Claim"/>'s own doc for why that's the deliberate design over a
    /// static instance-id-keyed dictionary).</summary>
    [TestCase]
    public void UnmountingAndRemountingMainUi_DoesNotAccumulateStaleClaims()
    {
        var first = MountMainUi();
        var firstCount = SurfaceArbiter.Discover(first.GetTree()).Count;
        Unmount(first);

        var second = MountMainUi();
        try
        {
            var secondCount = SurfaceArbiter.Discover(second.GetTree()).Count;
            AssertThat(secondCount).IsEqual(firstCount);
        }
        finally
        {
            Unmount(second);
        }
    }
}
#endif
