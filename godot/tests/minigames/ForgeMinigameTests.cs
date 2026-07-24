#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U23d: the "Anvil Map" forge overlay — the shared-target-line contract (this overlay renders
/// EXACTLY the polyline <c>ForgePath</c>/<c>ForgeScorer</c> regenerate sim-side from the SAME
/// seed), the captured trace's shape (even-length, in-range, capped Samples/Strikes), the
/// single-action contract (PKD8), and same-script determinism. Every scenario drives
/// <see cref="ForgeMinigame"/> through its public <c>Advance(double)</c>/input-seam methods
/// (<c>ForgeStrike</c>/<c>BellowsStart</c>/<c>BellowsStop</c>/<c>Plunge</c>) — no wall-clock, no
/// engine RNG anywhere in the driven path, so "same script twice" is a real determinism pin, not
/// a coincidence. PROPERTY-ONLY: the Anvil Map is a plain 2D <c>Control</c> canvas, never a 3D
/// <c>SubViewport</c> — the known gdUnit headless-hang trap never applies here.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMinigameTests
{
    private const int TestDay = 0;
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    [TestCase]
    public void EmittedTrace_HasEvenLengthSamplesAndStrikes_ValuesInRange_RespectsCap()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            DriveGoodRun(mg);

            AssertThat(mg.Completed).IsTrue();
            var trace = mg.EmittedAction!.Puzzle as ForgeTraceInput;
            AssertThat(trace is not null).IsTrue();
            AssertThat(trace!.Samples.Count % 2).IsEqual(0);
            AssertThat(trace.Strikes.Count % 2).IsEqual(0);
            AssertThat(trace.Samples.Count / 2).IsLessEqual(ForgeMinigame.MaxSamples);
            AssertThat(trace.Strikes.Count / 2).IsLessEqual(ForgeMinigame.MaxSamples);

            foreach (var value in trace.Samples)
            {
                AssertThat(value).IsGreaterEqual(0);
                AssertThat(value).IsLessEqual(1000);
            }

            foreach (var value in trace.Strikes)
            {
                AssertThat(value).IsGreaterEqual(0);
                AssertThat(value).IsLessEqual(1000);
            }

            AssertThat(trace.PathSeed).IsEqual(mg.PathSeed);
        }
        finally
        {
            mg.Free(); // never parented into a mounted tree — free it directly, no leaked orphan
        }
    }

    [TestCase]
    public void GoodRun_TracksPathBetter_ThanBadRun_HigherPreviewGrade()
    {
        var good = RunScript(DriveGoodRun);

        // A deliberately pathological trace over the SAME target line: heat pinned scorching-hot the
        // whole way (ignoring the target curve entirely) with maximally off-beat forge strikes. Scored
        // by the same pure scorer, this is what "not tracking the path" looks like — a real driven,
        // path-following run must beat it. (Constructed rather than driven because the forge physics
        // punish bad play with stalled progress, so a sloppy run can't reliably reach the path end at
        // all — the ForgeScorerTests cover the driven-perfect-vs-worst ranking on synthetic traces.)
        var badSamples = ImmutableList.CreateBuilder<int>();
        for (var x = 0; x <= 1000; x += 40)
        {
            badSamples.Add(x);
            badSamples.Add(950); // pinned scorching hot, nowhere near the target curve
        }

        var badStrikes = ImmutableList.Create(400, 1000, 500, 1000, 600, 1000); // forge-zone, maximally off-beat
        var badTrace = new ForgeTraceInput(badSamples.ToImmutable(), badStrikes, good.Trace.PathSeed);
        var badGrade = ForgeScorer.Score(DaggerRecipe, badTrace, ImmutableSortedSet<string>.Empty, ProfessionRegistry.Blacksmith).GradePermille;

        AssertThat(good.PreviewGrade).IsGreater(badGrade);
    }

    [TestCase]
    public void SameScriptTwice_ProducesIdenticalTraceAndGrade_NoHiddenRandomness()
    {
        var first = RunScript(DriveGoodRun);
        var second = RunScript(DriveGoodRun);

        AssertThat(second.Trace.Samples).ContainsExactly(first.Trace.Samples);
        AssertThat(second.Trace.Strikes).ContainsExactly(first.Trace.Strikes);
        AssertThat(second.Trace.PathSeed).IsEqual(first.Trace.PathSeed);
        AssertThat(second.PreviewGrade).IsEqual(first.PreviewGrade);
    }

    [TestCase]
    public void DifferentDay_RegeneratesADifferentPathSeed_ButStaysAgreeableWithForgePath()
    {
        var day0 = new ForgeMinigame();
        var day1 = new ForgeMinigame();
        try
        {
            day0.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, 0);
            day1.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, 1);

            AssertThat(day1.PathSeed).IsNotEqual(day0.PathSeed);

            // The overlay's rendered Path is EXACTLY what ForgePath.Generate regenerates from the
            // same seed — the byte-for-byte agreement the sim scorer depends on.
            var regenerated = ForgePath.Generate(DaggerRecipe.Tier, DaggerRecipe.Slot, DaggerRecipe.BaseStats.Weight, day0.PathSeed);
            AssertThat(regenerated).ContainsExactly(day0.Path);
        }
        finally
        {
            day0.Free();
            day1.Free();
        }
    }

    [TestCase]
    public void UnlockedAssists_ImprovePreviewGrade_ForTheIdenticalTrace()
    {
        var result = RunScript(DriveGoodRun);

        var baselineScore = ForgeScorer.Score(DaggerRecipe, result.Trace, ImmutableSortedSet<string>.Empty, ProfessionRegistry.Blacksmith);
        AssertThat(result.PreviewGrade).IsEqual(baselineScore.GradePermille); // the preview IS this same pure scorer

        var everyAssistNode = ImmutableSortedSet.Create(
            TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft, TalentTree.WeaponSpecialist);
        // DaggerRecipe is a Weapon recipe — Weapon Specialist's bonus is in scope (sim-side slot gating).
        AssertThat(DaggerRecipe.Slot).IsEqual(ItemSlot.Weapon);
        var assistedScore = ForgeScorer.Score(DaggerRecipe, result.Trace, everyAssistNode, ProfessionRegistry.Blacksmith);

        AssertThat(assistedScore.GradePermille).IsGreaterEqual(baselineScore.GradePermille);
    }

    [TestCase]
    public void Cancel_MidRun_QueuesNoActionAndRaisesCancelledExactlyOnce()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);
            var cancelledCount = 0;
            mg.Cancelled += () => cancelledCount++;

            mg.Advance(0.05);
            mg.ForgeStrike();
            mg.Cancel();
            mg.Cancel(); // double-cancel must not double-fire

            AssertThat(mg.WasCancelled).IsTrue();
            AssertThat(mg.Completed).IsFalse();
            AssertThat(mg.EmittedAction is null).IsTrue();
            AssertThat(cancelledCount).IsEqual(1);

            // A cancelled run never finishes, even if driven further.
            mg.Advance(5.0);
            mg.ForgeStrike();
            mg.Plunge();
            AssertThat(mg.EmittedAction is null).IsTrue();
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void Plunge_BeforeShapeReachesPathEnd_IsANoOp()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            mg.Plunge();

            AssertThat(mg.Completed).IsFalse();
            AssertThat(mg.ShapeXPermille).IsLess(1000);
            AssertThat(mg.EmittedAction is null).IsTrue();
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void HammerAndBellows_AreMutuallyExclusive_StrikeIsANoOpWhilePumping()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            mg.BellowsStart();
            var xBefore = mg.ShapeXPermille;
            mg.ForgeStrike(); // no-op while pumping
            AssertThat(mg.ShapeXPermille).IsEqual(xBefore);

            mg.BellowsStop();
            mg.Advance(1.0); // heat rose while pumping — a strike now should actually move the shape
            mg.ForgeStrike();
            AssertThat(mg.ShapeXPermille).IsGreater(xBefore);
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void CompletedRun_QueuesExactlyOneCraftAction_CarryingTheForgeTracePuzzle_ThroughTheRealForgePanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            AssertThat(overlay.Visible).IsTrue();

            DriveGoodRun(overlay);

            var pending = ui.Adapter.PendingActions.OfType<CraftAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].RecipeId).IsEqual(ScriptedSession.CraftRecipeId);
            AssertThat(pending[0].MaterialKey).IsEqual(ScriptedSession.CraftMaterial);
            AssertThat(pending[0].PerformanceGrade is null).IsTrue(); // the trace is the source; sim scores it
            AssertThat(pending[0].Puzzle is ForgeTraceInput).IsTrue();
            AssertThat(overlay.Visible).IsFalse(); // the overlay closes on completion
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Cancel_MidRun_ThroughTheRealForgePanel_QueuesNoAction()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            overlay.Advance(0.05); // mid-run, nowhere near the path end

            PressEnabled(ui.Forge, "ForgeMinigameCancel");

            AssertThat(overlay.WasCancelled).IsTrue();
            AssertThat(overlay.Visible).IsFalse();
            AssertThat(ui.Adapter.PendingActions.OfType<CraftAction>().Count()).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Scripted-run drivers — pure Advance(delta)/input-seam calls, no wall-clock, no RNG ────

    /// <summary>Works the billet to the path end on-tempo, tracking the target line's heat (pumping
    /// toward <see cref="ForgePath.HeatAt"/> at the current shape-x, floored at a workable heat so
    /// strikes — whose advance scales with heat — always progress and the run never stalls). Every
    /// strike lands on a tempo-period boundary, so it earns the on-beat advance bonus and a clean
    /// forge-strike score. Shared by the good and bad runs so they differ ONLY in the finish.</summary>
    private static void WorkBilletToEnd(ForgeMinigame mg)
    {
        var guard = 0;
        while (mg.ShapeXPermille < 1000)
        {
            var target = Math.Max(ForgePath.HeatAt(mg.Path, mg.ShapeXPermille), 500);
            if (mg.HeatYPermille < target - 40)
            {
                mg.BellowsStart();
                mg.Advance(ForgeMinigame.TempoPeriodSeconds); // a full period — tempo phase stays synced
                mg.BellowsStop();
            }
            else
            {
                mg.Advance(ForgeMinigame.TempoPeriodSeconds); // Elapsed on a beat boundary — on-tempo strike
                mg.ForgeStrike();
            }

            if (++guard > 5000)
            {
                throw new InvalidOperationException("run never reached the path end");
            }
        }
    }

    /// <summary>A competent run: works the billet to the end on-tempo, then lets heat drain toward
    /// the quench trough (the path's final heat) before plunging — a clean quench.</summary>
    private static void DriveGoodRun(ForgeMinigame mg)
    {
        WorkBilletToEnd(mg);

        var trough = ForgePath.HeatAt(mg.Path, 1000);
        var guard = 0;
        while (mg.HeatYPermille > trough + 40 && guard++ < 500)
        {
            mg.Advance(ForgeMinigame.TempoPeriodSeconds);
        }

        mg.Plunge();
    }


    private static ScriptResult RunScript(Action<ForgeMinigame> script)
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);
            script(mg);
            var trace = (ForgeTraceInput)mg.EmittedAction!.Puzzle!;
            return new ScriptResult(mg.PreviewGradePermille!.Value, trace);
        }
        finally
        {
            mg.Free();
        }
    }

    private readonly record struct ScriptResult(int PreviewGrade, ForgeTraceInput Trace);
}
#endif
