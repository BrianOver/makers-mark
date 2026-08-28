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
///
/// <para><b>U2 (§11.14.14) widens jurisdiction to the primer.</b> This whole class of defect —
/// copy naming a control the screen does not have — is not unique to the in-course tutorial: the
/// FIRST screen a new player reads, <c>NewGameSelect</c>'s "your first day" primer, carried the
/// exact same shape ("phases advance automatically", "press Advance") one scene BEFORE the course
/// even starts, where none of the cases above could ever see it. The primer cases below
/// (<c>ThePrimersClockNote_...</c>) apply this suite's own method — read a control's real printed
/// label through <see cref="PhaseVocab"/> rather than retyping it — to that scene too, so a
/// reworded bell or a flipped auto-advance default goes red there as well as in the course.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialCopyIsFollowableTests
{
    /// <summary>The chain's own total displayed-step count (no longer printed as one global
    /// denominator — U-T2-1 numbers within acts now). Hand-typed HERE on purpose, unlike in
    /// production where it is derived: a test that computes the expected value the same way the
    /// code does pins nothing.</summary>
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

    /// <summary>
    /// U-T2-1 (owner ruling, §11.13): the chain now numbers WITHIN acts — "The Hand-Off · 2/4",
    /// never "Tutorial 7/24" — because a countdown to ten was never going to survive becoming a
    /// countdown to twenty-four once the pointed chain outgrows day 3 (the owner's own ruling: the
    /// pointed chain now runs through day 7). <see cref="TutorialFlow.TotalSteps"/> still comes from
    /// the registry, never hand-typed, and the checklist still renders one row per displayed step —
    /// what changed is that the CONTIGUITY pin (formerly one flat 1..10 run) is now per-act: every
    /// act's own displayed steps number 1..N with no gap, and no step's copy claims a GLOBAL
    /// denominator any more.
    /// </summary>
    [TestCase]
    public void EveryAct_NumbersItsOwnBeats_AndNoBeatClaimsAGlobalDenominator()
    {
        // A silently lost step is the failure nobody sees until a human hits it: the total every
        // act's own count derives from moves, the ladder renders one row shorter, and nothing goes
        // red.
        AssertThat(TutorialFlow.TotalSteps)
            .OverrideFailureMessage(
                $"The tutorial now shows {TutorialFlow.TotalSteps} numbered steps, not {ExpectedDisplayedSteps} — " +
                "at least one act's own denominator just changed. If that is intended, change " +
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

            foreach (var row in rows)
            {
                AssertThat(string.IsNullOrWhiteSpace(row.Label))
                    .OverrideFailureMessage($"Checklist row {row.DisplayIndex} renders a blank label.")
                    .IsFalse();
            }

            // Per-act contiguity: every act's own displayed steps number 1..N with no gap, and each
            // act's own total matches how many displayed steps it actually has.
            var slotsByAct = TutorialFlow.Registry
                .Select(d => (d.DisplayIndex, d.Act, d.Step))
                .GroupBy(s => s.DisplayIndex)
                .Select(g => g.First())
                .OrderBy(s => s.DisplayIndex)
                .GroupBy(s => s.Act);
            foreach (var actGroup in slotsByAct)
            {
                var slots = actGroup.ToList();
                for (var i = 0; i < slots.Count; i++)
                {
                    var (position, total) = TutorialFlow.ActPosition(slots[i].Step);
                    AssertThat(position)
                        .OverrideFailureMessage(
                            $"{actGroup.Key}'s slot at DisplayIndex {slots[i].DisplayIndex} reads position " +
                            $"{position}, not {i + 1} — that act's own ladder has a gap.")
                        .IsEqual(i + 1);
                    AssertThat(total)
                        .OverrideFailureMessage(
                            $"{actGroup.Key}'s own total ({total}) does not match its actual step count ({slots.Count}).")
                        .IsEqual(slots.Count);
                }
            }

            // And no step's rendered copy claims the OLD global "N/10"-shaped denominator.
            var world = ui.Adapter.CurrentState;
            foreach (var step in Enum.GetValues<TutorialStep>())
            {
                var copy = Plain(ui.Tutorial.CopyFor(step, world));
                AssertThat(copy.Contains($"/{TutorialFlow.TotalSteps}", StringComparison.Ordinal))
                    .OverrideFailureMessage(
                        $"{step}'s copy still claims a global \"/{TutorialFlow.TotalSteps}\" denominator: \"{copy}\"")
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

                // U-T2-1: the prefix is act-scoped now ("The Hand-Off · 2/4:"), not a global
                // "N/10" denominator — check the join against TutorialFlow.ActPosition itself so
                // this never has to re-derive the act's own display name.
                var (position, total) = TutorialFlow.ActPosition(step);
                AssertThat(copy)
                    .OverrideFailureMessage($"{step}'s line does not carry its own act-scoped position: \"{copy}\"")
                    .Contains($"{position}/{total}:");

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

    /// <summary>
    /// U2 (§11.14.14): the primer's own clock note (<c>NewGameSelect.ClockNote</c>) is built EAGERLY
    /// at <c>_Ready</c>/<c>BuildUi</c> regardless of which of the four views is visible — same
    /// eager-build discipline the class doc on <c>NewGameSelect</c> already relies on for the
    /// FullPlaytest picker shortcut — so a bare mount is enough to read it; no NewGame/Pick press
    /// needed first.
    /// </summary>
    [TestCase]
    public void ThePrimersClockNote_QuotesTheSameBellLabels_AndTheControlBehindThemReallyExists()
    {
        // The exact join TheDepartureAndCloseSteps_... above pins for the course, applied one
        // scene earlier: read the words from PhaseVocab (the button's own source of truth) rather
        // than retyping them, so a reworded bell turns the primer's copy red too, not just the
        // tutorial's.
        var morningBell = PhaseVocab.BellVerb(DayPhase.Morning);
        var eveningBell = PhaseVocab.BellVerb(DayPhase.Evening);

        var primer = MountNewGameSelect();
        try
        {
            var clockNote = Find<Label>(primer, "ClockNote").Text;
            AssertThat(clockNote)
                .OverrideFailureMessage(
                    $"The primer's clock note never names the morning bell's real label (\"{morningBell}\"), " +
                    $"so a brand-new player is told to look for a press that prints something else:\n  \"{clockNote}\"")
                .Contains(morningBell);
            AssertThat(clockNote)
                .OverrideFailureMessage(
                    $"The primer's clock note never names the evening bell's real label (\"{eveningBell}\"):\n  \"{clockNote}\"")
                .Contains(eveningBell);
        }
        finally
        {
            UnmountNewGameSelect(primer);
        }

        // And the control those two words describe is really there, under the name both this suite
        // and TheDepartureAndCloseSteps_... above already pin.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.FindChild("AdvancePhase", recursive: true, owned: false) as Button)
                .OverrideFailureMessage(
                    "The primer's clock note describes a bell that does not exist in the live game.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The OLD defect's other half, pinned against the fact rather than the wording: the primer
    /// said "phases advance automatically" while <see cref="PhaseClock.AutoAdvance"/>'s own field
    /// default is OFF (player-decided — <c>MainUi</c>'s boot sequence only ever flips it on from a
    /// PERSISTED opt-in, never a fresh install). Reading the real default off a freshly constructed
    /// <see cref="PhaseClock"/>, rather than assuming it, means a future default flip (back to
    /// timed, or to some third mode) turns this red instead of leaving the primer's copy to drift
    /// silently the way it did the first time.
    /// </summary>
    [TestCase]
    public void ThePrimersClockNote_MatchesPhaseClocksActualAutoAdvanceDefault()
    {
        var manualByDefault = !new PhaseClock(new SimAdapter(GameComposition.NewCampaign(1))).AutoAdvance;

        var primer = MountNewGameSelect();
        try
        {
            var clockNote = Find<Label>(primer, "ClockNote").Text;

            AssertThat(clockNote.Contains("automatically", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage(
                    $"The primer claims phases \"advance automatically\" while PhaseClock's actual default is " +
                    $"{(manualByDefault ? "manual" : "automatic")}:\n  \"{clockNote}\"")
                .IsEqual(!manualByDefault);

            AssertThat(clockNote.Contains("waits for you", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage(
                    $"The primer never says the day waits for the player, though PhaseClock's actual default " +
                    $"is {(manualByDefault ? "manual" : "automatic")}:\n  \"{clockNote}\"")
                .IsEqual(manualByDefault);
        }
        finally
        {
            UnmountNewGameSelect(primer);
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

    /// <summary>
    /// U20 (§11.14.14, "the two absences" #1): leaving a room was never taught anywhere, and step 2
    /// (Shelve) is the FIRST one that requires it — walking back out of the workshop to the Shop.
    /// Pinned on BuyMaterial's own TeachNote (DisplayIndex 1), not Craft's: <see cref="TutorialFlow
    /// .Step"/> starts at <see cref="TutorialStep.BuyMaterial"/> by definition and is rendered from
    /// the very first frame, before the player has taken a single action, so it is the ONE row
    /// guaranteed to reach the screen on every path — including the starter-kit-skips-buy path,
    /// where <c>Step</c> jumps straight from BuyMaterial to Shelve in a single Advance() pass and
    /// Craft's own TeachNote never becomes current at all (<c>TutorialStepDef</c>'s own doc, "the
    /// shared display slot"). A lesson that depends on having bought material first is not "before"
    /// anything.
    /// </summary>
    [TestCase]
    public void TheRoomExitLesson_IsTaughtOnStep1_BeforeStep2RequiresLeavingTheWorkshop()
    {
        var ui = MountMainUi();
        try
        {
            var buyMaterial = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.BuyMaterial);
            var shelve = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.Shelve);

            AssertThat(buyMaterial.DisplayIndex)
                .OverrideFailureMessage(
                    "The room-exit lesson must be taught on a step BEFORE the one that requires leaving.")
                .IsLess(shelve.DisplayIndex);

            // Read the real bound key rather than guessing it, so a future rebind of "cancel" turns
            // this red instead of leaving stale copy behind (TutorialCopyIsFollowableTests' own
            // house style — see e.g. TheDepartureAndCloseSteps_... above).
            var exitKey = ShortcutMap.KeyLabel(ShortcutMap.Find("cancel"));
            AssertThat(buyMaterial.TeachNote.Contains(exitKey, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"Step 1's TeachNote never names the real \"leave the room\" key (\"{exitKey}\"):\n" +
                    $"  \"{buyMaterial.TeachNote}\"")
                .IsTrue();

            // And it is guaranteed ON SCREEN before anything else can happen — Step starts at
            // BuyMaterial before the player has taken a single action, so a fresh mount's own
            // current checklist row already carries this exact TeachNote.
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            var currentRow = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.Current);
            AssertThat(currentRow.TeachNote).IsEqual(buyMaterial.TeachNote);
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

    /// <summary>
    /// U-T2-16 (#162 defect 3): the card itself keeps ONE sentence now (walk to the counter, press
    /// Open Counter — they speak first) — Present/Suggest/Accept/Hold Firm/Counter moved OUT of the
    /// card and into this row's own TeachNote (rendered live in the checklist, and permanently in
    /// the Lessons book). <see cref="TutorialFlow.CounterAnsweredAtLeastOnce"/> accepts all five
    /// (Present/Suggest/Accept/HoldFirm/Counter), and before this unit no line of copy ANYWHERE ever
    /// named Suggest at all — this pins that every verb the predicate accepts is named somewhere the
    /// player can actually read.
    /// </summary>
    [TestCase]
    public void TheCounterStep_NamesEveryVerbItsOwnPredicateAccepts()
    {
        var note = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.OpenCounter).TeachNote;
        foreach (var verb in new[] { "Present", "Suggest", "Accept", "Hold Firm", "Counter" })
        {
            AssertThat(note.Contains(verb, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"OpenCounter's TeachNote never names \"{verb}\", one of the verbs " +
                    $"TutorialFlow.CounterAnsweredAtLeastOnce accepts:\n  \"{note}\"")
                .IsTrue();
        }
    }

    /// <summary>
    /// U1 (§11.14.14 defect): the counter is the ONE surface that actually has the mechanism the
    /// old shelf-side "pricing-as-a-decision" lesson used to (wrongly) describe — <c>WillingnessModel</c>'s
    /// pin/fleece swing hero mood, a gate <see cref="GameSim.Heroes.ShoppingAi.EvaluateItem"/> (the
    /// shelf's own gate) never touches. This pins that the counter's own TeachNote now names its
    /// real half of the pricing dilemma — a fair answer is remembered kindly, a squeeze is
    /// remembered too, just not kindly — and never smuggles in a client-invented number for the
    /// sim's own mood math (same "show only what the sim decided" discipline
    /// <see cref="GodotClient.Tests.ForgeMentorLessonsTests"/> pins for material-ceiling).
    /// </summary>
    [TestCase]
    public void TheCounterStep_TeachesThatAFairAnswerIsRememberedAndASqueezeIsnt()
    {
        var note = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.OpenCounter).TeachNote;

        AssertThat(note.Contains("remembered", StringComparison.OrdinalIgnoreCase))
            .OverrideFailureMessage(
                $"OpenCounter's TeachNote never names the counter's own remembered-relationship " +
                $"mechanism:\n  \"{note}\"")
            .IsTrue();

        AssertThat(note.Any(char.IsDigit))
            .OverrideFailureMessage(
                $"The counter's pricing lesson contains a digit — a client-authored quantity has " +
                $"leaked into copy that must only describe the mechanism:\n  \"{note}\"")
            .IsFalse();

        AssertThat(note.Contains("you should", StringComparison.OrdinalIgnoreCase))
            .OverrideFailureMessage(
                $"The counter's pricing lesson reads as a recommendation, not a naming of both " +
                $"sides:\n  \"{note}\"")
            .IsFalse();
    }

    /// <summary>
    /// U-T2-16 (#162 defect 4): mirrors <see
    /// cref="TutorialFlowTests.Step7_NeverClaimsADayGate_ForAConditionThatIsNotDayGated"/> — the same
    /// regression shape, for the SAME class of lie. OpenCounter's MinDay dropped from 2 to 1: the
    /// real gate was never the calendar, it was the counter's own Morning-only legality
    /// (<c>CounterHandlers.ApplyOpen</c>). The OLD wait copy said "The counter is a Day 2 lesson...
    /// it opens once Day 2 begins" for most of day 1, then a SECOND wait line ("only opens in the
    /// Morning") for most of day 2 — two wait lines for one gate. The gate is stated as what it
    /// actually is now: a Morning gate, never a day claim.
    /// </summary>
    [TestCase]
    public void TheCounterStep_NeverClaimsADayGate()
    {
        var ui = MountMainUi();
        try
        {
            var day1Evening = ui.Adapter.CurrentState with { Day = 1, Phase = DayPhase.Evening };
            var copy = Plain(ui.Tutorial.CopyFor(TutorialStep.OpenCounter, day1Evening));
            AssertThat(copy.Contains("opens once Day", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"OpenCounter's wait copy still promises a day gate: \"{copy}\"")
                .IsFalse();
            AssertThat(copy.Contains("Day 2", StringComparison.Ordinal))
                .OverrideFailureMessage($"OpenCounter's wait copy still names a specific day: \"{copy}\"")
                .IsFalse();
            AssertThat(copy)
                .OverrideFailureMessage($"OpenCounter's wait copy should name the real Morning gate: \"{copy}\"")
                .Contains("Morning");
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

    // ── U2 (§11.14.14) primer helpers: NewGameSelectTests.cs's own Mount/Unmount, duplicated here
    // rather than shared — that suite's doc comment states the repo convention outright ("small
    // test helpers are cheap to keep self-contained per suite"). MountMainUi/Unmount above stay
    // untouched; this scene is a different root entirely.

    private static NewGameSelect MountNewGameSelect()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen);
        return screen;
    }

    private static void UnmountNewGameSelect(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null; // never leak a picked campaign into a later suite
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
    }
}
#endif
