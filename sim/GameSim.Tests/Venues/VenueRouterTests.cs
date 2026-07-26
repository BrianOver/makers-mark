using System.Collections.Immutable;
using System.Linq;
using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Venues;

/// <summary>
/// Phase C U-C4: <see cref="VenueRouter"/>'s draw-free hero→venue comparator, tested in isolation
/// from <see cref="VenueRegistry.LiveRotation"/> churn — a pure function of (depth, power, live ids,
/// queue counts). Covers the three-stage priority order (clearability, then utility/headroom, then
/// queue-length), determinism, and the total-order tiebreak.
/// </summary>
public class VenueRouterTests
{
    private static readonly ImmutableArray<string> MineAndGloomwood =
        ImmutableArray.Create(VenueRegistry.MineId, "gloomwood");

    private static readonly Dictionary<string, int> NoQueue = new();

    [Fact]
    public void FreshParty_AtEqualQueue_PicksLowerHeadroom_OrOrdinalTiebreak()
    {
        // Floor 1's gate is 0 for BOTH the Mine and the Gloomwood, so a depth-0 party's headroom
        // (partyPower - 0) is IDENTICAL at both venues — a true utility tie. With an empty queue on
        // both sides, the comparator falls through to the final Ordinal-id tiebreak: "gloomwood" <
        // "mine" (Ordinal), so the Gloomwood wins deterministically, not by chance.
        var chosen = VenueRouter.ChooseVenue(partyDepth: 0, partyPower: 20, MineAndGloomwood, NoQueue);
        Assert.Equal("gloomwood", chosen);
    }

    [Fact]
    public void QueueLengthComparator_BreaksAUtilityTie_TowardTheShorterQueue()
    {
        // Same utility tie as above (floor-1 gate 0 at both venues), but the Mine already has fewer
        // parties queued this tick — the queue-length comparator overrides the ordinal fallback.
        var queue = new Dictionary<string, int> { ["mine"] = 0, ["gloomwood"] = 3 };
        var chosen = VenueRouter.ChooseVenue(partyDepth: 0, partyPower: 20, MineAndGloomwood, queue);
        Assert.Equal("mine", chosen);
    }

    [Fact]
    public void HeroesDistribute_AcrossBothLiveVenues_WhenRoutedSequentially()
    {
        // Simulates ExpeditionSystem's own sequential queue-bookkeeping: N identical depth-0 parties
        // routed one after another, incrementing the chosen venue's count each time. If routing always
        // picked one venue, every hero would pile onto it (the exact failure mode U-C4 exists to fix).
        // With the queue-length comparator active, the picks must alternate/split roughly evenly.
        var queueCounts = new Dictionary<string, int> { ["mine"] = 0, ["gloomwood"] = 0 };
        var picks = new List<string>();

        for (var i = 0; i < 10; i++)
        {
            var chosen = VenueRouter.ChooseVenue(partyDepth: 0, partyPower: 20, MineAndGloomwood, queueCounts);
            picks.Add(chosen);
            queueCounts[chosen] = queueCounts[chosen] + 1;
        }

        // Both venues got picked — routing is NOT a permanent lock onto a single "best" venue.
        Assert.Contains("mine", picks);
        Assert.Contains("gloomwood", picks);

        // Perfectly balanced: with an initial tie (Ordinal → Gloomwood first) and the queue comparator
        // strictly preferring the shorter queue thereafter, 10 identical parties split 5/5.
        Assert.Equal(5, picks.Count(p => p == "mine"));
        Assert.Equal(5, picks.Count(p => p == "gloomwood"));
    }

    [Fact]
    public void UnclearableVenue_NeverBeatsAClearableOne_EvenWithAShorterQueue()
    {
        // A depth-4 party targets floor 5 at the Mine (gate 100) and floor 4 (clamped) at the
        // Gloomwood (gate 75). With power 80 the party clears the Gloomwood's gate but NOT the Mine's
        // — clearability (stage 1 of the comparator) must win over the queue-length tiebreak (stage 3)
        // even when the Mine's queue is empty and the Gloomwood's is long.
        var queue = new Dictionary<string, int> { ["mine"] = 0, ["gloomwood"] = 50 };
        var chosen = VenueRouter.ChooseVenue(partyDepth: 4, partyPower: 80, MineAndGloomwood, queue);
        Assert.Equal("gloomwood", chosen);
    }

    [Fact]
    public void TightestFit_WinsOverLooserFit_WhenBothClear_RegardlessOfQueue()
    {
        // A depth-19 party (both venues clamp its candidate floor to their own top floor: Mine floor 5
        // gate 100, Gloomwood floor 4 gate 75) with power 200 clears both gates comfortably. Headroom
        // at the Mine (200-100=100) is smaller than at the Gloomwood (200-75=125), so the Mine is the
        // tighter fit and wins stage 2 — even though the Gloomwood's queue is shorter.
        var queue = new Dictionary<string, int> { ["mine"] = 5, ["gloomwood"] = 0 };
        var chosen = VenueRouter.ChooseVenue(partyDepth: 19, partyPower: 200, MineAndGloomwood, queue);
        Assert.Equal("mine", chosen);
    }

    [Fact]
    public void ChooseVenue_IsDeterministic_ForIdenticalInputs()
    {
        var queue = new Dictionary<string, int> { ["mine"] = 2, ["gloomwood"] = 1 };
        var a = VenueRouter.ChooseVenue(partyDepth: 2, partyPower: 45, MineAndGloomwood, queue);
        var b = VenueRouter.ChooseVenue(partyDepth: 2, partyPower: 45, MineAndGloomwood, queue);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MissingQueueKey_ReadsAsZero()
    {
        // An empty queue dictionary (no entries at all) must behave identically to explicit zeros —
        // callers seed the dictionary from LiveRotation, but ChooseVenue itself must not assume that.
        var chosen = VenueRouter.ChooseVenue(partyDepth: 0, partyPower: 20, MineAndGloomwood, new Dictionary<string, int>());
        Assert.Equal("gloomwood", chosen); // same ordinal-tiebreak result as the all-zero case
    }

    [Fact]
    public void EmptyLiveVenues_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VenueRouter.ChooseVenue(partyDepth: 0, partyPower: 20, ImmutableArray<string>.Empty, NoQueue));
    }

    [Fact]
    public void SingleLiveVenue_AlwaysWins_RegardlessOfClearability()
    {
        // A single-venue rotation (e.g. before Gloomwood went live) always returns that one venue,
        // even when the party cannot clear its gate — routing never strands a party with no pick.
        var oneVenue = ImmutableArray.Create(VenueRegistry.MineId);
        var chosen = VenueRouter.ChooseVenue(partyDepth: 0, partyPower: 0, oneVenue, NoQueue);
        Assert.Equal(VenueRegistry.MineId, chosen);
    }
}
