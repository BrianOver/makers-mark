using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Materials;
using GameSim.Professions;

namespace GameSim.Advisor;

/// <summary>
/// One suggested next step: an action to submit (or <c>null</c> when nothing productive is legal
/// yet — the destitution floor, <see cref="DestitutionRecoverySystem"/>, will resolve it next
/// Morning without player input) plus a short human-readable reason.
/// </summary>
public sealed record Suggestion(PlayerAction? Action, string Reason);

/// <summary>
/// Sim-side "what should I do" (plan 2026-07-19-002 U10, KTD9). Pure projection over
/// <see cref="GameState"/>: no kernel registration, no RNG, no <c>Contracts/</c> contact. Every
/// suggested <see cref="Suggestion.Action"/> is re-checked through
/// <see cref="ActionLegality.IsLegal"/> before being returned — Suggest never proposes an illegal
/// action.
///
/// Reuses <see cref="DestitutionRecoverySystem"/>'s cheapest-productive-path arithmetic (the
/// smallest tier-1 recipe's material, topped up at the vendor's own
/// <see cref="MaterialVendorHandlers.QuoteCost"/>) so the advisor's top pick and the no-softlock
/// floor's rescue target can never drift apart — when the state is a true destitution dead-end
/// (below R5's three conditions), the cheapest-path MATERIAL this module names is the exact one
/// <see cref="DestitutionRecoverySystem"/> is about to buy the player up to.
/// </summary>
public static class ObjectiveAdvisor
{
    public static ImmutableList<Suggestion> Suggest(GameState state)
    {
        var suggestions = ImmutableList.CreateBuilder<Suggestion>();
        var phase = state.Phase;
        var (materialKey, quantity, cost) = CheapestProductivePath(state.Player);

        // 1. If a craft is reachable RIGHT NOW (already enough of the cheapest-path material,
        //    cost == 0), suggest crafting it directly — the tightest loop.
        if (materialKey is not null && cost == 0)
        {
            var recipe = CheapestTier1Recipe(state.Player, materialKey);
            if (recipe is not null)
            {
                var craft = new CraftAction(recipe.RecipeId, materialKey);
                if (ActionLegality.IsLegal(state, craft, phase))
                {
                    suggestions.Add(new Suggestion(craft, $"You already have enough {materialKey} to craft '{recipe.RecipeId}'."));
                }
            }
        }
        else if (materialKey is not null && phase == DayPhase.Morning)
        {
            // 2. Otherwise, if buying the cheapest-path material at the Morning vendor is
            //    affordable, suggest that first (Playable Core's tutorial-shaped first step:
            //    buy material before craft is possible on a fresh save).
            var buy = new BuyMaterialAction(materialKey, quantity);
            if (ActionLegality.IsLegal(state, buy, phase))
            {
                suggestions.Add(new Suggestion(buy, $"Buy {quantity} {materialKey} ({cost}g) — the cheapest path to your next craft."));
            }
            else
            {
                // 3. True destitution dead-end (R5): the cheapest path is unaffordable and the
                //    floor's three conditions all hold. No legal action moves the player forward
                //    this Morning — DestitutionRecoverySystem tops the purse up to this SAME
                //    material's cost automatically before Expedition. Name it, but propose no
                //    illegal action.
                suggestions.Add(new Suggestion(null,
                    $"Not enough gold for {materialKey} yet ({cost}g needed) — the town's recovery stipend will cover it this morning."));
            }
        }

        // 4. Stock any unshelved player craft — always legal once one exists.
        var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        var equipped = state.Heroes.Values
            .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor })
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToHashSet();
        var stockable = state.Items.Values.FirstOrDefault(i =>
            i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value));
        if (stockable is not null)
        {
            var price = Math.Max(1, (stockable.Stats.Attack + stockable.Stats.Defense) * 2);
            var stock = new StockAction(stockable.Id, price);
            if (ActionLegality.IsLegal(state, stock, phase))
            {
                suggestions.Add(new Suggestion(stock, $"Shelve '{stockable.Name}' — it's finished and unsold."));
            }
        }

        return suggestions.ToImmutable();
    }

    /// <summary>
    /// The exact cheapest-productive-path arithmetic <see cref="DestitutionRecoverySystem"/> uses
    /// (kept in lockstep on purpose — see class doc): the best-stocked priced material topped up
    /// to the smallest selected-profession tier-1 recipe's quantity, quoted at the vendor's own
    /// formula. Returns the material key, the quantity still needed, and its quote cost (0 = a
    /// craft is already possible). Null key means no tier-1 recipe exists for any selected
    /// profession (defensive; every shipped profession has one).
    /// </summary>
    private static (string? MaterialKey, int Quantity, int Cost) CheapestProductivePath(PlayerState player)
    {
        var minQuantity = CheapestTier1RecipeQuantity(player);

        string? bestKey = null;
        var bestNeeded = 0;
        var bestCost = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var held = player.Materials.TryGetValue(key, out var stock) ? stock : 0;
            var needed = Math.Max(0, minQuantity - held);
            var cost = needed == 0 ? 0 : MaterialVendorHandlers.QuoteCost(key, needed);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestKey = key;
                bestNeeded = needed;
            }
        }

        return (bestKey, bestNeeded, bestCost == int.MaxValue ? 0 : bestCost);
    }

    /// <summary>Mirrors <see cref="DestitutionRecoverySystem"/>'s private helper of the same name.</summary>
    private static int CheapestTier1RecipeQuantity(PlayerState player)
    {
        var min = int.MaxValue;
        foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
        {
            if (recipe.Tier == 1 && player.IsSelected(recipe.Profession))
            {
                min = Math.Min(min, recipe.MaterialQuantity);
            }
        }

        return min == int.MaxValue ? 2 : min;
    }

    /// <summary>A tier-1 recipe for a selected profession whose baseline material is <paramref name="materialKey"/>.</summary>
    private static Recipe? CheapestTier1Recipe(PlayerState player, string materialKey) =>
        ProfessionRegistry.AllRecipes.Values
            .Where(r => r.Tier == 1 && player.IsSelected(r.Profession) && r.MaterialKey == materialKey)
            .OrderBy(r => r.MaterialQuantity)
            .FirstOrDefault();
}
