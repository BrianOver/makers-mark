using GameSim;
using GameSim.Arc;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Balance;

/// <summary>
/// Forward-ladder plan L6 (2026-08-10-003, "the gates go green") balance gate: the 3-act arc
/// reaches its own Ending inside pinned, TWO-SIDED day windows on a 100-day <see cref="BaselinePlayer"/>
/// run. A one-sided ">=" band cannot see an arc that never finishes — exactly the blind spot draft
/// #413 existed to close, back when the Gloomwood routing trap (§11.8) meant floor 5 was never
/// reached on any seed and every one of these fields sat at 0 forever. Act II is unchanged (L5:
/// fires day 3-4 on every seed, zero re-baseline risk). Act III/Climax read <see cref="Hero.LadderRank"/>
/// against the ladder's own <see cref="ArcDirectorSystem.TerminalRank"/>/<see cref="ArcDirectorSystem.ClimaxRank"/>
/// (L5 re-anchor) instead of a floor number.
///
/// <para><b>L6 measurement (2026-08-10, post-L5, `characterize --seeds 2026,1,7,42,99,1234,5678,
/// 31337,777,2468,13579 --days 100`)</b> — <c>state.Arc</c> read directly, the same field these
/// tests assert on: main seed (2026) rung-0 clear (first floor-5, <see cref="BalanceSimTests"/>) day
/// 18, Act III day 18, Climax day 26, Ending day 31. Across all 11 seeds, Ending ranges 19-36 (slowest:
/// seed 7 at day 36) — every seed lands inside the windows below on the first measurement; no finding,
/// no widened band.</para>
/// </summary>
public class ArcBalanceTests
{
    private const int Days = 100;
    private const ulong MainSeed = 2026;

    // Mirrors BalanceSimTests' established floor-3 pacing band for Act II. Kept as a separate local
    // const per that file's own "change them consciously" rule — not shared, to avoid coupling two
    // test files over one number.
    private const int NoActIIBeforeDay = 1;  // floor 3 cannot happen on day 1

    // Forward-ladder plan L6's exact two-sided windows for the main seed (measured post-L5, see the
    // class doc above). Supersedes the old one-sided NoActIIIBeforeDay=8 sanity floor.
    private const int ActIIIByDayLower = 15;
    private const int ActIIIByDayUpper = 30;
    private const int ClimaxByDay = 40;

    // The plan's all-11-seeds gate: every campaign must reach its own Ending well inside the
    // 100-day run. A seed that misses this is a finding to report, never a band to widen.
    private const int EndingByDayAcrossSweep = 60;

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_ArcPaces_Sanely_OnMainSeed()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(MainSeed);

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        var arc = state.Arc;

        Assert.NotEqual(CampaignAct.ActI, arc.Act); // the arc actually moves within 100 days
        Assert.True(arc.ActIIStartDay > NoActIIBeforeDay,
            $"Act II fired on day {arc.ActIIStartDay} — implausibly early (floor {ArcDirectorSystem.ActIIFloorThreshold} same-day as day 1)");

        Assert.True(arc.ActIIIStartDay > 0,
            $"Act III never fired in {Days} days (arc stuck at {arc.Act} — the ladder's terminal rank {ArcDirectorSystem.TerminalRank} was never reached)");
        Assert.InRange(arc.ActIIIStartDay, ActIIIByDayLower, ActIIIByDayUpper);

        Assert.True(arc.ClimaxDay > 0,
            $"the Climax never fired in {Days} days (arc stuck at {arc.Act} — the terminal venue's own bottom floor never fell)");
        Assert.True(arc.ClimaxDay <= ClimaxByDay,
            $"Climax fired on day {arc.ClimaxDay} — after the plan's day-{ClimaxByDay} ceiling");

        Assert.True(arc.EndingDay > 0,
            $"the campaign never reached its Ending in {Days} days (arc stuck at {arc.Act})");
        // L5: Ending schedules off ClimaxDay (the terminal venue's own boss falling), not
        // ActIIIStartDay (the terminal venue merely opening) — the two now land days apart.
        Assert.Equal(arc.ClimaxDay + ArcDirectorSystem.EndingDelayDays, arc.EndingDay);
    }

    [Theory]
    [Trait("Category", "Balance")]
    [InlineData(2026UL)] // main seed
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(99UL)]
    [InlineData(1234UL)]
    [InlineData(5678UL)]
    [InlineData(31337UL)]
    [InlineData(777UL)]
    [InlineData(2468UL)]
    [InlineData(13579UL)]
    public void HundredDay_ArcReachesEnding_AcrossAllElevenSeeds(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        var arc = state.Arc;

        Assert.True(arc.EndingDay > 0,
            $"seed {seed}: the campaign never reached its Ending in {Days} days (arc stuck at {arc.Act}) "
            + "— a seed that misses this is a finding to report, never a band to widen");
        Assert.True(arc.EndingDay <= EndingByDayAcrossSweep,
            $"seed {seed}: Ending fired on day {arc.EndingDay} — after the plan's day-{EndingByDayAcrossSweep} ceiling");
    }
}
