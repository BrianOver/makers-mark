using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// The 100-day balance gate (U10, R23): progression stays in band, the economy stays
/// solvent, the grin rate holds, trivialization is fenced, determinism is absolute.
/// Band constants here are THE tuning record — change them consciously.
/// </summary>
public class BalanceSimTests
{
    private const int Days = 100;
    private const ulong MainSeed = 2026;

    // Bands (tuned in U10):
    private const int Floor3ByDay = 40;      // at least one hero this deep this soon
    // Trivialization ceiling — catches a broken gate letting floor 5 fall in the first
    // few days. Re-fit from 15 to 8 after the death-clears-floor correctness fix: the
    // corrected sim reaches floor 5 around day 12-13 under baseline play (the old 15 was
    // calibrated against buggy clear logic). Floor-5 pacing (~day 12) is a playtest knob —
    // raise floor gates or slow advancement if first-session content should last longer.
    private const int NoFloor5BeforeDay = 8;

    // Forward-ladder plan L6 (2026-08-10-003, "the gates go green"): TWO-SIDED rung-0-clear window
    // on the main seed — a one-sided ">=" band can't see a campaign that never clears floor 5 at
    // all (exactly the blind spot draft #413's placeholder-Days upper bound existed to close, back
    // when floor 5 was unreachable on every seed). Measured post-L5 (`characterize --seeds 2026`):
    // main seed clears floor 5 on day 18, inside the plan's own [8,18] rung-0 rule.
    //
    // RE-BASELINE (2026-08-17, U-T1-9, §11.14.10-authorized): composing U-T1-9 (register #157,
    // UnlockTalentAction now spends an action slot, tier-2/3-smithing Forge-Tier-gated) with U-T1-11
    // (the reference smith buys ore, accepts commissions, and climbs the Forge Tier ladder) pushes
    // rung-0 clear on the main seed from day 18 to day 25 — BaselinePlayer now spends real action
    // slots and real gold on Forge Tier purchases and commission fulfillment instead of pure combat
    // support, so the whole roster gears up slightly slower. RNG-position pin asserted unchanged:
    // grepped this diff's non-test sim files (ActionLegality.cs, ActionBudget.cs,
    // CraftingHandlers.cs, TalentTree.cs, BaselinePlayer.cs, CommissionSystem.cs,
    // CommissionHandlers.cs, GoldLedger.cs) for new RNG/Random/.Next( calls — none found. The day-18
    // -> day-25 shift is entirely a different LEGAL-ACTION trajectory (different actions submitted
    // on different days), not a new or moved random draw; the fast lane's golden-replay test (same
    // seed, empty action stream) is unaffected and stays green. 18 -> 27 (two days of headroom over
    // the day-25 measurement, not shaved to the exact number, matching this file's own "change them
    // consciously" rule).
    private const int Floor5ByDay = 27;
    private const int MinAliveAtEnd = 3;      // recruit trickle keeps the town alive
    private const int GrinWindowDays = 60;    // grin-rate measured over the last N days
    private const int MinBeatsPerWindow = 60; // ≥1 attribution beat per day once rolling

    private sealed record RunStats(
        GameState Final,
        string FinalJson,
        int FirstFloor3Day,
        int FirstFloor5Day,
        int MinPlayerGold,
        ImmutableList<int> BeatDays);

    private static RunStats Run(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        var firstFloor3 = int.MaxValue;
        var firstFloor5 = int.MaxValue;
        var minGold = state.Player.Gold;
        var beatDays = ImmutableList.CreateBuilder<int>();

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;
            minGold = Math.Min(minGold, state.Player.Gold);

            foreach (var gameEvent in result.Events)
            {
                switch (gameEvent)
                {
                    case AttributionBeatEvent beat:
                        beatDays.Add(beat.Day);
                        break;
                    case FloorRecordSet { Floor: >= 3 } record:
                        firstFloor3 = Math.Min(firstFloor3, record.Day);
                        if (record.Floor >= 5)
                        {
                            firstFloor5 = Math.Min(firstFloor5, record.Day);
                        }

                        break;
                }
            }
        }

        return new RunStats(state, SaveCodec.Serialize(state), firstFloor3, firstFloor5, minGold, beatDays.ToImmutable());
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_Bands_Hold_OnMainSeed()
    {
        var stats = Run(MainSeed);

        Assert.True(stats.FirstFloor3Day <= Floor3ByDay,
            $"no hero reached floor 3 by day {Floor3ByDay} (first: {(stats.FirstFloor3Day == int.MaxValue ? "never" : stats.FirstFloor3Day)})");

        Assert.True(stats.FirstFloor5Day >= NoFloor5BeforeDay,
            $"floor 5 cleared on day {stats.FirstFloor5Day} — before the day-{NoFloor5BeforeDay} trivialization ceiling");
        Assert.True(stats.FirstFloor5Day <= Floor5ByDay,
            $"rung-0 (Mine/Sunken-Crypt) never cleared by day {Floor5ByDay} (first floor-5: "
            + $"{(stats.FirstFloor5Day == int.MaxValue ? "never" : stats.FirstFloor5Day.ToString())})");

        Assert.True(stats.MinPlayerGold >= 0, $"player went insolvent (min gold {stats.MinPlayerGold})");

        var alive = stats.Final.Heroes.Values.Count(h => h.Alive);
        Assert.InRange(alive, MinAliveAtEnd, 6);

        var windowStart = Days - GrinWindowDays;
        var beatsInWindow = stats.BeatDays.Count(d => d > windowStart);
        Assert.True(beatsInWindow >= MinBeatsPerWindow,
            $"grin rate too low: {beatsInWindow} attribution beats in the last {GrinWindowDays} days (need {MinBeatsPerWindow})");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void Ae5_HundredDay_ByteIdenticalReplay()
    {
        Assert.Equal(Run(MainSeed).FinalJson, Run(MainSeed).FinalJson);
    }

    [Theory]
    [Trait("Category", "Balance")]
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
    public void SeedSweep_CoreBands_Hold(ulong seed)
    {
        var stats = Run(seed);

        // Core survivability bands only — per-seed variance on pacing is expected.
        Assert.True(stats.MinPlayerGold >= 0, $"seed {seed}: insolvent (min {stats.MinPlayerGold})");
        var alive = stats.Final.Heroes.Values.Count(h => h.Alive);
        Assert.InRange(alive, 1, 6);
        Assert.True(stats.FirstFloor5Day >= NoFloor5BeforeDay,
            $"seed {seed}: floor 5 on day {stats.FirstFloor5Day} — trivialized");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void TenDayRun_IsDeterministic()
    {
        string Quick()
        {
            var kernel = GameComposition.BuildKernel();
            var state = GameComposition.NewCampaign(seed: 2026);
            for (var i = 0; i < 50; i++) // 10 days × 5-phase
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Quick(), Quick());
    }
}
