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
/// (kills 4-6) so <c>PartyFormation</c> forms exactly ONE party, and equips every survivor with
/// strong gear so combat rolls never introduce death-driven flakiness — the point of these tests is
/// the ROUTING decision, not combat outcome.
/// </summary>
public class LadderRoutingTests
{
    private static readonly Item StrongWeapon = PlayerItem(9001, "Test Blade", ItemSlot.Weapon, attack: 40, defense: 0);
    private static readonly Item StrongShield = PlayerItem(9002, "Test Shield", ItemSlot.Shield, attack: 0, defense: 40);
    private static readonly Item StrongArmor = PlayerItem(9003, "Test Armor", ItemSlot.Armor, attack: 0, defense: 40);

    /// <summary>A campaign with exactly heroes 1-3 alive, each hand-set to <paramref name="ranks"/>
    /// (one entry per hero, id order) and equipped with gear strong enough that they never lose a
    /// fight — isolating the routing decision from combat variance.</summary>
    private static GameState ThreeHeroParty(params int[] ranks)
    {
        var state = GameComposition.NewCampaign(seed: 4747);

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
    public void MixedRankParty_InterimMinRule_RoutesByTheLowestMember()
    {
        // Interim rule (L1; L2's cohort formation supersedes it — see VenueRouter.ChooseVenue's own
        // doc comment): party rank = MIN of members, so a party with even one rank-0 member never
        // marches into a rung she hasn't earned.
        var state = ThreeHeroParty(0, 1, 1);
        var kernel = GameComposition.BuildKernel();

        var venueId = TickMorningAndExpedition_AssertByteMatch(ref state, kernel);

        Assert.True(venueId is "mine" or "sunken-crypt", $"MIN(0,1,1)=0 should stay at the starter tier, got '{venueId}'");
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
            // The recruit trickle adds new heroes on its own schedule — kill off anyone beyond the
            // three under test each morning so PartyFormation keeps forming exactly ONE party across
            // the whole run (this test's ONLY interest is that one party's routing, day over day).
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
