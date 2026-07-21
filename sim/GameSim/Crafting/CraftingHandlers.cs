using GameSim.Contracts;
using GameSim.Professions;

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
        // 1. Recipe must exist (global lookup across all professions; consumables
        //    live in the same tables as gear — see RecipeTable).
        if (!ProfessionRegistry.TryGetRecipe(action.RecipeId, out var recipe))
        {
            return (state, new RejectedAction(action, $"Unknown recipe '{action.RecipeId}'."));
        }

        // 2. The recipe's profession must be registered and selected by this save.
        if (!ProfessionRegistry.TryGet(recipe!.Profession, out var profession))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' belongs to unknown profession '{recipe.Profession}'."));
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return (state, new RejectedAction(action, $"Profession '{recipe.Profession}' is not selected."));
        }

        // 3. Material must be a known grade key.
        if (!RecipeTable.MaterialGrades.TryGetValue(action.MaterialKey, out var materialGrade))
        {
            return (state, new RejectedAction(action, $"Unknown material '{action.MaterialKey}'."));
        }

        // 4. Tier gate (read from the profession definition) against this profession's talents.
        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'."));
        }

        // 5. Material quantity (material-efficiency node from the definition saves one, floor of 1).
        var efficiency = profession.MaterialEfficiencyNode is { } eff && talents.Contains(eff) ? 1 : 0;
        var needed = recipe.MaterialQuantity - efficiency;
        if (needed < 1)
        {
            needed = 1;
        }

        var have = state.Player.Materials.TryGetValue(action.MaterialKey, out var stock) ? stock : 0;
        if (have < needed)
        {
            return (state, new RejectedAction(action, $"Not enough {action.MaterialKey}: need {needed}, have {have}."));
        }

        // 6. All checks passed — consume, roll (the single RNG draw), mint, emit.
        // ActiveCraft professions (blacksmith, PA2/PKD2) dominance-roll off the captured
        // PerformanceGrade; every other profession keeps the untouched passive ±8 roll.
        var quality = profession.ActiveCraft
            ? QualityRoller.RollActive(recipe, materialGrade, talents, profession.Quality, rng, action.PerformanceGrade)
            : QualityRoller.Roll(recipe, materialGrade, talents, profession.Quality, rng, action.PerformanceGrade);
        var itemId = new ItemId(state.NextItemId);
        var item = ItemForge.Forge(itemId, recipe, quality, state.Day, action.SubScores);

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
        // Scope the unlock to the action's profession: node lookup, unlocked set, and prereqs
        // are all evaluated within that profession's definition (P1).
        if (!ProfessionRegistry.TryGet(action.Profession, out var profession))
        {
            return (state, new RejectedAction(action, $"Unknown profession '{action.Profession}'."));
        }

        if (!profession!.TalentNodes.TryGetValue(action.NodeId, out var node))
        {
            return (state, new RejectedAction(action, $"Unknown talent node '{action.NodeId}' in profession '{action.Profession}'."));
        }

        var talents = state.Player.TalentsFor(action.Profession);
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
            Player = state.Player.WithTalent(action.Profession, action.NodeId),
        };
        return (newState, null);
    }
}
