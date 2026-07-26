using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 5 — "legendary commissions"): the player's
/// <see cref="CommissionLegendaryWorkAction"/> handler. The plan groups this with the existing
/// (materials-only, Wave 4c) <see cref="ReforgeHeirloomAction"/> memorial reforge under one
/// "capped narrative sinks (3–4/campaign)" line; that action already covers the memorial-reforge
/// half. This handler supplies the missing GOLD half: a large, one-off, capped commission that
/// guarantees a Masterwork-grade item outright (no roll — a commissioned masterwork by definition
/// does not fail), scaled by the workshop's own <see cref="ForgeTierHandlers"/> progress (power-
/// matched: the richer the workshop has already made itself, the bigger the commission it can
/// support).
///
/// Determinism: integer-only, ZERO RNG (the mint is a direct <see cref="QualityGrade.Masterwork"/>
/// construction — no <see cref="IDeterministicRng"/> method is ever called, matching this unit's
/// "zero new RNG" note and the existing "draws no RNG" handler precedent). Reuses the existing
/// <see cref="ItemCrafted"/> event — no new Contracts event type.
/// </summary>
public sealed class LegendaryCommissionHandlers : IActionHandler
{
    /// <summary>Reserved <see cref="PlayerState.Materials"/> counter key (see
    /// <see cref="ForgeTierHandlers"/>'s class doc for why state rides this dictionary instead of a
    /// new Contracts field): lifetime count of legendary commissions fulfilled this campaign.</summary>
    public const string CommissionsUsedKey = "legendary-commissions-used";

    /// <summary>Capped narrative sink, per the plan ("3–4/campaign").</summary>
    public const int MaxPerCampaign = 4;

    /// <summary>Base gold cost, multiplied by (forge-tier index + 1) — power-matched to progression.</summary>
    public const int BaseGold = 3000;

    /// <summary>The commission draws down MaterialMultiplier x the recipe's normal material cost —
    /// the extra investment of commissioning a masterwork outright instead of rolling for one.</summary>
    public const int MaterialMultiplier = 2;

    public bool CanHandle(PlayerAction action, DayPhase phase) => action is CommissionLegendaryWorkAction; // all phases, like a craft

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not CommissionLegendaryWorkAction commission)
        {
            return (state, new RejectedAction(action, $"LegendaryCommissionHandlers cannot apply {action.GetType().Name}."));
        }

        // 1. Capped: only MaxPerCampaign ever, whole campaign.
        var used = state.Player.Materials.TryGetValue(CommissionsUsedKey, out var usedStock) ? usedStock : 0;
        if (used >= MaxPerCampaign)
        {
            return (state, new RejectedAction(action,
                $"All {MaxPerCampaign} legendary commissions for this era are already spoken for."));
        }

        // 2. Recipe must exist.
        if (!ProfessionRegistry.TryGetRecipe(commission.RecipeId, out var recipe))
        {
            return (state, new RejectedAction(action, $"Unknown recipe '{commission.RecipeId}'."));
        }

        // 3. The recipe's profession must be registered and selected by this save.
        if (!ProfessionRegistry.TryGet(recipe!.Profession, out var profession))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' belongs to unknown profession '{recipe.Profession}'."));
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return (state, new RejectedAction(action, $"Profession '{recipe.Profession}' is not selected."));
        }

        // 4. Material must be a known grade key.
        if (!RecipeTable.MaterialGrades.TryGetValue(commission.MaterialKey, out _))
        {
            return (state, new RejectedAction(action, $"Unknown material '{commission.MaterialKey}'."));
        }

        // 5. Recipe tier gate (same talent-node check as an ordinary craft).
        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'."));
        }

        // 6. Material quantity — a full double investment, no efficiency discount (this is the
        //    extravagant path, not the economical one).
        var neededMaterial = recipe.MaterialQuantity * MaterialMultiplier;
        var materialHave = state.Player.Materials.TryGetValue(commission.MaterialKey, out var matStock) ? matStock : 0;
        if (materialHave < neededMaterial)
        {
            return (state, new RejectedAction(action, $"Not enough {commission.MaterialKey}: need {neededMaterial}, have {materialHave}."));
        }

        // 7. Gold, power-matched to forge-tier progress.
        var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
        var cost = BaseGold * (tierIndex + 1);
        if (state.Player.Gold < cost)
        {
            return (state, new RejectedAction(action, $"Not enough gold: need {cost}, have {state.Player.Gold}."));
        }

        // 8. Day action-budget gate — checked LAST, like every other real-work handler.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        // All checks passed — consume, mint a guaranteed Masterwork outright (no roll, no RNG),
        // record the campaign-lifetime cap counter, emit.
        var itemId = new ItemId(state.NextItemId);
        var item = ItemForge.Forge(itemId, recipe, QualityGrade.Masterwork, state.Day);

        var newState = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.Add(itemId.Value, item),
            Player = state.Player with
            {
                Gold = state.Player.Gold - cost,
                Materials = state.Player.Materials
                    .SetItem(commission.MaterialKey, materialHave - neededMaterial)
                    .SetItem(CommissionsUsedKey, used + 1),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        events.Emit(new ItemCrafted(itemId, QualityGrade.Masterwork));

        return (newState, null);
    }
}
