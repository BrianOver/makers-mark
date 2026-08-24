#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;
using Godot;
using GodotClient.Panels;
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
                AssertThat(text.Contains(ObjectiveTracker.Plain(def.TeachNote), StringComparison.Ordinal))
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
                AssertThat(text.Contains(ObjectiveTracker.Plain(def.TeachNote), StringComparison.Ordinal))
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
                AssertThat(text.Contains(ObjectiveTracker.Plain(def.TeachNote), StringComparison.Ordinal))
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

    /// <summary>U5 (§11.14.14, "teaching surfaces render their own copy"): TeachNote copy is
    /// shared with the CLI, where <c>**bold**</c> is meaningful — a Godot <see cref="Label"/> has
    /// no markup parser, so before this fix the asterisks rendered literally in this permanent
    /// record. Proven against REAL production copy (<see cref="TutorialStep.OpenCounter"/>'s own
    /// TeachNote, the one registry row that actually carries emphasis today — "**Present** a
    /// shelved item...") rather than a fabricated fixture string, so this cannot pass by accident
    /// against copy nobody actually ships. <see cref="ObjectiveTracker.Plain"/> already made this
    /// exact promise for the tutorial card and checklist (<c>FreshCampaign_TutorialActive_...</c>
    /// in <c>TutorialFlowTests</c>); this panel had simply never called it.</summary>
    [TestCase]
    public void LessonsPanel_RendersNoLiteralAsterisks_ForARegistryTeachNoteThatCarriesBoldMarkup()
    {
        var openCounter = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.OpenCounter);
        AssertThat(openCounter.TeachNote.Contains("**", StringComparison.Ordinal))
            .OverrideFailureMessage(
                "Fixture assumption broke: OpenCounter's TeachNote no longer carries bold markup — " +
                "pick another registry row that does before trusting this test.")
            .IsTrue();

        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);

            AssertThat(text.Contains("**", StringComparison.Ordinal))
                .OverrideFailureMessage("The Lessons book rendered a literal \"**\" — markdown bold markup is leaking onto screen.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U5 (§11.14.14): every first-touch card used to head itself with the raw bookkeeping id —
    /// "◆ the-proof-taught" instead of anything a person wrote (<see
    /// cref="LessonsPanel.FirstTouchTitles"/> is the fix). This is the guard the unit asked for
    /// explicitly: it must iterate every REAL id, not a hand-typed handful, or it stops covering
    /// the family the exact way <c>TeachingCoverageCensusTests</c>' own doc warns against ("the
    /// 128-untested-assets lesson"). So it source-scans every <c>.cs</c> file under
    /// <c>res://scripts</c> for every live <c>ConsumeFirstTouch</c>/<c>ShowMentorFirstTouch</c> call
    /// site (the same idiom <see cref="TeachingCoverageCensusTests"/> already uses to verify one
    /// known id, run here in the opposite direction to DISCOVER the whole set) and checks the
    /// catalog against that discovered set in BOTH directions — a live id with no title, and a
    /// title left behind for an id nothing calls anymore, both fail by name.
    /// </summary>
    [TestCase]
    public void EveryLiveFirstTouchId_HasANonSlugTitleInTheCatalog()
    {
        var liveIds = AllLiveFirstTouchIds();

        // Denominator guard (this program's own recurring vacuous-green shape, per
        // TeachingCoverageCensusTests): a broken GlobalizePath or a regex that stopped matching
        // would make every check below pass by having nothing left to check.
        AssertThat(liveIds.Count)
            .OverrideFailureMessage("Source scan found too few first-touch ids -- the scan is broken, not the catalog.")
            .IsGreaterEqual(15);

        var problems = new List<string>();
        foreach (var id in liveIds)
        {
            if (!LessonsPanel.FirstTouchTitles.TryGetValue(id, out var title))
            {
                problems.Add(
                    $"\"{id}\" has a live ConsumeFirstTouch/ShowMentorFirstTouch call site but no title in " +
                    "LessonsPanel.FirstTouchTitles -- the book would head its card with the raw id.");
                continue;
            }

            if (title.Contains('-'))
            {
                problems.Add($"\"{id}\"'s catalog title (\"{title}\") still reads like a hyphenated slug.");
            }
        }

        foreach (var staleId in LessonsPanel.FirstTouchTitles.Keys.Except(liveIds))
        {
            problems.Add(
                $"LessonsPanel.FirstTouchTitles has a title for \"{staleId}\", but no live call site names it " +
                "anymore -- a rename/delete moved the id and this catalog entry was not updated with it.");
        }

        AssertThat(problems.Count).OverrideFailureMessage(string.Join("\n", problems)).IsEqual(0);
    }

    /// <summary>Every first-touch id ANY live production call site actually names — the source-scan
    /// idiom <see cref="TeachingCoverageCensusTests"/>' own <c>FirstTouchIdIsWiredInSource</c> uses
    /// to verify ONE known id, run in the opposite direction to discover the WHOLE set. Handles the
    /// one call site (<c>ForgePanel.MarkReadLessonId</c>) that reads a shared id from a
    /// <c>const string</c> instead of retyping the literal, same as that method does.</summary>
    private static HashSet<string> AllLiveFirstTouchIds()
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
        var source = string.Join("\n---FILE---\n", files.Select(File.ReadAllText));

        var ids = new HashSet<string>();
        foreach (Match m in Regex.Matches(
                     source, @"(?:ConsumeFirstTouch|ShowMentorFirstTouch)\(\s*(?:\r?\n\s*)?""([a-z0-9-]+)"""))
        {
            ids.Add(m.Groups[1].Value);
        }

        foreach (Match constDecl in Regex.Matches(source, @"\bconst\s+string\s+(\w+)\s*=\s*""([a-z0-9-]+)"""))
        {
            var constName = Regex.Escape(constDecl.Groups[1].Value);
            if (Regex.IsMatch(source, $@"(?:ConsumeFirstTouch|ShowMentorFirstTouch)\(\s*(?:\r?\n\s*)?{constName}\b"))
            {
                ids.Add(constDecl.Groups[2].Value);
            }
        }

        return ids;
    }
}
#endif
