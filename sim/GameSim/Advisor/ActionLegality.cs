using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Expedition;
using GameSim.Factions;
using GameSim.Kernel;
using GameSim.Materials;
using GameSim.Professions;

namespace GameSim.Advisor;

/// <summary>
/// Sim-side "what can I do" (plan 2026-07-19-002 U10, KTD9). <see cref="IActionHandler.CanHandle"/>
/// only checks action-type + phase — the REAL legality lives in each handler's <c>Apply</c>-level
/// <see cref="RejectedAction"/> guards, and <see cref="IActionHandler"/> is deny-listed
/// (<c>Contracts/</c>) so there is no shared Validate seam to call into. This module therefore
/// DELIBERATELY REPLICATES every guard from <see cref="Crafting.CraftingHandlers"/>,
/// <see cref="Economy.ShopHandlers"/>, <see cref="Economy.OreMarketHandlers"/>,
/// <see cref="Economy.MaterialVendorHandlers"/>, <see cref="Bounties.BountyHandlers"/>,
/// <see cref="Professions.ProfessionHandlers"/>, and <see cref="Expedition.CampHandlers"/> — a
/// second copy of the same rules, on purpose (KTD9: outside <c>Contracts/</c>, no kernel
/// registration, no RNG). The 100-day kernel-parity property test
/// (<c>ActionLegalityTests</c>) is the standing drift tripwire: any future handler change that
/// isn't mirrored here fails that test, never silently.
///
/// Pure projection over <see cref="GameState"/>: no mutation, no RNG, no wall clock, no
/// <c>Contracts/</c> edits.
/// </summary>
public static class ActionLegality
{
    /// <summary>
    /// Whether <paramref name="action"/> would be accepted (no <see cref="RejectedAction"/>) if
    /// submitted to the kernel right now, during <paramref name="phase"/>, against
    /// <paramref name="state"/>. Mirrors the exact Apply-level guard chain of the owning handler.
    /// </summary>
    public static bool IsLegal(GameState state, PlayerAction action, DayPhase phase) => action switch
    {
        CraftAction craft => CraftLegal(state, craft),
        StockAction stock => StockLegal(state, stock),
        SetPriceAction setPrice => SetPriceLegal(state, setPrice),
        UnstockAction unstock => UnstockLegal(state, unstock),
        BuyOreAction buyOre => phase == DayPhase.Evening && BuyOreLegal(state, buyOre),
        BuyMaterialAction buyMaterial => phase == DayPhase.Morning && BuyMaterialLegal(state, buyMaterial),
        PostBountyAction postBounty => (phase is DayPhase.Morning or DayPhase.Evening) && PostBountyLegal(state, postBounty),
        UnlockTalentAction unlock => UnlockTalentLegal(state, unlock),
        SetProfessionsAction setProfessions => SetProfessionsLegal(setProfessions),
        SendSupplyAction sendSupply => phase == DayPhase.Camp && SendSupplyLegal(state, sendSupply),
        RecallPartyAction recall => phase == DayPhase.Camp && RecallLegal(state, recall),
        _ => false,
    };

    /// <summary>
    /// A small set of CONCRETE legal actions available right now, one canonical instance per
    /// opportunity the current <see cref="GameState"/> offers (not every legal parameterization —
    /// e.g. one price per stockable item, not every possible price). Every entry is guaranteed
    /// legal by construction (each candidate is built and then re-checked through
    /// <see cref="IsLegal"/> before being included) — the kernel-parity test is the tripwire that
    /// keeps that guarantee true as handlers evolve.
    /// </summary>
    public static ImmutableList<PlayerAction> LegalActions(GameState state, DayPhase phase)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        // Craft: one candidate per recipe the player can afford in materials right now.
        foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
        {
            var candidate = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
            if (IsLegal(state, candidate, phase))
            {
                actions.Add(candidate);
            }
        }

        // Stock: one candidate per stockable player craft, priced by its stat sum (never zero).
        var shelvedIds = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        foreach (var item in state.Items.Values)
        {
            if (shelvedIds.Contains(item.Id.Value))
            {
                continue;
            }

            var price = Math.Max(1, (item.Stats.Attack + item.Stats.Defense) * 2);
            var candidate = new StockAction(item.Id, price);
            if (IsLegal(state, candidate, phase))
            {
                actions.Add(candidate);
            }
        }

        // SetPrice / Unstock: one candidate per shelved entry.
        foreach (var entry in state.Player.Shelf)
        {
            var setPrice = new SetPriceAction(entry.Item, entry.Price);
            if (IsLegal(state, setPrice, phase))
            {
                actions.Add(setPrice);
            }

            var unstock = new UnstockAction(entry.Item);
            if (IsLegal(state, unstock, phase))
            {
                actions.Add(unstock);
            }
        }

        // BuyOre: one candidate per open offer, buying the FULL offered quantity.
        foreach (var offer in state.OpenOreOffers)
        {
            var candidate = new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity);
            if (IsLegal(state, candidate, phase))
            {
                actions.Add(candidate);
            }
        }

        // BuyMaterial: one candidate per priced-pool key, quantity 1.
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var candidate = new BuyMaterialAction(key, 1);
            if (IsLegal(state, candidate, phase))
            {
                actions.Add(candidate);
            }
        }

        // PostBounty: one candidate per legal floor at the smallest positive escrow.
        if (state.Player.Gold >= 1)
        {
            for (var floor = 1; floor <= MonsterTable.FloorCount; floor++)
            {
                var candidate = new PostBountyAction(floor, 1);
                if (IsLegal(state, candidate, phase))
                {
                    actions.Add(candidate);
                }
            }
        }

        // UnlockTalent: every node whose prerequisites are already met, per selected profession.
        foreach (var professionId in state.Player.SelectedProfessions)
        {
            if (!ProfessionRegistry.TryGet(professionId, out var profession))
            {
                continue;
            }

            foreach (var node in profession!.TalentNodes.Values)
            {
                var candidate = new UnlockTalentAction(node.NodeId, professionId);
                if (IsLegal(state, candidate, phase))
                {
                    actions.Add(candidate);
                }
            }
        }

        // SetProfessions: re-affirming the current selection is always legal (a no-op change).
        var reaffirm = new SetProfessionsAction(state.Player.SelectedProfessions);
        if (IsLegal(state, reaffirm, phase))
        {
            actions.Add(reaffirm);
        }

        // Camp verbs: one recall candidate per un-recalled party; one send candidate per party
        // for the first eligible held consumable.
        if (phase == DayPhase.Camp)
        {
            var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
            var rivalShelved = state.RivalShelf.Select(s => s.Item.Value).ToHashSet();
            var packed = state.Heroes.Values.SelectMany(h => h.Pack).Select(i => i.Value).ToHashSet();

            foreach (var inFlight in state.InFlight)
            {
                if (inFlight.Party.Count == 0)
                {
                    continue;
                }

                var recall = new RecallPartyAction(inFlight.Party[0]);
                if (IsLegal(state, recall, phase))
                {
                    actions.Add(recall);
                }

                foreach (var item in state.Items.Values)
                {
                    if (item.Effect is null || !item.PlayerCrafted
                        || shelved.Contains(item.Id.Value) || rivalShelved.Contains(item.Id.Value)
                        || packed.Contains(item.Id.Value))
                    {
                        continue;
                    }

                    var send = new SendSupplyAction(inFlight.Party[0], item.Id);
                    if (IsLegal(state, send, phase))
                    {
                        actions.Add(send);
                        break;
                    }
                }
            }
        }

        return actions.ToImmutable();
    }

    // ---- CraftingHandlers.ApplyCraft guards (recipe/profession/material/tier/quantity) ----
    private static bool CraftLegal(GameState state, CraftAction action)
    {
        if (!ProfessionRegistry.TryGetRecipe(action.RecipeId, out var recipe))
        {
            return false;
        }

        if (!ProfessionRegistry.TryGet(recipe!.Profession, out var profession))
        {
            return false;
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return false;
        }

        if (!RecipeTable.MaterialGrades.ContainsKey(action.MaterialKey))
        {
            return false;
        }

        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return false;
        }

        var efficiency = profession.MaterialEfficiencyNode is { } eff && talents.Contains(eff) ? 1 : 0;
        var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
        var have = state.Player.Materials.TryGetValue(action.MaterialKey, out var stock) ? stock : 0;
        return have >= needed;
    }

    // ---- ShopHandlers.ApplyStock guards ----
    private static bool StockLegal(GameState state, StockAction action)
    {
        if (!state.Items.TryGetValue(action.Item.Value, out var item))
        {
            return false;
        }

        if (!item.PlayerCrafted)
        {
            return false;
        }

        foreach (var hero in state.Heroes.Values)
        {
            if (hero.Gear.Weapon == action.Item || hero.Gear.Shield == action.Item || hero.Gear.Armor == action.Item)
            {
                return false;
            }
        }

        if (item.Effect is not null && state.EventLog.Any(e => e is ItemSold sold && sold.Item == action.Item))
        {
            return false;
        }

        if (state.Player.Shelf.Any(e => e.Item == action.Item))
        {
            return false;
        }

        return action.Price > 0;
    }

    // ---- ShopHandlers.ApplySetPrice guards ----
    private static bool SetPriceLegal(GameState state, SetPriceAction action) =>
        state.Player.Shelf.Any(e => e.Item == action.Item) && action.Price > 0;

    // ---- ShopHandlers.ApplyUnstock guards ----
    private static bool UnstockLegal(GameState state, UnstockAction action) =>
        state.Player.Shelf.Any(e => e.Item == action.Item);

    // ---- OreMarketHandlers.Apply guards (quantity, offer, hero, tariffed cost) ----
    private static bool BuyOreLegal(GameState state, BuyOreAction action)
    {
        if (action.Quantity <= 0)
        {
            return false;
        }

        var index = state.OpenOreOffers.FindIndex(o => o.From == action.From && o.MaterialKey == action.MaterialKey);
        if (index < 0)
        {
            return false;
        }

        var offer = state.OpenOreOffers[index];

        if (!state.Heroes.TryGetValue(action.From.Value, out var hero) || !hero.Alive)
        {
            return false;
        }

        if (action.Quantity > offer.Quantity)
        {
            return false;
        }

        var baseLineCost = action.Quantity * offer.UnitPrice;
        var faction = FactionRegistry.ByOreKey(action.MaterialKey);
        var playerCost = baseLineCost;
        if (faction is not null)
        {
            long max = faction.MaxAdjustmentPerMille;
            var raw = IntegerCurves.MulDiv(state.Player.StandingFor(faction.Id), faction.MaxAdjustmentPerMille, faction.StandingCap);
            var adj = Math.Clamp(raw, -max, max);
            playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adj, 1000);
        }

        return state.Player.Gold >= playerCost;
    }

    // ---- MaterialVendorHandlers.Apply guards (quantity, priced pool, quote cost) ----
    private static bool BuyMaterialLegal(GameState state, BuyMaterialAction action)
    {
        if (action.Quantity <= 0)
        {
            return false;
        }

        if (!MaterialRegistry.IsPriced(action.MaterialKey))
        {
            return false;
        }

        var cost = MaterialVendorHandlers.QuoteCost(action.MaterialKey, action.Quantity);
        return cost <= state.Player.Gold;
    }

    // ---- BountyHandlers.Apply guards (floor range, positive reward, escrow) ----
    private static bool PostBountyLegal(GameState state, PostBountyAction action) =>
        action.TargetFloor is >= 1 and <= MonsterTable.FloorCount
        && action.RewardGold > 0
        && state.Player.Gold >= action.RewardGold;

    // ---- ProfessionHandlers.ApplySet guards ----
    private static bool SetProfessionsLegal(SetProfessionsAction action)
    {
        if (action.Professions.Count is < 1 or > ProfessionHandlers.MaxSelected)
        {
            return false;
        }

        return action.Professions.All(ProfessionRegistry.IsRegistered);
    }

    // ---- CraftingHandlers.ApplyUnlock guards ----
    private static bool UnlockTalentLegal(GameState state, UnlockTalentAction action)
    {
        if (!ProfessionRegistry.TryGet(action.Profession, out var profession))
        {
            return false;
        }

        if (!profession!.TalentNodes.TryGetValue(action.NodeId, out var node))
        {
            return false;
        }

        var talents = state.Player.TalentsFor(action.Profession);
        if (talents.Contains(action.NodeId))
        {
            return false;
        }

        return node.Prerequisites.All(talents.Contains);
    }

    // ---- CampHandlers.ApplySend guards ----
    private static bool SendSupplyLegal(GameState state, SendSupplyAction action)
    {
        var index = state.InFlight.FindIndex(f => f.Party.Contains(action.To));
        if (index < 0)
        {
            return false;
        }

        var inFlight = state.InFlight[index];
        if (inFlight.Dead.Contains(action.To.Value) || inFlight.Recalled || inFlight.SupplySent)
        {
            return false;
        }

        if (!state.Items.TryGetValue(action.Item.Value, out var item) || item.Effect is null || !item.PlayerCrafted)
        {
            return false;
        }

        if (state.Player.Shelf.Any(e => e.Item == action.Item) || state.RivalShelf.Any(e => e.Item == action.Item))
        {
            return false;
        }

        if (state.Heroes.Values.Any(h => h.Pack.Contains(action.Item)))
        {
            return false;
        }

        var fee = CampHandlers.SupplyFee(inFlight.CheckpointFloor);
        return state.Player.Gold >= fee;
    }

    // ---- CampHandlers.ApplyRecall guards ----
    private static bool RecallLegal(GameState state, RecallPartyAction action)
    {
        var index = state.InFlight.FindIndex(f => f.Party.Contains(action.Member));
        if (index < 0)
        {
            return false;
        }

        return !state.InFlight[index].Recalled;
    }
}
