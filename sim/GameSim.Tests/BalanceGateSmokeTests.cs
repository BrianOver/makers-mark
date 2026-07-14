using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests;

/// <summary>
/// Balance-lane gate. U3 version: a 10-day determinism run so the required CI check
/// is never vacuous. U10 replaces this with the full 100-day balance bands.
/// </summary>
public class BalanceGateSmokeTests
{
    [Fact]
    [Trait("Category", "Balance")]
    public void TenDayRun_IsDeterministic()
    {
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);

        GameState Run()
        {
            var state = GameFactory.NewGame(seed: 2026);
            for (var i = 0; i < 30; i++)
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return state;
        }

        Assert.Equal(SaveCodec.Serialize(Run()), SaveCodec.Serialize(Run()));
    }
}
