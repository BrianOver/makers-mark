using System.Collections.Immutable;
using System.Linq;
using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Venues;

/// <summary>
/// <see cref="VenueRouter"/>'s draw-free hero→venue comparator, tested in isolation from
/// <see cref="VenueRegistry.LiveRotation"/> churn — a pure function of (power, live ids, queue
/// counts). Covers the banded priority order (progression band by <see
/// cref="VenueDefinition.EntryPower"/>, then queue-length, then Ordinal id), determinism, and the
/// total-order tiebreak. The old headroom/"tightest fit" utility these tests used to pin is
/// deliberately gone: it sent every party to the highest-gate venue it could clear (PR #242's
/// measured skew — Gloomwood/Emberfall vacuumed the world, the Mine starved, the Sunken Crypt drew
/// ~zero), so a test asserting fit behavior would be pinning the bug.
/// </summary>
public class VenueRouterTests
{
    // Registered EntryPower bands (pinned by VenueConformanceTests): mine 0, sunken-crypt 0,
    // gloomwood 35, emberfall 70.
    private static readonly ImmutableArray<string> MineAndGloomwood =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood");

    private static readonly ImmutableArray<string> AllFour =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood", "sunken-crypt", "emberfall");

    private static readonly Dictionary<string, int> NoQueue = new();

    [Fact]
    public void WeakParty_BelowTheGloomwoodBand_StaysInTheMine()
    {
        // Power 20 < Gloomwood's EntryPower 35: only the Mine's band (0) is reached, so the Mine
        // wins even though nothing is queued anywhere — weak parties are never routed onward.
        var chosen = VenueRouter.ChooseVenue(partyPower: 20, MineAndGloomwood, NoQueue);
        Assert.Equal(VenueRegistry.MineId, chosen);
    }

    [Fact]
    public void StrongerParty_IsRoutedOnward_ToTheHighestBandReached()
    {
        // Power 35 reaches the Gloomwood band exactly (>= comparison); highest reached band wins.
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 35, MineAndGloomwood, NoQueue));

        // Power 70 reaches Emberfall's band exactly — the endgame venue outranks every lower band.
        Assert.Equal("emberfall", VenueRouter.ChooseVenue(partyPower: 70, AllFour, NoQueue));

        // Power 69 is one short of Emberfall: the Gloomwood band (35) is the highest reached.
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 69, AllFour, NoQueue));
    }

    [Fact]
    public void BandBeatsQueue_AStrongPartyNeverFallsBackToAnEmptyEarlyVenue()
    {
        // The band is a stronger signal than congestion: even with the Gloomwood heavily queued
        // and the Mine empty, a band-35 party raids the Gloomwood. (Queue only splits PEERS.)
        var queue = new Dictionary<string, int> { ["mine"] = 0, ["gloomwood"] = 50 };
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 40, MineAndGloomwood, queue));
    }

    [Fact]
    public void PeerVenues_SplitTheirBandByQueueLength()
    {
        // Mine and Sunken Crypt are both EntryPower 0 — true peers. A weak party goes to whichever
        // has fewer parties routed this tick; on a full tie, Ordinal id ("mine" < "sunken-crypt").
        var mineAndCrypt = ImmutableArray.Create(VenueRegistry.MineId, "sunken-crypt");

        Assert.Equal(VenueRegistry.MineId, VenueRouter.ChooseVenue(partyPower: 10, mineAndCrypt, NoQueue));

        var queue = new Dictionary<string, int> { ["mine"] = 2, ["sunken-crypt"] = 1 };
        Assert.Equal("sunken-crypt", VenueRouter.ChooseVenue(partyPower: 10, mineAndCrypt, queue));
    }

    [Fact]
    public void PeerVenues_RoutedSequentially_SplitEvenly()
    {
        // Simulates ExpeditionSystem's own sequential queue-bookkeeping: N identical weak parties
        // routed one after another, incrementing the chosen venue's count each time. If routing
        // always picked one venue, every hero would pile onto it (the exact failure mode the queue
        // comparator exists to fix).
        var mineAndCrypt = ImmutableArray.Create(VenueRegistry.MineId, "sunken-crypt");
        var queueCounts = new Dictionary<string, int> { ["mine"] = 0, ["sunken-crypt"] = 0 };
        var picks = new List<string>();

        for (var i = 0; i < 10; i++)
        {
            var chosen = VenueRouter.ChooseVenue(partyPower: 10, mineAndCrypt, queueCounts);
            picks.Add(chosen);
            queueCounts[chosen] = queueCounts[chosen] + 1;
        }

        // Perfectly balanced: initial tie goes Ordinal ("mine"), the queue comparator strictly
        // prefers the shorter queue thereafter — 10 identical parties split 5/5.
        Assert.Equal(5, picks.Count(p => p == "mine"));
        Assert.Equal(5, picks.Count(p => p == "sunken-crypt"));
    }

    [Fact]
    public void PartyBelowEveryBand_GetsTheNearestBand_NeverStranded()
    {
        // A rotation with no EntryPower-0 venue (not the live shape, but the router must not
        // assume curation): a power-10 party reaches neither band — the LOWEST entry (nearest
        // band, gloomwood 35 < emberfall 70) is the least-wrong home.
        var midAndEnd = ImmutableArray.Create("gloomwood", "emberfall");
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 10, midAndEnd, NoQueue));
    }

    [Fact]
    public void ChooseVenue_IsDeterministic_ForIdenticalInputs()
    {
        var queue = new Dictionary<string, int> { ["mine"] = 2, ["gloomwood"] = 1 };
        var a = VenueRouter.ChooseVenue(partyPower: 45, MineAndGloomwood, queue);
        var b = VenueRouter.ChooseVenue(partyPower: 45, MineAndGloomwood, queue);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ChooseVenue_IsOrderIndependent_AcrossRotationPermutations()
    {
        // The comparator is a total order, so the left-to-right scan must land on the same venue
        // no matter how the live rotation happens to be ordered.
        var queue = new Dictionary<string, int> { ["mine"] = 1, ["sunken-crypt"] = 1 };
        foreach (var power in new[] { 0, 10, 34, 35, 69, 70, 200 })
        {
            var expected = VenueRouter.ChooseVenue(power, AllFour, queue);
            var reversed = ImmutableArray.CreateRange(AllFour.Reverse());
            Assert.Equal(expected, VenueRouter.ChooseVenue(power, reversed, queue));
        }
    }

    [Fact]
    public void MissingQueueKey_ReadsAsZero()
    {
        // An empty queue dictionary (no entries at all) must behave identically to explicit zeros —
        // callers seed the dictionary from LiveRotation, but ChooseVenue itself must not assume that.
        var chosen = VenueRouter.ChooseVenue(partyPower: 20, MineAndGloomwood, new Dictionary<string, int>());
        Assert.Equal(VenueRegistry.MineId, chosen); // band-0 venue, same as the explicit-zero case
    }

    [Fact]
    public void EmptyLiveVenues_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VenueRouter.ChooseVenue(partyPower: 20, ImmutableArray<string>.Empty, NoQueue));
    }

    [Fact]
    public void SingleLiveVenue_AlwaysWins_RegardlessOfBand()
    {
        // A single-venue rotation always returns that one venue, even when the party is below its
        // band — routing never strands a party with no pick.
        var oneVenue = ImmutableArray.Create("emberfall");
        var chosen = VenueRouter.ChooseVenue(partyPower: 0, oneVenue, NoQueue);
        Assert.Equal("emberfall", chosen);
    }
}
