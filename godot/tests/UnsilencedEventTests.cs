#if GDUNIT_TESTS
using System.Collections.Generic;
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
/// into the old <c>AdventureTicker</c>'s allow-list and falling straight through its
/// <c>_ => null</c> arm, plus a <see cref="CampaignEnded"/> event that carried purpose-built
/// chronicle tallies and had no reader at all — the campaign could end and the player would never
/// be told.
///
/// <para>P2-MEMORY-12 (P2-OQ3): the ticker itself is deleted; every case below now drives
/// <see cref="LegendsWall.DayLines"/> directly (the SAME <c>FormatLine</c> switch, moved not
/// copied) instead of a live <c>AdventureTicker</c> instance's <c>OnPhaseCompleted</c>. These
/// tests assert the SURFACING, not the sim: each one proves a formerly-dropped event now produces
/// player-visible text, at full campaign retention rather than a rolling 3-day window. They
/// deliberately also pin the remaining exclusions (<see cref="SupplyDelivered"/>, <see
/// cref="MarketShareShifted"/>'s active-recovery direction), because those were judgment calls —
/// a future reader deserves to see they were decided rather than missed, and a test is the only
/// place that survives.</para>
///
/// <para>Driven directly against <see cref="LegendsWall.DayLines"/> with hand-built state,
/// following the deleted <c>AdventureTickerTests</c>' established technique: deterministic and
/// fast, and it can construct event shapes a real 40-day campaign would take minutes to reach.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UnsilencedEventTests
{
    [TestCase]
    public void CommissionLifecycle_AllThreePhases_Render()
    {
        var lines = Compose(
            StagedWorld(), day: 3,
            new CommissionPosted(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 7, PremiumGold: 30),
            new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 30),
            new CommissionExpired(new HeroId(2), ItemSlot.Shield));

        AssertThat(lines.Count).IsEqual(3);
        var text = Joined(lines);
        AssertThat(text).Contains("V1 wants Weapon work, Fine or better, by day 7");
        AssertThat(text).Contains("30g over list");
        AssertThat(text).Contains("V1 takes delivery of Dagger");
        AssertThat(text).Contains("S1 gave up waiting on that Shield commission.");
    }

    /// <summary>P2-SCREEN-39: the deadline kept. <see cref="Compose"/> stamps every event onto the
    /// SAME synthetic day, which cannot express "posted one day, fulfilled on another" — so this
    /// pair builds the log directly, mirroring the real Morning order (CommissionPosted logged
    /// before the CommissionFulfilled its deadline later governs).</summary>
    [TestCase]
    public void CommissionFulfilled_OnDeadlineDay_NamesTheDeadlineKept()
    {
        var state = StagedWorld() with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new CommissionPosted(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 7, PremiumGold: 30)
                    with { Id = new EventId(9001), Day = 2 },
                new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 30)
                    with { Id = new EventId(9002), Day = 7 }),
        };

        var text = Joined(LegendsWall.DayLines(state, 7));

        AssertThat(text).Contains("V1 takes delivery of Dagger");
        AssertThat(text).Contains("on the day it was due");
    }

    [TestCase]
    public void CommissionFulfilled_BeforeDeadlineDay_SaysNothingExtra()
    {
        var state = StagedWorld() with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new CommissionPosted(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 7, PremiumGold: 30)
                    with { Id = new EventId(9001), Day = 2 },
                new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 30)
                    with { Id = new EventId(9002), Day = 5 }),
        };

        var text = Joined(LegendsWall.DayLines(state, 5));

        AssertThat(text).Contains("V1 takes delivery of Dagger");
        AssertThat(text).NotContains("on the day it was due");
    }

    [TestCase]
    public void ConfidenceSpiral_EdgeTriggeredWarnings_Render()
    {
        var lines = Compose(
            StagedWorld(), day: 12,
            new RivalExpansionTriggered(ConfidencePermille: 340),
            new HeroConsideringLeaving(new HeroId(2), ConfidencePermille: 180),
            new TownConfidenceCollapsed(MissedAssessments: 3),
            new RecruitArrived(new HeroId(1)));

        AssertThat(lines.Count).IsEqual(4);
        var text = Joined(lines);
        AssertThat(text).Contains("confidence has slipped to 34%");
        AssertThat(text).Contains("S1 is talking about leaving town.");
        AssertThat(text).Contains("3 assessment(s) missed");
        AssertThat(text).Contains("V1 has come to town looking for work.");
    }

    /// <summary>
    /// Every id in <c>DirectorSystem.Catalog</c> must have authored prose. The unknown-id arm exists
    /// so a future catalog entry degrades to something true rather than vanishing — that fallback is
    /// asserted too, since a silently-dropped incident is the exact bug this file is about.
    /// </summary>
    [TestCase]
    public void DirectorIncidents_EveryCatalogId_HasAuthoredProse()
    {
        var lines = Compose(
            StagedWorld(), day: 5,
            Incident("whispers_in_the_dark", IncidentCategory.Rumor, IncidentMagnitude.Minor),
            Incident("goblin_probe", IncidentCategory.Skirmish, IncidentMagnitude.Minor),
            Incident("spider_brood_swells", IncidentCategory.Infestation, IncidentMagnitude.Notable),
            Incident("ghoul_warren_breaks", IncidentCategory.Breakout, IncidentMagnitude.Notable),
            Incident("the_forgeworm_stirs", IncidentCategory.Cataclysm, IncidentMagnitude.Severe));

        AssertThat(lines.Count).IsEqual(5);
        var text = Joined(lines);
        AssertThat(text).Contains("Whispers out of the dark");
        AssertThat(text).Contains("probed the mine mouth");
        AssertThat(text).Contains("spider brood is swelling");
        AssertThat(text).Contains("ghoul warren has broken open");
        AssertThat(text).Contains("forgeworm stirs");

        // No raw snake_case id ever reaches the player for a catalogued incident.
        AssertThat(text).NotContains("_");
    }

    [TestCase]
    public void UncataloguedIncident_DegradesToReadableLine_NeverVanishes()
    {
        var lines = Compose(
            StagedWorld(), day: 1,
            Incident("a_brand_new_horror", IncidentCategory.Rumor, IncidentMagnitude.Minor));

        AssertThat(lines.Count).IsEqual(1);
        AssertThat(Joined(lines)).Contains("a brand new horror");
    }

    /// <summary>
    /// The remaining deliberate exclusions. <see cref="SupplyDelivered"/> confirms the player's own
    /// camp action (CampPanel already shows it), and <see cref="MarketShareShifted"/>'s
    /// active-recovery direction (<c>RivalGained: false</c>, any day that spent an action slot)
    /// would drift every single Evening the player actually works — it names no cost worth
    /// disclosing (working is the expected default, not a fee). The OTHER direction —
    /// <c>RivalGained: true</c>, the idle-day charge law 7 requires be named — is no longer silent;
    /// see <see cref="MarketShareShifted_IdleDay_NamesThePlayersOwnIdleDay"/> below (P2-HONEST-23).
    ///
    /// <para><see cref="TariffApplied"/> (U5(b) ruling) joins them here rather than getting a
    /// renderer: it is the per-purchase price delta ONE buy's standing-at-the-time produced — like
    /// <see cref="SupplyDelivered"/>, confirmation of the player's OWN action (their own buy,
    /// already reflected in their own gold total and material count) rather than town news. The
    /// actual news — that the faction's standing itself crossed a line — is what
    /// <see cref="FactionStandingShifted"/> announces instead (see
    /// <c>FactionStanding_ThresholdCrossing_RendersExactlyOneLine_NamingTheFaction</c> below);
    /// voicing the per-buy arithmetic too would say the same fact twice in the same day's page.</para>
    ///
    /// Pinned so every one of these decisions is visible rather than looking like an oversight.
    /// </summary>
    [TestCase]
    public void DeliberateExclusions_StaySilentInTheDayLog()
    {
        var lines = Compose(
            StagedWorld(), day: 1,
            new SupplyDelivered(new HeroId(1), new ItemId(1), Fee: 5),
            new MarketShareShifted(Permille: 120, RivalGained: false),
            new TariffApplied(FactionRegistry.DeepveinId, "copper", BaseLineCost: 100, PlayerCost: 90, Delta: -10));

        AssertThat(lines.Count).IsEqual(0);
    }

    /// <summary>
    /// The day log's own spam guard, ported from the deleted marquee. A widened allow-list is
    /// exactly how a day's page becomes wallpaper, so prove the same event twice in one day yields
    /// one line — full retention is about never AGING content out, not about repeating an
    /// exact-text duplicate.
    /// </summary>
    [TestCase]
    public void SameDayRepeat_IsDeduped_SoAWiderAllowListCannotSpam()
    {
        var evt = Incident("goblin_probe", IncidentCategory.Skirmish, IncidentMagnitude.Minor);
        var lines = Compose(StagedWorld(), day: 4, evt, evt);

        AssertThat(lines.Count).IsEqual(1);
    }

    // ── the idle-day cost (P2-HONEST-23, law 7) ────────────────────────────────────────────────
    // Law 7 ("skipping stays legal and its cost is named in copy, never engineered") had a live
    // gap: MarketShareSystem's idle-day charge (+150‰ toward the rival) was real and mechanically
    // enforced, but nothing ever told the player it happened or why. The two tests below cover
    // both directions the unit's own spec demands, plus the per-tick dedupe guard.

    /// <summary>
    /// P2-HONEST-23: the idle-day HALF of <see cref="MarketShareShifted"/> now speaks — see
    /// <see cref="LegendsWall.FormatLine"/>'s case and its neighbouring exclusion comment.
    /// Phrased against the PROPERTY, not one sentence (a copy rewrite must stay free): the line
    /// must name the CAUSE — the player's own idle day, addressed directly — not just repeat the
    /// EFFECT <c>ShopPanel.RivalEdgeGradient</c> already names ("The rival's edge is creeping
    /// up."). The active-recovery direction (<c>RivalGained: false</c>, any day that spent a slot)
    /// stays silent exactly as before — pinned by
    /// <see cref="DeliberateExclusions_StaySilentInTheDayLog"/> above.
    /// </summary>
    [TestCase]
    public void MarketShareShifted_IdleDay_NamesThePlayersOwnIdleDay()
    {
        var lines = Compose(StagedWorld(), day: 6, new MarketShareShifted(Permille: 350, RivalGained: true));

        AssertThat(lines.Count).IsEqual(1);
        var text = Joined(lines);
        AssertThat(text.ToLowerInvariant().Contains("you"))
            .OverrideFailureMessage(
                "The idle-day line must address the player directly (the CAUSE) rather than "
                + $"only restating the rival's gain (the EFFECT). Line was \"{text}\".")
            .IsTrue();
    }

    /// <summary>The generic same-day dedupe (<see cref="SameDayRepeat_IsDeduped_SoAWiderAllowListCannotSpam"/>)
    /// already covers every event type structurally, but this event gets its own pin by name: this
    /// repo has shipped a per-tick nag before (1,287 fires in one run, per the repo's own history),
    /// and a future refactor that special-cased MarketShareShifted's rendering is exactly the kind
    /// of change that could silently step around the shared guard.</summary>
    [TestCase]
    public void MarketShareShifted_IdleDay_FiredTwiceInOneBatch_RendersExactlyOneLine()
    {
        var evt = new MarketShareShifted(Permille: 350, RivalGained: true);
        var lines = Compose(StagedWorld(), day: 6, evt, evt);

        AssertThat(lines.Count).IsEqual(1);
    }

    // ── faction standing (U5(b)/U5(c), R9) ─────────────────────────────────────────────────────
    // Faction standing was an entirely invisible economy layer before this pass: PlayerState.Standing
    // silently moved ore prices every Evening, and FactionStandingShifted had zero renderers anywhere
    // in godot/. These cover the two new surfaces: the day log's edge-triggered cause line (below)
    // and MainUi's non-zero-only standing chips (further below). The TariffApplied silence ruling is
    // pinned above, folded into DeliberateExclusions_StaySilentInTheDayLog.

    /// <summary>
    /// P2-MEMORY-12: the two crossings sit 46 days apart — far past the deleted marquee's own
    /// 3-day window — proving the day log's whole reason to exist: each day keeps its own page
    /// regardless of how long ago it happened, rather than one crossing evicting the other.
    /// </summary>
    [TestCase]
    public void FactionStanding_ThresholdCrossing_RendersExactlyOneLine_NamingTheFaction()
    {
        var state = StagedWorld();
        var favored = new FactionStandingShifted(
            FactionRegistry.DeepveinId, FactionRegistry.Deepvein.DisplayName, StandingShiftDirection.Favored)
        { Id = new EventId(1), Day = 4 };
        var cooled = new FactionStandingShifted(
            FactionRegistry.DeepveinId, FactionRegistry.Deepvein.DisplayName, StandingShiftDirection.Cooled)
        { Id = new EventId(2), Day = 50 };
        state = state with { EventLog = ImmutableList.Create<GameEvent>(favored, cooled) };

        var day4 = LegendsWall.DayLines(state, 4);
        AssertThat(day4.Count).IsEqual(1);
        AssertThat(Joined(day4)).Contains(FactionRegistry.Deepvein.DisplayName);
        AssertThat(Joined(day4)).Contains("remember your custom");

        var day50 = LegendsWall.DayLines(state, 50);
        AssertThat(day50.Count).IsEqual(1);
        AssertThat(Joined(day50)).Contains("cooling toward your shop");
    }

    /// <summary>
    /// The important one: pins the edge-trigger against the REAL sim, not a hand-built event. A
    /// single Morning of drift (Deepvein's DriftStep=2) moves standing from 10 to 8 — nowhere near
    /// either voicing boundary (FavoredExit = cap*2/5 = 40, FavoredEnter = cap/2 = 50) — so
    /// <c>FactionDriftSystem</c> emits nothing, and day 1's own page must render no faction line.
    /// A daily gauge movement would not reach a townsperson's ears.
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

        var text = Joined(LegendsWall.DayLines(adapter.CurrentState, 1));
        AssertThat(text).NotContains(FactionRegistry.Deepvein.DisplayName);
        AssertThat(text).NotContains("remember your custom");
        AssertThat(text).NotContains("cooling toward your shop");
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
    /// so they can never produce a chip (and, structurally, never a day-log line either — the
    /// composer only ever sees an event the sim actually stamps, and nothing can stamp one for a
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
    // silence, pinned below in BountyPaid_Renders_AndBountyPosted_StillRendersNothing).

    [TestCase]
    public void RentPaid_RendersOneLine_NamingAmountAndNextDue()
    {
        var lines = Compose(StagedWorld(), day: 10, new RentPaid(AmountGold: 40, NextAmountDueGold: 46));

        AssertThat(lines.Count).IsEqual(1);
        var text = Joined(lines);
        AssertThat(text).Contains("Rent paid — 40g");
        AssertThat(text).Contains("Next due: 46g");
    }

    /// <summary>Missed rent reads with an escalated tone (unpaid amount, miss count, and the
    /// climbing next-due figure) rather than the plain paid line — RentSystem's own chip/tooltip on
    /// <c>MainUi</c> is untouched by this unit (still reads <see cref="RentState"/> directly), so
    /// this only pins the day-log surface, not a re-test of the existing gauge.</summary>
    [TestCase]
    public void RentMissed_RendersWithEscalatedTone()
    {
        var lines = Compose(
            StagedWorld(), day: 20,
            new RentMissed(AmountDueGold: 46, NextAmountDueGold: 62, MissedPayments: 2, ConfidencePermille: 700));

        AssertThat(lines.Count).IsEqual(1);
        var text = Joined(lines);
        AssertThat(text).Contains("Rent went unpaid — 46g owed, 2 missed payment(s) now");
        AssertThat(text).Contains("next due climbs to 62g");
    }

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
        var lines = Compose(
            StagedWorld(), day: 7,
            new DuesSettledByPledge(
                new ItemId(10), "Emberbite", DuesCoveredGold: 60, NextDuesGold: 90, ConfidencePermille: 820));

        AssertThat(lines.Count).IsEqual(1);
        var text = Joined(lines);
        AssertThat(text).Contains("Emberbite");
        AssertThat(text).Contains("60g");
        AssertThat(text).Contains("Next dues: 90g");

        AssertThat(text.ToLowerInvariant().Contains("paid"))
            .OverrideFailureMessage(
                "The pledge line claims something was paid. Nothing was: a piece left the world and no "
                + $"coin moved. Line was \"{text}\".")
            .IsFalse();
        // A bare substring check reads "60g" as containing "0g" and fails on the very figure the
        // assertion two lines up requires — caught by this test failing on a correct line. The
        // boundary is what was meant: a zero amount, not a zero digit inside some other amount.
        AssertThat(Regex.IsMatch(text, @"\b0g\b"))
            .OverrideFailureMessage(
                "The pledge line names a 0g amount — the exact artefact of settling this cycle through "
                + $"GuildAssessmentPassed's DuesPaidGold. Line was \"{text}\".")
            .IsFalse();
    }

    /// <summary>The two events land on different days — P2-MEMORY-12's day pages keep each on its
    /// own page rather than accumulating both into one shared window the way the deleted marquee
    /// did.</summary>
    [TestCase]
    public void GuildAssessment_PassedAndMissed_Render()
    {
        var state = StagedWorld() with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new GuildAssessmentPassed(DuesPaidGold: 60, NextDuesGold: 90, ConfidencePermille: 820)
                    with { Id = new EventId(1), Day = 7 },
                new GuildAssessmentMissed(DuesDueGold: 90, NextDuesGold: 157, MissedAssessments: 1, ConfidencePermille: 600)
                    with { Id = new EventId(2), Day = 9 }),
        };

        var passedDay = LegendsWall.DayLines(state, 7);
        AssertThat(passedDay.Count).IsEqual(1);
        AssertThat(Joined(passedDay)).Contains("Guild Assessment paid — 60g");
        AssertThat(Joined(passedDay)).Contains("Next dues: 90g");

        var missedDay = LegendsWall.DayLines(state, 9);
        AssertThat(missedDay.Count).IsEqual(1);
        AssertThat(Joined(missedDay)).Contains("Guild Assessment missed — 90g unpaid, 1 time(s) now");
        AssertThat(Joined(missedDay)).Contains("Next dues climb to 157g");
    }

    /// <summary>Pins the crossing-only contract: <see cref="LegendsWall.FormatLine"/> renders when
    /// handed a <see cref="HeroRankUp"/>, and a day with none of them produces no line — which is
    /// exactly what ordinary XP gain under a rank threshold produces. The sim-side half of the
    /// guarantee (a survivor who stays under the next threshold never gets one stamped at all) is
    /// proven in <c>ExpeditionRevealSystemTests.Survivor_AccruesXp_ForSurvivalAndDepth_NoBeats</c>
    /// and <c>.CrossingARankThreshold_EmitsNamedHeroRankUp</c>.</summary>
    [TestCase]
    public void HeroRankUp_RendersOnCrossing_NotOnOrdinaryXpGain()
    {
        var day6 = Compose(StagedWorld(), day: 6, new HeroRankUp(new HeroId(1), "Delver"));
        AssertThat(day6.Count).IsEqual(1);
        AssertThat(Joined(day6)).Contains("V1 has risen to Delver");

        // Ordinary XP gain within a rank stamps no HeroRankUp at all — a day with none composes
        // nothing new.
        var day7 = Compose(StagedWorld(), day: 7);
        AssertThat(day7.Count).IsEqual(0);
    }

    /// <summary>The two bounty events pinned together deliberately, so the distinction can never
    /// silently drift apart: <see cref="BountyPaid"/> is the town paying out (news), while
    /// <see cref="BountyPosted"/> — the player's own action read back at them — stays silent.</summary>
    [TestCase]
    public void BountyPaid_Renders_AndBountyPosted_StillRendersNothing()
    {
        var lines = Compose(
            StagedWorld(), day: 8,
            new BountyPosted(new BountyId(1), TargetFloor: 4, RewardGold: 50),
            new BountyPaid(new BountyId(2), new HeroId(1), RewardGold: 75));

        AssertThat(lines.Count).IsEqual(1);
        var text = Joined(lines);
        AssertThat(text).Contains("V1 collects 75g on a completed bounty");
        AssertThat(text).NotContains("50g");
    }

    // ── the forward ladder (plan 2026-08-10-003, L5) ───────────────────────────────────────────

    /// <summary>A solo graduation names the hero singular; a whole-party graduation names the
    /// first graduate and counts the rest — proving <see cref="LegendsWall.FormatLine"/>'s
    /// <see cref="VenueGraduated"/> case is wired (it fired into total silence before L5, exactly
    /// the class of defect this file audits) and that it never invents a name for a hero the event
    /// didn't mention. The two graduations land on different days — full retention, not a shared
    /// window — so each keeps its own page independently.</summary>
    [TestCase]
    public void VenueGraduated_SoloAndParty_NameFirstGraduateAndCountTheRest()
    {
        var state = StagedWorld();

        var solo = Compose(state, day: 15, new VenueGraduated("mine", ImmutableList.Create(new HeroId(1)), NewRank: 1));
        AssertThat(solo.Count).IsEqual(1);
        AssertThat(Joined(solo)).Contains("V1 has proven ready for deeper ground.");

        var party = Compose(
            state, day: 16,
            new VenueGraduated("gloomwood", ImmutableList.Create(new HeroId(1), new HeroId(2)), NewRank: 2));
        AssertThat(party.Count).IsEqual(1);
        AssertThat(Joined(party)).Contains("V1 and 1 other have proven ready for deeper ground.");
    }

    /// <summary>A day with none of the four U7 economic moments (or any other event) must compose
    /// nothing — the no-placeholder-noise contract <see cref="LegendsWall.RenderDayLog"/> depends
    /// on (a day with zero lines gets no row on the book's index at all).</summary>
    [TestCase]
    public void EconomicMoments_DayWithNoneOfTheFour_RendersNothing()
    {
        var lines = Compose(StagedWorld(), day: 5);

        AssertThat(lines.Count).IsEqual(0);
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

    // ── fixtures / driver ───────────────────────────────────────────────────────────────────────

    private static IncidentFired Incident(string id, IncidentCategory category, IncidentMagnitude magnitude) =>
        new(id, category, magnitude, VenueRegistry.MineId, TensionAfter: 100);

    /// <summary>Stamps every event onto <paramref name="day"/> with a synthetic id, folds them into
    /// <paramref name="state"/>'s own <see cref="GameState.EventLog"/>, and returns
    /// <see cref="LegendsWall.DayLines"/> for that day — the same driver shape the deleted
    /// <c>AdventureTicker.OnPhaseCompleted</c> gave these tests, now against the day pages that
    /// replaced it.</summary>
    private static List<string> Compose(GameState state, int day, params GameEvent[] events)
    {
        var stamped = events
            .Select((e, i) => e with { Id = new EventId(9000 + i), Day = day })
            .ToImmutableList();
        return LegendsWall.DayLines(state with { EventLog = stamped }, day);
    }

    private static string Joined(IEnumerable<string> lines) => string.Join(" | ", lines);

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
