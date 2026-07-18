using System;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Materials;
using GameSim.Professions;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The forge (R4 display half): every recipe of every SELECTED profession (P1 — resolved
/// through <see cref="ProfessionRegistry"/>, so add-on professions appear here with zero
/// panel changes) with live material availability and a Craft button (queues
/// <see cref="CraftAction"/>), plus each profession's talent mini-tree with Unlock buttons
/// (queues <see cref="UnlockTalentAction"/>), plus the Morning vendor's buy rows (Playable
/// Core U3): one row per <see cref="MaterialRegistry.PricedPool"/> key with its marked-up
/// price, queueing <see cref="BuyMaterialAction"/>. Unlock enablement calls
/// <see cref="ProfessionDefinition.CanUnlock"/> — sim-owned validation, only rendered here.
/// </summary>
public partial class ForgePanel : SimPanel
{
    private const string RecipeDefaultOption = "(recipe default)";

    private Label? _feedback;
    private Label? _materialsLabel;
    private OptionButton? _materialSelect;
    private VBoxContainer? _vendorRows;
    private VBoxContainer? _recipeRows;
    private VBoxContainer? _talentRows;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        _materialsLabel!.Text = state.Player.Materials.IsEmpty
            ? "MATERIALS: none — buy ore from returning heroes (Evening ledger)"
            : "MATERIALS: " + string.Join(", ", state.Player.Materials.Select(m => $"{m.Key} x{m.Value}"));

        // Vendor rows (U3): every priced-pool material at its marked-up single-unit price.
        // Display quote only — the sim's MaterialVendorHandlers reprices authoritatively on
        // apply; this mirrors its exact formula (ceilDiv over sim-owned constants), no rules here.
        Clear(_vendorRows!);
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var unit = MaterialRegistry.UnitPrice(key);
            var quote = (int)(((long)unit * (1000 + MaterialVendorHandlers.VendorMarkupPermille) + 999) / 1000);
            var have = state.Player.Materials.TryGetValue(key, out var owned) ? owned : 0;
            var row = AddRow(_vendorRows!);
            AddIcon(row, IconRegistry.Ore(key));
            AddLabel(row, $"{key} — {quote}g each (have {have})");
            var buy = AddButton(row, $"BuyMat_{key}", "Buy 1", () => OnBuyMaterialPressed(key));
            // U6 gate, mirroring MaterialVendorHandlers: Morning-only CanHandle + the gold
            // check. Landing phase = the CURRENT phase (GameKernel.Tick applies the queued
            // batch against state.Phase before advancing), so the buy is legal exactly
            // while the sim still sits AT Morning.
            GateButton(buy,
                legal: state.Phase == DayPhase.Morning && quote <= state.Player.Gold,
                whyNot: state.Phase != DayPhase.Morning
                    ? "The vendor sells in the Morning."
                    : "You can't afford that yet.");
        }

        Clear(_recipeRows!);
        Clear(_talentRows!);
        foreach (var professionId in state.Player.SelectedProfessions)
        {
            if (!ProfessionRegistry.TryGet(professionId, out var profession))
            {
                continue;
            }

            var unlocked = state.Player.TalentsFor(professionId);
            foreach (var recipe in profession!.Recipes.Values)
            {
                var material = SelectedMaterialOr(recipe.MaterialKey);
                var have = state.Player.Materials.TryGetValue(material, out var stock) ? stock : 0;
                var row = AddRow(_recipeRows!);
                AddIcon(row, IconRegistry.Slot(recipe.Slot));
                AddLabel(row,
                    $"{recipe.Name} (t{recipe.Tier} {recipe.Slot}) — {recipe.MaterialQuantity}x {material} (have {have})" +
                    $"  atk {recipe.BaseStats.Attack} def {recipe.BaseStats.Defense} wt {recipe.BaseStats.Weight}");
                var craft = AddButton(row, $"Craft_{recipe.RecipeId}", "Craft", () => OnCraftPressed(recipe.RecipeId));
                // U6 gate, mirroring CraftingHandlers.ApplyCraft step 5 (material quantity
                // less the material-efficiency talent, floor 1) — the kernel's own math,
                // only rendered here. Crafting is legal in ALL phases (the forge never
                // closes), so there is deliberately NO phase term in this gate.
                var efficiency = profession.MaterialEfficiencyNode is { } eff && unlocked.Contains(eff) ? 1 : 0;
                var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
                GateButton(craft, have >= needed, $"Not enough {material} — need {needed}, have {have}.");
            }

            foreach (var node in profession.TalentNodes.Values)
            {
                var row = AddRow(_talentRows!);
                var hasNode = unlocked.Contains(node.NodeId);
                AddIcon(row, IconRegistry.Glyph("rune"));
                AddLabel(row, $"{node.Name} — {node.Description}{(hasNode ? " [unlocked]" : string.Empty)}");
                if (!hasNode)
                {
                    var button = AddButton(row, $"Unlock_{node.NodeId}", "Unlock", () => OnUnlockPressed(node.NodeId, professionId));
                    button.Disabled = !profession.CanUnlock(node.NodeId, unlocked);
                }
            }
        }
    }

    /// <summary>The action path the craft buttons share — tests drive this via the button signal.</summary>
    private void OnCraftPressed(string recipeId)
    {
        if (Adapter is null || !ProfessionRegistry.TryGetRecipe(recipeId, out var recipe))
        {
            return;
        }

        var material = SelectedMaterialOr(recipe!.MaterialKey);
        Adapter.Queue(new CraftAction(recipeId, material));
        _feedback!.Text = $"queued: craft {recipeId} with {material} (applies when the phase ticks)";
    }

    private void OnUnlockPressed(string nodeId, string professionId)
    {
        Adapter?.Queue(new UnlockTalentAction(nodeId, professionId));
        _feedback!.Text = $"queued: unlock {nodeId} (applies when the phase ticks)";
    }

    /// <summary>Queues a one-unit vendor buy (Morning-only in the sim; the U6 gate disables the
    /// row off-Morning, and a rejection that still surfaces becomes MainUi's toast).</summary>
    private void OnBuyMaterialPressed(string materialKey)
    {
        Adapter?.Queue(new BuyMaterialAction(materialKey, 1));
        _feedback!.Text = $"queued: buy 1 {materialKey} (applies when the phase ticks)";
    }

    private string SelectedMaterialOr(string recipeDefault)
    {
        var selected = _materialSelect!.Selected;
        return selected <= 0 ? recipeDefault : _materialSelect.GetItemText(selected);
    }

    private void EnsureBuilt()
    {
        if (_recipeRows is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "ForgeFeedback";
        _materialsLabel = AddLabel(body, "MATERIALS:");

        var selectRow = AddRow(body);
        AddLabel(selectRow, "Craft with:");
        _materialSelect = new OptionButton { Name = "MaterialSelect" };
        _materialSelect.AddItem(RecipeDefaultOption);
        foreach (var key in RecipeTable.MaterialGrades.Keys)
        {
            _materialSelect.AddItem(key);
        }

        _materialSelect.ItemSelected += _ => Refresh();
        selectRow.AddChild(_materialSelect);

        AddHeader(body, "MORNING VENDOR");
        _vendorRows = new VBoxContainer { Name = "VendorRows" };
        body.AddChild(_vendorRows);

        AddHeader(body, "RECIPES");
        _recipeRows = new VBoxContainer { Name = "RecipeRows" };
        body.AddChild(_recipeRows);

        AddHeader(body, "TALENTS");
        _talentRows = new VBoxContainer { Name = "TalentRows" };
        body.AddChild(_talentRows);
    }
}
