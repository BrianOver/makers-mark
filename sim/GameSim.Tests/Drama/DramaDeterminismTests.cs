using GameSim.Kernel;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// KTD4 applied to U8: shopping + expeditions + reveal + recruits + gossip composed
/// over many days must stay byte-deterministic.
/// </summary>
public class DramaDeterminismTests
{
    [Fact]
    public void ComposedMultiDayRun_SameSeedTwice_ByteIdenticalState()
    {
        string Run()
        {
            var state = GossipTests.ComposedWorld(seed: 7);
            var systems = GossipTests.ComposedSystems();
            for (var tick = 0; tick < 50; tick++) // 10 days (5-phase)
            {
                state = Tick(state, systems).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Run(), Run());
    }
}
