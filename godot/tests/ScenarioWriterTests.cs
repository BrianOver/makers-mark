#if GDUNIT_TESTS
using System;
using GameSim.Contracts;
using GameSim.Harness;
using GdUnit4;
using Godot;
using GodotClient.Tools;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// U18 (§11.14.14, "a tester can stand on day 4") — coverage for the dev-gated scenario writer
/// (<see cref="ScenarioWriter"/>). Determinism itself (same seed/day -> byte-identical bytes, and
/// the day-4 attribution-beat precondition) is pinned in the pure fast-lane
/// <c>sim/GameSim.Tests/Harness/ScenarioBuilderTests.cs</c> — nothing here repeats that proof. This
/// suite covers the two claims that only exist once Godot is in the loop: the env-var gate is off
/// by default and unreachable without it, and a written save loads through the REAL Continue path
/// with the apprenticeship chain honestly mid-flight.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScenarioWriterTests
{
    [TestCase]
    public void GateDay_AbsentEnvVar_ReturnsNull()
    {
        var backup = System.Environment.GetEnvironmentVariable("MM_SCENARIO_DAY");
        try
        {
            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", null);
            AssertThat(ScenarioWriter.GateDay()).IsNull();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", backup);
        }
    }

    [TestCase]
    public void GateDay_InvalidOrNonPositive_ReturnsNull()
    {
        var backup = System.Environment.GetEnvironmentVariable("MM_SCENARIO_DAY");
        try
        {
            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", "not-a-number");
            AssertThat(ScenarioWriter.GateDay()).IsNull();

            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", "0");
            AssertThat(ScenarioWriter.GateDay()).IsNull();

            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", "-3");
            AssertThat(ScenarioWriter.GateDay()).IsNull();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", backup);
        }
    }

    [TestCase]
    public void GateDay_ValidPositiveInteger_ReturnsIt()
    {
        var backup = System.Environment.GetEnvironmentVariable("MM_SCENARIO_DAY");
        try
        {
            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", "4");
            var day = ScenarioWriter.GateDay();
            AssertThat(day.HasValue).IsTrue();
            AssertThat(day!.Value).IsEqual(4);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MM_SCENARIO_DAY", backup);
        }
    }

    /// <summary>
    /// The end-to-end claim: a day-4 scenario write lands a Continue-loaded campaign on day 4 with
    /// the apprenticeship chain honestly mid-flight — not completed (day 4 is well short of
    /// <see cref="TutorialFlow.ChainBackstopDay"/>), and not reset to the chain's own step 1 (day 1's
    /// buy/craft/shelve/departure facts are already durably true in this state's own EventLog, so an
    /// honest fast-forward can never sit at step 1). Mounts through the SAME
    /// <c>SimAdapter(CampaignSave.TryLoad())</c> seam <c>NewGameSelect.OnContinuePressed</c> itself
    /// uses, so this proves the REAL Continue path, not a shortcut around it.
    /// </summary>
    [TestCase]
    public void Write_ThenContinueLoad_TutorialIsMidFlight_NotCompletedNotReset()
    {
        var campaignBackup = BackupCampaign();
        try
        {
            var state = ScenarioBuilder.BuildDay(seed: 1, day: 4);
            var (ok, _) = ScenarioWriter.Write(state);
            AssertThat(ok).IsTrue();

            var loaded = CampaignSave.TryLoad();
            AssertThat(loaded).IsNotNull();
            AssertThat(loaded!.Day).IsEqual(4);
            AssertThat(loaded.Phase).IsEqual(DayPhase.Morning);

            var ui = MountMainUi(new SimAdapter(loaded));
            try
            {
                AssertThat(ui.Tutorial.Completed).IsFalse();
                AssertThat(ui.Tutorial.Dismissed).IsFalse();
                AssertThat(ui.Tutorial.Active).IsTrue();
                AssertThat(ui.Tutorial.Step).IsNotEqual(TutorialStep.BuyMaterial);
            }
            finally
            {
                Unmount(ui); // also wipes tutorial_flow.json (U23 leak guard) — no cleanup needed here
            }
        }
        finally
        {
            RestoreCampaign(campaignBackup);
        }
    }

    // ── helpers: never clobber a real campaign save (CampaignSaveTests' own precedent) ────────────

    private static string? BackupCampaign() =>
        GodotFileAccess.FileExists(CampaignSave.SavePath) ? ReadCampaign() : null;

    private static void RestoreCampaign(string? backup)
    {
        if (backup is null)
        {
            CampaignSave.Clear();
            return;
        }

        WriteCampaign(backup);
    }

    private static string ReadCampaign()
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Read);
        return file.GetAsText();
    }

    private static void WriteCampaign(string contents)
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Write);
        file.StoreString(contents);
    }
}
#endif
