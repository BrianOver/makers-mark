using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Deterministic party grouping (R7's party half; consumed by U6's expedition resolver).
/// Rules: parties of 3 preferred, each anchored by a Vanguard when one is available;
/// leftover heroes form one smaller party (even solo). Dead heroes never party.
/// Pure function of the roster — no RNG, HeroId order throughout.
/// </summary>
public static class PartyFormation
{
    public static ImmutableList<ImmutableList<HeroId>> FormParties(ImmutableSortedDictionary<int, Hero> heroes)
    {
        // Values of a sorted dictionary enumerate in key (HeroId.Value) order.
        var alive = heroes.Values.Where(h => h.Alive).ToList();

        // Two id-ordered queues: anchors first, everyone else as fillers.
        var vanguards = new Queue<HeroId>(alive.Where(h => h.Role == HeroRole.Vanguard).Select(h => h.Id));
        var others = new Queue<HeroId>(alive.Where(h => h.Role != HeroRole.Vanguard).Select(h => h.Id));

        var parties = ImmutableList.CreateBuilder<ImmutableList<HeroId>>();
        var fullParties = alive.Count / 3;

        for (var p = 0; p < fullParties; p++)
        {
            var party = new List<HeroId>(3);

            // Anchor: one Vanguard if any are left ("at least 1" is a preference,
            // not a gate — vanguard-less rosters still go down the Mine).
            if (vanguards.Count > 0)
            {
                party.Add(vanguards.Dequeue());
            }

            // Fill with the lowest-id remaining heroes. Vanguards beyond the ones
            // reserved to anchor the REMAINING full parties may serve as fillers.
            var anchorsStillNeeded = fullParties - p - 1;
            while (party.Count < 3)
            {
                party.Add(DequeueFiller(vanguards, others, anchorsStillNeeded));
            }

            party.Sort((a, b) => a.Value.CompareTo(b.Value));
            parties.Add(party.ToImmutableList());
        }

        if (alive.Count % 3 != 0)
        {
            // Leftovers band together as one smaller party — even a solo run.
            var leftovers = vanguards.Concat(others)
                .OrderBy(id => id.Value)
                .ToImmutableList();
            parties.Add(leftovers);
        }

        return parties.ToImmutable();
    }

    /// <summary>
    /// Pop the lowest-id hero eligible to fill a slot. Vanguards are eligible fillers
    /// only when more remain than the later parties still need as anchors (if the
    /// non-vanguard queue runs dry the reserve is moot — later parties will be all
    /// vanguards anyway).
    /// </summary>
    private static HeroId DequeueFiller(Queue<HeroId> vanguards, Queue<HeroId> others, int anchorsStillNeeded)
    {
        if (others.Count == 0)
        {
            return vanguards.Dequeue();
        }

        var spareVanguards = vanguards.Count - anchorsStillNeeded;
        if (spareVanguards > 0 && vanguards.Peek().Value < others.Peek().Value)
        {
            return vanguards.Dequeue();
        }

        return others.Dequeue();
    }
}
