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

        // RE-PINNED for B2 (hero traits, Class 1). B1 held this BYTE-IDENTICAL to the pre-B1 value
        // (Class 0 — decisions unchanged, so the draw COUNT was identical). B2 gives heroes shop teeth,
        // so on this idle trace who buys/refuses what changes → gear → combat length → the existing
        // stream advances a DIFFERENT number of steps. The tell that this is legitimate and NOT a new
        // draw site: `Inc` (the stream identity) is UNCHANGED (13279888329118852579) — only `State`
        // (position) moved. A new/duplicated RNG stream would change Inc; a reordered/extra draw within
        // the SAME stream only moves State, which is exactly what a decision-count change does. The real
        // "no new draw site" guarantee for Class-1 units is the grep (rng.* confined to the 3 kernel
        // files); this pin now guards against an UNEXPLAINED future move. If it moves, first check Inc
        // (changed ⇒ a new stream, a real bug) then grep for a new `rng.` site.
        Assert.Equal(new RngState(6848733686438733362UL, 13279888329118852579UL), state.Rng);
    }
}
