using System.Collections.Immutable;

namespace GameSim.Venues;

/// <summary>
/// Deterministic, DRAW-FREE hero→venue routing: given a party's ladder rank, picks which LIVE venue
/// (<see cref="VenueRegistry.LiveRotation"/>) it raids today. A ranked comparator, integer-only
/// throughout (KTD2 — no RNG, no floats, no transcendental <c>Math.*</c>):
///
/// <list type="number">
/// <item><b>Eligibility.</b> Every venue declares a <see cref="VenueDefinition.LadderRank"/>
/// (0 = starter tier). A party is ELIGIBLE for a venue when <c>partyRank &gt;= venue.LadderRank</c>.
/// Eligible venues beat ineligible ones outright. Among ELIGIBLE venues, the HIGHEST LadderRank
/// wins — the party's frontier: a party sent as far up the ladder as it has earned. Among
/// INELIGIBLE venues (possible only in a rotation with no rank-0 venue), the LOWEST LadderRank —
/// the nearest rung — wins: routing never strands a party with no pick.</item>
/// <item><b>Queue length.</b> Venues tied on rank (e.g. the gate-identical, both-rank-0 Mine and
/// Sunken Crypt) split traffic toward whichever has fewer parties already routed THIS TICK
/// (<paramref name="queueCounts"/> in <see cref="ChooseVenue"/>), so peer venues share the rung
/// instead of one taking everything.</item>
/// <item><b>Venue id (Ordinal).</b> Final deterministic tiebreak — the comparator is a total
/// order, so two distinct venue ids never tie all the way down.</item>
/// </list>
///
/// <para><b>Why rank replaced the power band (the §11.8 fix).</b> The prior router read
/// <c>CombatMath.PartyAveragePower</c> — a continuous, non-monotonic signal that wobbles with gear
/// and roster churn — against a per-venue <c>EntryPower</c> threshold, and permanently preferred
/// the highest band a party had EVER reached. Power saturated (~70–76 router-side, measured) below
/// the Mine's floor-5 gate (100), so the moment a party's power crossed Gloomwood's band it was
/// routed there forever — a 4-floor venue — and could never route back to finish a 5-floor one
/// (§11.8, 2026-08-08). Every threshold value was swept and the lever saturates: the fix is not a
/// better number, it is a different signal. <see cref="Hero.LadderRank"/> only ever increments, on
/// a bottom-floor clear, so oscillation is impossible BY CONSTRUCTION — a party can fall back to a
/// lower-ranked venue (its own frontier has no live venue yet) but a rank itself never regresses,
/// so once a live rank-2 venue exists, a rank-2 party never permanently re-strands in rank-1
/// territory the way the old latch could. <c>partyPower</c> leaves routing entirely (still used for
/// the in-venue floor gate, <c>ExpeditionResolver</c>'s <c>venue.Gate(floor)</c> check — an
/// unrelated, unchanged mechanism, AE3).</para>
///
/// <para>The band-vs-headroom history this router replaced (PR #242's tightest-fit skew, the
/// 2026-08-01 EntryPower placement sweep) lives in git, not here — see
/// <c>VenueRegistry</c>/<c>GloomwoodVenue</c>/<c>EmberfallFoundryVenue</c> history for the deleted
/// field's tuning archaeology.</para>
///
/// Callers own the queue bookkeeping: increment <c>queueCounts[chosenId]</c> after each pick, so
/// parties processed later in the SAME tick see the updated count. Both the Morning prediction
/// (<c>MusterPlan.Compute</c>) and the authoritative Expedition tick (<c>ExpeditionSystem.Process</c>)
/// run the identical sequence of calls over the identical parties, landing on identical venues — one
/// rule, two call sites, the same no-drift precedent as <c>ExpeditionSystem.TargetFloorFor</c>.
///
/// Bounty-driven parties do NOT call this: a <c>Bounty</c> carries no venue id (bounties are
/// structurally Mine-scoped, R18 — "the Mine IS the map"), so a party with an accepted bounty routes
/// straight to the Mine; only bounty-free parties are routed by rank.
/// </summary>
public static class VenueRouter
{
    /// <summary>
    /// Choose the live venue a bounty-free party raids today. Pure integer comparison — draws no RNG,
    /// so routing can never become a new RNG-draw site (KTD2).
    /// </summary>
    /// <param name="partyRank">The party's ladder rank for routing purposes. Callers still compute
    /// this as the MIN of the party's members' <see cref="Hero.LadderRank"/> (L1's original rule),
    /// but <see cref="GameSim.Heroes.PartyFormation.FormParties"/> now cohorts by rank BEFORE a
    /// party is ever formed (L2, plan 2026-08-10-003), so every member of a party this method sees
    /// already shares one rank — MIN is exact, not interim: a no-op over a single-valued set.</param>
    /// <param name="liveVenueIds">The live rotation to choose among (<see cref="VenueRegistry.LiveRotation"/>).</param>
    /// <param name="queueCounts">Parties already routed to each venue id THIS TICK, prior to this pick.
    /// A missing key reads as zero (no parties routed there yet).</param>
    public static string ChooseVenue(
        int partyRank,
        ImmutableArray<string> liveVenueIds,
        IReadOnlyDictionary<string, int> queueCounts)
    {
        if (liveVenueIds.IsEmpty)
        {
            throw new ArgumentException("No live venues to route to.", nameof(liveVenueIds));
        }

        var bestId = liveVenueIds[0];
        var best = RankKey(bestId, partyRank, queueCounts);

        for (var i = 1; i < liveVenueIds.Length; i++)
        {
            var id = liveVenueIds[i];
            var key = RankKey(id, partyRank, queueCounts);
            if (IsBetter(key, best))
            {
                bestId = id;
                best = key;
            }
        }

        return bestId;
    }

    /// <summary>The comparator's keys, in priority order: has the party reached this venue's rung,
    /// how far up the ladder the rung sits, how long the queue is, and the venue id
    /// (final, always-decisive tiebreak).</summary>
    private static (bool Eligible, int LadderRank, int Queue, string Id) RankKey(
        string venueId, int partyRank, IReadOnlyDictionary<string, int> queueCounts)
    {
        var venue = VenueRegistry.Require(venueId);
        var queue = queueCounts.TryGetValue(venueId, out var count) ? count : 0;
        return (partyRank >= venue.LadderRank, venue.LadderRank, queue, venueId);
    }

    /// <summary>
    /// True iff <paramref name="candidate"/> outranks <paramref name="incumbent"/> under the
    /// comparator's priority order (Eligible; then highest LadderRank among eligible rungs, lowest
    /// among ineligible; then smallest Queue; then Ordinal Id). The Id fallback makes this a total
    /// order over distinct venue ids — never returns true for two identical keys, so
    /// <see cref="ChooseVenue"/>'s left-to-right scan is order-independent.
    ///
    /// <para><b>Total-order proof (unchanged shape from the EntryPower comparator it replaces):</b>
    /// each of the four fields is itself totally ordered (bool, int, int, then Ordinal string as the
    /// terminal tiebreak), and the fields are compared in a fixed, never-revisited sequence — so for
    /// any two DISTINCT venue ids, exactly one of <c>IsBetter(a,b)</c> / <c>IsBetter(b,a)</c> is true
    /// (antisymmetry + totality), and transitivity holds because each field's comparison is
    /// transitive and the sequence never contradicts an earlier field's verdict. The Id field can
    /// never itself tie for two distinct venues (Ordinal string equality implies identity), which is
    /// what guarantees the chain terminates in a strict answer rather than a tie.</para>
    /// </summary>
    private static bool IsBetter(
        (bool Eligible, int LadderRank, int Queue, string Id) candidate,
        (bool Eligible, int LadderRank, int Queue, string Id) incumbent)
    {
        if (candidate.Eligible != incumbent.Eligible)
        {
            return candidate.Eligible;
        }

        if (candidate.LadderRank != incumbent.LadderRank)
        {
            // Eligible rungs: send the party as far up the ladder as it has earned (the party's
            // frontier). Ineligible rungs (no rank-0 venue live): the nearest rung is the
            // least-wrong home — the never-strand rule, preserved from the EntryPower router.
            return candidate.Eligible
                ? candidate.LadderRank > incumbent.LadderRank
                : candidate.LadderRank < incumbent.LadderRank;
        }

        if (candidate.Queue != incumbent.Queue)
        {
            return candidate.Queue < incumbent.Queue;
        }

        return string.CompareOrdinal(candidate.Id, incumbent.Id) < 0;
    }
}
