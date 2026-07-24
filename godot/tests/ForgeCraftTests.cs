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

    // ── U23d: the Anvil Map overlay opens through the same real Controls (property-level and
    // real-drive Anvil Map coverage itself lives in ForgeMinigameTests) ──────────────────────────

    [TestCase]
    public void WorkForgeButton_OpensAnvilMapOverlay()
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
            AssertThat(overlay.RecipeId).IsEqual(ScriptedSession.CraftRecipeId);
            AssertThat(overlay.Path.Count).IsGreaterEqual(4);
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
