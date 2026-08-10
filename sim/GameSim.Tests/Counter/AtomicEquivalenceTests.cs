using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// THE PA3 PIN (plan 2026-07-21-002, PKD5): a Morning that never submits a counter action must
/// stay BYTE-IDENTICAL to the ATOMIC path of whatever kernel composition is current — the
/// atomic <c>HeroShoppingSystem</c> pass is still the default day loop. This is the FIRST test
/// PA3 lands (HIGH-RISK-land-tests-first): the expected hash below was originally captured
/// against the pre-PA3 <see cref="GameComposition.BuildKernel"/> (commit a7ae67d, before
/// <c>GameKernel.Advance</c>, <c>GameComposition</c>, or <c>HeroShoppingSystem</c> gained any
/// counter-awareness).
///
/// RE-BASELINED (Game-Feel Plan G3, 2026-07-21): this 30-day/zero-action script is now
/// necessarily idle every day by construction (ActionSlotsRemaining never drops, since no
/// slot-consuming action is ever submitted), so the NEW always-on <c>RentSystem</c> (rent comes
/// due at day 10/20/30) and <c>MarketShareSystem</c> (idling every day rides the rival's edge to
/// its 1000‰ cap, discounting <c>RivalRestockSystem</c>'s newly-minted stock) legitimately move
/// the serialized state — this is G3 working as designed, not a counter-additivity regression.
///
/// U9 "quality gets teeth" (2026-07-24) briefly re-baselined this hash: with
/// <c>VeteranMinQualityGrade</c> = Fine, veterans refused the flat-Common rival shelf outright,
/// reshaping who bought what across the idle run. The gate-b retune (2026-07-24) lowered that gate
/// to <see cref="QualityGrade.Common"/> (veterans refuse only Poor junk, per the plan's stated
/// intent), so veterans accept Common rival gear again and this idle trace returns BYTE-FOR-BYTE to
/// its pre-U9 value — the hash below is the original G3 baseline, restored. The PA3 invariant this
/// test protects (atomic == the current kernel's non-counter path) is unchanged — this run never
/// opens the counter, so <see cref="WillingnessModel"/>'s U9 quality bonus (haggle-only) never fires
/// here, and the continuous quality-demand effect is exercised by the counter/haggle tests instead.
/// </summary>
public class AtomicEquivalenceTests
{
    // RE-BASELINED AGAIN (Wave 3 implementation, 2026-07-24): CommissionSystem now posts
    // CommissionPosted (and silent-expiry) events for gappy heroes on this idle BaselinePlayer trace,
    // and only appends to GameState.Commissions / nudges nothing on the un-accepted path — a legitimate
    // demand-side surfacing, not an RNG/order change (CommissionSystem draws no RNG, pure projection
    // over MusterPlan). Party formation / target floors / expedition results are unchanged (the PKD7
    // pin in HaggleEconomicsTests still holds). Deliberate re-baseline, same class as the Wave 3
    // contracts field addition above.
    // RE-BASELINED (Wave 4a named-artifacts contract, 2026-07-24): adding the trailing
    // `Item.SignedName` init member (default null) means every item in the save JSON now carries
    // "SignedName":null — a pure serialized-SHAPE change, no behavior change (nothing signs items
    // yet; RNG stream + every value identical). Same class as the Commissions field addition.
    // RE-BASELINED (Wave 4c farewell + heirloom contracts, 2026-07-24): two trailing serialized fields
    // land together — `Memorial.Honored` (default false; every memorial on this idle trace, minted
    // when a hero dies, now serializes "Honored":false) and `Item.HeirloomLineage` (default null; every
    // item now carries "HeirloomLineage":null). Pure serialized-SHAPE change: nothing honors a memorial
    // or reforges an heirloom on the BaselinePlayer trace (no HonorMemorialAction / ReforgeHeirloomAction
    // is ever submitted), so the RNG stream and every value are identical — same class as the SignedName
    // and Commissions field additions above.
    // RE-BASELINED (Wave 5 U23e batch echo, 2026-07-24): the trailing `PlayerState.BatchEcho`
    // (default null) means the player object now serializes "BatchEcho":null. Pure serialized-SHAPE
    // change — batch echo only ever fires after a hand-forge (a ForgeTraceInput craft), which
    // BaselinePlayer never submits, so the memory stays null the whole idle run and the RNG stream +
    // every value are identical. (Note: registering the ForgeTraceInput puzzle type + wiring
    // ForgeScorer into crafting, Wave 5 U23a/U23c, did NOT shift this hash — a null Puzzle serializes
    // identically and no forge trace is ever submitted here; only this new PlayerState field moved it.)
    // RE-BASELINED (Phase B B0 contracts, 2026-07-25): the trailing `Hero.Xp` init member (default 0)
    // means every hero in the save JSON now carries "Xp":0 — a pure serialized-SHAPE change (Class 0a).
    // Nothing reads or accrues Xp yet (B0 only declares the field + the HeroDecisionExplained/HeroRankUp
    // event types, none of which is stamped on the BaselinePlayer idle trace), so the RNG stream and
    // every value are identical — same class as the SignedName / Memorial.Honored field additions above.
    // RE-BASELINED (Phase B B1 legibility spine, 2026-07-25): Class 0b (values change, draw-free &
    // decision-free). On this idle trace the sim now stamps HeroDecisionExplained + HeroRankUp events,
    // accrues Hero.Xp at the Evening reveal, and re-ranks the per-speaker gossip cap by salience
    // (which gossip *prose* is emitted changes). The idle hash MOVES because these VALUES differ — but
    // no decision changed (Balance gate 25/25 unchanged) and no RNG draw was added or reordered (the
    // PhaseBNoDrawGateTests RngState pin is byte-identical). Deliberate re-baseline, CommissionPosted
    // class — NOT the "shape-only, values identical" class of the B0/SignedName notes above.
    // RE-BASELINED (Phase B B2 hero traits, 2026-07-25): **Class 1** — the first Phase-B unit that
    // changes DECISIONS. 10 StableHash-derived traits give heroes shop teeth (price sensitivity,
    // quality demand, sentiment, haggle patience, consumable stocking), so on this idle trace who
    // buys/refuses what shifts → purchases → gear → combat length → the serialized state moves for
    // REAL (this is the U9-veteran-pickiness playbook, not a shape-only re-pin). Determinism holds
    // (same seed+actions = identical state) and NO new RNG draw SITE was added — traits are DERIVED,
    // grep confirms rng.* stays in the 3 kernel files, and the Balance gate is 25/25 UNCHANGED (the
    // bands absorbed it). Deliberate Class-1 re-baseline.
    // RE-BASELINED (Phase B B3 gossip salience v2, 2026-07-25): Class 0b (values change, draw-free &
    // decision-free). GossipGenerator now ranks the per-speaker 3-line cap by relationship affinity
    // (derived hero↔hero edges) on top of involvement/recency, so WHICH gossip prose is emitted on the
    // idle trace shifts. No decision changed (Balance 25/25) and no RNG draw added (grep + the
    // PhaseBNoDrawGate Inc-unchanged tell hold). Deliberate re-baseline, CommissionPosted class.
    // RE-BASELINED (Phase B B4 needs-lite + Phase C U-C6 level-flip, 2026-07-25): two decision-changing
    // units land together. B4: an unmet-demand streak drives a hero to boycott the shop (Class 1,
    // shopping decisions shift). U-C6 (the deferred KTD-B2 flip): Hero.Xp now grants a real Level at the
    // Evening reveal → CombatMath scales → heroes grow stronger and reach deeper floors on this trace
    // (Class 2, the intended combat re-baseline). The idle hash moves for REAL; determinism holds and
    // NO new draw SITE was added (grep clean; PhaseBNoDrawGate Inc unchanged, only State moved = same
    // stream, different draw count from decision/combat changes). Balance re-fit checked deliberately.
    // RE-BASELINED (Phase C U-C1 craft-modifier layer slice 1, 2026-07-25): **Class 0a — shape only,
    // values identical.** Three trailing nullable modifier slots (Item.QuenchOil/Rune/Fitting, all
    // null) plus CombatEvent.ModifierHpDelta (default 0) now serialize on every item and every combat
    // event in the save. The idle BaselinePlayer crafts no modifiers, so every slot stays null and
    // every effect (flee-oil threshold shift, Leech heal, Lodestone ore) is a no-op — the RNG stream
    // is byte-identical (the PhaseBNoDrawGate RngState pin is UNCHANGED, the grep gate holds, no new
    // draw site) and every combat value is identical. Only the serialized shape moved. Same class as
    // the SignedName / Memorial.Honored field additions. The Class-2 combat re-baseline arrives with
    // slice 2, when forge composition actually stamps modifiers onto crafted gear.
    // RE-BASELINED (Phase C U-C3 drama director): new single daily draw on the EXISTING kernel stream
    // (Inc unchanged, State advanced) — Class-2. DirectorSystem polls once every Morning and draws ONE
    // value from the shared stream to pick an incident, so every downstream draw that day shifts (combat,
    // recruit, quality) → the idle serialized state moves for REAL. The director/den state also serializes
    // now (GameState.Director, GameState.Venues entries, IncidentFired/DenThreatShifted events on the log).
    // Determinism holds (same seed+actions = identical state) and NO new stream was created — the sibling
    // PhaseBNoDrawGate pin confirms RngState.Inc is UNCHANGED, only State moved (one extra draw/day on the
    // same stream). The grep gate holds (rng. now confined to the 4 sites incl. Drama/DirectorSystem.cs).
    // RE-BASELINED (2026-08-01 venue-router power bands + T1 flip, relands #242): **Class 1/2 —
    // decisions AND draw counts change, one re-baseline for the whole branch.** Three changes land
    // together, all of which move this idle trace for REAL: (1) VenueRouter now routes bounty-free
    // parties by per-venue EntryPower bands instead of tightest-fit headroom, so WHICH venue every
    // party raids — and therefore every combat draw after the first Morning — shifts; (2) the T1
    // flip makes Sunken Crypt + Emberfall live and opens sentinel/skirmisher/occultist for
    // recruitment (the RecruitPool draw's modulus changes from 3 to 6, re-rolling every recruit);
    // (3) BaselinePlayer submits nothing on this zero-action trace, so its ActionLegality fix moves
    // nothing HERE — it re-baselines the policy-driven suites instead. Determinism holds (same
    // seed+actions = identical state; the balance gate's byte-identical-replay test still passes)
    // and NO new RNG draw SITE was added — the sibling PhaseBNoDrawGate pin confirms RngState.Inc
    // is UNCHANGED, only State moved (different draw count on the same stream, exactly the U-C3
    // shape above).
    // RE-BASELINED AGAIN, same PR (2026-08-01 pre-merge review: Emberfall pulled back to DORMANT
    // — built + banded but zero committed art, and it had measured 44% of all routing, i.e. half
    // the game's raids pointed at placeholder glyphs): **Class 0b — values change, draw-free.**
    // LiveRotation drops emberfall (no idle-trace party ever reached its 72 band in 30 days, so
    // every routing decision — and therefore the ENTIRE RNG stream — is byte-identical: the
    // sibling PhaseBNoDrawGate RngState pin did NOT move this time, State and Inc both equal).
    // What moves the hash is the priced-pool contraction (19 → 14 keys, Emberfall's ladder waits
    // for its art-gated go-live): the pool-derived, draw-free vendor/pricing surfaces serialize
    // differently. One PR, two recorded re-pins, still ONE deliberate re-baseline window.
    // RE-BASELINED AGAIN, same PR window (2026-08-01 Gloomwood band 55 → 72, the coordinator's
    // three-venue spread pass): **Class 2 — routing decisions change.** Idle-trace parties that
    // used to cross into the Gloomwood band at 55 now stay in the Mine/Crypt early band until 72,
    // so WHICH venue mid-power parties raid — and every combat draw after they first cross 55 —
    // shifts. Same stream, different position (PhaseBNoDrawGate: Inc byte-identical, State moved);
    // no new draw site (the router is still pure integer comparison). Third recorded re-pin, one
    // deliberate re-baseline window for the whole branch.
    // RE-BASELINED 2026-08-10 (forward-ladder plan 2026-08-10-003, L0 — Contracts micro-PR):
    // **Class 0b — values change, draw-free.** Hero gained the non-positional init member
    // LadderRank (default 0, the MoodPermille/Xp/Pack pattern), so every serialized hero carries
    // one new "LadderRank":0 property and the state JSON's bytes move. NOTHING ELSE moved:
    // nothing writes the field until L1, no draw site was added or reached differently, and the
    // sibling PhaseBNoDrawGate RngState pin is byte-identical (State AND Inc — verified before
    // this re-pin, the same both-equal signature as the 2026-08-01 Emberfall-dormant re-pin
    // above). Golden re-baseline #0 of the ladder wave's five — the save-shape one the plan's L0
    // names "save-fixture only."
    // GOLDEN RE-BASELINE #1 OF 5 (forward-ladder plan 2026-08-10-003, L1 — graduation + rank
    // router, §11.8's fix): **Class 2 — routing decisions change, same stream different position.**
    // VenueRouter.ChooseVenue now keys on Hero.LadderRank (MIN of party members, interim) instead
    // of CombatMath.PartyAveragePower against a per-venue EntryPower band — EntryPower is deleted.
    // Every hero starts, and on this idle trace STAYS, at LadderRank 0 (heroes still fight — a
    // zero-ACTION trace means the smith crafts/shops nothing, not that expeditions stop — so XP and
    // Level accrue daily even with empty gear; but nobody ever clears a bottom floor to graduate),
    // so every bounty-free party's rank is 0 the whole 30 days. The OLD EntryPower router read a
    // DIFFERENT signal for the same fights: raw party power off the same accruing Level, and this
    // exact idle trace is on record crossing the old Gloomwood band mid-run — two prior re-pins in
    // this file's own ledger (2026-08-01, "venue-router power bands" and "Gloomwood band 55 → 72")
    // exist BECAUSE this trace's power passes through the band region and permanently rerouted a
    // party there under the old sticky-highest-band rule. Under the ladder, that same party has no
    // path to Gloomwood at all (rank 0, never graduates) — it stays in the Mine/Sunken-Crypt rank-0
    // pair the entire 30 days instead, so which of the two peers it lands on (and every downstream
    // combat draw after the divergence point) shifts. The tell this is legitimate and not a new
    // stream: the sibling PhaseBNoDrawGateTests RngState pin shows `Inc` UNCHANGED
    // (13279888329118852579) and only `State` moved — same stream, different position, exactly the
    // 2026-08-01 EntryPower-placement re-pins' own signature, not a new draw site (the router is
    // still pure integer comparison; MIN over LadderRank adds no RNG). Determinism holds (same
    // seed+actions = identical state).
    // GOLDEN RE-BASELINE #2 OF 5 (forward-ladder plan 2026-08-10-003, L2 — cohort formation): **NO-OP
    // — hash UNCHANGED, value not re-pinned.** PartyFormation now groups alive heroes by LadderRank
    // before applying the existing anchor/id rules, so a party can no longer mix ranks. On THIS idle
    // trace every hero is, and stays, LadderRank 0 the entire 30 days (L1's own measurement
    // correction, recorded above: party power plateaus at 63-73, below the floor-5 gate of 100, so
    // nobody ever graduates) — one rank means one cohort, and grouping a single-cohort roster is a
    // no-op (GroupBy over one key, then OrderBy over that one group, reproduces the exact input order
    // FormParties already produced pre-L2). Cohort formation can only diverge from the pre-L2
    // behavior once a mixed-rank roster exists, which requires a graduation, which this trace cannot
    // produce until L3 fixes the gate-vs-power gap. Class 0 — no behavioral change on the golden
    // trace; verified by running the fast lane both with and without this unit's diff (git stash) and
    // confirming this exact SHA in both runs, plus the sibling PhaseBNoDrawGateTests RngState pin
    // (Inc AND State) unchanged in both runs too.
    // GOLDEN RE-BASELINE #3 OF 5 (forward-ladder plan 2026-08-10-003, L3 — gates + rung-1 recipes):
    // **Class 2 — routing/combat decisions change, same stream different position.** The Tier 8-9
    // RecipeTable rows are draw-neutral alone on this trace (isolated via git-stash, verified
    // byte-identical to the L2 value with only the recipes present — no player action is ever
    // submitted here, so nothing ever reaches them). The move is entirely the Mine/Sunken-Crypt
    // floor-5 re-gate (100 -> 70) plus its monster re-scale (HP 62 -> 50, Attack 35 -> 26) — see
    // VenueRegistry.BuildMine's own comment for the measurement. This idle trace's heroes shop the
    // RIVAL vendor autonomously even with zero player actions (HeroShoppingSystem needs none), and
    // a fully rival-geared level-6 hero reaches power up to baseAttack+72 (RivalCatalog's AE3
    // caps) — above the new 70 gate, short of the old 100. Confirmed directly (temporary hero-state
    // dump, this PR, reverted before commit): 3 of 10 heroes on this trace reach LadderRank 2 (both
    // the Mine graduation and the Gloomwood boss, re-gated 75 -> 73, clear) by day 30. See
    // PhaseBNoDrawGateTests.cs's matching ledger entry for the full account — `Inc` is STILL
    // byte-identical there (same stream, only `State` moved; no new draw site).
    // GOLDEN RE-BASELINE #4 OF 5 (forward-ladder plan 2026-08-10-003, L4 — Emberfall flips live as
    // rung 2): **Class 2 — routing/combat decisions change, same stream different position.** The
    // Tier 12-14 RecipeTable rows are draw-neutral alone on this trace by the same construction as
    // L3's rung-1 rows: no player action is ever submitted on this idle trace, so no craft ever
    // reaches them, and pre-flip their MaterialKey (firebrick/slagiron/emberglass) isn't even in
    // RecipeTable.MaterialGrades (it derives from MaterialRegistry.PricedPool, which L4 is the one
    // to expand) — ActionLegality.CraftLegal rejects them exactly as it did every rung-1 row before
    // L3's own PricedPool change landed. The move is entirely (1) VenueRegistry.LiveRotation gaining
    // "emberfall" and (2) firebrick..heartcoal joining MaterialRegistry.PricedPool in the same
    // re-baseline: this idle trace's heroes still autonomously shop the RIVAL vendor
    // (HeroShoppingSystem needs no player action) and still fight every phase even with zero player
    // actions. Confirmed directly (temporary CLI probe, this PR, reverted before commit): on THIS
    // trace (seed 9001, 30 days) two of ten heroes (Kettil, Nessa) reach LadderRank 3 — Emberfall's
    // OWN bottom floor clears, not merely a routing change — while the router pre-L4 would have kept
    // sending that same rank-2 party back to Gloomwood forever (no live rank-2 venue existed). Every
    // combat draw from that party's first Emberfall trip onward shifts. See
    // PhaseBNoDrawGateTests.cs's matching ledger entry for the full account — `Inc` is STILL
    // byte-identical there (same stream, only `State` moved; no new draw site — VenueRegistry and
    // MaterialRegistry are both pure data, no RNG).
    private const string ExpectedPreCounterSha256 =
        "B50A1DAC3F414ECAB951B1A1F7FD0D6838D32C4D8FCFCBAE7CE7C232C4F81454";

    [Fact]
    public void ThirtyDayRun_NoCounterActions_IsByteIdenticalToPrePa3Kernel()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 9001);

        for (var i = 0; i < 30 * 5; i++) // 5-phase day (staged resolution); NEVER submits OpenCounterAction
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        var json = SaveCodec.Serialize(state);
        var actualHash = Sha256Hex(json);

        Assert.Equal(ExpectedPreCounterSha256, actualHash);
    }

    private static string Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
