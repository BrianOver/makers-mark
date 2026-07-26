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
    private const string ExpectedPreCounterSha256 =
        "46059EE1A1DED4488D9653C2FAFB98FBE90AE203279C981317771CE340F09198";

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
