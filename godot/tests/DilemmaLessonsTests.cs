#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Contracts;
using GameSim.Factions;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave C (§11.14.4, Act II, R14.7 "the tutorial names the six dilemmas out loud — one
/// sentence each, both sides, no recommendation"): the two dilemmas this wave teaches — "pricing
/// as a decision" (dilemma #2) and "hold-or-sell" (dilemma #1, fires the first time the player
/// ever accepts a commission) — both routed through the SAME shared <see cref="MentorBanner"/>
/// Wave C introduces rather than a panel-private copy, and the SAME
/// <see cref="TutorialFlow.ConsumeFirstTouch"/> once-ever engine Wave A shipped.
///
/// <para>U1 (§11.14.14 defect): dilemma #2 used to fire ONLY from <c>ShopPanel.Reprice</c>, one
/// line after the SetPriceAction it fired for was already queued — so a player who stocked at the
/// suggested price and never touched a tag never heard it — and its copy described the counter's
/// own goodwill/mood machinery even though it fired off a shelf action that has no price-fairness
/// gate at all. It now fires at the FIRST price a player ever sets in a campaign, which is the
/// initial stock (<c>ShopPanel.PlaceOnShelf</c>) — Reprice can only ever touch an already-shelved
/// item, so Stock always comes first — and its copy claims only the shelf's real mechanism
/// (afford/can't-afford). The counter's own true half moved to <see cref="TutorialStep.OpenCounter"/>'s
/// own TeachNote (see <see cref="GodotClient.Tests.TutorialCopyIsFollowableTests"/> for that
/// coverage) — two true sentences, one per surface, rather than one sentence describing a
/// mechanism only one of the two surfaces has.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DilemmaLessonsTests
{
    private static readonly ItemId UnshelvedItemId = new(9402);
    private static readonly ItemId SecondUnshelvedItemId = new(9403);

    private static Item TestBuckler(ItemId id) => new(
        id, "test-dilemma-shelf-item", "Test Buckler", ItemSlot.Weapon,
        QualityGrade.Common, new ItemStats(Attack: 4, Defense: 0, Weight: 2),
        new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>U1 (§11.14.14 defect): the shelf half of the pricing lesson fires at the FIRST
    /// price a player ever sets — the initial stock, not a later reprice — so this fixture leaves
    /// the item UNSHELVED (in the player's crafted stock, not yet on <see cref="Player.Shelf"/>)
    /// so a test presses the real Stock button rather than starting from a state that has already
    /// skipped past the moment the lesson exists to teach.</summary>
    private static GameState StateWithOneUnshelvedItem()
    {
        var baseState = GameComposition.NewCampaign(seed: 9402);
        var item = TestBuckler(UnshelvedItemId);
        return baseState with { Items = baseState.Items.Add(item.Id.Value, item) };
    }

    private static GameState StateWithTwoUnshelvedItems()
    {
        var baseState = StateWithOneUnshelvedItem();
        var second = TestBuckler(SecondUnshelvedItemId);
        return baseState with { Items = baseState.Items.Add(second.Id.Value, second) };
    }

    /// <summary>Pricing is one of the two dilemmas the census named as wholly untaught before
    /// Wave C. U1 (§11.14.14 defect) moved the firing point onto the initial Stock press — the
    /// actual first price a player ever sets — and this drives that real button (never touching
    /// the StockPrice_ SpinBox first), exactly the "stocks at the suggested price and never
    /// reprices" case the unit exists to cover.</summary>
    [TestCase]
    public void StockingAShelfItemForTheFirstTimeEver_TeachesThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneUnshelvedItem()));
        try
        {
            ui.OpenPanel("Shop");

            PressEnabled(ui.Shop, $"Stock_{UnshelvedItemId.Value}");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<StockAction>().Any(a => a.Item == UnshelvedItemId))
                .OverrideFailureMessage("Setup check: the Stock press did not queue a StockAction.")
                .IsTrue();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The pricing dilemma never showed on the player's first-ever stock.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name); // attributed to Bryn, never an anonymous tooltip
            AssertThat(text).Contains("afford");
            AssertThat(text).Contains("counter");

            // Law 4 ("show only what the sim decided"): ShoppingAi.EvaluateItem (the shelf's own
            // gate) never touches mood/goodwill — the shelf half of the lesson must never borrow
            // the counter's own vocabulary for a mechanism this surface does not have.
            AssertThat(text.Contains("goodwill", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The shelf pricing lesson claims a goodwill mechanism the shelf does not have: \"{text}\"")
                .IsFalse();
            AssertThat(text.Contains("mood", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The shelf pricing lesson claims a mood mechanism the shelf does not have: \"{text}\"")
                .IsFalse();

            // R14.7 / law 12: influence never orders — the lesson must not tell the player which
            // side to take.
            AssertThat(text.Contains("you should", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The pricing dilemma reads as a recommendation: \"{text}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A second stock (on a different item) must NOT re-fire the lesson — the same
    /// once-ever contract every other first-touch lesson in this codebase carries (the 1287x
    /// memorial nag precedent).</summary>
    [TestCase]
    public void StockingASecondItem_DoesNotReshowThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithTwoUnshelvedItems()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Stock_{UnshelvedItemId.Value}");
            ui.Mentor.Dismiss();

            PressEnabled(ui.Shop, $"Stock_{SecondUnshelvedItemId.Value}");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The pricing dilemma fired a SECOND time — the anti-nag pin failed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U1 (§11.14.14 defect, requirement 4): the lesson retires OUT of Reprice once it
    /// has already fired at the first stock — Reprice stops being "the only teacher," and stops
    /// being A teacher too once the shelf lesson has had its one turn. The text stays re-readable
    /// forever in the Lessons book (<c>LessonsPanel</c> renders every <c>FirstTouch.Fired</c> id);
    /// this only pins that Reprice itself never re-triggers the banner.</summary>
    [TestCase]
    public void RepricingAnAlreadyStockedItem_NeverReshowsThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneUnshelvedItem()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Stock_{UnshelvedItemId.Value}");
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Setup check: the shelf lesson never fired on the first stock.")
                .IsTrue();
            ui.Mentor.Dismiss();
            // StockAction resolves immediately (ActionTiming.ResolvesImmediately) — CurrentState
            // already has the item on the shelf; re-opening the panel just refreshes the rows so
            // Reprice_ exists to press (no AdvancePhase, which would let HeroShoppingSystem run
            // and could buy the item back off the shelf before this test gets to it).
            ui.OpenPanel("Shop");

            PressEnabled(ui.Shop, $"Reprice_{UnshelvedItemId.Value}");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().Any(a => a.Item == UnshelvedItemId))
                .OverrideFailureMessage("Setup check: the Reprice press did not queue a SetPriceAction.")
                .IsTrue();
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Reprice re-fired the pricing dilemma — it should be retired from that surface entirely.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ============================================================================================
    // U27 (§11.14.14, dilemma #1, R14.7): hold-or-sell now fires at RENDER -- the moment an open
    // commission appears with a live Accept/Decline pair -- never on the Accept press alone. The
    // old wiring (ConsumeFirstTouch called from inside Accept's own Pressed handler) taught nothing
    // to a player who declined, which is exactly the "only one arm gets the lesson" shape R14.7
    // forbids. Moving it here means both arms, and neither press, are required to hear it.
    // ============================================================================================

    private static Commission OpenCommission(HeroId hero) =>
        new(hero, ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30);

    /// <summary>Hold-or-sell is the other dilemma named in Wave C's own unit list. The board renders
    /// it the instant an undecided commission appears -- no press at all -- which is the direct
    /// proof the old Accept-press-only wiring could never offer.</summary>
    [TestCase]
    public void OpenCommissionRenders_TeachesTheHoldOrSellDilemma_BeforeEitherPressEver()
    {
        var state = GameComposition.NewCampaign(seed: 9402) with { Commissions = ImmutableList.Create(OpenCommission(new HeroId(1))) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The hold-or-sell dilemma never showed when an open commission rendered, before either button was pressed.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("shelf");
            AssertThat(text).Contains("commission");

            // U23 (§11.14.14, "the shelf is a public place"): one fact makes three others
            // derivable -- taught here, once, by naming the verb that performs it.
            AssertThat(text)
                .OverrideFailureMessage($"The hold-or-sell lesson never names the Unstock control. Copy: \"{text}\"")
                .Contains("Unstock");

            // The lesson having already fired must not stop the real press from queuing normally.
            PressEnabled(ui.Commissions, "CommissionAccept_1");
            AssertThat(ui.Adapter.AppliedThisPhase.OfType<AcceptCommissionAction>().Any(a => a.Hero == new HeroId(1)))
                .OverrideFailureMessage("The Accept press did not queue an AcceptCommissionAction once the render-time lesson had already fired.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A player who DECLINES must be taught exactly as much as a player who accepts --
    /// R14.7's "both sides" was a lie under the old wiring, where only the Accept press ever reached
    /// ConsumeFirstTouch and a decliner heard nothing.</summary>
    [TestCase]
    public void DecliningACommissionForTheFirstTime_AlsoReceivedTheHoldOrSellDilemma()
    {
        var state = GameComposition.NewCampaign(seed: 9402) with { Commissions = ImmutableList.Create(OpenCommission(new HeroId(1))) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Setup check: the lesson never fired on render.")
                .IsTrue();
            ui.Mentor.Dismiss();

            PressEnabled(ui.Commissions, "CommissionDecline_1");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<DeclineCommissionAction>().Any(a => a.Hero == new HeroId(1)))
                .OverrideFailureMessage("The Decline press did not queue a DeclineCommissionAction.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>No open commission, nothing to decide -- the dilemma must stay silent (it may never
    /// fire on a day the player is not actually facing this fork).</summary>
    [TestCase]
    public void NoOpenCommission_TheHoldOrSellDilemmaStaysSilent()
    {
        var ui = MountMainUi();
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The hold-or-sell dilemma fired with no open commission on the board.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>An already-accepted commission renders no live Accept/Decline pair -- a settled
    /// commission is not a fork, so the dilemma must not fire off it alone.</summary>
    [TestCase]
    public void OnlyAnAlreadyAcceptedCommissionRenders_TheHoldOrSellDilemmaStaysSilent()
    {
        var accepted = OpenCommission(new HeroId(1)) with { Accepted = true };
        var state = GameComposition.NewCampaign(seed: 9402) with { Commissions = ImmutableList.Create(accepted) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The hold-or-sell dilemma fired off an already-accepted commission -- no live fork was ever on screen.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Once-ever across the whole campaign: a second board-open on a different hero's open
    /// commission must not re-show it -- the same anti-nag contract every other first-touch lesson
    /// here carries (the 1287x memorial-nag precedent).</summary>
    [TestCase]
    public void ASecondOpenCommission_DoesNotReshowTheHoldOrSellDilemma()
    {
        var state = GameComposition.NewCampaign(seed: 9402) with { Commissions = ImmutableList.Create(OpenCommission(new HeroId(1))) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);
            ui.Mentor.Dismiss();

            ui.Commissions.ShowOpen(ui.Adapter.CurrentState with { Commissions = ImmutableList.Create(OpenCommission(new HeroId(2))) });

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The hold-or-sell dilemma fired a SECOND time -- the anti-nag pin failed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Law: no timers on decisions — this banner carries no countdown, same contract as
    /// every other Wave B/C mentor lesson.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task MentorBanner_NeverAutoDismisses_RegardlessOfHowManyFramesPass()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneUnshelvedItem()));
        var player = new HumanPlayer(ui);
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Stock_{UnshelvedItemId.Value}");
            AssertThat(ui.Mentor.Visible).IsTrue();

            await player.Frames(90);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The shared mentor banner disappeared on its own after real frames passed with no player input.")
                .IsTrue();
        }
        finally
        {
            player.ReleaseAll();
            Unmount(ui);
        }
    }

    // ============================================================================================
    // U40 (§11.14.14, dilemma #5 "buy the ore, or buy the faction's favour" -- THE-GAME.md §3.5,
    // amended by owner ruling KTD8, register #170): the tariff fork.
    // ============================================================================================

    /// <summary>The FIRST TariffApplied event is the moment the player's own standing first moves
    /// what an ore buy costs them (OreMarketHandlers only ever emits it once the tariff actually
    /// moved the price -- a neutral-standing first buy is silent). Fires plainly through
    /// ConsumeFirstTouch, the same as "pricing-as-a-decision"/"hold-or-sell" -- never deferred like
    /// U26's demand-board beat, because a tariff only ever moves as the direct, immediate
    /// consequence of the player's OWN BuyOreAction, never a background AI tick.</summary>
    [TestCase]
    public void FirstTariffApplied_TeachesTheTariffForkDilemma()
    {
        var baseState = GameComposition.NewCampaign(ScriptedSession.Seed);
        var state = baseState with
        {
            EventLog = baseState.EventLog.Add(
                new TariffApplied(FactionRegistry.DeepveinId, "copper", BaseLineCost: 100, PlayerCost: 90, Delta: -10)),
        };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            // Any real tick re-checks the durable EventLog fact — a plain, side-effect-light
            // Morning buy, the same trigger this file's own fixtures use elsewhere to force a tick.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The tariff-fork dilemma never showed after the campaign's first-ever tariff.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("favour");

            // R1/R15 ("the copy names no price the player can offer, because none exists"):
            // BuyOreAction carries no price parameter at all -- the hero always receives the base
            // ask -- so the lesson must never invent an overpay/haggle mechanism for ore.
            AssertThat(text.Contains("pay more", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The tariff-fork lesson invents a price the player can offer: \"{text}\"")
                .IsFalse();
            AssertThat(text.Contains("overpay", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The tariff-fork lesson invents an overpay mechanism: \"{text}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A player who never buys ore never triggers a tariff, and never sees this lesson.</summary>
    [TestCase]
    public void NoTariffEver_TheTariffForkDilemmaStaysSilent()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The tariff-fork dilemma fired with no tariff ever applied.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ============================================================================================
    // U-T2 Wave F ("census hygiene"): the six dilemmas THE-GAME.md §3.5 says the game is actually
    // made of, in ONE place, each pinned to its real status -- taught (a live first-touch id,
    // verified in source, same idiom TeachingCoverageCensusTests uses) or honestly blocked/missing
    // (a citation, never a silent gap). R14.7: "the tutorial names the six dilemmas out loud... no
    // recommendation, pinned by corpus tests" -- this is that corpus test, and the reason it lives
    // beside the two lessons above rather than in a new file: THE-GAME.md's own list is a fixed,
    // hand-authored six items (a design-doc fact, not a reflectable registry), so a corpus test over
    // it is the right shape, not a hand-listed array standing in for a real one.
    // ============================================================================================

    private enum DilemmaOutcome
    {
        Taught,
        TaughtByNumberedStep,
        Blocked,
        Missing,
    }

    private readonly record struct DilemmaStatus(DilemmaOutcome Outcome, string Detail, TutorialStep? Step = null);

    /// <summary>Mirrors <c>TeachingCoverageCensusTests.FirstTouchIdIsWiredInSource</c> exactly (a
    /// small, deliberate duplicate rather than a cross-file coupling this codebase has no precedent
    /// for) -- source-scans <c>res://scripts</c> for a live <c>ConsumeFirstTouch</c>/
    /// <c>ShowMentorFirstTouch</c> call site naming <paramref name="id"/>.</summary>
    private static bool FirstTouchIdIsWiredInSource(string id)
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var source = string.Join("\n", Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories).Select(System.IO.File.ReadAllText));
        var escaped = Regex.Escape(id);

        if (Regex.IsMatch(source, $@"(?:ConsumeFirstTouch|ShowMentorFirstTouch)\(\s*(?:\r?\n\s*)?""{escaped}"""))
        {
            return true;
        }

        foreach (Match constDecl in Regex.Matches(source, $@"\bconst\s+string\s+(\w+)\s*=\s*""{escaped}"""))
        {
            var constName = Regex.Escape(constDecl.Groups[1].Value);
            if (Regex.IsMatch(source, $@"(?:ConsumeFirstTouch|ShowMentorFirstTouch)\(\s*(?:\r?\n\s*)?{constName}\b"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The six dilemmas, THE-GAME.md §3.5's own order and prose paraphrased into a short
    /// key. #4 and #5 are honestly BLOCKED/MISSING, not silently taught -- see each detail string's
    /// own citation.</summary>
    private static readonly (int Number, string Key, DilemmaStatus Status)[] SixDilemmas =
    [
        (1, "sell-the-good-one-or-hold-it-for-the-hero-who-needs-it",
            new DilemmaStatus(DilemmaOutcome.Taught, "hold-or-sell")),
        (2, "price-for-the-sale-or-price-for-the-relationship",
            new DilemmaStatus(DilemmaOutcome.Taught, "pricing-as-a-decision")),
        (3, "fill-the-empty-slot-or-upgrade-the-full-one",
            new DilemmaStatus(DilemmaOutcome.Taught, "the-muster-speaks")),
        // Taught at last, and the history is worth keeping because this row has been wrong twice.
        // It first read Blocked on two citations that were both false the day they were written
        // (Dilemma4_IsNotBlockedWhileTheSlotCostExists exists to stop that recurring). It then read
        // Missing, which was true of the DILEMMA but missed the sharper fact: a lesson for this
        // mechanic already existed and was actively denying the cost -- "Unlocking one costs you
        // nothing" -- having been written before U-T1-9 charged a slot for it. Naming the cost
        // honestly is what teaches the dilemma; Dilemma4_LessonNamesTheSlotCost_NeverDeniesIt pins
        // the copy against drifting back.
        (4, "spend-the-slot-or-bank-it",
            new DilemmaStatus(DilemmaOutcome.Taught, "first-talent-unlock")),
        // U40 (§11.14.14): Taught at last, on an amended dilemma. The design doc used to promise a
        // player could overpay a hero for goodwill -- the sim has no such mechanism (BuyOreAction
        // carries no price parameter, and the hero always receives the base ask) -- so THE-GAME.md
        // §3.5 #5 was corrected, by owner ruling (KTD8), to the fork the sim actually ships: every
        // purchase raises the SUPPLYING FACTION's standing, which discounts every future load from
        // them, so the real choice is whose ore to buy. "the-tariff-fork" fires on the first
        // TariffApplied event -- the moment a standing-earned discount first moves what the player
        // pays -- MainUi's own call site cites register #170 for why this used to be impossible.
        (5, "buy-the-ore-or-buy-the-goodwill",
            new DilemmaStatus(DilemmaOutcome.Taught, "the-tariff-fork")),
        (6, "send-the-runner-or-trust-their-judgment",
            new DilemmaStatus(DilemmaOutcome.TaughtByNumberedStep, "TutorialStep.Vigil", TutorialStep.Vigil)),
    ];

    /// <summary>
    /// The reason text in a Blocked row is prose, and <see cref="SixDilemmas_EachHasAPinnedStatus"/>
    /// only checks that it is non-blank -- never that it is TRUE. Dilemma #4's row proved how much
    /// that matters: it claimed PR #549 was parked and that <c>ActionBudget.ConsumesSlot</c> still
    /// excluded <c>UnlockTalentAction</c>, and both halves were false at the moment they were
    /// written. A green test carrying a false justification for a real gap is worse than no test,
    /// because the next session reads the justification and moves on.
    ///
    /// <para>Prose cannot be verified in general. THIS claim can, so it is: the one fact the row
    /// rested on is now asserted directly against the sim. If the slot cost exists, #4 is not
    /// blocked on the slot cost — whatever else may be true of it.</para>
    /// </summary>
    /// <summary>
    /// Dilemma #4's copy, pinned against the exact drift that made it wrong. The lesson for this
    /// mechanic already existed and said "Unlocking one costs you nothing" — true when written, then
    /// falsified by U-T1-9 charging a day action slot for <c>UnlockTalentAction</c>. A lesson that
    /// denies a cost is worse than an absent one: the player has been told there is no trade-off to
    /// weigh, in the one place the trade-off lives.
    ///
    /// <para>So this asserts the two halves TOGETHER — the sim charges the slot, and the copy the
    /// player actually reads says so — because either one alone can go green while the pair lies.
    /// Driven through a real unlock press, reading the banner a person would read.</para>
    /// </summary>
    [TestCase]
    public void Dilemma4_LessonNamesTheSlotCost_NeverDeniesIt()
    {
        AssertThat(ActionBudget.ConsumesSlot(new UnlockTalentAction("keen-eye", "blacksmith")))
            .OverrideFailureMessage(
                "The mechanic half is gone: unlocking a talent no longer spends a slot. Then this "
                + "lesson's copy is wrong in the other direction and dilemma #4 has no mechanic — "
                + "invert both on purpose, in a diff.")
            .IsTrue();

        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, "Unlock_keen-eye");

            var text = Find<Label>(ui.Forge, "ForgeMentorText").Text;
            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The talents lesson never showed on the campaign's first-ever unlock press.")
                .IsTrue();
            AssertThat(text).Contains(MentorVoice.Name); // attributed to Bryn, never an anonymous tooltip
            AssertThat(text)
                .OverrideFailureMessage(
                    $"The talents lesson does not name the action slot the unlock spends. Copy: \"{text}\"")
                .Contains("action slot");
            AssertThat(text.Contains("costs you nothing"))
                .OverrideFailureMessage(
                    "The talents lesson is back to denying the cost — the exact sentence U-T1-9 "
                    + $"falsified. Copy: \"{text}\"")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void Dilemma4_IsNotBlockedWhileTheSlotCostExists()
    {
        var slotCostExists = ActionBudget.ConsumesSlot(new UnlockTalentAction("any", "blacksmith"));
        var fourth = SixDilemmas.First(d => d.Number == 4).Status;

        AssertThat(slotCostExists)
            .OverrideFailureMessage(
                "ActionBudget.ConsumesSlot no longer charges a slot for UnlockTalentAction. If that is "
                + "deliberate, dilemma #4 (\"spend the slot or bank it\") genuinely has no mechanic "
                + "again and this test should be inverted along with its status row -- but say so on "
                + "purpose, in a diff, rather than leaving the corpus to guess.")
            .IsTrue();

        AssertThat(fourth.Outcome != DilemmaOutcome.Blocked)
            .OverrideFailureMessage(
                "Dilemma #4 is pinned Blocked while UnlockTalentAction demonstrably costs an action "
                + "slot, so the mechanic the lesson would teach is live. Blocked means 'cannot be "
                + "taught yet'; the honest status for 'could be taught, nobody wrote it' is Missing.")
            .IsTrue();
    }

    [TestCase]
    public void SixDilemmas_EachHasAPinnedStatus()
    {
        AssertThat(SixDilemmas.Length)
            .OverrideFailureMessage("THE-GAME.md §3.5 names exactly six dilemmas -- this corpus must too.")
            .IsEqual(6);

        var problems = new System.Collections.Generic.List<string>();
        foreach (var (number, key, status) in SixDilemmas)
        {
            switch (status.Outcome)
            {
                case DilemmaOutcome.Taught when !FirstTouchIdIsWiredInSource(status.Detail):
                    problems.Add($"#{number} ({key}) claims first-touch id \"{status.Detail}\" but no live call site was found.");
                    break;
                case DilemmaOutcome.TaughtByNumberedStep when !TutorialFlow.Registry.Any(def => def.Step == status.Step):
                    problems.Add($"#{number} ({key}) claims {status.Step}, but TutorialFlow.Registry has no row for it.");
                    break;
                case DilemmaOutcome.Blocked or DilemmaOutcome.Missing when string.IsNullOrWhiteSpace(status.Detail):
                    problems.Add($"#{number} ({key}) is marked {status.Outcome} with a blank citation -- a real reason is required.");
                    break;
            }
        }

        AssertThat(problems.Count)
            .OverrideFailureMessage(string.Join("\n", problems))
            .IsEqual(0);

        // U40 (§11.14.14): ZERO of six is now untaught. Was one (#5, "buy the ore or buy the
        // goodwill") until THE-GAME.md §3.5 #5 was amended by owner ruling (KTD8) to the fork the
        // sim actually ships -- whose ore, not how much -- and "the-tariff-fork" first-touch lesson
        // was wired to teach it. Was two before that, until #4 ("spend the slot or bank it") was
        // taught by fixing the lesson that already existed for its mechanic and had gone false --
        // see that row's own note and Dilemma4_LessonNamesTheSlotCost_NeverDeniesIt. This is a
        // deliberate, reviewed drop to zero (the task that landed it cites this exact assertion) --
        // the corpus no longer needs an "honest gap" branch, but the branch itself stays live: a
        // COUNT INCREASE -- a dilemma silently regressing to blocked/missing -- is exactly the
        // erosion this pin exists to catch, going forward.
        var untaught = SixDilemmas.Count(d => d.Status.Outcome is DilemmaOutcome.Blocked or DilemmaOutcome.Missing);
        AssertThat(untaught)
            .OverrideFailureMessage(
                $"Expected all six dilemmas to be honestly taught today (the last one, #5, closed by " +
                $"U40's tariff-fork lesson); found {untaught} still blocked/missing. If this grew, a " +
                "dilemma silently lost its teaching without a citation; if it shrank further, this " +
                "assertion is already wrong on its face -- fix it in the same diff.")
            .IsEqual(0);
    }

    // ============================================================================================
    // U27 (§11.14.14, R14.7): "the tutorial names the six dilemmas out loud -- one sentence each,
    // both sides, no recommendation." The tests above each check ONE fork against a couple of
    // hand-picked phrases (afford/counter, shelf/commission, empty slot/full one...); this is the
    // corpus-wide tripwire -- every live dilemma text, gathered in one place, checked against the
    // SAME bank of recommendation-signaling phrases. This is the single most load-bearing test in
    // the unit: R14.7 is a design ruling a reviewer can forget to re-check on every future edit to
    // any of these six lines, and this test is what makes forgetting go red instead of silent.
    // ============================================================================================

    /// <summary>Drives the real hold-or-sell render path (no press) and returns the banner text.</summary>
    private static string CaptureHoldOrSellText()
    {
        var state = GameComposition.NewCampaign(seed: 27001) with { Commissions = ImmutableList.Create(OpenCommission(new HeroId(1))) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);
            return Find<Label>(ui.Mentor, "MentorBannerText").Text;
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Drives the real first-ever-Stock render path and returns the banner text.</summary>
    private static string CapturePricingText()
    {
        var baseState = GameComposition.NewCampaign(seed: 27002);
        var item = TestBuckler(new ItemId(27002));
        var state = baseState with { Items = baseState.Items.Add(item.Id.Value, item) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Stock_{item.Id.Value}");
            return Find<Label>(ui.Mentor, "MentorBannerText").Text;
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Drives the real forecast-board render path against a fresh campaign (starter heroes
    /// reliably carry a gear gap, the same fixture <c>WaveDLessonsTests</c> relies on) and returns
    /// the banner text.</summary>
    private static string CaptureMusterSpeaksText()
    {
        var ui = MountMainUi(new SimAdapter(GameComposition.NewCampaign(seed: 27003)));
        try
        {
            ui.Forecast.ShowForTomorrow(ui.Adapter.CurrentState);
            return Find<Label>(ui.Mentor, "MentorBannerText").Text;
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Drives the real first-ever-unlock render path (same fixture as
    /// <see cref="Dilemma4_LessonNamesTheSlotCost_NeverDeniesIt"/>) and returns the Forge's own
    /// mentor text.</summary>
    private static string CaptureFirstTalentUnlockText()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, "Unlock_keen-eye");
            return Find<Label>(ui.Forge, "ForgeMentorText").Text;
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Drives the real first-tariff render path (same fixture as
    /// <see cref="FirstTariffApplied_TeachesTheTariffForkDilemma"/>) and returns the banner text.</summary>
    private static string CaptureTariffForkText()
    {
        var baseState = GameComposition.NewCampaign(ScriptedSession.Seed);
        var state = baseState with
        {
            EventLog = baseState.EventLog.Add(
                new TariffApplied(FactionRegistry.DeepveinId, "copper", BaseLineCost: 100, PlayerCost: 90, Delta: -10)),
        };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            return Find<Label>(ui.Mentor, "MentorBannerText").Text;
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Dilemma #6 is taught by a NUMBERED step, not a first-touch lesson, so there is no
    /// banner render to drive -- its one fixed copy lives on the registry row itself
    /// (<see cref="TutorialStepDef.TeachNote"/>, the exact text <see cref="MentorVoice.CurrentLesson"/>
    /// speaks verbatim), read directly rather than duplicated as a literal.</summary>
    private static string VigilTeachNoteText() =>
        TutorialFlow.Registry.First(def => def.Step == TutorialStep.Vigil).TeachNote;

    /// <summary>
    /// R14.7 as an executable tripwire: gathers every one of the six dilemmas' LIVE copy (driven
    /// through its real surface, never a hand-retyped literal that could silently drift from what a
    /// player actually reads) and checks each against one shared bank of recommendation-signaling
    /// phrases. <see cref="MentorVoiceTests.HerOwnAuthoredLines_NeverReadAsACommand"/> already checks
    /// the wider corpus for "!"/"must", narrowly by construction (that file's own doc); this widens
    /// the phrase bank specifically for the six dilemmas, where a soft nudge ("the smart choice",
    /// "you should", "better to") would defeat R14.7 just as surely as a bare command would, without
    /// ever tripping the narrower check.
    /// </summary>
    [TestCase]
    public void SixDilemmas_NoForkCopyRecommendsAnArm()
    {
        (string Dilemma, string Text)[] captured =
        [
            ("#1 hold-or-sell", CaptureHoldOrSellText()),
            ("#2 pricing-as-a-decision", CapturePricingText()),
            ("#3 the-muster-speaks", CaptureMusterSpeaksText()),
            ("#4 first-talent-unlock", CaptureFirstTalentUnlockText()),
            ("#5 the-tariff-fork", CaptureTariffForkText()),
            ("#6 send-the-runner (TutorialStep.Vigil)", VigilTeachNoteText()),
        ];

        string[] recommendationSignals =
        [
            "you should", "we recommend", "i recommend", "recommend", " must ", "the best choice",
            "the right choice", "the right call", "the smart choice", "the safe choice",
            "better to ", "it's better", "always choose", "you'll want to", "make sure to",
        ];

        var problems = new System.Collections.Generic.List<string>();
        foreach (var (dilemma, text) in captured)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                problems.Add($"{dilemma}: captured no text at all -- setup is broken, not a register defect.");
                continue;
            }

            if (text.TrimEnd().EndsWith('!'))
            {
                problems.Add($"{dilemma}: ends with an exclamation -- reads as an order, not a dilemma. \"{text}\"");
            }

            foreach (var signal in recommendationSignals)
            {
                if (text.Contains(signal, System.StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{dilemma}: contains \"{signal}\" -- reads as a recommendation, which R14.7 forbids. \"{text}\"");
                }
            }
        }

        AssertThat(problems.Count)
            .OverrideFailureMessage(string.Join("\n", problems))
            .IsEqual(0);
    }
}
#endif
