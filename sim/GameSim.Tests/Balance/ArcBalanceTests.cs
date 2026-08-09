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

    // Upper bound: Act III (and the Ending it schedules) must fire inside the campaign's own
    // 100-day window — a one-sided ">=" band can't see an arc that never reaches its finale, which
    // is exactly the blind spot this unit exists to close. MEASURED 2026-08-08 (U5
    // characterization): on the main seed (2026) AND all 10 sweep seeds used by
    // BalanceSimTests.SeedSweep_CoreBands_Hold (1, 7, 42, 99, 1234, 5678, 31337, 777, 2468, 13579),
    // Act III never fires within 100 days under current baseline play — every run plateaus in Act
    // II (deepest floor reached: 3 or 4; the floor-5 wall is never broken). There is no positive
    // example to derive a tighter bound from, so this is set to the window itself; the assertions
    // below are EXPECTED TO FAIL on main until a follow-up balance unit lets a party clear floor 5.
    // See the commit body for the full per-seed table.
    private const int ActIIIByDay = Days;

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
            $"Act III never fired in {Days} days (arc stuck at {arc.Act} — the floor-5 wall was never broken)");
        Assert.InRange(arc.ActIIIStartDay, NoActIIIBeforeDay, ActIIIByDay);

        Assert.True(arc.EndingDay > 0,
            $"the campaign never reached its Ending in {Days} days (arc stuck at {arc.Act})");
        Assert.InRange(arc.EndingDay, NoActIIIBeforeDay + ArcDirectorSystem.EndingDelayDays, Days);
        Assert.Equal(arc.ActIIIStartDay + ArcDirectorSystem.EndingDelayDays, arc.EndingDay);
    }
}
