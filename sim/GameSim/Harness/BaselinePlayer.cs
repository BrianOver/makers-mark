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
/// affordable ore that some unlocked recipe can actually spend, accept every open gear commission,
/// unlock talents in prerequisite order, and (U-T1-11) climb the Forge Tier ladder itself the
/// moment gold and ore both allow it.
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
                // U-T1-11: buy the next Forge Tier the moment it's actually legal (gold + the
                // floor's own ore + an action slot — ForgeTierHandlers/ActionLegality.UpgradeForgeLegal
                // own every threshold; this only asks, never re-derives). Checked FIRST in Morning:
                // it is the day's highest-leverage action whenever it's legal (unlocking an entire
                // Forge Tier's worth of recipes, not one talent node), so it should win any tie for
                // the day's action slot. Register #157 (PR #549) puts UnlockTalentAction on the
                // slot-consuming list too, so the two DO compete for the same slot once both are
                // legal on the same day — checking the tier purchase first is what makes it win that
                // competition: a tier purchase opens a whole tier of recipes and is legal only on the
                // handful of days gold and ore line up, while a talent unlock is available almost
                // every morning and simply happens a day later. Displacing the rarer, larger move for
                // the common one would be the wrong way round.
                var upgrade = new UpgradeForgeAction();
                if (ActionLegality.IsLegal(state, upgrade, state.Phase))
                {
                    actions.Add(upgrade);
                }

                // Unlock one talent per morning, prereq order. U-T1-9: the two smithing-tier gate
                // nodes also require a matching Forge Tier, so (mirroring the Expedition craft loop's
                // own "walk down to a legal one" fix below) ask ActionLegality first and skip a
                // prereq-eligible-but-Forge-Tier-locked candidate rather than emit the same doomed
                // unlock every morning forever.
                //
                // U-T1-9's own note here — that this scripted policy never submits
                // UpgradeForgeAction and its peak gold never approaches the 400g Forge Tier II cost
                // — was true when written and is now false: the block directly above submits it, and
                // (per U-T1-11's re-baseline measurement) all 11 balance seeds standalone, 9 of 11
                // composed with this change, reach Forge Tier II. Deleted rather than corrected in
                // place, per CLAUDE.md rule 8.
                var smithTalents = state.Player.TalentsFor(ProfessionRegistry.BlacksmithId);
                var next = TalentTree.Nodes.Values
                    .Where(n => !smithTalents.Contains(n.NodeId)
                                && n.Prerequisites.All(smithTalents.Contains))
                    .OrderBy(n => n.NodeId, StringComparer.Ordinal)
                    .Select(n => new UnlockTalentAction(n.NodeId, ProfessionRegistry.BlacksmithId))
                    .FirstOrDefault(candidate => ActionLegality.IsLegal(state, candidate, state.Phase));
                if (next is not null)
                {
                    actions.Add(next);
                }

                // U-T1-11 re-baseline: accept every open GEAR commission. Free (AcceptCommissionAction
                // spends no slot per ActionBudget) and, unlike the ordinary shelf, pays a guaranteed
                // PREMIUM over list once fulfilled — the "commission" channel CLAUDE.md names as one
                // of the four honest ways a craft reaches a hero, and one this policy never touched
                // before. Measured: composed-world seeds reach Forge Tier II by day 13-20 instead of
                // day 25-60 once this fires. Accepting blindly was the first thing to ever actually
                // exercise two pre-existing gaps in Heroes/CommissionSystem.cs and
                // Heroes/CommissionHandlers.cs, both fixed in this same PR (found and fixed, not
                // found and worked around): CommissionSystem.FindGapSlot's WornGap check read an
                // always-null Shield slot as "empty" for a class that can never equip one
                // (AllowsShield: false), posting an uncompletable Shield commission; and
                // CommissionHandlers.TryFulfillFromShelf matched a shelf item to a commission on
                // Slot+MinQuality only, with no role-fit or weight-cap check — both now mirror
                // ShoppingAi.EvaluateItem's own "can this hero physically use it" gates.
                //
                // Consumable-slot commissions are still deliberately EXCLUDED, and that one stays a
                // scope choice rather than a bug fix: CommissionSystem.FindGapSlot posts one for ANY
                // hero with an empty pack, regardless of trait, and accepting it would fulfil through
                // the same guaranteed-sale path a RECKLESS hero's trait fiction is "never restocks"
                // (TraitEffects.ConsumableStockTargetFor returns 0 for them by design) — overriding a
                // hero's own stocking trait is a different question from "can they wear it," and
                // ordinary consumable provisioning already has its own trait-respecting path
                // (HasBuyer/ShopConsumableOnce). The commission channel adds gear income here; it
                // does not silently overrule a hero's stocking trait.
                foreach (var commission in state.Commissions.Where(c => !c.Accepted && c.Slot != ItemSlot.Consumable))
                {
                    var accept = new AcceptCommissionAction(commission.Hero);
                    if (ActionLegality.IsLegal(state, accept, state.Phase))
                    {
                        actions.Add(accept);
                    }
                }

                // Stock every unshelved player craft at the rival's price formula.
                var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
                var equipped = state.Heroes.Values
                    .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor, h.Gear.Trinket })
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
                    // U-T1-11 re-baseline: a consumable's ItemStats are ALWAYS zero (Attack/Defense/
                    // Weight — it carries no gear score, by design, see RecipeTable), so the gear
                    // formula priced every field-salve at Math.Max(1, 0*2) = 1g, forever — measured:
                    // once gear demand across the roster saturates at the tier-1 ceiling and HasBuyer
                    // falls through to the always-has-a-buyer consumable, this 1g asking price capped
                    // total revenue far below what the item could actually fetch, even though
                    // ShoppingAi.EvaluateConsumable never compares price to value at all — only
                    // affordability — so real gold was being left on the table on every single sale.
                    // Priced on the heal Magnitude instead, mirroring the gear formula's own shape
                    // (double the "stat" that stands in for value).
                    var value = item.Effect is { } effect ? effect.Magnitude : item.Stats.Attack + item.Stats.Defense;
                    actions.Add(new StockAction(item.Id, Math.Max(1, value * 2)));
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
                //
                // U-T1-11: two adjustments once the forge ladder's next rung is genuinely in
                // reach, both gated on already holding the lock-and-key ore
                // (ForgeTierHandlers.OreQuantity of OreKey[tierIndex]) — never before, so neither
                // can fire while the tier is still purely aspirational:
                //
                // 1. Stop re-buying MORE of that specific ore once we already hold enough. Measured
                //    on the main balance seed pre-fix: 150 copper banked by day 30 against a 25-unit
                //    need — this policy's craft loop moves on to the next tier's material almost
                //    immediately, so anything past the threshold was pure dead stock, gold that
                //    could bank toward the tier instead.
                // 2. Reserve gold toward the tier's cost — but never so much that ore-buying goes
                //    to zero, and never so LITTLE that a balance that already cleared the cost gets
                //    spent back down below it before Morning gets a turn (UpgradeForgeAction is
                //    Morning-only; an Evening that spends a just-crossed threshold back to zero
                //    would make the crossing invisible). Below the cost, protect at most HALF of
                //    tonight's gold — the other half still buys ore every evening, so a still-thin
                //    economy is never starved to zero the way a prior full-cost-from-day-one
                //    reservation attempt was (reverted, #549's body, against an economy with no ore
                //    banked yet). At or above the cost, protect the FULL cost and spend only the
                //    surplus — the purchase is real now, and it must survive to Morning intact.
                //
                // Every OTHER material keeps buying at full budget throughout — a still-needed ore
                // (iron/steel feeding live tier-2/3 crafts) is never touched by either adjustment.
                //
                // U-T1-11 re-baseline: a third guard, orthogonal to the two above. Nothing stops the
                // craft loop's own recipe tier from being LOCKED for reasons that have nothing to do
                // with the forge ladder (an ordinary talent-tier gate not yet unlocked, or material
                // availability for the Gloomwood/Emberfall rungs) — buying ore for a tier this smith
                // cannot legally craft yet is dead stock exactly like the ladder-ore case above, just
                // for a different reason. `usableMaterials` mirrors ActionLegality.CraftLegal's own
                // tier-gate check (never re-deriving the quantity/slot halves) so a material only gets
                // bought when some already-unlocked recipe could actually spend it, or when it is the
                // ladder's own lock-and-key ore (which has a real use — buying the tier itself — even
                // before any recipe needs it).
                var smithProfession = ProfessionRegistry.TryGet(ProfessionRegistry.BlacksmithId, out var bp) ? bp : null;
                var eveningTalents = state.Player.TalentsFor(ProfessionRegistry.BlacksmithId);
                var usableMaterials = RecipeTable.All.Values
                    .Where(r => smithProfession is null
                                || !smithProfession.TierGate.TryGetValue(r.Tier, out var gate)
                                || eveningTalents.Contains(gate))
                    .Select(r => r.MaterialKey)
                    .ToHashSet();

                var tierIndex = Economy.ForgeTierHandlers.CurrentTierIndex(state.Player);
                var ladderOreKey = tierIndex <= Economy.ForgeTierHandlers.MaxUpgradeIndex
                    ? Economy.ForgeTierHandlers.OreKey[tierIndex]
                    : null;
                var gold = state.Player.Gold;
                var slots = state.ActionSlotsRemaining;
                var materials = state.Player.Materials;
                var ladderOreBanked = ladderOreKey is not null
                    && materials.TryGetValue(ladderOreKey, out var lockedOre)
                    && lockedOre >= Economy.ForgeTierHandlers.OreQuantity;

                if (ladderOreBanked)
                {
                    var cost = Economy.ForgeTierHandlers.GoldCost[tierIndex];
                    var protect = gold >= cost ? cost : Math.Min(cost - gold, gold / 2);
                    gold -= protect;
                }

                foreach (var offer in state.OpenOreOffers)
                {
                    if (slots <= 0)
                    {
                        break;
                    }

                    var isLadderOre = offer.MaterialKey == ladderOreKey;
                    if (isLadderOre && ladderOreBanked)
                    {
                        continue; // already banked enough of THIS key for the pending upgrade
                    }

                    if (!isLadderOre && !usableMaterials.Contains(offer.MaterialKey))
                    {
                        continue; // no unlocked recipe can spend this, and it isn't the ladder's own key
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
