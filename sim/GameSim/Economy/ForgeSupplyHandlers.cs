using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 3a — "coal + flux consumables"): the player's
/// <see cref="BuyForgeSupplyAction"/> handler, a standing Morning supplier alongside the existing
/// <see cref="MaterialVendorHandlers"/> base-material vendor (same shape, deliberately, per this
/// unit's brief: "reuse the BuyMaterialAction action/handler pattern"). Coal is a cheap, flat,
/// repeatable buy (keeps the forge running — the plan's "repeatable per-craft" sink); flux is the
/// rarer, premium consumable <see cref="MasterworkAttemptAction"/> spends attempting a guaranteed
/// high-quality forging.
///
/// Coal/flux stock rides the SAME generic <see cref="PlayerState.Materials"/> dictionary ordinary
/// ore does (no Contracts change — see <see cref="ForgeTierHandlers"/>'s class doc for why); they
/// are deliberately NOT added to <see cref="Materials.MaterialRegistry.PricedPool"/> (that pool is
/// explicitly frozen — expanding it is flagged there as its own determinism-gated re-baseline, out
/// of this unit's scope), so this handler prices them itself instead of routing through
/// <see cref="MaterialVendorHandlers"/>.
///
/// Determinism: integer-only, no RNG, no wall clock. Reuses the existing <see cref="MaterialPurchased"/>
/// sink event (an honest fit — this genuinely is "a keyed good bought for gold", the same shape the
/// event already models) rather than adding a new Contracts event type.
/// </summary>
public sealed class ForgeSupplyHandlers : IActionHandler
{
    public const string Coal = "coal";
    public const string Flux = "flux";

    /// <summary>Flat unit price, gold per unit. Coal is cheap and disposable; flux is the rare premium
    /// consumable (a full masterwork attempt spends <see cref="MasterworkAttemptHandlers.FluxCost"/> of
    /// it), priced accordingly. Public (not private) so <see cref="Advisor.ActionLegality"/>'s mirror
    /// reuses this SAME pricing formula instead of duplicating the magic numbers (U4 — the same
    /// "call the one shared formula" precedent as <see cref="MaterialVendorHandlers.QuoteCost"/>).
    /// Returns -1 (a "not stocked" sentinel) for any key that isn't <see cref="Coal"/> or
    /// <see cref="Flux"/>.</summary>
    public static int UnitPrice(string supplyKey) => supplyKey switch
    {
        Coal => 4,
        Flux => 40,
        _ => -1, // sentinel: "not stocked" (checked before use)
    };

    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is BuyForgeSupplyAction && phase == DayPhase.Morning;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not BuyForgeSupplyAction buy)
        {
            return (state, new RejectedAction(action, $"ForgeSupplyHandlers cannot apply {action.GetType().Name}."));
        }

        // 1. Quantity must be positive.
        if (buy.Quantity <= 0)
        {
            return (state, new RejectedAction(action, $"Quantity must be positive; got {buy.Quantity}."));
        }

        // 2. Only coal/flux are stocked.
        var unitPrice = UnitPrice(buy.SupplyKey);
        if (unitPrice < 0)
        {
            return (state, new RejectedAction(action, $"The forge supplier does not stock '{buy.SupplyKey}'."));
        }

        // 3. Price the line (flat, no markup curve — the plan's "repeatable" sink stays predictable).
        var cost = buy.Quantity * unitPrice;

        // 4. The player must be able to pay.
        if (cost > state.Player.Gold)
        {
            return (state, new RejectedAction(action, $"Not enough gold: need {cost}, have {state.Player.Gold}."));
        }

        // 5. Day action-budget gate — checked LAST, like every other real-work handler.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        var have = state.Player.Materials.TryGetValue(buy.SupplyKey, out var stock) ? stock : 0;
        var newState = state with
        {
            Player = state.Player with
            {
                Gold = state.Player.Gold - cost,
                Materials = state.Player.Materials.SetItem(buy.SupplyKey, have + buy.Quantity),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        events.Emit(new MaterialPurchased(buy.SupplyKey, buy.Quantity, cost));

        return (newState, null);
    }
}
