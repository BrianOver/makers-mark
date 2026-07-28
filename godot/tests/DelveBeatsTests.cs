#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// A1 (plan <c>2026-07-28-001</c> Part 2): <see cref="DelveBeats"/> is the pure, engine-free
/// projection the beat-driven delve stage (MineWatch upgrade, A2) will render from — same
/// technique and fixture style as <c>JourneyStreamTests</c> (pure C#, no Godot runtime, no
/// GODOT_BIN needed), against hand-built <see cref="FloorOutcome"/>/<see cref="CombatEvent"/>
/// fixtures. Covers the KTD5/R17/AE2 death-censor pin (the highest-value test here), the ≤3
/// Exchange-beat fight collapse, ore-on-cleared-floors, halt→Surface mapping, and recorded
/// floor-asc→HeroId→round emission order.
/// </summary>
[TestSuite]
public class DelveBeatsTests
{
    [TestCase]
    public void DeathRound_EmitsSwallowedByDark_NoHpAfterEntry_AndNeverLeaksTheFatalDamageNumber()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null),
                new CombatEvent(1, new HeroId(2), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))),
            new FloorOutcome(2, false, ImmutableList.Create(
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(1), 0, 10, false, null),
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(9), 0, 40, false, null), // fatal round
                new CombatEvent(2, new HeroId(2), "tunnel-spider", ImmutableList.Create(3), 6, 0, true, null))));
        var deaths = ImmutableList.Create(new HeroId(1));
        var heroes = Heroes();

        var beats = DelveBeats.BuildBeats(floors, deaths, ImmutableList<OreLoot>.Empty, heroes, ExpeditionHalt.FloorLost);

        var h1Beats = beats.Where(b => b.Hero == new HeroId(1) && b.Floor == 2).ToImmutableList();
        AssertThat(h1Beats.Count(b => b.Kind == DelveBeatKind.SwallowedByDark)).IsEqual(1);
        var cloud = h1Beats.Single(b => b.Kind == DelveBeatKind.SwallowedByDark);
        AssertThat(cloud.Clouded).IsTrue();
        AssertThat(cloud.HpAfter.ContainsKey(1)).IsFalse(); // no HP/corpse reveal for the dead hero

        // The fatal round's real damage number (40) never renders anywhere in the stream.
        AssertThat(beats.Any(b => b.DamageTaken == 40)).IsFalse();
        // The prior, non-fatal round (10 dmg) DID render — JourneyStream shows the buildup, only
        // hides the last hit.
        AssertThat(beats.Any(b => b.Hero == new HeroId(1) && b.DamageTaken == 10)).IsTrue();

        // Hero 1 never reappears in any later beat's HpAfter (self-censored going forward).
        var afterCloudIndex = beats.IndexOf(cloud);
        AssertThat(beats.Skip(afterCloudIndex + 1).All(b => !b.HpAfter.ContainsKey(1))).IsTrue();

        // A survivor's HpAfter is untouched by the other hero's death.
        AssertThat(beats.Last(b => b.Kind == DelveBeatKind.MonsterSlain).HpAfter.ContainsKey(2)).IsTrue();
    }

    [TestCase]
    public void FightCollapse_AtMostThreeExchangeBeats_ButKeepsEveryQuaffAndTheKill()
    {
        var uses = ImmutableList.Create(new ConsumableUse(new ItemId(9), Round: 4, HpBefore: 15, HpAfter: 30));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "ore-golem", ImmutableList.Create(1), 2, 3, false, null),  // round1: first blood
                new CombatEvent(1, new HeroId(1), "ore-golem", ImmutableList.Create(2), 1, 1, false, null),  // round2
                new CombatEvent(1, new HeroId(1), "ore-golem", ImmutableList.Create(3), 0, 8, false, null),  // round3: worst wound
                new CombatEvent(1, new HeroId(1), "ore-golem", ImmutableList.Create(4), 4, 0, false, null) { Uses = uses }, // round4: quaff
                new CombatEvent(1, new HeroId(1), "ore-golem", ImmutableList.Create(5), 10, 0, true, null)))); // round5: resolution/kill
        var heroes = Heroes();

        var beats = DelveBeats.BuildBeats(floors, ImmutableList<HeroId>.Empty, ImmutableList<OreLoot>.Empty, heroes, ExpeditionHalt.TargetReached);

        AssertThat(beats.Count(b => b.Kind == DelveBeatKind.Exchange)).IsLessEqual(3);
        AssertThat(beats.Count(b => b.Kind == DelveBeatKind.Quaff)).IsEqual(1);
        AssertThat(beats.Count(b => b.Kind == DelveBeatKind.MonsterSlain)).IsEqual(1);
        // The kill round's damage is preserved on the MonsterSlain beat.
        AssertThat(beats.Single(b => b.Kind == DelveBeatKind.MonsterSlain).DamageDealt).IsEqual(10);
        // Worst-wound round (8 dmg taken) is one of the surviving Exchange beats.
        AssertThat(beats.Any(b => b.Kind == DelveBeatKind.Exchange && b.DamageTaken == 8)).IsTrue();
    }

    [TestCase]
    public void OreBeats_AppearOnClearedFloors_NoneOnAnUnclearedFloor()
    {
        var loot = ImmutableList.Create(
            new OreLoot(new HeroId(1), "iron-ore", 2),
            new OreLoot(new HeroId(2), "iron-ore", 3));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null),
                new CombatEvent(1, new HeroId(2), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))),
            new FloorOutcome(2, false, ImmutableList.Create(
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(1), 2, 6, false, null))));
        var heroes = Heroes();

        var beats = DelveBeats.BuildBeats(floors, ImmutableList<HeroId>.Empty, loot, heroes, ExpeditionHalt.FloorLost);

        var oreBeats = beats.Where(b => b.Kind == DelveBeatKind.OreFound).ToImmutableList();
        AssertThat(oreBeats.Count).IsEqual(2);
        AssertThat(oreBeats.All(b => b.Floor == 1)).IsTrue(); // none attributed to the uncleared floor 2
        AssertThat(oreBeats.Select(b => b.DamageDealt).OrderBy(q => q)).IsEqual(new[] { 2, 3 }); // quantity carried in DamageDealt
    }

    [TestCase]
    public void Halt_TargetReached_EndsWithSurfaceBeat() => AssertEndsWithSurface(ExpeditionHalt.TargetReached);

    [TestCase]
    public void Halt_GateHeld_EndsWithSurfaceBeat() => AssertEndsWithSurface(ExpeditionHalt.GateHeld);

    [TestCase]
    public void Halt_FloorLost_EndsWithSurfaceBeat() => AssertEndsWithSurface(ExpeditionHalt.FloorLost);

    [TestCase]
    public void Halt_TooHurt_EndsWithSurfaceBeat() => AssertEndsWithSurface(ExpeditionHalt.TooHurt);

    [TestCase]
    public void Halt_Recalled_EndsWithSurfaceBeat() => AssertEndsWithSurface(ExpeditionHalt.Recalled);

    [TestCase]
    public void Halt_PartyWiped_NeverEmitsASurfaceBeat_NobodyLeftToSurface()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, false, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "ore-golem", ImmutableList.Create(9), 0, 40, false, null))));
        var beats = DelveBeats.BuildBeats(
            floors, ImmutableList.Create(new HeroId(1)), ImmutableList<OreLoot>.Empty, Heroes(), ExpeditionHalt.PartyWiped);

        AssertThat(beats.Any(b => b.Kind == DelveBeatKind.Surface)).IsFalse();
        AssertThat(beats[^1].Kind).IsEqual(DelveBeatKind.SwallowedByDark); // the story ends on the cloud beat
    }

    private static void AssertEndsWithSurface(ExpeditionHalt halt)
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))));
        var beats = DelveBeats.BuildBeats(floors, ImmutableList<HeroId>.Empty, ImmutableList<OreLoot>.Empty, Heroes(), halt);

        AssertThat(beats[^1].Kind).IsEqual(DelveBeatKind.Surface);
    }

    [TestCase]
    public void InFlightExpedition_StagedParty_EndsWithCampBeat_NeverSurface()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))));
        var camp = new InFlightExpedition(
            Party: ImmutableList.Create(new HeroId(1)),
            TargetFloor: 3,
            CheckpointFloor: 1,
            VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 40),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: floors,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);

        var beats = DelveBeats.Build(camp, Heroes());

        AssertThat(beats[^1].Kind).IsEqual(DelveBeatKind.Camp);
        AssertThat(beats.Any(b => b.Kind == DelveBeatKind.Surface)).IsFalse();
    }

    [TestCase]
    public void ExpeditionResult_Wrapper_MatchesCoreBuildBeats_ForItsOwnHalt()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))));
        var result = new ExpeditionResult(
            Party: ImmutableList.Create(new HeroId(1)),
            TargetFloor: 1,
            DeepestFloorCleared: 1,
            Floors: floors,
            Survivors: ImmutableList.Create(new HeroId(1)),
            Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty,
            Halt: ExpeditionHalt.TargetReached);

        var viaWrapper = DelveBeats.Build(result, Heroes());
        var viaCore = DelveBeats.BuildBeats(result.Floors, result.Deaths, result.Loot, Heroes(), result.Halt);

        AssertThat(viaWrapper.Select(b => b.Kind).SequenceEqual(viaCore.Select(b => b.Kind))).IsTrue();
    }

    [TestCase]
    public void BeatOrder_MatchesRecordedOrder_FloorAscThenHeroThenRound_NeverReSorted()
    {
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(2), "cave-rat", ImmutableList.Create(3), 5, 0, true, null), // hero 2 fights first
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))),
            new FloorOutcome(2, true, ImmutableList.Create(
                new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(3), 5, 0, true, null),
                new CombatEvent(2, new HeroId(2), "tunnel-spider", ImmutableList.Create(3), 5, 0, true, null))));
        var beats = DelveBeats.BuildBeats(floors, ImmutableList<HeroId>.Empty, ImmutableList<OreLoot>.Empty, Heroes(), ExpeditionHalt.TargetReached);

        AssertThat(string.Join(",", beats.Select(b => b.Floor))).IsEqual(string.Join(",", beats.Select(b => b.Floor).OrderBy(f => f)));

        // Within floor 1, hero 2's beats all precede hero 1's beats (recorded order preserved,
        // never re-sorted by HeroId despite hero 2 fighting first).
        var floor1 = beats.Where(b => b.Floor == 1 && b.Hero is not null).ToImmutableList();
        var firstHero1Index = floor1.ToList().FindIndex(b => b.Hero == new HeroId(1));
        var lastHero2Index = floor1.ToList().FindLastIndex(b => b.Hero == new HeroId(2));
        AssertThat(lastHero2Index).IsLess(firstHero1Index);
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    private static ImmutableSortedDictionary<int, Hero> Heroes() =>
        ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "H1"))
            .Add(2, Delver(2, "H2"));

    private static Hero Delver(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);
}
#endif
