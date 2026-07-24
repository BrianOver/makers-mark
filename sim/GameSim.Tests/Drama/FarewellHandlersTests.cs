using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// Wave 4c (U18, farewell rite): <see cref="FarewellHandlers"/> processes
/// <see cref="HonorMemorialAction"/>. Covers the happy path (mints Honored=true + emits
/// <see cref="MemorialHonored"/>), idempotency (a second rite is a clean no-op, not a
/// rejection), the missing-memorial rejection, Evening-only phase legality, and determinism
/// (draws no RNG).
/// </summary>
public class FarewellHandlersTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new FarewellHandlers()));

    private static GameState WithMemorial(HeroId hero, string name = "Torvald", int day = 3, bool honored = false) =>
        NewWorld() with
        {
            Phase = DayPhase.Evening,
            Drama = DramaState.Empty with
            {
                Memorials = ImmutableList.Create(new Memorial(hero, name, day, "a rusty sword", honored)),
            },
        };

    // ---- Happy path -----------------------------------------------------------------

    [Fact]
    public void Honor_FlipsMemorialHonored_EmitsMemorialHonored_WithHeroName()
    {
        var state = WithMemorial(new HeroId(1));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(1))));

        Assert.Empty(result.Rejected);
        var memorial = Assert.Single(result.NewState.Drama.Memorials);
        Assert.True(memorial.Honored);

        var honored = Assert.Single(result.Events.OfType<MemorialHonored>());
        Assert.Equal(new HeroId(1), honored.Hero);
        Assert.Equal("Torvald", honored.HeroName);
    }

    // ---- Idempotency ------------------------------------------------------------------

    [Fact]
    public void Honor_AlreadyHonored_IsCleanNoOp_NoEvent_NoRejection_NoStateChange()
    {
        var state = WithMemorial(new HeroId(1), honored: true);
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(1))));

        Assert.Empty(result.Rejected); // NOT a RejectedAction — asking twice isn't wrong
        Assert.Empty(result.Events.OfType<MemorialHonored>()); // no duplicate event
        var memorial = Assert.Single(result.NewState.Drama.Memorials);
        Assert.True(memorial.Honored);
    }

    [Fact]
    public void Honor_TwiceInSequentialTicks_SecondTimeIsNoOp()
    {
        var state = WithMemorial(new HeroId(1));
        var first = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(1))));
        Assert.Empty(first.Rejected);
        Assert.Single(first.Events.OfType<MemorialHonored>());

        // The kernel advances Evening -> next-day Morning on an empty-systems tick; force the
        // phase back to Evening so the second rite is exercised in its legal phase too.
        var second = Kernel.Tick(
            first.NewState with { Phase = DayPhase.Evening },
            ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(1))));
        Assert.Empty(second.Rejected);
        Assert.Empty(second.Events.OfType<MemorialHonored>());
    }

    // ---- Rejection: no memorial ------------------------------------------------------

    [Fact]
    public void Honor_NoMemorialForHero_TypedRejection()
    {
        var state = WithMemorial(new HeroId(1));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(99))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("No memorial recorded", rejection.Reason);
        Assert.Empty(result.Events.OfType<MemorialHonored>());
    }

    // ---- Phase legality: Evening only --------------------------------------------------

    [Theory]
    [InlineData(DayPhase.Morning)]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void Honor_OutsideEvening_NoHandlerAccepts_KernelRejects(DayPhase wrongPhase)
    {
        var state = WithMemorial(new HeroId(1)) with { Phase = wrongPhase };
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(1))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("No handler accepts", rejection.Reason);
        Assert.False(result.NewState.Drama.Memorials[0].Honored);
    }

    [Fact]
    public void CanHandle_TrueOnlyDuringEvening()
    {
        var handler = new FarewellHandlers();
        var action = new HonorMemorialAction(new HeroId(1));
        Assert.True(handler.CanHandle(action, DayPhase.Evening));
        foreach (var phase in new[] { DayPhase.Morning, DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep })
        {
            Assert.False(handler.CanHandle(action, phase));
        }
    }

    // ---- Determinism: no RNG draw, byte-identical replay -------------------------------

    [Fact]
    public void Honor_DrawsNoRng_StreamPositionUntouched()
    {
        var state = WithMemorial(new HeroId(1));
        var rng = new Pcg32(state.Rng);
        var before = rng.Snapshot();
        var sink = new CollectingSink();

        var handler = new FarewellHandlers();
        var (newState, rejected) = handler.Apply(state, new HonorMemorialAction(new HeroId(1)), rng, sink);

        Assert.Null(rejected);
        Assert.Equal(before, rng.Snapshot());
        Assert.Single(sink.Events.OfType<MemorialHonored>());
        Assert.True(newState.Drama.Memorials[0].Honored);
    }

    [Fact]
    public void SameState_SameHonorAction_ByteIdenticalReplay()
    {
        var state = WithMemorial(new HeroId(1));
        var actions = ImmutableList.Create<PlayerAction>(new HonorMemorialAction(new HeroId(1)));

        var a = Kernel.Tick(state, actions);
        var b = Kernel.Tick(state, actions);

        Assert.Equal(SaveCodec.Serialize(a.NewState), SaveCodec.Serialize(b.NewState));
    }

    private sealed class CollectingSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];

        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }
}
