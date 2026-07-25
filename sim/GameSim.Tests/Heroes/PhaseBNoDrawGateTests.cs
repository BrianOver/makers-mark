using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Phase B (KTD-B5): the no-draw property gate for the B1 Class-0 legibility spine. B1a/B1c/B1e
/// add new stamped events, accrued XP, and re-ranked gossip selection — all of which legitimately
/// move the idle-hash <see cref="GameSim.Tests.Counter.AtomicEquivalenceTests"/> pins (values
/// differ), but NONE of it may add or reorder a kernel RNG draw. This test isolates that one
/// property: the SAME 30-day/zero-action idle trace <c>AtomicEquivalenceTests</c> runs, asserting
/// only the serialized <see cref="RngState"/> — the exact <see cref="GameSim.Tests.Professions.Alchemy.AlchemyActiveCraftTests"/>
/// <c>Assert.Equal(a.Rng, b.Rng)</c> pattern, applied to a pinned expected value instead of a
/// second run, so this survives every future 0b value re-pin untouched.
/// </summary>
public class PhaseBNoDrawGateTests
{
    [Fact]
    public void ThirtyDayIdleTrace_RngStreamPositionUnchanged_ByB1()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 9001);

        for (var i = 0; i < 30 * 5; i++) // 5-phase day (staged resolution); NEVER submits any action
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        // Pinned RNG-stream position on this exact trace — verified BYTE-IDENTICAL to the pre-B1
        // value (captured the same way against commit d51fea9, before any B1 code existed) by
        // temporarily stashing every B1 change and re-running this exact loop. Proves the whole
        // B1 spine (B1a decision cards, B1c XP/rank, B1d identity, B1e gossip salience) drew or
        // reordered ZERO kernel rolls. If this ever moves, grep `rng.` in sim/GameSim/Heroes,
        // sim/GameSim/Drama, and sim/GameSim/Advisor for a new draw site before touching this constant.
        Assert.Equal(new RngState(2746782734468342717UL, 13279888329118852579UL), state.Rng);
    }
}
