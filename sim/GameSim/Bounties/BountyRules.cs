using System.Collections.Immutable;
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

    /// <summary>
    /// The first-accept loop (KTD8): every unaccepted bounty is offered to every alive hero in
    /// HeroId order; the first to accept claims it. Returns <paramref name="bounties"/> with
    /// <see cref="Bounty.AcceptedBy"/> set for whichever hero accepted this pass — already-accepted
    /// bounties pass through untouched. Pure, zero RNG. Shared by two callers: authoritative
    /// (<c>BountyJudgingSystem</c> at the Expedition tick, which passes <paramref name="onJudged"/>
    /// to emit the visible <c>BountyJudged</c> event, AE7) and predictive (<c>MusterSystem</c> at the
    /// Morning tick, silent — no callback — since the real judging is still two phases away and must
    /// not double-log).
    /// </summary>
    public static ImmutableList<Bounty> JudgeFirstAccept(
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableList<Bounty> bounties,
        Action<Bounty, Hero, bool, string>? onJudged = null)
    {
        foreach (var bounty in bounties.Where(b => b.AcceptedBy is null))
        {
            foreach (var hero in heroes.Values.Where(h => h.Alive))
            {
                var (accepted, reason) = Judge(hero, bounty);
                onJudged?.Invoke(bounty, hero, accepted, reason);

                if (accepted)
                {
                    bounties = bounties.Replace(bounty, bounty with { AcceptedBy = hero.Id });
                    break;
                }
            }
        }

        return bounties;
    }
}
