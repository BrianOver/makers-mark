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
    // gloomwood 72, emberfall 79 — Emberfall is a STRICTLY LATER band than Gloomwood (re-tuned
    // 2026-08-02 from an original 72/72 tie that measured badly — see VenueConformanceTests'
    // EntryPowerBands_AreTheTunedRecord and EmberfallFoundryVenue.Build for the full search).
    // Emberfall is dormant (not in LiveRotation) but stays in these rotations on purpose: the
    // comparator must already handle its band correctly on the day the art-gated go-live appends
    // it, and ChooseVenue takes the rotation as an argument precisely so liveness churn never
    // touches this suite.
    private static readonly ImmutableArray<string> MineAndGloomwood =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood");

    private static readonly ImmutableArray<string> AllFour =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood", "sunken-crypt", "emberfall");

    private static readonly Dictionary<string, int> NoQueue = new();

    [Fact]
    public void WeakParty_BelowTheGloomwoodBand_StaysInTheMine()
    {
        // Power 20 < Gloomwood's EntryPower 72: only the Mine's band (0) is reached, so the Mine
        // wins even though nothing is queued anywhere — weak parties are never routed onward.
        var chosen = VenueRouter.ChooseVenue(partyPower: 20, MineAndGloomwood, NoQueue);
        Assert.Equal(VenueRegistry.MineId, chosen);
    }

    [Fact]
    public void StrongerParty_IsRoutedOnward_ToTheHighestBandReached()
    {
        // Power 72 reaches the Gloomwood band exactly (>= comparison); highest reached band wins.
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 72, MineAndGloomwood, NoQueue));

        // Power 71 is one short: the early band is the highest reached — the veteran band is a
        // hard line, not a gradient.
        Assert.Equal(VenueRegistry.MineId, VenueRouter.ChooseVenue(partyPower: 71, MineAndGloomwood, NoQueue));

        // In the full four-venue rotation, power 72 reaches Gloomwood's band (72) but NOT
        // Emberfall's (79) — Gloomwood is the highest band actually reached.
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 72, AllFour, NoQueue));

        // Power 79 reaches BOTH bands; the higher one (Emberfall) wins — the two are no longer
        // peers, so this is decided by band alone, before queue or id ever get a say.
        Assert.Equal("emberfall", VenueRouter.ChooseVenue(partyPower: 79, AllFour, NoQueue));
    }

    [Fact]
    public void BandBeatsQueue_AStrongPartyNeverFallsBackToAnEmptyEarlyVenue()
    {
        // The band is a stronger signal than congestion: even with the Gloomwood heavily queued
        // and the Mine empty, a veteran-band party raids the Gloomwood. (Queue only splits PEERS.)
        var queue = new Dictionary<string, int> { ["mine"] = 0, ["gloomwood"] = 50 };
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 80, MineAndGloomwood, queue));
    }

    [Fact]
    public void GloomwoodAndEmberfall_AreNoLongerPeers_BandDecidesRegardlessOfQueue()
    {
        // Re-tuned 2026-08-02 (EntryPower 72 -> 79, VenueConformanceTests.EntryPowerBands_AreTheTunedRecord):
        // the original 72/72 tie let a heavily-queued Emberfall still lose parties to Gloomwood
        // only via the queue comparator; now the two sit in DIFFERENT bands, so queue never gets
        // a say between them. A power-80 party reaches both bands and always takes the higher one
        // (Emberfall), no matter how long Emberfall's own queue already is:
        var emberflooded = new Dictionary<string, int> { ["emberfall"] = 50, ["gloomwood"] = 0 };
        Assert.Equal("emberfall", VenueRouter.ChooseVenue(partyPower: 80, AllFour, emberflooded));

        // A power-75 party reaches ONLY Gloomwood's band (72), never Emberfall's (79) — it stays
        // in Gloomwood even with Gloomwood heavily queued and Emberfall empty (band beats queue,
        // the same property BandBeatsQueue_* pins for Mine/Gloomwood).
        var gloomFlooded = new Dictionary<string, int> { ["emberfall"] = 0, ["gloomwood"] = 50 };
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyPower: 75, AllFour, gloomFlooded));
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
        // assume curation): a power-10 party reaches neither band (gloomwood 72, emberfall 79).
        // The nearest-band rule picks the LOWEST unreached band — gloomwood — the point being a
        // deterministic pick exists, never a strand.
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
        foreach (var power in new[] { 0, 10, 71, 72, 200 })
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
