#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
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
/// PA6: the forge minigame overlay — deterministic scoring, the Jacksmith carry-forward flaw,
/// the single-action contract, talent-assist wiring, and end-to-end adapter fidelity against
/// PA2's active quality model. Every scenario drives <see cref="ForgeMinigame"/> through its
/// public <c>Advance(double)</c>/input-seam methods (<c>SmeltStop</c>/<c>ForgeStrike</c>/
/// <c>QuenchLock</c>) — no wall-clock, no engine RNG anywhere in the driven path, so "same script
/// twice" tests are a real determinism pin, not a coincidence.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMinigameTests
{
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    [TestCase]
    public void PerfectScriptedRun_ReachesMasterworkReachableGrade_AllSubScoresMax()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty);

            DriveSmeltPerfect(mg);
            DriveForgeOnBeatToCompletion(mg);
            DriveQuenchPerfect(mg);

            AssertThat(mg.Completed).IsTrue();
            AssertThat(mg.Smelt.SubScorePermille).IsEqual(1000);
            AssertThat(mg.Forge.SubScorePermille).IsEqual(1000);
            AssertThat(mg.Quench.SubScorePermille).IsEqual(1000);
            AssertThat(mg.EmittedAction!.PerformanceGrade!.Value).IsGreaterEqual(930); // Masterwork-reachable band
            AssertThat(mg.EmittedAction!.SubScores!).ContainsExactly(1000, 1000, 1000);
        }
        finally
        {
            mg.Free(); // never parented into a mounted tree — free it directly, no leaked orphan
        }
    }

    [TestCase]
    public void SloppyScriptedRun_ScoresMidOrLow_BelowThePerfectRun()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty);

            DriveSmeltOverheat(mg);
            DriveForgeOffBeatToCompletion(mg);
            DriveQuenchSloppy(mg);

            AssertThat(mg.Completed).IsTrue();
            AssertThat(mg.Smelt.Impurity).IsTrue();
            AssertThat(mg.Forge.MarCount).IsGreater(0);
            AssertThat(mg.EmittedAction!.PerformanceGrade!.Value).IsLess(500);
        }
        finally
        {
            mg.Free();
        }
    }

    [TestCase]
    public void SameScriptTwice_ProducesIdenticalGrade_NoHiddenRandomness()
    {
        var first = RunSloppyScript();
        var second = RunSloppyScript();

        AssertThat(second.PerformanceGrade!.Value).IsEqual(first.PerformanceGrade!.Value);
        AssertThat(second.SubScores!).ContainsExactly(first.SubScores!);

        static CraftAction RunSloppyScript()
        {
            var mg = new ForgeMinigame();
            try
            {
                mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty);
                DriveSmeltOverheat(mg);
                DriveForgeOffBeatToCompletion(mg);
                DriveQuenchSloppy(mg);
                return mg.EmittedAction!;
            }
            finally
            {
                mg.Free();
            }
        }
    }

    [TestCase]
    public void SmeltImpurity_CarriesForwardAsVisibleDross_AndCapsForgeSubScore()
    {
        var clean = new ForgeMinigame();
        var impure = new ForgeMinigame();
        try
        {
            clean.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty);
            DriveSmeltPerfect(clean);
            AssertThat(clean.Forge.HasDross).IsFalse();
            AssertThat(clean.Forge.ScoreCapPermille).IsEqual(1000);

            impure.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty);
            DriveSmeltOverheat(impure);
            AssertThat(impure.Smelt.Impurity).IsTrue();
            AssertThat(impure.Forge.HasDross).IsTrue(); // visible the instant the Forge beat opens
            AssertThat(impure.Forge.ScoreCapPermille).IsLess(1000);

            DriveForgeOnBeatToCompletion(impure); // even a clean forge run is capped by the carried-forward dross
            AssertThat(impure.Forge.SubScorePermille).IsLessEqual(impure.Forge.ScoreCapPermille);

            // Sub-scores land in the emitted action in beat order (smelt, forge, quench).
            DriveQuenchPerfect(impure);
            AssertThat(impure.EmittedAction!.SubScores![0]).IsEqual(impure.Smelt.SubScorePermille);
            AssertThat(impure.EmittedAction!.SubScores![1]).IsEqual(impure.Forge.SubScorePermille);
            AssertThat(impure.EmittedAction!.SubScores![2]).IsEqual(impure.Quench.SubScorePermille);
        }
        finally
        {
            clean.Free();
            impure.Free();
        }
    }

    [TestCase]
    public void CompletedRun_QueuesExactlyOneCraftAction_ThroughTheRealForgePanel()
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

            DriveSmeltPerfect(overlay);
            DriveForgeOnBeatToCompletion(overlay);
            DriveQuenchPerfect(overlay);

            var pending = ui.Adapter.PendingActions.OfType<CraftAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].RecipeId).IsEqual(ScriptedSession.CraftRecipeId);
            AssertThat(pending[0].PerformanceGrade!.Value).IsGreaterEqual(930);
            AssertThat(overlay.Visible).IsFalse(); // the overlay closes on completion
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Cancel_MidBeat_QueuesNoAction()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            overlay.Advance(0.05); // mid-smelt, nowhere near complete

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

    [TestCase]
    public void UnlockedAssists_WidenBandsAndSlowDrift_VersusNoTalentBaseline()
    {
        var baseline = new ForgeMinigame();
        var assisted = new ForgeMinigame();
        try
        {
            baseline.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty);

            var everyAssistNode = ImmutableSortedSet.Create(
                TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft, TalentTree.WeaponSpecialist);
            // DaggerRecipe is a Weapon recipe — Weapon Specialist's bonus is in scope (adapter-side
            // slot gating, mirroring the retired SlotShift semantics).
            AssertThat(DaggerRecipe.Slot).IsEqual(ItemSlot.Weapon);
            assisted.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, everyAssistNode);

            AssertThat(assisted.Smelt.BandWidthPermille).IsGreater(baseline.Smelt.BandWidthPermille);
            AssertThat(assisted.Quench.BandWidthPermille).IsGreater(baseline.Quench.BandWidthPermille);
            AssertThat(assisted.Smelt.RisePermilliePerSecond).IsLess(baseline.Smelt.RisePermilliePerSecond);
            AssertThat(assisted.Quench.OscillationHz).IsLess(baseline.Quench.OscillationHz);

            // Off-beat forgiveness only shows up on the Forge beat, which is (re)built on entering
            // that stage — drive both runs to Forge and compare the exported parameter directly.
            DriveSmeltPerfect(baseline);
            DriveSmeltPerfect(assisted);
            AssertThat(assisted.Forge.OffBeatForgivenessPermille).IsGreater(baseline.Forge.OffBeatForgivenessPermille);
        }
        finally
        {
            baseline.Free();
            assisted.Free();
        }
    }

    [TestCase]
    public void EmittedAction_AppliedToTheSim_RespectsPA2sMaterialCeiling()
    {
        var ui = MountMainUi();
        try
        {
            // Copper (grade 1) on the dagger (tier 1): materialStep == 0 → PA2's ceiling caps the
            // craft at Superior regardless of PerformanceGrade — even a perfect 1000 minigame run
            // (which alone would band Masterwork) must NOT reach Masterwork on this material.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            DriveSmeltPerfect(overlay);
            DriveForgeOnBeatToCompletion(overlay);
            DriveQuenchPerfect(overlay);

            ui.Adapter.AdvancePhase(); // CraftAction has no phase gate — resolves at whatever phase this lands on

            var crafted = ui.Adapter.CurrentState.Items.Values.Single(item => item.PlayerCrafted);
            AssertThat(crafted.RecipeId).IsEqual(ScriptedSession.CraftRecipeId);
            AssertThat(crafted.CraftSubScores).ContainsExactly(1000, 1000, 1000);
            AssertThat(crafted.Quality).IsNotEqual(QualityGrade.Masterwork); // PA2's material ceiling held
            AssertThat((int)crafted.Quality).IsLessEqual((int)QualityGrade.Superior);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Scripted-run drivers — pure Advance(delta)/input-seam calls, no wall-clock, no RNG ────

    private static void DriveSmeltPerfect(ForgeMinigame mg)
    {
        var guard = 0;
        while (mg.Smelt.HeatPermille < SmeltBeat.BandCenterPermille && !mg.Smelt.Complete)
        {
            mg.Advance(0.01);
            if (++guard > 200_000)
            {
                throw new InvalidOperationException("smelt heat never reached the band center");
            }
        }

        mg.SmeltStop();
    }

    private static void DriveSmeltOverheat(ForgeMinigame mg)
    {
        var guard = 0;
        while (!mg.Smelt.Complete)
        {
            mg.Advance(0.05); // never Stop — runs past the sweet zone until the timeout/over-heat path fires
            if (++guard > 200_000)
            {
                throw new InvalidOperationException("smelt never auto-completed");
            }
        }
    }

    private static void DriveForgeOnBeatToCompletion(ForgeMinigame mg)
    {
        var guard = 0;
        while (mg.Current == ForgeMinigame.Stage.Forge)
        {
            var period = mg.Forge.BeatPeriodSeconds;
            mg.ForgeStrike(); // Elapsed sits on a beat boundary (0, period, 2*period, ...) — on-beat every time
            if (mg.Current != ForgeMinigame.Stage.Forge)
            {
                break;
            }

            mg.Advance(period);
            if (++guard > 1000)
            {
                throw new InvalidOperationException("forge (on-beat) never completed");
            }
        }
    }

    private static void DriveForgeOffBeatToCompletion(ForgeMinigame mg)
    {
        var period = mg.Forge.BeatPeriodSeconds;
        mg.Advance(period / 2.0); // offset to the exact midpoint between pulses — off-beat every strike
        var guard = 0;
        while (mg.Current == ForgeMinigame.Stage.Forge)
        {
            mg.ForgeStrike();
            if (mg.Current != ForgeMinigame.Stage.Forge)
            {
                break;
            }

            mg.Advance(period); // stays at the same off-beat phase offset every cycle
            if (++guard > 1000)
            {
                throw new InvalidOperationException("forge (off-beat) never completed");
            }
        }
    }

    private static void DriveQuenchPerfect(ForgeMinigame mg) => mg.QuenchLock(); // NeedlePermille starts at dead-center

    private static void DriveQuenchSloppy(ForgeMinigame mg)
    {
        var quarterPeriod = 0.25 / mg.Quench.OscillationHz; // needle swings to its extreme — worst-case distance
        mg.Advance(quarterPeriod);
        mg.QuenchLock();
    }
}
#endif
