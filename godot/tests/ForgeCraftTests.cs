#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Materials;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using GodotClient.Ui;
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

            var pending = ui.Adapter.AppliedThisPhase.OfType<CraftAction>().ToList();
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
            var pending = ui.Adapter.AppliedThisPhase.OfType<UnlockTalentAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].NodeId).IsEqual("keen-eye");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U3 (painted-interiors plan): FocusSection sibling coverage — the station-driven
    // scroll/flash must never collapse the panel it lands on (mirrors
    // ZeroMaterials_RendersInsufficientChip_DisablesCraftButton_NoLayoutCollapse's own contract,
    // just for the NEW entry point rather than a fresh Refresh) ──────────────────────────────────

    [TestCase]
    public void FocusSection_CraftAndMaterials_NeverCollapsesTheLayout()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            ui.Forge.FocusSection("craft");
            AssertThat(ui.Forge.LastFocusedSection).IsEqual("craft");
            // The recipe card itself still stands with real content — never a blank/collapsed panel.
            AssertThat(RenderedText(ui.Forge)).Contains("Dagger");
            AssertThat(ui.Forge.FindChildren($"RecipeCard_{ScriptedSession.CraftRecipeId}", "PanelContainer",
                recursive: true, owned: false).Count > 0).IsTrue();

            ui.Forge.FocusSection("materials");
            AssertThat(ui.Forge.LastFocusedSection).IsEqual("materials");
            AssertThat(RenderedText(ui.Forge)).Contains("copper"); // a vendor row for the craft material
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FocusSection_UnknownSection_IsANoOp_NeverThrows()
    {
        // Table-validation (InteriorRoomTests.KnownFocusValues) is what stops a bogus Focus from
        // ever shipping in InteriorLayout2D — this only proves FocusSection itself degrades
        // quietly rather than throwing if it is ever handed a value outside that table.
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            ui.Forge.FocusSection("not-a-real-section");
            AssertThat(ui.Forge.LastFocusedSection).IsEqual("not-a-real-section");
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

    // ── U3 (the Foundry): forge-tier chip, coal/flux stock chips, UpgradeForgeAction (bell-rider),
    // BuyForgeSupplyAction (immediate) — the two Morning sinks that were fully sim-implemented and
    // console-reachable but had no button in the shipped client. ──────────────────────────────────

    /// <summary>Default seed-2026 campaign with gold/copper/forge-tier/coal/flux overridden —
    /// mirrors <c>RejectionUxTests.CampaignWith</c>'s override shape, widened for the Foundry's
    /// own reserved-key state (<see cref="ForgeTierHandlers.ForgeTierKey"/>, <see
    /// cref="ForgeSupplyHandlers.Coal"/>/<see cref="ForgeSupplyHandlers.Flux"/>). U4 (P6b) adds
    /// <paramref name="commissionsUsed"/> for <see cref="LegendaryCommissionHandlers.CommissionsUsedKey"/> —
    /// additive, default 0, so every existing call site is unaffected.</summary>
    private static SimAdapter FoundryCampaign(int gold, int copper = 0, int tierIndex = 0, int coal = 0, int flux = 0, int commissionsUsed = 0)
    {
        var state = GameComposition.NewCampaign(ScriptedSession.Seed);
        var materials = ImmutableSortedDictionary<string, int>.Empty;
        if (copper > 0) materials = materials.SetItem(MaterialRegistry.Copper, copper);
        if (tierIndex > 0) materials = materials.SetItem(ForgeTierHandlers.ForgeTierKey, tierIndex);
        if (coal > 0) materials = materials.SetItem(ForgeSupplyHandlers.Coal, coal);
        if (flux > 0) materials = materials.SetItem(ForgeSupplyHandlers.Flux, flux);
        if (commissionsUsed > 0) materials = materials.SetItem(LegendaryCommissionHandlers.CommissionsUsedKey, commissionsUsed);

        return new SimAdapter(state with { Player = state.Player with { Gold = gold, Materials = materials } });
    }

    // ── U4 (P6b): the two last dead verbs — MasterworkAttemptAction (a purchased GUARANTEE,
    // resolves immediately) and CommissionLegendaryWorkAction (capped at 4/campaign, a bell-rider)
    // — both sim-complete since Phase D, both now buttons on every recipe card beside whichever
    // craft path that card already offers. ──────────────────────────────────────────────────────

    [TestCase]
    public void BelowForgeTierTwo_MasterworkRowDisabled_ReasonNamesTheTierGate_TierChipShowsCurrentTier()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 999_999, copper: 100, coal: 10, flux: 10)); // tierIndex defaults to 0 (Forge I)
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(RenderedText(ui.Forge)).Contains("Forge I");

            var masterwork = Find<Button>(ui.Forge, $"Masterwork_{ScriptedSession.CraftRecipeId}");
            var state = ui.Adapter.CurrentState;
            AssertThat(ActionLegality.IsLegal(state, new MasterworkAttemptAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial), state.Phase)).IsFalse();
            AssertThat(masterwork.Disabled).IsTrue();
            AssertThat(masterwork.TooltipText).Contains("Forge Tier");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AtForgeTierTwo_Affordable_MasterworkAttemptSucceeds_ConsumesExactCoalFluxAndSurcharge_MintsPlayerCraftedItem()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 500, copper: 10, tierIndex: 1, coal: 3, flux: 1));
        try
        {
            ui.OpenPanel("Forge");

            var state = ui.Adapter.CurrentState;
            AssertThat(ActionLegality.IsLegal(state, new MasterworkAttemptAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial), state.Phase)).IsTrue();
            var masterwork = Find<Button>(ui.Forge, $"Masterwork_{ScriptedSession.CraftRecipeId}");
            AssertThat(masterwork.Disabled).IsFalse();

            // KTD5 evidence: a real Pressed signal, not a hand-built action.
            PressEnabled(ui.Forge, $"Masterwork_{ScriptedSession.CraftRecipeId}");

            var after = ui.Adapter.CurrentState;
            AssertThat(after.Player.Gold).IsEqual(300); // 500 - 100*(1+1) = 200 surcharge at Tier II
            AssertThat(after.Player.Materials[ForgeSupplyHandlers.Coal]).IsEqual(0);
            AssertThat(after.Player.Materials[ForgeSupplyHandlers.Flux]).IsEqual(0);
            AssertThat(after.Player.Materials[MaterialRegistry.Copper]).IsEqual(8); // 10 - 2 (dagger's MaterialQuantity)

            var minted = after.Items.Values.Single(i => i.RecipeId == ScriptedSession.CraftRecipeId);
            AssertThat(minted.Quality == QualityGrade.Superior || minted.Quality == QualityGrade.Masterwork).IsTrue();
            // "A purchased masterwork still earns attribution beats" — the mint-time half of that
            // proof (the full end-to-end beat is proven at the sim level in
            // MasterworkAttemptHandlersTests, which does not need a Godot runtime to exercise).
            AssertThat(minted.PlayerCrafted).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void MissingFlux_MasterworkRowDisabled_QueueingAnywayRejectsWithNoPartialConsumption()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 500, copper: 10, tierIndex: 1, coal: 3, flux: 0));
        try
        {
            ui.OpenPanel("Forge");

            var masterwork = Find<Button>(ui.Forge, $"Masterwork_{ScriptedSession.CraftRecipeId}");
            AssertThat(masterwork.Disabled).IsTrue();
            AssertThat(masterwork.TooltipText).Contains("flux");

            // Row disabled is proven above; also prove the KERNEL itself refuses it with NO
            // partial consumption, same style as BuyingCoal_WithInsufficientGold_Rejects... above —
            // a future edit that ever let a stale-enabled row through must still cost nothing.
            ui.Adapter.Queue(new MasterworkAttemptAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Player.Gold).IsEqual(500);
            AssertThat(ui.Adapter.CurrentState.Player.Materials[ForgeSupplyHandlers.Coal]).IsEqual(3);
            AssertThat(ui.Adapter.CurrentState.Player.Materials.ContainsKey(ForgeSupplyHandlers.Flux)).IsFalse();
            AssertThat(ui.Adapter.CurrentState.Player.Materials[MaterialRegistry.Copper]).IsEqual(10);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void LegendaryCommission_AtCap_RowDisabled_ReasonNamesTheCap_CounterReadsZeroRemaining()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 999_999, copper: 100, commissionsUsed: LegendaryCommissionHandlers.MaxPerCampaign));
        try
        {
            ui.OpenPanel("Forge");

            var state = ui.Adapter.CurrentState;
            AssertThat(ActionLegality.IsLegal(state, new CommissionLegendaryWorkAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial), state.Phase)).IsFalse();
            var commission = Find<Button>(ui.Forge, $"Commission_{ScriptedSession.CraftRecipeId}");
            AssertThat(commission.Disabled).IsTrue();
            AssertThat(commission.TooltipText).Contains("already spoken for");
            AssertThat(commission.Text).Contains($"0 of {LegendaryCommissionHandlers.MaxPerCampaign}");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void LegendaryCommission_Affordable_QueuesAsBellRider_TrayShowsVocabString()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 999_999, copper: 100));
        try
        {
            ui.OpenPanel("Forge");

            var state = ui.Adapter.CurrentState;
            AssertThat(ActionLegality.IsLegal(state, new CommissionLegendaryWorkAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial), state.Phase)).IsTrue();
            var commission = Find<Button>(ui.Forge, $"Commission_{ScriptedSession.CraftRecipeId}");
            AssertThat(commission.Disabled).IsFalse();

            // KTD5 evidence: a real Pressed signal.
            PressEnabled(ui.Forge, $"Commission_{ScriptedSession.CraftRecipeId}");

            var queuedAction = new CommissionLegendaryWorkAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial);
            AssertThat(ui.Adapter.PendingActions.OfType<CommissionLegendaryWorkAction>().Count()).IsEqual(1);
            var chip = Find<HBoxContainer>(ui, "BellTray").GetChild(0);
            AssertThat(Find<Label>(chip, "Verb").Text).IsEqual(PendingVerbVocab.DisplayName(queuedAction));
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void FreshSave_TierChipReadsForgeI_UpgradeRowDisabled_ReasonNamesMissingCopper()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge"); // fresh default campaign: zero copper, Morning day 1

            AssertThat(RenderedText(ui.Forge)).Contains("Forge I");

            var upgrade = Find<Button>(ui.Forge, "UpgradeForge");
            var state = ui.Adapter.CurrentState;
            var legal = ActionLegality.IsLegal(state, new UpgradeForgeAction(), state.Phase);
            AssertThat(legal).IsFalse(); // sanity: the scenario really is illegal, not a stale mirror
            AssertThat(upgrade.Disabled).IsEqual(!legal);
            AssertThat(upgrade.TooltipText).Contains("copper");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AffordableUpgrade_InMorning_EnablesRow_PressQueuesBellRider_TrayShowsDisplayName()
    {
        var ui = MountMainUi(FoundryCampaign(gold: ForgeTierHandlers.GoldCost[0], copper: ForgeTierHandlers.OreQuantity));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.OpenPanel("Forge");

            var state = ui.Adapter.CurrentState;
            var legal = ActionLegality.IsLegal(state, new UpgradeForgeAction(), state.Phase);
            AssertThat(legal).IsTrue(); // sanity: the scenario really is legal
            var upgrade = Find<Button>(ui.Forge, "UpgradeForge");
            AssertThat(upgrade.Disabled).IsFalse();

            PressEnabled(ui.Forge, "UpgradeForge");

            AssertThat(ui.Adapter.PendingActions.OfType<UpgradeForgeAction>().Count()).IsEqual(1);
            var chip = Find<HBoxContainer>(ui, "BellTray").GetChild(0);
            AssertThat(Find<Label>(chip, "Verb").Text).IsEqual(PendingVerbVocab.DisplayName(new UpgradeForgeAction()));
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SameAffordableState_InEvening_DisablesRow_ReasonNamesThePhase()
    {
        var ui = MountMainUi(FoundryCampaign(gold: ForgeTierHandlers.GoldCost[0], copper: ForgeTierHandlers.OreQuantity));
        try
        {
            AdvanceToPhase(ui, DayPhase.Evening);
            ui.OpenPanel("Forge");

            var state = ui.Adapter.CurrentState;
            var legal = ActionLegality.IsLegal(state, new UpgradeForgeAction(), state.Phase);
            AssertThat(legal).IsFalse(); // sanity: Morning-only, and it is Evening now

            var upgrade = Find<Button>(ui.Forge, "UpgradeForge");
            AssertThat(upgrade.Disabled).IsTrue();
            AssertThat(upgrade.TooltipText).Contains("Morning");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void BuyingCoal_TenTimes_DropsGoldByForty_IncrementsCoalChip()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 100));
        try
        {
            ui.OpenPanel("Forge");
            var startingGold = ui.Adapter.CurrentState.Player.Gold;

            for (var i = 0; i < 10; i++)
            {
                PressEnabled(ui.Forge, $"BuySupply_{ForgeSupplyHandlers.Coal}");
            }

            AssertThat(ui.Adapter.CurrentState.Player.Gold).IsEqual(startingGold - 10 * ForgeSupplyHandlers.UnitPrice(ForgeSupplyHandlers.Coal));
            AssertThat(ui.Adapter.CurrentState.Player.Materials[ForgeSupplyHandlers.Coal]).IsEqual(10);
            AssertThat(RenderedText(ui.Forge)).Contains("10"); // the Coal stat chip's own value
        }
        finally { Unmount(ui); }
    }

    /// <summary>Flux mirrors coal's row exactly (same handler, different key/price) — this proves
    /// its OWN button is a real, independently-clickable path rather than assuming coal's coverage
    /// carries over.</summary>
    [TestCase]
    public void BuyingFlux_PressEnabled_DropsGoldByUnitPrice_IncrementsFluxChip()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 100));
        try
        {
            ui.OpenPanel("Forge");
            var startingGold = ui.Adapter.CurrentState.Player.Gold;

            PressEnabled(ui.Forge, $"BuySupply_{ForgeSupplyHandlers.Flux}");

            AssertThat(ui.Adapter.CurrentState.Player.Gold).IsEqual(startingGold - ForgeSupplyHandlers.UnitPrice(ForgeSupplyHandlers.Flux));
            AssertThat(ui.Adapter.CurrentState.Player.Materials[ForgeSupplyHandlers.Flux]).IsEqual(1);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void BuyingCoal_WithInsufficientGold_RejectsWithNoStateChange()
    {
        var ui = MountMainUi(FoundryCampaign(gold: 0));
        try
        {
            ui.OpenPanel("Forge");

            // The row itself renders Disabled for this state (proven by the enable-mirror tests
            // above) — the insufficient-gold path is exercised the same way RejectionUxTests does,
            // queuing directly to prove the KERNEL still refuses it even if some future edit ever
            // let a stale-enabled row through.
            AssertThat(Find<Button>(ui.Forge, $"BuySupply_{ForgeSupplyHandlers.Coal}").Disabled).IsTrue();

            ui.Adapter.Queue(new BuyForgeSupplyAction(ForgeSupplyHandlers.Coal, 1));

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Player.Gold).IsEqual(0);
            AssertThat(ui.Adapter.CurrentState.Player.Materials.ContainsKey(ForgeSupplyHandlers.Coal)).IsFalse();
            AssertThat(RenderedText(ui)).Contains("You can't afford that yet.");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AtMaxTier_UpgradeRowDisabled_ReasonNamesTheMaximum()
    {
        var maxTierIndex = ForgeTierHandlers.MaxUpgradeIndex + 1; // Forge V — nothing left to buy
        var ui = MountMainUi(FoundryCampaign(gold: 999_999, tierIndex: maxTierIndex));
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(ForgeTierHandlers.CurrentTierIndex(ui.Adapter.CurrentState.Player)).IsEqual(maxTierIndex);
            var state = ui.Adapter.CurrentState;
            var legal = ActionLegality.IsLegal(state, new UpgradeForgeAction(), state.Phase);
            AssertThat(legal).IsFalse();

            var upgrade = Find<Button>(ui.Forge, "UpgradeForge");
            AssertThat(upgrade.Disabled).IsTrue();
            AssertThat(upgrade.TooltipText).Contains("maximum");
            AssertThat(RenderedText(ui.Forge)).Contains("Forge V");
        }
        finally { Unmount(ui); }
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
