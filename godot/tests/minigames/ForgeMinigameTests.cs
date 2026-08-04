#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// ACT 1 (U7 "verify by playing" plan) of the two-act forge — the shared-target-line contract
/// (this overlay renders EXACTLY the polyline <c>ForgePath</c>/<c>ForgeScorer</c> regenerate
/// sim-side from the SAME seed), the captured PARTIAL trace's shape (even-length, in-range, capped
/// Samples/Strikes), and same-script determinism. Every scenario drives <see cref="ForgeMinigame"/>
/// through its public <c>Advance(double)</c>/input-seam methods — no wall-clock, no engine RNG
/// anywhere in the driven path. Act 2 (the quench) and the full two-act chain live in
/// <c>ForgeTwoActTests</c> — this suite is Act 1's own unit-level mechanics only.
/// PROPERTY-ONLY: the Anvil Map is a plain 2D <c>Control</c> canvas, never a 3D <c>SubViewport</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMinigameTests
{
    private const int TestDay = 0;
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    [TestCase]
    public void ShapingResult_HasEvenLengthSamplesAndStrikes_ValuesInRange_RespectsCap()
    {
        var mg = new ForgeMinigame();
        try
        {
            ForgeMinigame.ShapingResult? result = null;
            mg.ShapingDone += r => result = r;
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            WorkBilletToEnd(mg);

            AssertThat(mg.Completed).IsTrue();
            AssertThat(result is not null).IsTrue();
            var r = result!.Value;
            AssertThat(r.Samples.Count % 2).IsEqual(0);
            AssertThat(r.Strikes.Count % 2).IsEqual(0);
            AssertThat(r.Samples.Count / 2).IsLessEqual(ForgeMinigame.MaxSamples);
            AssertThat(r.Strikes.Count / 2).IsLessEqual(ForgeMinigame.MaxSamples);

            foreach (var value in r.Samples)
            {
                AssertThat(value).IsGreaterEqual(0);
                AssertThat(value).IsLessEqual(1000);
            }

            foreach (var value in r.Strikes)
            {
                AssertThat(value).IsGreaterEqual(0);
                AssertThat(value).IsLessEqual(1000);
            }

            AssertThat(r.PathSeed).IsEqual(mg.PathSeed);
            AssertThat(r.StrikesLanded).IsEqual(mg.StrikesLanded);
        }
        finally
        {
            mg.Free(); // never parented into a mounted tree — free it directly, no leaked orphan
        }
    }

    [TestCase]
    public void GoodRun_TracksPathBetter_ThanBadRun_HigherPreviewGrade()
    {
        var good = RunScript(WorkBilletToEnd);

        // A deliberately pathological trace over the SAME target line: heat pinned scorching-hot the
        // whole way (ignoring the target curve entirely) with maximally off-beat forge strikes. Scored
        // by the same pure scorer, this is what "not tracking the path" looks like — a real driven,
        // path-following run must beat it.
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

    /// <summary>
    /// U7 (2026-08-04-001 plan): regression guard for the measured-safe skill-curve floor. Below
    /// ~15 required strikes, a standalone harness referencing the REAL <c>ForgeScorer</c> found
    /// <c>ForgeWinnabilityTests</c>' <c>TempoTight</c>/<c>TempoLoose</c> invariant starts losing on
    /// the 5 CI seeds — not U6's old bug, but ordinary sampling noise from too few strikes. 21/18
    /// is the measured-robust pair (tempo-tight 313.2 vs tempo-loose 298.2, a +15 margin). If either
    /// constant moves, re-run that sweep before trusting the new number — this pin exists so a
    /// future "make it faster" pass cannot silently reopen the exact bug class this unit closed.
    /// </summary>
    [TestCase]
    public void RequiredStrikeConstants_StayAtTheMeasuredSafeValues()
    {
        AssertThat(ForgeMinigame.BaseRequiredStrikes).IsEqual(21);
        AssertThat(ForgeMinigame.MinRequiredStrikes).IsEqual(18);
    }

    [TestCase]
    public void SameScriptTwice_ProducesIdenticalTraceAndGrade_NoHiddenRandomness()
    {
        var first = RunScript(WorkBilletToEnd);
        var second = RunScript(WorkBilletToEnd);

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
        var result = RunScript(WorkBilletToEnd);

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
            var shapingDoneCount = 0;
            mg.Cancelled += () => cancelledCount++;
            mg.ShapingDone += _ => shapingDoneCount++;

            mg.Advance(0.05);
            mg.ForgeStrike();
            mg.Cancel();
            mg.Cancel(); // double-cancel must not double-fire

            AssertThat(mg.WasCancelled).IsTrue();
            AssertThat(mg.Completed).IsFalse();
            AssertThat(shapingDoneCount).IsEqual(0);
            AssertThat(cancelledCount).IsEqual(1);

            // A cancelled run never finishes, even if driven further.
            mg.Advance(5.0);
            mg.ForgeStrike();
            AssertThat(mg.Completed).IsFalse();
            AssertThat(shapingDoneCount).IsEqual(0);
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
            AssertThat(ui.Adapter.AppliedThisPhase.OfType<CraftAction>().Count()).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U3 "forge feel pass": aimed strike, pump strokes (P002 plan) ────────────────────────────
    // Presentation-only — none of these touch ForgeTraceInput/CraftAction/scoring; every gesture
    // still terminates in the SAME public seams (ForgeStrike/PumpStroke) the scenarios above
    // already pin.

    [TestCase]
    public void PumpStroke_RaisesHeatByExactlyOneQuantum_ClampedAt1000()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);
            var before = mg.HeatYPermille;

            mg.PumpStroke();

            AssertThat(mg.HeatYPermille).IsEqual(before + ForgeMinigame.PumpStrokeHeatPermille);

            // Drive heat to the ceiling, then prove one more stroke clamps rather than overshoots.
            var guard = 0;
            while (mg.HeatYPermille < 1000 && guard++ < 100)
            {
                mg.PumpStroke();
            }

            AssertThat(mg.HeatYPermille).IsEqual(1000);
            mg.PumpStroke();
            AssertThat(mg.HeatYPermille).IsEqual(1000);
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void RightDrag_QuantizesIntoPumpStrokes_ByFixedPixelThreshold()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);
            var before = mg.HeatYPermille;

            mg._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            // Exactly 3 strokes' worth of downward motion, split across two motion events (the
            // accumulator must persist across events, not reset each call).
            mg._GuiInput(new InputEventMouseMotion { Relative = new Vector2(0, ForgeMinigame.PumpStrokeDragPixels * 2) });
            mg._GuiInput(new InputEventMouseMotion { Relative = new Vector2(0, ForgeMinigame.PumpStrokeDragPixels) });

            AssertThat(mg.HeatYPermille).IsEqual(before + ForgeMinigame.PumpStrokeHeatPermille * 3);

            // A leftover fractional remainder (< N px) must NOT fire a fourth stroke yet.
            var afterThree = mg.HeatYPermille;
            mg._GuiInput(new InputEventMouseMotion { Relative = new Vector2(0, ForgeMinigame.PumpStrokeDragPixels - 1) });
            AssertThat(mg.HeatYPermille).IsEqual(afterThree);

            // Upward motion is ignored, never subtracted from the banked accumulator or from heat.
            mg._GuiInput(new InputEventMouseMotion { Relative = new Vector2(0, -500) });
            AssertThat(mg.HeatYPermille).IsEqual(afterThree);

            // Releasing the button resets the accumulator — the leftover 17px is discarded, not
            // carried into a fresh drag.
            mg._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            mg._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            mg._GuiInput(new InputEventMouseMotion { Relative = new Vector2(0, ForgeMinigame.PumpStrokeDragPixels - 1) });
            AssertThat(mg.HeatYPermille).IsEqual(afterThree);
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void WouldHit_TrueInsideBilletRect_FalseFarOutside()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            // Unmounted, so the canvas has never been laid out — the anchor resolves to the origin,
            // but the SAME rect math a real, laid-out canvas would use is exercised either way.
            var anchor = mg.BilletAnchor;
            AssertThat(mg.WouldHit(anchor)).IsTrue();
            AssertThat(mg.WouldHit(anchor + new Vector2(ForgeMinigame.BilletHitBoxSize / 2f - 2f, 0))).IsTrue();
            AssertThat(mg.WouldHit(anchor + new Vector2(10_000, 10_000))).IsFalse();
            AssertThat(mg.WouldHit(anchor + new Vector2(-10_000, 10_000))).IsFalse();
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public async Task WouldHit_ReflectsTheRealLaidOutCanvas_ThroughTheRealForgePanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            await SettleLayout(overlay); // container layout is deferred — let the canvas actually size itself

            var anchor = overlay.BilletAnchor;
            AssertThat(anchor.X).IsGreater(0f); // real layout ran — no longer the degenerate origin
            AssertThat(overlay.WouldHit(anchor)).IsTrue();
            AssertThat(overlay.WouldHit(anchor + new Vector2(2_000, 2_000))).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SpaceStrike_StillWorks_WhenWouldHitWouldBeFalse()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            var farAway = mg.BilletAnchor + new Vector2(10_000, 10_000);
            AssertThat(mg.WouldHit(farAway)).IsFalse(); // establishes aim would reject this spot

            // Space never checks WouldHit at all — it is the unaimed, always-valid keyboard path.
            var xBefore = mg.ShapeXPermille;
            mg._GuiInput(new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = false });
            AssertThat(mg.ShapeXPermille).IsGreater(xBefore);
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void LeftClick_OffTheBillet_DoesNotStrike_OnTheBilletDoes()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);

            var xBefore = mg.ShapeXPermille;
            var farAway = mg.BilletAnchor + new Vector2(10_000, 10_000);
            mg._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = farAway });
            AssertThat(mg.ShapeXPermille).IsEqual(xBefore); // missed the billet — no-op

            mg._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = mg.BilletAnchor });
            AssertThat(mg.ShapeXPermille).IsGreater(xBefore); // landed on the billet — struck
        }
        finally
        {
            mg.Free();
        }
    }

    // ── Scripted-run drivers — pure Advance(delta)/input-seam calls, no wall-clock, no RNG ────

    /// <summary>Works the billet to Act 1's OWN finish line (<see cref="ForgeMinigame.ShapingFinishPermille"/>,
    /// not the sim's full 1000-domain) on-tempo, tracking the target line's heat (pumping toward
    /// <see cref="ForgePath.HeatAt"/> at the current shape-x, floored at a workable heat so strikes
    /// always progress and the run never stalls). Every strike lands on a tempo-period boundary, so
    /// it earns the on-beat advance bonus and a clean forge-strike score.</summary>
    private static void WorkBilletToEnd(ForgeMinigame mg)
    {
        var guard = 0;
        while (!mg.Completed)
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
                throw new InvalidOperationException("run never reached Act 1's finish line");
            }
        }
    }

    private static ScriptResult RunScript(Action<ForgeMinigame> script)
    {
        var mg = new ForgeMinigame();
        try
        {
            ForgeMinigame.ShapingResult? result = null;
            mg.ShapingDone += r => result = r;
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);
            script(mg);
            var r = result!.Value;
            var trace = new ForgeTraceInput(r.Samples, r.Strikes, r.PathSeed);
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
