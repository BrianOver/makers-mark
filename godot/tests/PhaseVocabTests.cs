#if GDUNIT_TESTS
using System;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// U2 (playtest-three plan, KTD-B/KTD-G): pins <see cref="PhaseVocab"/> as the ONE vocabulary
/// every phase-name surface reads from, and proves the counter-hold guard actually fires.
///
/// <para><b>Mutation-checked by construction, not just by inspection.</b> Every assertion here
/// reads either the raw sim word ("Camp"/"ExpeditionDeep"/lowercased <c>DayPhase.ToString()</c>)
/// or a hand-verified ABSENCE of it — the exact three surfaces the owner's playtest caught
/// disagreeing (<c>ObjectiveTracker</c>'s day timeline, <c>NewGameSelect</c>'s continue blurb,
/// and a fourth this unit also found: <c>MainUi</c>'s "Phase" stat chip). Reverting
/// <see cref="PhaseVocab"/> to a passthrough (<c>phase.ToString()</c>) or reverting any of the
/// three call sites back to the raw enum flips these assertions to fail — verified by re-reading
/// each call site's pre-edit form (git history) against the assertions below, since the engine
/// suite itself is orchestrator-run, not run from this worktree (repo rule).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PhaseVocabTests
{
    [TestCase]
    public void EveryDayPhase_HasADisplayNameAndABellVerb_NeverTheRawEnumSpelling()
    {
        foreach (var phase in Enum.GetValues<DayPhase>())
        {
            var display = PhaseVocab.Display(phase);
            AssertThat(display).OverrideFailureMessage($"{phase} has no display name").IsNotEmpty();
            AssertThat(display).OverrideFailureMessage($"{phase} still renders its own raw enum spelling")
                .IsNotEqual(phase.ToString());

            var state = GameComposition.NewCampaign(1) with { Phase = phase };
            AssertThat(PhaseVocab.BellVerb(state)).IsNotEmpty();
        }
    }

    [TestCase]
    public void Display_OfTheSavedPhaseString_ReadsPhaseVocab_AndDegradesGracefullyOnGarbage()
    {
        // The save envelope's stored form (CampaignSave.Envelope.Phase) is always
        // DayPhase.ToString() verbatim (KTD-B: format untouched) — exercise that exact contract.
        AssertThat(PhaseVocab.Display(DayPhase.Camp.ToString())).IsEqual("Vigil");
        AssertThat(PhaseVocab.Display(DayPhase.ExpeditionDeep.ToString())).IsEqual("Deep Vigil");

        // A foreign/corrupted save must never crash the continue screen — degrade to itself.
        AssertThat(PhaseVocab.Display("NotARealPhase")).IsEqual("NotARealPhase");
    }

    [TestCase]
    public void DayTimeline_RendersPhaseVocab_NeverTheRawKernelPhaseName()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var phase in new[]
                     {
                         DayPhase.Morning, DayPhase.Expedition, DayPhase.Camp,
                         DayPhase.ExpeditionDeep, DayPhase.Evening,
                     })
            {
                AdvanceToPhase(ui, phase);
                ui.RefreshAll();

                var timelineText = RenderedText(ui.Timeline);
                var expected = PhaseVocab.Display(phase);
                AssertThat(timelineText)
                    .OverrideFailureMessage(
                        $"timeline at {phase} was \"{timelineText.Trim()}\" — expected it to contain " +
                        $"PhaseVocab's \"{expected}\"")
                    .Contains(expected);

                // The exact split-brain the owner's playtest caught: the raw sim word rendered
                // right above a HUD banner that had already moved on to friendlier vocabulary.
                AssertThat(timelineText.Contains("Camp")).IsFalse();
                AssertThat(timelineText.Contains("ExpeditionDeep")).IsFalse();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ContinueBlurb_ReadsPhaseVocab_ForANonMorningSave_NeverLowercasedRawEnum()
    {
        var backup = Backup();
        try
        {
            // Autosave always fires at Evening-completion (lands the player at next-Morning), so a
            // Camp/Deep save is theoretical, not a path a real session takes today — but the stored
            // field is a plain string (CampaignSave.Envelope.Phase) and CampaignSave.Save takes
            // whatever GameState it's given, so this reproduces the exact "camp of day 5" the
            // owner's playtest reported without waiting on a real Camp-phase autosave to exist.
            CampaignSave.Save(GameComposition.NewCampaign(11) with { Phase = DayPhase.Camp, Day = 5 });

            var tree = (SceneTree)Engine.GetMainLoop();
            var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
            tree.Root.AddChild(screen);
            try
            {
                var blurb = Find<Label>(screen, "ContinueBlurb");
                AssertThat(blurb.Text).Contains("the Vigil of day 5");
                AssertThat(blurb.Text.Contains("camp of day", StringComparison.OrdinalIgnoreCase)).IsFalse();
            }
            finally
            {
                MainUi.AdapterOverride = null;
                screen.GetParent()?.RemoveChild(screen);
                screen.Free();
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>
    /// The owner's repeated complaint ("Continue day 2 is still there") was two things at once: a
    /// genuinely stale save (autosave silently dead for tanning/engineering until PR #336 fixed
    /// the unregistered <c>CraftPuzzleInput</c> subtypes) and this button not saying enough to let
    /// the player tell a fresh checkpoint from an old one without reading the blurb underneath it.
    /// The button itself must now spell out day AND phase, not day alone.
    /// </summary>
    [TestCase]
    public void ContinueButton_NamesDayAndPhase_NeverDayAlone()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(3) with { Phase = DayPhase.Morning, Day = 2 });

            var tree = (SceneTree)Engine.GetMainLoop();
            var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
            tree.Root.AddChild(screen);
            try
            {
                var button = Find<Button>(screen, "Continue");
                AssertThat(button.Text)
                    .OverrideFailureMessage($"Continue button read \"{button.Text}\" — expected the day AND the phase word")
                    .Contains("Day 2");
                AssertThat(button.Text).Contains(PhaseVocab.Display(DayPhase.Morning));
            }
            finally
            {
                MainUi.AdapterOverride = null;
                screen.GetParent()?.RemoveChild(screen);
                screen.Free();
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    [TestCase]
    public void MineGateBuilding_IsNamedForWhatItOpens_NotBareSceneryWord()
    {
        // Nametag only (world-rendered label) — the click-routing key ("minegate") and sprite id
        // are untouched, and the noticeboard building (which opens a DIFFERENT panel, Depths vs
        // Bounties) is deliberately unchanged (plan U2: "noticeboard stays Bounties").
        var gate = TownLayout2D.Venues.First(v => v.Key == "minegate");
        AssertThat(gate.Nametag).IsEqual("Mine Gate");

        var board = TownLayout2D.Venues.First(v => v.Key == "noticeboard");
        AssertThat(board.Nametag).IsEqual("Bounties");
    }

    [TestCase]
    public void Morning_NamesWhoIsReadyAtTheGate()
    {
        var ui = MountMainUi();
        try
        {
            var alive = ui.Adapter.CurrentState.Heroes.Values.Count(h => h.Alive);
            var clockLabel = Find<Label>(ui, "ClockLabel").Text;
            AssertThat(clockLabel)
                .OverrideFailureMessage($"Morning clock label was \"{clockLabel}\" with {alive} alive")
                .Contains(alive == 1 ? "1 hero ready at the gate" : $"{alive} heroes ready at the gate");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// KTD-G's guard end to end: the bell pressed against an open counter is logged (grep-able
    /// evidence for the one theorized stuck-day cause), the day still moves (never silently
    /// holds — U5's contract, unchanged), AND the confirmation toast actually renders.
    ///
    /// <para>That last assertion caught a real, pre-existing bug (not introduced by this unit,
    /// present since PR #197): <c>MainUi</c>'s bell handler called <c>ShowBellToast</c> BEFORE
    /// <c>Clock.AdvanceNow()</c>, but <c>AdvanceNow</c> synchronously fires
    /// <c>OnPhaseCompleted</c>, which unconditionally re-decides the SAME toast banner (rejection
    /// &gt; world notice &gt; <c>ClearToast()</c>) — and a plain counter-close+advance tick
    /// produces neither, so <c>ClearToast()</c> always ran last and wiped the confirmation before
    /// a frame ever rendered it. First observed as a CI failure on this exact assertion (empty
    /// string, not "wrong text") — fixed at the source by moving the toast call to fire AFTER
    /// <c>AdvanceNow</c> in <c>MainUi</c>'s bell handler, which this test now pins.</para>
    /// </summary>
    [TestCase]
    public void RingingBell_AgainstAnOpenCounter_LogsMorningHold_ShowsTheToast_AndStillNeverSilentlyHolds()
    {
        var logPath = ProjectSettings.GlobalizePath("user://playtest-log-morninghold.jsonl");
        PlaytestLog.RedirectForTests(logPath);
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new OpenCounterAction());
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Counter is { Closed: false }).IsTrue();

            PressEnabled(ui, "AdvancePhase");

            var notes = System.IO.File.Exists(logPath)
                ? System.IO.File.ReadAllLines(logPath).Where(l => l.Contains("\"kind\":\"note\"")).ToList()
                : new System.Collections.Generic.List<string>();
            AssertThat(notes.Any(n => n.Contains("MORNING-HOLD: counter open")))
                .OverrideFailureMessage($"no MORNING-HOLD note in: [{string.Join(" | ", notes)}]")
                .IsTrue();

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var toast = Find<Label>(ui, "RejectionToast").Text;
            AssertThat(toast)
                .OverrideFailureMessage(
                    $"toast was \"{toast}\" — the confirmation was set before Clock.AdvanceNow() and got " +
                    "wiped by OnPhaseCompleted's own ClearToast() before ever rendering")
                .Contains("parties depart");
        }
        finally
        {
            Unmount(ui);
            PlaytestLog.RedirectForTests(null);
        }
    }

    // ── helpers: never clobber a real campaign save (CampaignSaveTests' own precedent) ──────────

    private static string? Backup() =>
        GodotFileAccess.FileExists(CampaignSave.SavePath) ? ReadSave() : null;

    private static void Restore(string? backup)
    {
        if (backup is null)
        {
            CampaignSave.Clear();
            return;
        }

        WriteSave(backup);
    }

    private static string ReadSave()
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Read);
        return file.GetAsText();
    }

    private static void WriteSave(string contents)
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Write);
        file.StoreString(contents);
    }
}
#endif
