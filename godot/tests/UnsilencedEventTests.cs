#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Venues;
using GdUnit4;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Coverage for a class of defect this project keeps rediscovering: a sim system that computes
/// correctly and is then dropped before it reaches a pixel. An audit found ten event types firing
/// into <see cref="AdventureTicker"/>'s allow-list and falling straight through its <c>_ => null</c>
/// arm, plus a <see cref="CampaignEnded"/> event that carried purpose-built chronicle tallies and
/// had no reader at all — the campaign could end and the player would never be told.
///
/// <para>These tests assert the SURFACING, not the sim: each one proves a formerly-dropped event
/// now produces player-visible text. They deliberately also pin the two exclusions
/// (<see cref="SupplyDelivered"/>, <see cref="MarketShareShifted"/>), because those were judgment
/// calls — a future reader deserves to see they were decided rather than missed, and a test is the
/// only place that survives.</para>
///
/// <para>Driven directly against the components with hand-built state, following
/// <see cref="AdventureTickerTests"/>' established technique: deterministic and fast, and it can
/// construct event shapes a real 40-day campaign would take minutes to reach.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UnsilencedEventTests
{
    [TestCase]
    public void CommissionLifecycle_AllThreePhases_Render()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var events = ImmutableList.Create<GameEvent>(
                new CommissionPosted(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 7, PremiumGold: 30),
                new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 30),
                new CommissionExpired(new HeroId(2), ItemSlot.Shield));

            ticker.OnPhaseCompleted(DayPhase.Morning, completedDay: 3, state, events);

            AssertThat(ticker.Lines.Count).IsEqual(3);
            AssertThat(ticker.DisplayText).Contains("V1 wants Weapon work, Fine or better, by day 7");
            AssertThat(ticker.DisplayText).Contains("30g over list");
            AssertThat(ticker.DisplayText).Contains("V1 takes delivery of Dagger");
            AssertThat(ticker.DisplayText).Contains("S1 gave up waiting on that Shield commission.");
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void ConfidenceSpiral_EdgeTriggeredWarnings_Render()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var events = ImmutableList.Create<GameEvent>(
                new RivalExpansionTriggered(ConfidencePermille: 340),
                new HeroConsideringLeaving(new HeroId(2), ConfidencePermille: 180),
                new TownConfidenceCollapsed(MissedAssessments: 3),
                new RecruitArrived(new HeroId(1)));

            ticker.OnPhaseCompleted(DayPhase.Evening, completedDay: 12, state, events);

            AssertThat(ticker.Lines.Count).IsEqual(4);
            AssertThat(ticker.DisplayText).Contains("confidence has slipped to 34%");
            AssertThat(ticker.DisplayText).Contains("S1 is talking about leaving town.");
            AssertThat(ticker.DisplayText).Contains("3 assessment(s) missed");
            AssertThat(ticker.DisplayText).Contains("V1 has come to town looking for work.");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>
    /// Every id in <c>DirectorSystem.Catalog</c> must have authored prose. The unknown-id arm exists
    /// so a future catalog entry degrades to something true rather than vanishing — that fallback is
    /// asserted too, since a silently-dropped incident is the exact bug this file is about.
    /// </summary>
    [TestCase]
    public void DirectorIncidents_EveryCatalogId_HasAuthoredProse()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var events = ImmutableList.Create<GameEvent>(
                Incident("whispers_in_the_dark", IncidentCategory.Rumor, IncidentMagnitude.Minor),
                Incident("goblin_probe", IncidentCategory.Skirmish, IncidentMagnitude.Minor),
                Incident("spider_brood_swells", IncidentCategory.Infestation, IncidentMagnitude.Notable),
                Incident("ghoul_warren_breaks", IncidentCategory.Breakout, IncidentMagnitude.Notable),
                Incident("the_forgeworm_stirs", IncidentCategory.Cataclysm, IncidentMagnitude.Severe));

            ticker.OnPhaseCompleted(DayPhase.Morning, completedDay: 5, state, events);

            AssertThat(ticker.Lines.Count).IsEqual(5);
            AssertThat(ticker.DisplayText).Contains("Whispers out of the dark");
            AssertThat(ticker.DisplayText).Contains("probed the mine mouth");
            AssertThat(ticker.DisplayText).Contains("spider brood is swelling");
            AssertThat(ticker.DisplayText).Contains("ghoul warren has broken open");
            AssertThat(ticker.DisplayText).Contains("forgeworm stirs");

            // No raw snake_case id ever reaches the player for a catalogued incident.
            AssertThat(ticker.DisplayText).NotContains("_");
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void UncataloguedIncident_DegradesToReadableLine_NeverVanishes()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Morning,
                completedDay: 1,
                StagedWorld(),
                ImmutableList.Create<GameEvent>(
                    Incident("a_brand_new_horror", IncidentCategory.Rumor, IncidentMagnitude.Minor)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("a brand new horror");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>
    /// The two deliberate exclusions. <see cref="SupplyDelivered"/> confirms the player's own camp
    /// action (CampPanel already shows it) and <see cref="MarketShareShifted"/> drifts every single
    /// Evening — in a finite marquee it would crowd out the news above. Pinned so the decision is
    /// visible rather than looking like an oversight.
    /// </summary>
    [TestCase]
    public void DeliberateExclusions_StaySilentInTheMarquee()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Evening,
                completedDay: 1,
                StagedWorld(),
                ImmutableList.Create<GameEvent>(
                    new SupplyDelivered(new HeroId(1), new ItemId(1), Fee: 5),
                    new MarketShareShifted(Permille: 120, RivalGained: true)));

            AssertThat(ticker.Lines.Count).IsEqual(0);
            AssertThat(ticker.DisplayText).IsEmpty();
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>
    /// The marquee's spam guard. A widened allow-list is exactly how a ticker becomes wallpaper, so
    /// prove the same event twice in one day yields one line.
    /// </summary>
    [TestCase]
    public void SameDayRepeat_IsDeduped_SoAWiderAllowListCannotSpam()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var evt = Incident("goblin_probe", IncidentCategory.Skirmish, IncidentMagnitude.Minor);
            ticker.OnPhaseCompleted(
                DayPhase.Morning, completedDay: 4, StagedWorld(), ImmutableList.Create<GameEvent>(evt, evt));

            AssertThat(ticker.Lines.Count).IsEqual(1);
        }
        finally
        {
            ticker.Free();
        }
    }

    // ── the ending chronicle ────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Chronicle_RendersTheEventsOwnTallies_NotDerivedState()
    {
        var scroll = new ChronicleScroll();
        try
        {
            var ending = new CampaignEnded(
                DeepestFloorReached: 9,
                MemorialCount: 2,
                HonoredMemorialCount: 1,
                AttributionBeatCount: 14,
                GossipHighlightCount: 6,
                LegendaryHeroCount: 1);

            scroll.ShowFor(ending);

            AssertThat(scroll.Visible).IsTrue();
            AssertThat(scroll.Shown).IsEqual(ending);

            // Every line starts hidden — the reveal is staged, so nothing is visible at t=0.
            AssertThat(scroll.RevealedCount).IsEqual(0);

            // Enough elapsed time to clear the title hold plus every line.
            for (var i = 0; i < 40; i++)
            {
                scroll.Tick(0.2);
            }

            AssertThat(scroll.RevealedCount).IsGreater(6); // 6 tallies (memorials > 0 adds one) + closer
        }
        finally
        {
            scroll.Free();
        }
    }

    /// <summary>
    /// A clean run — nobody died — must still read as an achievement rather than a blank row, and
    /// the "of those, honored" line must not appear when there are no memorials to honor.
    /// </summary>
    [TestCase]
    public void Chronicle_ZeroLossRun_ReadsAsEarned_AndSkipsTheHonoredLine()
    {
        var scroll = new ChronicleScroll();
        try
        {
            scroll.ShowFor(new CampaignEnded(
                DeepestFloorReached: 4,
                MemorialCount: 0,
                HonoredMemorialCount: 0,
                AttributionBeatCount: 0,
                GossipHighlightCount: 0,
                LegendaryHeroCount: 0));

            for (var i = 0; i < 40; i++)
            {
                scroll.Tick(0.2);
            }

            var text = VisibleText(scroll);
            AssertThat(text).Contains("every one of them came home");
            AssertThat(text).NotContains("farewell rite");
            AssertThat(text).Contains("The forge is still warm.");
        }
        finally
        {
            scroll.Free();
        }
    }

    private static string VisibleText(ChronicleScroll scroll)
    {
        var sb = new System.Text.StringBuilder();
        Walk(scroll, sb);
        return sb.ToString();

        static void Walk(Godot.Node node, System.Text.StringBuilder sb)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is Godot.Label label)
                {
                    sb.Append(label.Text).Append(' ');
                }

                Walk(child, sb);
            }
        }
    }

    private static IncidentFired Incident(string id, IncidentCategory category, IncidentMagnitude magnitude) =>
        new(id, category, magnitude, VenueRegistry.MineId, TensionAfter: 100);

    private static GameState StagedWorld()
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "V1", "vanguard"))
            .Add(2, Delver(2, "S1", "striker"));
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, Dagger());
        return GameFactory.NewGame(9098) with { Heroes = heroes, Items = items };
    }

    private static Hero Delver(int id, string name, string classId) => new(
        new HeroId(id), name, classId, Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item Dagger() => new(
        new ItemId(1), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(4, 0, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
}
#endif
