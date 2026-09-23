using System.Linq;
using GameSim;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// P2-HONEST-51 ("buy the ore or buy the goodwill" is one of the six decisions the game is made
/// of, and under the policy that exercises every other one it is never made): a 20-seed x 100-day
/// sweep found <see cref="BaselinePlayer"/> accepting 908 ore offers against
/// <see cref="ForgeCounterPlayer"/>'s 46 -- a ~19.7x gap, and a cliff rather than a slope
/// (ForgeCounter buys nothing at all until day 15). That measurement did not diagnose a cause:
/// <c>TryPaySlot</c> has exactly one caller so it cannot account for a gap this size, and gold is
/// not visible in the chronicle. This test does not diagnose it either -- it pins the gap as a
/// census, the same role <see cref="Hygiene.BalanceCorpusCoverageCensusTests"/> plays for corpus
/// coverage, so the next change to either policy's ore-buying arm moves a reviewed number instead
/// of a silent one.
///
/// <para><b>Event choice.</b> <see cref="TariffApplied"/> fires from <c>OreMarketHandlers</c> on
/// every accepted <see cref="BuyOreAction"/> whose faction-standing tariff actually moved the price
/// (nonzero delta) -- see that handler's doc comment. It is the accepted-buy proxy the finding
/// itself used, is already-recorded (no new instrumentation), and is cheap to count from a plain
/// tick loop.</para>
///
/// <para><b>Sweep size, and why the ratio here is not the headline ratio.</b> 20 seeds x 100 days
/// for two policies is far too slow for routine runs; this follows
/// <see cref="ForgeCounterCommissionFulfillmentBalanceTests"/>'s precedent of a smaller multi-seed
/// sweep at full day-length (5 seeds, same as that file). Three independent 5-seed blocks were run
/// while setting this test's band (seeds 1-5, 501-505, 7001-7005) and none reproduced anything near
/// 19.7x -- they measured 3.1x, 2.4x and 1.5x. A 20-seed aggregate is not five 5-seed samples
/// averaged, so this test does not chase that headline number; what all three blocks agreed on is
/// the direction and the order of magnitude (BaselinePlayer meaningfully ahead, every block, by at
/// least 1.5x), and that agreement is what the pinned floor below is built from. Absolute counts at
/// 5 seeds are not pinned as literals for the same reason -- only the ratio, with a floor comment
/// carrying the three-block measurement it came from.</para>
/// </summary>
public class OreBudgetGapCensusTests
{
    private const int Days = 100;
    private const int SeedCount = 5;

    // Seed 1 -- the same default the CLI's `batch` sub-command uses when no --start-seed is given
    // (BatchRunner.Parse: startSeed = 1UL), so this sweep is the same corpus a session would get by
    // just running the documented batch command with --seeds 5.
    private const ulong StartSeed = 1;

    private static int CountAcceptedOreBuys(BatchRunner.Policy policy, ulong startSeed, int seedCount)
    {
        var kernel = GameComposition.BuildKernel();
        var policyFn = BatchRunner.PolicyFn(policy, CraftHand.Average);
        var accepted = 0;

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= Days)
            {
                var result = kernel.Tick(state, policyFn(state));
                state = result.NewState;
                accepted += result.Events.OfType<TariffApplied>().Count();
            }
        }

        return accepted;
    }

    // Memoized so the two [Fact]s below (sanity + ratio) each read the same sweep result instead of
    // recomputing a 5-seed x 100-day sweep twice per policy -- same rationale as
    // FactionTariffBalanceTests.MemoizedFavoredRun.
    private static readonly Lazy<int> MemoizedBaseline =
        new(() => CountAcceptedOreBuys(BatchRunner.Policy.Baseline, StartSeed, SeedCount),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<int> MemoizedForgeCounter =
        new(() => CountAcceptedOreBuys(BatchRunner.Policy.ForgeCounter, StartSeed, SeedCount),
            LazyThreadSafetyMode.ExecutionAndPublication);

    // Measured 2026-09-23 on this exact commit, at this file's own sweep (StartSeed=1, SeedCount=5,
    // Days=100): BaselinePlayer 734 accepted ore buys, ForgeCounterPlayer 233 -- ratio 3.1x. Two
    // OTHER 5-seed blocks sampled while setting this band: StartSeed=501 -> 754/313 = 2.4x;
    // StartSeed=7001 -> 702/460 = 1.5x. None of the three comes close to the 20-seed finding's
    // ~19.7x (908/46) -- a 20-seed aggregate is not five 5-seed samples averaged, and this test does
    // not claim otherwise. What the three DO agree on: BaselinePlayer buys meaningfully more ore
    // than ForgeCounterPlayer, every block, by at least 1.5x. The floor is set under that lowest
    // observed ratio (1.5x) with a ~15% margin, so an unrelated future change that nudges the shared
    // RNG stream (shifting which ore offers appear, without touching either policy's buying logic)
    // does not false-fail this census, while a real narrowing of the gap -- ForgeCounterPlayer
    // catching up, or BaselinePlayer's buying arm regressing -- still trips it.
    private const double MinBaselineToForgeCounterRatio = 1.3;

    [Fact]
    [Trait("Category", "Balance")]
    public void BothPolicies_ActuallyBuyOreUnderThisSweep_SoTheGapIsMeasuringSomethingReal()
    {
        var baseline = MemoizedBaseline.Value;
        var forgeCounter = MemoizedForgeCounter.Value;

        Assert.True(baseline > 0,
            $"BaselinePlayer accepted zero ore buys across {SeedCount} seeds x {Days} days -- sweep is not exercising the ore market at all.");
        Assert.True(forgeCounter > 0,
            $"ForgeCounterPlayer accepted zero ore buys across {SeedCount} seeds x {Days} days -- sweep is not exercising the ore market at all.");
    }

    /// <summary>
    /// The pinned census: BaselinePlayer's ore-buy count vs ForgeCounterPlayer's, as a ratio floor
    /// rather than either raw literal (CLAUDE.md rule 8's "pin the relationship, not the number").
    /// A future change that closes this gap (ForgeCounter starts buying ore near BaselinePlayer's
    /// rate) or widens it further from an unrelated regression (BaselinePlayer stops buying) is
    /// exactly the "reviewed diff" this unit exists to force -- when this goes red, read the two
    /// counts in the message, not just the ratio, to see which side moved.
    /// </summary>
    [Fact]
    [Trait("Category", "Balance")]
    public void BaselinePlayer_BuysOreMeaningfullyMoreOftenThanForgeCounter()
    {
        var baseline = MemoizedBaseline.Value;
        var forgeCounter = MemoizedForgeCounter.Value;

        Assert.True(forgeCounter > 0,
            "ForgeCounterPlayer accepted zero ore buys -- ratio is undefined/infinite; "
            + $"BaselinePlayer accepted {baseline}. See BothPolicies_ActuallyBuyOreUnderThisSweep... for the zero-guard.");

        var ratio = (double)baseline / forgeCounter;

        Assert.True(ratio >= MinBaselineToForgeCounterRatio,
            $"ore-buy gap narrowed below the pinned floor: BaselinePlayer={baseline}, "
            + $"ForgeCounterPlayer={forgeCounter} (ratio {ratio:F1}x, floor {MinBaselineToForgeCounterRatio}x). "
            + "The original finding measured a ~19.7x gap (908/46, 20 seeds x 100 days); this "
            + $"census's own sweep ({SeedCount} seeds x {Days} days, StartSeed={StartSeed}) measured "
            + "3.1x when this floor was set -- if this test just went red, one policy's ore-buying "
            + "behavior changed shape; check which count moved before touching this band.");
    }
}
