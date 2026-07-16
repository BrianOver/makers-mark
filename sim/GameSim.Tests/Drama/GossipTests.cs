using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Heroes;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// Tavern gossip (R14): every line grows from a REAL stamped event — never from a
/// disconnected flavor pool. Generation is a pure function; the Morning system reads
/// yesterday's already-stamped log entries.
///
/// U4 note: prose is pack-driven now, so assertions here are STRUCTURAL (source ids,
/// caps, selection, facts-verbatim). Exact prose is pinned in
/// <c>Flavor/TavernPackTests</c> against golden (campaign, event id) inputs.
/// </summary>
public class GossipTests
{
    /// <summary>Arbitrary fixed campaign identity for pure-function tests.</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    private static GossipEmitted[] Generate(GameState state, int maxLines, params GameEvent[] events) =>
        [.. GossipGenerator.Generate(events, state.Heroes, state.Items, Campaign, maxLines)];

    [Fact]
    public void Generator_TemplatesEverySupportedEventType_CitingItsSourceId()
    {
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        var salve = PlayerItem(11, "Field Salve", ItemSlot.Consumable, 0, 0);
        var state = WithItem(WithItem(NewWorld(), blade), salve);
        var sources = new GameEvent[]
        {
            new HeroDied(new HeroId(1), 2, "slain by a Tunnel Spider", GearSet.Empty) { Id = new EventId(5), Day = 1 },
            new AttributionBeatEvent(BeatType.KillingBlow, blade.Id, new HeroId(1), 2, "detail") { Id = new EventId(6), Day = 1 },
            new AttributionBeatEvent(BeatType.LethalSave, blade.Id, new HeroId(2), 2, "detail") { Id = new EventId(7), Day = 1 },
            new AttributionBeatEvent(BeatType.BreakpointClear, blade.Id, new HeroId(3), 2, "detail") { Id = new EventId(8), Day = 1 },
            new AttributionBeatEvent(BeatType.Provisioned, salve.Id, new HeroId(4), 2, "detail") { Id = new EventId(9), Day = 1 },
            new AttributionBeatEvent(BeatType.PotionLifesave, salve.Id, new HeroId(5), 2, "detail") { Id = new EventId(10), Day = 1 },
            new FloorRecordSet(new HeroId(4), 3) { Id = new EventId(11), Day = 1 },
            new RecruitArrived(new HeroId(5)) { Id = new EventId(12), Day = 1 },
        };

        var lines = Generate(state, maxLines: 10, sources);

        Assert.Equal(sources.Length, lines.Length);
        for (var i = 0; i < sources.Length; i++)
        {
            Assert.Equal(sources[i].Id, lines[i].Source); // R14: cite the source event
            Assert.False(string.IsNullOrWhiteSpace(lines[i].Line));
        }

        Assert.Contains("Torvald", lines[0].Line);         // death line names the hero (R4)
        Assert.Contains("Fine Iron Blade", lines[1].Line); // beat line names the item (R4)
        Assert.Contains("Field Salve", lines[4].Line);     // Provisioned names the consumable (R4)
        Assert.Contains("Field Salve", lines[5].Line);     // PotionLifesave names the consumable (R4)
    }

    [Fact]
    public void Generator_IgnoresUntemplatedAndUnstampedEvents()
    {
        var state = NewWorld();
        var lines = Generate(
            state,
            maxLines: 10,
            new ItemCrafted(new ItemId(1), QualityGrade.Fine) { Id = new EventId(3), Day = 1 },   // no template
            new GossipEmitted(new EventId(1), "old line") { Id = new EventId(4), Day = 1 },        // never gossip about gossip
            new HeroDied(new HeroId(1), 1, "slain by a Cave Rat", GearSet.Empty));                 // unstamped (Id 0) — not a real logged event

        Assert.Empty(lines);
    }

    [Fact]
    public void Generator_CapsAtMaxLines_PickingFirstNInLogOrder()
    {
        var state = NewWorld();
        var sources = Enumerable.Range(1, 5)
            .Select(i => (GameEvent)new FloorRecordSet(new HeroId(1), 2) { Id = new EventId(i), Day = 1 })
            .ToArray();

        var lines = Generate(state, GossipGenerator.MaxLinesPerDay, sources);

        Assert.Equal(3, lines.Length);
        Assert.Equal(new[] { 1, 2, 3 }, lines.Select(l => l.Source.Value));
    }

    [Fact]
    public void GossipSystem_MorningGossipsAboutYesterdaysLog_DrawingNoRng()
    {
        var state = NewWorld() with { Day = 2, Phase = DayPhase.Morning };
        state = state with
        {
            NextEventId = 2,
            EventLog = state.EventLog.Add(
                new HeroDied(new HeroId(1), 2, "slain by a Tunnel Spider", GearSet.Empty) { Id = new EventId(1), Day = 1 }),
        };

        var tick = Tick(state, new GossipSystem());

        var gossip = Assert.Single(tick.Events.OfType<GossipEmitted>());
        Assert.Equal(new EventId(1), gossip.Source);
        Assert.Equal(2, gossip.Day); // told the morning after
        Assert.Contains("Torvald", gossip.Line);
        Assert.Equal(state.Rng, tick.NewState.Rng); // KTD2: gossip consumes zero RNG state
    }

    [Fact]
    public void GossipSystem_FirstMorning_HasNothingToTell()
    {
        var tick = Tick(NewWorld(), new GossipSystem());

        Assert.Empty(tick.Events.OfType<GossipEmitted>());
    }

    [Fact]
    public void Property_ComposedMultiDayRun_EveryGossipCitesARealLoggedEvent_CappedPerDay()
    {
        var state = ComposedWorld(seed: 2026);
        var systems = ComposedSystems();

        for (var tick = 0; tick < 36; tick++) // 12 days
        {
            state = Tick(state, systems).NewState;
        }

        var gossip = state.EventLog.OfType<GossipEmitted>().ToList();
        Assert.NotEmpty(gossip); // a 12-day run with deaths/records/beats must produce talk

        foreach (var line in gossip)
        {
            var source = Assert.Single(state.EventLog, e => e.Id == line.Source); // real + unique (R14)
            Assert.Equal(line.Day - 1, source.Day); // yesterday's news, told this morning
            Assert.True(
                source is HeroDied or AttributionBeatEvent or FloorRecordSet or RecruitArrived,
                $"gossip grew from an untemplated event type {source.GetType().Name}");
        }

        foreach (var day in gossip.GroupBy(g => g.Day))
        {
            Assert.InRange(day.Count(), 1, GossipGenerator.MaxLinesPerDay);
        }
    }

    /// <summary>Starting six + a stocked player shelf so purchases, beats, deaths, and records all occur.</summary>
    internal static GameState ComposedWorld(ulong seed)
    {
        var state = NewWorld(seed);
        var shelf = ImmutableList.CreateBuilder<ShelfEntry>();
        var items = new[]
        {
            PlayerItem(100, "Forgemaster Blade", ItemSlot.Weapon, attack: 12, defense: 0),
            PlayerItem(101, "Forgemaster Edge", ItemSlot.Weapon, attack: 11, defense: 0),
            PlayerItem(102, "Forgemaster Shield", ItemSlot.Shield, attack: 0, defense: 9),
            PlayerItem(103, "Forgemaster Plate", ItemSlot.Armor, attack: 0, defense: 9),
            PlayerItem(104, "Forgemaster Mail", ItemSlot.Armor, attack: 0, defense: 8),
            PlayerItem(105, "Forgemaster Buckler", ItemSlot.Shield, attack: 0, defense: 8),
        };
        foreach (var item in items)
        {
            state = WithItem(state, item);
            shelf.Add(new ShelfEntry(item.Id, Price: 20));
        }

        return state with
        {
            NextItemId = 200,
            Player = state.Player with { Shelf = shelf.ToImmutable() },
        };
    }

    internal static IPhaseSystem[] ComposedSystems() =>
    [
        new RecruitSystem(),
        new GossipSystem(),
        new HeroShoppingSystem(),
        new ExpeditionSystem(),
        new ExpeditionRevealSystem(),
    ];
}
