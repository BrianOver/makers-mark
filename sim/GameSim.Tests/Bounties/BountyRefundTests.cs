using System.Collections.Immutable;
using GameSim;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Bounties;

/// <summary>
/// Regression: an accepted bounty whose hero lives but never reaches the target floor
/// must refund its escrow at expiry — previously the gold leaked from the town total.
/// </summary>
public class BountyRefundTests
{
    [Fact]
    public void AcceptedButNeverCompleted_RefundsAtExpiry_ConservesGold()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 3);

        long TownGold(GameState s) => s.Player.Gold + s.Heroes.Values.Sum(h => (long)h.Gold);
        var before = TownGold(state);

        // Post a floor-1 bounty rich enough to be accepted, then run well past expiry.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 40))).NewState;

        var sawAccept = false;
        var refundedByExpiry = false;
        for (var i = 0; i < 5 * (BountyRules.ExpiryDays + 4); i++) // 5-phase day: 5 ticks/day
        {
            var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = result.NewState;
            if (result.Events.OfType<BountyJudged>().Any(j => j.Accepted))
            {
                sawAccept = true;
            }
        }

        // Whatever happened (paid, hero died and refunded, or accepted-incomplete refunded),
        // the board must be empty and town gold conserved modulo rival sinks — the key
        // guarantee is no escrow is stranded on the board.
        Assert.Empty(state.Bounties);
        // P2-HONEST-07: a second assertion used to follow — `Assert.DoesNotContain(state.Bounties,
        // b => !b.Paid)` — reading as "no unpaid bounty is left on the board". It could only ever
        // pass for the wrong reason. `Bounty.Paid` is `false` at its one construction site and is
        // never set true anywhere in sim/ or godot/ (BountyPayoutSystem removes a paid bounty
        // instead of flipping the flag), so the predicate `!b.Paid` is true of EVERY bounty that
        // has ever existed, and the only state satisfying that assertion is the empty board the
        // line above already proves. It restated its predecessor while appearing to add a
        // guarantee — the same false-receipt shape the satisfiable-gate census exists to catch,
        // and the reason `Bounty.Paid` stays booked as vestigial (§11, Appendix A §7).
        _ = (sawAccept, refundedByExpiry, before);
    }

    [Fact]
    public void UnacceptedBounty_StillRefunds()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 3);
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(5, 90))).NewState;
        var escrowed = state.Player.Gold;

        for (var i = 0; i < 5 * (BountyRules.ExpiryDays + 2); i++) // 5-phase day: 5 ticks/day
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.Empty(state.Bounties);
        Assert.True(state.Player.Gold >= escrowed + 90 - 200, "floor-5 bounty should have refunded (heroes can't reach it)");
        // P2-MEMORY-15: the refund is no longer silent — one event, the escrow amount, the lapse reason,
        // and nobody had accepted it.
        var refunded = Assert.Single(state.EventLog.OfType<BountyRefunded>());
        Assert.Equal(90, refunded.RewardGold);
        Assert.Equal(BountyRefundReason.Lapsed, refunded.Reason);
        Assert.Null(refunded.AcceptedBy);
    }

    [Fact]
    public void EveryRefund_IsAnnounced_AndAnnouncedRefundsSumToEscrowReturned()
    {
        // P2-MEMORY-15 two-sided: over a run that posts a bounty every morning none is refunded without
        // a BountyRefunded, and every BountyRefunded names gold the till actually got back — the sum of
        // announced refunds plus payouts equals the escrow posted minus what is still on the board.
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 2026);
        var posted = 0L;
        for (var tick = 0; tick < 5 * 40; tick++)
        {
            var actions = state.Phase == DayPhase.Morning && state.Player.Gold >= 60 && state.Bounties.Count < 3
                ? ImmutableList.Create<PlayerAction>(new PostBountyAction(state.Day % 2 == 0 ? 5 : 2, 40)) // floor 5 lapses, floor 2 pays: both branches
                : ImmutableList<PlayerAction>.Empty;
            var result = kernel.Tick(state, actions);
            posted += result.Events.OfType<BountyPosted>().Sum(b => (long)b.RewardGold);
            state = result.NewState;
        }

        var refunds = state.EventLog.OfType<BountyRefunded>().ToList();
        var paid = state.EventLog.OfType<BountyPaid>().Sum(p => (long)p.RewardGold);
        var onBoard = state.Bounties.Sum(b => (long)b.RewardGold);
        Assert.True(posted > 0, "fixture posted nothing");
        Assert.Equal(posted, refunds.Sum(r => (long)r.RewardGold) + paid + onBoard);
        Assert.True(refunds.Count > 0, "40 days of bounties produced no refund — widen the run");
        Assert.All(refunds, r => Assert.True(r.Reason == BountyRefundReason.Lapsed || r.AcceptedBy is not null,
            "a death refund must name the hero who had taken it"));
    }

    /// <summary>
    /// U0 (audit T4): pins the exact refund mechanics instead of the loose "no escrow stranded"
    /// check above — the player purse must jump by EXACTLY <c>RewardGold</c> on the precise tick
    /// where <c>state.Day == PostedOnDay + BountyRules.ExpiryDays</c> and <c>state.Phase ==
    /// Evening</c> (the tick <see cref="Bounties.BountyPayoutSystem"/> actually runs the expiry
    /// branch). Floor 5 is beyond every starting hero's day-1 reach (DeepestFloorReached 0 ⇒
    /// reach 1), so this bounty is guaranteed — deterministically, no seed-hunting — to decline
    /// every judging pass and ride untouched to expiry.
    /// </summary>
    [Fact]
    public void UnacceptedBounty_PursePlusRewardGold_OnExactExpiryDayEveningTick()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 3);

        var postedOnDay = state.Day;
        const int rewardGold = 90;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(5, rewardGold))).NewState;

        var expiryDay = postedOnDay + BountyRules.ExpiryDays;
        var sawExpiryTick = false;

        for (var i = 0; i < 5 * (BountyRules.ExpiryDays + 2) && !sawExpiryTick; i++) // 5-phase day
        {
            if (state.Day == expiryDay && state.Phase == DayPhase.Evening)
            {
                var goldBefore = state.Player.Gold;
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
                Assert.Equal(goldBefore + rewardGold, state.Player.Gold);
                sawExpiryTick = true;
                continue;
            }

            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.True(sawExpiryTick, "loop never reached the expiry-day Evening tick — widen the bound");
        Assert.Empty(state.Bounties); // refunded and dropped, not left dangling
    }

    /// <summary>
    /// U0 (audit T4/FR-1): calibrates the acceptance floor itself. <see cref="BountyRules.Judge"/>
    /// (`BountyRules.cs:19-33`) declines ONLY when the floor is beyond a hero's reach or the reward
    /// undercuts <c>floor * 10</c> — so a bounty posted at exactly <c>max(alive heroes'
    /// DeepestFloorReached) + 1</c> for exactly <see cref="BountyRules.MinimumReward"/> of that
    /// floor MUST be accepted by the first alive hero the first-accept loop reaches. Deterministic:
    /// every starting hero has <c>DeepestFloorReached == 0</c>, so the target floor is always 1 —
    /// no seed-hunting required.
    /// </summary>
    [Fact]
    public void FloorTimesTenAtDeepestReachPlusOne_IsAlwaysAccepted()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 3);

        var targetFloor = state.Heroes.Values.Where(h => h.Alive).Max(h => h.DeepestFloorReached) + 1;
        var reward = BountyRules.MinimumReward(targetFloor); // the exact acceptance floor — never below it

        var postResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(targetFloor, reward)));
        state = postResult.NewState;
        var postedId = postResult.Events.OfType<BountyPosted>().Single().Bounty;

        // The post lands during Morning; BountyJudgingSystem runs at the next tick's Expedition
        // phase (the first phase where real, event-emitting judging happens — Advance() moves
        // Morning -> Expedition).
        var judgeResult = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        var judgments = judgeResult.Events.OfType<BountyJudged>().Where(j => j.Bounty == postedId).ToList();
        Assert.Contains(judgments, j => j.Accepted);
    }
}
