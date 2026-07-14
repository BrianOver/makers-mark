using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>
/// Action handler for the crafting module (U4): <see cref="CraftAction"/> and
/// <see cref="UnlockTalentAction"/>. Crafting is legal in ALL THREE phases — the forge
/// never closes (Morning, Expedition, Evening).
///
/// Determinism note (KTD4): every rejection happens BEFORE any RNG draw, so a refused
/// action never advances the stream. Exactly one Roll100 is drawn per successful craft.
///
/// Talent points (v1): unlocking costs nothing beyond prerequisite edges — the
/// talent-point economy (earn rate, per-node costs) is deliberately deferred; when it
/// lands, the cost check slots in next to the prerequisite check below.
/// </summary>
public sealed class CraftingHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is CraftAction or UnlockTalentAction; // all phases legal

    public (GameState State, RejectedAction? Rejected) Apply(GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            CraftAction craft => ApplyCraft(state, craft, rng, events),
            UnlockTalentAction unlock => ApplyUnlock(state, unlock),
            _ => (state, new RejectedAction(action, $"CraftingHandlers cannot apply {action.GetType().Name}.")),
        };

    private static (GameState, RejectedAction?) ApplyCraft(GameState state, CraftAction action, IDeterministicRng rng, IEventSink events)
    {
        // 1. Recipe must exist.
        if (!RecipeTable.All.TryGetValue(action.RecipeId, out var recipe))
        {
            return (state, new RejectedAction(action, $"Unknown recipe '{action.RecipeId}'."));
        }

        // 2. Material must be a known grade key.
        if (!RecipeTable.MaterialGrades.TryGetValue(action.MaterialKey, out var materialGrade))
        {
            return (state, new RejectedAction(action, $"Unknown material '{action.MaterialKey}'."));
        }

        // 3. Tier gate: tier 2/3 recipes need their unlock talent.
        var talents = state.Player.Talents;
        var gate = recipe.Tier switch
        {
            2 => TalentTree.Tier2Smithing,
            3 => TalentTree.Tier3Smithing,
            _ => null,
        };
        if (gate is not null && !talents.Contains(gate))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'."));
        }

        // 4. Material quantity (material-efficiency saves one, floor of 1).
        var needed = recipe.MaterialQuantity - (talents.Contains(TalentTree.MaterialEfficiency) ? 1 : 0);
        if (needed < 1)
        {
            needed = 1;
        }

        var have = state.Player.Materials.TryGetValue(action.MaterialKey, out var stock) ? stock : 0;
        if (have < needed)
        {
            return (state, new RejectedAction(action, $"Not enough {action.MaterialKey}: need {needed}, have {have}."));
        }

        // 5. All checks passed — consume, roll (the single RNG draw), mint, emit.
        var quality = QualityRoller.Roll(recipe, materialGrade, talents, rng);
        var itemId = new ItemId(state.NextItemId);
        var item = ItemForge.Forge(itemId, recipe, quality, state.Day);

        var newState = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.Add(itemId.Value, item),
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem(action.MaterialKey, have - needed),
            },
        };

        events.Emit(new ItemCrafted(itemId, quality));
        return (newState, null);
    }

    private static (GameState, RejectedAction?) ApplyUnlock(GameState state, UnlockTalentAction action)
    {
        var talents = state.Player.Talents;

        if (!TalentTree.Nodes.TryGetValue(action.NodeId, out var node))
        {
            return (state, new RejectedAction(action, $"Unknown talent node '{action.NodeId}'."));
        }

        if (talents.Contains(action.NodeId))
        {
            return (state, new RejectedAction(action, $"Talent '{action.NodeId}' is already unlocked."));
        }

        foreach (var prereq in node.Prerequisites)
        {
            if (!talents.Contains(prereq))
            {
                return (state, new RejectedAction(action, $"Talent '{action.NodeId}' requires '{prereq}' first."));
            }
        }

        // No cost in v1 (talent-point economy deferred — see class doc).
        var newState = state with
        {
            Player = state.Player with { Talents = talents.Add(action.NodeId) },
        };
        return (newState, null);
    }
}
