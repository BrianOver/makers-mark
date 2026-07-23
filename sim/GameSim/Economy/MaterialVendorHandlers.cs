using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Materials;

namespace GameSim.Economy;

/// <summary>
/// The Morning materials vendor (Playable Core R2/R3, KD2): the always-available supply
/// floor. Any selected profession can buy base materials at a standard marked-up price
/// every Morning, so the craft loop is reachable on day 1 without waiting on hero raids.
///
/// Pricing: <c>cost = ceilDiv(quantity * MaterialRegistry.UnitPrice(key) * (1000 + VendorMarkupPermille), 1000)</c>.
/// Ceiling division so a single unit still carries the markup (1 copper at +250‰ = ceil(3.75) = 4g),
/// which keeps the vendor strictly pricier than base — returning heroes' Evening ore offers
/// (<see cref="OreMarketHandlers"/>, base ask ± faction tariff) stay the cheaper, exotic upside layer.
///
/// Gold conservation (KTD3, <see cref="TariffApplied"/>/<see cref="SupplyDelivered"/> precedent):
/// the vendor's purse is unmodelled — the cost is a recorded gold SINK, stamped as
/// <see cref="MaterialPurchased"/>, which the conservation invariant reconciles against.
///
/// Phase legality: Morning ONLY (the vendor opens with the town; hero offers own the Evening).
/// Sells only the <see cref="MaterialRegistry.PricedPool"/>; inert/unknown keys are rejected.
/// Check order is fixed (quantity sanity, priced-pool membership, gold) so rejection reasons are
/// stable. No RNG; every rejection happens before any state change — inserting this handler leaves
/// the kernel RNG stream, golden replay, and every existing seed's world byte-identical.
/// </summary>
public sealed class MaterialVendorHandlers : IActionHandler
{
    /// <summary>Vendor markup over base unit price, per-mille (+250‰ = +25%). Pinned by
    /// MaterialVendorHandlersTests; flagged for balance confirmation (plan 005 KTD-C).</summary>
    public const int VendorMarkupPermille = 250;

    /// <summary>
    /// The vendor's aggregate line quote — the ONE pricing formula (class doc): ceiling
    /// division keeps the markup alive on a single unit. Shared by the handler, the
    /// ForgePanel display quote, and <see cref="DestitutionRecoverySystem"/>'s
    /// cheapest-path-to-a-craft arithmetic, so the three can never drift.
    /// </summary>
    public static int QuoteCost(string materialKey, int quantity)
    {
        long baseLine = (long)quantity * Materials.MaterialRegistry.UnitPrice(materialKey);
        return (int)((baseLine * (1000 + VendorMarkupPermille) + 999) / 1000);
    }

    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is BuyMaterialAction && phase == DayPhase.Morning;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not BuyMaterialAction buy)
        {
            return (state, new RejectedAction(action, $"MaterialVendorHandlers cannot apply {action.GetType().Name}."));
        }

        // 1. Quantity must be positive.
        if (buy.Quantity <= 0)
        {
            return (state, new RejectedAction(action, $"Quantity must be positive; got {buy.Quantity}."));
        }

        // 2. The vendor sells only the priced pool (KTD-C: all keys, every profession's base
        //    materials — but never inert/unregistered ones).
        if (!MaterialRegistry.IsPriced(buy.MaterialKey))
        {
            return (state, new RejectedAction(action, $"The vendor does not sell '{buy.MaterialKey}'."));
        }

        // 3. Price the line via the shared quote (see QuoteCost — the one pricing formula).
        var cost = QuoteCost(buy.MaterialKey, buy.Quantity);

        // 4. The player must be able to pay.
        if (cost > state.Player.Gold)
        {
            return (state, new RejectedAction(action, $"Not enough gold: need {cost}, have {state.Player.Gold}."));
        }

        // 5. Day action-budget gate (Game-Feel Plan G3): restocking is real work — checked LAST,
        //    after every economic precondition, so existing rejection reasons stay byte-identical
        //    on a day with slots to spare; only a legal buy with zero slots left is newly refused.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        // All checks passed — the exact move: gold down by cost, materials up by quantity,
        // stamped sink event. No RNG, no other state touched.
        var have = state.Player.Materials.TryGetValue(buy.MaterialKey, out var stock) ? stock : 0;
        var newState = state with
        {
            Player = state.Player with
            {
                Gold = state.Player.Gold - cost,
                Materials = state.Player.Materials.SetItem(buy.MaterialKey, have + buy.Quantity),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        events.Emit(new MaterialPurchased(buy.MaterialKey, buy.Quantity, cost));

        return (newState, null);
    }
}
