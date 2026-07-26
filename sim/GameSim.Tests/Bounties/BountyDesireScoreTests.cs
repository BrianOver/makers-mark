using System.Collections.Immutable;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Bounties;

/// <summary>
/// Phase C U-C5: the D_q bounty-bite math (<c>BountyRules.DesireScore</c>/<c>Judge</c>) — greed
/// (PriceSensitivity trait) × bounty gold, minus reputation (hero Level) / distance (target
/// floor). Pure integer, DRAW-FREE (no <c>rng.</c> anywhere in <see cref="BountyRules"/>) — the
/// legible incentive card R18 promises heroes: influence, never orders.
///
/// The starting six's traits are campaign-invariant (<c>TraitRegistry.TraitsFor</c> is a pure
/// function of HeroId/Name only — B2, KTD-B3), so their PriceSensitivity draw is fixed and known:
/// Torvald/Brunhilde/Sable/Moss are neutral (neither Spendthrift nor Thrifty, greed
/// <see cref="BountyRules.BaseGreed"/>); Kael and Elowen are Spendthrift (greed
/// <see cref="BountyRules.SpendthriftGreed"/>). No seed-hunting required — verified once via
/// <c>TraitRegistry.TraitsFor</c> directly, not re-derived per test.
/// </summary>
public class BountyDesireScoreTests
{
    private static Hero HeroAt(int id, string name, int level, int deepestFloorReached = 0) => new(
        new HeroId(id), name, "vanguard", Level: level, MaxHp: 25, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepestFloorReached, DiedOnDay: null);

    private static Bounty BountyAt(int floor, int rewardGold) =>
        new(new BountyId(1), floor, rewardGold, PostedOnDay: 1, AcceptedBy: null, Paid: false);

    // ---- greed: the PriceSensitivity trait's bounty-side reading ------------------------------

    [Fact]
    public void GreedFor_MatchesTheKnownStartingCastDraw()
    {
        // Pins the exact trait draw this whole file's math depends on (campaign-invariant — B2).
        Assert.Equal(BountyRules.BaseGreed, BountyRules.GreedFor(HeroAt(1, "Torvald", 1)));
        Assert.Equal(BountyRules.BaseGreed, BountyRules.GreedFor(HeroAt(2, "Brunhilde", 1)));
        Assert.Equal(BountyRules.SpendthriftGreed, BountyRules.GreedFor(HeroAt(3, "Kael", 1)));
        Assert.Equal(BountyRules.BaseGreed, BountyRules.GreedFor(HeroAt(4, "Sable", 1)));
        Assert.Equal(BountyRules.SpendthriftGreed, BountyRules.GreedFor(HeroAt(5, "Elowen", 1)));
        Assert.Equal(BountyRules.BaseGreed, BountyRules.GreedFor(HeroAt(6, "Moss", 1)));
    }

    [Fact]
    public void Judge_GreedyHero_Accepts_NeutralHero_DeclinesTheIdenticalBounty()
    {
        // The exact reward the OLD flat rule (RewardGold >= floor*10) required and no more — a
        // neutral hero now wants a bit above that (D_q needs headroom over the reputation drag),
        // but Kael's Spendthrift greed (14 vs baseline 10) clears the identical bounty anyway.
        var bounty = BountyAt(floor: 1, rewardGold: BountyRules.MinimumReward(1)); // 10g
        var neutral = HeroAt(1, "Torvald", level: 1);
        var greedy = HeroAt(3, "Kael", level: 1);

        var (neutralAccepted, neutralReason) = BountyRules.Judge(neutral, bounty);
        var (greedyAccepted, greedyReason) = BountyRules.Judge(greedy, bounty);

        Assert.False(neutralAccepted);
        Assert.True(greedyAccepted);
        Assert.Contains("D_q", neutralReason, StringComparison.Ordinal);
        Assert.Contains("D_q", greedyReason, StringComparison.Ordinal);
    }

    // ---- reputation: Hero.Level scales the drag, strongest at short distance ------------------

    [Fact]
    public void Judge_FamousHero_Declines_GreenHero_AcceptsTheIdenticalBounty()
    {
        // Same name/id (identical greed — neutral, "Sable" draws neither Spendthrift nor Thrifty),
        // same floor, same reward: only Level (reputation) differs. The green level-1 hero bites;
        // the level-5 veteran's earned reputation makes the identical floor-1 job not worth it.
        var bounty = BountyAt(floor: 1, rewardGold: 15);
        var green = HeroAt(4, "Sable", level: 1);
        var famous = HeroAt(4, "Sable", level: 5);

        Assert.Equal(BountyRules.GreedFor(green), BountyRules.GreedFor(famous)); // isolates reputation alone

        var (greenAccepted, _) = BountyRules.Judge(green, bounty);
        var (famousAccepted, famousReason) = BountyRules.Judge(famous, bounty);

        Assert.True(greenAccepted);
        Assert.False(famousAccepted);
        Assert.Contains("rep", famousReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReputationFor_ScalesWithLevel_NeverWithMoodOrGold()
    {
        var level1 = HeroAt(1, "Torvald", level: 1);
        var level4 = HeroAt(1, "Torvald", level: 4);

        Assert.Equal(BountyRules.ReputationPerLevel, BountyRules.ReputationFor(level1));
        Assert.Equal(4 * BountyRules.ReputationPerLevel, BountyRules.ReputationFor(level4));
    }

    // ---- distance: the reputation drag SHRINKS as the bounty's floor gets farther -------------

    [Fact]
    public void DesireScore_FartherBounty_CarriesLessReputationDrag_SameHeroSameReward()
    {
        // Isolate the distance term: same hero (same greed, same reputation), same reward — only
        // TargetFloor (and so DistanceFor) differs. greed*reward is identical on both sides, so any
        // gap between the two scores is exactly the shrinking reputation/distance subtraction — a
        // famous hero's earned standing barely slows her down on a job far from town.
        var hero = HeroAt(4, "Sable", level: 10, deepestFloorReached: 10); // reach covers both floors
        var near = BountyAt(floor: 1, rewardGold: 50);
        var far = BountyAt(floor: 5, rewardGold: 50);

        var nearScore = BountyRules.DesireScore(hero, near);
        var farScore = BountyRules.DesireScore(hero, far);

        Assert.True(farScore > nearScore,
            $"expected the farther bounty's smaller reputation drag to score higher (near {nearScore}, far {farScore})");
        Assert.Equal(
            (BountyRules.ReputationFor(hero) / BountyRules.DistanceFor(near)) - (BountyRules.ReputationFor(hero) / BountyRules.DistanceFor(far)),
            farScore - nearScore);
    }

    [Fact]
    public void DesireScore_IsPureIntegerMath_NoRngNoStatePersisted()
    {
        // D_q is a comparison, never a roll (KTD2) — calling it twice for the identical inputs
        // must be byte-identical, and it must not require (or mutate) any RNG stream.
        var hero = HeroAt(4, "Sable", level: 3);
        var bounty = BountyAt(floor: 2, rewardGold: 33);

        Assert.Equal(BountyRules.DesireScore(hero, bounty), BountyRules.DesireScore(hero, bounty));
    }

    // ---- legibility card: the Reason string spells out every D_q term ------------------------

    [Fact]
    public void Judge_DeclineReason_NamesEveryD_qTerm()
    {
        var hero = HeroAt(4, "Sable", level: 5);
        var bounty = BountyAt(floor: 1, rewardGold: 15);
        var (accepted, reason) = BountyRules.Judge(hero, bounty);

        Assert.False(accepted);
        var greed = BountyRules.GreedFor(hero);
        var reputation = BountyRules.ReputationFor(hero);
        var distance = BountyRules.DistanceFor(bounty);
        var score = BountyRules.DesireScore(hero, bounty);
        var threshold = BountyRules.AcceptanceThreshold(bounty.TargetFloor);

        Assert.Contains($"D_q {score}", reason, StringComparison.Ordinal);
        Assert.Contains($"greed {greed}", reason, StringComparison.Ordinal);
        Assert.Contains($"rep {reputation}/dist {distance}", reason, StringComparison.Ordinal);
        Assert.Contains($"{threshold}", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_AcceptReason_NamesEveryD_qTerm()
    {
        var hero = HeroAt(3, "Kael", level: 1); // Spendthrift — clears the identical bounty easily
        var bounty = BountyAt(floor: 1, rewardGold: 15);
        var (accepted, reason) = BountyRules.Judge(hero, bounty);

        Assert.True(accepted);
        var greed = BountyRules.GreedFor(hero);
        var reputation = BountyRules.ReputationFor(hero);
        var distance = BountyRules.DistanceFor(bounty);
        var score = BountyRules.DesireScore(hero, bounty);
        var threshold = BountyRules.AcceptanceThreshold(bounty.TargetFloor);

        Assert.Contains($"D_q {score}", reason, StringComparison.Ordinal);
        Assert.Contains($"greed {greed}", reason, StringComparison.Ordinal);
        Assert.Contains($"rep {reputation}/dist {distance}", reason, StringComparison.Ordinal);
        Assert.Contains($"{threshold}", reason, StringComparison.Ordinal);
    }

    // ---- retreat exemption: ExpeditionSystem/ExpeditionDeepSystem DERIVE the exemption from the
    // real Bounty.AcceptedBy field (not just a resolver parameter — ResolverTests's Pin 3 already
    // proves ExpeditionResolver honors an exemption handed to it manually; these prove the SYSTEM
    // wiring actually reaches into GameState.Bounties to build that exemption for a real acceptor).

    private static Item TWeapon(int id) => new(
        new ItemId(id), "sword", "Titan Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(40, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item TArmor(int id) => new(
        new ItemId(id), "plate", "Titan Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, 30, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Titan(int id) => new(
        new HeroId(id), $"Titan{id}", "vanguard", Level: 10, MaxHp: 300, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>Two fresh Titans (ceiling 1) and a floor-3 bounty already accepted by
    /// <paramref name="acceptorId"/> (constructed directly onto <c>GameState.Bounties</c>,
    /// bypassing <c>BountyRules.Judge</c>'s own reach gate — a separate, already-tested concern —
    /// so this isolates the retreat-exemption WIRING alone). Runs Expedition -> Camp ->
    /// ExpeditionDeep on the real <see cref="ExpeditionSystem"/>/<see cref="ExpeditionDeepSystem"/>
    /// pair (GameComposition's own registration) and returns the finished result.</summary>
    private static ExpeditionResult RunExemptionScenario(int acceptorId)
    {
        var bounty = new Bounty(new BountyId(1), TargetFloor: 3, RewardGold: 300, PostedOnDay: 1,
            AcceptedBy: new HeroId(acceptorId), Paid: false);

        var state = GameFactory.NewGame(seed: 1, heroes: ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Titan(1)).Add(2, Titan(2))) with
        {
            Phase = DayPhase.Expedition,
            Items = new[] { TWeapon(90), TArmor(91) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            Bounties = ImmutableList.Create(bounty),
        };

        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList<IActionHandler>.Empty);

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Expedition: stage 1, parks camp
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Camp: no action, just advances
        var deep = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);       // ExpeditionDeep: stage 2

        return Assert.Single(deep.NewState.PendingExpeditions);
    }

    [Fact]
    public void BountyAcceptor_Hero1_PushesToTargetFloor_PartnerRetreatsAtOwnCeiling()
    {
        var result = RunExemptionScenario(acceptorId: 1);

        Assert.Equal(3, result.DeepestFloorCleared); // the acceptor honored the bounty's target floor
        Assert.Contains(result.Floors, f => f.Floor == 2 && f.Combats.Any(c => c.Hero.Value == 1));
        Assert.Contains(result.Floors, f => f.Floor == 3 && f.Combats.Any(c => c.Hero.Value == 1));
        // Hero 2 never accepted anything — her own ceiling (deepest 0) still applies past floor 1.
        Assert.DoesNotContain(result.Floors, f => f.Floor >= 2 && f.Combats.Any(c => c.Hero.Value == 2));
        Assert.Contains(new HeroId(1), result.Survivors);
        Assert.Contains(new HeroId(2), result.Survivors); // retreated, banked, never died
    }

    [Fact]
    public void BountyAcceptor_Hero2_PushesToTargetFloor_PartnerRetreatsAtOwnCeiling()
    {
        // Swap which hero accepted — proves the exemption is read off the real Bounty.AcceptedBy
        // field each time, not hardcoded to whichever hero happened to accept in the first test.
        var result = RunExemptionScenario(acceptorId: 2);

        Assert.Equal(3, result.DeepestFloorCleared);
        Assert.Contains(result.Floors, f => f.Floor == 3 && f.Combats.Any(c => c.Hero.Value == 2));
        Assert.DoesNotContain(result.Floors, f => f.Floor >= 2 && f.Combats.Any(c => c.Hero.Value == 1));
    }
}
