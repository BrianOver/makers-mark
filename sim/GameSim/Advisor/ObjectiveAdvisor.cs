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
///
/// <para><b>P2-HONEST-24: <see cref="Reason"/> is never an order.</b> The game's first law
/// (CLAUDE.md rule 12, <c>LAW:influence-never-orders</c>) says influence never orders the player —
/// and until this unit that held for hero-facing verbs (<c>HeroSovereigntyCensusTests</c>) but not
/// for the advisor's OWN voice, which imperative-moded its player-facing copy ("craft 'X' now",
/// "Raise the forge to Tier N") and had that spoken verbatim in Bryn's mouth
/// (<c>MentorIdleVoice</c>). Every <see cref="Reason"/> this class returns states a fact or a
/// stake — what is true, what it costs to skip, what a purchase would unlock — never an
/// instruction. <c>AdvisorNeverOrdersTests</c> is the tripwire.</para>
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
                        $"{memorial.HeroName}'s memorial still waits unhonored — the rite keeps, and it will wait as long as you do."));
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
                // P2-SCREEN-31: trimmed to help this line (and BuyMaterial/Craft's shared tutorial
                // slot, which can render ANY top suggestion — TutorialFlow.StepText) fit the
                // ObjectiveTracker's real 3-line tutorial budget. Every fact
                // ObjectiveAdvisorTests.OpenCommission_TopSuggestion_... checks for (hero, slot,
                // quality, "{premium}g", deadline day) is still present, just shorter-worded.
                suggestions.Add(new Suggestion(accept,
                    $"{commission.HeroName}'s commission: {commission.Slot} at {commission.MinQuality}+, " +
                    $"{commission.PremiumGold}g by day {commission.DeadlineDay}" +
                    $"{GameSim.Heroes.CommissionSystem.SlotHonestyNote(commission.Slot)}."));
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

        // 2. Fallback (unchanged from pre-U8 except for P2-HONEST-36's news gate below): the
        //    cheapest-productive-path loop — still the tightest loop when no sharper demand signal
        //    exists (fresh saves, no live commission or stall yet).
        //
        //    P2-HONEST-36: measured at 5,147 of 13,745 advice lines (37%) — "You already have
        //    enough copper to craft 'buckler'" repeated night after night while the player simply
        //    never acted on it, the same shape P2-MEMORY-04 fixed for the memorial rite (a
        //    permanent fact told again as if it were news). This class holds no standing state
        //    (see class doc, U8), so "changed since last said" can't be a remembered flag — it is
        //    read off <see cref="IsFallbackNews"/>, which is a pure fact already recorded in
        //    <see cref="GameState.ActionLog"/> (no Contracts change). Not news: the block adds
        //    nothing, `suggestions` stays whatever it already was, and every caller's existing
        //    empty-list copy (CLI's "(none right now)", <c>ObjectiveTracker.NoObjectiveText</c>)
        //    says the board is quiet — no new copy needed.
        if (suggestions.Count == 0)
        {
            var (materialKey, quantity, cost) = CheapestProductivePath(state.Player);

            if (materialKey is not null && !IsFallbackNews(state, materialKey))
            {
                // Same fact as last time it was told (or never touched at all) — quiet.
            }
            else if (materialKey is not null && cost == 0)
            {
                // Craft is reachable RIGHT NOW (already enough of the cheapest-path material).
                var recipe = CheapestTier1Recipe(state.Player, materialKey);
                if (recipe is not null)
                {
                    var craft = new CraftAction(recipe.RecipeId, materialKey);
                    if (ActionLegality.IsLegal(state, craft, phase))
                    {
                        suggestions.Add(new Suggestion(craft, $"You already have enough {MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant()} to craft '{recipe.RecipeId}'."));
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
                    // P2-HONEST-24 + P2-ONBOARD-06 together, and the pair is the reason this reads as a
                    // gerund rather than either a command or a bare noun phrase. P2-ONBOARD-06 deleted all
                    // WHERE-to-walk copy from the tutorial cards on the grounds that the overlay already
                    // points at the thing, leaving "name the action" as this card's ONLY remaining job --
                    // pinned by TutorialFlowTests.Step1Copy_NamesTheAction_NeverWhereToWalk. P2-HONEST-24
                    // forbids the imperative that used to name it ("Buy 2 copper..."). A gerund satisfies
                    // both at once: the action is named, and nobody is told to take it.
                    suggestions.Add(new Suggestion(buy, $"Buying {quantity} {MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant()} ({cost}g) is the cheapest path to your next craft."));
                }
                else
                {
                    // True destitution dead-end (R5): the cheapest path is unaffordable and the
                    // floor's three conditions all hold. No legal action moves the player forward
                    // this Morning — DestitutionRecoverySystem tops the purse up to this SAME
                    // material's cost automatically before Expedition. Name it, but propose no
                    // illegal action.
                    suggestions.Add(new Suggestion(null,
                        $"Not enough gold for {MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant()} yet ({cost}g needed) — the town's recovery stipend will cover it this morning."));
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
                suggestions.Add(new Suggestion(stock, $"'{stockable.Name}' is finished, but it isn't on the shelf yet — nothing sells until it is."));
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

        // P2-SCREEN-31: both Reasons below trimmed to help the tutorial's shared BuyMaterial/Craft
        // slot (TutorialFlow.StepText can render ANY top suggestion) fit the tracker's real 3-line
        // budget — "aiming for floor X, missing Y gear" collapses to "missing Y gear for floor X",
        // and "you already have enough"/"would complete" shorten without dropping a fact.
        // Seed2026_15DayRun_TopSuggestion_... still matches on the literal word "stalled".
        var have = state.Player.Materials.TryGetValue(recipe.MaterialKey, out var stock) ? stock : 0;
        if (have >= recipe.MaterialQuantity)
        {
            var craft = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
            return ActionLegality.IsLegal(state, craft, phase)
                ? new Suggestion(craft,
                    $"{stall.HeroName}, {DepthCopy.Standing(stall.DeepestFloorReached)}, needs {slot} for floor {stall.TargetFloor} " +
                    $"— '{recipe.Name}' is ready: enough {MaterialRegistry.Require(recipe.MaterialKey).DisplayName.ToLowerInvariant()} in stock.")
                : null;
        }

        if (phase == DayPhase.Morning)
        {
            var buy = new BuyMaterialAction(recipe.MaterialKey, recipe.MaterialQuantity);
            if (ActionLegality.IsLegal(state, buy, phase))
            {
                var cost = MaterialVendorHandlers.QuoteCost(recipe.MaterialKey, recipe.MaterialQuantity);
                return new Suggestion(buy,
                    $"{stall.HeroName}, {DepthCopy.Standing(stall.DeepestFloorReached)}, needs {slot} for floor {stall.TargetFloor} " +
                    $"— {recipe.MaterialQuantity} {MaterialRegistry.Require(recipe.MaterialKey).DisplayName.ToLowerInvariant()} ({cost}g) completes '{recipe.Name}'.");
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
    /// crafted at all, so no material purchase would help yet), and when that unlock is itself held
    /// shut by the workshop's Forge Tier the purchase that opens it is named instead of nothing at
    /// all (see the branch's own comment). Once every tier is already unlocked,
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

            var talents = state.Player.TalentsFor(recipe.Profession);
            var unlock = new UnlockTalentAction(gate, recipe.Profession);
            if (ActionLegality.IsLegal(state, unlock, phase))
            {
                // P2-SCREEN-31: trimmed to help this line (and BuyMaterial/Craft's shared tutorial
                // slot) fit the tracker's real 3-line budget — "carries ... gear below" / "(currently
                // X)" / "opens the way to" shorten without dropping a fact.
                return new Suggestion(unlock,
                    $"{stall.HeroName}'s {targetSlot} is under floor {nextFloor}'s {required}+ bar ({carried} now) " +
                    $"— '{profession.TalentNodes[gate].Name}' unlocks '{recipe.Name}'.");
            }

            // The rung BELOW the unlock, and the one this branch used to answer with silence. A gate
            // node also requires the workshop to already stand at a matching Forge Tier
            // (TalentTree.ForgeTierRequirement, enforced by ActionLegality.UnlockTalentLegal and
            // CraftingHandlers.ApplyUnlock alike), so at the baseline Forge I every blacksmith gate
            // is illegal and the `return null` above fired for the whole early campaign — the
            // advisor went quiet at exactly the step blocking the ladder, and named the ladder only
            // once the purchase that opens it had already been made. ObjectiveAdvisorTests'
            // own U10 fixture had to patch a Forge-Tier-boosted projection over the real state to
            // reach the line at all, which is that gap written down.
            //
            // Suggest the purchase instead, and only when it is honestly the whole answer: the
            // prerequisite talents are already in hand (otherwise the missing prereq is the real
            // next step, not the forge), ONE upgrade actually clears the requirement (the gate chain
            // and the tier ladder advance in lockstep for every registered profession, so this holds
            // today — asserted rather than assumed, so a future gate needing two upgrades falls back
            // to today's silence rather than promising a rung it cannot reach), and
            // UpgradeForgeAction is legal right now (Morning, gold, the floor's ore, a slot —
            // ActionLegality decides, never a second copy of that arithmetic here).
            var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
            var upgrade = new UpgradeForgeAction();
            if (profession.CanUnlock(gate, talents)
                && TalentTree.ForgeTierRequirement.TryGetValue(gate, out var requiredTierIndex)
                && tierIndex < requiredTierIndex
                && tierIndex + 1 >= requiredTierIndex
                && ActionLegality.IsLegal(state, upgrade, phase))
            {
                // Payload first, context second — deliberately the reverse of every sibling reason
                // above. ObjectiveTracker clamps an advisor line to two lines at its dock width and
                // ellipsizes the tail (its own Refresh doc: "for advisor text losing the tail is
                // fine"), and here the tail is the part that names a purchase; leading with the
                // hero would have put the whole answer past the clamp. Every number is the sim's
                // own (ForgeTierHandlers' cost/ore tables, and its own "Forge Tier {tierIndex + 2}"
                // display convention for the tier a single upgrade lands on), never re-derived.
                //
                // P2-SCREEN-31: this used to be TWO sentences ("... opens the way to 'X'. {hero}
                // carries ... opens that recipe once the forge can hold it.") repeating the recipe
                // name and the "opens" verb — the single worst offender the fit-gate measurement
                // found (~200 chars). Folded into one sentence, payload still first; every fact
                // ObjectiveAdvisorTests.QualityStall_TopSuggestion_RaisesTheForge_... checks (the
                // gate name, "{GoldCost}g", the hero, one Tier-2 recipe name) is still present.
                return new Suggestion(upgrade,
                    $"Forge Tier {tierIndex + 2} ({ForgeTierHandlers.GoldCost[tierIndex]}g, " +
                    $"{ForgeTierHandlers.OreQuantity} {MaterialRegistry.Require(ForgeTierHandlers.OreKey[tierIndex]).DisplayName.ToLowerInvariant()}) " +
                    $"unlocks '{profession.TalentNodes[gate].Name}' for '{recipe.Name}' — {stall.HeroName}'s {targetSlot} " +
                    $"is under floor {nextFloor}'s {required}+ bar ({carried} now).");
            }

            return null;
        }

        // Every tier is already unlocked — the gate isn't the blocker. Craft (or buy toward) the
        // slot's best recipe, its own better material raising the quality ceiling.
        var best = recipes[^1];
        var have = state.Player.Materials.TryGetValue(best.MaterialKey, out var stock) ? stock : 0;
        if (have >= best.MaterialQuantity)
        {
            // P2-SCREEN-31: trimmed — same shape as the unlock case above.
            var craft = new CraftAction(best.RecipeId, best.MaterialKey);
            return ActionLegality.IsLegal(state, craft, phase)
                ? new Suggestion(craft,
                    $"{stall.HeroName}'s {targetSlot} is under floor {nextFloor}'s {required}+ bar ({carried} now) " +
                    $"— '{best.Name}' is ready: enough {MaterialRegistry.Require(best.MaterialKey).DisplayName.ToLowerInvariant()} in stock.")
                : null;
        }

        if (phase == DayPhase.Morning)
        {
            var buy = new BuyMaterialAction(best.MaterialKey, best.MaterialQuantity);
            if (ActionLegality.IsLegal(state, buy, phase))
            {
                var cost = MaterialVendorHandlers.QuoteCost(best.MaterialKey, best.MaterialQuantity);
                return new Suggestion(buy,
                    $"{stall.HeroName}'s {targetSlot} is under floor {nextFloor}'s {required}+ bar ({carried} now) " +
                    $"— {best.MaterialQuantity} {MaterialRegistry.Require(best.MaterialKey).DisplayName.ToLowerInvariant()} ({cost}g) completes '{best.Name}'.");
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
            ? new Suggestion(stock, $"You crafted a {held.Quality} {held.Name}, still unshelved — {demandLabel} wants it.")
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

    /// <summary>
    /// P2-HONEST-36: is the cheapest-productive-path fallback fact NEWS? <see cref="Suggest"/> is
    /// memoryless (class doc, U8) — it never remembers what it said yesterday — so "changed since
    /// last said" has to be read off a fact <see cref="GameState"/> already records, never a new
    /// flag. <paramref name="materialKey"/>'s held quantity (and therefore whether the fallback
    /// reads "already have enough" vs "buying N is cheapest" vs the destitution dead-end) only
    /// moves when the player submits an action that spends or acquires it — buying it, crafting
    /// with it, trading ore for it, or spending it on a heirloom/masterwork/legendary attempt — and
    /// every submitted action is already in <see cref="GameState.ActionLog"/>. Reselecting
    /// professions also rewrites the whole cheapest-path calculation (a different tier-1 recipe's
    /// quantity), so it counts as news for every material, not just the one it happens to name.
    ///
    /// Day 1 has no earlier day to have already said anything — always news, the
    /// <c>GossipSystem</c> "day 1 has no yesterday" precedent. Every day after that where nothing
    /// touched the fact, it is the same permanent fact already told: quiet, the same shape
    /// P2-MEMORY-04 fixed for the memorial rite.
    /// </summary>
    private static bool IsFallbackNews(GameState state, string materialKey)
    {
        if (state.Day <= 1)
        {
            return true;
        }

        foreach (var batch in state.ActionLog)
        {
            if (batch.Day != state.Day)
            {
                continue;
            }

            foreach (var action in batch.Actions)
            {
                if (TouchesMaterial(action, materialKey))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Every <see cref="PlayerAction"/> shape that can move <paramref name="materialKey"/>'s
    /// held quantity or rewrite which recipe is cheapest. Anything else (selling, shelving,
    /// haggling, bounties, memorials...) never touches the fallback's own arithmetic.</summary>
    private static bool TouchesMaterial(PlayerAction action, string materialKey) => action switch
    {
        BuyMaterialAction buy => buy.MaterialKey == materialKey,
        BuyOreAction ore => ore.MaterialKey == materialKey,
        CraftAction craft => craft.MaterialKey == materialKey,
        ReforgeHeirloomAction reforge => reforge.MaterialKey == materialKey,
        MasterworkAttemptAction masterwork => masterwork.MaterialKey == materialKey,
        CommissionLegendaryWorkAction legendary => legendary.MaterialKey == materialKey,
        SetProfessionsAction => true, // rewrites CheapestTier1RecipeQuantity for every material
        _ => false,
    };
}
