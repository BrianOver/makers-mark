using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Heroes;

/// <summary>
/// P2-PEOPLE-27 ("one like the one that held"): the proof behind a commission. A hero asking for a
/// slot names the player-crafted piece a PARTY-MATE's legend deed was earned by in that slot — the
/// most recent such deed. A party-mate is anyone who has departed in the same party as the asker
/// (<see cref="PartyDeparted"/>); a legend deed is <see cref="LegendQuery.IsLegendDeed"/> (decisive,
/// never a killing blow — 22 kills a night is the job, not proof). The asker's own deeds do not
/// count: the point is what they watched happen to someone beside them. Pure, no RNG, no clock.
/// </summary>
public static class CommissionProof
{
    public sealed record Proof(ItemId Item, HeroId ProvedFor);

    public static Proof? For(GameState state, HeroId asker, ItemSlot slot)
    {
        var mates = PartyMates(state, asker);
        if (mates.Count == 0)
        {
            return null;
        }

        for (var i = state.EventLog.Count - 1; i >= 0; i--)
        {
            if (state.EventLog[i] is not AttributionBeatEvent beat || beat.Hero == asker || !mates.Contains(beat.Hero)
                || !LegendQuery.IsLegendDeed(beat))
            {
                continue;
            }

            if (state.Items.TryGetValue(beat.Item.Value, out var item) && item.PlayerCrafted && item.Slot == slot)
            {
                return new Proof(item.Id, beat.Hero);
            }
        }

        return null;
    }

    /// <summary>Every hero who has ever departed in a party with <paramref name="hero"/>.</summary>
    public static ImmutableHashSet<HeroId> PartyMates(GameState state, HeroId hero)
    {
        var mates = ImmutableHashSet.CreateBuilder<HeroId>();
        foreach (var departed in state.EventLog.OfType<PartyDeparted>())
        {
            if (!departed.Party.Contains(hero))
            {
                continue;
            }

            foreach (var member in departed.Party)
            {
                if (member != hero)
                {
                    mates.Add(member);
                }
            }
        }

        return mates.ToImmutable();
    }
}
