using GameSim.Contracts;
using GameSim.Factions;
using GameSim.Heroes;

namespace GameSim.Economy;

/// <summary>
/// The Evening ore market (R6, the flywheel's buyback half): the player buys
/// floor-scaled ore from returning heroes' open offers.
///
/// Boundary (U8 owns the reveal): <see cref="OreOffered"/> events and
/// <see cref="GameState.OpenOreOffers"/> are POPULATED by U8's Evening pipeline from
/// expedition loot; this handler only CONSUMES them. It never reads
/// <c>PendingExpeditions</c>.
///
/// Gold conservation (R17): a purchase is an exact move — player gold down by
/// quantity * unit price, hero gold up by the same amount (via
/// <see cref="HeroOps.ApplyLootIncome"/>, feeding tomorrow's shopping budget),
/// player materials up by quantity, offer reduced or removed. Nothing minted,
/// nothing burned. GoldConservationTests asserts this as a property.
///
/// Phase legality: Evening ONLY — ore changes hands when heroes are back in town.
/// Offer matching: the FIRST offer matching (From, MaterialKey) in list order is the
/// one traded against; a single purchase never spans multiple offers (buy twice
/// instead). Check order is fixed (quantity sanity, offer lookup, hero, offered
/// quantity, gold) so rejection reasons are stable. No RNG; every rejection happens
/// before any state change. No events: the offer event already exists in the log —
/// the purchase itself is pure state transfer recorded by the action log.
/// </summary>
public sealed class OreMarketHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is BuyOreAction && phase == DayPhase.Evening;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not BuyOreAction buy)
        {
            return (state, new RejectedAction(action, $"OreMarketHandlers cannot apply {action.GetType().Name}."));
        }

        // 1. Quantity must be positive.
        if (buy.Quantity <= 0)
        {
            return (state, new RejectedAction(action, $"Quantity must be positive; got {buy.Quantity}."));
        }

        // 2. A matching open offer must exist (first match in list order).
        var index = state.OpenOreOffers.FindIndex(o => o.From == buy.From && o.MaterialKey == buy.MaterialKey);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No open ore offer of '{buy.MaterialKey}' from {buy.From}."));
        }

        var offer = state.OpenOreOffers[index];

        // 3. The selling hero must exist and be alive — gold never flows to a corpse
        //    (a stale offer can outlive its hero between evenings).
        if (!state.Heroes.TryGetValue(buy.From.Value, out var hero))
        {
            return (state, new RejectedAction(action, $"No such hero {buy.From}."));
        }

        if (!hero.Alive)
        {
            return (state, new RejectedAction(action, $"{hero.Name} ({buy.From}) is no longer alive; the offer is void."));
        }

        // 4. Cannot buy more than is offered (also bounds the cost arithmetic).
        if (buy.Quantity > offer.Quantity)
        {
            return (state, new RejectedAction(action, $"Only {offer.Quantity} {offer.MaterialKey} offered; asked for {buy.Quantity}."));
        }

        // 5. The player must be able to pay.
        var cost = buy.Quantity * offer.UnitPrice;
        if (state.Player.Gold < cost)
        {
            return (state, new RejectedAction(action, $"Not enough gold: need {cost}, have {state.Player.Gold}."));
        }

        // All checks passed — the exact move.
        var have = state.Player.Materials.TryGetValue(buy.MaterialKey, out var stock) ? stock : 0;
        var remaining = offer.Quantity - buy.Quantity;
        var newPlayer = state.Player with
        {
            Gold = state.Player.Gold - cost,
            Materials = state.Player.Materials.SetItem(buy.MaterialKey, have + buy.Quantity),
        };

        // P5 U2: a successful ore purchase RAISES the supplying faction's standing (R5/KTD6), in
        // the SAME state update as the buy. Discount-only core (KTD8): standing only rises here —
        // it never falls (that is the Morning FactionDriftSystem) — so we clamp to +StandingCap.
        // No tariff yet (U3 prices, priced-before-rise); U2 adds only the rise. Pure integer, no RNG.
        var faction = FactionRegistry.ByOreKey(buy.MaterialKey);
        if (faction is not null)
        {
            var raised = Math.Min(newPlayer.StandingFor(faction.Id) + faction.RiseStep, faction.StandingCap);
            newPlayer = newPlayer.WithStanding(faction.Id, raised);
        }

        var newState = state with
        {
            Player = newPlayer,
            Heroes = state.Heroes.SetItem(hero.Id.Value, HeroOps.ApplyLootIncome(hero, cost)),
            OpenOreOffers = remaining == 0
                ? state.OpenOreOffers.RemoveAt(index)
                : state.OpenOreOffers.SetItem(index, offer with { Quantity = remaining }),
        };
        return (newState, null);
    }
}
