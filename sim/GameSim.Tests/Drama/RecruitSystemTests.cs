using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// Recruit trickle (R10/AE6): dead heroes are replaced at a gated rate — never a
/// dead-end save, never a roster above six.
/// </summary>
public class RecruitSystemTests
{
    private static GameState Kill(GameState state, int heroId) => state with
    {
        Heroes = state.Heroes.SetItem(heroId, state.Heroes[heroId] with { Alive = false, DiedOnDay = state.Day }),
    };

    private static int AliveCount(GameState state) => state.Heroes.Values.Count(h => h.Alive);

    [Fact]
    public void DeadHero_RecruitArrivesNextMorning_WhenGateIdle()
    {
        var state = Kill(NewWorld(), 1);

        var tick = Tick(state, new RecruitSystem());

        var arrived = Assert.Single(tick.Events.OfType<RecruitArrived>());
        Assert.Equal(new HeroId(7), arrived.Hero);
        Assert.Equal(8, tick.NewState.NextHeroId);
        Assert.Equal(6, AliveCount(tick.NewState));
        Assert.Equal(RecruitSystem.RecruitGateDays, tick.NewState.Drama.DaysUntilNextRecruit);

        var recruit = tick.NewState.Heroes[7];
        Assert.True(recruit.Alive);
        Assert.Equal(1, recruit.Level);
        Assert.False(string.IsNullOrEmpty(recruit.Name));
    }

    [Fact]
    public void FullRoster_NoRecruit_EvenWithGateIdle()
    {
        var tick = Tick(NewWorld(), new RecruitSystem());

        Assert.Empty(tick.Events.OfType<RecruitArrived>());
        Assert.Equal(7, tick.NewState.NextHeroId);
        Assert.Equal(6, AliveCount(tick.NewState));
    }

    [Fact]
    public void GateDecrementsEachMorning_SecondRecruitArrivesOnGateExpiry()
    {
        // Two deaths, one idle gate: recruit #1 arrives morning 1, recruit #2 exactly
        // RecruitGateDays mornings later — the trickle, not a flood.
        var state = Kill(Kill(NewWorld(), 1), 2);
        var system = new RecruitSystem();

        var tick = Tick(state, system); // morning 1: recruit #1
        Assert.Single(tick.Events.OfType<RecruitArrived>());
        Assert.Equal(5, AliveCount(tick.NewState));
        state = tick.NewState;

        var arrivals = new List<int>(); // mornings (1-based from now) when a recruit arrived
        for (var morning = 1; morning <= RecruitSystem.RecruitGateDays; morning++)
        {
            state = Tick(state, system).NewState;                    // Expedition (no-op)
            state = Tick(state, system).NewState;                    // Evening (no-op)
            var morningTick = Tick(state, system);                   // next Morning
            state = morningTick.NewState;
            if (morningTick.Events.OfType<RecruitArrived>().Any())
            {
                arrivals.Add(morning);
            }
        }

        Assert.Equal(new[] { RecruitSystem.RecruitGateDays }, arrivals); // within 3 mornings, on the dot
        Assert.Equal(6, AliveCount(state));
        Assert.Equal(9, state.NextHeroId); // two recruits minted: 7 and 8
    }

    [Fact]
    public void AE6_DeathThroughReveal_MemorialAndRecruitWithinThreeMornings_RosterCapped()
    {
        // Full AE6 arc: a crafted death result revealed at Evening, then mornings roll.
        var state = NewWorld();
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            floors: [new FloorOutcome(1, false, [Combat(1, 1, "Cave Rat", taken: 40)])]);
        var systems = new IPhaseSystem[] { new RecruitSystem(), new ExpeditionRevealSystem() };

        var evening = Tick(AtEvening(state, result), systems);
        Assert.Single(evening.Events.OfType<HeroDied>());
        Assert.Single(evening.NewState.Drama.Memorials);
        Assert.Equal(5, AliveCount(evening.NewState));
        state = evening.NewState;

        var morningsUntilRecruit = 0;
        for (var morning = 1; morning <= 3 && morningsUntilRecruit == 0; morning++)
        {
            var tick = Tick(state, systems); // Morning
            state = tick.NewState;
            if (tick.Events.OfType<RecruitArrived>().Any())
            {
                morningsUntilRecruit = morning;
            }

            state = Tick(state, systems).NewState; // Expedition (no-op)
            state = Tick(state, systems).NewState; // Evening (no-op reveal)
        }

        Assert.InRange(morningsUntilRecruit, 1, 3); // AE6: within the gated window
        Assert.Equal(6, AliveCount(state));
        Assert.Equal(8, state.NextHeroId);
    }

    [Fact]
    public void RosterNeverExceedsSix_OverALongRun()
    {
        var state = Kill(Kill(Kill(NewWorld(), 1), 3), 5);
        var system = new RecruitSystem();

        for (var tick = 0; tick < 90; tick++) // 30 full days
        {
            state = Tick(state, system).NewState;
            Assert.True(AliveCount(state) <= 6, $"alive roster exceeded six at tick {tick}");
        }

        Assert.Equal(6, AliveCount(state)); // returned toward six, then held
    }

    [Fact]
    public void RecruitDraws_AreDeterministic()
    {
        Hero RecruitOf(GameState end) => end.Heroes[7];

        var a = Tick(Kill(NewWorld(seed: 11), 1), new RecruitSystem()).NewState;
        var b = Tick(Kill(NewWorld(seed: 11), 1), new RecruitSystem()).NewState;

        Assert.Equal(RecruitOf(a), RecruitOf(b));
    }
}
