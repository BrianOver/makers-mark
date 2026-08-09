#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The trail <c>PlaytestLog</c> exists to leave for the bug that prompted this unit: the owner
/// pressed send-off and the day jumped straight to night, skipping most of the game, with no
/// artifact anywhere that could reconstruct what happened. A <c>tick</c> row said the day advanced
/// and nothing else — a real player press and an unattended beat timer produced an IDENTICAL row.
///
/// <para>This suite pins the fix: every real phase transition now carries <c>beat</c>
/// (<c>RaidConductor.Current</c>) and <c>cause</c> (who/what asked for it), and the two cases that
/// most needed telling apart — a player's own "Skip" press versus <c>RaidConductor</c>'s own
/// elapsed-timer auto-advance — read as visibly different causes in the same file. The third case
/// pins the "no artifact at all" half of the bug directly: a session where the player does nothing
/// still has to produce a file another session can actually parse.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlaytestTrailTests
{
    [TestCase]
    public void PressingAdvancePhase_LogsTheRealTransition_WithAnAttributedPressCause()
    {
        var path = ProjectSettings.GlobalizePath("user://playtest-trail-press.jsonl");
        PlaytestLog.RedirectForTests(path);
        MainUi? ui = null;
        try
        {
            ui = MountMainUi();
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Fixture premise failed: a fresh campaign must start at Morning.")
                .IsEqual(DayPhase.Morning);

            // The exact control the owner's report names ("Skip"/"AdvancePhase" — send-off).
            PressEnabled(ui, "AdvancePhase");

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var ticks = TicksInvolving(path, "Morning", "Expedition");
            AssertThat(ticks.Count).OverrideFailureMessage(Dump(ticks)).IsGreaterEqual(1);

            var row = ticks[0];
            AssertThat(row).Contains("\"cause\":\"press:AdvancePhase\"");
            AssertThat(row)
                .OverrideFailureMessage("Beat should already be resynced to SendOff by the time the tick is logged. " + row)
                .Contains("\"beat\":\"SendOff\"");
        }
        finally
        {
            if (ui is not null)
            {
                Unmount(ui);
            }

            PlaytestLog.RedirectForTests(null);
        }
    }

    [TestCase]
    public void AnUnattendedBeatAdvance_LogsADistinctAutoCause_NeverAPressOne()
    {
        var path = ProjectSettings.GlobalizePath("user://playtest-trail-auto.jsonl");
        PlaytestLog.RedirectForTests(path);
        MainUi? ui = null;
        try
        {
            // The default fresh campaign (seed 2026) is the GUARANTEED-unstaged day — every hero's
            // first trip targets floor 1, which resolves at the Expedition tick with InFlight empty
            // (RaidConductorTests' own UnstagedSeed). So SendOff auto-advancing straight to Camp is
            // the common day-1 case, not a contrived one — the same shape as the owner's report.
            ui = MountMainUi();
            PressEnabled(ui, "AdvancePhase"); // the ONE press this test makes: Morning -> Expedition
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

            // Elapse the beat's own pinned max with NO further player input — RaidConductor's own
            // auto-driver, called from MainUi._Process exactly like a real frame would, just without
            // waiting real wall-clock seconds for it (established pattern — see e.g.
            // DayAdvanceHudTests/PlayableLoopTests' own ui._Process(bigDelta) calls).
            ui._Process(RaidConductor.SendOffMaxSeconds);

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Fixture premise failed: SendOff never auto-advanced past its own pinned max.")
                .IsEqual(DayPhase.Camp);

            var ticks = TicksInvolving(path, "Expedition", "Camp");
            AssertThat(ticks.Count).OverrideFailureMessage(Dump(ticks)).IsGreaterEqual(1);

            var row = ticks[0];
            // This is the whole point: nobody pressed anything for THIS tick, and the log says so
            // by naming the real mechanism instead of leaving the field blank or, worse, reusing
            // the earlier press's cause.
            AssertThat(row).Contains("\"cause\":\"auto:conductor-beat-elapsed\"");
            AssertThat(row)
                .OverrideFailureMessage("An unattended auto-tick must never carry a press: cause. " + row)
                .NotContains("\"cause\":\"press:");
        }
        finally
        {
            if (ui is not null)
            {
                Unmount(ui);
            }

            PlaytestLog.RedirectForTests(null);
        }
    }

    [TestCase]
    public void ASessionWithNoActions_StillProducesAWellFormedFile()
    {
        var path = ProjectSettings.GlobalizePath("user://playtest-trail-empty.jsonl");
        PlaytestLog.RedirectForTests(path);
        MainUi? ui = null;
        try
        {
            // Mount and immediately unmount — no button ever pressed, no bell ever rung. The bug
            // this whole unit exists to close was "there is no artifact at all"; this is the floor
            // that artifact must clear even when the player (or a crashed harness) does nothing.
            ui = MountMainUi();
        }
        finally
        {
            if (ui is not null)
            {
                Unmount(ui);
            }

            PlaytestLog.RedirectForTests(null);
        }

        AssertThat(System.IO.File.Exists(path)).IsTrue();
        var lines = System.IO.File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        AssertThat(lines.Count).OverrideFailureMessage(Dump(lines)).IsGreaterEqual(1);

        foreach (var line in lines)
        {
            // "Well-formed" checked for real, not by substring match: every row must parse as a
            // legal JSON object, or a consumer reading this file line-by-line breaks on line 1 of a
            // quiet session — exactly the session most likely to matter for reproducing a bug.
            System.Text.Json.JsonDocument.Parse(line);
        }

        AssertThat(lines[0]).Contains("\"kind\":\"session\"");
    }

    /// <summary>Every <c>tick</c> row recording a real transition from <paramref name="fromPhase"/>
    /// to <paramref name="toPhase"/> — the two DayPhase enum names, exactly as <c>PlaytestLog.Tick</c>
    /// writes them.</summary>
    private static List<string> TicksInvolving(string path, string fromPhase, string toPhase) =>
        System.IO.File.Exists(path)
            ? System.IO.File.ReadAllLines(path)
                .Where(l => l.Contains("\"kind\":\"tick\"")
                    && l.Contains($"\"fromPhase\":\"{fromPhase}\"")
                    && l.Contains($"\"phase\":\"{toPhase}\""))
                .ToList()
            : new List<string>();

    private static string Dump(List<string> lines) => $"lines: [{string.Join(" | ", lines)}]";
}
#endif
