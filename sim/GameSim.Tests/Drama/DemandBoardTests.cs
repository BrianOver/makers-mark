using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim; // GameComposition

namespace GameSim.Tests.Drama;

/// <summary>
/// U4 (plan 2026-07-25-001, KTD-5): <see cref="DemandBoard"/> is a pure read model over
/// <see cref="GameState"/>/<see cref="GameState.EventLog"/> — no mutation, no RNG draw. These tests
/// drive a real seeded campaign through <see cref="GameComposition"/> + <see cref="BaselinePlayer"/>
/// (the same idiom as <c>BalanceSimTests</c>/<c>CommissionSystemTests</c>) rather than hand-rolled
/// fixtures, since the snapshot's whole point is to aggregate real, systems-driven state.
/// Per the plan: static demand not rotating day-to-day is expected and is NOT asserted here as a
/// pass/fail — only presence/shape is.
/// </summary>
public class DemandBoardTests
{
    /// <summary>
    /// Runs a seeded campaign for <paramref name="days"/> full days (5 ticks/day — the staged-
    /// resolution idiom every day-loop test in this repo uses), keyed by calendar day to the LAST
    /// state observed while <see cref="GameState.Day"/> still equalled that day (i.e. after that
    /// day's Evening tick has fully resolved, just before the next Morning begins).
    /// </summary>
    private static ImmutableSortedDictionary<int, GameState> RunSeededDays(ulong seed, int days)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);
        var byDay = new SortedDictionary<int, GameState>();

        for (var tick = 0; tick < days * 5; tick++)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            byDay[state.Day] = state;
        }

        return byDay.ToImmutableSortedDictionary();
    }

    [Fact]
    public void Snapshot_NonEmpty_OnDay1OfSeededRun()
    {
        var byDay = RunSeededDays(seed: 2026, days: 1);

        var snapshot = DemandBoard.Snapshot(byDay[1]);

        // The bounty price-floor reference is unconditional (pure BountyRules.MinimumReward per
        // Mine floor) so it alone would trivially satisfy "non-empty" — the meaningful signal is
        // that the OPEN COMMISSION board is already populated: every starting hero has GearSet.Empty
        // (HeroRoster.StartingSix), so CommissionSystem's very first Morning gap-scan posts up to
        // MaxOpenCommissions commissions before the player has taken a single action.
        Assert.Equal(5, snapshot.BountyFloorMinimums.Count); // Mine.FloorCount
        Assert.NotEmpty(snapshot.OpenCommissions);
    }

    [Fact]
    public void EveryOpenCommission_RendersItsFiveJudgingFields()
    {
        var byDay = RunSeededDays(seed: 2026, days: 1);
        var state = byDay[1];

        var snapshot = DemandBoard.Snapshot(state);

        Assert.NotEmpty(snapshot.OpenCommissions);
        foreach (var entry in snapshot.OpenCommissions)
        {
            // (1) hero — resolvable, non-empty display name
            Assert.True(state.Heroes.ContainsKey(entry.Hero.Value));
            Assert.False(string.IsNullOrEmpty(entry.HeroName));
            // (2) slot — one of the three commissionable gear slots
            Assert.Contains(entry.Slot, new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor });
            // (3) min-quality — a real grade value
            Assert.True(Enum.IsDefined(entry.MinQuality));
            // (4) premium — a positive gold ask (BasePremiumGold alone is 15)
            Assert.True(entry.PremiumGold > 0);
            // (5) deadline — strictly in the future of the day it was posted
            Assert.True(entry.DeadlineDay > state.Day);
        }
    }

    [Fact]
    public void DepthStall_EntryAppears_WithinTwoDaysOfPlateauOnset()
    {
        const int Days = 15;
        var byDay = RunSeededDays(seed: 2026, days: Days);

        // Find the first calendar day any depth-stall entry appears.
        var firstStallDay = 0;
        DepthStallEntry? firstEntry = null;
        for (var day = 1; day <= Days; day++)
        {
            var snapshot = DemandBoard.Snapshot(byDay[day]);
            if (snapshot.DepthStalls.Count > 0)
            {
                firstStallDay = day;
                firstEntry = snapshot.DepthStalls[0];
                break;
            }
        }

        Assert.True(firstStallDay > 0, "expected at least one depth-stall entry within 15 days on seed 2026");

        // Independently re-derive that hero's plateau onset straight from the raw log (the same
        // rule DemandBoard documents: last FloorRecordSet, else RecruitArrived, else day 1) —
        // without calling back into DemandBoard's own aggregation — and confirm the entry fired
        // right at the documented threshold, not merely "eventually".
        var log = byDay[firstStallDay].EventLog;
        var hero = firstEntry!.Hero;
        var arrivalDay = log.OfType<RecruitArrived>()
            .Where(e => e.Hero == hero).Select(e => e.Day)
            .DefaultIfEmpty(1).Max();
        var lastProgressDay = log.OfType<FloorRecordSet>()
            .Where(e => e.Hero == hero).Select(e => e.Day)
            .DefaultIfEmpty(arrivalDay).Max();

        Assert.InRange(
            firstStallDay - lastProgressDay,
            DemandBoard.StallThresholdDays,
            DemandBoard.StallThresholdDays + 1); // +1 day-boundary slack (byDay captures END-of-day state)
    }

    /// <summary>
    /// N1 (plan 2026-07-25-001 Slice 2 addendum): the "gear's full — something else is blocking the
    /// push" non-answer must never be reachable. Every stalled hero either has a concrete
    /// <see cref="DepthStallEntry.BlockingSlot"/> (an empty slot) OR — when every slot is worn — a
    /// concrete quality gap (<see cref="DepthStallEntry.CarriedQuality"/> vs
    /// <see cref="DepthStallEntry.RequiredQuality"/> for the next floor). Scans every calendar day of
    /// a seeded 15-day run so the check covers however many distinct stalled heroes actually appear
    /// (not just the lead entry a narration layer happens to print).
    /// </summary>
    [Fact]
    public void DepthStall_NamesConcreteBlocker_NeverFallsThroughToNonAnswer()
    {
        const int Days = 15;
        var byDay = RunSeededDays(seed: 2026, days: Days);

        var sawAnyStall = false;
        var sawAnyQualityGate = false;

        for (var day = 1; day <= Days; day++)
        {
            var snapshot = DemandBoard.Snapshot(byDay[day]);
            foreach (var stall in snapshot.DepthStalls)
            {
                sawAnyStall = true;

                if (stall.BlockingSlot is not null)
                {
                    // Slot gap already fully explains it — the quality fields stay unset (no
                    // double-diagnosis of the same hero).
                    Assert.Null(stall.CarriedQuality);
                    Assert.Null(stall.RequiredQuality);
                    continue;
                }

                // No empty slot: this is exactly the case that used to fall through to
                // "something else is blocking the push". It must now name the quality gate.
                Assert.NotNull(stall.CarriedQuality);
                Assert.NotNull(stall.RequiredQuality);
                Assert.True(Enum.IsDefined(stall.CarriedQuality!.Value));
                Assert.True(Enum.IsDefined(stall.RequiredQuality!.Value));
                sawAnyQualityGate = true;
            }
        }

        Assert.True(sawAnyStall, "expected at least one depth-stall entry within 15 days on seed 2026");
        Assert.True(sawAnyQualityGate,
            "expected at least one full-gear stall (the exact case that used to be a non-answer) within 15 days on seed 2026");
    }
}
