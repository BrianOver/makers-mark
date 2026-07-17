using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

public class PhaseMachineTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList<IActionHandler>.Empty);

    [Fact]
    public void EmptyCampAndDeepTicks_DrawNoRng()
    {
        // U2 proof-of-stream-preservation: the two new ticks (Camp, ExpeditionDeep) have no
        // registered systems yet, so a composed kernel must draw ZERO RNG across them —
        // the Rng snapshot is byte-equal before and after each. This is what keeps the
        // balance bands unchanged after U2 (the stream didn't move).
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 4242);

        // Advance to the Camp tick (Morning -> Expedition -> Camp).
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Morning done
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Expedition done
        Assert.Equal(DayPhase.Camp, state.Phase);

        var beforeCamp = state.Rng;
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Camp tick
        Assert.Equal(DayPhase.ExpeditionDeep, state.Phase);
        Assert.Equal(beforeCamp, state.Rng); // Camp drew nothing

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // ExpeditionDeep tick
        Assert.Equal(DayPhase.Evening, state.Phase);
        Assert.Equal(beforeCamp, state.Rng); // ExpeditionDeep drew nothing either
    }

    [Fact]
    public void PostBounty_DuringCamp_IsRejected()
    {
        // D2: the BountyHandlers whitelist is Morning/Evening only — posting during the new
        // Camp tick must come back rejected (no handler accepts it), not silently legalized.
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 4243);

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Morning done
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // Expedition done
        Assert.Equal(DayPhase.Camp, state.Phase);

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(3, 50)));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<PostBountyAction>(rejected.Action);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
    }

    [Fact]
    public void PhasesAdvance_FivePhaseDay_ThenNextDay()
    {
        var state = GameFactory.NewGame(seed: 1);
        Assert.Equal((1, DayPhase.Morning), (state.Day, state.Phase));

        state = Kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        Assert.Equal((1, DayPhase.Expedition), (state.Day, state.Phase));

        state = Kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        Assert.Equal((1, DayPhase.Camp), (state.Day, state.Phase));

        state = Kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        Assert.Equal((1, DayPhase.ExpeditionDeep), (state.Day, state.Phase));

        state = Kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        Assert.Equal((1, DayPhase.Evening), (state.Day, state.Phase));

        state = Kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        Assert.Equal((2, DayPhase.Morning), (state.Day, state.Phase)); // day 2 after 5 ticks
    }

    [Fact]
    public void UnhandledAction_IsRejectedWithTypedReason_NeverSilentlyDropped()
    {
        // No handlers are registered, so any action must come back rejected.
        var state = GameFactory.NewGame(seed: 1);
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("iron-sword", "iron")));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<CraftAction>(rejected.Action);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
    }

    [Fact]
    public void ActionLog_RecordsEveryBatchInOrder()
    {
        var state = GameFactory.NewGame(seed: 1);
        var batch1 = ImmutableList.Create<PlayerAction>(new SetPriceAction(new ItemId(1), 10));
        var batch2 = ImmutableList.Create<PlayerAction>(new UnstockAction(new ItemId(1)));

        state = Kernel.Tick(state, batch1).NewState;
        state = Kernel.Tick(state, batch2).NewState;

        Assert.Equal(2, state.ActionLog.Count);
        Assert.Equal(DayPhase.Morning, state.ActionLog[0].Phase);
        Assert.Equal(DayPhase.Expedition, state.ActionLog[1].Phase);
        Assert.IsType<SetPriceAction>(state.ActionLog[0].Actions[0]);
    }

    [Fact]
    public void Events_GetSequentialIds_AndDayStamps()
    {
        var state = GameFactory.NewGame(seed: 1);
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new EmitTwoEventsSystem()),
            ImmutableList<IActionHandler>.Empty);

        var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(result.Events[0].Id.Value + 1, result.Events[1].Id.Value);
        Assert.All(result.Events, e => Assert.Equal(1, e.Day));
        Assert.Equal(result.NewState.EventLog.Count, result.Events.Count);
    }

    private sealed class EmitTwoEventsSystem : IPhaseSystem
    {
        public DayPhase Phase => DayPhase.Morning;
        public string Name => "emit-two";

        public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
        {
            events.Emit(new RecruitArrived(new HeroId(99)));
            events.Emit(new RecruitArrived(new HeroId(100)));
            return state;
        }
    }
}
