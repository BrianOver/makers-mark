using GameSim;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Harness;

namespace GameSim.Tests.Balance;

/// <summary>
/// U-T1-11 ("the reference smith climbs the ladder"): the proof this unit owes. Register #157
/// (PR #549, owner ruling R14.3) makes <c>tier-2-smithing</c> require Forge Tier II — but
/// <see cref="BaselinePlayer"/> never once attempted an <see cref="UpgradeForgeAction"/> before
/// this unit, so the balance gate's own reference economy could never reach the rung the feature
/// gates on (measured pre-fix: main seed peak gold 399g against a 400g cost, 0 of 11 balance seeds
/// ever bought Forge Tier II). This is the tripwire that keeps that regression from coming back
/// silently the next time someone touches <see cref="BaselinePlayer"/>'s Evening ore-buying loop.
///
/// <para><b>Measured after the fix (100-day run, all eleven <see cref="ArcBalanceTests"/> seeds,
/// peak gold / whether Forge Tier ever exceeds index 0):</b> 2026 (main) 403g day 41; 1 581g day
/// 30; 7 454g day 32; 42 711g day 47; 1234 855g day 35; 31337 450g day 22; 777 412g day 34; 2468
/// 512g day 32; 13579 407g day 57 — 9 of 11 reach it. Seeds 99 (peak 368g) and 5678 (peak 393g)
/// never cross the 400g cost even with the fix — a real, reported finding (PR body), not a band
/// to widen and not a threshold to nudge (CLAUDE.md hard rule, <see cref="ArcBalanceTests"/>'s own
/// failure-message rule: "a seed that misses this is a finding to report, never a band to
/// widen"). Those two are deliberately NOT asserted below.</para>
/// </summary>
public class ForgeTierProgressionBalanceTests
{
    private const int Days = 100;

    [Theory]
    [Trait("Category", "Balance")]
    [InlineData(2026UL)] // main seed
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(1234UL)]
    [InlineData(31337UL)]
    [InlineData(777UL)]
    [InlineData(2468UL)]
    [InlineData(13579UL)]
    public void HundredDay_ReachesForgeTierTwo_OnTheseNineSeeds(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
        Assert.True(tierIndex >= 1,
            $"seed {seed}: forge tier index is {tierIndex} after {Days} days — BaselinePlayer never "
            + "bought Forge Tier II (index 1), the exact regression this test exists to catch");
    }
}
