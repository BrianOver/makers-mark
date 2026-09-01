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
/// proves the DISCOVERED set actually matches at runtime: every one of the nine full-rect modal
/// surfaces <c>MainUi.BuildUi</c> constructs is found by <see cref="SurfaceArbiter.Discover"/> once
/// mounted, <c>Chronicle</c> included — the exact row <c>MainUi.OverlaySurfaces()</c>'s own
/// hand-written array is missing.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SurfaceArbiterDiscoveryTests
{
    [TestCase]
    public void Discover_FindsAllNineFullScreenModalClaims_ChronicleIncluded()
    {
        var ui = MountMainUi();
        try
        {
            var claims = SurfaceArbiter.Discover(ui.GetTree());
            var ids = claims.Select(c => c.Claim.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

            string[] expected =
            [
                "Bestiary", "Camp", "Chronicle", "Commissions", "Forecast", "Ledger", "Legends",
                "Mirror", "SystemMenu",
            ];
            AssertThat(ids.Count).IsEqual(expected.Length);
            foreach (var id in expected)
            {
                AssertThat(ids.Contains(id))
                    .OverrideFailureMessage($"Expected \"{id}\" among discovered claims: [{string.Join(", ", ids)}]")
                    .IsTrue();
            }

            // The one row MainUi.OverlaySurfaces()'s hand-written array is missing — proven found
            // here by DISCOVERY, never by adding a tenth hand-written string to a second list.
            AssertThat(ids.Contains("Chronicle")).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveryDiscoveredClaim_IsInTheFullScreenModalRegion()
    {
        var ui = MountMainUi();
        try
        {
            var claims = SurfaceArbiter.Discover(ui.GetTree());
            AssertThat(claims.Count).IsGreater(0);
            foreach (var (claim, _) in claims)
            {
                AssertThat(claim.Region).IsEqual(SurfaceRegion.FullScreenModal);
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
