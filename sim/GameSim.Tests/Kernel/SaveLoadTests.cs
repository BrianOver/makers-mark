using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// Snapshot saves (KTD4): serialize → deserialize → continue must equal an uninterrupted run.
/// </summary>
public class SaveLoadTests
{
    private sealed class RngProbeSystem : IPhaseSystem
    {
        public DayPhase Phase => DayPhase.Evening;
        public string Name => "rng-probe";

        public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
        {
            return state with { Player = state.Player with { Gold = state.Player.Gold + rng.Roll100() } };
        }
    }

    [Fact]
    public void SaveAtDayN_LoadAndContinue_EqualsUninterruptedRun()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new RngProbeSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Uninterrupted: 10 full days.
        var uninterrupted = GameFactory.NewGame(seed: 1234);
        for (var i = 0; i < 30; i++)
        {
            uninterrupted = kernel.Tick(uninterrupted, ImmutableList<PlayerAction>.Empty).NewState;
        }

        // Interrupted: 5 days, save, load, 5 more days.
        var first = GameFactory.NewGame(seed: 1234);
        for (var i = 0; i < 15; i++)
        {
            first = kernel.Tick(first, ImmutableList<PlayerAction>.Empty).NewState;
        }

        var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(first));
        for (var i = 0; i < 15; i++)
        {
            loaded = kernel.Tick(loaded, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.Equal(SaveCodec.Serialize(uninterrupted), SaveCodec.Serialize(loaded));
    }

    [Fact]
    public void RoundTrip_PreservesPolymorphicActionsAndEvents()
    {
        var state = GameFactory.NewGame(seed: 5);
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(3, 50))).NewState;

        var roundTripped = SaveCodec.Deserialize(SaveCodec.Serialize(state));

        var logged = Assert.Single(roundTripped.ActionLog);
        var bounty = Assert.IsType<PostBountyAction>(logged.Actions[0]);
        Assert.Equal(3, bounty.TargetFloor);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(roundTripped));
    }
}
