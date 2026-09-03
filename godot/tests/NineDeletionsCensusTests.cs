#if GDUNIT_TESTS
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-ONBOARD-06 (docs/design/MAKERS-MARK.md §11.15, "the beat-sheet registry and the nine
/// deletions"): a grep-level guard over every deleted string this unit's own rework removed, so a
/// later merge — a rebase that resurrects a stale hunk, a copy-paste from an old branch — cannot
/// silently bring one back. Source-scans <c>res://scripts</c> (the same idiom
/// <c>TeachingCoverageCensusTests.ReadAllGodotScriptSource</c> already established), never
/// <c>res://tests</c>: a test file's own doc comments are allowed to cite deleted copy as history
/// (several in this program do, deliberately, past tense), and this suite's job is catching a
/// resurrection that would actually reach a player, not policing prose about history.
///
/// <para>Three of the plan's nine deletions carry no unique literal string to grep for — the
/// card's second text block and the four open-timed banner lessons landed in prior units
/// (P2-SCREEN-06 #662, P2-ONBOARD-02 #672) and already carry their own structural guards
/// (<c>ObjectiveTracker</c>'s own "exactly ONE prose block" class doc; <c>FireOnOpenRetiredTests</c>
/// makes the whole category unreachable), and the generic wait fallback (P2-SCREEN-08 #665) is
/// pinned by <c>TutorialNeverAsksTheImpossibleTests</c>' own coverage sweep against the
/// <see langword="throw"/> that replaced it. Re-asserting those here would test the same fact
/// through a strictly weaker mechanism than the ones that already own it. The duplicate
/// "The Mark · 1 of 1" card (deletion #5) is pinned structurally in
/// <c>LessonsPanelTests.LessonsPanel_RendersExactlyOneCard_ForTheSharedMarkSlot_CarryingBothTeachNotes</c>
/// rather than here, for the same reason: there is no deleted STRING for it, only a deleted
/// duplicate NODE. The primer's five-line phase legend (deletion #8) is pinned structurally in
/// <c>NewGameSelectTests.Pick_ShowsPrimer_WithClockNoteAndSeed_NeverTouchingAdapter_AndNoPhaseLegend</c>
/// — the underlying copy (<c>MainUi.PhaseLegend</c>) is not itself deleted, only its duplicate
/// appearance on the primer, so there is no string this census could ban without also banning the
/// still-live in-game HUD tooltip that legitimately carries it.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NineDeletionsCensusTests
{
    private static readonly System.Lazy<string> AllGodotScriptSource = new(ReadAllGodotScriptSource);

    /// <summary>Concatenates every <c>.cs</c> file under <c>res://scripts</c> into one blob — the
    /// SAME fixture-guarded read <c>TeachingCoverageCensusTests.ReadAllGodotScriptSource</c> uses,
    /// duplicated rather than shared across files (no shared test-infrastructure module exists for
    /// it yet, and one file's own private static is not a public seam to reuse from another).</summary>
    private static string ReadAllGodotScriptSource()
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);

        if (files.Length < 100)
        {
            throw new System.InvalidOperationException(
                $"Only found {files.Length} .cs files under {scriptsDir} -- too few to trust a source " +
                "scan against. GlobalizePath is resolving somewhere unexpected, not this floor.");
        }

        return string.Join("\n---FILE---\n", files.Select(File.ReadAllText));
    }

    /// <summary>Deletion #1: the OLD <c>first-morning</c> cold open (U16's text), replaced in place
    /// by beat 0 — <see cref="GodotClient.Ui.TutorialFlow.FirstMorningBeatText"/> now holds
    /// different words under the same id/mechanism. A distinctive substring of the old text, not
    /// the whole paragraph, so a future rewording of beat 0 itself does not have to keep dodging
    /// this exact ban.</summary>
    [TestCase]
    public void OldFirstMorningColdOpen_NeverReappears()
    {
        AssertThat(AllGodotScriptSource.Value.Contains("I kept this bench for the smith before you"))
            .OverrideFailureMessage(
                "The pre-P2-ONBOARD-06 first-morning cold open (\"I kept this bench for the smith "
                + "before you\") is back in res://scripts — beat 0 was supposed to replace it, not "
                + "sit alongside it.")
            .IsFalse();
    }

    /// <summary>Deletion #6: the unreachable "Ask me anything" line <c>MentorVoice.GreetingLine</c>
    /// used to carry — pressing Bryn has never once reached it (<c>MainUi.OnStationActivated</c>
    /// special-cases her id before the generic flavor-station branch that would read it), so the
    /// whole constant and its value were deleted rather than kept as permanently-dead prose.</summary>
    [TestCase]
    public void UnreachableGreetingLine_NeverReappears()
    {
        AssertThat(AllGodotScriptSource.Value.Contains("First time at the bench? Ask me anything"))
            .OverrideFailureMessage(
                "MentorVoice's old, permanently-unreachable \"Ask me anything\" greeting is back in "
                + "res://scripts — pressing Bryn still always speaks CurrentLesson instead, so this "
                + "text can still never reach a player.")
            .IsFalse();
    }

    /// <summary>Deletion #2: "all WHERE copy in cards" — the four literal shapes
    /// <c>TutorialFlow.GoTo</c> used to emit ("Walk to **{building}** (WASD), press **E**", "Walk to
    /// the **{building}**, then press **E**", "You're at the **{arrivedNoun}**") are gone along with
    /// the method itself; <see cref="GodotClient.Ui.TutorialOverlay"/>'s own pulse is now the only
    /// on-screen answer to "where." Scanning for the bolded-markdown shape specifically (never the
    /// bare phrase) is deliberate: it is what <c>GoTo</c> actually emitted, and it is what a doc
    /// comment describing this deletion as HISTORY would have no reason to reproduce byte-for-byte
    /// (several such comments in this program cite the bug in plain, unbolded prose instead, which
    /// this scan does not and should not catch).</summary>
    [TestCase]
    public void GoTosWhereCopyTemplates_NeverReappear()
    {
        var source = AllGodotScriptSource.Value;
        foreach (var needle in new[] { "Walk to **", "Walk to the **", "You're at the **" })
        {
            AssertThat(source.Contains(needle))
                .OverrideFailureMessage(
                    $"The deleted GoTo() WHERE-copy template \"{needle}\" is back in res://scripts — "
                    + "the pointer owns WHERE now (P2-ONBOARD-06, §11.15, deletion #2); the card must "
                    + "carry WHAT only.")
                .IsFalse();
        }
    }
}
#endif
