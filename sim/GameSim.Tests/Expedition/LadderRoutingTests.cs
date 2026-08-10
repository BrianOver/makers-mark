using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Venues;
using Xunit;
using static GameSim.Tests.Drama.DramaFixtures;

namespace GameSim.Tests.Expedition;

/// <summary>
/// The forward ladder's rank routing (owner ruling 2026-08-10, plan 2026-08-10-003 L1, §11.8's
/// fix), exercised through the REAL production wiring — <see cref="GameComposition.BuildKernel"/>'s
/// full Morning+Expedition pipeline (<c>MusterSystem</c>/<c>MusterPlan.Compute</c> and
/// <c>ExpeditionSystem.Process</c>) — not just the pure <see cref="GameSim.Tests.Venues.VenueRouterTests"/>
/// comparator suite. Every test here shrinks the starting six to exactly the three lowest-id heroes
/// (kills 4-6) and equips every survivor with strong gear so combat rolls never introduce
/// death-driven flakiness — the point of these tests is the ROUTING decision, not combat outcome.
/// Uniform-rank trios form exactly ONE party (<c>PartyFormation</c>'s pre-L2 shape); a MIXED-rank
/// trio is the one deliberate exception (<see cref="MixedRankTrio_CohortFormation_SplitsIntoTwoPartiesRoutedByTheirOwnRank"/>) —
/// L2's cohort formation splits it into two rank-uniform parties before either ever reaches routing.
/// </summary>
public class LadderRoutingTests
{
    private static readonly Item StrongWeapon = PlayerItem(9001, "Test Blade", ItemSlot.Weapon, attack: 40, defense: 0);
    private static readonly Item StrongShield = PlayerItem(9002, "Test Shield", ItemSlot.Shield, attack: 0, defense: 40);
    private static readonly Item StrongArmor = PlayerItem(9003, "Test Armor", ItemSlot.Armor, attack: 0, defense: 40);

    /// <summary>A campaign with exactly heroes 1-3 alive, each hand-set to <paramref name="ranks"/>
    /// (one entry per hero, id order) and equipped with gear strong enough that they never lose a
    /// fight — isolating the routing decision from combat variance. The recruit gate is pushed out
    /// far past any test's loop bound so <c>RecruitSystem</c> never mints a same-morning 7th hero
    /// (its default is 0 — see <c>DramaState.Empty</c> — which fires the very first Morning while
    /// alive is short of six): pre-L2, a stray recruit's high id always sorted into the LEFTOVER slot
    /// harmlessly; post-L2, a rank-0 recruit could join a rank-0 SOLO party's cohort and silently
    /// turn it into a pair, which is exactly the kind of incidental interference this helper's own
    /// "exactly heroes 1-3 alive" contract already promised not to allow.</summary>
    private static GameState ThreeHeroParty(params int[] ranks)
    {
        var state = GameComposition.NewCampaign(seed: 4747);
        state = state with { Drama = state.Drama with { DaysUntilNextRecruit = 1000 } };

        foreach (var deadId in new[] { 4, 5, 6 })
        {
            state = state with { Heroes = state.Heroes.SetItem(deadId, state.Heroes[deadId] with { Alive = false }) };
        }

        for (var i = 0; i < 3; i++)
        {
            var id = i + 1;
            state = Equip(Equip(Equip(state, id, StrongWeapon), id, StrongShield), id, StrongArmor);
            state = state with { Heroes = state.Heroes.SetItem(id, state.Heroes[id] with { LadderRank = ranks[i] }) };
        }

        return state;
    }

    private static readonly ImmutableList<HeroId> Roster = ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3));

    /// <summary>Ticks Morning (capturing the PartiesFormed prediction) then Expedition (capturing
    /// the authoritative pick), asserting the two never disagree, and returns the actual venue id —
    /// the byte-match property (MusterPlan.Compute vs ExpeditionSystem.Process) pinned on every call.</summary>
    private static string TickMorningAndExpedition_AssertByteMatch(ref GameState state, GameKernel kernel)
    {
        var morning = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        state = morning.NewState;
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());
        var plan = Assert.Single(predicted.Parties, p => p.Roster.SequenceEqual(Roster));

        var expedition = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        state = expedition.NewState;

        // Day 1 (fresh heroes, target floor 1) is Unstaged — the result lands directly in
        // PendingExpeditions this same tick. From day 2 on, a clean stage-1 clear PARKS the party as
        // an InFlightExpedition instead (staged resolution) — check both, same as
        // VenueRoutingIntegrationTests.VenueIdsThisTick.
        var actualVenueId = state.PendingExpeditions
            .Where(r => r.Party.SequenceEqual(Roster))
            .Select(r => r.VenueId)
            .Concat(state.InFlight.Where(f => f.Party.SequenceEqual(Roster)).Select(f => f.VenueId))
            .Single();

        Assert.Equal(plan.VenueId, actualVenueId); // MusterPlan/ExpeditionSystem symmetry, pinned
        return actualVenueId;
    }

    [Fact]
    public void RookieParty_RankZero_RoutesToTheStarterTier()
    {
        var state = ThreeHeroParty(0, 0, 0);
        var kernel = GameComposition.BuildKernel();

        var venueId = TickMorningAndExpedition_AssertByteMatch(ref state, kernel);

        // Rank 0 is ineligible for Gloomwood (rank 1) — even with strong gear (high partyPower,
        // which no longer participates in routing at all), the party stays at the starter tier.
        Assert.True(venueId is "mine" or "sunken-crypt", $"expected a rank-0 starter venue, got '{venueId}'");
    }

    [Fact]
    public void RankOneParty_RoutesToGloomwood()
    {
        var state = ThreeHeroParty(1, 1, 1);
        var kernel = GameComposition.BuildKernel();

        var venueId = TickMorningAndExpedition_AssertByteMatch(ref state, kernel);

        Assert.Equal("gloomwood", venueId);
    }

    [Fact]
    public void MixedRankTrio_CohortFormation_SplitsIntoTwoPartiesRoutedByTheirOwnRank()
    {
        // L2 (cohort formation) supersedes L1's interim MIN-of-members rule: PartyFormation now
        // groups alive heroes by LadderRank BEFORE a party is ever formed (see PartyFormation's own
        // doc comment), so a rank-0 hero and two rank-1 heroes can no longer share a single party —
        // MIN dragging a veteran back to the Mine because a recruit shares her roster is exactly the
        // failure L2 exists to close. What used to be ONE mixed-rank party of three (this test's old
        // premise, MIN(0,1,1)=0) is now TWO rank-uniform parties, each routed by its own cohort.
        var state = ThreeHeroParty(0, 1, 1);
        var kernel = GameComposition.BuildKernel();

        var morning = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        state = morning.NewState;
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());
        Assert.Equal(2, predicted.Parties.Count);

        var rank0Plan = Assert.Single(predicted.Parties, p => p.Roster.Contains(new HeroId(1)));
        Assert.Equal(new HeroId(1), Assert.Single(rank0Plan.Roster));
        Assert.True(rank0Plan.VenueId is "mine" or "sunken-crypt", $"expected a rank-0 starter venue, got '{rank0Plan.VenueId}'");

        var rank1Plan = Assert.Single(predicted.Parties, p => p.Roster.Contains(new HeroId(2)));
        Assert.Equal(new[] { 2, 3 }, rank1Plan.Roster.Select(id => id.Value).OrderBy(v => v));
        Assert.Equal("gloomwood", rank1Plan.VenueId);

        var expedition = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        state = expedition.NewState;

        string ActualVenueFor(ImmutableList<HeroId> roster) => state.PendingExpeditions
            .Where(r => r.Party.SequenceEqual(roster))
            .Select(r => r.VenueId)
            .Concat(state.InFlight.Where(f => f.Party.SequenceEqual(roster)).Select(f => f.VenueId))
            .Single();

        // MusterPlan/ExpeditionSystem symmetry, pinned per party (the byte-match property this
        // whole file exercises — TickMorningAndExpedition_AssertByteMatch's single-party version).
        Assert.Equal(rank0Plan.VenueId, ActualVenueFor(rank0Plan.Roster));
        Assert.Equal(rank1Plan.VenueId, ActualVenueFor(rank1Plan.Roster));
    }

    [Fact]
    public void RankTwoParty_WithNoLiveRankTwoVenue_FallsBackToGloomwood_AndNeverOscillatesAcrossDays()
    {
        // The no-oscillation property the ladder is built on: Emberfall (rank 2) is dormant, so a
        // rank-2 party's highest ELIGIBLE live rung is Gloomwood (rank 1) — and because LadderRank
        // only ever increments, this holds every day forever; it never "flips back" to a rank-0
        // venue the way the old power latch could strand a party in the other direction.
        var state = ThreeHeroParty(2, 2, 2);
        var kernel = GameComposition.BuildKernel();

        for (var day = 0; day < 5; day++)
        {
            // Belt-and-suspenders: ThreeHeroParty already pushes the recruit gate out past this
            // loop's bound, but kill off anyone beyond the three under test each morning anyway so
            // PartyFormation keeps forming exactly ONE party across the whole run even if that
            // changes (this test's ONLY interest is that one party's routing, day over day).
            foreach (var id in state.Heroes.Keys.Where(id => id > 3))
            {
                state = state with { Heroes = state.Heroes.SetItem(id, state.Heroes[id] with { Alive = false }) };
            }

            var venueId = TickMorningAndExpedition_AssertByteMatch(ref state, kernel);
            Assert.Equal("gloomwood", venueId);

            // Ranks never regressed across the day just ticked — the property this whole wave exists
            // to guarantee.
            foreach (var id in new[] { 1, 2, 3 })
            {
                Assert.Equal(2, state.Heroes[id].LadderRank);
            }

            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Evening
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Camp
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // ExpeditionDeep
        }
    }

    [Fact]
    public void SixHeroMixedRankRoster_CohortsIntoThreeParties_EachRoutedByItsOwnRank()
    {
        // The plan's own worked example (ranks {0,0,0,1,1,2} across the full starting six): the
        // rank-0 trio (Torvald/Brunhilde/Kael), the rank-1 pair (Sable/Elowen), and the solo rank-2
        // veteran (Moss) form THREE parties, none mixed, each routed by its own cohort's rank.
        var state = GameComposition.NewCampaign(seed: 4747);
        var ranks = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 1, [5] = 1, [6] = 2 };
        foreach (var (id, rank) in ranks)
        {
            state = state with { Heroes = state.Heroes.SetItem(id, state.Heroes[id] with { LadderRank = rank }) };
        }

        var kernel = GameComposition.BuildKernel();
        var morning = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());

        Assert.Equal(3, predicted.Parties.Count);

        var rank0Plan = Assert.Single(predicted.Parties, p => p.Roster.Contains(new HeroId(1)));
        Assert.Equal(new[] { 1, 2, 3 }, rank0Plan.Roster.Select(id => id.Value).OrderBy(v => v));
        Assert.True(rank0Plan.VenueId is "mine" or "sunken-crypt", $"expected a rank-0 starter venue, got '{rank0Plan.VenueId}'");

        var rank1Plan = Assert.Single(predicted.Parties, p => p.Roster.Contains(new HeroId(4)));
        Assert.Equal(new[] { 4, 5 }, rank1Plan.Roster.Select(id => id.Value).OrderBy(v => v));
        Assert.Equal("gloomwood", rank1Plan.VenueId);

        var rank2Plan = Assert.Single(predicted.Parties, p => p.Roster.Contains(new HeroId(6)));
        Assert.Equal(new HeroId(6), Assert.Single(rank2Plan.Roster));
        // No live rank-2 venue (Emberfall dormant) — the frontier rule falls back to the highest
        // ELIGIBLE live rung, same as RankTwoParty_WithNoLiveRankTwoVenue_FallsBackToGloomwood above.
        Assert.Equal("gloomwood", rank2Plan.VenueId);
    }

    [Fact]
    public void AcceptedBounty_ShortCircuitsToTheMine_RegardlessOfRank()
    {
        // L1 scope item 5: the pre-router Mine short-circuit (bounties are structurally Mine-scoped,
        // R18) stays exactly as is — a rank-2 party (Gloomwood-eligible, and Gloomwood is the venue
        // it would otherwise route to per the test above) still raids the Mine when a member has an
        // accepted bounty.
        var state = ThreeHeroParty(2, 2, 2);
        state = state with
        {
            Bounties = ImmutableList.Create(
                new Bounty(new BountyId(1), TargetFloor: 1, RewardGold: 50, PostedOnDay: 1, AcceptedBy: new HeroId(1), Paid: false)),
        };
        var kernel = GameComposition.BuildKernel();

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Morning
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Expedition

        var actual = Assert.Single(state.PendingExpeditions, r => r.Party.SequenceEqual(Roster));
        Assert.Equal(VenueRegistry.MineId, actual.VenueId);
    }
}
