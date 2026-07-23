using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Presentation;

namespace GameSim.Tests.Presentation;

/// <summary>
/// U-W1: the Presentation Scheduler (docs/plans/2026-07-21-005-watch-surfaces.md). Covers the
/// pacing contract's numbered rules: determinism (rule-adjacent — the whole module must be
/// byte-stable), telegraph/hold/resolve shape (rule 2), beat budget caps (rule 3), the no-leak
/// ordering invariant (rule 4), and honest near-miss detection (rule 5) — plus a regression test
/// for the item-name threading bug found while building this (an attribution beat's rendered
/// line must carry the item's real display NAME, never the raw <see cref="ItemId"/> shape).
/// </summary>
public class PresentationSchedulerTests
{
    private const ulong Campaign = 0xC0FFEEUL;
    private const int Day = 4;

    // ---------------------------------------------------------------- builders

    private static Hero MakeHero(int id, string name, int maxHp = 30) =>
        new(new HeroId(id), name, "warrior", 1, maxHp, 0, GearSet.Empty,
            ImmutableList<ItemMemory>.Empty, true, 0, null);

    private static Item MakeItem(int id, string name) =>
        new(new ItemId(id), "recipe", name, ItemSlot.Weapon, QualityGrade.Common,
            default, new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static CombatEvent Combat(
        int floor, HeroId hero, string monster, int dmgTaken, bool killed,
        ItemId? killingItem = null, ImmutableList<ConsumableUse>? uses = null) =>
        new(floor, hero, monster, ImmutableList.Create(3, 3), 5, dmgTaken, killed, killingItem)
        {
            Uses = uses ?? ImmutableList<ConsumableUse>.Empty,
        };

    private static ImmutableSortedDictionary<int, Item> Items(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static ExpeditionResult MakeResult(
        ImmutableList<HeroId> party,
        ImmutableList<FloorOutcome> floors,
        ImmutableList<HeroId>? deaths = null,
        ImmutableList<AttributionBeat>? beats = null,
        int targetFloor = 0) => new(
        party,
        targetFloor == 0 ? floors[^1].Floor : targetFloor,
        floors[^1].Floor,
        floors,
        party.Where(h => !(deaths ?? ImmutableList<HeroId>.Empty).Contains(h)).ToImmutableList(),
        deaths ?? ImmutableList<HeroId>.Empty,
        beats ?? ImmutableList<AttributionBeat>.Empty,
        ImmutableList<OreLoot>.Empty,
        ImmutableSortedDictionary<int, int>.Empty,
        "mine",
        ExpeditionHalt.TargetReached);

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void Schedule_SameInputs_IsByteIdenticalTwice()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var items = Items(MakeItem(10, "Riverfang"));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                Combat(1, heroIds[0], "Cave Rat", 4, true, killingItem: new ItemId(10)),
                Combat(1, heroIds[1], "Cave Rat", 6, true))),
            new FloorOutcome(2, true, ImmutableList.Create(
                Combat(2, heroIds[0], "Tunnel Spider", 25, true),
                Combat(2, heroIds[1], "Tunnel Spider", 3, true))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.KillingBlow, new ItemId(10), heroIds[0], 1, "Riverfang landed the killing blow"));
        var result = MakeResult(heroIds, floors, beats: beats);

        var first = PresentationScheduler.Schedule(result, party, items, Campaign, Day);
        var second = PresentationScheduler.Schedule(result, party, items, Campaign, Day);

        // NOTE: ImmutableArray<T>.Equals is reference equality on the backing array, not
        // element-wise — Assert.Equal(first, second) on the arrays directly would always report
        // "not equal" even for byte-identical contents. Compare as plain lists instead.
        Assert.Equal(first.ToList(), second.ToList());
        Assert.NotEmpty(first);
    }

    [Fact]
    public void Schedule_EmptyParty_Throws()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, new HeroId(1), "Cave Rat", 4, true))));
        var result = MakeResult(ImmutableList.Create(new HeroId(1)), floors);

        Assert.Throws<ArgumentException>(() =>
            PresentationScheduler.Schedule(result, ImmutableList<Hero>.Empty, Items(), Campaign, Day));
    }

    // ---------------------------------------------------------------- telegraph/hold/resolve shape (rule 2)

    [Fact]
    public void Schedule_GlanceAndPullFocusBeats_CarryBothTelegraphAndResolveLines()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var deaths = ImmutableList.Create(heroIds[0]);
        var floors = ImmutableList.Create(
            new FloorOutcome(1, false, ImmutableList.Create(Combat(1, heroIds[0], "Deep Ghoul", 30, killed: false))));
        var result = MakeResult(heroIds, floors, deaths: deaths) with { Halt = ExpeditionHalt.PartyWiped };

        var beats = PresentationScheduler.Schedule(result, party, Items(), Campaign, Day);

        var dilated = beats.Where(b => b.Tier != BeatTier.Ambient).ToList();
        Assert.NotEmpty(dilated);
        foreach (var beat in dilated)
        {
            Assert.False(string.IsNullOrEmpty(beat.TelegraphLine));
            Assert.False(string.IsNullOrEmpty(beat.ResolveLine));
        }
    }

    [Fact]
    public void Schedule_AmbientBeats_CarryEmptyTelegraphLine()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                Combat(1, heroIds[0], "Cave Rat", 4, true),
                Combat(1, heroIds[1], "Cave Rat", 3, true))));
        var result = MakeResult(heroIds, floors);

        var beats = PresentationScheduler.Schedule(result, party, Items(), Campaign, Day);

        var ambient = beats.Where(b => b.Tier == BeatTier.Ambient);
        Assert.All(ambient, b => Assert.Equal(string.Empty, b.TelegraphLine));
    }

    // ---------------------------------------------------------------- budget caps (rule 3)

    [Fact]
    public void Schedule_ManyDeaths_CapsAtOnePullFocusAndSixGlance()
    {
        // 10 floors, each with a distinct hero dying — every floor is a maximal-stakes (death)
        // candidate, so the budget pass must still cap the output rather than dilating them all.
        var party = Enumerable.Range(1, 10).Select(i => MakeHero(i, $"Hero{i}")).ToImmutableList();
        var floors = ImmutableList.CreateRange(Enumerable.Range(1, 10).Select(floor =>
            new FloorOutcome(floor, false, ImmutableList.Create(
                Combat(floor, new HeroId(floor), "Deep Ghoul", 30, killed: false)))));
        var deaths = ImmutableList.CreateRange(Enumerable.Range(1, 10).Select(i => new HeroId(i)));
        var result = MakeResult(party.Select(h => h.Id).ToImmutableList(), floors, deaths: deaths)
            with
        { Halt = ExpeditionHalt.PartyWiped };

        var beats = PresentationScheduler.Schedule(result, party, Items(), Campaign, Day);

        Assert.True(beats.Count(b => b.Tier == BeatTier.PullFocus) <= PresentationScheduler.MaxPullFocus);
        Assert.True(beats.Count(b => b.Tier == BeatTier.Glance) <= PresentationScheduler.MaxGlance);
        // Every floor is still told SOMEWHERE (never silently dropped) — one beat per floor plus
        // the departure/closer bookends.
        Assert.Equal(10 + 2, beats.Length);
    }

    // ---------------------------------------------------------------- no-leak (rule 4)

    [Fact]
    public void Schedule_RevealOrder_IsStrictlyAscendingAndFloorsNeverGoBackward()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                Combat(1, heroIds[0], "Cave Rat", 4, true),
                Combat(1, heroIds[1], "Cave Rat", 3, true))),
            new FloorOutcome(2, true, ImmutableList.Create(
                Combat(2, heroIds[0], "Tunnel Spider", 25, true),
                Combat(2, heroIds[1], "Tunnel Spider", 3, true))),
            new FloorOutcome(3, true, ImmutableList.Create(
                Combat(3, heroIds[0], "Ore Golem", 5, true),
                Combat(3, heroIds[1], "Ore Golem", 3, true))));
        var beats = PresentationScheduler.Schedule(MakeResult(heroIds, floors), party, Items(), Campaign, Day);

        for (var i = 0; i < beats.Length; i++)
        {
            Assert.Equal(i, beats[i].RevealOrder);
        }

        var floorSequence = beats.Where(b => b.Floor is not null).Select(b => b.Floor!.Value).ToList();
        var sorted = floorSequence.OrderBy(f => f).ToList();
        Assert.Equal(sorted, floorSequence); // ascending — no floor's fact ever precedes an earlier floor's
    }

    [Fact]
    public void Schedule_NoBeatReferencesAFloorLaterThanItsOwnPosition()
    {
        // Stronger no-leak check: walk the beats in order and track the highest floor number seen
        // so far. A beat's own Floor must never REGRESS below a floor already fully told earlier —
        // i.e. no beat can smuggle in a fact from a floor whose turn hasn't come round yet, because
        // floors are scheduled strictly in the order the resolver produced them.
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var floors = ImmutableList.CreateRange(Enumerable.Range(1, 5).Select(floor =>
            new FloorOutcome(floor, true, ImmutableList.Create(
                Combat(floor, heroIds[0], "Cave Rat", 2, true)))));
        var beats = PresentationScheduler.Schedule(MakeResult(heroIds, floors), party, Items(), Campaign, Day);

        var highestSoFar = 0;
        foreach (var beat in beats)
        {
            if (beat.Floor is { } floor)
            {
                Assert.True(floor >= highestSoFar, "a later-scheduled beat must never carry an earlier floor's fact out of order");
                highestSoFar = Math.Max(highestSoFar, floor);
            }
        }
    }

    // ---------------------------------------------------------------- near-miss (rule 5)

    [Fact]
    public void Schedule_HeroEndsAt15PercentOrUnderAndLives_IsFlaggedAsNearMiss()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess", maxHp: 100));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        // 90 damage on a 100-hp hero leaves 10 hp = 10% <= 15% threshold, and the hero survives.
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, heroIds[0], "Ore Golem", 90, killed: false))));
        var result = MakeResult(heroIds, floors);

        var beats = PresentationScheduler.Schedule(result, party, Items(), Campaign, Day);

        var floor1 = beats.Single(b => b.Floor == 1);
        Assert.NotEqual(BeatTier.Ambient, floor1.Tier);
        Assert.Contains("10%", floor1.ResolveLine);
    }

    [Fact]
    public void Schedule_HeroAboveNearMissThreshold_StaysAmbient()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess", maxHp: 100));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        // 50 damage on a 100-hp hero leaves 50% — comfortably above the 15% near-miss threshold,
        // and no kill/death/attribution beat on this floor either, so it stays a routine ambient clear.
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, heroIds[0], "Ore Golem", 50, killed: false))));
        var result = MakeResult(heroIds, floors);

        var beats = PresentationScheduler.Schedule(result, party, Items(), Campaign, Day);

        var floor1 = beats.Single(b => b.Floor == 1);
        Assert.Equal(BeatTier.Ambient, floor1.Tier);
    }

    [Fact]
    public void Schedule_ProvenSave_OutranksAndSuppressesPlainNearMissDoubleCount()
    {
        // A hero at low HP whose fall was PROVABLY prevented by a maker's-marked item (LethalSave)
        // must be scheduled as the proven-save beat, not double-counted as a second plain near-miss
        // candidate for the same hero on the same floor.
        var party = ImmutableList.Create(MakeHero(1, "Kess", maxHp: 100));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var items = Items(MakeItem(10, "Riverfang"));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, heroIds[0], "Ore Golem", 90, killed: true, killingItem: new ItemId(10)))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.LethalSave, new ItemId(10), heroIds[0], 1, "Riverfang saved Kess"));
        var result = MakeResult(heroIds, floors, beats: beats);

        var schedule = PresentationScheduler.Schedule(result, party, items, Campaign, Day);

        // Exactly one dilated (non-ambient) beat for floor 1 — the proven save, not two competing beats.
        Assert.Single(schedule, b => b.Floor == 1);
        var floor1 = schedule.Single(b => b.Floor == 1);
        Assert.Contains("Riverfang", floor1.ResolveLine);
    }

    // ---------------------------------------------------------------- item-name threading regression

    [Fact]
    public void Schedule_AttributionBeat_ResolveLineCarriesRealItemName_NotRawItemId()
    {
        // Regression test: an earlier build of this scheduler passed the AttributionBeat's raw
        // ItemId through to TavernPack instead of the resolved Item.Name, so the rendered line
        // showed the internal id shape (e.g. "ItemId { Value = 10 }") instead of "Riverfang". The
        // fix threads itemName through RenderTavern explicitly (see PresentationScheduler.cs).
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var items = Items(MakeItem(10, "Riverfang"));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                Combat(1, heroIds[0], "Cave Rat", 4, true, killingItem: new ItemId(10)))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.KillingBlow, new ItemId(10), heroIds[0], 1, "Riverfang landed the killing blow"));
        var result = MakeResult(heroIds, floors, beats: beats);

        var schedule = PresentationScheduler.Schedule(result, party, items, Campaign, Day);
        var floor1 = schedule.Single(b => b.Floor == 1);

        Assert.Contains("Riverfang", floor1.ResolveLine);
        Assert.DoesNotContain("ItemId", floor1.ResolveLine);
        Assert.Equal(new ItemId(10), floor1.Item);
    }

    [Fact]
    public void Schedule_AttributionBeat_ItemNotInRegistry_FallsBackToRawIdLikeGossipGenerator()
    {
        // Mirrors GossipGenerator.ItemName's fallback (Drama/GossipGenerator.cs): an item id absent
        // from the registry falls back to the id's own ToString(), never a thrown exception.
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                Combat(1, heroIds[0], "Cave Rat", 4, true, killingItem: new ItemId(10)))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.KillingBlow, new ItemId(10), heroIds[0], 1, "landed the killing blow"));
        var result = MakeResult(heroIds, floors, beats: beats);

        var schedule = PresentationScheduler.Schedule(result, party, Items(/* empty registry */), Campaign, Day);

        // Must not throw, and must produce SOME line (falls back to the ItemId's own text).
        var floor1 = schedule.Single(b => b.Floor == 1);
        Assert.False(string.IsNullOrEmpty(floor1.ResolveLine));
    }

    // ---------------------------------------------------------------- tier assignment

    [Fact]
    public void Schedule_Death_IsPromotedToPullFocus()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var deaths = ImmutableList.Create(heroIds[0]);
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, heroIds[1], "Cave Rat", 2, true))),
            new FloorOutcome(2, false, ImmutableList.Create(Combat(2, heroIds[0], "Deep Ghoul", 30, killed: false))));
        var result = MakeResult(heroIds, floors, deaths: deaths) with { Halt = ExpeditionHalt.FloorLost };

        var beats = PresentationScheduler.Schedule(result, party, Items(), Campaign, Day);

        var deathFloor = beats.Single(b => b.Floor == 2);
        Assert.Equal(BeatTier.PullFocus, deathFloor.Tier);
    }

    [Fact]
    public void Schedule_PlainKillingBlow_IsGlanceNotPullFocus()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var heroIds = party.Select(h => h.Id).ToImmutableList();
        var items = Items(MakeItem(10, "Riverfang"));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, heroIds[0], "Cave Rat", 2, true, killingItem: new ItemId(10)))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.KillingBlow, new ItemId(10), heroIds[0], 1, "Riverfang landed the killing blow"));
        var result = MakeResult(heroIds, floors, beats: beats);

        var schedule = PresentationScheduler.Schedule(result, party, items, Campaign, Day);
        var floor1 = schedule.Single(b => b.Floor == 1);

        Assert.Equal(BeatTier.Glance, floor1.Tier);
    }
}
