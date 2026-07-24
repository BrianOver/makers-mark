#if GDUNIT_TESTS
using System;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U5 (R12/R11/R15/KTD5 — resolves OQ4 to click-to-craft cards): the forge rebuilt around
/// <c>UiKit.Card</c>/<c>ArtRect</c>/<c>StatChip</c> — every scenario proves the pre-rethink
/// contract survives (<see cref="ForgePanel.OnCraftPressed"/>'s <see cref="CraftAction"/> queue,
/// <see cref="ForgePanel.OnUnlockPressed"/>'s <see cref="UnlockTalentAction"/> queue, the
/// <c>MaterialSelect</c>/<see cref="ForgePanel.SelectedMaterialOr"/> re-render, and
/// <c>ProfessionDefinition.CanUnlock</c> talent gating) through the real Controls, plus the
/// KTD5 evidence this unit exists to add: the Craft affordance is reachable ONLY through the
/// deterministic <c>Pressed</c> signal (<see cref="PressEnabled"/>), never a drag gesture.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeCraftTests
{
    [TestCase]
    public void AffordableRecipe_EnablesCraftButton_PressedSignalQueuesCraftAction()
    {
        var ui = MountMainUi();
        try
        {
            // Fresh campaign starts with zero materials (GameFactory.NewGame(seed)) — buy the
            // dagger's 2 copper through the adapter, mirroring ShopPanelTests.CraftDagger, so the
            // card's affordability chip lights and the Craft button is a real, clickable path.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase(); // Morning: the buy lands
            ui.OpenPanel("Forge"); // U21: RefreshAll is visibility-gated — open it for a fresh read

            var craft = Find<Button>(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
            AssertThat(craft.Disabled).IsFalse();

            // KTD5 evidence: the craft affordance is reachable through the Pressed signal —
            // the deterministic path gdUnit can drive — not a drag gesture.
            PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");

            var pending = ui.Adapter.PendingActions.OfType<CraftAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].RecipeId).IsEqual(ScriptedSession.CraftRecipeId);
            AssertThat(pending[0].MaterialKey).IsEqual(ScriptedSession.CraftMaterial);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ZeroMaterials_RendersInsufficientChip_DisablesCraftButton_NoLayoutCollapse()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Player.Materials.IsEmpty).IsTrue();

            var craft = Find<Button>(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
            AssertThat(craft.Disabled).IsTrue();
            AssertThat(craft.TooltipText.Length > 0).IsTrue();

            // The card itself still stands with real content — never a blank/collapsed panel.
            var forgeText = RenderedText(ui.Forge);
            AssertThat(forgeText).Contains("Dagger");
            AssertThat(ui.Forge.FindChildren($"RecipeCard_{ScriptedSession.CraftRecipeId}", "PanelContainer",
                recursive: true, owned: false).Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ChangingMaterialSelect_RerendersRecipeCards_WithChosenMaterial()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(RenderedText(ui.Forge)).Contains("copper"); // dagger's recipe default

            var select = Find<OptionButton>(ui.Forge, "MaterialSelect");
            SelectMaterialByKey(select, "iron");

            AssertThat(RenderedText(ui.Forge)).Contains("iron");

            // The dagger's Craft button now gates on iron (zero on hand), proving the
            // re-render actually re-read SelectedMaterialOr rather than caching copper.
            var craft = Find<Button>(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
            AssertThat(craft.Disabled).IsTrue();
            AssertThat(craft.TooltipText).Contains("iron");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TalentCard_EnablesUnlockButton_OnlyWhenCanUnlockIsTrue()
    {
        var ui = MountMainUi();
        try
        {
            // keen-eye has no prerequisites — unlockable from a fresh save.
            var unlockable = Find<Button>(ui.Forge, "Unlock_keen-eye");
            AssertThat(unlockable.Disabled).IsFalse();

            // master-touch requires keen-eye, not yet unlocked — locked.
            var locked = Find<Button>(ui.Forge, "Unlock_master-touch");
            AssertThat(locked.Disabled).IsTrue();

            PressEnabled(ui.Forge, "Unlock_keen-eye");
            var pending = ui.Adapter.PendingActions.OfType<UnlockTalentAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].NodeId).IsEqual("keen-eye");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U1: the forge minigame's sweet-zone band overlay ─────────────────────────────────────

    [TestCase]
    public void SweetZoneBand_MapsCenterHalfWidthToGaugeFraction_ClampedToDomainEdges()
    {
        // SmeltBeat's own band center (620) — a plain-math pin independent of any live Control.
        AssertThat(ForgeMinigame.BandStartFraction(SmeltBeat.BandCenterPermille, 130)).IsEqual(0.49);
        AssertThat(ForgeMinigame.BandEndFraction(SmeltBeat.BandCenterPermille, 130)).IsEqual(0.75);

        // QuenchBeat's own target (500).
        AssertThat(ForgeMinigame.BandStartFraction(QuenchBeat.TargetPermille, 130)).IsEqual(0.37);
        AssertThat(ForgeMinigame.BandEndFraction(QuenchBeat.TargetPermille, 130)).IsEqual(0.63);

        // A band wide/near-edge enough to run off the [0,1000] domain clamps rather than
        // reporting a negative/over-1 anchor fraction (a talent-widened band near either end).
        AssertThat(ForgeMinigame.BandStartFraction(50, 200)).IsEqual(0.0);
        AssertThat(ForgeMinigame.BandEndFraction(950, 200)).IsEqual(1.0);
    }

    [TestCase]
    public void ForgeMinigameOverlay_DrawsSweetZoneBand_PerBeatType_HiddenDuringForge()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            var band = Find<ColorRect>(overlay, "ForgeMinigameSweetZoneBand");

            // Smelt: the band tracks the beat's own live center/half-width.
            AssertThat(band.Visible).IsTrue();
            var smeltHalf = overlay.Smelt.BandWidthPermille / 2;
            AssertThat(band.AnchorLeft).IsEqual((float)ForgeMinigame.BandStartFraction(SmeltBeat.BandCenterPermille, smeltHalf));
            AssertThat(band.AnchorRight).IsEqual((float)ForgeMinigame.BandEndFraction(SmeltBeat.BandCenterPermille, smeltHalf));

            // Forge's sweet zone is temporal (strike on the metronome beat), not a range on this
            // progress readout — the band hides rather than showing a stale Smelt region.
            overlay.SmeltStop();
            AssertThat(overlay.Current).IsEqual(ForgeMinigame.Stage.Forge);
            AssertThat(band.Visible).IsFalse();

            // Drive Forge (on-beat) to completion, then Quench re-shows the band at its own
            // center/width — proving the overlay updates PER beat type, not just once at Configure.
            var period = overlay.Forge.BeatPeriodSeconds;
            var guard = 0;
            while (overlay.Current == ForgeMinigame.Stage.Forge)
            {
                overlay.ForgeStrike();
                if (overlay.Current != ForgeMinigame.Stage.Forge)
                {
                    break;
                }

                overlay.Advance(period);
                if (++guard > 1000)
                {
                    throw new InvalidOperationException("forge (on-beat) never completed");
                }
            }

            AssertThat(overlay.Current).IsEqual(ForgeMinigame.Stage.Quench);
            AssertThat(band.Visible).IsTrue();
            var quenchHalf = overlay.Quench.BandWidthPermille / 2;
            AssertThat(band.AnchorLeft).IsEqual((float)ForgeMinigame.BandStartFraction(QuenchBeat.TargetPermille, quenchHalf));
            AssertThat(band.AnchorRight).IsEqual((float)ForgeMinigame.BandEndFraction(QuenchBeat.TargetPermille, quenchHalf));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Select a <c>MaterialSelect</c> item by its displayed text (never a hardcoded
    /// index — <c>RecipeTable.MaterialGrades</c> is alphabetical, not insertion-order) and emit
    /// the same <c>ItemSelected</c> signal a real dropdown pick fires, driving the panel's
    /// <c>Refresh()</c> exactly as a player's click would.</summary>
    private static void SelectMaterialByKey(OptionButton select, string materialKey)
    {
        for (var i = 0; i < select.ItemCount; i++)
        {
            if (select.GetItemText(i) == materialKey)
            {
                select.Selected = i;
                select.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                return;
            }
        }

        throw new InvalidOperationException($"No MaterialSelect item '{materialKey}'.");
    }
}
#endif
