using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Tests.Balance;

/// <summary>
/// The scripted baseline player policy (U10): craft the best recipe materials allow,
/// price at the rival's own formula (better stats win value ties), buy every affordable
/// ore offer, unlock talents in prerequisite order. Deterministic — no RNG of its own.
/// </summary>
public static class BaselinePlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        switch (state.Phase)
        {
            case DayPhase.Morning:
                // Unlock one affordable talent per morning, prereq order (they're free in v1).
                var smithTalents = state.Player.TalentsFor(ProfessionRegistry.BlacksmithId);
                var next = TalentTree.Nodes.Values
                    .Where(n => !smithTalents.Contains(n.NodeId)
                                && n.Prerequisites.All(smithTalents.Contains))
                    .OrderBy(n => n.NodeId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (next is not null)
                {
                    actions.Add(new UnlockTalentAction(next.NodeId, ProfessionRegistry.BlacksmithId));
                }

                // Stock every unshelved player craft at the rival's price formula.
                var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
                var equipped = state.Heroes.Values
                    .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor })
                    .Where(id => id is not null)
                    .Select(id => id!.Value.Value)
                    .ToHashSet();
                foreach (var item in state.Items.Values.Where(i =>
                             i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value)))
                {
                    var statSum = item.Stats.Attack + item.Stats.Defense;
                    actions.Add(new StockAction(item.Id, Math.Max(1, statSum * 2)));
                }

                break;

            case DayPhase.Expedition:
                // Craft while heroes are away: best affordable recipe by tier then stat sum.
                foreach (var recipe in RecipeTable.All.Values
                             .OrderByDescending(r => r.Tier)
                             .ThenByDescending(r => r.BaseStats.Attack + r.BaseStats.Defense))
                {
                    var have = state.Player.Materials.GetValueOrDefault(recipe.MaterialKey);
                    if (have >= recipe.MaterialQuantity)
                    {
                        actions.Add(new CraftAction(recipe.RecipeId, recipe.MaterialKey));
                        break; // one craft per window keeps the policy simple and stable
                    }
                }

                break;

            case DayPhase.Evening:
                // Buy every ore offer the purse can afford, in offer order.
                var gold = state.Player.Gold;
                foreach (var offer in state.OpenOreOffers)
                {
                    var cost = offer.Quantity * offer.UnitPrice;
                    if (cost <= gold)
                    {
                        actions.Add(new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity));
                        gold -= cost;
                    }
                }

                break;
        }

        return actions.ToImmutable();
    }
}
