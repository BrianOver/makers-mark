using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim; // GameComposition

namespace GameSim.Tests.Heroes;

/// <summary>
/// P2-PEOPLE-11 (owner ruling P2-OQ1, 2026-08-31, full town rest): "the town buries its own before
/// it hunts" — the calendar day immediately after ANY hero's death (<see cref="Hero.DiedOnDay"/> ==
/// yesterday) collapses Morning straight to Evening (<see cref="GameKernel"/>'s existing
/// phase-collapse rule, now wake-aware via <see cref="PartyFormation.FormParties(ImmutableSortedDictionary{int,Hero},int)"/>),
/// so nothing that depends on a party actually marching fires that day: no <see cref="PartyDeparted"/>
/// (Expedition never runs), no <see cref="BountyJudged"/> (same reason — <see
/// cref="Bounties.BountyJudgingSystem"/> is registered on <see cref="DayPhase.Expedition"/>), and no
/// new <see cref="CommissionPosted"/> (<see cref="CommissionSystem"/>'s own explicit wake gate).
/// Full end-to-end, through the real kernel — never re-deriving legality, only observing the tick's
/// event batch, the same shape every other kernel-level test in this file uses.
/// </summary>
public class WakeDayTests
{
    /// <summary>Kills the given hero on <paramref name="diedOnDay"/> and forces the clock to
    /// <paramref name="day"/>, leaving every other starting-six hero alive. A bounty is pre-posted
    /// so a wake-day regression that let Expedition run would have something to actually judge —
    /// an always-empty board would let "no BountyJudged" pass for the wrong reason.</summary>
    private static GameState StateOnDay(int day, int diedHeroId, int diedOnDay, ulong seed = 4400)
    {
        var state = GameComposition.NewCampaign(seed);
        var dead = state.Heroes[diedHeroId] with { Alive = false, DiedOnDay = diedOnDay };
        return state with
        {
            Day = day,
            Heroes = state.Heroes.SetItem(diedHeroId, dead),
            Bounties = ImmutableList.Create(
                new Bounty(new BountyId(1), TargetFloor: 1, RewardGold: 40, PostedOnDay: day - 1, AcceptedBy: null, Paid: false)),
        };
    }

    [Fact]
    public void WakeMorning_NoPartyDeparted_NoCommissionPosted_NoBountyJudged_AndCollapsesToEvening()
    {
        var state = StateOnDay(day: 2, diedHeroId: 6, diedOnDay: 1);
        var kernel = GameComposition.BuildKernel();

        var morning = kernel.Tick(state, BaselinePlayer.ActionsFor(state));

        Assert.Empty(morning.Events.OfType<PartyDeparted>());
        Assert.Empty(morning.Events.OfType<CommissionPosted>());
        Assert.Empty(morning.Events.OfType<BountyJudged>());

        // The prediction line itself must agree there is no march today (MusterPlan.Compute is now
        // wake-aware) — a non-empty PartiesFormed here would be a false omen nobody's Expedition
        // tick backs up.
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());
        Assert.Empty(predicted.Parties);

        // The phase machine actually folds the day: Morning -> Evening, day unchanged.
        Assert.Equal(DayPhase.Evening, morning.NewState.Phase);
        Assert.Equal(2, morning.NewState.Day);
    }

    [Fact]
    public void DayAfterTheWake_PartiesMarchNormally_NoChaining()
    {
        // Day 2 is the wake (a day-1 death); day 3 has no new death, so it must march exactly like
        // any ordinary day — wakes cannot chain from an old death.
        var state = StateOnDay(day: 3, diedHeroId: 6, diedOnDay: 1);
        var kernel = GameComposition.BuildKernel();

        var morning = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());
        Assert.NotEmpty(predicted.Parties);
        Assert.Equal(DayPhase.Expedition, morning.NewState.Phase); // no collapse today

        var expedition = kernel.Tick(morning.NewState, BaselinePlayer.ActionsFor(morning.NewState));
        var departed = expedition.Events.OfType<PartyDeparted>().ToList();
        Assert.NotEmpty(departed);
        Assert.Equal(predicted.Parties.Count, departed.Count);
    }

    [Fact]
    public void DeathTwoDaysAgo_IsNotAWake_MarchesNormallyToday()
    {
        var state = StateOnDay(day: 3, diedHeroId: 6, diedOnDay: 1); // died day 1, today is day 3
        var kernel = GameComposition.BuildKernel();

        var morning = kernel.Tick(state, BaselinePlayer.ActionsFor(state));

        Assert.Equal(DayPhase.Expedition, morning.NewState.Phase);
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());
        Assert.NotEmpty(predicted.Parties);
    }

    [Fact]
    public void WakeMorning_ExistingAcceptedCommission_StillExpires_OnlyNewPostingIsGated()
    {
        // T10 (already shipped) voids a dead hero's own commission on death; this test's concern is
        // narrower and P2-PEOPLE-11-specific: an ACCEPTED commission belonging to a DIFFERENT, still
        // -living hero must still be free to expire on its own deadline during a wake morning — the
        // wake gate blocks new POSTINGS only, never the existing expiry sweep.
        var state = StateOnDay(day: 2, diedHeroId: 6, diedOnDay: 1);
        var survivor = new HeroId(1);
        state = state with
        {
            Commissions = ImmutableList.Create(
                new Commission(survivor, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 1, PremiumGold: 25, Accepted: true)),
        };

        var sink = new CollectingSink();
        var after = new CommissionSystem().Process(state, new Pcg32(state.Rng), sink);

        Assert.Contains(sink.Events, e => e is CommissionExpired expired && expired.Hero == survivor);
        Assert.Empty(sink.Events.OfType<CommissionPosted>());
        Assert.Empty(after.Commissions);
    }

    private sealed class CollectingSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }
}
