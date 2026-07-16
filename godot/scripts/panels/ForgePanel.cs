using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The forge (R4 display half): every recipe of every SELECTED profession (P1 — resolved
/// through <see cref="ProfessionRegistry"/>, so add-on professions appear here with zero
/// panel changes) with live material availability and a Craft button (queues
/// <see cref="CraftAction"/>), plus each profession's talent mini-tree with Unlock buttons
/// (queues <see cref="UnlockTalentAction"/>). Unlock enablement calls
/// <see cref="ProfessionDefinition.CanUnlock"/> — sim-owned validation, only rendered here.
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
        Clear(_talentRows!);
        foreach (var professionId in state.Player.SelectedProfessions)
        {
            if (!ProfessionRegistry.TryGet(professionId, out var profession))
            {
                continue;
            }

            foreach (var recipe in profession!.Recipes.Values)
            {
                var material = SelectedMaterialOr(recipe.MaterialKey);
                var have = state.Player.Materials.TryGetValue(material, out var stock) ? stock : 0;
                var row = AddRow(_recipeRows!);
                AddIcon(row, IconRegistry.Slot(recipe.Slot));
                AddLabel(row,
                    $"{recipe.Name} (t{recipe.Tier} {recipe.Slot}) — {recipe.MaterialQuantity}x {material} (have {have})" +
                    $"  atk {recipe.BaseStats.Attack} def {recipe.BaseStats.Defense} wt {recipe.BaseStats.Weight}");
                AddButton(row, $"Craft_{recipe.RecipeId}", "Craft", () => OnCraftPressed(recipe.RecipeId));
            }

            var unlocked = state.Player.TalentsFor(professionId);
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
        _feedback!.Text = $"queued: craft {recipeId} with {material} (applies next phase)";
    }

    private void OnUnlockPressed(string nodeId, string professionId)
    {
        Adapter?.Queue(new UnlockTalentAction(nodeId, professionId));
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
