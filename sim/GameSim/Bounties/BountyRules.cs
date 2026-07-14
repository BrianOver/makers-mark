using GameSim.Contracts;

namespace GameSim.Bounties;

/// <summary>
/// How heroes weigh a bounty (R18): influence, never orders. Pure decision logic
/// with legible reasons, mirroring the shopping AI's pattern (R8).
/// </summary>
public static class BountyRules
{
    /// <summary>Unaccepted bounties lapse and refund after this many days (tuned in U10).</summary>
    public const int ExpiryDays = 3;

    /// <summary>Minimum reward per target floor a hero considers worth the risk.</summary>
    public static int MinimumReward(int floor) => floor * 10;

    /// <summary>Weigh a bounty for one hero. Accept, or decline with a visible reason (AE7).</summary>
    public static (bool Accepted, string Reason) Judge(Hero hero, Bounty bounty)
    {
        var reach = hero.DeepestFloorReached + 1;
        if (bounty.TargetFloor > reach)
        {
            return (false, $"floor {bounty.TargetFloor} is beyond what {hero.Name} dares (deepest: {hero.DeepestFloorReached})");
        }

        if (bounty.RewardGold < MinimumReward(bounty.TargetFloor))
        {
            return (false, $"{bounty.RewardGold}g is too thin for floor {bounty.TargetFloor} — {hero.Name} wants {MinimumReward(bounty.TargetFloor)}g");
        }

        return (true, $"{hero.Name} takes the floor {bounty.TargetFloor} bounty for {bounty.RewardGold}g");
    }
}
