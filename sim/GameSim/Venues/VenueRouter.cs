using System.Collections.Immutable;

namespace GameSim.Venues;

/// <summary>
/// Deterministic, DRAW-FREE hero→venue routing (Phase C U-C4): given a party's current depth record
/// and gear power, picks which LIVE venue (<see cref="VenueRegistry.LiveRotation"/>) it raids today.
/// A two-stage comparator, integer-only throughout (KTD2 — no RNG, no floats, no transcendental
/// <c>Math.*</c>):
///
/// <list type="number">
/// <item><b>Utility.</b> For each live venue, compute the party's usual candidate floor there (the
/// depth-plus-one rule, clamped to THAT venue's own <see cref="VenueDefinition.FloorCount"/>) and its
/// <c>headroom = partyPower - venue.Gate(candidateFloor)</c>. A venue the party can clear
/// (headroom &gt;= 0) always outranks one it cannot; among clearable venues the SMALLEST non-negative
/// headroom wins (the tightest fit — least gear power left idle).</item>
/// <item><b>Queue-length comparator.</b> Ties (utility-equal — e.g. every floor-1 gate is 0, so a
/// fresh party's headroom is identical everywhere) break toward whichever venue has fewer parties
/// already routed to it THIS TICK (<paramref name="queueCounts"/> in <see cref="ChooseVenue"/>), so
/// heroes spread across the live rotation instead of piling onto one "best" venue.</item>
/// <item><b>Venue id (Ordinal).</b> Final deterministic tiebreak — the comparator is a total order,
/// so two distinct venue ids never tie all the way down.</item>
/// </list>
///
/// Callers own the queue bookkeeping: increment <c>queueCounts[chosenId]</c> after each pick, so
/// parties processed later in the SAME tick see the updated count. Both the Morning prediction
/// (<c>MusterPlan.Compute</c>) and the authoritative Expedition tick (<c>ExpeditionSystem.Process</c>)
/// run the identical sequence of calls over the identical parties, landing on identical venues — one
/// rule, two call sites, the same no-drift precedent as <c>ExpeditionSystem.TargetFloorFor</c>.
///
/// Bounty-driven parties do NOT call this: a <c>Bounty</c> carries no venue id (bounties are
/// structurally Mine-scoped, R18 — "the Mine IS the map"), so a party with an accepted bounty routes
/// straight to the Mine; only bounty-free parties are routed by utility.
/// </summary>
public static class VenueRouter
{
    /// <summary>
    /// Choose the live venue a bounty-free party raids today. Pure integer comparison — draws no RNG,
    /// so routing can never become a new RNG-draw site (KTD2).
    /// </summary>
    /// <param name="partyDepth">The party's current depth record (its members' max
    /// <c>Hero.DeepestFloorReached</c>) — the same input <c>ExpeditionSystem.TargetFloorFor</c> reads,
    /// fed in venue-agnostic here since the candidate floor clamps per-venue below.</param>
    /// <param name="partyPower">The party's average effective power (<c>CombatMath.PartyAveragePower</c>).</param>
    /// <param name="liveVenueIds">The live rotation to choose among (<see cref="VenueRegistry.LiveRotation"/>).</param>
    /// <param name="queueCounts">Parties already routed to each venue id THIS TICK, prior to this pick.
    /// A missing key reads as zero (no parties routed there yet).</param>
    public static string ChooseVenue(
        int partyDepth,
        int partyPower,
        ImmutableArray<string> liveVenueIds,
        IReadOnlyDictionary<string, int> queueCounts)
    {
        if (liveVenueIds.IsEmpty)
        {
            throw new ArgumentException("No live venues to route to.", nameof(liveVenueIds));
        }

        var bestId = liveVenueIds[0];
        var best = RankKey(bestId, partyDepth, partyPower, queueCounts);

        for (var i = 1; i < liveVenueIds.Length; i++)
        {
            var id = liveVenueIds[i];
            var key = RankKey(id, partyDepth, partyPower, queueCounts);
            if (IsBetter(key, best))
            {
                bestId = id;
                best = key;
            }
        }

        return bestId;
    }

    /// <summary>The comparator's four keys, in priority order: can the party clear its candidate
    /// floor here at all, how tight the fit is, how long the queue is, and the venue id (final,
    /// always-decisive tiebreak).</summary>
    private static (bool CanClear, int Headroom, int Queue, string Id) RankKey(
        string venueId, int partyDepth, int partyPower, IReadOnlyDictionary<string, int> queueCounts)
    {
        var venue = VenueRegistry.Require(venueId);
        var candidateFloor = Math.Clamp(partyDepth + 1, 1, venue.FloorCount);
        var gate = venue.Gate(candidateFloor);
        var headroom = partyPower - gate;
        var queue = queueCounts.TryGetValue(venueId, out var count) ? count : 0;
        return (headroom >= 0, headroom, queue, venueId);
    }

    /// <summary>True iff <paramref name="candidate"/> outranks <paramref name="incumbent"/> under the
    /// comparator's priority order (CanClear, then smallest Headroom, then smallest Queue, then
    /// Ordinal Id). The Id fallback makes this a total order over distinct venue ids — never returns
    /// true for two identical keys, so <see cref="ChooseVenue"/>'s left-to-right scan is
    /// order-independent.</summary>
    private static bool IsBetter(
        (bool CanClear, int Headroom, int Queue, string Id) candidate,
        (bool CanClear, int Headroom, int Queue, string Id) incumbent)
    {
        if (candidate.CanClear != incumbent.CanClear)
        {
            return candidate.CanClear;
        }

        if (candidate.Headroom != incumbent.Headroom)
        {
            return candidate.Headroom < incumbent.Headroom;
        }

        if (candidate.Queue != incumbent.Queue)
        {
            return candidate.Queue < incumbent.Queue;
        }

        return string.CompareOrdinal(candidate.Id, incumbent.Id) < 0;
    }
}
