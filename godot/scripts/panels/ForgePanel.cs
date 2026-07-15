using GameSim.Contracts;
using GameSim.Crafting;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The forge (R4 display half): every recipe in <see cref="RecipeTable.All"/> with
/// live material availability and a Craft button (queues <see cref="CraftAction"/>),
/// plus the talent mini-tree with Unlock buttons (queues
/// <see cref="UnlockTalentAction"/>). Unlock enablement calls
/// <see cref="TalentTree.CanUnlock"/> — sim-owned validation, only rendered here.
/// </summary>
public partial class ForgePanel : SimPanel
{
    private const string RecipeDefaultOption = "(recipe default)";

    private Label? _feedback;
    private Label? _materialsLabel;
    private OptionButton? _materialSelect;
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

        Clear(_recipeRows!);
        foreach (var recipe in RecipeTable.All.Values)
        {
            var material = SelectedMaterialOr(recipe.MaterialKey);
            var have = state.Player.Materials.TryGetValue(material, out var stock) ? stock : 0;
            var row = AddRow(_recipeRows!);
            AddLabel(row,
                $"{recipe.Name} (t{recipe.Tier} {recipe.Slot}) — {recipe.MaterialQuantity}x {material} (have {have})" +
                $"  atk {recipe.BaseStats.Attack} def {recipe.BaseStats.Defense} wt {recipe.BaseStats.Weight}");
            AddButton(row, $"Craft_{recipe.RecipeId}", "Craft", () => OnCraftPressed(recipe.RecipeId));
        }

        Clear(_talentRows!);
        foreach (var node in TalentTree.Nodes.Values)
        {
            var row = AddRow(_talentRows!);
            var unlocked = state.Player.Talents.Contains(node.NodeId);
            AddLabel(row, $"{node.Name} — {node.Description}{(unlocked ? " [unlocked]" : string.Empty)}");
            if (!unlocked)
            {
                var button = AddButton(row, $"Unlock_{node.NodeId}", "Unlock", () => OnUnlockPressed(node.NodeId));
                button.Disabled = !TalentTree.CanUnlock(node.NodeId, state.Player.Talents);
            }
        }
    }

    /// <summary>The action path the craft buttons share — tests drive this via the button signal.</summary>
    private void OnCraftPressed(string recipeId)
    {
        if (Adapter is null || !RecipeTable.All.TryGetValue(recipeId, out var recipe))
        {
            return;
        }

        var material = SelectedMaterialOr(recipe.MaterialKey);
        Adapter.Queue(new CraftAction(recipeId, material));
        _feedback!.Text = $"queued: craft {recipeId} with {material} (applies next phase)";
    }

    private void OnUnlockPressed(string nodeId)
    {
        Adapter?.Queue(new UnlockTalentAction(nodeId));
        _feedback!.Text = $"queued: unlock {nodeId} (applies next phase)";
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

        AddHeader(body, "RECIPES");
        _recipeRows = new VBoxContainer { Name = "RecipeRows" };
        body.AddChild(_recipeRows);

        AddHeader(body, "TALENTS");
        _talentRows = new VBoxContainer { Name = "TalentRows" };
        body.AddChild(_talentRows);
    }
}
