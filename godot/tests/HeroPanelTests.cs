#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Gap-closing unit (2026-07): two sim systems computed narration-specific signals with zero
/// client reader before this — <c>NeedsSystem.Snapshot</c> (B4: unmet-demand streak, telegraph,
/// boycott, recovery) and <c>RelationshipSystem.TopEdgesFor</c> (B3: per-pair comrade/grief/
/// grudge/rivalry edges). Both are now rendered as chips on <see cref="HeroPanel"/>'s existing
/// card idiom, next to the established Standing chip. These scenarios drive
/// <see cref="MainUi.HeroCards"/> off hand-built <see cref="GameState"/>s that pin each Needs
/// crossing state and a relationship edge, plus the day-1 honest-empty-state case for both.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroPanelTests
{
    // ── Needs / boycott (B4) ─────────────────────────────────────────────────────────────────

    [TestCase]
    public void Day1FreshHero_NoUnmetDemandStreak_RendersNoNeedsChip()
    {
        // Arrival day defaults to day 1 for a hero with no RecruitArrived event (NeedsSystem's
        // own fallback) — on day 1 itself the streak is 0, nowhere near the telegraph threshold.
        var ui = MountMainUi(new SimAdapter(WorldAtDay(1, ImmutableList<GameEvent>.Empty, MakeHero(1, "Solo"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("Solo");
            AssertThat(cardText).NotContains("Needs");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SteadyTelegraphedStreak_RendersRestlessChip()
    {
        // No purchase ever, arrival day 1: streak = Day - 1. Day 6 => streak 5 (>=4 telegraph,
        // <6 boycott) and yesterday's streak (day 5 => 4) was ALSO telegraphed, so this is the
        // steady state, not the "just crossed" moment.
        var ui = MountMainUi(new SimAdapter(WorldAtDay(6, ImmutableList<GameEvent>.Empty, MakeHero(1, "Nia"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("Needs");
            AssertThat(cardText).Contains("restless");
            AssertThat(cardText).NotContains("growing restless");
            AssertThat(cardText).NotContains("boycott");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StreakJustCrossesTelegraphThreshold_RendersGrowingRestlessChip()
    {
        // Day 5 => streak 4 (just crossed); yesterday (day 4 => streak 3) was still content —
        // the "just telegraphed today" moment, distinct from the steady restless state above.
        var ui = MountMainUi(new SimAdapter(WorldAtDay(5, ImmutableList<GameEvent>.Empty, MakeHero(1, "Nia"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("growing restless");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SteadyBoycott_RendersBoycottingChip()
    {
        // No purchase ever, arrival day 1: Day 8 => streak 7 (>=6 boycott); yesterday (day 7 =>
        // streak 6) was ALSO boycotting — steady state, not the first day of the boycott.
        var ui = MountMainUi(new SimAdapter(WorldAtDay(8, ImmutableList<GameEvent>.Empty, MakeHero(1, "Torvi"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("boycotting");
            AssertThat(cardText).NotContains("just started boycotting");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StreakJustCrossesBoycottThreshold_RendersJustStartedBoycottingChip()
    {
        // Day 7 => streak 6 (exactly at the boycott threshold); yesterday (day 6 => streak 5)
        // was telegraphed but not yet boycotting — the boycott's first day.
        var ui = MountMainUi(new SimAdapter(WorldAtDay(7, ImmutableList<GameEvent>.Empty, MakeHero(1, "Torvi"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("just started boycotting");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReturnAfterATelegraphedDrought_RendersRecoveryChip_TheMorningAfter()
    {
        // Mirrors sim/GameSim.Tests/Heroes/NeedsRecoveryTests.cs's own recipe: bought on day 8
        // after a long drought (streak as-of day 7 = 6 >= telegraph threshold), observed at day 9
        // ("tomorrow's Morning" — every real caller reads this post-Evening-tick).
        var sold = new ItemSold(new ItemId(1), new HeroId(1), 50, FromPlayerShop: true) { Id = new EventId(1), Day = 8 };
        var ui = MountMainUi(new SimAdapter(
            WorldAtDay(9, ImmutableList.Create<GameEvent>(sold), MakeHero(1, "Bram"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("back at the counter");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Relationships (B3) ───────────────────────────────────────────────────────────────────

    [TestCase]
    public void SoloHero_NoSharedHistory_RendersNoRelationshipChip()
    {
        var ui = MountMainUi(new SimAdapter(
            WorldAtDay(1, ImmutableList<GameEvent>.Empty, MakeHero(1, "Solo"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).NotContains("Bond");
            AssertThat(cardText).NotContains("Grudge");
            AssertThat(cardText).NotContains("Rivalry");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SharedExpedition_RendersComradeBondChip_NamingTheOtherHero()
    {
        var departed = new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2)), TargetFloor: 1)
        {
            Id = new EventId(1),
            Day = 5,
        };
        var ui = MountMainUi(new SimAdapter(WorldAtDay(
            6, ImmutableList.Create<GameEvent>(departed), MakeHero(1, "Aria"), MakeHero(2, "Bram"))));
        try
        {
            var ariaText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(ariaText).Contains("Bond");
            AssertThat(ariaText).Contains("Bram");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OutbidPair_RendersGrudgeChip_NamingTheOtherHero()
    {
        var missed = new HeroPassedOnItem(new HeroId(3), new ItemId(1), "too pricey")
        {
            Id = new EventId(1),
            Day = 5,
        };
        var sold = new ItemSold(new ItemId(1), new HeroId(4), 20, FromPlayerShop: true)
        {
            Id = new EventId(2),
            Day = 5,
        };
        var ui = MountMainUi(new SimAdapter(WorldAtDay(
            6, ImmutableList.Create<GameEvent>(missed, sold), MakeHero(3, "Coen"), MakeHero(4, "Dagny"))));
        try
        {
            var coenText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_3"));
            AssertThat(coenText).Contains("Grudge");
            AssertThat(coenText).Contains("Dagny");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Hero MakeHero(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 1, MaxHp: 25, Gold: 20,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameState WorldAtDay(int day, ImmutableList<GameEvent> log, params Hero[] heroes) =>
        GameFactory.NewGame(9001) with
        {
            Day = day,
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            EventLog = log,
        };
}
#endif
