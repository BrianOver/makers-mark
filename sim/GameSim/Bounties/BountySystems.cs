using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Bounties;

/// <summary>
/// Expedition-phase judging: every alive hero weighs each open bounty in HeroId order;
/// the first acceptance claims it (one hero per bounty). Runs BEFORE the expedition
/// system in composition so an accepted bounty can shape that day's target floor.
/// Every judgment — accept or decline — is a visible event (AE7).
/// </summary>
public sealed class BountyJudgingSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Expedition;

    public string Name => "bounty-judging";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        foreach (var bounty in state.Bounties.Where(b => b.AcceptedBy is null))
        {
            foreach (var hero in state.Heroes.Values.Where(h => h.Alive))
            {
                var (accepted, reason) = BountyRules.Judge(hero, bounty);
                events.Emit(new BountyJudged(bounty.Id, hero.Id, accepted, reason));

                if (accepted)
                {
                    state = state with
                    {
                        Bounties = state.Bounties.Replace(bounty, bounty with { AcceptedBy = hero.Id }),
                    };
                    break;
                }
            }
        }

        return state;
    }
}

/// <summary>
/// Evening payout: runs AFTER the expedition reveal so DeepestFloorReached is current.
/// A bounty pays when its accepting hero survived and has reached the target floor;
/// paid bounties are removed (no double-pay). Unaccepted bounties past expiry refund
/// the escrowed gold to the player silently (documented policy; no event type exists).
/// </summary>
public sealed class BountyPayoutSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Evening;

    public string Name => "bounty-payout";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var remaining = ImmutableList.CreateBuilder<Bounty>();

        foreach (var bounty in state.Bounties)
        {
            if (bounty.AcceptedBy is { } heroId
                && state.Heroes.TryGetValue(heroId.Value, out var hero)
                && hero.Alive
                && hero.DeepestFloorReached >= bounty.TargetFloor)
            {
                state = state with
                {
                    Heroes = state.Heroes.SetItem(heroId.Value, hero with { Gold = hero.Gold + bounty.RewardGold }),
                };
                events.Emit(new BountyPaid(bounty.Id, heroId, bounty.RewardGold));
                continue; // paid — drop from the board
            }

            if (bounty.AcceptedBy is { } acceptor
                && (!state.Heroes.TryGetValue(acceptor.Value, out var h) || !h.Alive))
            {
                // Acceptor died before completing: refund and drop.
                state = state with { Player = state.Player with { Gold = state.Player.Gold + bounty.RewardGold } };
                continue;
            }

            if (state.Day - bounty.PostedOnDay >= BountyRules.ExpiryDays)
            {
                // Lapsed — refund the escrow. Reached after the paid (60-71) and
                // dead-acceptor (73-79) branches, so this also catches an accepted
                // hero who lived but never reached the target floor by expiry:
                // without it, that escrow would leak from the town gold total.
                state = state with { Player = state.Player with { Gold = state.Player.Gold + bounty.RewardGold } };
                continue;
            }

            remaining.Add(bounty);
        }

        return state with { Bounties = remaining.ToImmutable() };
    }
}
