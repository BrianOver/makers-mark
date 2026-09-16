#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Chronicle;
using GameSim.Contracts;
using GameSim.Factions;
using GameSim.Factions.Wardens;
using GameSim.Kernel;
using GameSim.Venues;
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Coverage for a class of defect this project keeps rediscovering: a sim system that computes
/// correctly and is then dropped before it reaches a pixel. An audit found ten event types firing
/// into <see cref="AdventureTicker"/>'s allow-list and falling straight through its <c>_ => null</c>
/// arm, plus a <see cref="CampaignEnded"/> event that carried purpose-built chronicle tallies and
/// had no reader at all — the campaign could end and the player would never be told.
///
/// <para>These tests assert the SURFACING, not the sim: each one proves a formerly-dropped event
/// now produces player-visible text. They deliberately also pin the remaining exclusions
/// (<see cref="SupplyDelivered"/>, <see cref="MarketShareShifted"/>'s active-recovery direction),
/// because those were judgment calls — a future reader deserves to see they were decided rather
/// than missed, and a test is the only place that survives. P2-HONEST-23 later narrowed
/// <see cref="MarketShareShifted"/>'s own exclusion to that one direction — see
/// <see cref="MarketShareShifted_IdleDay_NamesThePlayersOwnIdleDay"/> below for the half that now
/// speaks.</para>
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
    /// The remaining deliberate exclusions. <see cref="SupplyDelivered"/> confirms the player's own
    /// camp action (CampPanel already shows it), and <see cref="MarketShareShifted"/>'s
    /// active-recovery direction (<c>RivalGained: false</c>, any day that spent an action slot)
    /// drifts every single Evening the player actually works — in a finite marquee it would crowd
    /// out the news above, and it names no cost worth disclosing (working is the expected default,
    /// not a fee). The OTHER direction — <c>RivalGained: true</c>, the idle-day charge law 7
    /// requires be named — is no longer silent; see
    /// <see cref="MarketShareShifted_IdleDay_NamesThePlayersOwnIdleDay"/> below (P2-HONEST-23).
    ///
    /// <para><see cref="TariffApplied"/> (U5(b) ruling) joins them here rather than getting a
    /// renderer: it is the per-purchase price delta ONE buy's standing-at-the-time produced — like
    /// <see cref="SupplyDelivered"/>, confirmation of the player's OWN action (their own buy,
    /// already reflected in their own gold total and material count) rather than town news. The
    /// actual news — that the faction's standing itself crossed a line — is what
    /// <see cref="FactionStandingShifted"/> announces instead (see
    /// <c>FactionStanding_ThresholdCrossing_RendersExactlyOneLine_NamingTheFaction</c> below);
    /// voicing the per-buy arithmetic too would say the same fact twice in one marquee.</para>
    ///
    /// Pinned so every one of these decisions is visible rather than looking like an oversight.
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
                    new MarketShareShifted(Permille: 120, RivalGained: false),
                    new TariffApplied(FactionRegistry.DeepveinId, "copper", BaseLineCost: 100, PlayerCost: 90, Delta: -10)));

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

    // ── the idle-day cost (P2-HONEST-23, law 7) ────────────────────────────────────────────────
    // Law 7 ("skipping stays legal and its cost is named in copy, never engineered") had a live
    // gap: MarketShareSystem's idle-day charge (+150‰ toward the rival) was real and mechanically
    // enforced, but nothing ever told the player it happened or why. The two tests below cover
    // both directions the unit's own spec demands, plus the per-tick dedupe guard.

    /// <summary>
    /// P2-HONEST-23: the idle-day HALF of <see cref="MarketShareShifted"/> now speaks — see
    /// <see cref="AdventureTicker"/>'s FormatLine case and its neighbouring exclusion comment.
    /// Phrased against the PROPERTY, not one sentence (a copy rewrite must stay free): the line
    /// must name the CAUSE — the player's own idle day, addressed directly — not just repeat the
    /// EFFECT <c>ShopPanel.RivalEdgeGradient</c> already names ("The rival's edge is creeping
    /// up."). The active-recovery direction (<c>RivalGained: false</c>, any day that spent a slot)
    /// stays silent exactly as before — pinned by
    /// <see cref="DeliberateExclusions_StaySilentInTheMarquee"/> above.
    /// </summary>
    [TestCase]
    public void MarketShareShifted_IdleDay_NamesThePlayersOwnIdleDay()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Evening, completedDay: 6, StagedWorld(),
                ImmutableList.Create<GameEvent>(new MarketShareShifted(Permille: 350, RivalGained: true)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText.ToLowerInvariant().Contains("you"))
                .OverrideFailureMessage(
                    "The idle-day line must address the player directly (the CAUSE) rather than "
                    + $"only restating the rival's gain (the EFFECT). Line was \"{ticker.DisplayText}\".")
                .IsTrue();
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>The generic same-day dedupe (<see cref="SameDayRepeat_IsDeduped_SoAWiderAllowListCannotSpam"/>)
    /// already covers every event type structurally, but this event gets its own pin by name: this
    /// repo has shipped a per-tick nag before (1,287 fires in one run, per the repo's own history),
    /// and a future refactor that special-cased MarketShareShifted's rendering is exactly the kind
    /// of change that could silently step around the shared guard.</summary>
    [TestCase]
    public void MarketShareShifted_IdleDay_FiredTwiceInOneBatch_RendersExactlyOneLine()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var evt = new MarketShareShifted(Permille: 350, RivalGained: true);
            ticker.OnPhaseCompleted(
                DayPhase.Evening, completedDay: 6, StagedWorld(), ImmutableList.Create<GameEvent>(evt, evt));

            AssertThat(ticker.Lines.Count).IsEqual(1);
        }
        finally
        {
            ticker.Free();
        }
    }

    // ── faction standing (U5(b)/U5(c), R9) ─────────────────────────────────────────────────────
    // Faction standing was an entirely invisible economy layer before this pass: PlayerState.Standing
    // silently moved ore prices every Evening, and FactionStandingShifted had zero renderers anywhere
    // in godot/. These cover the two new surfaces: the ticker's edge-triggered cause line (b, below)
    // and MainUi's non-zero-only standing chips (c, below). The TariffApplied silence ruling is pinned
    // above, folded into DeliberateExclusions_StaySilentInTheMarquee.

    [TestCase]
    public void FactionStanding_ThresholdCrossing_RendersExactlyOneLine_NamingTheFaction()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var favored = new FactionStandingShifted(
                FactionRegistry.DeepveinId, FactionRegistry.Deepvein.DisplayName, StandingShiftDirection.Favored);

            ticker.OnPhaseCompleted(DayPhase.Evening, completedDay: 4, state, ImmutableList.Create<GameEvent>(favored));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains(FactionRegistry.Deepvein.DisplayName);
            AssertThat(ticker.DisplayText).Contains("remember your custom");

            var cooled = new FactionStandingShifted(
                FactionRegistry.DeepveinId, FactionRegistry.Deepvein.DisplayName, StandingShiftDirection.Cooled);
            ticker.OnPhaseCompleted(DayPhase.Morning, completedDay: 5, state, ImmutableList.Create<GameEvent>(cooled));

            // Day 4's line is still retained (MaxDaysRetained=3, and 4 > 5-3) alongside day 5's.
            AssertThat(ticker.Lines.Count).IsEqual(2);
            AssertThat(ticker.DisplayText).Contains("cooling toward your shop");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>
    /// The important one: pins the edge-trigger against the REAL sim, not a hand-built event. A
    /// single Morning of drift (Deepvein's DriftStep=2) moves standing from 10 to 8 — nowhere near
    /// either voicing boundary (FavoredExit = cap*2/5 = 40, FavoredEnter = cap/2 = 50) — so
    /// <c>FactionDriftSystem</c> emits nothing, and the ticker fed that tick's real event batch
    /// must render no faction line. A daily gauge movement would not reach a townsperson's ears.
    /// </summary>
    [TestCase]
    public void FactionStanding_SubThresholdMorningDrift_RendersNoLine()
    {
        var start = GameFactory.NewGame(9099);
        var withStanding = start with { Player = start.Player.WithStanding(FactionRegistry.DeepveinId, 10) };
        var adapter = new SimAdapter(withStanding);

        adapter.AdvancePhase(); // day 1's Morning: FactionDriftSystem runs, steps 10 -> 8

        AssertThat(adapter.CurrentState.Player.StandingFor(FactionRegistry.DeepveinId)).IsEqual(8);
        AssertThat(adapter.LastEvents.OfType<FactionStandingShifted>().Count()).IsEqual(0);

        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(DayPhase.Morning, completedDay: 1, adapter.CurrentState, adapter.LastEvents);

            AssertThat(ticker.DisplayText).NotContains(FactionRegistry.Deepvein.DisplayName);
            AssertThat(ticker.DisplayText).NotContains("remember your custom");
            AssertThat(ticker.DisplayText).NotContains("cooling toward your shop");
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void FactionStanding_BuyingOreRaisesStanding_ChipAppears_AndDiscountsTheNextBuy()
    {
        var ui = MountMainUi(new SimAdapter(EveningOreWorld(totalQuantity: 200, unitPrice: 10, gold: 100_000)));
        try
        {
            // Neutral standing: U5(c)'s "zero shows nothing" — no chip yet.
            AssertThat(ui.FindChild("StandingChip_deepvein", recursive: true, owned: false)).IsNull();

            var beforeFirstBuy = ui.Adapter.CurrentState.Player.Gold;
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), "copper", 100));
            var afterFirstBuy = ui.Adapter.CurrentState.Player.Gold;
            var firstCost = beforeFirstBuy - afterFirstBuy;

            AssertThat(firstCost).IsEqual(1000); // neutral standing: full price, 100 x 10g
            AssertThat(ui.Adapter.CurrentState.Player.StandingFor(FactionRegistry.DeepveinId))
                .IsEqual(FactionRegistry.Deepvein.RiseStep);

            var chip = Find<Control>(ui, "StandingChip_deepvein");
            AssertThat(RenderedText(chip)).Contains($"{FactionRegistry.Deepvein.RiseStep}");

            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), "copper", 100));
            var afterSecondBuy = ui.Adapter.CurrentState.Player.Gold;
            var secondCost = afterFirstBuy - afterSecondBuy;

            // The next buy's price reflects the standing the first buy just earned: same quantity,
            // same unit price, cheaper.
            AssertThat(secondCost).IsLess(firstCost);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FactionStanding_DecaysAcrossMornings_ChipValueDrops_AndDisappearsAtZero()
    {
        var driftStep = FactionRegistry.Deepvein.DriftStep;
        var start = GameFactory.NewGame(9101);

        // Two steps above neutral, mounted right at Morning (day 1's own starting phase) — ONE
        // AdvancePhase() call runs exactly that Morning's systems (FactionDriftSystem included)
        // and stops there, so this needs no assumption about how the rest of a zero-hero day behaves.
        var twoStepsUp = start with { Player = start.Player.WithStanding(FactionRegistry.DeepveinId, driftStep * 2) };
        var ui = MountMainUi(new SimAdapter(twoStepsUp));
        try
        {
            AssertThat(RenderedText(Find<Control>(ui, "StandingChip_deepvein"))).Contains($"{driftStep * 2}");

            ui.Adapter.AdvancePhase();

            AssertThat(ui.Adapter.CurrentState.Player.StandingFor(FactionRegistry.DeepveinId)).IsEqual(driftStep);
            AssertThat(RenderedText(Find<Control>(ui, "StandingChip_deepvein"))).Contains($"{driftStep}");
        }
        finally
        {
            Unmount(ui);
        }

        // A second, independent Morning exactly one step from neutral — drift never overshoots
        // (FactionDriftSystem.StepTowardZero), so this snaps straight to 0, and the chip must
        // vanish entirely rather than render "0".
        var oneStepUp = start with { Player = start.Player.WithStanding(FactionRegistry.DeepveinId, driftStep) };
        var ui2 = MountMainUi(new SimAdapter(oneStepUp));
        try
        {
            ui2.Adapter.AdvancePhase();

            AssertThat(ui2.Adapter.CurrentState.Player.StandingFor(FactionRegistry.DeepveinId)).IsEqual(0);
            AssertThat(ui2.FindChild("StandingChip_deepvein", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui2);
        }
    }

    /// <summary>
    /// The Gloomwood Wardens are registered (their four nature-ores extend the material ladder)
    /// but have no live venue in rotation (<see cref="WardensFaction"/>'s own doc: "registered, not
    /// in the live rotation") — nothing in real play can ever raise their standing above neutral,
    /// so they can never produce a chip (and, structurally, never a ticker line either — the
    /// renderer only ever sees an event the sim actually stamps, and nothing can stamp one for a
    /// faction whose ore no live venue ever offers). Contrasted here against Deepvein, which DOES
    /// carry standing in this same state, so the chip row's "non-zero only" filter is proven to be
    /// about the VALUE, not a coincidence of an otherwise-empty state.
    /// </summary>
    [TestCase]
    public void FactionStanding_NoLiveOreSource_NeverProducesAChip()
    {
        var start = GameFactory.NewGame(9102);
        var mixedStanding = start with
        {
            Player = start.Player.WithStanding(FactionRegistry.DeepveinId, FactionRegistry.Deepvein.RiseStep),
        };
        var ui = MountMainUi(new SimAdapter(mixedStanding));
        try
        {
            AssertThat(ui.FindChild($"StandingChip_{WardensFaction.Id}", recursive: true, owned: false)).IsNull();
            AssertThat(Find<Control>(ui, $"StandingChip_{FactionRegistry.DeepveinId}")).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>An Evening world with one hero and one open Deepvein-ore offer, purse deep enough
    /// for two full-price buys — the fixture <see cref="FactionStanding_BuyingOreRaisesStanding_ChipAppears_AndDiscountsTheNextBuy"/>
    /// drives twice in the same Evening to observe the tariff move between them.</summary>
    private static GameState EveningOreWorld(int totalQuantity, int unitPrice, int gold) =>
        GameFactory.NewGame(9100) with
        {
            Phase = DayPhase.Evening,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, Delver(1, "V1", "vanguard")),
            Player = PlayerState.NewGame(gold),
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", totalQuantity, unitPrice)),
        };

    // ── economic moments (U7, the moment-lines batch) ──────────────────────────────────────────
    // Four events that move the player's gold and, before this pass, told the player nothing:
    // RentPaid/RentMissed (RentSystem, 10-day cadence), GuildAssessmentPassed/Missed
    // (GuildAssessmentSystem, 7-day cadence), HeroRankUp (a rank-CROSSING only — ordinary XP gain
    // emits no event at all), and BountyPaid (the town paying out, unlike BountyPosted's own-action
    // silence pinned above in PlayerOwnActionEvents_NeverRender / AdventureTickerTests.cs).

    [TestCase]
    public void RentPaid_RendersOneLine_NamingAmountAndNextDue()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Morning, completedDay: 10, StagedWorld(),
                ImmutableList.Create<GameEvent>(new RentPaid(AmountGold: 40, NextAmountDueGold: 46)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("Rent paid — 40g");
            AssertThat(ticker.DisplayText).Contains("Next due: 46g");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>Missed rent reads with an escalated tone (unpaid amount, miss count, and the
    /// climbing next-due figure) rather than the plain paid line — RentSystem's own chip/tooltip on
    /// <c>MainUi</c> is untouched by this unit (still reads <see cref="RentState"/> directly), so
    /// this only pins the NEW marquee surface, not a re-test of the existing gauge.</summary>
    [TestCase]
    public void RentMissed_RendersWithEscalatedTone()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Morning, completedDay: 20, StagedWorld(),
                ImmutableList.Create<GameEvent>(
                    new RentMissed(AmountDueGold: 46, NextAmountDueGold: 62, MissedPayments: 2, ConfidencePermille: 700)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("Rent went unpaid — 46g owed, 2 missed payment(s) now");
            AssertThat(ticker.DisplayText).Contains("next due climbs to 62g");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>The two calls are 2 days apart, not the GuildAssessmentSystem's real 7-day
    /// cadence: <see cref="AdventureTicker.MaxDaysRetained"/> is a 3-day rolling window (by
    /// design — the strip must not grow unbounded across a long campaign), so a real 7-day gap
    /// would purge the Passed line before the Missed one ever landed, and this test would be
    /// asserting a marquee state a player could never actually see. Staying inside the window
    /// is what proves the two lines coexist without one evicting or overwriting the other.</summary>
    /// <summary>
    /// P2-LONG-18: a cycle settled by a pledged piece is its own event, and the reason it is its own
    /// event is entirely visible from here. While the pledge rode on two optional parameters of
    /// <see cref="GuildAssessmentPassed"/>, THIS renderer kept compiling and started saying "Guild
    /// Assessment paid — 0g" — a sentence that is false twice over, since nothing was paid and no
    /// gold moved. A defaulted parameter changes no signature, so nothing anywhere went red.
    ///
    /// <para>So the assertions run in both directions. The line must name the piece and the dues it
    /// covered, and it must NOT contain the word "paid" or a "0g", because those are the exact two
    /// tokens the wrong version produced. Phrased against the tokens rather than against the whole
    /// sentence so a copy rewrite stays free while the lie stays caught.</para>
    /// </summary>
    [TestCase]
    public void DuesSettledByPledge_Renders_WithoutClaimingAnythingWasPaid()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Morning, completedDay: 7, StagedWorld(),
                ImmutableList.Create<GameEvent>(
                    new DuesSettledByPledge(
                        new ItemId(10), "Emberbite", DuesCoveredGold: 60, NextDuesGold: 90, ConfidencePermille: 820)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("Emberbite");
            AssertThat(ticker.DisplayText).Contains("60g");
            AssertThat(ticker.DisplayText).Contains("Next dues: 90g");

            AssertThat(ticker.DisplayText.ToLowerInvariant().Contains("paid"))
                .OverrideFailureMessage(
                    "The pledge line claims something was paid. Nothing was: a piece left the world and no "
                    + $"coin moved. Line was \"{ticker.DisplayText}\".")
                .IsFalse();
            // A bare substring check reads "60g" as containing "0g" and fails on the very figure the
            // assertion two lines up requires — caught by this test failing on a correct line. The
            // boundary is what was meant: a zero amount, not a zero digit inside some other amount.
            AssertThat(Regex.IsMatch(ticker.DisplayText, @"\b0g\b"))
                .OverrideFailureMessage(
                    "The pledge line names a 0g amount — the exact artefact of settling this cycle through "
                    + $"GuildAssessmentPassed's DuesPaidGold. Line was \"{ticker.DisplayText}\".")
                .IsFalse();
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void GuildAssessment_PassedAndMissed_Render()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(
                DayPhase.Morning, completedDay: 7, StagedWorld(),
                ImmutableList.Create<GameEvent>(
                    new GuildAssessmentPassed(DuesPaidGold: 60, NextDuesGold: 90, ConfidencePermille: 820)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("Guild Assessment paid — 60g");
            AssertThat(ticker.DisplayText).Contains("Next dues: 90g");

            ticker.OnPhaseCompleted(
                DayPhase.Morning, completedDay: 9, StagedWorld(),
                ImmutableList.Create<GameEvent>(
                    new GuildAssessmentMissed(DuesDueGold: 90, NextDuesGold: 157, MissedAssessments: 1, ConfidencePermille: 600)));

            AssertThat(ticker.Lines.Count).IsEqual(2);
            AssertThat(ticker.DisplayText).Contains("Guild Assessment missed — 90g unpaid, 1 time(s) now");
            AssertThat(ticker.DisplayText).Contains("Next dues climb to 157g");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>Pins the crossing-only contract at the ticker's own boundary: it renders when
    /// handed a <see cref="HeroRankUp"/>, and stays silent when a day's batch has none — which is
    /// exactly what ordinary XP gain under a rank threshold produces. The sim-side half of the
    /// guarantee (a survivor who stays under the next threshold never gets one stamped at all) is
    /// proven in <c>ExpeditionRevealSystemTests.Survivor_AccruesXp_ForSurvivalAndDepth_NoBeats</c>
    /// and <c>.CrossingARankThreshold_EmitsNamedHeroRankUp</c> — the same structural argument this
    /// file's class doc already makes for <see cref="HeroDied"/>.</summary>
    [TestCase]
    public void HeroRankUp_RendersOnCrossing_NotOnOrdinaryXpGain()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();

            ticker.OnPhaseCompleted(
                DayPhase.Evening, completedDay: 6, state,
                ImmutableList.Create<GameEvent>(new HeroRankUp(new HeroId(1), "Delver")));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("V1 has risen to Delver");

            // Ordinary XP gain within a rank stamps no HeroRankUp at all — nothing new to render.
            ticker.OnPhaseCompleted(DayPhase.Evening, completedDay: 7, state, ImmutableList<GameEvent>.Empty);

            AssertThat(ticker.Lines.Count).IsEqual(1);
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>The two bounty events pinned together deliberately, so the distinction can never
    /// silently drift apart: <see cref="BountyPaid"/> is the town paying out (news), while
    /// <see cref="BountyPosted"/> — the player's own action read back at them — stays silent (also
    /// pinned standalone by <c>AdventureTickerTests.PlayerOwnActionEvents_NeverRender</c>).</summary>
    [TestCase]
    public void BountyPaid_Renders_AndBountyPosted_StillRendersNothing()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var events = ImmutableList.Create<GameEvent>(
                new BountyPosted(new BountyId(1), TargetFloor: 4, RewardGold: 50),
                new BountyPaid(new BountyId(2), new HeroId(1), RewardGold: 75));

            ticker.OnPhaseCompleted(DayPhase.Evening, completedDay: 8, StagedWorld(), events);

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("V1 collects 75g on a completed bounty");
            AssertThat(ticker.DisplayText).NotContains("50g");
        }
        finally
        {
            ticker.Free();
        }
    }

    // ── the forward ladder (plan 2026-08-10-003, L5) ───────────────────────────────────────────

    /// <summary>A solo graduation names the hero singular; a whole-party graduation names the
    /// first graduate and counts the rest — proving the ticker's VenueGraduated case is wired
    /// (it fired into total silence before L5, exactly the class of defect this file audits) and
    /// that it never invents a name for a hero the event didn't mention.</summary>
    [TestCase]
    public void VenueGraduated_SoloAndParty_NameFirstGraduateAndCountTheRest()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();

            ticker.OnPhaseCompleted(
                DayPhase.Evening, completedDay: 15, state,
                ImmutableList.Create<GameEvent>(
                    new VenueGraduated("mine", ImmutableList.Create(new HeroId(1)), NewRank: 1)));

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("V1 has proven ready for deeper ground.");

            // Day 16, not a later week: the marquee retains only MaxDaysRetained (3) days, so a
            // second graduation far in the future would prune the first line and read as a wiring
            // failure when it is only retention doing its job.
            ticker.OnPhaseCompleted(
                DayPhase.Evening, completedDay: 16, state,
                ImmutableList.Create<GameEvent>(
                    new VenueGraduated("gloomwood", ImmutableList.Create(new HeroId(1), new HeroId(2)), NewRank: 2)));

            AssertThat(ticker.Lines.Count).IsEqual(2);
            AssertThat(ticker.DisplayText).Contains("V1 and 1 other have proven ready for deeper ground.");
        }
        finally
        {
            ticker.Free();
        }
    }

    /// <summary>A day with none of the four U7 economic moments must add nothing — the no-
    /// placeholder-noise contract <see cref="AdventureTicker.OnPhaseCompleted"/> already documents.</summary>
    [TestCase]
    public void EconomicMoments_DayWithNoneOfTheFour_RendersNothing()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.OnPhaseCompleted(DayPhase.Morning, completedDay: 5, StagedWorld(), ImmutableList<GameEvent>.Empty);

            AssertThat(ticker.Lines.Count).IsEqual(0);
            AssertThat(ticker.DisplayText).IsEmpty();
        }
        finally
        {
            ticker.Free();
        }
    }

    // ── the campaign's ending (P2-MEMORY-14: ChronicleScroll deleted; LegendsWall.ShowBindPage is
    // now the reader for CampaignEnded — its own class doc) ────────────────────────────────────────

    /// <summary>The property this suite exists to prove, aimed at today's reader: a campaign ending
    /// must still produce player-visible text, never a blank page. <see cref="ChronicleComposer"/>'s
    /// own predicate logic is pinned at the sim layer (<c>ChronicleComposerTests</c>); this only
    /// proves the Godot reader actually renders what that pure function returns — the exact gap this
    /// suite's own class doc says a real audit once found (a tallied event with no reader at all).</summary>
    [TestCase]
    public void CampaignEnding_OpensTheBook_ToPlayerVisibleChronicleText()
    {
        var ui = MountMainUi();
        try
        {
            var world = GameFactory.NewGame(9420);

            ui.Legends.ShowBindPage(world);

            AssertThat(ui.Legends.Visible)
                .OverrideFailureMessage("setup check: ShowBindPage must open the book.")
                .IsTrue();
            AssertThat(RenderedText(ui.Legends))
                .OverrideFailureMessage("A campaign ending produced no player-visible chronicle text.")
                .Contains(ChronicleComposer.Closer);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>P2-OQ4's own second constraint on the bind: stamped with the day it was composed,
    /// because the world stays open after binding and an undated page would read as more final than
    /// it is.</summary>
    [TestCase]
    public void CampaignEnding_StampsTheDayItWasComposed()
    {
        var ui = MountMainUi();
        try
        {
            var world = GameFactory.NewGame(9421) with { Day = 12 };

            ui.Legends.ShowBindPage(world);

            AssertThat(RenderedText(ui.Legends))
                .OverrideFailureMessage("The bind page must name the day it was composed (P2-OQ4).")
                .Contains("day 12");
        }
        finally
        {
            Unmount(ui);
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
