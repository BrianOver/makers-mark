using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Deterministic party grouping (R7's party half; consumed by U6's expedition resolver).
/// Rules: parties of 3 preferred, each anchored by an anchor-class hero (a Vanguard, via
/// <see cref="ClassDefinition.IsAnchor"/>) when one is available; leftover heroes form one
/// smaller party (even solo). Dead heroes never party. Pure function of the roster — no RNG,
/// HeroId order throughout.
///
/// <para><b>Cohort by rank FIRST (forward ladder L2, plan 2026-08-10-003).</b> The recruit
/// trickle guarantees mixed-<see cref="Hero.LadderRank"/> rosters, so no single party-rank rule
/// can be correct: MAX-of-members would march a fresh rank-0 recruit into a veteran-scaled
/// rung she hasn't earned; MIN-of-members (the interim rule <see cref="GameSim.Venues.VenueRouter"/>
/// used before this landed) would drag a graduated veteran back to the Mine the moment ANY
/// recruit shares her party. The fix groups alive heroes by <see cref="Hero.LadderRank"/> BEFORE
/// applying the anchor/id rules above, running them independently within each cohort (ascending
/// rank order — deterministic, and inconsequential to routing since every caller shares one
/// <c>queueCounts</c> dictionary across cohorts regardless of visiting order). A cohort's own
/// leftovers form their own smaller party — a solo veteran run into the deep venue is honest
/// drama, not a bug. The postcondition every routing caller now relies on: every formed party's
/// members share exactly one <see cref="Hero.LadderRank"/>, so the old MIN-of-members routing
/// rule (<see cref="GameSim.Expedition.ExpeditionSystem.Process"/>, <see cref="MusterPlan.Compute"/>)
/// becomes exact rather than interim — MIN over a single-valued set is that value.</para>
/// </summary>
public static class PartyFormation
{
    private static bool IsAnchor(Hero hero) => ClassRegistry.Require(hero.ClassId).IsAnchor;

    /// <summary>
    /// P2-PEOPLE-11 (owner ruling P2-OQ1, 2026-08-31, full town rest): true the morning after ANY
    /// hero died — "the town buries its own before it hunts." Purely derived from
    /// <see cref="Hero.DiedOnDay"/> (never a counter, per the ruling), so it can never desync from
    /// the roster and needs no new state: a hero's death day is permanent once stamped, so this
    /// reads identically no matter when it's asked. Wakes cannot chain — deaths come only from an
    /// expedition, and <see cref="FormParties(ImmutableSortedDictionary{int,Hero},int)"/> makes sure
    /// no expedition runs ON a wake day, so day+1 is never itself a wake day from a death dated day.
    /// </summary>
    public static bool IsWakeDay(ImmutableSortedDictionary<int, Hero> heroes, int day) =>
        heroes.Values.Any(h => h.DiedOnDay == day - 1);

    /// <summary>Pure party-shape overload (no wake-day gate) — kept for callers that only care about
    /// what the roster CAN field (rank cohorts, anchor fill), never whether today is a raid day at
    /// all. <see cref="FormParties(ImmutableSortedDictionary{int,Hero},int)"/> is the one every
    /// day-authoritative caller (Expedition tick, Morning prediction, the phase-collapse check) must
    /// use instead.</summary>
    public static ImmutableList<ImmutableList<HeroId>> FormParties(ImmutableSortedDictionary<int, Hero> heroes)
    {
        // Values of a sorted dictionary enumerate in key (HeroId.Value) order.
        var alive = heroes.Values.Where(h => h.Alive).ToList();

        var parties = ImmutableList.CreateBuilder<ImmutableList<HeroId>>();

        // Cohort by rank first (see class doc): each cohort runs the existing anchor/id-order
        // rules in total isolation from every other cohort's members.
        foreach (var cohort in alive.GroupBy(h => h.LadderRank).OrderBy(g => g.Key))
        {
            FormPartiesWithinCohort(cohort.ToList(), parties);
        }

        return parties.ToImmutable();
    }

    /// <summary>
    /// The day-authoritative form of <see cref="FormParties(ImmutableSortedDictionary{int,Hero})"/>:
    /// returns empty on a wake day (<see cref="IsWakeDay"/>) before running any grouping rule at
    /// all — no party marches the morning after a death, full stop. Every caller that decides
    /// whether today is a raid day (<see cref="Expedition.ExpeditionSystem"/>, the Morning
    /// prediction in <c>MusterPlan.Compute</c>, and <see cref="Kernel.GameKernel"/>'s own
    /// phase-collapse check) must call THIS overload, not the day-less one, so the wake and the
    /// actual march can never disagree.
    /// </summary>
    public static ImmutableList<ImmutableList<HeroId>> FormParties(ImmutableSortedDictionary<int, Hero> heroes, int day) =>
        IsWakeDay(heroes, day) ? ImmutableList<ImmutableList<HeroId>>.Empty : FormParties(heroes);

    /// <summary>The pre-L2 grouping rules (anchor preference, id-order fill, smaller leftover
    /// party), now scoped to a single already-rank-uniform cohort instead of the whole roster.
    /// Appends every party this cohort forms onto the shared <paramref name="parties"/> builder.</summary>
    private static void FormPartiesWithinCohort(List<Hero> cohort, ImmutableList<ImmutableList<HeroId>>.Builder parties)
    {
        // Two id-ordered queues: anchor-class heroes first, everyone else as fillers.
        var anchors = new Queue<HeroId>(cohort.Where(IsAnchor).Select(h => h.Id));
        var others = new Queue<HeroId>(cohort.Where(h => !IsAnchor(h)).Select(h => h.Id));

        var fullParties = cohort.Count / 3;

        for (var p = 0; p < fullParties; p++)
        {
            var party = new List<HeroId>(3);

            // Anchor: one anchor-class hero if any are left ("at least 1" is a preference,
            // not a gate — anchor-less rosters still go down the Mine).
            if (anchors.Count > 0)
            {
                party.Add(anchors.Dequeue());
            }

            // Fill with the lowest-id remaining heroes. Anchors beyond the ones
            // reserved to anchor the REMAINING full parties may serve as fillers.
            var anchorsStillNeeded = fullParties - p - 1;
            while (party.Count < 3)
            {
                party.Add(DequeueFiller(anchors, others, anchorsStillNeeded));
            }

            party.Sort((a, b) => a.Value.CompareTo(b.Value));
            parties.Add(party.ToImmutableList());
        }

        if (cohort.Count % 3 != 0)
        {
            // Leftovers band together as one smaller party — even a solo run.
            var leftovers = anchors.Concat(others)
                .OrderBy(id => id.Value)
                .ToImmutableList();
            parties.Add(leftovers);
        }
    }

    /// <summary>
    /// Pop the lowest-id hero eligible to fill a slot. Anchor-class heroes are eligible
    /// fillers only when more remain than the later parties still need as anchors (if the
    /// non-anchor queue runs dry the reserve is moot — later parties will be all anchors
    /// anyway).
    /// </summary>
    private static HeroId DequeueFiller(Queue<HeroId> anchors, Queue<HeroId> others, int anchorsStillNeeded)
    {
        if (others.Count == 0)
        {
            return anchors.Dequeue();
        }

        var spareAnchors = anchors.Count - anchorsStillNeeded;
        if (spareAnchors > 0 && anchors.Peek().Value < others.Peek().Value)
        {
            return anchors.Dequeue();
        }

        return others.Dequeue();
    }
}
