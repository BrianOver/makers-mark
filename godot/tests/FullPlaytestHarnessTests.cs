#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Regression coverage for fix/playtest-harness-honesty: a real run of <see cref="FullPlaytest"/>
/// on main printed <c>done — 147 shots, 0 anomalies</c> while its own stderr carried a live
/// <c>[CampaignSave] save failed</c> warning and hundreds of <c>rejected CraftAction</c> warnings —
/// the harness never looked at anything Godot itself pushed. These tests cover the pieces of that
/// fix that are unit-testable outside a real 5-run/8-day boot: (1) <see cref="EngineLogAnomalies"/>,
/// the pure grouping logic FullPlaytest now runs over Godot's own console log; (2)
/// <see cref="FullPlaytest.ExitCodeFor"/>, so a nonzero anomaly count is visible in the exit path,
/// not only the report body; (3) the <see cref="MainUi"/> rejection-warning dedup fix — a measured
/// real run duplicate-warned 368 times for 83 genuinely distinct rejections, because the warning
/// loop re-emitted the whole phase-accumulated list on every immediate action instead of just the
/// new tail.
/// </summary>
[TestSuite]
public class FullPlaytestHarnessTests
{
    // ── EngineLogAnomalies: pure text processing, no Godot runtime needed ──────────────────

    [TestCase]
    public void Scan_GroupsRepeatedLines_AndCountsThem()
    {
        // Mutation check: gut EngineLogAnomalies.Scan (e.g. `return new List<Group>();`) and this
        // goes red — Count drops to 0, every Single() throws. Restore it and this is green again.
        var lines = new[]
        {
            "Godot Engine v4.6.3.stable.mono",
            "WARNING: [MainUi] rejected CraftAction: Not enough copper: need 2, have 0.",
            "   at: void GodotClient.MainUi.OnPhaseCompleted(...) (res://scripts/MainUi.cs:429)",
            "WARNING: [MainUi] rejected CraftAction: Not enough copper: need 3, have 0.",
            "WARNING: [MainUi] rejected CraftAction: Not enough copper: need 2, have 0.",
            "ERROR: Resource file not found: res://assets/icons/ore_verdigris.svg (expected type: unknown)",
            "ERROR: Resource file not found: res://assets/icons/ore_verdigris.svg (expected type: unknown)",
            "WARNING: [CampaignSave] save failed (NotSupportedException: Runtime type " +
            "'GameSim.Professions.EngineeringAssemblyInput' is not supported.)",
        };

        var groups = EngineLogAnomalies.Scan(lines);

        // Three distinct problems, not eight raw lines and not zero: the digit-normalized
        // "need N, have N" collapses the three CraftAction lines into ONE group (count 3); the
        // identical resource error collapses into ONE group (count 2); the campaign-save warning
        // stands alone. The backtrace continuation line and the boot banner are not WARNING:/
        // ERROR: lines and must not appear at all.
        AssertThat(groups.Count).IsEqual(3);

        var craft = groups.Single(g => g.Message.Contains("rejected CraftAction"));
        AssertThat(craft.Count).IsEqual(3);

        var icon = groups.Single(g => g.Message.Contains("ore_verdigris"));
        AssertThat(icon.Count).IsEqual(2);

        var save = groups.Single(g => g.Message.Contains("CampaignSave"));
        AssertThat(save.Count).IsEqual(1);
    }

    [TestCase]
    public void Scan_OfAPlainLog_FindsNothing()
    {
        var lines = new[]
        {
            "Godot Engine v4.6.3.stable.mono",
            "[MainUi] tick complete: day 1 Morning -> day 1 Morning (1 events, 0 rejections)",
            "[fullplaytest] done — 12 shots, 0 anomalies.",
        };

        AssertThat(EngineLogAnomalies.Scan(lines)).IsEmpty();
    }

    // ── exit code: nonzero must be visible without reading the report body ─────────────────

    [TestCase]
    public void ExitCodeFor_IsNonzero_OnlyWhenAnomaliesExist()
    {
        AssertThat(FullPlaytest.ExitCodeFor(0)).IsEqual(0);
        AssertThat(FullPlaytest.ExitCodeFor(1)).IsEqual(1);
        AssertThat(FullPlaytest.ExitCodeFor(83)).IsEqual(1);
    }

    // ── the rejection-warning dedup fix (MainUi) ────────────────────────────────────────────

    /// <summary>
    /// Mutation check: revert <c>OnPhaseCompleted</c>'s dedup guard to the original bare
    /// <c>foreach (var rejected in Adapter.LastRejections)</c> and this goes red — three
    /// accumulating rejections (counts 1, then 2, then 3 — SimAdapter's own cumulative-per-phase
    /// contract, pinned elsewhere by RejectionUxTests) would push 1 + 2 + 3 = 6 warnings, not 3.
    /// </summary>
    [TestCase]
    [RequireGodotRuntime]
    public void RejectionWarnings_CountDistinctRefusals_NotAccumulatedReprints()
    {
        var ui = MountMainUi();
        try
        {
            // Three SEPARATE immediate rejections in the SAME phase — BuyMaterialAction resolves
            // immediately (ActionTiming), and a fresh campaign has no gold for 9999 units, so
            // every call is refused the same way without changing state (a rejected action is a
            // no-op), matching the proven pattern in RejectionUxTests.
            for (var i = 0; i < 3; i++)
            {
                ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, 9999));
            }

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(3);
            AssertThat(ui.RejectionWarningsEmitted).IsEqual(3);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
