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

        // RE-BASELINED (Phase C U-C3 drama director): new single daily draw on the EXISTING kernel stream
        // (Inc unchanged, State advanced) — Class-2. DirectorSystem now polls once every Morning and draws
        // ONE value from the SAME stream to pick an incident, so over this 30-day idle trace the stream
        // advances an extra ~30 draws (plus the downstream combat/recruit re-shuffle those draws cause).
        // The tell this is legitimate and NOT a new stream: `Inc` (the stream identity) is UNCHANGED
        // (13279888329118852579) — only `State` (position) moved. A new/duplicated Pcg32 would change Inc;
        // adding draws to the one stream only moves State. If this moves again, first check Inc (changed ⇒
        // a new stream, a real bug) then grep for a new `rng.` site outside the 4 allowed files (which now
        // include Drama/DirectorSystem.cs).
        // RE-BASELINED (2026-08-01 venue-router power bands + T1 flip, relands #242): NO new draw —
        // the banded VenueRouter is draw-free by construction (pure integer comparison, same as the
        // tightest-fit rule it replaced), but it routes parties to DIFFERENT venues, so combat
        // draw COUNTS differ; and the RecruitPool growing 3 → 6 changes what each existing recruit
        // draw maps to. Same stream, different position: `Inc` is byte-identical to the U-C3 value
        // above — only `State` moved. The grep gate holds (no new `rng.` site; Venues/ has none).
        Assert.Equal(new RngState(4500645089244275289UL, 13279888329118852579UL), state.Rng);
    }
}
