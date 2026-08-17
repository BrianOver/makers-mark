using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Heroes;
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
                // U-T1: a CONSUMABLE that has ever sold is gone for good (ShopHandlers 3b — it lives
                // in a hero's pack until drunk, then it's just gone). Neither "shelved" nor "equipped"
                // (gear slots only) ever catches that case, so pre-fix this re-offered the SAME sold
                // consumable id as a doomed StockAction every single morning for the rest of the
                // campaign (harmless to state — rejections never mutate — but pure ActionLog noise a
                // shopkeeper who remembers their own sales wouldn't generate). GEAR is deliberately
                // exempt: a sold weapon a hero later drops for an upgrade has no "already sold" rule
                // in ShopHandlers — it is genuinely second-hand stock, and re-shelving it for whichever
                // hero needs it next is real income this policy should keep (an early cut of this fix
                // blocked it and peak gold measurably dropped, 399g -> 286g, for exactly that reason).
                var soldConsumables = state.EventLog.OfType<ItemSold>()
                    .Select(e => e.Item.Value)
                    .Where(id => state.Items.TryGetValue(id, out var sold) && sold.Effect is not null)
                    .ToHashSet();
                foreach (var item in state.Items.Values.Where(i =>
                             i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value)
                             && (i.Effect is null || !soldConsumables.Contains(i.Id.Value))))
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
                // U-T1 ("the reference player learns to trade"): a plain shopkeeper doesn't keep
                // manufacturing stock nobody in town can use. Measured before this fix, on the main
                // balance seed: 98 items crafted in 100 days, but only 18 ever sold off the player
                // shelf (rival took another 40) — 82% of production was dead weight, because the old
                // rule crafted the single biggest thing it could afford every day regardless of
                // whether any hero had a gap for it. HasBuyer asks the same question a hero already
                // asks (ShoppingAi: is this a strict gear-score upgrade, or an empty pack for a
                // consumable) against the CURRENT alive roster, net of what the player's own shelf
                // already offers unsold — so this loop still prefers the best tier it can afford, but
                // skips a candidate with no real buyer instead of shelving another item nobody wants.
                foreach (var recipe in RecipeTable.All.Values
                             .OrderByDescending(r => r.Tier)
                             .ThenByDescending(r => r.BaseStats.Attack + r.BaseStats.Defense))
                {
                    var candidate = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
                    if (ActionLegality.IsLegal(state, candidate, state.Phase) && HasBuyer(state, recipe))
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

    /// <summary>
    /// U-T1: does <paramref name="recipe"/> have a real buyer RIGHT NOW? Estimated on
    /// <see cref="Recipe.BaseStats"/> (Common-grade — <see cref="Crafting.ItemForge"/> only scales
    /// stats UP from there) since the harness doesn't know the quality roll before the craft
    /// happens, same as a real smith wouldn't; auto-craft's <c>AutoCraftGrade</c> (550, jitter ±25)
    /// never reaches the Poor band, so Common is a safe, if slightly conservative, floor.
    ///
    /// Gear: true when some ALIVE, role/weight-compatible hero's current best option in this slot —
    /// worn gear OR whatever the player's own shelf already offers unsold — is weaker than this
    /// recipe would be. That "net of the shelf" check is what stops the loop from re-crafting a
    /// second copy of a slot the shelf already covers while the first copy is still waiting for a
    /// buyer (exactly the pileup the pre-fix measurement found: 105 stock attempts, 18 sales, 94/100
    /// days ending with a nonempty shelf).
    ///
    /// Consumables: true when some alive hero's <see cref="Hero.Pack"/> is below their stocking
    /// target AND the shelf doesn't already carry an unsold Heal item (a consumable that's ever
    /// sold never restocks — ShopHandlers 3b — so this is the only staleness check consumables need).
    /// </summary>
    private static bool HasBuyer(GameState state, Recipe recipe)
    {
        if (recipe.Effect is { Kind: ConsumableKind.Heal })
        {
            var alreadyShelved = state.Player.Shelf.Any(e =>
                state.Items.TryGetValue(e.Item.Value, out var shelved) && shelved.Effect is { Kind: ConsumableKind.Heal });
            return !alreadyShelved
                && state.Heroes.Values.Any(h => h.Alive && h.Pack.Count < TraitEffects.ConsumableStockTargetFor(h));
        }

        var estimated = recipe.BaseStats.Attack + recipe.BaseStats.Defense;

        foreach (var hero in state.Heroes.Values)
        {
            if (!hero.Alive)
            {
                continue;
            }

            var heroClass = ClassRegistry.Require(hero.ClassId);
            if (recipe.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
            {
                continue;
            }

            if (heroClass.MaxItemWeight is { } weightCap && recipe.BaseStats.Weight > weightCap)
            {
                continue;
            }

            var bestAvailable = hero.Gear.Slot(recipe.Slot) is { } wornId
                && state.Items.TryGetValue(wornId.Value, out var worn)
                ? worn.Stats.Attack + worn.Stats.Defense
                : 0;

            foreach (var entry in state.Player.Shelf)
            {
                if (!state.Items.TryGetValue(entry.Item.Value, out var shelved) || shelved.Slot != recipe.Slot)
                {
                    continue;
                }

                if (heroClass.MaxItemWeight is { } shelfWeightCap && shelved.Stats.Weight > shelfWeightCap)
                {
                    continue; // this hero couldn't wear the unsold copy either — doesn't cover them
                }

                bestAvailable = Math.Max(bestAvailable, shelved.Stats.Attack + shelved.Stats.Defense);
            }

            if (estimated > bestAvailable)
            {
                return true;
            }
        }

        return false;
    }
}
