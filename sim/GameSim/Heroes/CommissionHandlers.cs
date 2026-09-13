using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>P2-PEOPLE-17 ("stocking a piece names the morning queue that will reach it first",
/// decision 1 — sell the good one or hold it for the hero who needs it): which accepted commission
/// a piece would fill, and who else's Morning turn comes before that hero's. A fact about queue
/// POSITION, computed by walking <see cref="HeroShoppingSystem.MorningShoppingOrder"/> exactly as
/// the real Morning pass will — never advice about pricing or holding the piece (law 12, "influence
/// never orders"): the caller decides what to do with the fact, this only states it.</summary>
public sealed record CommissionQueueForecast(Commission Commission, ImmutableArray<HeroId> AheadInQueue);

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U14): the player's two responses to a posted
/// commission — <see cref="AcceptCommissionAction"/> flips <see cref="Commission.Accepted"/> (locking
/// it in, so a later delivery pays out and a later miss stings — see <see cref="CommissionSystem"/>'s
/// expiry half); <see cref="DeclineCommissionAction"/> removes it outright, no obligation either way.
/// Both act on the hero's single open (not-yet-accepted) commission — <see cref="CommissionSystem"/>
/// never posts a second one to a hero who already has a live commission (open or accepted), so "by
/// hero" is an unambiguous target.
/// </summary>
public sealed class CommissionHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        phase == DayPhase.Morning && action is AcceptCommissionAction or DeclineCommissionAction;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            AcceptCommissionAction accept => ApplyAccept(state, accept),
            DeclineCommissionAction decline => ApplyDecline(state, decline),
            _ => (state, new RejectedAction(action, $"CommissionHandlers cannot apply {action.GetType().Name}.")),
        };

    private static (GameState, RejectedAction?) ApplyAccept(GameState state, AcceptCommissionAction action)
    {
        var index = state.Commissions.FindIndex(c => c.Hero == action.Hero && !c.Accepted);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No open commission from hero {action.Hero.Value} to accept."));
        }

        var commission = state.Commissions[index];
        var updated = state.Commissions.SetItem(index, commission with { Accepted = true });
        return (state with { Commissions = updated }, null);
    }

    private static (GameState, RejectedAction?) ApplyDecline(GameState state, DeclineCommissionAction action)
    {
        var index = state.Commissions.FindIndex(c => c.Hero == action.Hero && !c.Accepted);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No open commission from hero {action.Hero.Value} to decline."));
        }

        return (state with { Commissions = state.Commissions.RemoveAt(index) }, null);
    }

    /// <summary>Mood gained when an ACCEPTED commission is delivered by its deadline — bigger than the
    /// everyday counter-haggle "pin" bonus (<c>WillingnessModel.PinMoodBonus</c> = 60): this is a
    /// promise kept, not just a fair price.</summary>
    public const int FulfillMoodBonus = 100;

    /// <summary>
    /// Wave 3 fulfillment (U14): called from the sale/shopping path (<see cref="HeroShoppingSystem"/>)
    /// BEFORE the normal gear-shopping pass for a hero — an accepted commission is a standing request
    /// straight to the smith, so it is checked (and, if satisfiable, bought) ahead of the hero's own
    /// ordinary gear-score shopping for the day. Looks only at the PLAYER'S shelf (a commission is a
    /// forge request, not "whatever the rival happens to stock") for the first item (lowest ItemId —
    /// deterministic) whose slot and quality satisfy the hero's accepted commission. If the hero can
    /// afford list price + the commission's premium, the sale is GUARANTEED — it bypasses the ordinary
    /// <see cref="ShoppingAi"/> verdict gates (veteran-quality, gear-score-must-improve, ...) exactly
    /// as a bespoke commission should — at that guaranteed price, with a mood bump and a
    /// <see cref="CommissionFulfilled"/> beat. Returns null when there is nothing to fulfill (no
    /// accepted commission, no matching shelf item, or the hero can't cover the guaranteed price), so
    /// the caller falls through to the ordinary shopping pass unchanged.
    /// </summary>
    public static GameState? TryFulfillFromShelf(GameState state, Hero hero, IEventSink events)
    {
        var commission = state.Commissions.FirstOrDefault(c => c.Accepted && c.Hero == hero.Id);
        if (commission is null)
        {
            return null;
        }

        var heroClass = ClassRegistry.Require(hero.ClassId);

        ShelfEntry? match = null;
        Item? matchItem = null;
        foreach (var entry in state.Player.Shelf.OrderBy(e => e.Item.Value))
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item) || !Satisfies(commission, item, heroClass))
            {
                continue;
            }

            match = entry;
            matchItem = item;
            break;
        }

        if (match is null || matchItem is null)
        {
            return null;
        }

        // BUG-1 fix (playtest 2026-07-25): fulfilment requires only that the hero can afford LIST
        // price. If they can't also cover the full premium, fulfil anyway at list + whatever premium
        // they can pay — NEVER fall through here, because the ordinary shopping pass would otherwise
        // buy this exact item at plain list, leaking the premium with zero player-facing signal (an
        // accepted commission silently paying no bonus). A hero who can't even afford list waits a
        // later Morning (ordinary shopping can't take it either).
        if (hero.Gold < match.Price)
        {
            return null;
        }

        var premiumPaid = Math.Min(commission.PremiumGold, hero.Gold - match.Price);
        var totalPrice = match.Price + premiumPaid;

        // Consumables are CARRIED, not worn, and getting this wrong is silent and expensive:
        // GearSet has no Consumable field, so WithSlot's default arm returns `this` unchanged —
        // routing a potion through Gear would take the hero's gold, emit CommissionFulfilled, and
        // hand them nothing. Branching on Effect (not on the slot enum) mirrors
        // HeroShoppingSystem.ApplyPurchase and ExpeditionResolver.TryQuaff, both of which key off
        // ConsumableEffect DATA rather than recipe ids or slots.
        var updatedHero = matchItem.Effect is not null
            ? hero with
            {
                Gold = hero.Gold - totalPrice,
                Pack = hero.Pack.Add(matchItem.Id),
            }
            : hero with
            {
                Gold = hero.Gold - totalPrice,
                Gear = hero.Gear.WithSlot(matchItem.Slot, matchItem.Id),
            };

        var next = state with
        {
            Heroes = state.Heroes.SetItem(hero.Id.Value, updatedHero),
            Player = state.Player with
            {
                Gold = state.Player.Gold + totalPrice,
                Shelf = state.Player.Shelf.Remove(match),
            },
            Commissions = state.Commissions.Remove(commission),
        };

        events.Emit(new ItemSold(matchItem.Id, hero.Id, totalPrice, FromPlayerShop: true));
        events.Emit(new CommissionFulfilled(hero.Id, matchItem.Id, premiumPaid));

        return CommissionSystem.BumpMood(next, hero.Id, FulfillMoodBonus);
    }

    /// <summary>
    /// The forge-request match predicate <see cref="TryFulfillFromShelf"/>'s shelf scan uses,
    /// extracted so a reader never re-derives it (P2-PEOPLE-17): slot and quality first, then the
    /// two "can this hero physically use it" facts a bespoke commission still enforces even though it
    /// bypasses <see cref="ShoppingAi"/>'s ordinary preference gates (U-T1-11, found while wiring
    /// <c>BaselinePlayer</c> to accept commissions) — a Shield never reaches a shield-incapable class
    /// through ordinary shopping (<see cref="ShoppingAi.EvaluateItem"/>'s own first check), so it
    /// must not reach one through a commission either, and the same goes for a class's per-slot
    /// weight cap.
    /// </summary>
    public static bool Satisfies(Commission commission, Item item, ClassDefinition heroClass)
    {
        if (item.Slot != commission.Slot || item.Quality < commission.MinQuality)
        {
            return false;
        }

        if (item.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
        {
            return false;
        }

        if (heroClass.MaxItemWeight is { } cap && item.Stats.Weight > cap)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// P2-PEOPLE-17: which ACCEPTED commission <paramref name="item"/> would fill if stocked, and
    /// who shops ahead of that hero in the Morning queue right now — the fact decision 1 ("sell the
    /// good one, or hold it for the hero who needs it") is otherwise made blind: a piece stocked to
    /// fill one hero's ask can be bought out from under them by an earlier hero's ORDINARY shopping
    /// before that hero's own commission check ever runs (<see cref="HeroShoppingSystem.ShopOnce"/>
    /// checks <see cref="TryFulfillFromShelf"/> first, but only on that hero's OWN turn).
    ///
    /// <para>Only ACCEPTED commissions are considered: an open (not yet accepted) commission carries
    /// no guaranteed sale or premium — <see cref="TryFulfillFromShelf"/> never touches one either —
    /// so naming one here would promise a sale the sim has not committed to. Walked in
    /// <see cref="HeroShoppingSystem.MorningShoppingOrder"/>'s own order (never re-sorted here): the
    /// first hero in that real order whose accepted commission <paramref name="item"/> satisfies is
    /// the one who would actually receive it, because every hero ahead of them takes their own turn
    /// — commission check, then ordinary shopping — first and can take the shelf slot before this
    /// hero's turn ever arrives. Returns null when no accepted commission matches: no ask, no line.</para>
    ///
    /// <para>This is a fact about queue POSITION, never a suggestion about pricing or holding the
    /// piece — law 12, "influence never orders": the caller renders the fact and stops there.</para>
    /// </summary>
    public static CommissionQueueForecast? ForecastQueueFor(GameState state, Item item)
    {
        var aheadInQueue = ImmutableArray.CreateBuilder<HeroId>();
        foreach (var heroIdValue in HeroShoppingSystem.MorningShoppingOrder(state))
        {
            var heroId = new HeroId(heroIdValue);
            var commission = state.Commissions.FirstOrDefault(c => c.Accepted && c.Hero == heroId);
            if (commission is null)
            {
                aheadInQueue.Add(heroId);
                continue;
            }

            if (!state.Heroes.TryGetValue(heroIdValue, out var hero)
                || !Satisfies(commission, item, ClassRegistry.Require(hero.ClassId)))
            {
                aheadInQueue.Add(heroId);
                continue;
            }

            return new CommissionQueueForecast(commission, aheadInQueue.ToImmutable());
        }

        return null;
    }
}
