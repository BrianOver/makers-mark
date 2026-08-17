#if GDUNIT_TESTS
using System;
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U2 (tutorial-revamp plan, §11.13): the Lessons book — every <see
/// cref="TutorialFlow.Registry"/> row's own <see cref="TutorialStepDef.TeachNote"/>, permanently,
/// surviving dismissal/completion (the whole point: the ten teaching paragraphs used to live only
/// inside a 32px scroll sliver that showed exactly one and vanished the instant the chain ended).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LessonsPanelTests
{
    [TestCase]
    public void LessonsPanel_RendersEveryRegistryRows_TeachNote()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);
            foreach (var def in TutorialFlow.Registry)
            {
                AssertThat(text.Contains(def.TeachNote, StringComparison.Ordinal))
                    .OverrideFailureMessage($"{def.Step}'s TeachNote is missing from the Lessons book.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The whole reason this panel exists: <see cref="TutorialFlow.Dismiss"/> must never
    /// take the lessons down with it.</summary>
    [TestCase]
    public void LessonsPanel_RendersEveryTeachNote_AfterDismiss()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.Dismiss();
            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);
            foreach (var def in TutorialFlow.Registry)
            {
                AssertThat(text.Contains(def.TeachNote, StringComparison.Ordinal))
                    .OverrideFailureMessage($"{def.Step}'s TeachNote vanished from the Lessons book after Dismiss.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Same claim as <see cref="LessonsPanel_RendersEveryTeachNote_AfterDismiss"/>, for
    /// the OTHER way the chain stops being <see cref="TutorialFlow.Active"/>.</summary>
    [TestCase]
    public void LessonsPanel_RendersEveryTeachNote_AfterTheChainIsComplete()
    {
        var ui = MountMainUi();
        try
        {
            // Fastest real path to Completed: the chain's own backstop — no need to drive the
            // in-game days for a claim about the BOOK, not the chain's own advance logic (that is
            // TutorialFlowTests' job). Read the backstop from the flow rather than hardcoding a day:
            // this was a literal 4, which silently stopped meaning "the chain closes" when U-T2-2
            // split that constant into the warrant's end (still day 4) and the chain's own close
            // (now later, because the pointed chain runs through day 7).
            ui.Tutorial.Advance(ui.Adapter.CurrentState with { Day = TutorialFlow.ChainBackstopDay });
            AssertThat(ui.Tutorial.Completed).IsTrue();

            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);
            foreach (var def in TutorialFlow.Registry)
            {
                AssertThat(text.Contains(def.TeachNote, StringComparison.Ordinal))
                    .OverrideFailureMessage($"{def.Step}'s TeachNote vanished from the Lessons book after completion.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LessonsPanel_MarksTheCurrentStep_WhileTheChainIsActive()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            ui.OpenPanel("Lessons");

            var currentCard = Find<Control>(ui.Lessons, "Lesson_1_BuyMaterial");
            AssertThat(RenderedText(currentCard))
                .OverrideFailureMessage("The current step's own card does not carry the filled marker.")
                .Contains("◆");

            var laterCard = Find<Control>(ui.Lessons, $"Lesson_2_{TutorialStep.Shelve}");
            var laterText = RenderedText(laterCard);
            AssertThat(laterText.Contains("◆", StringComparison.Ordinal))
                .OverrideFailureMessage("A step that is not current is marked as if it were.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Once the chain is inactive, nothing is "current" — no card should claim the filled
    /// marker (there is no live step left to point at).</summary>
    [TestCase]
    public void LessonsPanel_MarksNoStep_AsCurrent_OnceTheChainIsInactive()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.Dismiss();
            ui.OpenPanel("Lessons");

            var text = RenderedText(ui.Lessons);
            AssertThat(text.Contains("◆", StringComparison.Ordinal))
                .OverrideFailureMessage("A dismissed chain still marks some row as current.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U-T2-7 (Wave A substrate, §11.14.4): once a first-touch lesson has fired, it lives
    /// in this book PERMANENTLY — "then lives in the Lessons book" is the plan's own second half of
    /// the first-touch tier, and it must not depend on the tutorial chain still being active.</summary>
    [TestCase]
    public void LessonsPanel_RendersAFirstTouchLesson_OnceItHasFired()
    {
        var ui = MountMainUi();
        try
        {
            var fired = ui.Tutorial.ConsumeFirstTouch("test-first-touch-panel", "This fired exactly once.");
            AssertThat(fired).IsNotNull();

            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);

            AssertThat(text.Contains("This fired exactly once.", StringComparison.Ordinal))
                .OverrideFailureMessage("A fired first-touch lesson's own text is missing from the Lessons book.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The book renders whatever fired FIRST, never a later attempt to overwrite it — same
    /// "re-reading beats re-running, never a stale claim" contract every other lesson in this book
    /// already has.</summary>
    [TestCase]
    public void LessonsPanel_NeverOverwritesAFiredFirstTouchLesson_WithALaterAttempt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.ConsumeFirstTouch("test-overwrite", "The original text.");
            ui.Tutorial.ConsumeFirstTouch("test-overwrite", "A later, different text.");

            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);

            AssertThat(text.Contains("The original text.", StringComparison.Ordinal)).IsTrue();
            AssertThat(text.Contains("A later, different text.", StringComparison.Ordinal))
                .OverrideFailureMessage("The Lessons book rendered a SECOND attempt's text — the first fire must be permanent.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
