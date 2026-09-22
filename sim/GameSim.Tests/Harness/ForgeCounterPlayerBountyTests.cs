using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Bounties;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-HONEST-47 (docs/design/MAKERS-MARK.md §11.18 measurement 1, "the reference smith posts a
/// bounty"): link 3's one lever aimed at where the heroes go (§11.7.1) — decision 6's town-facing
/// twin — had never been pulled by any policy. Every analytics run this plan has ever quoted ends
/// with <c>Bounties: 0 accepted / 0 declined</c> because no policy in
/// <c>sim/GameSim/Harness/</c> so much as names <see cref="PostBountyAction"/>.
///
/// <para>These are properties of the arm's rule — post when the Mine's own held-gate streak says
/// the town is stalling at a floor and the shop can cover the escrow, never when it can't or when
/// a bounty already targets that floor — not of a named seed or hero, so growing the roster or the
/// venue table cannot quietly stop them covering it.</para>
/// </summary>
public class ForgeCounterPlayerBountyTests
{
    private static GameState BaseState(int day, int gold, ImmutableList<GameEvent> eventLog,
        ImmutableList<ExpeditionResult> lastNight, ImmutableList<Bounty> bounties) =>
        GameFactory.NewGame(seed: 4747) with
        {
            Day = day,
            Player = PlayerState.NewGame(gold),
            EventLog = eventLog,
            LastNightExpeditions = lastNight,
            Bounties = bounties,
        };

    private static GameEvent GateHeldOn(int day) =>
        new DecisionExplained(
            "expedition-halt:mine", nameof(ExpeditionHalt.GateHeld),
            "0 survived, 0 dead, cleared 0/0, 0 floors fought")
        { Day = day };

    private static ExpeditionResult HeldAt(int floor, int power = 20, int required = 40) =>
        new(
            ImmutableList<HeroId>.Empty, TargetFloor: floor, DeepestFloorCleared: 0,
            ImmutableList<FloorOutcome>.Empty, ImmutableList<HeroId>.Empty, ImmutableList<HeroId>.Empty,
            ImmutableList<AttributionBeat>.Empty, ImmutableList<OreLoot>.Empty,
            ImmutableSortedDictionary<int, int>.Empty,
            VenueId: "mine", Halt: ExpeditionHalt.GateHeld)
        { GateHeldAt = new GateReading(floor, power, required) };

    [Fact]
    public void TwoNightsHeldAtTheSameFloor_AndTheShopCanCoverIt_PostsThere()
    {
        var reward = BountyRules.MinimumReward(3);
        var state = BaseState(
            day: 5,
            gold: reward + 100,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty);

        var post = Assert.Single(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
        Assert.Equal(3, post.TargetFloor);
        Assert.Equal(reward, post.RewardGold);
    }

    [Fact]
    public void OneNightHeld_IsNotYetAStall_NoPost()
    {
        // Only day 4 held (no day 3) — a single held evening is not "the town's marches have been
        // halting," matching DemandBoard.StallThresholdDays' own "at least two days" bar.
        var state = BaseState(
            day: 5,
            gold: 1000,
            eventLog: ImmutableList.Create(GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty);

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void ShopCannotCoverTheReward_NoPost()
    {
        var reward = BountyRules.MinimumReward(3);
        var state = BaseState(
            day: 5,
            gold: reward - 1,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty);

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void ABountyAlreadyTargetsTheHeldFloor_NeverStacksASecondEscrow()
    {
        var reward = BountyRules.MinimumReward(3);
        var existing = new Bounty(new BountyId(1), TargetFloor: 3, RewardGold: 30, PostedOnDay: 4, AcceptedBy: null, Paid: false);
        var state = BaseState(
            day: 5,
            gold: reward + 100,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList.Create(existing));

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void PostedBounty_IsAlwaysLegal_ThroughTheProductionKernel()
    {
        var reward = BountyRules.MinimumReward(3);
        var state = BaseState(
            day: 5,
            gold: reward + 100,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty);

        var kernel = GameComposition.BuildKernel();
        var result = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state));

        Assert.Empty(result.Rejected);
        Assert.Contains(result.Events, e => e is BountyPosted);
    }

    /// <summary>Seeds 1..N driven end to end through the production kernel — the same sweep shape
    /// <see cref="ForgeCounterPlayerVigilTests"/> and <see cref="ForgeCounterPlayerWakeTests"/> use.
    /// The property this unit exists to measure: the verb was never once taken by any policy, so a
    /// sweep that still posts nothing means the arm is inert.</summary>
    private static (int Posted, int Accepted, int Declined, int Paid, int Refunded) SweepBounty(int seeds, int days)
    {
        var kernel = GameComposition.BuildKernel();
        int posted = 0, accepted = 0, declined = 0, paid = 0, refunded = 0;

        foreach (var seed in Enumerable.Range(1, seeds).Select(i => (ulong)i))
        {
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= days)
            {
                var result = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state));
                state = result.NewState;

                posted += result.Events.OfType<BountyPosted>().Count();
                foreach (var judged in result.Events.OfType<BountyJudged>())
                {
                    if (judged.Accepted)
                    {
                        accepted++;
                    }
                    else
                    {
                        declined++;
                    }
                }

                paid += result.Events.OfType<BountyPaid>().Count();
                refunded += result.Events.OfType<BountyRefunded>().Count();
            }
        }

        return (posted, accepted, declined, paid, refunded);
    }

    [Fact]
    public void DrivenAcrossASeedSweep_TheSmithActuallyPostsBounties()
    {
        var tally = SweepBounty(seeds: 10, days: 60);
        Assert.True(tally.Posted > 0, $"the smith never posted a bounty across the sweep ({tally})");
    }

    [Fact]
    public void TheBountyArmIsDeterministic_SameSeedSamePosts()
    {
        // No RNG, no clock: the held-streak read and the reward math are pure functions of the
        // event log and the player's gold. Two runs of the same seeds must post identically.
        Assert.Equal(SweepBounty(seeds: 3, days: 40), SweepBounty(seeds: 3, days: 40));
    }
}
