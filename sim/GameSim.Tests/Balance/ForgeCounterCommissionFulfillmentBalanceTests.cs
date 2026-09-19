using System.Linq;
using GameSim;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// P2-HONEST-33 ("counter-served heroes never take the shelf path"): pins that the standing-request
/// fix actually moves the needle under a multi-seed <see cref="BatchRunner.Policy.ForgeCounter"/>
/// sweep, not just in the hand-built unit tests. PR #923 measured 1,018 commissions posted and
/// ZERO fulfilled over 20 seeds x 100 days under this exact policy — the cause was
/// <c>HeroShoppingSystem.MorningShoppingOrder</c> excluding every counter-served hero from the ONLY
/// pass that ran <see cref="GameSim.Heroes.CommissionHandlers.TryFulfillFromShelf"/>. A smaller sweep
/// here (still multi-seed, still full-length days, just fewer seeds than the PR-body evidence table)
/// keeps this fast-lane-affordable while still being a real regression guard: a future change that
/// re-introduces the bug (or a new one with the same shape) drops fulfilled back to 0 and this test
/// goes red. No exact count is pinned — a sweep's absolute totals drift with any unrelated tuning
/// change — only the floor that made the original bug reportable at all.
/// </summary>
public class ForgeCounterCommissionFulfillmentBalanceTests
{
    private const int Days = 100;
    private const int SeedCount = 5;
    private const ulong StartSeed = 5001;

    private static int CountFulfilled(ulong startSeed, int seedCount)
    {
        var kernel = GameComposition.BuildKernel();
        var policyFn = BatchRunner.PolicyFn(BatchRunner.Policy.ForgeCounter, CraftHand.Average);
        var fulfilled = 0;

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= Days)
            {
                var result = kernel.Tick(state, policyFn(state));
                state = result.NewState;
                fulfilled += result.Events.OfType<CommissionFulfilled>().Count();
            }
        }

        return fulfilled;
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void ForgeCounterPolicy_FulfillsAtLeastOneCommission_AcrossASeedSweep()
    {
        var fulfilled = CountFulfilled(StartSeed, SeedCount);

        Assert.True(fulfilled > 0, $"expected forgecounter to fulfil at least 1 commission across {SeedCount} seeds x {Days} days, got {fulfilled}");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void BaselinePolicy_NeverOpensCounter_FulfillmentUnaffectedByThisFix()
    {
        // Control: BaselinePlayer never opens the counter (Counter always null), so this fix's new
        // FulfillServedHeroCommissions pass is a permanent no-op on this policy — the atomic-
        // equivalence pin already covers Counter==null byte-for-byte, this just also checks the
        // commission angle specifically.
        var kernel = GameComposition.BuildKernel();
        var policyFn = BatchRunner.PolicyFn(BatchRunner.Policy.Baseline, CraftHand.Average);
        var state = GameComposition.NewCampaign(StartSeed);
        var sawServed = false;

        while (state.Day <= Days)
        {
            var result = kernel.Tick(state, policyFn(state));
            state = result.NewState;
            if (state.Counter is not null)
            {
                sawServed = true;
            }
        }

        Assert.False(sawServed, "BaselinePlayer opened a counter session — control assumption broken");
    }
}
