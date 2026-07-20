#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U16 (world rework plan, AE2/KTD5/KTD11): <see cref="JourneyStream"/> is the pure PHASE→STREAM
/// TABLE reader every spectate surface (MineWatch/ScryingMirror/PipDock) composes. Pure C#, no
/// Godot runtime — same technique as <c>PhaseClockTests</c>, so this suite runs fast and never
/// needs GODOT_BIN. Covers the AE2/KTD5 death-censor pin, recorded beat order, monster-name
/// fidelity, the player-crafted attribution gate, and multi-party support directly against
/// hand-built <see cref="ExpeditionResult"/>/<see cref="InFlightExpedition"/> fixtures (deterministic
/// — no RNG hunting for a death), plus a real-seed sweep proving the reader never throws or leaks
/// death text across live campaigns.
/// </summary>
[TestSuite]
public class JourneyStreamTests
{
    [TestCase]
    public void Expedition_Phase_BuildsRumoredCards_FromPartiesFormed_NoBeats()
    {
        var state = World() with { Phase = DayPhase.Expedition };
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(1), new HeroId(2)), TargetFloor: 3, VenueId: "mine");
        var events = ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)));

        var cards = JourneyStream.Build(state, events);

        AssertThat(cards.Count).IsEqual(1);
        var card = cards[0];
        AssertThat(card.Stage).IsEqual(JourneyStage.Rumored);
        AssertThat(card.TargetFloor).IsEqual(3);
        AssertThat(card.Beats.IsEmpty).IsTrue(); // rumored: no combat beats exist yet
    }

    [TestCase]
    public void Camp_Phase_StagedParty_ReadsInFlightFloors_Staged_NoAttributionYet()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, new ItemId(1)))));
        var inFlight = StagedParty(floors);
        var state = World() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(inFlight) };

        var cards = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty);

        AssertThat(cards.Count).IsEqual(1);
        var card = cards[0];
        AssertThat(card.Stage).IsEqual(JourneyStage.Staged);
        AssertThat(card.Beats.Any(b => b.Text.Contains("cave-rat"))).IsTrue();
        AssertThat(card.Beats.Any(b => b.IsAttribution)).IsFalse(); // no beats proven until finalize
    }

    [TestCase]
    public void Deep_Phase_HeldParty_SameBeatsAsCampPhase_NoNewOnes()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))));
        var inFlight = StagedParty(floors);
        var campState = World() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(inFlight) };
        var deepState = campState with { Phase = DayPhase.ExpeditionDeep };

        var campCards = JourneyStream.Build(campState, ImmutableList<GameEvent>.Empty);
        var deepCards = JourneyStream.Build(deepState, ImmutableList<GameEvent>.Empty);

        AssertThat(deepCards.Single().Stage).IsEqual(JourneyStage.Held);
        AssertThat(deepCards.Single().Beats.Select(b => b.Text).SequenceEqual(campCards.Single().Beats.Select(b => b.Text)))
            .IsTrue();
    }

    [TestCase]
    public void DeathRound_RendersCloud_NeverDeathText_CampPhase()
    {
        AssertNoDeathTextEverAppears(DayPhase.Camp);
    }

    [TestCase]
    public void DeathRound_RendersCloud_NeverDeathText_EveningPhase()
    {
        // KTD5 pin: even once PendingExpeditions carries the FULL merged result at the Evening
        // phase, the mirror still censors — the Evening TICK's own reveal (a separate surface)
        // hasn't fired yet by definition (Build is a pure read of already-produced state).
        AssertNoDeathTextEverAppears(DayPhase.Evening);
    }

    private static void AssertNoDeathTextEverAppears(DayPhase phase)
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))),
            new FloorOutcome(2, false, ImmutableList.Create(
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(1), 2, 40, false, null))));
        var result = ResolvedResult(floors, deaths: ImmutableList.Create(new HeroId(1)));
        var state = World() with { Phase = phase, PendingExpeditions = ImmutableList.Create(result) };

        var card = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty).Single();

        AssertThat(card.Beats.Any(b => b.Text.Contains("is lost from sight below floor 2"))).IsTrue();
        AssertThat(card.Beats.Any(b => b.Text.Contains("died"))).IsFalse();
        AssertThat(card.Beats.Any(b => b.Text.Contains("takes 40"))).IsFalse(); // the fatal round's real damage line never renders
    }

    [TestCase]
    public void BeatOrder_MatchesRecordedOrder_FloorAscThenHeroThenRound()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null),
                new CombatEvent(1, new HeroId(2), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))),
            new FloorOutcome(2, true, ImmutableList.Create(
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(2, 4), 3, 6, false, null),
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(5), 6, 0, true, null))));
        var result = ResolvedResult(floors, ImmutableList<HeroId>.Empty);
        var state = World() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };

        var beats = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty).Single().Beats;

        // floor 1 enter, hero1 kill, hero2 kill, floor 2 enter, hero1 round1 hurt, hero1 round2 kill.
        AssertThat(string.Join(",", beats.Select(b => b.Floor))).IsEqual("1,1,1,2,2,2");
        AssertThat(beats[1].Text.Contains("H1")).IsTrue();
        AssertThat(beats[2].Text.Contains("H2")).IsTrue();
        AssertThat(beats[4].Text.Contains("takes 6")).IsTrue();  // round 1: hurt
        AssertThat(beats[5].Text.Contains("fells")).IsTrue();    // round 2: kill
    }

    [TestCase]
    public void MonsterNames_MatchCombatEventMonsterKind()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "forgeworm", ImmutableList.Create(3), 5, 0, true, null))));
        var result = ResolvedResult(floors, ImmutableList<HeroId>.Empty);
        var state = World() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };

        var beats = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty).Single().Beats;

        AssertThat(beats.Any(b => b.Text.Contains("forgeworm"))).IsTrue();
    }

    [TestCase]
    public void AttributionCallout_OnlyForPlayerCraftedItems()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, new ItemId(1)))));
        var playerCrafted = new AttributionBeat(BeatType.KillingBlow, new ItemId(1), new HeroId(1), 1, "the Player's Hammer lands the kill");
        var vendorStock = new AttributionBeat(BeatType.KillingBlow, new ItemId(2), new HeroId(1), 1, "a rival blade lands the kill");
        var result = ResolvedResult(floors, ImmutableList<HeroId>.Empty) with
        {
            Beats = ImmutableList.Create(playerCrafted, vendorStock),
        };
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(1, CraftedItem(1, "Player's Hammer"))
            .Add(2, VendorItem(2, "Rival Blade"));
        var state = World() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result), Items = items };

        var beats = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty).Single().Beats;

        AssertThat(beats.Any(b => b.IsAttribution && b.Text.Contains("Player's Hammer"))).IsTrue();
        AssertThat(beats.Any(b => b.Text.Contains("rival blade"))).IsFalse();
    }

    [TestCase]
    public void MultiParty_TwoCards_EachWithOwnFloors()
    {
        var partyA = ResolvedResult(
            ImmutableList.Create(new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null)))),
            ImmutableList<HeroId>.Empty,
            party: ImmutableList.Create(new HeroId(1)));
        var partyB = ResolvedResult(
            ImmutableList.Create(new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(4), "tunnel-spider", ImmutableList.Create(3), 5, 0, true, null)))),
            ImmutableList<HeroId>.Empty,
            party: ImmutableList.Create(new HeroId(4)));
        var state = World() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(partyA, partyB) };

        var cards = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty);

        AssertThat(cards.Count).IsEqual(2);
        AssertThat(cards[0].Beats.Any(b => b.Text.Contains("cave-rat"))).IsTrue();
        AssertThat(cards[1].Beats.Any(b => b.Text.Contains("tunnel-spider"))).IsTrue();
        AssertThat(cards[0].PartyKey).IsNotEqual(cards[1].PartyKey);
    }

    [TestCase]
    public void RealSeeds_NeverThrows_AndNeverLeaksDeathText_AcrossADaySweep()
    {
        // "under 3 seeded expeditions": real ticked campaigns, not hand-built data — the reader
        // must survive whatever the resolver actually produces, seed to seed.
        foreach (var seed in new ulong[] { 9101, 9102, 9103 })
        {
            var adapter = new SimAdapter(seed);
            for (var i = 0; i < 12; i++) // a few full day cycles
            {
                adapter.AdvancePhase();
                var cards = JourneyStream.Build(adapter.CurrentState, adapter.LastEvents);
                foreach (var card in cards)
                {
                    foreach (var beat in card.Beats)
                    {
                        AssertThat(beat.Text.Contains("died")).IsFalse();
                        AssertThat(beat.Text.Length > 0).IsTrue();
                    }
                }
            }
        }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    private static GameState World() => GameFactory.NewGame(4242) with
    {
        Heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "H1"))
            .Add(2, Delver(2, "H2"))
            .Add(4, Delver(4, "H4")),
    };

    private static Hero Delver(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item CraftedItem(int id, string name) => new(
        new ItemId(id), "recipe", name, ItemSlot.Weapon, QualityGrade.Fine, new ItemStats(1, 0, 1),
        new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item VendorItem(int id, string name) => new(
        new ItemId(id), "recipe", name, ItemSlot.Weapon, QualityGrade.Common, new ItemStats(1, 0, 1),
        Mark: null, History: ImmutableList<ItemHistoryEntry>.Empty);

    private static InFlightExpedition StagedParty(ImmutableList<FloorOutcome> floors) => new(
        Party: ImmutableList.Create(new HeroId(1)),
        TargetFloor: 2,
        CheckpointFloor: 1,
        VenueId: "mine",
        Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 40),
        Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
        Gold: ImmutableSortedDictionary<int, int>.Empty,
        Dead: ImmutableSortedSet<int>.Empty,
        Floors: floors,
        Loot: ImmutableList<OreLoot>.Empty,
        DeepestFloorCleared: 1);

    private static ExpeditionResult ResolvedResult(
        ImmutableList<FloorOutcome> floors, ImmutableList<HeroId> deaths, ImmutableList<HeroId>? party = null) => new(
        Party: party ?? ImmutableList.Create(new HeroId(1)),
        TargetFloor: 2,
        DeepestFloorCleared: 1,
        Floors: floors,
        Survivors: (party ?? ImmutableList.Create(new HeroId(1))).Where(h => !deaths.Contains(h)).ToImmutableList(),
        Deaths: deaths,
        Beats: ImmutableList<AttributionBeat>.Empty,
        Loot: ImmutableList<OreLoot>.Empty,
        GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);
}
#endif
