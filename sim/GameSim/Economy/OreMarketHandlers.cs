using GameSim.Contracts;
using GameSim.Factions;
using GameSim.Heroes;
using GameSim.Kernel;

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
/// Gold conservation (R17, extended by P5 U3/KTD3): the hero always receives the base ask (quantity *
/// unit price) via <see cref="HeroOps.ApplyLootIncome"/>, feeding tomorrow's shopping budget; the
/// player pays a faction-standing-tariffed cost (may differ from base — see below); player materials
/// up by quantity, offer reduced or removed. The signed difference (playerCost − base) is a
/// faction sink/source, recorded as a <see cref="TariffApplied"/> event; GoldConservationTests
/// asserts the extended invariant (town total moves by exactly minus rival sales minus Σ tariff delta).
///
/// Phase legality: Evening ONLY — ore changes hands when heroes are back in town.
/// Offer matching: the FIRST offer matching (From, MaterialKey) in list order is the
/// one traded against; a single purchase never spans multiple offers (buy twice
/// instead). Check order is fixed (quantity sanity, offer lookup, hero, offered
/// quantity, gold) so rejection reasons are stable. No RNG; every rejection happens
/// before any state change. The only event a purchase emits is the P5 U3 <see cref="TariffApplied"/>
/// delta when the standing tariff moves the price (KTD3); the offer event already exists in the log
/// and the transfer itself is recorded by the action log.
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

        // 5. Price the purchase (P5 U3, R7/R8/KTD3/KTD4/KTD6/KTD8). The hero ALWAYS receives the
        //    base ask; the supplying faction's standing-AT-START (before this buy's rise, KTD6)
        //    scales only what the PLAYER pays, on the AGGREGATE line cost — never per-unit (KTD4:
        //    per-unit rounds a cheap-ore nudge to zero). Positive standing → positive adjustment →
        //    the player pays less (discount); negative standing (unreachable in this discount-only
        //    core, KTD8, but computed for the dormant surcharge branch) → the player pays more. adj
        //    is clamped to ±MaxAdjustmentPerMille so the tariff stays a bounded nudge (R8).
        var baseLineCost = buy.Quantity * offer.UnitPrice;
        var faction = FactionRegistry.ByOreKey(buy.MaterialKey);
        var playerCost = baseLineCost;
        if (faction is not null)
        {
            var adjPerMille = TariffAdjustmentPerMille(state.Player.StandingFor(faction.Id), faction);
            playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);
        }

        // 6. The player must be able to pay the TARIFFED cost (what they actually hand over).
        if (state.Player.Gold < playerCost)
        {
            return (state, new RejectedAction(action, $"Not enough gold: need {playerCost}, have {state.Player.Gold}."));
        }

        // All checks passed — the exact move. Player gold down by the tariffed playerCost; hero gold
        // up by the base ask (KTD3, unchanged); materials up; offer reduced or removed.
        var have = state.Player.Materials.TryGetValue(buy.MaterialKey, out var stock) ? stock : 0;
        var remaining = offer.Quantity - buy.Quantity;
        var newPlayer = state.Player with
        {
            Gold = state.Player.Gold - playerCost,
            Materials = state.Player.Materials.SetItem(buy.MaterialKey, have + buy.Quantity),
        };

        // P5 U2: a successful ore purchase RAISES the supplying faction's standing (R5/KTD6), AFTER
        // pricing (priced-before-rise, KTD6 — the earned standing discounts only SUBSEQUENT buys).
        // Discount-only core (KTD8): standing only rises here — it never falls (that is the Morning
        // FactionDriftSystem) — so we clamp to +StandingCap. Pure integer, no RNG.
        if (faction is not null)
        {
            var raised = Math.Min(newPlayer.StandingFor(faction.Id) + faction.RiseStep, faction.StandingCap);
            newPlayer = newPlayer.WithStanding(faction.Id, raised);
        }

        var newState = state with
        {
            Player = newPlayer,
            Heroes = state.Heroes.SetItem(hero.Id.Value, HeroOps.ApplyLootIncome(hero, baseLineCost)),
            OpenOreOffers = remaining == 0
                ? state.OpenOreOffers.RemoveAt(index)
                : state.OpenOreOffers.SetItem(index, offer with { Quantity = remaining }),
        };

        // Record the tariff delta (MANDATORY, KTD3) — the faction sink(+)/source(−) the gold-
        // conservation invariant reconciles against. Emit only when a faction supplies the ore AND
        // the tariff moved the price (delta != 0): a neutral-standing buy is byte-identical to the
        // pre-tariff behavior and leaves the log clean.
        if (faction is not null)
        {
            var delta = playerCost - baseLineCost;
            if (delta != 0)
            {
                events.Emit(new TariffApplied(faction.Id, buy.MaterialKey, baseLineCost, playerCost, delta));
            }
        }

        return (newState, null);
    }

    /// <summary>
    /// The per-mille ore-price adjustment for a standing (R7/R8/KTD4): standing is mapped linearly
    /// onto the faction's <see cref="FactionDefinition.MaxAdjustmentPerMille"/> at its
    /// <see cref="FactionDefinition.StandingCap"/> via round-to-nearest <see cref="IntegerCurves.MulDiv"/>,
    /// then clamped to ±MaxAdjustmentPerMille. Positive standing → positive adjustment (the caller
    /// subtracts it from 1000 → cheaper). The clamp bounds the tariff even if a standing ever exceeds
    /// the cap (defensive; gameplay clamps standing to the cap). Pure integer, no RNG.
    /// </summary>
    private static long TariffAdjustmentPerMille(int standing, FactionDefinition faction)
    {
        long max = faction.MaxAdjustmentPerMille;
        var raw = IntegerCurves.MulDiv(standing, faction.MaxAdjustmentPerMille, faction.StandingCap);
        return Math.Clamp(raw, -max, max);
    }
}
