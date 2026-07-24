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
    private const string ExpectedPreCounterSha256 =
        "8EB0FCB1328334BEE182405DD51461CDC7675D3BAA75B879F01FCA710C47B25B";

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
