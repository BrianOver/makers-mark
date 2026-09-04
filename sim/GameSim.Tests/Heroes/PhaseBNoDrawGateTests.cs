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
        // GOLDEN RE-BASELINE #4 OF 5 (forward-ladder plan 2026-08-10-003, L4 — Emberfall flips live
        // as rung 2): see AtomicEquivalenceTests.cs's matching ledger entry for the full account.
        // VenueRegistry.LiveRotation gaining "emberfall" plus firebrick..heartcoal joining
        // MaterialRegistry.PricedPool are both pure data (no RNG), but they change WHICH venue a
        // rank-2 party on this trace raids — pre-L4 the router fell back to Gloomwood forever (no
        // live rank-2 venue); post-L4 it routes to Emberfall outright. Confirmed directly (temporary
        // CLI probe, this PR, reverted before commit): two of ten heroes (Kettil, Nessa) reach
        // LadderRank 3 by day 30 — Emberfall's own bottom floor clears — so every combat draw from
        // that party's first Emberfall trip onward shifts. `Inc` is STILL byte-identical
        // (13279888329118852579) — same stream, only `State` moved; no new draw site.
        // GOLDEN RE-BASELINE #5 OF 5 (forward-ladder plan 2026-08-10-003, L5 — arc re-anchor +
        // graduation news): see AtomicEquivalenceTests.cs's matching ledger entry for the full
        // account. **Class 0b — values change, draw-free — and unlike every entry above, the pin
        // below does not move AT ALL, neither `Inc` nor `State`.** ArcDirectorSystem's rewritten Act
        // III/Climax/Ending triggers read Hero.LadderRank (already on every hero from L0/L1) and the
        // existing EventLog/Drama state, draw zero RNG (unchanged — the system's own doc comment has
        // always said so), and their own output, `state.Arc`, has exactly one reader anywhere in
        // sim/GameSim/ (confirmed by grep): ArcDirectorSystem itself. Nothing downstream of the arc
        // consumes it, so a rewritten trigger cannot move a single combat/shop/recruit draw. The
        // AtomicEquivalenceTests SHA256 DOES move on this same trace (Act reads ActIII instead of
        // Ended at day 30, plus the new ClimaxDay field populated) — that movement is pure
        // serialized VALUE change on a stream this exact assertion proves is untouched.
        // GOLDEN RE-BASELINE #6 (§11.13 amendment, U4 — the apprenticeship warrant): **Class 2 —
        // combat decisions change, same stream different position, deliberately.** See
        // AtomicEquivalenceTests.cs's matching ledger entry for the full account — a hero on this
        // idle trace (seed 9001, zero player actions, so the ConcludeApprenticeshipAction opt-out
        // is never submitted) who would have died within Day <= ApprenticeWarrant.LastGraceDay (3)
        // now survives at 1 HP instead, and every draw downstream of that save point shifts.
        // `Inc` is STILL byte-identical (13279888329118852579) — same stream, only `State` moved;
        // the clamp draws no RNG of its own (ApprenticeWarrant.TryClamp is pure integer math).
        // GOLDEN RE-BASELINE #7 (§11.14.8, T6 — "every decision leaves a reason"): see
        // AtomicEquivalenceTests.cs's matching ledger entry for the full account.
        // ExpeditionRevealSystem now emits a persisted DecisionExplained per ExpeditionResult; its
        // own Reveal method never touches the `rng` parameter Process receives, so it draws zero
        // RNG. **Neither `Inc` nor `State` moves for this entry** — the pin below stays byte-for-byte
        // the U4 value, the same both-unchanged signature the L5 arc re-anchor entry recorded. The
        // AtomicEquivalenceTests SHA256 DOES move on this same trace (a new event per revealed
        // expedition, plus the downstream flavour-pack variant reroll that a shifted EventId causes)
        // — that movement is pure serialized-content change on a stream this exact assertion proves
        // is untouched.
        // GOLDEN RE-BASELINE #8 (T10 U48, CommissionSystem dead-hero fix): see
        // AtomicEquivalenceTests.cs's matching ledger entry for the full account. ExpireCommissions
        // now drops a dead hero's commission immediately instead of at its deadline; CommissionSystem
        // draws no RNG before or after. **Neither `Inc` nor `State` moves for this entry** — same
        // both-unchanged signature as L5/U4/T6. The AtomicEquivalenceTests SHA256 DOES move (which
        // hero gets the freed commission board slot, and when, differs) — pure serialized-content
        // change on a stream this exact assertion proves is untouched.
        // 2026-09-03 (BatchEchoFloor 550 -> 800, owner ruling dated 2026-09-03): **NO-OP — neither
        // `Inc` nor `State` moves.** See AtomicEquivalenceTests.cs's matching ledger entry for the
        // full account: this idle trace's driver, BaselinePlayer, never submits a hand-forge
        // (ForgeTraceInput), so GameState.Player.BatchEcho stays null all 30 days and the raised
        // constant is never read on this trace at all — not "reads it and it's a no-op," never
        // reached. Confirmed directly: this test itself is one of the 1785/1785 green fast-lane
        // tests both before and after the change, same pinned value both times.
        // 2026-09-03 (ForgeScorer forgiveness: subtractive -> proportional, owner ruling §11.7.11):
        // **NO-OP — neither `Inc` nor `State` moves.** See AtomicEquivalenceTests.cs's matching
        // ledger entry for the full account. Two reasons, either sufficient: this idle trace's
        // driver, BaselinePlayer, never submits a hand-forge (ForgeTraceInput), so ForgeScorer is
        // never reached on this trace; and the new proportional rule is an exact arithmetic
        // identity with the old subtractive one at zero unlocked assist talents, so even a trace
        // that did hand-forge would be unmoved until a talent lands. ForgeScorer is pure and draws
        // no RNG before or after either way — a scoring-rule change cannot add, remove, or reorder
        // a draw. Confirmed directly: this test is one of the 1859/1859 green fast-lane tests with
        // the change in, same pinned value.
        Assert.Equal(new RngState(4182585629336870939UL, 13279888329118852579UL), state.Rng);
    }
}
