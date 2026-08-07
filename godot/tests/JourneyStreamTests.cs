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

    // ── U-EXP1 (Expedition-watchable, owner-flagged twice — "the player just sits there"):
    // before this unit, DayPhase.Expedition rendered NOTHING but the bare rumor line above for the
    // whole phase. These pin the fix: JourneyCard.Manifest/PartyNames surface who went and what
    // player-crafted gear they carry the INSTANT the party departs — a roster fact (Hero.Gear),
    // never a JourneyBeat, so the Beats.IsEmpty pin above is left completely untouched. ────────────

    [TestCase]
    public void Expedition_Phase_RumoredCard_ManifestNamesPlayerCraftedGear_NotVendorGear()
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "Torvald") with { Gear = new GearSet(new ItemId(1), new ItemId(2), null) })
            .Add(2, Delver(2, "Elowen"));
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(1, CraftedItem(1, "Fine Iron Blade"))
            .Add(2, VendorItem(2, "Rival Shield"));
        var state = World() with { Phase = DayPhase.Expedition, Heroes = heroes, Items = items };
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(1), new HeroId(2)), TargetFloor: 3, VenueId: "mine");
        var events = ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)));

        var card = JourneyStream.Build(state, events).Single();

        AssertThat(card.Beats.IsEmpty).IsTrue(); // still no combat beats — Manifest is a SEPARATE field
        AssertThat(card.Manifest.Count).IsEqual(1); // the vendor shield never earns a line (no MakersMark)
        AssertThat(card.Manifest[0].Text).IsEqual("Torvald carries your Fine Iron Blade.");
        AssertThat(card.Manifest[0].Item).IsEqual(new ItemId(1));
    }

    [TestCase]
    public void Expedition_Phase_RumoredCard_PartyNames_ResolvedInRosterOrder()
    {
        var state = World() with { Phase = DayPhase.Expedition };
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(2), new HeroId(1)), TargetFloor: 3, VenueId: "mine");
        var events = ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)));

        var card = JourneyStream.Build(state, events).Single();

        AssertThat(string.Join(",", card.PartyNames)).IsEqual("H2,H1"); // roster order, not id order
    }

    [TestCase]
    public void Expedition_Phase_NoPlayerCraftedGear_ManifestEmpty_NeverFabricatesALine()
    {
        // GearSet.Empty (Delver's default) — a fresh/bare-handed party. The whole point of KTD2
        // purity here: no MakersMark means no line, ever, however tempting a placeholder would be.
        var state = World() with { Phase = DayPhase.Expedition };
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 3, VenueId: "mine");
        var events = ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)));

        var card = JourneyStream.Build(state, events).Single();

        AssertThat(card.Manifest.IsEmpty).IsTrue();
    }

    [TestCase]
    public void Expedition_Phase_RumoredCard_Manifest_NeverCapped_AllThreeHeroesNamed()
    {
        // U2 (the send-off unit): the FeedVisibleLines-1 cap that silently dropped a party's 3rd
        // carried item lived entirely in MineWatch's own (now-retired) RumoredLines renderer,
        // never in BuildManifest itself. Pinned here at the pure data layer — the one this unit's
        // manifest-consuming renderers (MineWatch's departure slate, PipDock) all read from — so a
        // future renderer cannot reintroduce the same class of bug by assuming a cap belongs
        // upstream of it.
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "Torvald") with { Gear = new GearSet(new ItemId(1), null, null) })
            .Add(2, Delver(2, "Elowen") with { Gear = new GearSet(new ItemId(2), null, null) })
            .Add(4, Delver(4, "Brask") with { Gear = new GearSet(new ItemId(3), null, null) });
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(1, CraftedItem(1, "Fine Iron Blade"))
            .Add(2, CraftedItem(2, "Fine Iron Bow"))
            .Add(3, CraftedItem(3, "Fine Iron Staff"));
        var state = World() with { Phase = DayPhase.Expedition, Heroes = heroes, Items = items };
        var plan = new PartyPlan(
            ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(4)), TargetFloor: 3, VenueId: "mine");
        var events = ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)));

        var card = JourneyStream.Build(state, events).Single();

        AssertThat(card.Manifest.Count).IsEqual(3);
        AssertThat(card.Manifest.Any(m => m.Text.Contains("Fine Iron Blade"))).IsTrue();
        AssertThat(card.Manifest.Any(m => m.Text.Contains("Fine Iron Bow"))).IsTrue();
        AssertThat(card.Manifest.Any(m => m.Text.Contains("Fine Iron Staff"))).IsTrue();
    }

    [TestCase]
    public void DepartureLine_PrefersManifestLine_OverPlaceholder_WhenCraftedGearPresent()
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "Torvald") with { Gear = new GearSet(new ItemId(1), null, null) });
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, CraftedItem(1, "Fine Iron Blade"));
        var state = World() with { Phase = DayPhase.Expedition, Heroes = heroes, Items = items };
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 3, VenueId: "mine");
        var card = JourneyStream.Build(state, ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)))).Single();

        var line = JourneyStream.DepartureLine(card);

        AssertThat(line).IsEqual("Torvald carries your Fine Iron Blade.");
        AssertThat(line.Contains("A party sets out")).IsFalse();
    }

    [TestCase]
    public void DepartureLine_FallsBackToPlaceholder_WhenManifestEmpty()
    {
        var state = World() with { Phase = DayPhase.Expedition };
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 5, VenueId: "mine");
        var card = JourneyStream.Build(state, ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)))).Single();

        var line = JourneyStream.DepartureLine(card);

        AssertThat(line).IsEqual("A party sets out for floor 5…");
    }

    [TestCase]
    public void Camp_Phase_StagedParty_ManifestAlsoPresent_SameGearFactRegardlessOfStage()
    {
        // The manifest is a ROSTER fact (gear doesn't change mid-raid), not a combat beat, so it
        // must keep showing once the party stages at Camp too — not just at the Rumored moment.
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "Torvald") with { Gear = new GearSet(new ItemId(1), null, null) });
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, CraftedItem(1, "Fine Iron Blade"));
        var inFlight = StagedParty(ImmutableList<FloorOutcome>.Empty); // Party is HeroId(1) already
        var state = World() with
        {
            Phase = DayPhase.Camp, Heroes = heroes, Items = items, InFlight = ImmutableList.Create(inFlight),
        };

        var card = JourneyStream.Build(state, ImmutableList<GameEvent>.Empty).Single();

        AssertThat(card.Manifest.Any(m => m.Text == "Torvald carries your Fine Iron Blade.")).IsTrue();
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
