using System.Collections.Immutable;
using System.Linq;
using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Venues;

/// <summary>
/// <see cref="VenueRouter"/>'s draw-free hero→venue comparator, tested in isolation from
/// <see cref="VenueRegistry.LiveRotation"/> churn — a pure function of (party rank, live ids, queue
/// counts). Covers the ranked priority order (eligibility by <see
/// cref="VenueDefinition.LadderRank"/>, then queue-length, then Ordinal id), determinism, and the
/// total-order tiebreak.
///
/// REWRITTEN for the forward ladder (owner ruling 2026-08-10, plan 2026-08-10-003 L1, §11.8's
/// fix): the old <c>partyPower</c>/<c>EntryPower</c> band comparator this suite used to pin is
/// GONE — it read a continuous, non-monotonic power signal that saturated below a venue's own
/// floor-5 gate and permanently stole mid-power parties into a shallower venue they could never
/// route back out of (§11.8, measured 2026-08-08). Rank routing replaces it: <see cref="Hero.LadderRank"/>
/// only ever increments (on a bottom-floor clear), so the same strand can never recur BY
/// CONSTRUCTION, not by re-tuning a threshold. A test asserting power-band behavior would be
/// pinning the bug this wave exists to remove.
/// </summary>
public class VenueRouterTests
{
    // Registered LadderRanks (pinned by VenueConformanceTests): mine 0, sunken-crypt 0,
    // gloomwood 1, emberfall 2 — a STRICT ladder, unlike the old EntryPower bands (which tied
    // Gloomwood and Emberfall at 72 to make the go-live a queue-split). Ranks never tie between
    // distinct rungs, so a party eligible for both always prefers the higher one outright — no
    // ordinal tiebreak needed between rungs. Emberfall went LIVE in L4 (forward-ladder plan
    // 2026-08-10-003) — these synthetic rotations exercise it both IN and (deliberately) OUT of a
    // live array on purpose: ChooseVenue takes the rotation as an argument precisely so liveness
    // churn never touches this suite, and the "absent" cases below stay a real regression guard for
    // any FUTURE rung that ships ranked-but-not-yet-live, the same shape Emberfall itself was
    // through L1-L3.
    private static readonly ImmutableArray<string> MineAndGloomwood =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood");

    private static readonly ImmutableArray<string> AllFour =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood", "sunken-crypt", "emberfall");

    private static readonly Dictionary<string, int> NoQueue = new();

    [Fact]
    public void RookieParty_RankZero_StaysAtTheStarterTier()
    {
        // Rank 0 is ineligible for Gloomwood (rank 1): only the starter tier (rank 0) is reached,
        // so the Mine wins even though nothing is queued anywhere — rookies are never routed onward.
        var chosen = VenueRouter.ChooseVenue(partyRank: 0, MineAndGloomwood, NoQueue);
        Assert.Equal(VenueRegistry.MineId, chosen);
    }

    [Fact]
    public void RankOneParty_IsRoutedOnward_ToGloomwood()
    {
        // Rank 1 is eligible for Gloomwood (rank 1 <= 1); the highest eligible rung wins.
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyRank: 1, MineAndGloomwood, NoQueue));

        // Rank 0 is one rung short: the starter tier is the highest rung reached — the ladder is a
        // hard line, not a gradient.
        Assert.Equal(VenueRegistry.MineId, VenueRouter.ChooseVenue(partyRank: 0, MineAndGloomwood, NoQueue));
    }

    [Fact]
    public void RankTwoParty_InTheFullRotation_RoutesStraightToEmberfall_NoTieNeeded()
    {
        // Emberfall (rank 2) and Gloomwood (rank 1) no longer tie the way EntryPower 72/72 did —
        // ranks are a STRICT ladder, so a rank-2 party's highest eligible rung is Emberfall,
        // outright, with no queue/ordinal tiebreak in play.
        Assert.Equal("emberfall", VenueRouter.ChooseVenue(partyRank: 2, AllFour, NoQueue));

        // Even with Emberfall heavily queued and everything else empty: rank beats queue, exactly
        // like the old band comparator (queue only ever splits PEERS on an equal rung).
        var queue = new Dictionary<string, int> { ["emberfall"] = 50 };
        Assert.Equal("emberfall", VenueRouter.ChooseVenue(partyRank: 2, AllFour, queue));
    }

    [Fact]
    public void RankTwoParty_WithNoLiveRankTwoVenue_FallsBackToGloomwood_AndNeverStrandsOrOscillates()
    {
        // The no-oscillation property the ladder is built on (§11.8's fix), exercised on a
        // deliberately CONSTRUCTED rotation with no rank-2 venue live (Emberfall itself was exactly
        // this shape through L1-L3, and any future rung will be too before its own art-gated
        // go-live): a rank-2 party's highest ELIGIBLE rung among [mine(0), gloomwood(1)] is
        // Gloomwood. Because LadderRank only ever increments, this party can never fall lower than
        // rank 2 again, and Gloomwood (rank 1 <= 2) stays eligible and wins every future tick too —
        // it never "flips back" to the Mine the way the old power latch could strand a party the
        // other direction.
        var liveWithoutEmberfall = ImmutableArray.Create(VenueRegistry.MineId, "gloomwood");
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyRank: 2, liveWithoutEmberfall, NoQueue));

        // Rank beats queue here too: even with the Mine empty and Gloomwood heavily queued, the
        // higher-ranked, still-eligible venue wins — congestion only splits peers on an equal rung.
        var queue = new Dictionary<string, int> { [VenueRegistry.MineId] = 0, ["gloomwood"] = 50 };
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyRank: 2, liveWithoutEmberfall, queue));
    }

    [Fact]
    public void PeerVenues_SplitTheirRankByQueueLength()
    {
        // Mine and Sunken Crypt are both rank 0 — true peers. A rookie party goes to whichever has
        // fewer parties routed this tick; on a full tie, Ordinal id ("mine" < "sunken-crypt").
        var mineAndCrypt = ImmutableArray.Create(VenueRegistry.MineId, "sunken-crypt");

        Assert.Equal(VenueRegistry.MineId, VenueRouter.ChooseVenue(partyRank: 0, mineAndCrypt, NoQueue));

        var queue = new Dictionary<string, int> { ["mine"] = 2, ["sunken-crypt"] = 1 };
        Assert.Equal("sunken-crypt", VenueRouter.ChooseVenue(partyRank: 0, mineAndCrypt, queue));
    }

    [Fact]
    public void PeerVenues_RoutedSequentially_SplitEvenly()
    {
        // Simulates ExpeditionSystem's own sequential queue-bookkeeping: N identical rank-0 parties
        // routed one after another, incrementing the chosen venue's count each time. If routing
        // always picked one venue, every hero would pile onto it (the exact failure mode the queue
        // comparator exists to fix).
        var mineAndCrypt = ImmutableArray.Create(VenueRegistry.MineId, "sunken-crypt");
        var queueCounts = new Dictionary<string, int> { ["mine"] = 0, ["sunken-crypt"] = 0 };
        var picks = new List<string>();

        for (var i = 0; i < 10; i++)
        {
            var chosen = VenueRouter.ChooseVenue(partyRank: 0, mineAndCrypt, queueCounts);
            picks.Add(chosen);
            queueCounts[chosen] = queueCounts[chosen] + 1;
        }

        // Perfectly balanced: initial tie goes Ordinal ("mine"), the queue comparator strictly
        // prefers the shorter queue thereafter — 10 identical parties split 5/5.
        Assert.Equal(5, picks.Count(p => p == "mine"));
        Assert.Equal(5, picks.Count(p => p == "sunken-crypt"));
    }

    [Fact]
    public void PartyBelowEveryLiveRung_GetsTheNearestRung_NeverStranded()
    {
        // A rotation with no rank-0 venue (not the live shape, but the router must not assume
        // curation): a rank-0 party is ineligible for both. Among ineligible rungs the LOWEST wins
        // outright — Gloomwood (1) beats Emberfall (2), no tie in play — the point is a
        // deterministic pick exists, never a strand.
        var midAndEnd = ImmutableArray.Create("gloomwood", "emberfall");
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyRank: 0, midAndEnd, NoQueue));

        // A rank-1 party is eligible for Gloomwood but not Emberfall — same answer, now via the
        // eligible branch instead of the never-strand branch.
        Assert.Equal("gloomwood", VenueRouter.ChooseVenue(partyRank: 1, midAndEnd, NoQueue));
    }

    [Fact]
    public void ChooseVenue_IsDeterministic_ForIdenticalInputs()
    {
        var queue = new Dictionary<string, int> { ["mine"] = 2, ["gloomwood"] = 1 };
        var a = VenueRouter.ChooseVenue(partyRank: 0, MineAndGloomwood, queue);
        var b = VenueRouter.ChooseVenue(partyRank: 0, MineAndGloomwood, queue);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ChooseVenue_IsOrderIndependent_AcrossRotationPermutations()
    {
        // The comparator is a total order, so the left-to-right scan must land on the same venue
        // no matter how the live rotation happens to be ordered.
        var queue = new Dictionary<string, int> { ["mine"] = 1, ["sunken-crypt"] = 1 };
        foreach (var rank in new[] { 0, 1, 2, 3 })
        {
            var expected = VenueRouter.ChooseVenue(rank, AllFour, queue);
            var reversed = ImmutableArray.CreateRange(AllFour.Reverse());
            Assert.Equal(expected, VenueRouter.ChooseVenue(rank, reversed, queue));
        }
    }

    [Fact]
    public void MissingQueueKey_ReadsAsZero()
    {
        // An empty queue dictionary (no entries at all) must behave identically to explicit zeros —
        // callers seed the dictionary from LiveRotation, but ChooseVenue itself must not assume that.
        var chosen = VenueRouter.ChooseVenue(partyRank: 0, MineAndGloomwood, new Dictionary<string, int>());
        Assert.Equal(VenueRegistry.MineId, chosen); // rank-0 venue, same as the explicit-zero case
    }

    [Fact]
    public void EmptyLiveVenues_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VenueRouter.ChooseVenue(partyRank: 0, ImmutableArray<string>.Empty, NoQueue));
    }

    [Fact]
    public void SingleLiveVenue_AlwaysWins_RegardlessOfRank()
    {
        // A single-venue rotation always returns that one venue, even when the party hasn't reached
        // its rank — routing never strands a party with no pick.
        var oneVenue = ImmutableArray.Create("emberfall");
        var chosen = VenueRouter.ChooseVenue(partyRank: 0, oneVenue, NoQueue);
        Assert.Equal("emberfall", chosen);
    }
}
