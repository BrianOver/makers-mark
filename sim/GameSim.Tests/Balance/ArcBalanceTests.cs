using GameSim;
using GameSim.Arc;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Balance;

/// <summary>
/// Phase D (U-D3) balance gate: the 3-act arc paces sanely over a 100-day BaselinePlayer run on
/// the main balance seed — it actually moves (not stuck at Act I forever) and it doesn't move
/// instantly. Act II still reads the same floor-3 trivialization floor <see cref="BalanceSimTests"/>
/// pins; Act III/Climax (L5, forward-ladder plan 2026-08-10-003) read <see cref="Hero.LadderRank"/>
/// against the ladder's own <see cref="ArcDirectorSystem.TerminalRank"/>/<see cref="ArcDirectorSystem.ClimaxRank"/>
/// instead of a floor number, so the day-8 floor below is a loose sanity ceiling, not a pinned band —
/// the plan's own two-sided bands are L6's gate, not this one's.
/// </summary>
public class ArcBalanceTests
{
    private const int Days = 100;
    private const ulong MainSeed = 2026;

    // Mirrors BalanceSimTests' established floor-3 pacing band for Act II. Kept as a separate local
    // const per that file's own "change them consciously" rule — not shared, to avoid coupling two
    // test files over one number.
    private const int NoActIIBeforeDay = 1;  // floor 3 cannot happen on day 1

    // Act III now requires reaching the ladder's TOP rank (graduating Gloomwood, itself gated behind
    // graduating the Mine/Crypt first) — day 8 remains a safe, deliberately loose sanity floor, not a
    // measured band (L6 owns the tight two-sided contract).
    private const int NoActIIIBeforeDay = 8;

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
            // L5: Ending schedules off ClimaxDay (the terminal venue's own boss falling), not
            // ActIIIStartDay (the terminal venue merely opening) — the two now land days apart.
            Assert.Equal(arc.ClimaxDay + ArcDirectorSystem.EndingDelayDays, arc.EndingDay);
        }
    }
}
