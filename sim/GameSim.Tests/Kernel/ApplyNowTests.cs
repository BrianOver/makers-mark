using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// <see cref="GameKernel.ApplyNow"/> — the apply-one-action-without-ending-the-phase path that lets the
/// player's workshop verbs resolve the moment they are taken.
///
/// <para>The contract worth pinning is what it must NOT do: no phase advance, no day advance, no phase
/// systems. Those three are what made <see cref="GameKernel.Tick"/> unusable for immediate feedback, and
/// if any of them leaked in here the game would silently skip phases every time the player bought a
/// material.</para>
/// </summary>
public class ApplyNowTests
{
    /// <summary>A Morning system that would move Gold if it ever ran — the probe for "no systems".</summary>
    private sealed class GoldStampSystem : IPhaseSystem
    {
        public const int Stamp = 777;

        public DayPhase Phase => DayPhase.Morning;
        public string Name => "gold-stamp";

        public GameState Process(GameState state, IDeterministicRng rng, IEventSink events) =>
            state with { Player = state.Player with { Gold = Stamp } };
    }

    [Fact]
    public void ApplyNow_DoesNotAdvanceThePhaseOrTheDay()
    {
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(4242);

        var result = kernel.ApplyNow(state, new BuyMaterialAction("copper", 1));

        Assert.Equal(state.Phase, result.NewState.Phase);
        Assert.Equal(state.Day, result.NewState.Day);
    }

    [Fact]
    public void ApplyNow_DoesNotRunPhaseSystems()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new GoldStampSystem()),
            ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(4242);
        Assert.Equal(DayPhase.Morning, state.Phase); // the stamp system's own phase, so it WOULD run in Tick

        var applied = kernel.ApplyNow(state, new BuyMaterialAction("copper", 1)).NewState;
        Assert.NotEqual(GoldStampSystem.Stamp, applied.Player.Gold);

        // Control: the same kernel, the same state, through Tick — proves the probe system really is
        // wired and really would have fired, so the assertion above is about ApplyNow's behaviour and
        // not about a system that could never have run in the first place.
        var ticked = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new BuyMaterialAction("copper", 1))).NewState;
        Assert.Equal(GoldStampSystem.Stamp, ticked.Player.Gold);
    }

    [Fact]
    public void ApplyNow_WithNoHandlerForThePhase_RejectsAndChangesNothing()
    {
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(4242);

        var result = kernel.ApplyNow(state, new BuyMaterialAction("copper", 1));

        Assert.Single(result.Rejected);
        Assert.Equal(state.Player.Gold, result.NewState.Player.Gold);
    }

    [Fact]
    public void ApplyNow_LogsTheActionSoAReloadedSaveKnowsItHappened()
    {
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(4242);
        var action = new BuyMaterialAction("copper", 1);

        var result = kernel.ApplyNow(state, action);

        var batch = Assert.Single(result.NewState.ActionLog);
        Assert.Equal(action, Assert.Single(batch.Actions));
        Assert.Equal(state.Day, batch.Day);
        Assert.Equal(state.Phase, batch.Phase);
    }

    /// <summary>
    /// Same seed, same action sequence, byte-identical result — the determinism spine (KTD4) has to hold
    /// through the new path too, not just through <see cref="GameKernel.Tick"/>.
    /// </summary>
    [Fact]
    public void ApplyNow_IsDeterministic()
    {
        GameState Run()
        {
            var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
            var state = GameFactory.NewGame(99);
            for (var i = 0; i < 20; i++)
            {
                state = kernel.ApplyNow(state, new BuyMaterialAction("copper", 1)).NewState;
            }

            return state;
        }

        Assert.Equal(SaveCodec.Serialize(Run()), SaveCodec.Serialize(Run()));
    }

    /// <summary>
    /// The split itself: the workshop resolves now, the world's commitments wait for the bell. Guards
    /// against a future action type quietly becoming instant — the classifier is deny-by-default and
    /// this pins both sides of that.
    /// </summary>
    [Fact]
    public void ActionTiming_ClassifiesWorkshopVerbsInstant_AndWorldCommitmentsQueued()
    {
        Assert.True(ActionTiming.ResolvesImmediately(new BuyMaterialAction("copper", 1)));
        Assert.True(ActionTiming.ResolvesImmediately(new CraftAction("dagger", "copper")));
        Assert.True(ActionTiming.ResolvesImmediately(new StockAction(new ItemId(1), 10)));
        Assert.True(ActionTiming.ResolvesImmediately(new SetPriceAction(new ItemId(1), 10)));

        Assert.False(ActionTiming.ResolvesImmediately(new PostBountyAction(1, 25)));
        Assert.False(ActionTiming.ResolvesImmediately(new RecallPartyAction(new HeroId(1))));
        Assert.False(ActionTiming.ResolvesImmediately(new UnlockTalentAction("node", "blacksmith")));
        Assert.False(ActionTiming.ResolvesImmediately(new OpenCounterAction()));
    }
}
