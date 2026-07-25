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
/// un-honored memorial once the farewell rite is legal (Evening) — the thin death-adjacent bridge to
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

        // 0. Death-adjacent bridge (U8): a memorial is only actionable once the rite is legal
        //    (Evening, FarewellHandlers) — a hero who dies THIS Evening is honorable starting the
        //    NEXT one. Once honored, the memorial drops out of this read (Honored filter), so this
        //    branch self-invalidates with zero extra state.
        if (phase == DayPhase.Evening)
        {
            var memorial = state.Drama.Memorials.FirstOrDefault(m => !m.Honored);
            if (memorial is not null)
            {
                var honor = new HonorMemorialAction(memorial.Hero);
                if (ActionLegality.IsLegal(state, honor, phase))
                {
                    suggestions.Add(new Suggestion(honor,
                        $"Honor {memorial.HeroName}'s memorial — their {memorial.GearNamed} still waits at the stone."));
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
                    $"for a {commission.PremiumGold}g premium (due day {commission.DeadlineDay})."));
            }
        }

        if (suggestions.Count == 0)
        {
            var stall = demand.DepthStalls.FirstOrDefault(s => s.BlockingSlot is not null);
            if (stall is not null)
            {
                var slotSuggestion = SuggestSlotCraftOrBuy(state, stall, phase);
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

        // 3. Stock any unshelved player craft — always legal once one exists (unchanged, always
        //    appended after whatever won above).
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
                    $"{stall.HeroName} is stalled at floor {stall.DeepestFloorReached}/{stall.TargetFloor} missing {slot} gear " +
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
                    $"{stall.HeroName} is stalled at floor {stall.DeepestFloorReached}/{stall.TargetFloor} missing {slot} gear " +
                    $"— buy {recipe.MaterialQuantity} {recipe.MaterialKey} ({cost}g) toward '{recipe.Name}'.");
            }
        }

        return null;
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
