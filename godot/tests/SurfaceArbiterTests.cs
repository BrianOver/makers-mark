#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-03 (§11.15): <see cref="SurfaceArbiter.Resolve"/> pinned pair-by-pair — the same
/// methodology <see cref="TutorialAnchorArbiterTests"/> already uses for <see
/// cref="TutorialAnchorArbiter.Resolve"/>, adapted to a list instead of a fixed four-slot record
/// because THIS arbiter's whole job is resolving an unknown-in-advance set of discovered claims
/// (P2-KTD3's "discovery, not a list") rather than picking between a known, named handful of
/// sources. No <c>[RequireGodotRuntime]</c>: <see cref="SurfaceClaim"/> is a plain C# record and
/// <see cref="SurfaceArbiter.Resolve"/> touches no Godot API.
/// </summary>
[TestSuite]
public class SurfaceArbiterTests
{
    private static readonly SurfaceClaim Ledger = new("Ledger", SurfaceRegion.FullScreenModal, 1, OwnsScreen: true);
    private static readonly SurfaceClaim Chronicle = new("Chronicle", SurfaceRegion.FullScreenModal, 4, OwnsScreen: true);
    private static readonly SurfaceClaim Camp = new("Camp", SurfaceRegion.FullScreenModal, 7, OwnsScreen: true);
    private static readonly SurfaceClaim Mirror = new("Mirror", SurfaceRegion.FullScreenModal, 9, OwnsScreen: true);

    /// <summary>P2-SCREEN-04: ProvenanceCard's own "child-modal rank" — see its own claim call for
    /// why 100 must beat every FullScreenModal precedence (1-9 today).</summary>
    private static readonly SurfaceClaim ProvenanceCard = new("ProvenanceCard", SurfaceRegion.ChildModal, 100, OwnsScreen: true);

    [TestCase]
    public void EmptyList_ResolvesToNull()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim>());
        AssertThat(result).IsNull();
    }

    [TestCase]
    public void SingleClaim_ResolvesToItself()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Chronicle });
        AssertThat(result).IsEqual(Chronicle);
    }

    [TestCase]
    public void Mirror_OutranksLedger()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Ledger, Mirror });
        AssertThat(result).IsEqual(Mirror);
    }

    [TestCase]
    public void Mirror_OutranksLedger_RegardlessOfListOrder()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Mirror, Ledger });
        AssertThat(result).IsEqual(Mirror);
    }

    [TestCase]
    public void Camp_OutranksChronicle()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Chronicle, Camp });
        AssertThat(result).IsEqual(Camp);
    }

    [TestCase]
    public void Chronicle_OutranksLedger()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Ledger, Chronicle });
        AssertThat(result).IsEqual(Chronicle);
    }

    [TestCase]
    public void Mirror_OutranksEveryOtherClaimAtOnce()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Ledger, Chronicle, Camp, Mirror });
        AssertThat(result).IsEqual(Mirror);
    }

    [TestCase]
    public void Ledger_WinsOnlyWhenNothingElseIsPresent()
    {
        var result = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Ledger });
        AssertThat(result).IsEqual(Ledger);
    }

    [TestCase]
    public void ChildModalRank_OutranksEveryFullScreenModalClaim_RegardlessOfListOrder()
    {
        var forward = SurfaceArbiter.Resolve(new List<SurfaceClaim> { Ledger, Chronicle, Camp, Mirror, ProvenanceCard });
        var reverse = SurfaceArbiter.Resolve(new List<SurfaceClaim> { ProvenanceCard, Mirror, Camp, Chronicle, Ledger });
        AssertThat(forward).IsEqual(ProvenanceCard);
        AssertThat(reverse).IsEqual(ProvenanceCard);
    }

    [TestCase]
    public void SerializeForLog_OrdersHighestPrecedenceFirst()
    {
        var line = SurfaceArbiter.SerializeForLog(new List<SurfaceClaim> { Ledger, Mirror, Chronicle });
        AssertThat(line).IsEqual(
            "Mirror[FullScreenModal]@9; Chronicle[FullScreenModal]@4; Ledger[FullScreenModal]@1");
    }
}
#endif
