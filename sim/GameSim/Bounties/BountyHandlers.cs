using GameSim.Contracts;
using GameSim.Expedition;

namespace GameSim.Bounties;

/// <summary>
/// PostBountyAction handling (R18). The reward is ESCROWED from the player's gold at
/// post time — payout moves escrow to the hero; expiry refunds it. Gold is conserved.
/// </summary>
public sealed class BountyHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is PostBountyAction && phase != DayPhase.Expedition;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        var post = (PostBountyAction)action;

        if (post.TargetFloor is < 1 or > MonsterTable.FloorCount)
        {
            return (state, new RejectedAction(action, $"The Mine has floors 1-{MonsterTable.FloorCount}; {post.TargetFloor} isn't one of them."));
        }

        if (post.RewardGold <= 0)
        {
            return (state, new RejectedAction(action, "A bounty needs a positive reward."));
        }

        if (state.Player.Gold < post.RewardGold)
        {
            return (state, new RejectedAction(action, $"Can't escrow {post.RewardGold}g — you have {state.Player.Gold}g."));
        }

        var bounty = new Bounty(
            new BountyId(state.NextBountyId), post.TargetFloor, post.RewardGold,
            PostedOnDay: state.Day, AcceptedBy: null, Paid: false);

        events.Emit(new BountyPosted(bounty.Id, bounty.TargetFloor, bounty.RewardGold));

        return (state with
        {
            NextBountyId = state.NextBountyId + 1,
            Player = state.Player with { Gold = state.Player.Gold - post.RewardGold },
            Bounties = state.Bounties.Add(bounty),
        }, null);
    }
}
