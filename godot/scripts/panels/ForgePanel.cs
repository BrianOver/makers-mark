using System;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Materials;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

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
///
/// <para>P007 U5 (R12/KTD2/KTD3/KTD5 — resolves OQ4 to click-to-craft): recipe rows are now
/// <see cref="UiKit.Card"/>s — a recipe <see cref="UiKit.ArtRect"/> (falling back to the slot
/// icon on any manifest miss), name/tier/slot, output atk/def/wt <see cref="UiKit.StatChip"/>s,
/// and a material-requirement chip that lights <see cref="UiKit.ChipTone.Positive"/> when
/// affordable / stays <see cref="UiKit.ChipTone.Neutral"/> ("dim") when not — a VISUAL mirror
/// only, read off <c>state.Player.Materials</c>; the kernel's <see cref="CraftAction"/> stays
/// the real gate (a card never bypasses the sim's own validation). Talent rows are unlock
/// cards. Every sim read/queue path (<see cref="SelectedMaterialOr"/>, <see cref="OnCraftPressed"/>/
/// <see cref="OnUnlockPressed"/>, <see cref="ProfessionDefinition.CanUnlock"/> enablement) and
/// every control <c>Name</c> (<c>MaterialSelect</c>, <c>Craft_{recipeId}</c>,
/// <c>Unlock_{nodeId}</c>) is preserved verbatim — only the visual composition changed.</para>
/// </summary>
public partial class ForgePanel : SimPanel
{
    private const string RecipeDefaultOption = "(recipe default)";

    /// <summary>Recipe-art tile edge length (px) for a recipe/talent card — matches
    /// <c>ShopPanel.ItemArtSize</c> so an item's icon reads at the same weight everywhere.</summary>
    private const float RecipeArtSize = 56f;

    /// <summary>Sane minimum width (px) for a recipe card's info column (R7-class guard) — a long
    /// recipe name must keep enough room to wrap at word boundaries, not mid-word.</summary>
    private const float RecipeInfoColumnMinWidth = 180f;

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
                // U6 gate, mirroring CraftingHandlers.ApplyCraft step 5 (material quantity
                // less the material-efficiency talent, floor 1) — the kernel's own math,
                // only rendered here. Crafting is legal in ALL phases (the forge never
                // closes), so there is deliberately NO phase term in this gate.
                var efficiency = profession.MaterialEfficiencyNode is { } eff && unlocked.Contains(eff) ? 1 : 0;
                var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
                var affordable = have >= needed;

                var card = Card($"RecipeCard_{recipe.RecipeId}");
                _recipeRows!.AddChild(card);
                var cardBody = new VBoxContainer();
                card.AddChild(cardBody);

                var headerRow = AddRow(cardBody);
                headerRow.AddChild(ArtRect(
                    AssetCatalog.ItemIconId(recipe.RecipeId), new Vector2(RecipeArtSize, RecipeArtSize),
                    // Caption restored (recipe.Name): on a manifest MISS this is the ONLY place
                    // the placeholder's caption comes from — dropping it would show the raw asset
                    // key instead of the recipe name. On a HIT it also renders under the icon
                    // now, alongside the fuller infoCol line below — redundant, never wrong.
                    IconRegistry.Slot(recipe.Slot), recipe.Name));

                var infoCol = new VBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(RecipeInfoColumnMinWidth, 0),
                };
                headerRow.AddChild(infoCol);
                AddLabel(infoCol, $"{recipe.Name} (t{recipe.Tier} {recipe.Slot})");
                var outputRow = AddRow(infoCol);
                outputRow.AddChild(StatChip("Atk", $"{recipe.BaseStats.Attack}"));
                outputRow.AddChild(StatChip("Def", $"{recipe.BaseStats.Defense}"));
                outputRow.AddChild(StatChip("Wt", $"{recipe.BaseStats.Weight}"));

                // Affordability lighting (KTD5) is a VISUAL MIRROR ONLY, read off the same
                // state.Player.Materials the gate below reads — the kernel's CraftAction stays
                // the real gate; a stale-enabled press is still honestly rejected downstream.
                var controlsRow = AddRow(cardBody);
                controlsRow.AddChild(StatChip(
                    material, $"{recipe.MaterialQuantity}x (have {have})",
                    affordable ? UiKit.ChipTone.Positive : UiKit.ChipTone.Neutral));
                var craft = AddButton(controlsRow, $"Craft_{recipe.RecipeId}", "Craft", () => OnCraftPressed(recipe.RecipeId));
                GateButton(craft, affordable, $"Not enough {material} — need {needed}, have {have}.");
            }

            foreach (var node in profession.TalentNodes.Values)
            {
                var hasNode = unlocked.Contains(node.NodeId);
                var card = Card($"TalentCard_{node.NodeId}");
                _talentRows!.AddChild(card);
                var cardBody = new VBoxContainer();
                card.AddChild(cardBody);

                var row = AddRow(cardBody);
                AddIcon(row, IconRegistry.Glyph("rune"));
                var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddChild(infoCol);
                AddLabel(infoCol, $"{node.Name} — {node.Description}{(hasNode ? " [unlocked]" : string.Empty)}");
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
