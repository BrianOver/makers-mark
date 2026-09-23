using System.Collections.Immutable;
using GameSim.Classes;
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
/// <para>P2-HONEST-49 (§11.19 measurement 1) found the arm posting a price nobody would take:
/// <see cref="BountyRules.MinimumReward"/> alone clears <see cref="BountyRules.AcceptanceThreshold"/>
/// for no hero who has raised a level, because <see cref="BountyRules.ReputationFor"/> only ever
/// subtracts. The arm now prices off the acceptance rule itself — the tests below assert that
/// property (a real hero would take the posted price; nobody would take one gold less; nothing is
/// posted when no living, reach-eligible hero could ever take the floor) rather than pinning the
/// number <see cref="BountyRules.MinimumReward"/> happens to return.</para>
///
/// <para>These are properties of the arm's rule — post when the Mine's own held-gate streak says
/// the town is stalling at a floor and the shop can cover a price a real hero would take, never
/// when it can't or when a bounty already targets that floor — not of a named seed or hero, so
/// growing the roster or the venue table cannot quietly stop them covering it.</para>
///
/// <para>P2-HONEST-50 (§11.19 measurement 2) found the posted floor was always the SAME floor the
/// party had just failed at — pricing the party's own plan, not aiming it anywhere. The arm now
/// names a floor within some living hero's own reach that is NOT the held floor (the floor
/// <c>ExpeditionSystem.TargetFloorFor</c>'s own non-bounty formula says the party was already
/// marching to). The tests below assert that property — the posted floor is never the held floor,
/// and it always sits within a real hero's own reach — never a fixed floor number.</para>
/// </summary>
public class ForgeCounterPlayerBountyTests
{
    private static Hero MakeHero(int id, int level, int deepestFloorReached) => new(
        new HeroId(id), $"Hero{id}", ClassRegistry.VanguardId, Level: level, MaxHp: 25, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepestFloorReached, DiedOnDay: null);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static GameState BaseState(int day, int gold, ImmutableList<GameEvent> eventLog,
        ImmutableList<ExpeditionResult> lastNight, ImmutableList<Bounty> bounties,
        ImmutableSortedDictionary<int, Hero>? heroes = null) =>
        GameFactory.NewGame(seed: 4747) with
        {
            Day = day,
            Player = PlayerState.NewGame(gold),
            EventLog = eventLog,
            LastNightExpeditions = lastNight,
            Bounties = bounties,
            Heroes = heroes ?? ImmutableSortedDictionary<int, Hero>.Empty,
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

    /// <summary>A throwaway probe bounty for asking <see cref="BountyRules.Judge"/> directly — never
    /// posted, mirroring how the production arm itself prices (never re-deriving the D_q formula).</summary>
    private static Bounty ProbeBounty(int floor, int reward) =>
        new(new BountyId(999), floor, reward, PostedOnDay: 0, AcceptedBy: null, Paid: false);

    [Fact]
    public void TwoNightsHeldAtTheSameFloor_AndTheShopCanCoverIt_PostsAPriceARealHeroWouldTake()
    {
        var hero = MakeHero(1, level: 3, deepestFloorReached: 3);
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: Roster(hero));

        var post = Assert.Single(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());

        // P2-HONEST-50: the posted floor is never the held floor (3, the floor the party was
        // already marching to), and it always sits within the reach BountyRules.Judge enforces.
        Assert.NotEqual(3, post.TargetFloor);
        Assert.True(post.TargetFloor <= hero.DeepestFloorReached + 1,
            "the posted floor must sit within some living hero's own reach");

        // The property, not the number: some reach-eligible living hero actually takes the posted
        // price, and nobody would have taken one gold less — the cheapest price the rule allows.
        Assert.True(BountyRules.Judge(hero, ProbeBounty(post.TargetFloor, post.RewardGold)).Accepted,
            "the posted price must clear the acceptance rule for a real hero");
        Assert.False(BountyRules.Judge(hero, ProbeBounty(post.TargetFloor, post.RewardGold - 1)).Accepted,
            "one gold cheaper must NOT clear the acceptance rule — the arm must post the cheapest taken price");
    }

    [Fact]
    public void EveryLivingHeroReachEqualsTheHeldFloor_NoDivertedTargetExists_PostsNothing()
    {
        // P2-HONEST-50: the arm no longer posts at the held floor itself, so a roster whose ENTIRE
        // reach has converged on that floor gives it nowhere else to aim — not "no hero could ever
        // take the floor" (MinAcceptableReward's job) but "there is no floor left to name."
        var converged = MakeHero(1, level: 5, deepestFloorReached: 2); // reach = 3 == held floor
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: Roster(converged));

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void PicksTheDeepestReachableFloorThatIsNotTheHeldFloor_NotJustTheRosterMax()
    {
        // Held floor is 3. HeroA's own reach (4) equals nothing special; HeroB's reach (6) is the
        // roster's deepest but coincides with nothing here either — the point is exclusion: a THIRD
        // hero whose reach equals the held floor must never win even though it's a legal candidate
        // in isolation, and among what's left the DEEPEST reach wins, not the first one found.
        var matchesHeldFloor = MakeHero(1, level: 4, deepestFloorReached: 2); // reach 3 == avoid
        var shallow = MakeHero(2, level: 2, deepestFloorReached: 1);          // reach 2
        var deepest = MakeHero(3, level: 6, deepestFloorReached: 4);         // reach 5
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: Roster(matchesHeldFloor, shallow, deepest));

        var post = Assert.Single(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
        Assert.Equal(5, post.TargetFloor);
    }

    [Fact]
    public void NoLivingHeroAtAll_PostsNothing()
    {
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: ImmutableSortedDictionary<int, Hero>.Empty);

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void OneNightHeld_IsNotYetAStall_NoPost()
    {
        // Only day 4 held (no day 3) — a single held evening is not "the town's marches have been
        // halting," matching DemandBoard.StallThresholdDays' own "at least two days" bar.
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: Roster(MakeHero(1, level: 3, deepestFloorReached: 3)));

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void ShopCannotCoverThePriceTheRuleWouldTake_NoPost()
    {
        var hero = MakeHero(1, level: 3, deepestFloorReached: 3);
        var probeState = BaseState(
            day: 5, gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: Roster(hero));
        var wouldPost = Assert.Single(ForgeCounterPlayer.ActionsFor(probeState).OfType<PostBountyAction>());

        var poor = probeState with { Player = PlayerState.NewGame(wouldPost.RewardGold - 1) };
        Assert.Empty(ForgeCounterPlayer.ActionsFor(poor).OfType<PostBountyAction>());
    }

    [Fact]
    public void ABountyAlreadyTargetsTheDivertedFloor_NeverStacksASecondEscrow()
    {
        // Held floor is 3; this hero's own reach (4) is the only diverted candidate — an existing
        // bounty AT THAT DIVERTED FLOOR (not the held floor) must still block a second escrow.
        var existing = new Bounty(new BountyId(1), TargetFloor: 4, RewardGold: 30, PostedOnDay: 4, AcceptedBy: null, Paid: false);
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList.Create(existing),
            heroes: Roster(MakeHero(1, level: 3, deepestFloorReached: 3)));

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state).OfType<PostBountyAction>());
    }

    [Fact]
    public void PostedBounty_IsAlwaysLegal_ThroughTheProductionKernel()
    {
        var state = BaseState(
            day: 5,
            gold: 100_000,
            eventLog: ImmutableList.Create(GateHeldOn(3), GateHeldOn(4)),
            lastNight: ImmutableList.Create(HeldAt(3)),
            bounties: ImmutableList<Bounty>.Empty,
            heroes: Roster(MakeHero(1, level: 3, deepestFloorReached: 3)));

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
