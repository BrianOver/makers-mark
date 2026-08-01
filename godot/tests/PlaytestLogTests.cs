#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Proves the session recorder's CALL SITES fire, not just that the writer works.
///
/// <para>The recorder exists so a human play session produces data instead of recollection, and the
/// minigame verbs are the rows nothing else can supply: a phase tick says the day advanced, never
/// whether the player worked a craft, what came out, or whether they walked out halfway. Those call
/// sites live in <c>ForgePanel</c>'s overlay handlers — the kind of code a refactor moves — and a
/// lost <c>PlaytestLog.Note</c> fails nothing, it just quietly stops recording. So the assertions
/// here read the written FILE, driving the panel through the same Controls a player clicks.</para>
///
/// <para><b>Why this is ONE test case and not three.</b> The recorder is a process-wide static.
/// Written as three cases (open/cancel, complete, disarmed) they each armed it at their own path,
/// and a case that armed or disarmed it while a sibling was mid-flight sent that sibling's row to
/// the wrong file — a lost row that looked exactly like a missing call site. It reproduced only when
/// neighbouring suites were in the run, which is the signature of shared mutable state, not of a
/// broken call site. One case that owns the static start to finish cannot race itself.</para>
///
/// <para>The log is armed through <see cref="PlaytestLog.RedirectForTests"/> and disarmed in the
/// finally block, so the other ~550 engine tests still run with the recorder off. Both paths live
/// under <c>user://</c> (the engine's writable sandbox) rather than the repo's <c>runs/</c>, so a
/// test run cannot leave behind a file that reads as a real play session.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlaytestLogTests
{
    [TestCase]
    public void TheMinigameCallSites_RecordOpenCancelAndDone_AndNothingWhenDisarmed()
    {
        var path = ProjectSettings.GlobalizePath("user://playtest-log-callsites.jsonl");
        PlaytestLog.RedirectForTests(path);
        try
        {
            // ── open + abandon ────────────────────────────────────────────────────────────────
            var ui = MountMainUi();
            try
            {
                OpenAnvilMap(ui);
                var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
                AssertThat(overlay.Visible).IsTrue();

                overlay.Cancel();

                var notes = Notes(path);
                AssertThat(notes.Count).OverrideFailureMessage(Dump(notes)).IsEqual(2);
                AssertThat(notes[0]).Contains("minigame open forge");
                AssertThat(notes[0]).Contains($"recipe={ScriptedSession.CraftRecipeId}");
                AssertThat(notes[0]).Contains($"mat={ScriptedSession.CraftMaterial}");

                // Abandonment is the strongest "this is not fun" signal the game can record, and it
                // is invisible on every other surface — the run queues no action and closes quietly.
                AssertThat(notes[1]).Contains("minigame cancel forge");
                AssertThat(notes[1]).Contains("shape=");
            }
            finally
            {
                Unmount(ui);
            }

            // ── a completed craft carries the grade ───────────────────────────────────────────
            ui = MountMainUi();
            try
            {
                OpenAnvilMap(ui);
                var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
                DriveToPlunge(overlay);
                AssertThat(overlay.Completed).IsTrue();

                var notes = Notes(path);
                AssertThat(notes.Count).OverrideFailureMessage(Dump(notes)).IsEqual(4);
                AssertThat(notes[3]).Contains("minigame done forge");

                // The grade is why a "done" row beats a tick: it answers whether the run the player
                // just sweated through actually paid.
                AssertThat(notes[3]).Contains($"grade={overlay.PreviewGradePermille}");
                AssertThat(notes[3]).Contains("sub=");
            }
            finally
            {
                Unmount(ui);
            }

            // ── disarmed: the default for every other test, and for anyone who launches the game
            //    without the launcher — the instrumentation must cost nothing and leave no trace ──
            PlaytestLog.RedirectForTests(null);
            var sealedCount = Notes(path).Count;

            ui = MountMainUi();
            try
            {
                OpenAnvilMap(ui);
                Find<ForgeMinigame>(ui.Forge, "ForgeMinigame").Cancel();

                AssertThat(PlaytestLog.Active).IsFalse();
                AssertThat(Notes(path).Count).IsEqual(sealedCount);
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            PlaytestLog.RedirectForTests(null);
        }
    }

    /// <summary>Buy the dagger's copper, open the forge, and press Work — the same enabled Controls
    /// a player clicks (<c>ForgeCraftTests</c>' own path).</summary>
    private static void OpenAnvilMap(MainUi ui)
    {
        ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
        ui.Adapter.AdvancePhase();
        ui.OpenPanel("Forge");
        PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
    }

    /// <summary>Works the billet to the path end on-tempo and plunges — the same public
    /// <c>Advance</c>/input seams <c>ForgeMinigameTests</c>' scripted drivers use (no wall-clock,
    /// no RNG), reduced to the shortest run that reaches a completed craft.</summary>
    private static void DriveToPlunge(ForgeMinigame mg)
    {
        var guard = 0;
        while (mg.ShapeXPermille < 1000)
        {
            var target = Math.Max(ForgePath.HeatAt(mg.Path, mg.ShapeXPermille), 500);
            if (mg.HeatYPermille < target - 40)
            {
                mg.BellowsStart();
                mg.Advance(ForgeMinigame.TempoPeriodSeconds);
                mg.BellowsStop();
            }
            else
            {
                mg.Advance(ForgeMinigame.TempoPeriodSeconds);
                mg.ForgeStrike();
            }

            if (++guard > 5000)
            {
                throw new InvalidOperationException("run never reached the path end");
            }
        }

        mg.Plunge();
    }

    private static string Dump(List<string> notes) => $"notes: [{string.Join(" | ", notes)}]";

    private static List<string> Notes(string path) =>
        System.IO.File.Exists(path)
            ? System.IO.File.ReadAllLines(path).Where(l => l.Contains("\"kind\":\"note\"")).ToList()
            : new List<string>();
}
#endif
