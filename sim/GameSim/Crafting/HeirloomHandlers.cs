using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Crafting;

/// <summary>
/// Wave 4c (U20, heirloom reforge): the player's <see cref="ReforgeHeirloomAction"/> handler —
/// reforges a fallen hero's worn gear into a new item carrying their legend-line forward
/// (<see cref="Item.HeirloomLineage"/>), "the dead persist as inheritance" (R6).
///
/// Legal in ALL THREE phases, same as <see cref="CraftingHandlers"/> (the forge never closes) —
/// a reforge IS a craft, with a source-provenance guard bolted on front.
///
/// Guard chain (each a distinct typed rejection, ALL before any RNG draw — KTD4):
/// 1. <see cref="ReforgeHeirloomAction.SourceItem"/> must be a real item.
/// 2. It must have been worn by a hero at the moment they died — recorded on some
///    <see cref="HeroDied"/> event's <see cref="HeroDied.WornGear"/> — the ONLY way a piece of
///    gear becomes reforgeable. (An item worn by a still-living hero, or never worn by anyone who
///    died, is never eligible.)
/// 3. It must not already have been reforged — a <see cref="HeirloomReforged"/> event with the
///    same <see cref="HeirloomReforged.SourceItem"/> already in the log. One heirloom per fallen
///    piece of gear, ever.
/// 4-8. The SAME recipe/profession/material/tier/quantity guard chain as
///    <see cref="CraftingHandlers"/>'s <c>ApplyCraft</c> (deliberately duplicated inline — the
///    handler is private-static there and Contracts/ owns no shared Validate seam; see
///    <c>GameSim.Advisor.ActionLegality</c>'s class doc for the codebase's standing precedent on
///    this exact kind of duplication).
/// 9. The day action-budget gate (Game-Feel Plan G3) — reforging is real work, checked LAST like
///    every other handler's slot gate.
///
/// Determinism: reuses <see cref="ItemForge.Forge"/> and the SAME <see cref="QualityRoller"/>
/// path a normal craft draws. <see cref="ReforgeHeirloomAction"/> carries no PerformanceGrade/
/// Puzzle, so it always resolves through the null-grade (auto-craft) branch — exactly ONE
/// <see cref="IDeterministicRng.Roll100"/> draw, identical to any other auto-craft (KTD4 draw-
/// count contract). The lineage string is pure data derived from already-resolved state, no RNG.
/// </summary>
public sealed class HeirloomHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) => action is ReforgeHeirloomAction;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not ReforgeHeirloomAction reforge)
        {
            return (state, new RejectedAction(action, $"HeirloomHandlers cannot apply {action.GetType().Name}."));
        }

        // 1. Source item must be real.
        if (!state.Items.TryGetValue(reforge.SourceItem.Value, out var sourceItem))
        {
            return (state, new RejectedAction(action, $"Unknown source item {reforge.SourceItem}."));
        }

        // 2. Source item must have been worn by a hero recorded in a HeroDied event — the first
        //    such record in log order, deterministic.
        HeroId? fallenHero = null;
        foreach (var evt in state.EventLog)
        {
            if (evt is HeroDied died && WoreItem(died.WornGear, reforge.SourceItem))
            {
                fallenHero = died.Hero;
                break;
            }
        }

        if (fallenHero is null)
        {
            return (state, new RejectedAction(action, $"{sourceItem.Name} ({reforge.SourceItem}) was never worn by a fallen hero."));
        }

        // 3. Guard against reforging the same fallen gear twice.
        foreach (var evt in state.EventLog)
        {
            if (evt is HeirloomReforged already && already.SourceItem == reforge.SourceItem)
            {
                return (state, new RejectedAction(action, $"{sourceItem.Name} ({reforge.SourceItem}) has already been reforged."));
            }
        }

        // 4. Recipe must exist.
        if (!ProfessionRegistry.TryGetRecipe(reforge.RecipeId, out var recipe))
        {
            return (state, new RejectedAction(action, $"Unknown recipe '{reforge.RecipeId}'."));
        }

        // 5. The recipe's profession must be registered and selected by this save.
        if (!ProfessionRegistry.TryGet(recipe!.Profession, out var profession))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' belongs to unknown profession '{recipe.Profession}'."));
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return (state, new RejectedAction(action, $"Profession '{recipe.Profession}' is not selected."));
        }

        // 6. Material must be a known grade key.
        if (!RecipeTable.MaterialGrades.TryGetValue(reforge.MaterialKey, out var materialGrade))
        {
            return (state, new RejectedAction(action, $"Unknown material '{reforge.MaterialKey}'."));
        }

        // 7. Tier gate.
        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'."));
        }

        // 8. Material quantity (material-efficiency node saves one, floor of 1).
        var efficiency = profession.MaterialEfficiencyNode is { } eff && talents.Contains(eff) ? 1 : 0;
        var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
        var have = state.Player.Materials.TryGetValue(reforge.MaterialKey, out var stock) ? stock : 0;
        if (have < needed)
        {
            return (state, new RejectedAction(action, $"Not enough {reforge.MaterialKey}: need {needed}, have {have}."));
        }

        // 9. Day action-budget gate — checked LAST, like every other real-work handler.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        // 10. All checks passed — consume, roll (the single RNG draw; null grade = auto-craft
        //     baseline, exactly the same path a bare CraftAction with no captured grade takes),
        //     mint, stamp the lineage, emit.
        var quality = profession.ActiveCraft
            ? QualityRoller.RollActive(recipe, materialGrade, talents, profession.Quality, rng)
            : QualityRoller.Roll(recipe, materialGrade, talents, profession.Quality, rng);
        var itemId = new ItemId(state.NextItemId);
        var item = ItemForge.Forge(itemId, recipe, quality, state.Day);

        var fallenName = state.Heroes.TryGetValue(fallenHero.Value.Value, out var fallenRecord) ? fallenRecord.Name : "a fallen hero";
        var lineage = $"forged from the {sourceItem.Name} of {fallenName}";
        item = item with { HeirloomLineage = lineage };

        var newState = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.Add(itemId.Value, item),
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem(reforge.MaterialKey, have - needed),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        events.Emit(new ItemCrafted(itemId, quality));
        events.Emit(new HeirloomReforged(itemId, reforge.SourceItem, lineage));

        return (newState, null);
    }

    private static bool WoreItem(GearSet gear, ItemId item) =>
        gear.Weapon == item || gear.Shield == item || gear.Armor == item || gear.Trinket == item;
}
