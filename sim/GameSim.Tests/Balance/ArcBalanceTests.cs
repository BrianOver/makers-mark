using GameSim;
using GameSim.Arc;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Balance;

/// <summary>
/// Phase D (U-D3) balance gate: the 3-act arc paces sanely over a 100-day BaselinePlayer run on
/// the main balance seed — it actually moves (not stuck at Act I forever) and it doesn't move
/// instantly (Act II/III can't fire before the same trivialization floors <see cref="BalanceSimTests"/>
/// already pins for floor 3 / floor 5 — this unit's thresholds read those exact same signals).
/// </summary>
public class ArcBalanceTests
{
    private const int Days = 100;
    private const ulong MainSeed = 2026;

    // Mirrors BalanceSimTests' established pacing bands (same floor-3/floor-5 signals this unit's
    // Act II/Act III thresholds read). Kept as separate local consts per that file's own "change
    // them consciously" rule — not shared, to avoid coupling two test files over one number.
    private const int NoActIIBeforeDay = 1;  // floor 3 cannot happen on day 1
    private const int NoActIIIBeforeDay = 8; // mirrors BalanceSimTests.NoFloor5BeforeDay

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

        if (arc.ActIIIStartDay > 0)
        {
            Assert.True(arc.ActIIIStartDay >= NoActIIIBeforeDay,
                $"Act III/Climax fired on day {arc.ActIIIStartDay} — before the day-{NoActIIIBeforeDay} trivialization ceiling");
        }

        if (arc.EndingDay > 0)
        {
            Assert.Equal(arc.ActIIIStartDay + ArcDirectorSystem.EndingDelayDays, arc.EndingDay);
        }
    }
}
