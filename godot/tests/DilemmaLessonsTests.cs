#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave C (§11.14.4, Act II, R14.7 "the tutorial names the six dilemmas out loud — one
/// sentence each, both sides, no recommendation"): the two dilemmas this wave teaches — "pricing
/// as a decision" (dilemma #2, fires the first time the player ever reprices a shelf item) and
/// "hold-or-sell" (dilemma #1, fires the first time the player ever accepts a commission) — both
/// routed through the SAME shared <see cref="MentorBanner"/> Wave C introduces rather than a
/// panel-private copy, and the SAME <see cref="TutorialFlow.ConsumeFirstTouch"/> once-ever engine
/// Wave A shipped.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DilemmaLessonsTests
{
    private static readonly ItemId ShelvedItemId = new(9401);

    private static GameState StateWithOneShelvedItem()
    {
        var baseState = GameComposition.NewCampaign(seed: 9401);
        var item = new Item(
            ShelvedItemId, "test-dilemma-shelf-item", "Test Buckler", ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(Attack: 4, Defense: 0, Weight: 2),
            new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
        };
    }

    /// <summary>Pricing is one of the two dilemmas the census named as wholly untaught before this
    /// unit (plan §11.14.4's own finding). Reprice's own Wave-C wiring must reach the shared
    /// banner regardless of which control drove it (the legacy button, exercised here).</summary>
    [TestCase]
    public void RepricingAShelfItemForTheFirstTime_TeachesThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneShelvedItem()));
        try
        {
            ui.OpenPanel("Shop");

            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().Any(a => a.Item == ShelvedItemId))
                .OverrideFailureMessage("Setup check: the Reprice press did not queue a SetPriceAction.")
                .IsTrue();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The pricing dilemma never showed on the player's first-ever reprice.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name); // attributed to Bryn, never an anonymous tooltip
            AssertThat(text).Contains("relationship");
            AssertThat(text).Contains("sale");

            // R14.7: no recommendation — the lesson must not tell the player which side to take.
            AssertThat(text.Contains("you should", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The pricing dilemma reads as a recommendation, not a naming of both sides: \"{text}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A second reprice must NOT re-fire the lesson — the same once-ever contract every
    /// other first-touch lesson in this codebase carries (the 1287x memorial nag precedent).</summary>
    [TestCase]
    public void RepricingASecondTime_DoesNotReshowThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneShelvedItem()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");
            ui.Mentor.Dismiss();

            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The pricing dilemma fired a SECOND time — the anti-nag pin failed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Hold-or-sell is the other dilemma named in Wave C's own unit list — accepting a
    /// commission is the moment the player chooses to hold a slot for one named hero rather than
    /// sell freely off the shelf.</summary>
    [TestCase]
    public void AcceptingACommissionForTheFirstTime_TeachesTheHoldOrSellDilemma()
    {
        var commission = new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30);
        var state = GameComposition.NewCampaign(seed: 9402) with { Commissions = ImmutableList.Create(commission) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            PressEnabled(ui.Commissions, "CommissionAccept_1");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<AcceptCommissionAction>().Any(a => a.Hero == new HeroId(1)))
                .OverrideFailureMessage("Setup check: the Accept press did not queue an AcceptCommissionAction.")
                .IsTrue();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The hold-or-sell dilemma never showed on the player's first-ever commission accept.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("shelf");
            AssertThat(text).Contains("commission");
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
        var ui = MountMainUi(new SimAdapter(StateWithOneShelvedItem()));
        var player = new HumanPlayer(ui);
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");
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
        (4, "spend-the-slot-or-bank-it",
            new DilemmaStatus(DilemmaOutcome.Blocked,
                "PR #549 is parked (inverts a hero-trait relationship: prepared heroes dying more than " +
                "reckless ones); on main today ActionBudget.ConsumesSlot still excludes UnlockTalentAction, " +
                "so the dilemma has no live mechanic to teach yet. Owner ruling, Wave D report: 'Dilemma " +
                "#4's lesson is blocked on #549, not on you.'")),
        (5, "buy-the-ore-or-buy-the-goodwill",
            new DilemmaStatus(DilemmaOutcome.Missing,
                "Issue #170 / docs/playtests/2026-08-16-owner-playtest-register.md: OreMarketHandlers " +
                "always pays the hero the base ask; a player paying more feeds a faction sink, not the " +
                "hero, and the surcharge branch is commented dormant. The goodwill half of the dilemma " +
                "has no mechanism behind it -- teaching it would make the game lie about its own choices.")),
        (6, "send-the-runner-or-trust-their-judgment",
            new DilemmaStatus(DilemmaOutcome.TaughtByNumberedStep, "TutorialStep.Vigil", TutorialStep.Vigil)),
    ];

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

        // Two of six are honestly not-yet-taught -- if that count ever drops to zero this test
        // itself should be revisited (the corpus stops needing an "honest gap" branch at all), but a
        // COUNT INCREASE (a third dilemma silently regresses to blocked/missing) is exactly the
        // erosion this pin exists to catch.
        var untaught = SixDilemmas.Count(d => d.Status.Outcome is DilemmaOutcome.Blocked or DilemmaOutcome.Missing);
        AssertThat(untaught)
            .OverrideFailureMessage(
                $"Expected exactly 2 of the six dilemmas (#4 slot budget, #5 ore gift) to be honestly " +
                $"blocked/missing today; found {untaught}. If this grew, a dilemma silently lost its " +
                "teaching without a citation; if it shrank, update this pin to match the fix that landed.")
            .IsEqual(2);
    }
}
#endif
