#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The stranger test, made executable. The owner's complaint was not that a step was wrong — every
/// step was correct — but that a step written by someone who already knows the answer is not a
/// step: <i>"step 2 for example doesn't explain WHERE in the shop to add stuff"</i>, <i>"Tutorial 4
/// — HOW to watch them depart??"</i>, <i>"need to explain how the bounties work further"</i>.
///
/// <para>Correct-but-unfollowable copy cannot be caught by asserting the step machine advances,
/// which is what every other tutorial suite here does — the machine was already right in all three
/// of those cases. What it CAN be caught by is the specific failure underneath all three: <b>the
/// copy naming a thing the screen does not call that</b>. "Shelve it" when the button says Stock.
/// "Open Hero Cards from the tray" when the tray's buttons have no text at all and that one's
/// tooltip says Renown. "Press Next/Advance" when the advance control is labelled for whatever it
/// is about to do. "The winch-house slate", "the bell row", "the noticeboard" — three pieces of
/// vocabulary that appear in no label anywhere in the game.</para>
///
/// <para>So this suite pins the vocabulary JOIN, not the prose: each step's copy must quote the
/// literal words a player can see, and wherever the game owns those words in code (<see
/// cref="PhaseVocab.BellVerb"/>, a tray button's own <see cref="Control.TooltipText"/>) the
/// assertion reads them from there rather than retyping them — so renaming a control turns the
/// tutorial line that names it RED instead of quietly making it a lie. Plus the two structural
/// guards the ask names outright: the step count, and no step ever shipping an empty string.</para>
///
/// <para>Every case reads copy through <see cref="TutorialFlow.CopyFor"/> against a projected
/// state, never by driving a campaign — deliberately, so this suite has no dependency whatsoever on
/// the phase/beat advance machinery it would otherwise have to walk three in-game days of.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialCopyIsFollowableTests
{
    /// <summary>The one denominator every line prints ("Tutorial N/10"). Hand-typed HERE on
    /// purpose, unlike in production where it is derived: a test that computes the expected value
    /// the same way the code does pins nothing.</summary>
    private const int ExpectedDisplayedSteps = 10;

    /// <summary>
    /// Per step, the literal on-screen strings its copy must contain — a control's own printed
    /// text, a section heading, or a named place. Not a keyword allowlist: each entry is a thing a
    /// stranger can find by reading the screen, and a step that names none of them is a step whose
    /// reader has to already know the answer.
    /// </summary>
    private static readonly Dictionary<TutorialStep, string[]> MustName = new()
    {
        // WHERE (the workshop) and HOW (walk with WASD, then E at a station — the gesture that
        // opens the panel where buying and crafting both live).
        [TutorialStep.BuyMaterial] = ["Forge", "WASD", "press E"],
        [TutorialStep.Craft] = ["Forge", "press E"],

        // The owner's step 2, verbatim: "doesn't explain WHERE in the shop to add stuff". The
        // section heading and the button's own word are the answer, and the button says Stock.
        [TutorialStep.Shelve] = ["Shop", "Unshelved Crafts", "Stock"],

        [TutorialStep.PostBounty] = ["Bounties", "POST BOUNTY", "Post"],

        // "HOW to watch them depart??" — the answer is a button somewhere else entirely.
        [TutorialStep.WatchDeparture] = ["Mine Gate", "top of the screen"],

        [TutorialStep.LookIn] = ["Watch", "top of the screen"],
        // U2 (tutorial-revamp plan, §11.13): re-scoped alongside the row's own IsDone/copy rewrite
        // — completion no longer requires a closed sale, so the copy no longer promises specific
        // Present/Accept/Hold Firm/Counter buttons (any one of several verbs satisfies the step;
        // naming all of them made the OLD copy read as a checklist of required presses). "Open
        // Counter" is still the one verb the step actually gates on.
        [TutorialStep.OpenCounter] = ["Shop", "Open Counter"],
        [TutorialStep.Vigil] = ["Send", "Recall"],
        [TutorialStep.EveningClose] = ["EVENING LEDGER", "ORE OFFERED", "Buy"],

        // The tray is icon-only; the words exist solely as tooltips (asserted against the live
        // buttons in TrayStepCopy_... below).
        [TutorialStep.MeetHeroes] = ["top right", "Renown"],
        [TutorialStep.Commission] = ["tray", "Commissions", "Accept", "Decline"],
    };

    /// <summary>Vocabulary that names something the screen does not: three phrases the owner
    /// tripped over, plus the two words the advance control has never printed.</summary>
    private static readonly string[] BannedVocabulary =
    [
        "press Next", "Next/Advance", "bell row", "winch-house", "noticeboard",
    ];

    [TestCase]
    public void TheChainShows_ExactlyTenNumberedSteps_AndTheChecklistHasARowForEveryOne()
    {
        // A silently lost step is the failure nobody sees until a human hits it: the denominator
        // every line prints moves, the ladder renders one row shorter, and nothing goes red.
        AssertThat(TutorialFlow.TotalSteps)
            .OverrideFailureMessage(
                $"The tutorial now shows {TutorialFlow.TotalSteps} numbered steps, not {ExpectedDisplayedSteps} — " +
                "every \"Tutorial N/10\" line in the game just renumbered. If that is intended, change " +
                "ExpectedDisplayedSteps here in the same commit.")
            .IsEqual(ExpectedDisplayedSteps);

        var ui = MountMainUi();
        try
        {
            var rows = ui.Tutorial.Checklist(ui.Adapter.CurrentState);
            AssertThat(rows.Count)
                .OverrideFailureMessage(
                    $"The checklist renders {rows.Count} rows for {TutorialFlow.TotalSteps} displayed steps — " +
                    "a step exists that the player can never see coming.")
                .IsEqual(TutorialFlow.TotalSteps);

            for (var i = 0; i < rows.Count; i++)
            {
                AssertThat(rows[i].DisplayIndex)
                    .OverrideFailureMessage($"Checklist row {i} is numbered {rows[i].DisplayIndex} — the ladder has a gap.")
                    .IsEqual(i + 1);
                AssertThat(string.IsNullOrWhiteSpace(rows[i].Label))
                    .OverrideFailureMessage($"Checklist row {i + 1} renders a blank label.")
                    .IsFalse();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveryStepsCopy_IsNonEmpty_AndNamesSomethingTheStrangerCanActuallyFindOnScreen()
    {
        var ui = MountMainUi();
        try
        {
            var world = ui.Adapter.CurrentState;
            foreach (var step in Enum.GetValues<TutorialStep>())
            {
                var copy = Plain(ui.Tutorial.CopyFor(step, ActionableFor(world, step)));

                AssertThat(string.IsNullOrWhiteSpace(copy))
                    .OverrideFailureMessage(
                        $"{step} renders an EMPTY tutorial line. On screen that is a step number over blank " +
                        "space, and nothing else in the build reports it.")
                    .IsFalse();

                AssertThat(copy)
                    .OverrideFailureMessage($"{step}'s line does not carry its step number: \"{copy}\"")
                    .Contains($"/{TutorialFlow.TotalSteps}:");

                foreach (var needle in MustName[step])
                {
                    AssertThat(copy.Contains(needle, StringComparison.Ordinal))
                        .OverrideFailureMessage(
                            $"{step}'s line never says \"{needle}\", so a stranger reading only this sentence " +
                            $"cannot find the place or the control it means:\n  \"{copy}\"")
                        .IsTrue();
                }
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The failure mode on the OTHER side of this fix, and the one that nearly shipped inside it: a
    /// tutorial line is rendered UNCLAMPED (<c>ObjectiveTracker.Refresh</c> turns off ClipText and
    /// TrimEllipsis for tutorial text on purpose, because the two-line clamp used to eat the half of
    /// the sentence that says what to do). Unclamped means a long enough line grows the objective
    /// chip until it runs off the bottom of the window — the "still cutoff" class of bug this repo
    /// has fixed three times. Nothing else measures it, because the height guard
    /// (<c>HudBoundsTests.ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel</c>) only ever sees
    /// day 1's step 1; steps 2-10 are unmeasured by anything.
    /// </summary>
    [TestCase]
    public void NoStepsCopy_OutgrowsTheObjectiveCardsOwnUnclampedLineBudget()
    {
        // ObjectiveTracker reserves six unclamped WordSmart lines at its 296px text width, which is
        // about 40 characters a line at the body font. Deliberately a character count and not a
        // measured rect: the point is to fail while someone is WRITING the copy, not after a
        // layout pass on one particular window size.
        const int budget = 6 * 40;

        var ui = MountMainUi();
        try
        {
            var world = ui.Adapter.CurrentState;
            foreach (var step in Enum.GetValues<TutorialStep>())
            {
                var copy = Plain(ui.Tutorial.CopyFor(step, ActionableFor(world, step)));
                AssertThat(copy.Length)
                    .OverrideFailureMessage(
                        $"{step}'s line is {copy.Length} characters — past the {budget} the objective card reserves, " +
                        "so it grows the chip instead of fitting in it. Move the explanation into the step's " +
                        $"TeachNote (which renders inside the scrolling checklist and costs no height):\n  \"{copy}\"")
                    .IsLessEqual(budget);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Step 5's OTHER line — the one the owner actually hit. The Watch control is on screen only
    /// while a party is underground, so in any other phase step 5 cannot name it, and for a while it
    /// named it anyway ("it auto jumped to night???? yet this is still on tutorial 5???"). What the
    /// wait variant owes a stranger instead is the press that brings the Mirror back, in the words
    /// the button prints — read from <see cref="PhaseVocab"/>, not retyped, so a reworded bell turns
    /// this red rather than quietly sending them hunting.
    /// </summary>
    [TestCase]
    public void LookInsWaitVariant_NamesThePressThatBringsTheMirrorBack_NotTheControlThatIsNotThere()
    {
        var ui = MountMainUi();
        try
        {
            // Morning: a party is structurally NOT out, so this is the branch the screen renders.
            var waiting = ui.Adapter.CurrentState with { Day = 3, Phase = DayPhase.Morning };
            var copy = Plain(ui.Tutorial.CopyFor(TutorialStep.LookIn, waiting));

            AssertThat(string.IsNullOrWhiteSpace(copy))
                .OverrideFailureMessage(
                    "Step 5 renders a BLANK card whenever no party is out — which is most of the day. " +
                    "The whole job of this surface is telling the player what to do.")
                .IsFalse();

            var morningBell = PhaseVocab.BellVerb(waiting with { Phase = DayPhase.Morning });
            AssertThat(copy)
                .OverrideFailureMessage(
                    $"Step 5's wait line never names the press that puts a party underground (\"{morningBell}\"), " +
                    $"so a stranger is left waiting for a button that is not coming:\n  \"{copy}\"")
                .Contains(morningBell);

            AssertThat(copy.Contains("Watch", StringComparison.Ordinal))
                .OverrideFailureMessage(
                    "Step 5's wait line points at the Watch control, which is not on screen in this phase — " +
                    $"the exact defect the owner reported on 2026-08-09:\n  \"{copy}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NoStepCopy_NamesAControlTheScreenDoesNotHave()
    {
        var ui = MountMainUi();
        try
        {
            var day1 = ui.Adapter.CurrentState;
            foreach (var step in Enum.GetValues<TutorialStep>())
            {
                // BOTH branches: the actionable instruction AND the day-1 wait/gated variant, which
                // is where "press Next/Advance" lived and survived three rounds of copy fixes.
                var later = ActionableFor(day1, step);
                foreach (var copy in new[] { Plain(ui.Tutorial.CopyFor(step, later)), Plain(ui.Tutorial.CopyFor(step, day1)) })
                {
                    foreach (var banned in BannedVocabulary)
                    {
                        AssertThat(copy.Contains(banned, StringComparison.OrdinalIgnoreCase))
                            .OverrideFailureMessage(
                                $"{step}'s copy says \"{banned}\", which is not printed anywhere on screen — the " +
                                $"player is being sent to look for words that do not exist:\n  \"{copy}\"")
                            .IsFalse();
                    }
                }
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TheDepartureAndCloseSteps_QuoteTheAdvanceControlByItsRealPrintedLabel()
    {
        // "HOW to watch them depart??" — the departure is caused by ending the Morning, so the copy
        // has to name the button that does it. Read from PhaseVocab (the button's own source of
        // truth) rather than retyped, so a reworded bell turns these two lines red instead of
        // quietly sending the player to press something that is no longer called that.
        var ui = MountMainUi();
        try
        {
            var state = Actionable(ui.Adapter.CurrentState);
            var morningBell = PhaseVocab.BellVerb(state with { Phase = DayPhase.Morning });
            var eveningBell = PhaseVocab.BellVerb(state with { Phase = DayPhase.Evening });

            var departure = Plain(ui.Tutorial.CopyFor(TutorialStep.WatchDeparture, state));
            AssertThat(departure)
                .OverrideFailureMessage(
                    $"The departure step never names the control that CAUSES the departure (\"{morningBell}\"), " +
                    $"so it says what to watch and not how:\n  \"{departure}\"")
                .Contains(morningBell);

            var close = Plain(ui.Tutorial.CopyFor(TutorialStep.EveningClose, state));
            AssertThat(close)
                .OverrideFailureMessage($"The evening step does not name the bell's real label (\"{eveningBell}\"):\n  \"{close}\"")
                .Contains(eveningBell);

            // And that control is really there, under the name both lines describe by position.
            AssertThat(ui.FindChild("AdvancePhase", recursive: true, owned: false) as Button)
                .OverrideFailureMessage("Both lines point at an advance button that does not exist in the live scene.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TheTraySteps_QuoteTheTooltipsTheTrayButtonsActuallyCarry_NotTheirPanelTitles()
    {
        // The tray's buttons have EMPTY Text — the words live only in tooltips, and HeroCards' is
        // "Renown". "Open Hero Cards from the tray" sent a stranger hunting two words that appear
        // on screen nowhere. This is the join, asserted against the live buttons.
        //
        // U3 (tutorial-revamp plan, §11.13): HeroCards/Commissions are now SurfaceUnlocks-gated —
        // while closed, MainUi.RefreshSurfaceUnlocks swaps the GATE's own Reason onto the
        // button's TooltipText (SurfaceUnlocks' own doc: "greyed, not hidden"), which is a
        // DIFFERENT sentence than the substantive tooltip step 9/10's copy quotes. A fresh
        // MountMainUi() never earned either gate (no sale, no commission), so the live buttons
        // read their CLOSED-gate reason here, not the OpenTooltip this test means to check —
        // the fixture no longer reaches the surface the way a player actually would by the time
        // they read these two steps. Mounting with those two facts already true (a player can
        // legitimately have neither by step 9/10 — SurfaceUnlocks' own "no gate may hide a
        // tutorial anchor" pin exists for exactly that case — but CAN also already have both,
        // and only that path lets this suite check the tooltip join without driving the
        // tutorial's own step machine, which it deliberately never does elsewhere) earns both
        // gates for real, matching MainUi.SurfaceEffectivelyOpen's non-override branch.
        var earned = GameComposition.NewCampaign(9703) with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new ItemSold(new ItemId(1), new HeroId(1), 10, FromPlayerShop: true) { Id = new EventId(1), Day = 1 },
                new CommissionPosted(new HeroId(1), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 5, PremiumGold: 10)
                    { Id = new EventId(2), Day = 1 }),
        };
        var ui = MountMainUi(new SimAdapter(earned));
        try
        {
            var state = Actionable(ui.Adapter.CurrentState);

            var heroes = ui.FindChild("OpenHeroCards", recursive: true, owned: false) as Button;
            AssertThat(heroes).OverrideFailureMessage("The tray's hero-cards button is missing.").IsNotNull();
            AssertThat(heroes!.Text)
                .OverrideFailureMessage(
                    $"The tray button now prints \"{heroes.Text}\" — if the tray gained real labels, this suite's " +
                    "whole premise (tooltip-only tray) needs rechecking, not just this assertion.")
                .IsEqual(string.Empty);
            AssertThat(Plain(ui.Tutorial.CopyFor(TutorialStep.MeetHeroes, state)))
                .OverrideFailureMessage(
                    $"Step 9 does not name the tooltip the button actually carries (\"{heroes.TooltipText}\").")
                .Contains(heroes.TooltipText);

            var commissions = ui.FindChild("OpenCommissions", recursive: true, owned: false) as Button;
            AssertThat(commissions).OverrideFailureMessage("The tray's commissions button is missing.").IsNotNull();
            AssertThat(Plain(ui.Tutorial.CopyFor(TutorialStep.Commission, state)))
                .OverrideFailureMessage(
                    $"Step 10 does not name the tooltip the button actually carries (\"{commissions!.TooltipText}\").")
                .Contains(commissions.TooltipText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveryStep_CarriesATeachNote_AndTheCurrentOnesReachesTheScreen()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var def in TutorialFlow.Registry)
            {
                AssertThat(string.IsNullOrWhiteSpace(def.TeachNote))
                    .OverrideFailureMessage($"{def.Step}: TeachNote is empty — the step teaches nothing about itself.")
                    .IsFalse();
                AssertThat(def.TeachNote.Trim())
                    .OverrideFailureMessage(
                        $"{def.Step}: TeachNote just restates the label (\"{def.ShortLabel}\") instead of explaining " +
                        "what the mechanism is.")
                    .IsNotEqual(def.ShortLabel.Trim());
            }

            // Exactly one row carries it, and it is the current one — ten explanations at once is a
            // wall, not a lesson.
            var rows = ui.Tutorial.Checklist(ui.Adapter.CurrentState);
            AssertThat(rows.Count(r => r.TeachNote is not null))
                .OverrideFailureMessage("The teach note should be attached to exactly one checklist row (the current one).")
                .IsEqual(1);
            AssertThat(rows.Single(r => r.TeachNote is not null).Current).IsTrue();

            // And it is on the SCREEN. The whole reason the notes were wrong for so long is that
            // nothing rendered them: ten paragraphs of teaching copy no player had ever read.
            var label = ui.Objective.FindChild("TutorialChecklistTeachNote", recursive: true, owned: false) as Label;
            AssertThat(label)
                .OverrideFailureMessage(
                    "The current step's teach note is not rendered anywhere in the objective dock — it is written, " +
                    "pinned non-empty, and invisible, which is exactly how it drifted into describing a mechanism " +
                    "the game does not have.")
                .IsNotNull();
            AssertThat(label!.Text)
                .IsEqual(Plain(rows.Single(r => r.Current).TeachNote!));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TheBountyStep_AndTheBountyPanel_BothExplainWhatABountyActuallyIs()
    {
        // "need to explain how the bounties work further - confusing". Four facts, because a poster
        // who does not know them cannot price a bounty: the gold goes now, a hero chooses, the hero
        // keeps it, and it comes back if nobody takes it. The old teach note said a bounty asks for
        // an ITEM, which is a mechanism the sim does not have — PostBountyAction names a floor.
        var ui = MountMainUi();
        try
        {
            var note = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.PostBounty).TeachNote;
            AssertThat(note).Contains("floor");
            AssertThat(note.Contains("fetch", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The bounty note still describes fetching an item, which bounties do not do: \"{note}\"")
                .IsFalse();

            var panel = RenderedText(ui.Bounties);
            foreach (var fact in new[] { "leaves your purse", "keeps the gold", "refuse", "comes back" })
            {
                AssertThat(panel.Contains(fact, StringComparison.OrdinalIgnoreCase))
                    .OverrideFailureMessage(
                        $"The Bounties panel never says \"{fact}\" — the player is asked to set a price for a " +
                        "transaction the screen never describes.")
                    .IsTrue();
            }

            AssertThat(ui.Bounties.FindChild("BountyExplainer", recursive: true, owned: false) as Label)
                .OverrideFailureMessage("The bounty explainer label is gone from the panel.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Day 3 Morning with a full slot budget — the projection under which nearly every step
    /// in the chain is actionable, so <see cref="TutorialFlow.CopyFor"/> returns the real instruction
    /// rather than a day-gate or phase-gate wait variant. A pure <c>with</c> projection, never a
    /// driven campaign: this suite must not depend on phase advance at all.</summary>
    private static GameState Actionable(GameState state) => ActionableFor(state, TutorialStep.BuyMaterial);

    /// <summary>
    /// The same projection, per step — because ONE phase no longer makes the whole chain actionable
    /// and cannot: <see cref="TutorialStep.LookIn"/>'s control (Watch) exists only while a party is
    /// underground, so Morning renders its wait variant instead of the instruction. Asking for the
    /// instruction in a phase that structurally cannot show it is how this suite read a correct line
    /// as a missing one. The wait variant is not skipped by this — it has its own case below.
    /// </summary>
    private static GameState ActionableFor(GameState state, TutorialStep step) =>
        state with
        {
            Day = 3,
            Phase = step == TutorialStep.LookIn ? DayPhase.Expedition : DayPhase.Morning,
            ActionSlotsRemaining = ActionBudget.SlotsPerDay,
        };

    /// <summary>Read the copy the way the screen does — <c>Label</c> has no markup parser, so the
    /// panel strips the emphasis markers before rendering (<see cref="ObjectiveTracker.Plain"/>).
    /// Asserting on the raw string would let a needle "match" across a marker the player never sees.</summary>
    private static string Plain(string copy) => ObjectiveTracker.Plain(copy);
}
#endif
