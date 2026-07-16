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
/// </summary>
public static class PartyFormation
{
    private static bool IsAnchor(Hero hero) => ClassRegistry.Require(hero.ClassId).IsAnchor;

    public static ImmutableList<ImmutableList<HeroId>> FormParties(ImmutableSortedDictionary<int, Hero> heroes)
    {
        // Values of a sorted dictionary enumerate in key (HeroId.Value) order.
        var alive = heroes.Values.Where(h => h.Alive).ToList();

        // Two id-ordered queues: anchor-class heroes first, everyone else as fillers.
        var anchors = new Queue<HeroId>(alive.Where(IsAnchor).Select(h => h.Id));
        var others = new Queue<HeroId>(alive.Where(h => !IsAnchor(h)).Select(h => h.Id));

        var parties = ImmutableList.CreateBuilder<ImmutableList<HeroId>>();
        var fullParties = alive.Count / 3;

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

        if (alive.Count % 3 != 0)
        {
            // Leftovers band together as one smaller party — even a solo run.
            var leftovers = anchors.Concat(others)
                .OrderBy(id => id.Value)
                .ToImmutableList();
            parties.Add(leftovers);
        }

        return parties.ToImmutable();
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
