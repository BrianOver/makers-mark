using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GameSim.Heroes;

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

        ShelfEntry? match = null;
        Item? matchItem = null;
        foreach (var entry in state.Player.Shelf.OrderBy(e => e.Item.Value))
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                continue;
            }

            if (item.Slot != commission.Slot || item.Quality < commission.MinQuality)
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

        var totalPrice = match.Price + commission.PremiumGold;
        if (hero.Gold < totalPrice)
        {
            return null; // can't yet afford the guaranteed price — try again a later Morning
        }

        var updatedHero = hero with
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
        events.Emit(new CommissionFulfilled(hero.Id, matchItem.Id, commission.PremiumGold));

        return CommissionSystem.BumpMood(next, hero.Id, FulfillMoodBonus);
    }
}
