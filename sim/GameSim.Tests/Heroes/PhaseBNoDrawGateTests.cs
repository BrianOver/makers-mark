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
        // RE-BASELINED (2026-08-01 same PR window, Gloomwood band 55 → 72): same class as above —
        // mid-power idle parties now stay in the early band until 72, so their venues and combat
        // draw counts shift again. `Inc` STILL byte-identical; only `State` moved.
        // GOLDEN RE-BASELINE #1 OF 5 (forward-ladder plan 2026-08-10-003, L1 — graduation + rank
        // router, §11.8's fix): VenueRouter now keys on Hero.LadderRank instead of party power
        // against EntryPower — see AtomicEquivalenceTests.cs's matching ledger entry for the full
        // account (this exact idle trace is the one whose power crossed the old Gloomwood band and
        // drove the two 2026-08-01 re-pins directly above; under the ladder it never graduates out
        // of rank 0, so it never reaches Gloomwood at all, and the combat draws after the old
        // divergence point shift). `Inc` is STILL byte-identical (13279888329118852579) — same
        // stream, only `State` moved; the router remains pure integer comparison, no new draw site.
        // GOLDEN RE-BASELINE #2 OF 5 (forward-ladder plan 2026-08-10-003, L2 — cohort formation):
        // NO-OP — see AtomicEquivalenceTests.cs's matching ledger entry for the full account. Every
        // hero on this trace stays LadderRank 0 the whole 30 days, so PartyFormation's new
        // group-by-rank step groups everyone into one cohort — a no-op over a single-valued grouping.
        // BOTH fields are still byte-identical to the L1 value above (verified via git-stash
        // before/after, not just re-asserted).
        // GOLDEN RE-BASELINE #3 OF 5 (forward-ladder plan 2026-08-10-003, L3 — gates + rung-1
        // recipes): **Class 2 — routing/combat decisions change, same stream different position.**
        // Verified by isolating each change (git-stash the Venues/ edits, re-run): the Tier 8-9
        // RecipeTable rows are draw-NEUTRAL alone (this pin is byte-identical to the L2 value with
        // only the recipes present) — no player action is ever submitted on this idle trace, so no
        // craft ever reaches them, exactly as characterized for the BaselinePlayer economy. The
        // MOVE comes entirely from the Mine/Sunken-Crypt floor-5 re-gate (100 -> 70) and floor-5
        // monster re-scale (HP 62 -> 50, Attack 35 -> 26; see VenueRegistry.BuildMine's own comment
        // for the measurement). On THIS idle trace (seed 9001, zero player actions EVER — no
        // crafting, no shopping) heroes still fight and still autonomously shop the RIVAL vendor's
        // shelf (HeroShoppingSystem/ShoppingAi need no player action), reaching up to
        // baseAttack+72 power fully rival-geared at level 6 (RivalCatalog's AE3 caps: weapon 20 +
        // shield 16 + armor 18) — comfortably above the new 70 gate, previously short of the old
        // 100. Confirmed directly (temporary hero-state dump, this PR, reverted before commit):
        // three of ten heroes on this trace reach LadderRank 2 (Mine graduation AND the Gloomwood
        // boss both clear) by day 30, all on rival gear alone. The Gloomwood boss re-gate (75 -> 73)
        // is exercised, not neutral, on this exact trace. Determinism holds (same seed+actions =
        // identical state). `Inc` is STILL byte-identical (13279888329118852579) — same stream,
        // only `State` moved; the gate/monster changes are pure data, no new draw site (grep
        // confirms rng.* stays in the same files).
        Assert.Equal(new RngState(12780702255728477106UL, 13279888329118852579UL), state.Rng);
    }
}
