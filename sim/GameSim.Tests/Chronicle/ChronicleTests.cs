using System.Collections.Immutable;
using Analytics;
using GameSim;
using GameSim.Chronicle;
using GameSim.Contracts;

namespace GameSim.Tests.Chronicle;

public class ChronicleTests
{
    private static GameState PlayedState(ulong seed, int days)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);
        for (var i = 0; i < days * 5; i++) // 5-phase day
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        return state;
    }

    [Fact]
    public void Export_RoundTrips_ByteIdentical()
    {
        var data = ChronicleCodec.FromState(42, PlayedState(42, 10));
        var json = ChronicleCodec.Serialize(data);
        var back = ChronicleCodec.Deserialize(json);

        Assert.Equal(json, ChronicleCodec.Serialize(back));
        Assert.Equal(data.Events.Count, back.Events.Count);
        Assert.Equal(data.Heroes.Count, back.Heroes.Count);
        Assert.Equal(42UL, back.Seed);
    }

    [Fact]
    public void Report_Totals_MatchFixture()
    {
        var hero = new Hero(new HeroId(1), "Torvald", "vanguard", 1, 25, 30,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: false, 0, DiedOnDay: 2);

        var events = ImmutableList.Create<GameEvent>(
            new HeroDied(new HeroId(1), 2, "slain", GearSet.Empty) { Id = new EventId(1), Day = 2 },
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(5), new HeroId(1), 1, "x") { Id = new EventId(2), Day = 1 },
            new AttributionBeatEvent(BeatType.LethalSave, new ItemId(5), new HeroId(1), 1, "y") { Id = new EventId(3), Day = 1 },
            new ItemSold(new ItemId(5), new HeroId(1), 18, FromPlayerShop: true) { Id = new EventId(4), Day = 1 },
            new ItemSold(new ItemId(6), new HeroId(1), 10, FromPlayerShop: false) { Id = new EventId(5), Day = 1 },
            new LootIncomeReceived(new HeroId(1), 25) { Id = new EventId(6), Day = 1 },
            new HeroPassedOnItem(new HeroId(1), new ItemId(7), "can't afford at 45g — has 30g") { Id = new EventId(7), Day = 1 });

        var report = Report.Build([new ChronicleData(1, Day: 4, DayPhase.Morning, ImmutableList.Create(hero), events)]);

        Assert.Contains("Runs: 1 | Total sim-days: 3", report);
        Assert.Contains("KillingBlow: 1", report);
        Assert.Contains("LethalSave: 1", report);
        Assert.Contains("Player sales: 1 (18g revenue) | Rival sales: 1", report);
        Assert.Contains("Hero loot income: 25g", report);
        Assert.Contains("1× can't afford", report);
        Assert.Contains("Vanguard | 1", report);
    }

    [Fact]
    public void Bucket_CollapsesReasonVariants()
    {
        Assert.Equal("can't afford", Report.Bucket("can't afford at 99g — has 1g"));
        Assert.Equal("too heavy for role", Report.Bucket("too heavy for a mystic — 9 weight, carries at most 4"));
        Assert.Equal("current gear is better", Report.Bucket("current Steel Blade is better"));
    }

    [Fact]
    public void Report_EmptyRunList_StillRenders()
    {
        var report = Report.Build([]);
        Assert.Contains("Runs: 0", report);
    }
}
