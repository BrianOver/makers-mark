using System.Collections.Immutable;

namespace GameSim.Venues;

/// <summary>
/// Deterministic, DRAW-FREE hero→venue routing: given a party's gear power, picks which LIVE venue
/// (<see cref="VenueRegistry.LiveRotation"/>) it raids today. A banded comparator, integer-only
/// throughout (KTD2 — no RNG, no floats, no transcendental <c>Math.*</c>):
///
/// <list type="number">
/// <item><b>Progression band.</b> Every venue declares an <see cref="VenueDefinition.EntryPower"/>
/// (0 = early venue). A party is IN a venue's band when <c>partyPower &gt;= EntryPower</c>. Among
/// venues whose band the party has reached, the HIGHEST EntryPower wins: weak parties stay in the
/// early venues, stronger parties are routed onward — the progression the venue roster is built
/// around. If the party is below EVERY live band (possible only in a rotation with no
/// EntryPower-0 venue), the LOWEST EntryPower — the nearest band — wins: routing never strands a
/// party with no pick.</item>
/// <item><b>Queue length.</b> Venues tied on band (e.g. the gate-identical, both-EntryPower-0
/// Mine and Sunken Crypt) split traffic toward whichever has fewer parties already routed THIS
/// TICK (<paramref name="queueCounts"/> in <see cref="ChooseVenue"/>), so peer venues share the
/// band instead of one taking everything.</item>
/// <item><b>Venue id (Ordinal).</b> Final deterministic tiebreak — the comparator is a total
/// order, so two distinct venue ids never tie all the way down.</item>
/// </list>
///
/// <para><b>Why bands replaced the old headroom utility.</b> The first router ranked clearable
/// venues by smallest <c>power - gate(candidateFloor)</c> ("tightest fit"). Because higher-gate
/// venues always fit tighter for any party that can clear them, that rule sent every mid-power
/// party to the highest-gate venue it could survive — Gloomwood vacuumed the world, the Mine
/// starved, and the Sunken Crypt (gate-identical to the Mine but later in Ordinal order) drew
/// ~zero. Measured in PR #242's 15-seed sweep. Gates also cannot tell the Mine from Emberfall at
/// all (identical ladders, grade-1..5 vs grade-12..16 ore), and router-side power never sees the
/// in-run craft/consumable modifiers the resolver applies at real gate checks — so both the fit
/// rule AND its clearability input are deliberately gone, replaced by the explicit per-venue
/// band. Floor targeting still lives in <c>ExpeditionSystem.TargetFloorFor</c>, which is why this
/// router no longer reads the party's depth record.</para>
///
/// Callers own the queue bookkeeping: increment <c>queueCounts[chosenId]</c> after each pick, so
/// parties processed later in the SAME tick see the updated count. Both the Morning prediction
/// (<c>MusterPlan.Compute</c>) and the authoritative Expedition tick (<c>ExpeditionSystem.Process</c>)
/// run the identical sequence of calls over the identical parties, landing on identical venues — one
/// rule, two call sites, the same no-drift precedent as <c>ExpeditionSystem.TargetFloorFor</c>.
///
/// Bounty-driven parties do NOT call this: a <c>Bounty</c> carries no venue id (bounties are
/// structurally Mine-scoped, R18 — "the Mine IS the map"), so a party with an accepted bounty routes
/// straight to the Mine; only bounty-free parties are routed by band.
/// </summary>
public static class VenueRouter
{
    /// <summary>
    /// Choose the live venue a bounty-free party raids today. Pure integer comparison — draws no RNG,
    /// so routing can never become a new RNG-draw site (KTD2).
    /// </summary>
    /// <param name="partyPower">The party's average effective power (<c>CombatMath.PartyAveragePower</c>).</param>
    /// <param name="liveVenueIds">The live rotation to choose among (<see cref="VenueRegistry.LiveRotation"/>).</param>
    /// <param name="queueCounts">Parties already routed to each venue id THIS TICK, prior to this pick.
    /// A missing key reads as zero (no parties routed there yet).</param>
    public static string ChooseVenue(
        int partyPower,
        ImmutableArray<string> liveVenueIds,
        IReadOnlyDictionary<string, int> queueCounts)
    {
        if (liveVenueIds.IsEmpty)
        {
            throw new ArgumentException("No live venues to route to.", nameof(liveVenueIds));
        }

        var bestId = liveVenueIds[0];
        var best = RankKey(bestId, partyPower, queueCounts);

        for (var i = 1; i < liveVenueIds.Length; i++)
        {
            var id = liveVenueIds[i];
            var key = RankKey(id, partyPower, queueCounts);
            if (IsBetter(key, best))
            {
                bestId = id;
                best = key;
            }
        }

        return bestId;
    }

    /// <summary>The comparator's keys, in priority order: has the party reached this venue's band,
    /// how far up the progression ladder the band sits, how long the queue is, and the venue id
    /// (final, always-decisive tiebreak).</summary>
    private static (bool InBand, int EntryPower, int Queue, string Id) RankKey(
        string venueId, int partyPower, IReadOnlyDictionary<string, int> queueCounts)
    {
        var venue = VenueRegistry.Require(venueId);
        var queue = queueCounts.TryGetValue(venueId, out var count) ? count : 0;
        return (partyPower >= venue.EntryPower, venue.EntryPower, queue, venueId);
    }

    /// <summary>True iff <paramref name="candidate"/> outranks <paramref name="incumbent"/> under the
    /// comparator's priority order (InBand; then highest EntryPower among reached bands, lowest among
    /// unreached; then smallest Queue; then Ordinal Id). The Id fallback makes this a total order over
    /// distinct venue ids — never returns true for two identical keys, so <see cref="ChooseVenue"/>'s
    /// left-to-right scan is order-independent.</summary>
    private static bool IsBetter(
        (bool InBand, int EntryPower, int Queue, string Id) candidate,
        (bool InBand, int EntryPower, int Queue, string Id) incumbent)
    {
        if (candidate.InBand != incumbent.InBand)
        {
            return candidate.InBand;
        }

        if (candidate.EntryPower != incumbent.EntryPower)
        {
            // Reached bands: send the party as far up the ladder as it has earned. Unreached
            // bands (no EntryPower-0 venue live): the nearest band is the least-wrong home.
            return candidate.InBand
                ? candidate.EntryPower > incumbent.EntryPower
                : candidate.EntryPower < incumbent.EntryPower;
        }

        if (candidate.Queue != incumbent.Queue)
        {
            return candidate.Queue < incumbent.Queue;
        }

        return string.CompareOrdinal(candidate.Id, incumbent.Id) < 0;
    }
}
