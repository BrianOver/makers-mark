using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

public class ExpeditionSystemTests
{
    private static GameState NewWorldWithHeroes()
    {
        var state = GameFactory.NewGame(seed: 99);
        return HeroRoster.InstallStartingRoster(state);
    }

    [Fact]
    public void ExpeditionPhase_PartiesDepart_ResultsPending_RevealDeferred()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        var state = NewWorldWithHeroes();
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Morning -> Expedition
        var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);     // Expedition runs

        Assert.NotEmpty(result.NewState.PendingExpeditions);
        Assert.Contains(result.Events, e => e is PartyDeparted);
        // Reveal is U8's job: hero state must be untouched at departure (KTD5).
        Assert.All(result.NewState.Heroes.Values, h => Assert.True(h.Alive));
        Assert.Equal(6, result.NewState.Heroes.Count);
    }

    [Fact]
    public void DeadHeroes_NeverDepart()
    {
        var state = NewWorldWithHeroes();
        var dead = state.Heroes[1] with { Alive = false, DiedOnDay = 1 };
        state = state with { Heroes = state.Heroes.SetItem(1, dead) };

        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        var departed = result.Events.OfType<PartyDeparted>().SelectMany(e => e.Party);
        Assert.DoesNotContain(new HeroId(1), departed);
    }

    [Fact]
    public void TargetFloor_IsDeepestClearedPlusOne_CappedAtFive()
    {
        var state = NewWorldWithHeroes();
        var veteran = state.Heroes[1] with { DeepestFloorReached = 5 };
        state = state with { Heroes = state.Heroes.SetItem(1, veteran) };

        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        Assert.All(result.Events.OfType<PartyDeparted>(), e => Assert.InRange(e.TargetFloor, 1, 5));
    }

    [Fact]
    public void FullDay_WithShoppingAndExpedition_IsDeterministic()
    {
        var systems = ImmutableList.Create<IPhaseSystem>(new HeroShoppingSystem(), new ExpeditionSystem());
        var kernel = new GameKernel(systems, ImmutableList<IActionHandler>.Empty);

        string Run()
        {
            var s = NewWorldWithHeroes();
            for (var i = 0; i < 9; i++) // 3 days
            {
                s = kernel.Tick(s, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return SaveCodec.Serialize(s);
        }

        Assert.Equal(Run(), Run());
    }
}
