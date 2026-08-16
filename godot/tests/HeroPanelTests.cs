#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
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

    // ── Decision cards (U6) ──────────────────────────────────────────────────────────────────
    // HeroDecisionExplained is stamped in two places with zero client reader before this unit:
    // HeroShoppingSystem (chosen gear vs. runner-up, gated to decisions that touched the
    // player's own shelf) and MusterSystem (a bounty overriding a party's default target
    // floor). Both share one event shape, rendered here per hero — this panel's own doc calls
    // it "the guild hall the heroes drink and muster in", so it is the muster surface too.

    [TestCase]
    public void SixHeroesShopping_WithStagedStock_RenderSixExplanations_NeverOnTheTicker()
    {
        // VOLUME correction: HeroShoppingSystem only stamps a card when the player's OWN shelf
        // was on one side of the decision or the other (HeroShoppingSystem.cs:213-216) — an
        // empty rival shelf plus six identical player-shelf weapons (every starting hero's
        // GearSet is empty, so any weapon is a strict upgrade) makes EVERY one of the six
        // starting heroes' purchases a player-shelf decision, guaranteeing six cards rather than
        // hoping a hand-wavy scenario happens to produce them.
        var ui = MountMainUi(new SimAdapter(SixHeroesWithStagedShelfState()));
        try
        {
            // U3 (tutorial-revamp plan, §11.13): HeroCards is now SurfaceUnlocks-gated (opens on
            // the sim's first player-shelf sale) — MainUi.OpenPanel's own defense-in-depth guard
            // (added the same wave, for any caller reaching the router without going through the
            // tray button's Disabled state) refuses a still-gated open with a bell toast instead
            // of opening the drawer. Opening BEFORE the tick below — while nobody has bought
            // anything yet — used to be harmless staging order; now it is a refused no-op, so the
            // drawer never actually opens and never gets built with today's decisions. Tick FIRST
            // (this fixture's 6 sales earn the gate for real, the same way a player would), then
            // open — HeroPanel.DecisionsToday reads the durable GameState.EventLog filtered by
            // Day, not the tick's ephemeral LastEvents, so opening after the tick sees the exact
            // same six decisions either way.
            ui.Adapter.AdvancePhase(); // Morning: all six heroes shop the staged shelf, in order
            ui.OpenPanel("HeroCards");

            // Sanity: the staging actually produced six real player-shelf sales, not a no-op.
            var sold = ui.Adapter.LastEvents.OfType<ItemSold>().Count(e => e.FromPlayerShop);
            AssertThat(sold).IsEqual(6);
            AssertThat(ui.Adapter.LastEvents.OfType<HeroDecisionExplained>().Count()).IsEqual(6);

            var heroCardsText = RenderedText(ui.HeroCards);
            for (var i = 0; i < 6; i++)
            {
                AssertThat(heroCardsText).Contains($"Test Blade {i + 1}");
            }

            // Six distinct explanation lines actually rendered (not deduped/overwritten).
            AssertThat(heroCardsText.Split("‰ gap)").Length - 1).IsEqual(6);

            // PLACEMENT: never on the marquee — it fires per shopping hero every morning, which
            // would crowd the news above it out of a finite strip (the same reason
            // MarketShareShifted is a pinned ticker exclusion, AdventureTickerTests/
            // UnsilencedEventTests). The six ItemSold sales above DO legitimately add ticker
            // lines; this only asserts the DECISION cards never also land there.
            AssertThat(ui.Ticker.DisplayText).NotContains("‰ gap)");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HeroWhoseDecisionNeverTouchesThePlayerShelf_RendersNoExplanation()
    {
        // Pins HeroShoppingSystem.cs:213-216: an empty player shelf and a single rival-only
        // upgrade means the buying hero's decision never touches the player's own shelf on
        // either side (best = the rival item; no other Buy candidate exists for a runner-up).
        // The sim itself declines to stamp a card — this surface must not invent one.
        var ui = MountMainUi(new SimAdapter(CampaignWithOnlyRivalUpgradeState()));
        try
        {
            ui.OpenPanel("HeroCards");
            ui.Adapter.AdvancePhase(); // Morning: the first hero buys the rival item

            // Sanity: a sale did happen (this isn't "no explanation because nothing happened").
            AssertThat(ui.Adapter.LastEvents.OfType<ItemSold>().Any(e => !e.FromPlayerShop)).IsTrue();
            AssertThat(ui.Adapter.LastEvents.OfType<HeroDecisionExplained>().Count()).IsEqual(0);

            AssertThat(RenderedText(ui.HeroCards)).NotContains("◆");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GearDecision_NamesTheChosenItem_TheRunnerUp_AndTheScoreGap()
    {
        var decision = new HeroDecisionExplained(
            new HeroId(1), "Iron Sword", "Rusty Dagger", "upgrade: +5 gear score for 10g", GapPermille: 240)
        {
            Id = new EventId(1),
            Day = 5,
        };
        var ui = MountMainUi(new SimAdapter(
            WorldAtDay(5, ImmutableList.Create<GameEvent>(decision), MakeHero(1, "Ada"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("Iron Sword over Rusty Dagger");
            AssertThat(cardText).Contains("upgrade: +5 gear score for 10g");
            AssertThat(cardText).Contains("240‰ gap)");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GearDecision_WithNoRunnerUp_RendersHonestly_NoDanglingOverFragment()
    {
        var decision = new HeroDecisionExplained(
            new HeroId(1), "Iron Sword", "nothing else affordable", "upgrade: +5 gear score for 10g",
            GapPermille: 1000)
        {
            Id = new EventId(1),
            Day = 5,
        };
        var ui = MountMainUi(new SimAdapter(
            WorldAtDay(5, ImmutableList.Create<GameEvent>(decision), MakeHero(1, "Bram"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_1"));
            AssertThat(cardText).Contains("Iron Sword over nothing else affordable");
            AssertThat(cardText).NotContains("over  "); // no dangling double-space from a blank runner-up
            AssertThat(cardText).NotContains("over :"); // no dangling empty-runner-up artifact
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void MusterOverride_BountyDecision_RendersOnTheHeroCard()
    {
        // The MusterSystem variant: same event shape, but Chosen/RunnerUp name floors, not
        // items — pins that the reader never assumed one or the other.
        var decision = new HeroDecisionExplained(
            new HeroId(2), "floor 3 (bounty)", "floor 2 (deepest reached + 1)",
            "the party's accepted bounty overrode the usual depth-based target floor", GapPermille: 200)
        {
            Id = new EventId(1),
            Day = 5,
        };
        var ui = MountMainUi(new SimAdapter(
            WorldAtDay(5, ImmutableList.Create<GameEvent>(decision), MakeHero(2, "Coen"))));
        try
        {
            var cardText = RenderedText(Find<PanelContainer>(ui.HeroCards, "HeroCard_2"));
            AssertThat(cardText).Contains("floor 3 (bounty) over floor 2 (deepest reached + 1)");
            AssertThat(cardText).Contains("bounty overrode the usual depth-based target floor");
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

    /// <summary>The starting six-hero campaign with the player's shelf staged with one weapon
    /// per hero — every starting hero's <see cref="GearSet"/> is empty (see <c>HeroRoster</c>),
    /// so any weapon is a strict gear-score upgrade, and every hero can afford one at 10g (the
    /// starting cast's lowest gold is 30).
    ///
    /// <para>Value ratio is deliberately 8 gear-score per 10g (0.8) — <c>RivalRestockSystem</c>
    /// (Morning, registered ahead of <c>HeroShoppingSystem</c>) unconditionally re-mints the full
    /// <c>RivalCatalog</c> onto the rival shelf every morning regardless of what this fixture
    /// sets <c>RivalShelf</c> to, and every catalog line is priced at exactly statSum*2 (ratio
    /// 0.5 by construction — <c>RivalCatalog</c>'s own doc). 0.8 beats 0.5 outright for every
    /// hero (<see cref="ShoppingAi.IsBetterValue"/>'s cross-multiplication is not a near-tie), so
    /// which of these six or the freshly-minted rival items a hero prefers is never left to the
    /// ItemId tie-break — every hero's <c>best</c> is one of these six until they run out, which
    /// is what guarantees a player-shelf decision (see <see cref="HeroShoppingSystem"/>'s
    /// early-return) for all six heroes rather than most of them.</para>
    /// </summary>
    private static GameState SixHeroesWithStagedShelfState()
    {
        var baseState = GameComposition.NewCampaign(9601);
        var items = baseState.Items;
        var shelf = ImmutableList<ShelfEntry>.Empty;
        for (var i = 0; i < 6; i++)
        {
            var itemId = new ItemId(9600 + i);
            var item = new Item(
                itemId, "test-decision-explained", $"Test Blade {i + 1}", ItemSlot.Weapon,
                QualityGrade.Common, new ItemStats(Attack: 8, Defense: 0, Weight: 1),
                new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
            items = items.Add(itemId.Value, item);
            shelf = shelf.Add(new ShelfEntry(itemId, 10));
        }

        return baseState with
        {
            Items = items,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Player = baseState.Player with { Shelf = shelf },
        };
    }

    /// <summary>The starting six-hero campaign with an EMPTY player shelf and a single
    /// rival-only weapon — the first hero to shop buys it, and nobody else has anything left to
    /// consider, so no decision ever touches the player's own shelf.</summary>
    private static GameState CampaignWithOnlyRivalUpgradeState()
    {
        var baseState = GameComposition.NewCampaign(9602);
        var itemId = new ItemId(9700);
        var item = new Item(
            itemId, "test-rival-only-upgrade", "Rival Test Blade", ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(Attack: 5, Defense: 0, Weight: 1),
            Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = baseState.Items.Add(itemId.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList<ShelfEntry>.Empty },
            RivalShelf = ImmutableList.Create(new ShelfEntry(itemId, 10)),
        };
    }
}
#endif
