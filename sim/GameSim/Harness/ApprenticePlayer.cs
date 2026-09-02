using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Harness;

/// <summary>
/// P2-ONBOARD-03 (docs/design/MAKERS-MARK.md §11.15): plays The Warrant roughly as the guided
/// course describes it, so a batch sweep (<c>GameSim.Cli.BatchRunner --policy apprentice</c>) can
/// measure which seeds actually produce the intended first week. This is the INSTRUMENT the seed
/// search (P2-ONBOARD-04) runs against, not the pin itself — nothing here searches seeds or
/// asserts a beat criterion.
///
/// <para>The course, roughly: buy copper and craft on day 1; shelve every unsold player craft at a
/// fair price EXCEPT one held consumable, kept back in reserve for the camp runner; open the
/// counter on day 2 and close FAIR — accept the hero's own offer, never haggle for more; accept
/// every open GEAR commission (delivery is automatic once a matching item lands on the shelf, see
/// <see cref="Heroes.CommissionHandlers.TryFulfillFromShelf"/> — there is no separate "deliver"
/// action); buy every affordable ore offer each evening; and send the one reserved consumable to a
/// camped party the moment one fires. Every entry in <see cref="GameState.InFlight"/> is a
/// deep-bound camp by construction — <see cref="Expedition.ExpeditionSystem"/> only parks a party
/// when its checkpoint floor is strictly below its target floor, so there is no such thing as an
/// InFlight camp that ISN'T headed deeper.</para>
///
/// <para>Deliberately NARROW rather than adaptively optimal (one gear recipe, one consumable
/// recipe, fixed rules) — this is a SCRIPT, not a second reference player, so the seed search that
/// runs against it has a stable, predictable target rather than a moving one.</para>
///
/// <para>Same purity contract as every other policy in this namespace (see
/// <see cref="BaselinePlayer"/>): a pure function of <see cref="GameState"/>, no IO, no RNG of its
/// own, no wall clock. Branching on <see cref="GameState.Day"/> is deliberate here and nowhere
/// else in <c>Harness/</c> — this policy IS a calendar script; the others are day-agnostic.</para>
/// </summary>
public static class ApprenticePlayer
{
    /// <summary>The one gear recipe this course ever crafts — cheap, tier 1, always legal the
    /// moment enough copper is banked.</summary>
    private const string GearRecipeId = "dagger";

    /// <summary>The one consumable recipe this course ever crafts — the vigil runner needs a
    /// consumable to carry, and nothing else in this narrow menu produces one.</summary>
    private const string ConsumableRecipeId = "field-salve";

    /// <summary>The Morning standing-vendor material this course ever buys.</summary>
    private const string BootstrapMaterialKey = "copper";

    /// <summary>The calendar day Bryn's beat 7 names: "tomorrow someone comes to the counter."</summary>
    private const int CounterOpensOnDay = 2;

    public static ImmutableList<PlayerAction> ActionsFor(GameState state) => state.Phase switch
    {
        DayPhase.Morning => MorningActions(state),
        DayPhase.Expedition => ExpeditionActions(state),
        DayPhase.Camp => CampActions(state),
        DayPhase.Evening => EveningActions(state),
        // Camp is the only decision window during a staged expedition (D5 precedent, BaselinePlayer):
        // ExpeditionDeep carries no player verb at all.
        _ => ImmutableList<PlayerAction>.Empty,
    };

    private static ImmutableList<PlayerAction> MorningActions(GameState state)
    {
        // Day 2, mid- or end-session: this policy drives the counter one action per tick, exactly
        // like CounterPlayer — the ordinary routine below already ran on the tick that opened it.
        if (state.Day == CounterOpensOnDay && state.Counter is { Closed: false })
        {
            return CounterFairActions(state);
        }

        if (state.Day == CounterOpensOnDay && state.Counter is { Closed: true })
        {
            return ImmutableList<PlayerAction>.Empty; // already closing this tick — nothing left to do
        }

        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        // Accept every open GEAR commission. Consumable-slot commissions are excluded for the same
        // reason BaselinePlayer excludes them: this policy's shelf never carries a spare consumable
        // to fulfil one from (the one it holds is reserved for the camp runner), so accepting one
        // would only ever sit open.
        foreach (var commission in state.Commissions.Where(c => !c.Accepted && c.Slot != ItemSlot.Consumable))
        {
            var accept = new AcceptCommissionAction(commission.Hero);
            if (ActionLegality.IsLegal(state, accept, state.Phase))
            {
                actions.Add(accept);
            }
        }

        // Buy material: top up copper to one craft's worth (both recipes this policy ever crafts
        // need the same quantity) whenever short — the always-available Morning vendor floor that
        // makes day 1 reachable with zero starting materials (MaterialVendorHandlers' own class doc).
        var neededCopper = Math.Max(
            RecipeTable.All[GearRecipeId].MaterialQuantity,
            RecipeTable.All[ConsumableRecipeId].MaterialQuantity);
        var haveCopper = state.Player.Materials.TryGetValue(BootstrapMaterialKey, out var copper) ? copper : 0;
        if (haveCopper < neededCopper)
        {
            var buy = new BuyMaterialAction(BootstrapMaterialKey, neededCopper - haveCopper);
            if (ActionLegality.IsLegal(state, buy, state.Phase))
            {
                actions.Add(buy);
            }
        }

        // Shelve every unsold player craft at a fair price (the rival's own formula: double the
        // stat that stands in for value — BaselinePlayer's pricing, proven fair by the same
        // measurement), except the one held consumable this policy reserves for the camp runner.
        var reserve = ReserveConsumable(state);
        var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        var equipped = state.Heroes.Values
            .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor, h.Gear.Trinket })
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToHashSet();
        // A consumable that has ever sold is gone for good (ShopHandlers 3b) — never re-offer it.
        var soldConsumables = state.EventLog.OfType<ItemSold>()
            .Select(e => e.Item.Value)
            .Where(id => state.Items.TryGetValue(id, out var sold) && sold.Effect is not null)
            .ToHashSet();

        foreach (var item in state.Items.Values.Where(i =>
                     i.PlayerCrafted && i.Id != reserve
                     && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value)
                     && (i.Effect is null || !soldConsumables.Contains(i.Id.Value))))
        {
            var value = item.Effect is { } effect ? effect.Magnitude : item.Stats.Attack + item.Stats.Defense;
            var stock = new StockAction(item.Id, Math.Max(1, value * 2));
            if (ActionLegality.IsLegal(state, stock, state.Phase))
            {
                actions.Add(stock);
            }
        }

        // Day 2, first tick of the Morning (Counter still null): open it, alongside the ordinary
        // routine above — OpenCounterAction reads only state.Heroes, so processing it after the
        // accept/buy/stock actions above (which never touch Heroes) is order-independent.
        if (state.Day == CounterOpensOnDay)
        {
            var open = new OpenCounterAction();
            if (ActionLegality.IsLegal(state, open, state.Phase))
            {
                actions.Add(open);
            }
        }

        return actions.ToImmutable();
    }

    /// <summary>The single action this policy submits for one tick of a day-2 counter session:
    /// present the shelf's best item, respond FAIR to any standing offer (Accept — take the hero's
    /// own number, never haggle for more), or close once there is nothing left to do. Mirrors
    /// <see cref="CounterPlayer"/>'s state machine exactly, with one behavioral change (Accept
    /// instead of a computed Counter) — see this type's class doc for why that is "fair."</summary>
    private static ImmutableList<PlayerAction> CounterFairActions(GameState state)
    {
        var counter = state.Counter!;
        if (counter.Active is not { } activeId
            || !state.Heroes.TryGetValue(activeId.Value, out var hero)
            || !hero.Alive)
        {
            return ImmutableList.Create<PlayerAction>(new CloseCounterAction());
        }

        if (counter.Round > 0 && counter.StandingOfferGold is not null && counter.Presented is not null)
        {
            return ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept));
        }

        var best = BestPresentable(state, hero);
        return ImmutableList.Create<PlayerAction>(
            best is { } chosen ? new PresentItemAction(chosen) : new CloseCounterAction());
    }

    /// <summary>The shelf item that gains this hero the most over their currently equipped item in
    /// that slot, or <see langword="null"/> for an empty shelf. Iterates in ItemId order so a score
    /// tie always resolves the same way (determinism).</summary>
    private static ItemId? BestPresentable(GameState state, Hero hero)
    {
        ItemId? best = null;
        var bestScore = int.MinValue;

        foreach (var entry in state.Player.Shelf.OrderBy(e => e.Item.Value))
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                continue; // defensive: a shelf entry outliving its item should never crash the morning
            }

            var equippedScore = hero.Gear.Slot(item.Slot) is { } equippedId
                                 && state.Items.TryGetValue(equippedId.Value, out var equipped)
                ? equipped.Stats.Attack + equipped.Stats.Defense
                : 0;
            var score = item.Stats.Attack + item.Stats.Defense - equippedScore;
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Item;
            }
        }

        return best;
    }

    private static ImmutableList<PlayerAction> ExpeditionActions(GameState state)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();
        if (state.ActionSlotsRemaining <= 0)
        {
            return actions.ToImmutable();
        }

        // Craft gear whenever the shelf holds none unsold (keep the shop stocked); once a gear
        // item is sitting there waiting for a buyer, spend the day's craft on the consumable
        // instead — banking or topping up the one reserved for the camp runner. Any consumable
        // beyond the single reserve flows to the shelf like any other craft (the Morning stocking
        // loop above only ever holds back one), so this never piles up unsold stock either way.
        var hasUnsoldGear = state.Player.Shelf.Any(e =>
            state.Items.TryGetValue(e.Item.Value, out var shelved) && shelved.PlayerCrafted && shelved.Effect is null);
        var recipe = RecipeTable.All[hasUnsoldGear ? ConsumableRecipeId : GearRecipeId];

        var craft = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
        if (ActionLegality.IsLegal(state, craft, state.Phase))
        {
            actions.Add(craft);
        }

        return actions.ToImmutable();
    }

    private static ImmutableList<PlayerAction> CampActions(GameState state)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        // Send the one reserved consumable to the first camped party that can legally receive it —
        // "one vigil supply when a deep-bound camp fires" (CLAUDE.md's fourth honest channel). The
        // reserve is a single item, so this can only ever fire once until the craft loop rebuilds it.
        if (ReserveConsumable(state) is { } supply)
        {
            foreach (var inFlight in state.InFlight)
            {
                if (inFlight.Party.Count == 0)
                {
                    continue;
                }

                var send = new SendSupplyAction(inFlight.Party[0], supply);
                if (ActionLegality.IsLegal(state, send, state.Phase))
                {
                    actions.Add(send);
                    break;
                }
            }
        }

        return actions.ToImmutable();
    }

    private static ImmutableList<PlayerAction> EveningActions(GameState state)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();
        var gold = state.Player.Gold;
        var slots = state.ActionSlotsRemaining;

        // Buy every ore offer the purse can afford, in offer order, while the day still has action
        // slots (G3) — the same simple always-buy rule BaselinePlayer opens its Evening with, minus
        // the Forge Tier reservation logic: this course never pursues a tier upgrade.
        foreach (var offer in state.OpenOreOffers)
        {
            if (slots <= 0)
            {
                break;
            }

            var cost = offer.Quantity * offer.UnitPrice;
            if (cost > gold)
            {
                continue;
            }

            var buy = new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity);
            if (ActionLegality.IsLegal(state, buy, state.Phase))
            {
                actions.Add(buy);
                gold -= cost;
                slots--;
            }
        }

        return actions.ToImmutable();
    }

    /// <summary>The single consumable this policy holds back from the shelf for the camp runner —
    /// the lowest ItemId (deterministic) player-crafted consumable that is unshelved, not on the
    /// rival's shelf, not already in any hero's pack, and has never sold (a sold consumable is
    /// gone for good once drunk — ShopHandlers 3b — so its id must never be treated as held stock
    /// again even after it drops out of every pack). Null when none is held.</summary>
    private static ItemId? ReserveConsumable(GameState state)
    {
        var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        var rivalShelved = state.RivalShelf.Select(s => s.Item.Value).ToHashSet();
        var packed = state.Heroes.Values.SelectMany(h => h.Pack).Select(i => i.Value).ToHashSet();
        var soldConsumables = state.EventLog.OfType<ItemSold>().Select(e => e.Item.Value).ToHashSet();

        return state.Items.Values
            .Where(i => i.PlayerCrafted && i.Effect is not null
                        && !shelved.Contains(i.Id.Value) && !rivalShelved.Contains(i.Id.Value)
                        && !packed.Contains(i.Id.Value) && !soldConsumables.Contains(i.Id.Value))
            .OrderBy(i => i.Id.Value)
            .Select(i => (ItemId?)i.Id)
            .FirstOrDefault();
    }
}
