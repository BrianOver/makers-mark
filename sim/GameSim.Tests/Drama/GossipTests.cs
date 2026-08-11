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
            new VenueGraduated("mine", ImmutableList.Create(new HeroId(6)), NewRank: 1) { Id = new EventId(13), Day = 1 },
        };

        var lines = Generate(state, maxLines: 10, sources);

        // Phase B (B1e): with maxLines covering every source, nothing is dropped, but the
        // TELLING ORDER is now salience-ranked (involvement, then recency), not log order — so
        // assert set-membership (every source cited exactly once) and per-source content by
        // lookup, not by position.
        Assert.Equal(
            sources.Select(s => s.Id.Value).OrderBy(v => v),
            lines.Select(l => l.Source.Value).OrderBy(v => v));
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Line)));

        string LineFor(EventId id) => lines.Single(l => l.Source == id).Line;

        Assert.Contains("Torvald", LineFor(sources[0].Id));         // death line names the hero (R4)
        Assert.Contains("Fine Iron Blade", LineFor(sources[1].Id)); // beat line names the item (R4)
        Assert.Contains("Field Salve", LineFor(sources[4].Id));     // Provisioned names the consumable (R4)
        Assert.Contains("Field Salve", LineFor(sources[5].Id));     // PotionLifesave names the consumable (R4)
        Assert.Contains(state.Heroes[6].Name, LineFor(sources[8].Id)); // VenueGraduated names the graduate (L5)
    }

    /// <summary>Forward-ladder plan (L5): a party graduates together — the line names the first
    /// graduate and counts the rest, rather than inventing names for heroes the template never
    /// mentions or silently dropping the fact that more than one hero advanced.</summary>
    [Fact]
    public void VenueGraduated_MultipleGraduates_NamesFirstAndCountsTheRest()
    {
        var state = NewWorld();
        var solo = Generate(state, maxLines: 1,
            new VenueGraduated("mine", ImmutableList.Create(new HeroId(1)), NewRank: 1) { Id = new EventId(1), Day = 1 });
        Assert.Contains("Torvald", solo[0].Line);
        Assert.DoesNotContain("other", solo[0].Line);

        var pair = Generate(state, maxLines: 1,
            new VenueGraduated("mine", ImmutableList.Create(new HeroId(1), new HeroId(2)), NewRank: 1) { Id = new EventId(2), Day = 1 });
        Assert.Contains("Torvald", pair[0].Line);
        Assert.Contains("1 other", pair[0].Line);

        var trio = Generate(state, maxLines: 1,
            new VenueGraduated("mine", ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), NewRank: 1) { Id = new EventId(3), Day = 1 });
        Assert.Contains("Torvald", trio[0].Line);
        Assert.Contains("2 others", trio[0].Line);
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
    public void Generator_ToolAssistBeat_StaysUntold()
    {
        // ToolAssist is a reserved BeatType with no pack key yet (BeatBaseKey returns null),
        // so a stamped ToolAssist beat must fall through to 'untold' — no line, no throw.
        var state = NewWorld();
        var lines = Generate(
            state,
            maxLines: 10,
            new AttributionBeatEvent(BeatType.ToolAssist, new ItemId(1), new HeroId(1), 2, "detail") { Id = new EventId(5), Day = 1 });

        Assert.Empty(lines);
    }

    [Fact]
    public void Generator_CapsAtMaxLines_PickingMostRecentForATiedSpeaker()
    {
        // Phase B (B1e): all five events share one subject (HeroId 1), so involvement ties across
        // the board — recency (EventId descending) is the sole, total tie-break. The freshest 3
        // survive the cap, not the first 3 in log order (the OLD behavior this test used to pin).
        var state = NewWorld();
        var sources = Enumerable.Range(1, 5)
            .Select(i => (GameEvent)new FloorRecordSet(new HeroId(1), 2) { Id = new EventId(i), Day = 1 })
            .ToArray();

        var lines = Generate(state, GossipGenerator.MaxLinesPerDay, sources);

        Assert.Equal(3, lines.Length);
        Assert.Equal(new[] { 5, 4, 3 }, lines.Select(l => l.Source.Value));
    }

    [Fact]
    public void Generator_RanksBySpeakerInvolvement_BeforeRecency()
    {
        // Phase B (B1e) core behavior: hero 1 has two tellable events today (more "involved"),
        // hero 2 has only one — even though hero 2's event is the single most RECENT event
        // overall. Involvement outranks recency, so both of hero 1's events win a slot ahead of
        // hero 2's, and the total tie-break (EventId descending) orders hero 1's own two.
        var state = NewWorld();
        GameEvent[] sources =
        [
            new FloorRecordSet(new HeroId(1), 2) { Id = new EventId(1), Day = 1 },
            new FloorRecordSet(new HeroId(1), 3) { Id = new EventId(2), Day = 1 },
            new FloorRecordSet(new HeroId(2), 2) { Id = new EventId(3), Day = 1 }, // most recent, but hero 2's only event
        ];

        var lines = Generate(state, maxLines: 2, sources);

        Assert.Equal(new[] { 2, 1 }, lines.Select(l => l.Source.Value));
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
