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
    private const string ExpectedPreCounterSha256 =
        "7164E452CE6113BA541C03A44618402C44E8CA15A7AD429DC4C4BA896623E9DF";

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
