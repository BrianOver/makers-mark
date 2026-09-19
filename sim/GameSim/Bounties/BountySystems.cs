using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Bounties;

/// <summary>
/// Expedition-phase judging: every alive hero weighs each open bounty in HeroId order;
/// the first acceptance claims it (one hero per bounty). Runs BEFORE the expedition
/// system in composition so an accepted bounty can shape that day's target floor.
/// Every judgment — accept or decline — is a visible event (AE7).
///
/// <para>P2-PEOPLE-11 (owner ruling P2-OQ1): needs no wake-day check of its own. A wake morning
/// collapses straight to <see cref="DayPhase.Evening"/> (<see cref="Kernel.GameKernel"/>'s
/// Morning->Evening fold), so this system — registered on <see cref="DayPhase.Expedition"/> — simply
/// never runs that day. The visible effect is a bounty's 3-day <see cref="BountyRules.ExpiryDays"/>
/// window losing one of its judging chances without the window itself changing:
/// <see cref="BountyPayoutSystem"/> still counts calendar days, never judged days — "the town buries
/// its own before it hunts," not "the board waits for you."</para>
/// </summary>
public sealed class BountyJudgingSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Expedition;

    public string Name => "bounty-judging";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // Shared first-accept loop (KTD8) — identical behavior, extracted so MusterSystem can
        // predict the same outcome one phase earlier (Morning) without duplicating the rule.
        var judged = BountyRules.JudgeFirstAccept(
            state.Heroes,
            state.Bounties,
            (bounty, hero, accepted, reason) => events.Emit(new BountyJudged(bounty.Id, hero.Id, accepted, reason)));

        return state with { Bounties = judged };
    }
}

/// <summary>
/// Evening payout: runs AFTER the expedition reveal so DeepestFloorReached is current.
/// A bounty pays when its accepting hero survived and has reached the target floor;
/// paid bounties are removed (no double-pay). Unaccepted bounties past expiry refund
/// the escrowed gold to the player and say so (<see cref="BountyRefunded"/>, P2-MEMORY-15).
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
                events.Emit(new BountyRefunded(bounty.Id, bounty.RewardGold, BountyRefundReason.AcceptorDied, acceptor));
                continue;
            }

            if (state.Day - bounty.PostedOnDay >= BountyRules.ExpiryDays)
            {
                // Lapsed — refund the escrow. Reached after the paid (60-71) and
                // dead-acceptor (73-79) branches, so this also catches an accepted
                // hero who lived but never reached the target floor by expiry:
                // without it, that escrow would leak from the town gold total.
                //
                // P2-PEOPLE-11: deliberately still plain calendar days, unwidened by a wake
                // morning's lost judging chance (see BountyJudgingSystem's own doc) — the ruling's
                // explicit instruction is to NAME the effective shortening, never extend the window
                // to compensate for it.
                state = state with { Player = state.Player with { Gold = state.Player.Gold + bounty.RewardGold } };
                events.Emit(new BountyRefunded(bounty.Id, bounty.RewardGold, BountyRefundReason.Lapsed, bounty.AcceptedBy));
                continue;
            }

            remaining.Add(bounty);
        }

        return state with { Bounties = remaining.ToImmutable() };
    }
}
