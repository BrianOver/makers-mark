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
/// RE-BASELINED AGAIN (U9 "quality gets teeth", 2026-07-24, exactly the future case this
/// docstring warned about): the player crafts nothing on this script (zero actions, ever), so
/// every hero purchase on this run comes off the RIVAL shelf, which is always
/// <see cref="QualityGrade.Common"/> (flat quality, <c>RivalCatalog</c>) — a legitimate demand-side
/// change. Once <c>ShoppingAi.VeteranFloorThreshold</c> (floor 3) is crossed, a veteran now
/// refuses Common-grade rival gear outright (<c>ShoppingAi.VeteranMinQualityGrade</c> = Fine) —
/// exactly the intended "picky veteran" effect, and it legitimately reshapes who buys what for the
/// rest of this 30-day script, hence a new hash. The PA3 invariant this test protects (atomic ==
/// the current kernel's non-counter path) is otherwise unchanged — this run still never opens the
/// counter, so <see cref="WillingnessModel"/>'s U9 quality bonus (haggle-only) never fires here.
/// </summary>
public class AtomicEquivalenceTests
{
    private const string ExpectedPreCounterSha256 =
        "AF9CDC9D2F7D96BAB6D25E6C78D56F1BD1EC8CD50180C694F15938CCDD967701";

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
