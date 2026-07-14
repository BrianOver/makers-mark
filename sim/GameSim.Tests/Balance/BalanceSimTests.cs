using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
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
    private const int NoFloor5BeforeDay = 15; // trivialization ceiling
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

        for (var tick = 0; tick < Days * 3; tick++)
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
            for (var i = 0; i < 30; i++)
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Quick(), Quick());
    }
}
