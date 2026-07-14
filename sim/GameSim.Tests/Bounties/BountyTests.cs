using System.Collections.Immutable;
using GameSim;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Bounties;

/// <summary>R18/AE7: bounties are weighed, never obeyed; payout exactly once; escrowed gold conserved.</summary>
public class BountyTests
{
    private static GameState Post(GameState state, GameKernel kernel, int floor, int reward, out TickResult result)
    {
        result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(floor, reward)));
        return result.NewState;
    }

    private static GameKernel FullKernel() => GameComposition.BuildKernel();

    [Fact]
    public void PostBounty_EscrowsPlayerGold_EmitsPosted()
    {
        var kernel = FullKernel();
        var state = GameComposition.NewCampaign(1);
        var goldBefore = state.Player.Gold;

        state = Post(state, kernel, floor: 1, reward: 30, out var result);

        Assert.Contains(result.Events, e => e is BountyPosted);
        Assert.Equal(goldBefore - 30, state.Player.Gold);
        var bounty = Assert.Single(state.Bounties);
        Assert.Equal(1, bounty.TargetFloor);
        Assert.Null(bounty.AcceptedBy);
    }

    [Fact]
    public void PostBounty_Rejected_WhenUnaffordable_OrBadFloor_OrBadReward()
    {
        var kernel = FullKernel();
        var state = GameComposition.NewCampaign(1);

        var r1 = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 9999)));
        Assert.NotEmpty(r1.Rejected);

        var r2 = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(9, 10)));
        Assert.NotEmpty(r2.Rejected);

        var r3 = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 0)));
        Assert.NotEmpty(r3.Rejected);
    }

    [Fact]
    public void Ae7_UnreachableBounty_DeclinedWithVisibleReason()
    {
        // Fresh heroes have deepest 0 — a floor-5 bounty is beyond everyone's daring.
        var kernel = FullKernel();
        var state = GameComposition.NewCampaign(1);
        state = Post(state, kernel, floor: 5, reward: 90, out _); // Morning: posted
        var expedition = kernel.Tick(state, ImmutableList<PlayerAction>.Empty); // Expedition: judged

        var judgments = expedition.Events.OfType<BountyJudged>().ToList();
        Assert.NotEmpty(judgments);
        Assert.All(judgments, j => Assert.False(j.Accepted));
        Assert.All(judgments, j => Assert.False(string.IsNullOrWhiteSpace(j.Reason)));
        Assert.Null(expedition.NewState.Bounties[0].AcceptedBy);
    }

    [Fact]
    public void ThinReward_Declined_RichReachableBounty_Accepted()
    {
        var kernel = FullKernel();
        var state = GameComposition.NewCampaign(1);

        state = Post(state, kernel, floor: 1, reward: 5, out _);
        var thin = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        Assert.All(thin.Events.OfType<BountyJudged>(), j => Assert.False(j.Accepted));

        var state2 = GameComposition.NewCampaign(1);
        state2 = Post(state2, kernel, floor: 1, reward: 40, out _);
        var rich = kernel.Tick(state2, ImmutableList<PlayerAction>.Empty);
        var accepted = rich.Events.OfType<BountyJudged>().Where(j => j.Accepted).ToList();
        Assert.Single(accepted); // exactly one hero takes it
        Assert.Equal(accepted[0].Hero, rich.NewState.Bounties[0].AcceptedBy);
    }

    [Fact]
    public void CompletedBounty_PaysAcceptingHero_ExactlyOnce()
    {
        var kernel = FullKernel();
        var state = GameComposition.NewCampaign(1);
        state = Post(state, kernel, floor: 1, reward: 40, out _);

        // Run several full days; floor 1 gets cleared quickly by a healthy party.
        BountyPaid? paid = null;
        for (var i = 0; i < 15 && paid is null; i++)
        {
            var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = result.NewState;
            paid = result.Events.OfType<BountyPaid>().FirstOrDefault();
        }

        Assert.NotNull(paid);
        Assert.Equal(40, paid!.RewardGold);
        Assert.Empty(state.Bounties); // removed after payout — no double-pay possible

        // Continue days: no second payment ever appears.
        for (var i = 0; i < 9; i++)
        {
            var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = result.NewState;
            Assert.DoesNotContain(result.Events, e => e is BountyPaid);
        }
    }

    [Fact]
    public void UnacceptedBounty_Expires_RefundsPlayer()
    {
        var kernel = FullKernel();
        var state = GameComposition.NewCampaign(1);
        state = Post(state, kernel, floor: 5, reward: 90, out _); // nobody dares floor 5
        var goldAfterEscrow = state.Player.Gold;

        for (var i = 0; i < 3 * BountyRules.ExpiryDays + 3; i++)
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.Empty(state.Bounties);
        Assert.Equal(goldAfterEscrow + 90, state.Player.Gold);
    }

    [Fact]
    public void FullCampaign_WithBounty_Deterministic()
    {
        string Run()
        {
            var kernel = FullKernel();
            var state = GameComposition.NewCampaign(5);
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 35))).NewState;
            for (var i = 0; i < 20; i++)
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Run(), Run());
    }
}
