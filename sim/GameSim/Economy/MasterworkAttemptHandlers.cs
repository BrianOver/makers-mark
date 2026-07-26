using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 3b — the "flux for masterwork attempts" half of "coal + flux
/// consumables"): the player's <see cref="MasterworkAttemptAction"/> handler. Spends coal, rare
/// flux, gold (scaled by the workshop's own <see cref="ForgeTierHandlers"/> progress — power-
/// matched to how far the player has already invested), and the recipe's normal materials on a
/// premium forging session that trades the ordinary craft's RNG roll for a GUARANTEED floor —
/// gold buys certainty, not just odds.
///
/// <para><b>Gated by Forge Tier</b> (ties sink 1 and sink 3 together, as the plan's list implies):
/// the workshop must be at least Forge Tier II (<see cref="RequiredForgeTierIndex"/>) before a
/// masterwork attempt is even offered — the deeper investment in sink 1 unlocks access to sink 3's
/// premium path.</para>
///
/// <para><b>Deterministic outcome, ZERO RNG</b> (Phase D determinism note: all-integer, zero new
/// RNG): rather than drawing from <see cref="QualityRoller"/> (which would spend the kernel's ONE
/// shared RNG stream on a brand-new draw site for this unit), the result is a pure function of the
/// same material-grade-vs-recipe-tier relationship <see cref="QualityRoller.RollActive"/> already
/// uses as its ceiling — recomputed here as a FLOOR instead: at least <see cref="QualityGrade.Superior"/>
/// always (that is what "gold buys certainty" means), stepping up to <see cref="QualityGrade.Masterwork"/>
/// when the supplied material already outgrades the recipe's tier. No <c>IDeterministicRng</c> method
/// is ever called — the parameter exists only to satisfy <see cref="IActionHandler"/>, matching the
/// existing "draws no RNG" handlers already in this composition (Camp/Counter/Commission/Farewell).</para>
/// </summary>
public sealed class MasterworkAttemptHandlers : IActionHandler
{
    /// <summary>Minimum forge-tier index (see <see cref="ForgeTierHandlers.CurrentTierIndex"/>) a
    /// masterwork attempt requires — index 1 = Forge Tier II, the first upgrade past baseline.</summary>
    public const int RequiredForgeTierIndex = 1;

    /// <summary>Coal spent per attempt (keeps the forge running through the extra session).</summary>
    public const int CoalCost = 3;

    /// <summary>Rare flux spent per attempt.</summary>
    public const int FluxCost = 1;

    /// <summary>Gold surcharge per attempt, multiplied by (forge-tier index + 1) — power-matched:
    /// the deeper a workshop has already invested in <see cref="UpgradeForgeAction"/>, the pricier
    /// its premium sessions (a richer smith commands a richer premium).</summary>
    public const int GoldSurchargePerTier = 100;

    public bool CanHandle(PlayerAction action, DayPhase phase) => action is MasterworkAttemptAction; // all phases, like CraftAction — the forge never closes

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not MasterworkAttemptAction attempt)
        {
            return (state, new RejectedAction(action, $"MasterworkAttemptHandlers cannot apply {action.GetType().Name}."));
        }

        // 1. Recipe must exist.
        if (!ProfessionRegistry.TryGetRecipe(attempt.RecipeId, out var recipe))
        {
            return (state, new RejectedAction(action, $"Unknown recipe '{attempt.RecipeId}'."));
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

        // 3. Forge-tier gate — the premium path only opens once the workshop has invested in itself.
        var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
        if (tierIndex < RequiredForgeTierIndex)
        {
            return (state, new RejectedAction(action,
                $"A masterwork attempt requires Forge Tier {RequiredForgeTierIndex + 1} or higher (workshop is Tier {tierIndex + 1})."));
        }

        // 4. Material must be a known grade key.
        if (!RecipeTable.MaterialGrades.TryGetValue(attempt.MaterialKey, out var materialGrade))
        {
            return (state, new RejectedAction(action, $"Unknown material '{attempt.MaterialKey}'."));
        }

        // 5. Recipe tier gate (same talent-node check as an ordinary craft).
        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'."));
        }

        // 6. Material quantity (material-efficiency node saves one, floor of 1 — same rule as CraftingHandlers).
        var efficiency = profession.MaterialEfficiencyNode is { } eff && talents.Contains(eff) ? 1 : 0;
        var neededMaterial = System.Math.Max(1, recipe.MaterialQuantity - efficiency);
        var materialHave = state.Player.Materials.TryGetValue(attempt.MaterialKey, out var matStock) ? matStock : 0;
        if (materialHave < neededMaterial)
        {
            return (state, new RejectedAction(action, $"Not enough {attempt.MaterialKey}: need {neededMaterial}, have {materialHave}."));
        }

        // 7. Coal.
        var coalHave = state.Player.Materials.TryGetValue(ForgeSupplyHandlers.Coal, out var coalStock) ? coalStock : 0;
        if (coalHave < CoalCost)
        {
            return (state, new RejectedAction(action, $"Not enough coal: need {CoalCost}, have {coalHave}."));
        }

        // 8. Flux.
        var fluxHave = state.Player.Materials.TryGetValue(ForgeSupplyHandlers.Flux, out var fluxStock) ? fluxStock : 0;
        if (fluxHave < FluxCost)
        {
            return (state, new RejectedAction(action, $"Not enough flux: need {FluxCost}, have {fluxHave}."));
        }

        // 9. Gold surcharge, power-matched to forge-tier progress.
        var surcharge = GoldSurchargePerTier * (tierIndex + 1);
        if (state.Player.Gold < surcharge)
        {
            return (state, new RejectedAction(action, $"Not enough gold: need {surcharge}, have {state.Player.Gold}."));
        }

        // 10. Day action-budget gate — checked LAST, like every other real-work handler.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        // All checks passed — consume, mint at the guaranteed deterministic floor (no RNG draw at
        // all: see class doc), emit.
        var masteryGrade = profession.Quality.MaterialMasteryNode is { } mastery && talents.Contains(mastery) ? 1 : 0;
        var materialStep = materialGrade + masteryGrade - recipe.Tier;
        var quality = materialStep >= 1 ? QualityGrade.Masterwork : QualityGrade.Superior;

        var itemId = new ItemId(state.NextItemId);
        var item = ItemForge.Forge(itemId, recipe, quality, state.Day);

        var newState = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.Add(itemId.Value, item),
            Player = state.Player with
            {
                Gold = state.Player.Gold - surcharge,
                Materials = state.Player.Materials
                    .SetItem(attempt.MaterialKey, materialHave - neededMaterial)
                    .SetItem(ForgeSupplyHandlers.Coal, coalHave - CoalCost)
                    .SetItem(ForgeSupplyHandlers.Flux, fluxHave - FluxCost),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        events.Emit(new ItemCrafted(itemId, quality));

        return (newState, null);
    }
}
