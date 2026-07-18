using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// AE5 for the economy composition: two identical scripted runs through the full U7
/// stack (restock → shopping, shop + ore + Morning-vendor handlers) must serialize
/// byte-identically. No composed system or handler draws RNG, so this also guards
/// against accidental draws sneaking in.
/// </summary>
public class EconomyDeterminismTests
{
    private static GameState RunScript()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new RivalRestockSystem(), new HeroShoppingSystem()),
            ImmutableList.Create<IActionHandler>(
                new ShopHandlers(), new OreMarketHandlers(), new MaterialVendorHandlers()));

        var state = GoldConservationTests.ScriptStart();
        foreach (var actions in GoldConservationTests.ScriptTicks())
        {
            state = kernel.Tick(state, actions).NewState;
        }

        return state;
    }

    [Fact]
    public void TwoIdenticalRuns_ByteIdenticalSerializedState()
    {
        var a = SaveCodec.Serialize(RunScript());
        var b = SaveCodec.Serialize(RunScript());

        Assert.Equal(a, b);
    }

    [Fact]
    public void EconomySystems_NeverAdvanceTheRngStream()
    {
        // The whole U7 morning + evening pipeline is dice-free: the RNG state after a
        // full scripted run equals the seed-derived starting stream, untouched.
        var end = RunScript();
        Assert.Equal(RngState.FromSeed(11), end.Rng);
    }
}
