using System.Collections.Immutable;
using Analytics;
using GameSim.Chronicle;
using GameSim.Contracts;

namespace GameSim.Tests.Analytics;

/// <summary>
/// The report tool must tolerate every chronicle ever written (observability plan U3):
/// a pre-P3 export deserializes heroes with a null ClassId — that must degrade to "unknown",
/// never crash the analytics run (found live: Brian's day-4 playtest export).
/// </summary>
public class ReportLegacyTests
{
    [Fact]
    public void Build_ToleratesLegacyHero_WithNullClassId()
    {
        var legacyHero = new Hero(
            new HeroId(1), "Old-Timer", ClassId: null!, Level: 1, MaxHp: 20, Gold: 5,
            new GearSet(null, null, null), ImmutableList<ItemMemory>.Empty,
            Alive: false, DeepestFloorReached: 2, DiedOnDay: 3);
        var died = new HeroDied(new HeroId(1), Floor: 2, "old age", new GearSet(null, null, null)) with
        {
            Id = new EventId(1),
            Day = 3,
        };
        var run = new ChronicleData(
            Seed: 11, Day: 4, DayPhase.Morning,
            ImmutableList.Create(legacyHero),
            ImmutableList.Create<GameEvent>(died));

        var report = Report.Build([run]);

        Assert.Contains("Unknown", report, StringComparison.Ordinal);
    }
}
