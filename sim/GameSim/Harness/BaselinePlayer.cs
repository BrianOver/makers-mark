using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Harness;

/// <summary>
/// The scripted baseline player policy (U10; moved from the Balance tests for the telemetry
/// batch runner — one policy, shared by the balance gate and the CLI batch farm, never forked).
/// Craft the best recipe the kernel would accept (asked via <see cref="Advisor.ActionLegality"/>,
/// never re-derived here), price at the rival's own formula (better stats win value ties), buy
/// every affordable ore offer, unlock talents in prerequisite order.
/// Deterministic — no RNG of its own, no IO, no wall clock: purity-safe inside GameSim.
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
                // G3: a craft spends an action slot, so skip if the day's budget is already spent
                // (an over-budget craft would only be rejected — leaving state unchanged — anyway).
                if (state.ActionSlotsRemaining <= 0)
                {
                    break;
                }

                // Ask ActionLegality, never re-derive the rule. This used to hand-roll
                // `have >= recipe.MaterialQuantity`, which missed the material-efficiency talent
                // discount (ActionLegality.CraftLegal: needed = Max(1, quantity - efficiency)) —
                // the scripted smith refused ~90% of crafts the kernel would have accepted, so
                // every balance number described a smith who wouldn't work. It also ignored the
                // tier gate: the old check could emit a doomed craft for a tier-locked recipe
                // (rejected, no craft that window) instead of walking down to a legal one.
                foreach (var recipe in RecipeTable.All.Values
                             .OrderByDescending(r => r.Tier)
                             .ThenByDescending(r => r.BaseStats.Attack + r.BaseStats.Defense))
                {
                    var candidate = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
                    if (ActionLegality.IsLegal(state, candidate, state.Phase))
                    {
                        actions.Add(candidate);
                        break; // one craft per window keeps the policy simple and stable
                    }
                }

                break;

            case DayPhase.Camp:
            case DayPhase.ExpeditionDeep:
                // D5: the baseline holds at the staged ticks — no camp verbs, no deep actions. The
                // balance gate keeps measuring the SAME policy across the day's raid phases (bands
                // must not move from the empty player-action window). The kill-risk-1 send/never-send
                // A/B lives in a test-local scripted policy (U4), never here — BaselinePlayer is
                // never forked.
                //
                // 2026-08-02 loop-legibility widening (KTD-D(1)): these two phases (and Expedition)
                // are no longer walked AT ALL on a day nobody raids — GameKernel.Advance collapses
                // Morning straight to Evening when the roster has no alive hero to form a party. The
                // BaselinePlayer's real-campaign roster always has heroes once installed (U10's
                // starting six + recruit trickle), so this branch is still reached on every ordinary
                // balance-gate day; it just no longer describes the every-day case as "two empty
                // ticks" the way the harness comment used to — a day with truly nobody to send is
                // now a Morning->Evening fold, not a walk through these two phases with zero actions.
                break;

            case DayPhase.Evening:
                // Buy every ore offer the purse can afford, in offer order — but only while the
                // day still has action slots (G3): each buy spends one, so the baseline now stops
                // at the budget instead of emitting doomed, would-be-rejected buys. Rejected buys
                // never mutated state, so the 100-day balance bands are byte-identical either way;
                // this just keeps the ActionLog clean (no RejectedAction spam).
                var gold = state.Player.Gold;
                var slots = state.ActionSlotsRemaining;
                foreach (var offer in state.OpenOreOffers)
                {
                    if (slots <= 0)
                    {
                        break;
                    }

                    var cost = offer.Quantity * offer.UnitPrice;
                    if (cost <= gold)
                    {
                        actions.Add(new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity));
                        gold -= cost;
                        slots--;
                    }
                }

                break;
        }

        return actions.ToImmutable();
    }
}
