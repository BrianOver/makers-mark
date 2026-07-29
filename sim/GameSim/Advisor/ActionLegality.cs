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
/// <see cref="Professions.ProfessionHandlers"/>, <see cref="Expedition.CampHandlers"/>,
/// <see cref="Economy.ForgeTierHandlers"/>, <see cref="Economy.ForgeSupplyHandlers"/>,
/// <see cref="Economy.MasterworkAttemptHandlers"/>, and <see cref="Economy.LegendaryCommissionHandlers"/>
/// — a second copy of the same rules, on purpose (KTD9: outside <c>Contracts/</c>, no kernel
/// registration, no RNG). The 100-day kernel-parity property test
/// (<c>ActionLegalityTests</c>) is the standing drift tripwire: any future handler change that
/// isn't mirrored here fails that test, never silently.
///
/// <para>U4: <see cref="IsLegal"/>'s fallthrough THROWS <see cref="UnhandledActionException"/>
/// instead of returning <c>false</c> for an action type this switch has no case for. This is what
/// lets a reflection-driven test (<c>ActionLegalityTests</c>) tell "handled, the real answer
/// happens to be false" apart from "never reached a case at all" — a plain <c>false</c> fallthrough
/// is legal-shaped and silent (exactly how the four Phase-D gold-sink verbs went unmirrored:
/// <see cref="UpgradeForgeAction"/>, <see cref="BuyForgeSupplyAction"/>,
/// <see cref="MasterworkAttemptAction"/>, <see cref="CommissionLegendaryWorkAction"/> all reported
/// ILLEGAL, forever, with no test failure). Every concrete <see cref="PlayerAction"/> derived type
/// in <c>Contracts/</c> has a real case below; the throw only fires for a FUTURE type nobody has
/// mirrored yet, which is exactly the drift this module exists to make loud.</para>
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
        AcceptCommissionAction accept => phase == DayPhase.Morning && AcceptCommissionLegal(state, accept),
        DeclineCommissionAction decline => phase == DayPhase.Morning && DeclineCommissionLegal(state, decline),
        HonorMemorialAction honor => phase == DayPhase.Evening && HonorMemorialLegal(state, honor),
        ReforgeHeirloomAction reforge => ReforgeHeirloomLegal(state, reforge),
        OpenCounterAction => phase == DayPhase.Morning && OpenCounterLegal(state),
        PresentItemAction present => phase == DayPhase.Morning && PresentItemLegal(state, present),
        SuggestItemAction suggest => phase == DayPhase.Morning && SuggestItemLegal(state, suggest),
        HaggleResponseAction haggle => phase == DayPhase.Morning && HaggleResponseLegal(state, haggle),
        CloseCounterAction => phase == DayPhase.Morning && CloseCounterLegal(state),
        UpgradeForgeAction => phase == DayPhase.Morning && UpgradeForgeLegal(state),
        BuyForgeSupplyAction buyForgeSupply => phase == DayPhase.Morning && BuyForgeSupplyLegal(state, buyForgeSupply),
        MasterworkAttemptAction masterwork => MasterworkAttemptLegal(state, masterwork),
        CommissionLegendaryWorkAction commissionLegendary => CommissionLegendaryWorkLegal(state, commissionLegendary),
        _ => throw new UnhandledActionException(action.GetType()),
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

        // AcceptCommission / DeclineCommission: one candidate pair per still-open (not-yet-accepted)
        // commission — mirrors CommissionHandlers' own "by hero" target lookup.
        foreach (var commission in state.Commissions)
        {
            if (commission.Accepted)
            {
                continue;
            }

            var accept = new AcceptCommissionAction(commission.Hero);
            if (IsLegal(state, accept, phase))
            {
                actions.Add(accept);
            }

            var decline = new DeclineCommissionAction(commission.Hero);
            if (IsLegal(state, decline, phase))
            {
                actions.Add(decline);
            }
        }

        // HonorMemorial: one candidate per NOT-YET-honored memorial (an already-honored one is a
        // legal idempotent no-op too — see HonorMemorialLegal — but isn't a fresh "opportunity").
        foreach (var memorial in state.Drama.Memorials)
        {
            if (memorial.Honored)
            {
                continue;
            }

            var honor = new HonorMemorialAction(memorial.Hero);
            if (IsLegal(state, honor, phase))
            {
                actions.Add(honor);
            }
        }

        // ReforgeHeirloom: one candidate per not-yet-reforged fallen-gear item, first recipe (in
        // registry order) that turns out legal — same "one canonical instance" shape as Craft above.
        var reforgedSources = state.EventLog.OfType<HeirloomReforged>().Select(e => e.SourceItem.Value).ToHashSet();
        var fallenItems = state.EventLog.OfType<HeroDied>()
            .SelectMany(died => new[] { died.WornGear.Weapon, died.WornGear.Shield, died.WornGear.Armor, died.WornGear.Trinket })
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Where(item => !reforgedSources.Contains(item.Value))
            .Distinct()
            .OrderBy(item => item.Value);

        foreach (var sourceItem in fallenItems)
        {
            foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
            {
                var candidate = new ReforgeHeirloomAction(sourceItem, recipe.RecipeId, recipe.MaterialKey);
                if (IsLegal(state, candidate, phase))
                {
                    actions.Add(candidate);
                    break;
                }
            }
        }

        // Counter verbs: only reachable while the session-opening phase (Morning) is live. MF-8 —
        // BaselinePlayer never opens the counter, so these candidates are only ever exercised when
        // the caller drives CounterPlayer (or an equivalent open-session fixture).
        if (phase == DayPhase.Morning)
        {
            var open = new OpenCounterAction();
            if (IsLegal(state, open, phase))
            {
                actions.Add(open);
            }

            var close = new CloseCounterAction();
            if (IsLegal(state, close, phase))
            {
                actions.Add(close);
            }

            if (state.Counter is { Closed: false, Active: { } activeHero } session
                && state.Heroes.TryGetValue(activeHero.Value, out var activeHeroState))
            {
                // PresentItem: one candidate per shelved item, first legal wins.
                foreach (var entry in state.Player.Shelf)
                {
                    var present = new PresentItemAction(entry.Item);
                    if (IsLegal(state, present, phase))
                    {
                        actions.Add(present);
                        break;
                    }
                }

                // SuggestItem: ANY known item is a legal candidate — a wrong-slot suggestion is a
                // legal no-op (SuggestItemLegal), so one canonical instance exercises the verb.
                var suggestTarget = state.Items.Values.FirstOrDefault();
                if (suggestTarget is not null)
                {
                    var suggest = new SuggestItemAction(suggestTarget.Id);
                    if (IsLegal(state, suggest, phase))
                    {
                        actions.Add(suggest);
                    }
                }

                // HaggleResponse: only meaningful once a round is open with a standing offer.
                if (session.Round > 0 && session.StandingOfferGold is { } standingOffer && session.Presented is not null)
                {
                    var accept = new HaggleResponseAction(HaggleResponseKind.Accept);
                    if (IsLegal(state, accept, phase))
                    {
                        actions.Add(accept);
                    }

                    var holdFirm = new HaggleResponseAction(HaggleResponseKind.HoldFirm);
                    if (IsLegal(state, holdFirm, phase))
                    {
                        actions.Add(holdFirm);
                    }

                    var counterPrice = Math.Clamp(standingOffer, 1, Math.Max(1, activeHeroState.Gold));
                    var counter = new HaggleResponseAction(HaggleResponseKind.Counter, counterPrice);
                    if (IsLegal(state, counter, phase))
                    {
                        actions.Add(counter);
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
        if (have < needed)
        {
            return false;
        }

        // ---- CraftingHandlers.ApplyCraft guard 7 (action-budget, checked LAST) ----
        return state.ActionSlotsRemaining > 0;
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

        if (state.Player.Gold < playerCost)
        {
            return false;
        }

        // ---- OreMarketHandlers.Apply guard 7 (action-budget, checked LAST) ----
        return state.ActionSlotsRemaining > 0;
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
        if (cost > state.Player.Gold)
        {
            return false;
        }

        // ---- MaterialVendorHandlers.Apply guard 5 (action-budget, checked LAST) ----
        return state.ActionSlotsRemaining > 0;
    }

    // ---- BountyHandlers.Apply guards (floor range, positive reward, escrow, action-budget
    // checked LAST) ----
    private static bool PostBountyLegal(GameState state, PostBountyAction action) =>
        action.TargetFloor is >= 1 and <= MonsterTable.FloorCount
        && action.RewardGold > 0
        && state.Player.Gold >= action.RewardGold
        && state.ActionSlotsRemaining > 0;

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

    // ---- CommissionHandlers.ApplyAccept guards (Heroes/CommissionHandlers.cs) — the hero's single
    // open (not-yet-accepted) commission is the unambiguous target. ----
    private static bool AcceptCommissionLegal(GameState state, AcceptCommissionAction action) =>
        state.Commissions.Any(c => c.Hero == action.Hero && !c.Accepted);

    // ---- CommissionHandlers.ApplyDecline guards (same open-commission lookup as Accept) ----
    private static bool DeclineCommissionLegal(GameState state, DeclineCommissionAction action) =>
        state.Commissions.Any(c => c.Hero == action.Hero && !c.Accepted);

    // ---- FarewellHandlers.Apply guards (Drama/FarewellHandlers.cs). A memorial must be recorded
    // for the hero — but honoring an ALREADY-honored memorial is an IDEMPOTENT NO-OP in the handler
    // (returns success, not a RejectedAction), so legality does not require Honored == false; it
    // mirrors the handler's actual accept/reject boundary, not "first rite only".
    private static bool HonorMemorialLegal(GameState state, HonorMemorialAction action) =>
        state.Drama.Memorials.Any(m => m.Hero == action.Hero);

    // ---- HeirloomHandlers.Apply guards (Crafting/HeirloomHandlers.cs): source provenance (worn by
    // a fallen hero, not already reforged) + the SAME recipe/profession/material/tier/quantity chain
    // as CraftLegal above + the action-budget gate (guard 9, checked last like every other
    // real-work handler). Legal in any phase — same as Craft, the forge never closes.
    private static bool ReforgeHeirloomLegal(GameState state, ReforgeHeirloomAction action)
    {
        if (!state.Items.ContainsKey(action.SourceItem.Value))
        {
            return false;
        }

        var fallenHero = state.EventLog.OfType<HeroDied>()
            .Any(died => WoreItem(died.WornGear, action.SourceItem));
        if (!fallenHero)
        {
            return false;
        }

        if (state.EventLog.OfType<HeirloomReforged>().Any(already => already.SourceItem == action.SourceItem))
        {
            return false;
        }

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
        if (have < needed)
        {
            return false;
        }

        return state.ActionSlotsRemaining > 0;
    }

    private static bool WoreItem(GearSet gear, ItemId item) =>
        gear.Weapon == item || gear.Shield == item || gear.Armor == item || gear.Trinket == item;

    // ---- CounterHandlers.ApplyOpen guards: legal only when no session is open (null) or the prior
    // one already finished closing. ----
    private static bool OpenCounterLegal(GameState state) =>
        state.Counter is not { Closed: false };

    // ---- CounterHandlers.ApplyPresent guards (RequireActiveSession + item known + item shelved) ----
    private static bool PresentItemLegal(GameState state, PresentItemAction action) =>
        HasActiveSession(state) && state.Items.ContainsKey(action.Item.Value)
        && state.Player.Shelf.Any(e => e.Item == action.Item);

    // ---- CounterHandlers.ApplySuggest guards (RequireActiveSession + item known). fable: a
    // suggestion aimed at a slot the hero wouldn't wear is a LEGAL NO-OP in the handler
    // (HaggleResolver.ApplySuggestBonus just returns the session unchanged — CounterHandlers.cs:
    // 110-114) — it is NOT rejected, so legality here does not check slot fit at all. Only "no open
    // session" / "no active customer" / "unknown item" make a SuggestItem illegal.
    private static bool SuggestItemLegal(GameState state, SuggestItemAction action) =>
        HasActiveSession(state) && state.Items.ContainsKey(action.Item.Value);

    // ---- CounterHandlers.ApplyClose guards: legal whenever a session (open OR already-closing)
    // exists — closing an already-closing session is another idempotent no-op. ----
    private static bool CloseCounterLegal(GameState state) =>
        state.Counter is not null;

    // ---- CounterHandlers.ApplyHaggle + HaggleResolver.ResolveHaggleResponse guards: a standing
    // offer must exist (round open, presented item still shelved), then Accept/HoldFirm always
    // resolve, and Counter additionally needs a positive price the active hero can afford. ----
    private static bool HaggleResponseLegal(GameState state, HaggleResponseAction action)
    {
        if (state.Counter is not { Closed: false } session || session.Active is not { } activeHero)
        {
            return false;
        }

        if (session.Round == 0 || session.StandingOfferGold is null || session.Presented is not { } presentedId)
        {
            return false;
        }

        if (!state.Items.ContainsKey(presentedId.Value))
        {
            return false;
        }

        if (!state.Player.Shelf.Any(e => e.Item == presentedId))
        {
            return false;
        }

        return action.Kind switch
        {
            HaggleResponseKind.Accept => true,
            HaggleResponseKind.HoldFirm => true,
            HaggleResponseKind.Counter => action.Price is { } price && price > 0
                && state.Heroes.TryGetValue(activeHero.Value, out var hero) && price <= hero.Gold,
            _ => false,
        };
    }

    /// <summary>Shared gate mirroring CounterHandlers.RequireActiveSession: a session must be open
    /// (not already closing) and a customer must actually be at the counter.</summary>
    private static bool HasActiveSession(GameState state) =>
        state.Counter is { Closed: false, Active: not null };

    // ---- ForgeTierHandlers.Apply guards (U4): ceiling, lock-and-key floor ore, gold, action-budget
    // checked LAST — same order as the handler. ----
    private static bool UpgradeForgeLegal(GameState state)
    {
        var tierIndex = Economy.ForgeTierHandlers.CurrentTierIndex(state.Player);

        // 1. Already at the ceiling (Forge V) — nothing left to buy.
        if (tierIndex > Economy.ForgeTierHandlers.MaxUpgradeIndex)
        {
            return false;
        }

        var oreKey = Economy.ForgeTierHandlers.OreKey[tierIndex];
        var cost = Economy.ForgeTierHandlers.GoldCost[tierIndex];

        // 2. Lock-and-key: must have the floor's ore in hand — gold alone never buys past the Mine.
        var oreHave = state.Player.Materials.TryGetValue(oreKey, out var oreStock) ? oreStock : 0;
        if (oreHave < Economy.ForgeTierHandlers.OreQuantity)
        {
            return false;
        }

        // 3. Gold.
        if (state.Player.Gold < cost)
        {
            return false;
        }

        // 4. Day action-budget gate — checked LAST, like every other real-work handler.
        return state.ActionSlotsRemaining > 0;
    }

    // ---- ForgeSupplyHandlers.Apply guards (U4): quantity, stocked supply key (via the shared
    // UnitPrice formula — the one pricing source, same precedent as BuyMaterialLegal/QuoteCost),
    // cost, action-budget checked LAST. ----
    private static bool BuyForgeSupplyLegal(GameState state, BuyForgeSupplyAction action)
    {
        if (action.Quantity <= 0)
        {
            return false;
        }

        var unitPrice = Economy.ForgeSupplyHandlers.UnitPrice(action.SupplyKey);
        if (unitPrice < 0)
        {
            return false;
        }

        var cost = action.Quantity * unitPrice;
        if (cost > state.Player.Gold)
        {
            return false;
        }

        return state.ActionSlotsRemaining > 0;
    }

    // ---- MasterworkAttemptHandlers.Apply guards (U4): recipe/profession/selected, forge-tier gate
    // (unlocked by ForgeTierHandlers progress), material grade/tier/quantity (same efficiency-node
    // chain as CraftLegal), coal, flux, gold surcharge power-matched to forge tier, action-budget
    // checked LAST — same order as the handler. Legal in any phase — the forge never closes, same
    // as CraftAction/ReforgeHeirloomAction. ----
    private static bool MasterworkAttemptLegal(GameState state, MasterworkAttemptAction action)
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

        var tierIndex = Economy.ForgeTierHandlers.CurrentTierIndex(state.Player);
        if (tierIndex < Economy.MasterworkAttemptHandlers.RequiredForgeTierIndex)
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
        var neededMaterial = Math.Max(1, recipe.MaterialQuantity - efficiency);
        var materialHave = state.Player.Materials.TryGetValue(action.MaterialKey, out var matStock) ? matStock : 0;
        if (materialHave < neededMaterial)
        {
            return false;
        }

        var coalHave = state.Player.Materials.TryGetValue(Economy.ForgeSupplyHandlers.Coal, out var coalStock) ? coalStock : 0;
        if (coalHave < Economy.MasterworkAttemptHandlers.CoalCost)
        {
            return false;
        }

        var fluxHave = state.Player.Materials.TryGetValue(Economy.ForgeSupplyHandlers.Flux, out var fluxStock) ? fluxStock : 0;
        if (fluxHave < Economy.MasterworkAttemptHandlers.FluxCost)
        {
            return false;
        }

        var surcharge = Economy.MasterworkAttemptHandlers.GoldSurchargePerTier * (tierIndex + 1);
        if (state.Player.Gold < surcharge)
        {
            return false;
        }

        return state.ActionSlotsRemaining > 0;
    }

    // ---- LegendaryCommissionHandlers.Apply guards (U4): campaign cap, recipe/profession/selected,
    // material grade/tier/quantity (DOUBLE, no efficiency discount — the extravagant path, per the
    // handler), gold power-matched to forge tier, action-budget checked LAST — same order as the
    // handler. Legal in any phase, like a craft. ----
    private static bool CommissionLegendaryWorkLegal(GameState state, CommissionLegendaryWorkAction action)
    {
        var used = state.Player.Materials.TryGetValue(Economy.LegendaryCommissionHandlers.CommissionsUsedKey, out var usedStock) ? usedStock : 0;
        if (used >= Economy.LegendaryCommissionHandlers.MaxPerCampaign)
        {
            return false;
        }

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

        var neededMaterial = recipe.MaterialQuantity * Economy.LegendaryCommissionHandlers.MaterialMultiplier;
        var materialHave = state.Player.Materials.TryGetValue(action.MaterialKey, out var matStock) ? matStock : 0;
        if (materialHave < neededMaterial)
        {
            return false;
        }

        var tierIndex = Economy.ForgeTierHandlers.CurrentTierIndex(state.Player);
        var cost = Economy.LegendaryCommissionHandlers.BaseGold * (tierIndex + 1);
        if (state.Player.Gold < cost)
        {
            return false;
        }

        return state.ActionSlotsRemaining > 0;
    }
}

/// <summary>
/// U4: thrown by <see cref="ActionLegality.IsLegal"/>'s switch fallthrough for any concrete
/// <see cref="PlayerAction"/> derived type this module has no mirrored case for yet — the drift
/// signal a reflection-driven test can catch that a silent <c>false</c> fallthrough could not (see
/// <see cref="ActionLegality"/>'s class doc). Should never fire in production: every concrete type
/// <c>Contracts/Actions.cs</c> defines has a case in the switch above. If it ever does fire, it
/// means a new action type shipped without its legality mirror — exactly the bug this exists to
/// surface loudly instead of silently reporting the new verb as permanently illegal.
/// </summary>
public sealed class UnhandledActionException(Type actionType)
    : Exception($"ActionLegality.IsLegal has no case for action type '{actionType.Name}' — add one before shipping this action.")
{
    public Type ActionType { get; } = actionType;
}
