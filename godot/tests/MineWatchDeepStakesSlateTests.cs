#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-LONG-27 ("the Deep vigil gets a stakes slate", link3): the <see
/// cref="DayPhase.ExpeditionDeep"/> phase used to show nothing new — the same marching figures and
/// stage-1 beats Camp already showed, replaying (<c>docs/design/THE-GAME.md</c> used to say so in
/// plain words). <see cref="MineWatch.DeepStakesSlateLines"/> is the fix: who is below, which
/// floor they are bound for, which of the player's own work each of them carries, and whose depth
/// record stands — words only, never a percentage, never a control to press.
///
/// <para>Every scenario below is phrased against the PROPERTY (iterate party shapes, construct
/// mixed crafted/rival gear) rather than one hand-picked instance — this repo's own "a guard
/// naming one literal stops covering its family the moment the family grows" lesson, paid for four
/// times already per <c>CLAUDE.md</c>.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MineWatchDeepStakesSlateTests
{
    // ── the property: every party shape names exactly who is below, nobody else ────────────────

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void NamesEveryHeroBelow_AndNobodyElse_ForEveryPartySize(int partySize)
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var (state, camp) = WorldWithCampedParty(partySize);

            watch.Refresh(
                state with { Phase = DayPhase.ExpeditionDeep, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            var lines = watch.DeepStakesSlateLines;
            for (var i = 1; i <= partySize; i++)
            {
                var name = $"Hero{i}";
                AssertThat(lines.Any(l => l.Contains(name) && l.Contains("bound for floor")))
                    .OverrideFailureMessage(
                        $"party size {partySize}: no 'bound for floor' line named {name}. Lines: " +
                        string.Join(" | ", lines))
                    .IsTrue();
            }

            // Every OTHER hero in town (real, in state.Heroes, just not on this delve) must never
            // appear on the card — the property that matters, not just "the roster we listed".
            for (var i = partySize + 1; i <= 3; i++)
            {
                var strayName = $"Hero{i}";
                AssertThat(lines.Any(l => l.Contains(strayName)))
                    .OverrideFailureMessage($"a hero not below ({strayName}) was named on the Deep stakes slate.")
                    .IsFalse();
            }
        }
        finally
        {
            watch.Free();
        }
    }

    // ── link1, the test that matters most: attribution follows the mark, not mere possession ────

    [TestCase]
    public void PlayerCraftedItem_AttributedToItsBearer_RivalItem_NeverAttributed()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var craftedId = new ItemId(1);
            var rivalId = new ItemId(2);
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", new GearSet(craftedId, null, null)))
                .Add(2, Delver(2, "Elowen", new GearSet(rivalId, null, null)));
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(craftedId, "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty))
                .Add(2, new Item(rivalId, "recipe", "Rival Shield", ItemSlot.Shield, QualityGrade.Common,
                    new ItemStats(0, 1, 0), Mark: null, History: ImmutableList<ItemHistoryEntry>.Empty));
            var camp = CampOf(new HeroId(1), new HeroId(2));
            var state = GameFactory.NewGame(9201) with { Heroes = heroes, Items = items };

            watch.Refresh(
                state with { Phase = DayPhase.ExpeditionDeep, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            var lines = watch.DeepStakesSlateLines;
            AssertThat(lines.Any(l => l.Contains("Torvald") && l.Contains("Fine Iron Blade")))
                .OverrideFailureMessage("the player-crafted item was not attributed to its bearer.")
                .IsTrue();
            AssertThat(lines.Any(l => l.Contains("Rival Shield")))
                .OverrideFailureMessage("a rival (non-player-crafted) item was attributed on the slate.")
                .IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void NobodyBelowCarriesPlayerCraftedGear_HonestEmptyState_NeverFabricated()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", GearSet.Empty));
            var camp = CampOf(new HeroId(1));
            var state = GameFactory.NewGame(9202) with { Heroes = heroes };

            watch.Refresh(
                state with { Phase = DayPhase.ExpeditionDeep, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            AssertThat(watch.DeepStakesSlateLines.Any(l => l.Contains("Nobody below carries anything you forged.")))
                .IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    // ── §11.7.4's own guard: stakes are recorded facts, never a percentage/odds/chance ───────────

    private static readonly Regex ProbabilityLanguage = new(
        @"%|chance|odds|probability|likely|unlikely|risk\w*|\d+\s*(?:/|:)\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [TestCase]
    public void NeverReadsAsAPercentage_OddsOrProbability()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", new GearSet(new ItemId(1), null, null)))
                .Add(2, Delver(2, "Elowen", new GearSet(null, null, null)))
                .Add(3, Delver(3, "Brask", new GearSet(null, null, new ItemId(2))));
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(new ItemId(1), "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty))
                .Add(2, new Item(new ItemId(2), "recipe", "Fine Iron Plate", ItemSlot.Armor, QualityGrade.Fine,
                    new ItemStats(0, 2, 0), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty));
            var camp = CampOf(new HeroId(1), new HeroId(2), new HeroId(3));
            var state = GameFactory.NewGame(9203) with { Heroes = heroes, Items = items };

            watch.Refresh(
                state with { Phase = DayPhase.ExpeditionDeep, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            var lines = watch.DeepStakesSlateLines;
            AssertThat(lines.Count).IsGreater(0); // fixture guard: an empty card proves nothing below
            foreach (var line in lines)
            {
                AssertThat(ProbabilityLanguage.IsMatch(line))
                    .OverrideFailureMessage($"Deep stakes slate line reads as a likelihood, not a recorded fact: \"{line}\"")
                    .IsFalse();
            }
        }
        finally
        {
            watch.Free();
        }
    }

    // ── §11.7.4's other guard: no verb, no press, anywhere in this card ───────────────────────────

    [TestCase]
    public void CardIsPressableNowhere_NoBaseButtonAnywhereInIt()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", new GearSet(new ItemId(1), null, null)));
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(new ItemId(1), "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty));
            var camp = CampOf(new HeroId(1));
            var state = GameFactory.NewGame(9204) with { Heroes = heroes, Items = items };

            watch.Refresh(
                state with { Phase = DayPhase.ExpeditionDeep, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            var slate = Find<PanelContainer>(watch, "DeepStakesSlate");
            AssertThat(slate.Visible).IsTrue(); // fixture guard: checking an absent card proves nothing

            var pressables = ScreenObservation.Descendants(slate).OfType<BaseButton>().ToList();
            AssertThat(pressables.Count)
                .OverrideFailureMessage(
                    "the Deep stakes slate contains a pressable control — §11.7.4 forbids any verb here: "
                    + string.Join(", ", pressables.Select(b => b.Name)))
                .IsEqual(0);
        }
        finally
        {
            watch.Free();
        }
    }

    // ── negative control: the card exists ONLY at the Deep phase, with a real vigil below ────────

    [TestCase]
    public void Absent_DuringExpeditionPhase()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var (state, camp) = WorldWithCampedParty(2);
            watch.Refresh(state with { Phase = DayPhase.Expedition, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            AssertSlateAbsent(watch);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Absent_DuringCampPhase_EvenWithARealVigil()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var (state, camp) = WorldWithCampedParty(2);
            watch.Refresh(state with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            AssertSlateAbsent(watch);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Absent_DuringEveningPhase()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var (state, camp) = WorldWithCampedParty(2);
            watch.Refresh(state with { Phase = DayPhase.Evening, InFlight = ImmutableList.Create(camp) },
                ImmutableList<GameEvent>.Empty);

            AssertSlateAbsent(watch);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Absent_DuringMorningPhase_NoPartyKnownYet()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = GameFactory.NewGame(9205);
            watch.Refresh(state with { Phase = DayPhase.Morning }, ImmutableList<GameEvent>.Empty);

            AssertSlateAbsent(watch);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Absent_AtDeepPhase_WhenNobodyIsActuallyCamped()
    {
        // repo task #67's own case: the whole trip already resolved at the Expedition tick, so
        // InFlight is empty even though Phase == ExpeditionDeep (AlreadyBackThisCycle == true) —
        // nobody is actually below, so the card must not claim anyone is.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var result = new ExpeditionResult(
                Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 1, DeepestFloorCleared: 1,
                Floors: ImmutableList<FloorOutcome>.Empty, Survivors: ImmutableList.Create(new HeroId(1)),
                Deaths: ImmutableList<HeroId>.Empty, Beats: ImmutableList<AttributionBeat>.Empty,
                Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);
            var state = GameFactory.NewGame(9206) with
            {
                Phase = DayPhase.ExpeditionDeep,
                PendingExpeditions = ImmutableList.Create(result),
            };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.AlreadyBackThisCycle).IsTrue(); // fixture guard: proves the scenario is real
            AssertSlateAbsent(watch);
        }
        finally
        {
            watch.Free();
        }
    }

    private static void AssertSlateAbsent(MineWatch watch)
    {
        AssertThat(watch.DeepStakesSlateLines.IsEmpty)
            .OverrideFailureMessage(
                "the Deep stakes slate carried lines outside DayPhase.ExpeditionDeep: "
                + string.Join(" | ", watch.DeepStakesSlateLines))
            .IsTrue();
        var slate = Find<PanelContainer>(watch, "DeepStakesSlate");
        AssertThat(slate.Visible).IsFalse();
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private static Hero Delver(int id, string name, GearSet gear, int deepestFloor = 1) => new(
        new HeroId(id), name, "vanguard", Level: 3, MaxHp: 40, Gold: 10,
        gear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: deepestFloor, DiedOnDay: null);

    private static InFlightExpedition CampOf(params HeroId[] party) => new(
        Party: party.ToImmutableList(), TargetFloor: 4, CheckpointFloor: 2, VenueId: "mine",
        Hp: ImmutableSortedDictionary<int, int>.Empty, Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
        Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
        Floors: ImmutableList<FloorOutcome>.Empty, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 2);

    /// <summary>Three real heroes always live in <c>state.Heroes</c> (town-wide roster), but only
    /// the first <paramref name="partySize"/> of them are on THIS delve — the fixture the property
    /// test above needs to prove "nobody who is not below" against a hero who genuinely exists,
    /// not merely one that was never created.</summary>
    private static (GameState State, InFlightExpedition Camp) WorldWithCampedParty(int partySize)
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty;
        var items = ImmutableSortedDictionary<int, Item>.Empty;
        var partyIds = new List<HeroId>();

        for (var i = 1; i <= 3; i++)
        {
            var onThisDelve = i <= partySize;
            var weaponId = new ItemId(100 + i);
            heroes = heroes.Add(i, Delver(i, $"Hero{i}", new GearSet(onThisDelve ? weaponId : null, null, null), deepestFloor: i));

            if (onThisDelve)
            {
                items = items.Add(100 + i, new Item(weaponId, "recipe", $"Blade{i}", ItemSlot.Weapon,
                    QualityGrade.Fine, new ItemStats(1, 0, 1), new MakersMark("Player", 1),
                    ImmutableList<ItemHistoryEntry>.Empty));
                partyIds.Add(new HeroId(i));
            }
        }

        var camp = CampOf(partyIds.ToArray());
        var state = GameFactory.NewGame((ulong)(9200 + partySize)) with { Heroes = heroes, Items = items };
        return (state, camp);
    }
}
#endif
