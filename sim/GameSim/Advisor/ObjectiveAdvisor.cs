using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
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
///
/// <para><b>U8 (plan 2026-07-25-001) staleness fix.</b> <see cref="Suggest"/> is a PURE, memoryless
/// projection — it holds no "standing suggestion" of its own — so a stale premise (a sold listing, a
/// resolved/orphaned bounty, an accepted/expired commission, an honored memorial) invalidates itself
/// automatically the next time the CALLER re-suggests: the read simply no longer matches. The actual
/// audit defect (FR-4, docs/design/2026-07-25-core-interaction-audit.md: "one suggestion verbatim for
/// 15+ days, T4") was never about missing invalidation — it was that the SAME low-priority fallback
/// (buy-material/craft) always won the race regardless of what the town actually needed. U8 fixes
/// that by giving two higher-priority, demand-aware suggestions first crack every call: (0) an
/// un-honored memorial on the FIRST Evening the farewell rite is legal — exactly once per memorial
/// (P2-MEMORY-04), the thin death-adjacent bridge to
/// Phase A's Legend Engine; (1) <see cref="DemandBoard.Snapshot"/>'s current top demand — an open
/// commission (a guaranteed sale, locked in by accepting) or a depth-stalled hero's blocking gear slot
/// (craft/buy toward it). Only when NEITHER fires does the original cheapest-productive-path fallback
/// (unchanged) and the trailing "shelve it" suggestion run.</para>
/// </summary>
public static class ObjectiveAdvisor
{
    public static ImmutableList<Suggestion> Suggest(GameState state)
    {
        var suggestions = ImmutableList.CreateBuilder<Suggestion>();
        var phase = state.Phase;

        // 0. Death-adjacent bridge (U8), narrowed to fire ONCE per memorial (P2-MEMORY-04): the
        //    rite is suggested on the FIRST Evening it is legal — the memorial is raised during the
        //    death-revealing Evening's system pass, so the first Evening a caller can act on it is
        //    Day + 1 — and never again. The prior read (first un-honored memorial, every Evening,
        //    forever) re-presented a permanent fact nightly as if it were news, measured at 1,287
        //    fires in one campaign. Thereafter an un-honored memorial is a fact the wall carries,
        //    not a prompt anyone repeats: the ledger is news at the threshold of memory. Stateless
        //    and deterministic — the predicate is a pure read of Memorial.Day, no new state. The
        //    one line it keeps names the cost of skipping (the rite keeps; nothing is lost but the
        //    saying of it). Once honored, the memorial drops out of this read (Honored filter).
        if (phase == DayPhase.Evening)
        {
            var memorial = state.Drama.Memorials.FirstOrDefault(m => !m.Honored && state.Day == m.Day + 1);
            if (memorial is not null)
            {
                var honor = new HonorMemorialAction(memorial.Hero);
                if (ActionLegality.IsLegal(state, honor, phase))
                {
                    suggestions.Add(new Suggestion(honor,
                        $"Honor {memorial.HeroName}'s memorial — the rite keeps, and it will wait as long as you do."));
                }
            }
        }

        // 1. Demand-driven (U8): read the SAME snapshot the CLI/Godot demand surfaces read
        //    (DemandBoard, U4) and answer the current top demand instead of a frozen default.
        //    Commissions first — accepting locks in a guaranteed future sale, the strongest signal
        //    the town gives — then a depth-stalled hero's blocking gear slot.
        var demand = DemandBoard.Snapshot(state);

        if (phase == DayPhase.Morning && demand.OpenCommissions.Count > 0)
        {
            var commission = demand.OpenCommissions[0];
            var accept = new AcceptCommissionAction(commission.Hero);
            if (ActionLegality.IsLegal(state, accept, phase))
            {
                suggestions.Add(new Suggestion(accept,
                    $"Accept {commission.HeroName}'s commission — {commission.Slot} at {commission.MinQuality}+ quality " +
                    $"for a {commission.PremiumGold}g premium (due day {commission.DeadlineDay}){GameSim.Heroes.CommissionSystem.SlotHonestyNote(commission.Slot)}."));
            }
        }

        // U10 (plan 2026-07-25-001, Slice 3 addendum): the TOP depth stall, whichever shape it is —
        // an empty <see cref="DepthStallEntry.BlockingSlot"/> (handled since U8) or a filled-but-
        // under-quality gate (BlockingSlot null, RequiredQuality > CarriedQuality — previously
        // skipped entirely, the fable-flagged "call without response" gap: "floor 3 wants Fine+" was
        // named but nothing guided PRODUCING it). Picking FirstOrDefault() unfiltered (not "first
        // SLOT stall") is the fix: the top demand answers whichever kind it actually is, instead of
        // silently hunting past a quality stall for a slot stall further down the list.
        // The QUALITY-gated depth stall is the progression blocker a Common+ commission never solves
        // (accepting Torvald's Common+ Shield does not lift a Fine+ floor-3 wall), so surface its
        // upgrade path even when a commission was already suggested — the two are different-horizon
        // goals (near-term premium vs breaking the depth ceiling). Gating this behind
        // "suggestions.Count == 0" masked U10 entirely in practice, since a commission is almost
        // always open early-game (fable Slice-3 playtest). Deduped so it never repeats the commission.
        // Scan for the first QUALITY-gated stall specifically (not just the top stall) — a slot
        // stall ahead of it in the list must not hide it, since they call for different answers.
        var qualityStall = demand.DepthStalls.FirstOrDefault(s => s.BlockingSlot is null);
        if (qualityStall is not null)
        {
            var upgrade = SuggestQualityUpgrade(state, qualityStall, phase);
            if (upgrade is not null && suggestions.All(s => !Equals(s.Action, upgrade.Action)))
            {
                suggestions.Add(upgrade);
            }
        }

        // The SLOT-gated stall stays a fallback: a slot commission usually IS that same slot need,
        // so it only fires when nothing sharper did.
        if (suggestions.Count == 0)
        {
            var slotStall = demand.DepthStalls.FirstOrDefault(s => s.BlockingSlot is not null);
            if (slotStall is not null)
            {
                var slotSuggestion = SuggestSlotCraftOrBuy(state, slotStall, phase);
                if (slotSuggestion is not null)
                {
                    suggestions.Add(slotSuggestion);
                }
            }
        }

        // 2. Fallback (unchanged from pre-U8): the cheapest-productive-path loop — still the
        //    tightest loop when no sharper demand signal exists (fresh saves, no live commission or
        //    stall yet).
        if (suggestions.Count == 0)
        {
            var (materialKey, quantity, cost) = CheapestProductivePath(state.Player);

            if (materialKey is not null && cost == 0)
            {
                // Craft is reachable RIGHT NOW (already enough of the cheapest-path material).
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
                // Otherwise, if buying the cheapest-path material at the Morning vendor is
                // affordable, suggest that first (Playable Core's tutorial-shaped first step: buy
                // material before craft is possible on a fresh save).
                var buy = new BuyMaterialAction(materialKey, quantity);
                if (ActionLegality.IsLegal(state, buy, phase))
                {
                    suggestions.Add(new Suggestion(buy, $"Buy {quantity} {materialKey} ({cost}g) — the cheapest path to your next craft."));
                }
                else
                {
                    // True destitution dead-end (R5): the cheapest path is unaffordable and the
                    // floor's three conditions all hold. No legal action moves the player forward
                    // this Morning — DestitutionRecoverySystem tops the purse up to this SAME
                    // material's cost automatically before Expedition. Name it, but propose no
                    // illegal action.
                    suggestions.Add(new Suggestion(null,
                        $"Not enough gold for {materialKey} yet ({cost}g needed) — the town's recovery stipend will cover it this morning."));
                }
            }
        }

        // U11 (plan 2026-07-25-001, Slice 3 addendum): fulfillment guidance — a shelved or held
        // player item may already ANSWER the top commission/stall's need (right slot, quality at or
        // above the bar). That is worth surfacing regardless of which suggestion won above (it is
        // information about existing inventory, not a competing directive), so it is appended
        // unconditionally rather than gated behind `suggestions.Count == 0`.
        var fulfillment = SuggestFulfillmentMatch(state, demand, phase);
        if (fulfillment is not null)
        {
            suggestions.Add(fulfillment);
        }

        // 3. Stock any unshelved player craft — always legal once one exists (unchanged, always
        //    appended after whatever won above).
        var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        var equipped = state.Heroes.Values
            .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor, h.Gear.Trinket })
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToHashSet();
        var stockable = state.Items.Values.FirstOrDefault(i =>
            i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value));
        if (stockable is not null)
        {
            var price = SuggestedPrice.For(stockable);
            var stock = new StockAction(stockable.Id, price);
            if (ActionLegality.IsLegal(state, stock, phase))
            {
                suggestions.Add(new Suggestion(stock, $"Shelve '{stockable.Name}' — it's finished and unsold."));
            }
        }

        return suggestions.ToImmutable();
    }

    /// <summary>U8 demand answer for a depth-stalled hero's blocking gear slot
    /// (<see cref="DepthStallEntry.BlockingSlot"/>): the lowest-tier recipe for that slot among the
    /// player's selected professions — craft it now if the material is already in stock, else buy
    /// toward it (Morning only; <see cref="BuyMaterialAction"/> is Morning-gated). Returns null when
    /// no recipe exists for the slot under a selected profession, or when neither action is legal
    /// right now (e.g. it's not Morning and the material still falls short) — the caller falls
    /// through to the unchanged fallback rather than propose nothing at all.</summary>
    private static Suggestion? SuggestSlotCraftOrBuy(GameState state, DepthStallEntry stall, DayPhase phase)
    {
        if (stall.BlockingSlot is not { } slot)
        {
            return null;
        }

        var recipe = ProfessionRegistry.AllRecipes.Values
            .Where(r => r.Slot == slot && state.Player.IsSelected(r.Profession))
            .OrderBy(r => r.Tier)
            .ThenBy(r => r.MaterialQuantity)
            .FirstOrDefault();
        if (recipe is null)
        {
            return null;
        }

        var have = state.Player.Materials.TryGetValue(recipe.MaterialKey, out var stock) ? stock : 0;
        if (have >= recipe.MaterialQuantity)
        {
            var craft = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
            return ActionLegality.IsLegal(state, craft, phase)
                ? new Suggestion(craft,
                    $"{stall.HeroName} is stalled at {DepthCopy.Deepest(stall.DeepestFloorReached)}, aiming for floor {stall.TargetFloor}, missing {slot} gear " +
                    $"— craft '{recipe.Name}' now, you already have enough {recipe.MaterialKey}.")
                : null;
        }

        if (phase == DayPhase.Morning)
        {
            var buy = new BuyMaterialAction(recipe.MaterialKey, recipe.MaterialQuantity);
            if (ActionLegality.IsLegal(state, buy, phase))
            {
                var cost = MaterialVendorHandlers.QuoteCost(recipe.MaterialKey, recipe.MaterialQuantity);
                return new Suggestion(buy,
                    $"{stall.HeroName} is stalled at {DepthCopy.Deepest(stall.DeepestFloorReached)}, aiming for floor {stall.TargetFloor}, missing {slot} gear " +
                    $"— buy {recipe.MaterialQuantity} {recipe.MaterialKey} ({cost}g) toward '{recipe.Name}'.");
            }
        }

        return null;
    }

    /// <summary>U10 (plan 2026-07-25-001, Slice 3 addendum) demand answer for a depth-stalled
    /// hero's QUALITY gate (<see cref="DepthStallEntry.BlockingSlot"/> null, <see
    /// cref="DepthStallEntry.RequiredQuality"/> above <see cref="DepthStallEntry.CarriedQuality"/>):
    /// every Weapon/Shield/Armor slot is filled, but the worn gear is under-quality for the next
    /// floor. Names the sub-par slot (<see cref="SubParSlot"/> mirrors <c>CommissionSystem</c>'s own
    /// private gap-scan), then walks that slot's recipes tier-ascending for the selected profession:
    /// the first one whose <see cref="ProfessionDefinition.TierGate"/> talent ISN'T unlocked yet is
    /// "the better item" — unlocking that gate is the direct next step (a locked tier can't be
    /// crafted at all, so no material purchase would help yet). Once every tier is already unlocked,
    /// the gate is no longer the blocker: suggest (re)crafting the slot's HIGHEST-tier recipe with
    /// its own better baseline material, buying it first if not in stock (Morning only) — mirrors
    /// <see cref="SuggestSlotCraftOrBuy"/>'s craft-now/buy-toward-it shape exactly. Returns null when
    /// no recipe exists for the slot under a selected profession, the hero can't be resolved, no
    /// worn item is actually sub-par (defensive — <see cref="DemandBoard"/>'s own
    /// RequiredQuality &gt; CarriedQuality check should already guarantee one exists), or neither
    /// action is legal right now — the caller falls through to the unchanged fallback rather than
    /// propose nothing at all.</summary>
    private static Suggestion? SuggestQualityUpgrade(GameState state, DepthStallEntry stall, DayPhase phase)
    {
        if (stall.BlockingSlot is not null
            || stall.RequiredQuality is not { } required
            || stall.CarriedQuality is not { } carried
            || required <= carried)
        {
            return null;
        }

        if (!state.Heroes.TryGetValue(stall.Hero.Value, out var hero))
        {
            return null;
        }

        if (SubParSlot(hero.Gear, state.Items, required) is not { } targetSlot)
        {
            return null;
        }

        var recipes = ProfessionRegistry.AllRecipes.Values
            .Where(r => r.Slot == targetSlot && state.Player.IsSelected(r.Profession))
            .OrderBy(r => r.Tier)
            .ThenBy(r => r.MaterialQuantity)
            .ToList();
        if (recipes.Count == 0)
        {
            return null;
        }

        var nextFloor = stall.DeepestFloorReached + 1;

        // The lowest-tier recipe whose talent gate isn't unlocked yet — a locked tier can't be
        // crafted at all (CraftLegal's own tier-gate guard), so unlocking it is the unambiguous next
        // step, ahead of any material purchase.
        foreach (var recipe in recipes)
        {
            if (!ProfessionRegistry.TryGet(recipe.Profession, out var profession))
            {
                continue;
            }

            if (!profession!.TierGate.TryGetValue(recipe.Tier, out var gate)
                || state.Player.TalentsFor(recipe.Profession).Contains(gate))
            {
                continue;
            }

            var unlock = new UnlockTalentAction(gate, recipe.Profession);
            return ActionLegality.IsLegal(state, unlock, phase)
                ? new Suggestion(unlock,
                    $"{stall.HeroName} carries {targetSlot} gear below floor {nextFloor}'s {required}+ bar (currently {carried}) " +
                    $"— unlock '{profession.TalentNodes[gate].Name}' to open the way to '{recipe.Name}'.")
                : null;
        }

        // Every tier is already unlocked — the gate isn't the blocker. Craft (or buy toward) the
        // slot's best recipe, its own better material raising the quality ceiling.
        var best = recipes[^1];
        var have = state.Player.Materials.TryGetValue(best.MaterialKey, out var stock) ? stock : 0;
        if (have >= best.MaterialQuantity)
        {
            var craft = new CraftAction(best.RecipeId, best.MaterialKey);
            return ActionLegality.IsLegal(state, craft, phase)
                ? new Suggestion(craft,
                    $"{stall.HeroName} carries {targetSlot} gear below floor {nextFloor}'s {required}+ bar (currently {carried}) " +
                    $"— craft '{best.Name}' now, you already have enough {best.MaterialKey}.")
                : null;
        }

        if (phase == DayPhase.Morning)
        {
            var buy = new BuyMaterialAction(best.MaterialKey, best.MaterialQuantity);
            if (ActionLegality.IsLegal(state, buy, phase))
            {
                var cost = MaterialVendorHandlers.QuoteCost(best.MaterialKey, best.MaterialQuantity);
                return new Suggestion(buy,
                    $"{stall.HeroName} carries {targetSlot} gear below floor {nextFloor}'s {required}+ bar (currently {carried}) " +
                    $"— buy {best.MaterialQuantity} {best.MaterialKey} ({cost}g) toward '{best.Name}'.");
            }
        }

        return null;
    }

    /// <summary>
    /// <b>DELIBERATELY NARROWER than <c>CommissionSystem.FindGapSlot</c> — do not "fix" the drift.</b>
    /// This scan used to mirror it, and that claim is no longer true: commissions now also ask for
    /// Consumable and Trinket, while this stays worn Weapon/Shield/Armor only. The two answer
    /// different questions, and conflating them would give bad advice.
    ///
    /// <para>This one serves <see cref="SuggestQualityUpgrade"/>: what is blocking a hero from going
    /// DEEPER. Depth is gated by worn gear power — a potion does not raise a hero's depth ceiling and a
    /// trinket is an augment, so neither belongs in a stall diagnosis. <c>FindGapSlot</c> answers the
    /// separate question of what a hero will ASK the smith for, where supplies and favours are
    /// legitimately part of the ask.</para>
    ///
    /// <para>Returns the first worn Weapon/Shield/Armor slot whose item quality falls below
    /// <paramref name="bar"/>, in the same fixed order <see cref="RaidForecast.MissingItemSlots"/> and
    /// <see cref="DemandBoard"/> use. A defensively-null worn slot also counts as sub-par (never
    /// thrown) — it shouldn't occur here since <see cref="SuggestQualityUpgrade"/> only calls this
    /// once <see cref="DepthStallEntry.BlockingSlot"/> is confirmed null (every slot filled).</para>
    /// </summary>
    private static ItemSlot? SubParSlot(GearSet gear, ImmutableSortedDictionary<int, Item> items, QualityGrade bar)
    {
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            var worn = gear.Slot(slot);
            if (worn is not { } id || !items.TryGetValue(id.Value, out var item) || item.Quality < bar)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>U11 (plan 2026-07-25-001, Slice 3 addendum) fulfillment guidance: does a shelved or
    /// held (crafted, unshelved, unequipped) player item already ANSWER the top demand — the right
    /// slot at or above the required quality? Reads the SAME top-demand target U10/U8 answer (the
    /// top open commission if one exists, else the top depth stall's needed slot/quality — an empty
    /// <see cref="DepthStallEntry.BlockingSlot"/> accepts ANY quality, a quality stall needs <see
    /// cref="SubParSlot"/>'s bar). A SHELVED match names the item and — R-real, KTD9 spirit —
    /// flags a PURSE MISMATCH when the target hero's gold falls short of the asking price (the sale
    /// can't close as priced); the caller cannot fix that discrepancy from this seam, so the action
    /// is null (informational, like the destitution message above). A HELD (unshelved) match instead
    /// proposes the concrete next step: shelve it. Returns null when there is no live commission or
    /// stall to answer, the target hero can't be resolved, or no held/shelved item matches.</summary>
    private static Suggestion? SuggestFulfillmentMatch(GameState state, DemandSnapshot demand, DayPhase phase)
    {
        ItemSlot slot;
        QualityGrade minQuality;
        HeroId targetHero;
        string heroName;
        string demandLabel;

        if (demand.OpenCommissions.Count > 0)
        {
            var commission = demand.OpenCommissions[0];
            slot = commission.Slot;
            minQuality = commission.MinQuality;
            targetHero = commission.Hero;
            heroName = commission.HeroName;
            demandLabel = $"{commission.HeroName}'s {slot} commission";
        }
        else if (demand.DepthStalls.FirstOrDefault() is { } stall)
        {
            if (stall.BlockingSlot is { } blocking)
            {
                slot = blocking;
                minQuality = QualityGrade.Poor; // an empty slot — anything crafted answers it
            }
            else if (stall.RequiredQuality is { } required && stall.CarriedQuality is { } carried && required > carried)
            {
                if (!state.Heroes.TryGetValue(stall.Hero.Value, out var stalledHero)
                    || SubParSlot(stalledHero.Gear, state.Items, required) is not { } subParSlot)
                {
                    return null;
                }

                slot = subParSlot;
                minQuality = required;
            }
            else
            {
                return null;
            }

            targetHero = stall.Hero;
            heroName = stall.HeroName;
            demandLabel = $"{stall.HeroName}'s stall";
        }
        else
        {
            return null;
        }

        if (!state.Heroes.TryGetValue(targetHero.Value, out var hero))
        {
            return null;
        }

        // Shelved first: it is already for sale, so a purse mismatch is worth flagging.
        foreach (var entry in state.Player.Shelf)
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item) || item.Slot != slot || item.Quality < minQuality)
            {
                continue;
            }

            return hero.Gold < entry.Price
                ? new Suggestion(null,
                    $"You have a {item.Quality} {item.Name} shelved — {demandLabel} wants it, but {heroName} " +
                    $"only carries {hero.Gold}g against the {entry.Price}g asking price — the sale can't close as priced.")
                : new Suggestion(null, $"You have a {item.Quality} {item.Name} shelved — {demandLabel} wants it.");
        }

        // Held: finished, unshelved, unequipped — propose shelving it (the concrete next step).
        var shelvedIds = state.Player.Shelf.Select(e => e.Item.Value).ToHashSet();
        var equippedIds = state.Heroes.Values
            .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor, h.Gear.Trinket })
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToHashSet();
        var held = state.Items.Values.FirstOrDefault(i =>
            i.PlayerCrafted && i.Slot == slot && i.Quality >= minQuality
            && !shelvedIds.Contains(i.Id.Value) && !equippedIds.Contains(i.Id.Value));
        if (held is null)
        {
            return null;
        }

        var price = SuggestedPrice.For(held);
        var stock = new StockAction(held.Id, price);
        return ActionLegality.IsLegal(state, stock, phase)
            ? new Suggestion(stock, $"You crafted a {held.Quality} {held.Name} — shelve it, {demandLabel} wants it.")
            : null;
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
