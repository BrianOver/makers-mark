using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Materials;
using GameSim.Professions;

namespace GameSim.Harness;

/// <summary>
/// The scripted baseline player policy (U10; moved from the Balance tests for the telemetry
/// batch runner — one policy, shared by the balance gate and the CLI batch farm, never forked).
/// Craft the best recipe the kernel would accept (asked via <see cref="Advisor.ActionLegality"/>,
/// never re-derived here), price at the rival's own formula (better stats win value ties), buy
/// every affordable ore offer it can fit in the day's action budget — best grade first, since the
/// purse never covers them all — and unlock talents in prerequisite order.
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
                // Buy every ore offer the purse can afford — but only while the day still has
                // action slots (G3): each buy spends one, so the baseline stops at the budget
                // instead of emitting doomed, would-be-rejected buys.
                //
                // BEST ORE FIRST (2026-08-09). This loop used to walk `state.OpenOreOffers` in
                // its natural order, which is the order the reveal appended loot: floor 1 first,
                // then floor 2, and so on (`ExpeditionRevealSystem`, `ExpeditionResolver` banks
                // ore per cleared floor ascending). The purse cannot cover every offer — measured
                // median gold at the Evening tick is 2-4g — so "first affordable wins" silently
                // resolved to "ALWAYS BUY THE SHALLOWEST, WORST ORE ON THE LIST." Measured over
                // 100 days: the market offered 208 units of steel across 108 offers, 36 of them
                // affordable at the moment the player looked, and the scripted smith bought
                // exactly zero — peak steel held was 0, peak iron 4, while peak copper was 51.
                // 87% of its 84 crafts were the tier-1 shortsword, and not one tier-3 item existed
                // in any of 11 seeds despite every tier talent being unlocked by day 8.
                //
                // This is a harness defect of the SAME CLASS as the one #328 fixed in the craft
                // branch above (a hand-rolled rule that made the scripted smith refuse ~90% of
                // legal crafts, so every balance number described a smith who wouldn't work).
                // Sorting by material grade, then cheapest-first within a grade, is the minimal
                // correction
                // that matches this loop's own stated intent: it still buys only what it can
                // afford, still respects the slot budget, and still emits no rejected action.
                // It is NOT a competence upgrade — the policy makes no new KIND of decision.
                var gold = state.Player.Gold;
                var slots = state.ActionSlotsRemaining;
                var offers = state.OpenOreOffers
                    .OrderByDescending(o => MaterialRegistry.Grade(o.MaterialKey))
                    .ThenBy(o => o.Quantity * o.UnitPrice)
                    .ThenBy(o => o.MaterialKey, StringComparer.Ordinal)
                    .ThenBy(o => o.From.Value);
                foreach (var offer in offers)
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
