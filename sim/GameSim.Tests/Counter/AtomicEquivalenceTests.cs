using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// THE PA3 PIN (plan 2026-07-21-002, PKD5): a Morning that never submits a counter action must
/// stay BYTE-IDENTICAL to the pre-Phase-A kernel — the atomic <c>HeroShoppingSystem</c> pass is
/// still the default day loop. This is the FIRST test PA3 lands (HIGH-RISK-land-tests-first):
/// the expected hash below was captured by running this exact 30-day/no-counter-action script
/// against the pre-PA3 <see cref="GameComposition.BuildKernel"/> (commit a7ae67d, before
/// <c>GameKernel.Advance</c>, <c>GameComposition</c>, or <c>HeroShoppingSystem</c> gained any
/// counter-awareness). Every later PA3/PA4 change must keep this hash green — it is the
/// structural guarantee that stepped-Morning is an ADDITIVE seam, not a rules rewrite (PKD9).
/// </summary>
public class AtomicEquivalenceTests
{
    private const string ExpectedPreCounterSha256 =
        "EC3DDFCCA9F14C55F892E9ECF525151801108D7BBB8EF1EB3E82ED018DEA40D9";

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
