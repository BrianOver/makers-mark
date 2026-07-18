using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Expedition;

/// <summary>
/// The Camp-phase decision verbs (staged resolution, verdict §5 step 5): <see cref="SendSupplyAction"/>
/// and <see cref="RecallPartyAction"/>, legal ONLY during <see cref="DayPhase.Camp"/> against a live
/// matching <see cref="InFlightExpedition"/>. Send pays the winch-house runner to drop one HELD
/// consumable to the FRONT of a camped hero's pack (the resolver quaffs front-first, so the delivery
/// drinks before anything the hero already carried); Recall rings the bell so the Deep tick banks and
/// surfaces without rolling deeper floors.
///
/// Determinism (KTD4): draws NO RNG — the fee is a formula and every insertion is deterministic, so
/// the Camp tick leaves the kernel stream position untouched (pinned by CampHandlersTests). Every
/// rejection happens before any state change and carries a typed reason — never a silent drop.
///
/// Economy (KTD3): the runner's fee is a town-gold SINK recorded on <see cref="SupplyDelivered.Fee"/>
/// so the gold-conservation invariant reconciles against it (TariffApplied precedent —
/// GoldConservationTests).
/// </summary>
public sealed class CampHandlers : IActionHandler
{
    // The runner's fee (integer). Kill-risk-1 tuning knobs (D5): at the v1 floor-1 camp the fee is
    // 9g — deliberately priced just ABOVE the pinned 8g salve sale price
    // (SalveProvisioningBalanceTests.SalvePrice), so sending a salve always costs more than selling
    // one: the rationing tension the camp window exists to create.
    internal const int SupplyFeeBase = 6;
    internal const int SupplyFeePerFloor = 3;

    /// <summary>The runner's charge to reach a party camped below <paramref name="checkpointFloor"/>.</summary>
    internal static int SupplyFee(int checkpointFloor) => SupplyFeeBase + SupplyFeePerFloor * checkpointFloor;

    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        (action is SendSupplyAction or RecallPartyAction) && phase == DayPhase.Camp;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            SendSupplyAction send => ApplySend(state, send, events),
            RecallPartyAction recall => ApplyRecall(state, recall, events),
            _ => (state, new RejectedAction(action, $"CampHandlers cannot apply {action.GetType().Name}.")),
        };

    /// <summary>
    /// SendSupply: validate in the fixed D5 order (each failure a distinct typed reason), then charge
    /// the fee and front-insert the item onto BOTH the hero's persistent pack (so the Evening
    /// pack-depletion reconciles and an undrunk delivery stays with the hero) AND the parked working
    /// pack stage 2 actually quaffs from.
    /// </summary>
    private static (GameState, RejectedAction?) ApplySend(GameState state, SendSupplyAction action, IEventSink events)
    {
        // 1. A party must be camped with this hero.
        var index = state.InFlight.FindIndex(f => f.Party.Contains(action.To));
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No party is camped with {action.To}."));
        }

        var inFlight = state.InFlight[index];

        // 2. Defensive: a hero who fell in stage 1 is unreachable (unreachable under the v1 park
        //    invariant — a parked party has no dead — but the reason is typed, never a silent pass).
        if (inFlight.Dead.Contains(action.To.Value))
        {
            return (state, new RejectedAction(action, $"{action.To} fell below — the runner can't reach them."));
        }

        // 3. The recall bell short-circuits a send: the runner won't chase a surfacing party.
        if (inFlight.Recalled)
        {
            return (state, new RejectedAction(action, "The recall bell has rung — the runner won't chase them."));
        }

        // 4. One runner per party per day.
        if (inFlight.SupplySent)
        {
            return (state, new RejectedAction(action, "One runner per party per day — this party's delivery is spent."));
        }

        // 5. The item must exist in the world.
        if (!state.Items.TryGetValue(action.Item.Value, out var item))
        {
            return (state, new RejectedAction(action, $"No such item {action.Item}."));
        }

        // 6. The runner carries consumables only.
        if (item.Effect is null)
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) isn't a consumable — the runner carries consumables only."));
        }

        // 7. Ownership — the item must be in the player's own hands: a marked craft, not shelved, not
        //    on the rival's shelf, not already in a hero's pack. (Each a distinct typed reason.)
        if (!item.PlayerCrafted)
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) isn't your craft to send."));
        }

        if (state.Player.Shelf.Any(e => e.Item == action.Item))
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is shelved — unstock it first."));
        }

        if (state.RivalShelf.Any(e => e.Item == action.Item))
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is on the rival's shelf, not in your hands."));
        }

        if (state.Heroes.Values.Any(h => h.Pack.Contains(action.Item)))
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is already in a hero's pack."));
        }

        // 8. The runner's fee.
        var fee = SupplyFee(inFlight.CheckpointFloor);
        if (state.Player.Gold < fee)
        {
            return (state, new RejectedAction(action, $"Can't pay the {fee}g runner — you have {state.Player.Gold}g."));
        }

        // Apply. Front-insert onto the working pack stage 2 quaffs from (what makes the delivery drink
        // first) AND onto the hero's persistent pack (so the Evening Pack.Remove depletion reconciles
        // and an undrunk delivery carries with the hero). The fee is a recorded gold sink (KTD3).
        var hero = state.Heroes[action.To.Value];
        var working = inFlight.Packs.TryGetValue(action.To.Value, out var packed) ? packed : ImmutableList<ItemId>.Empty;
        var newInFlight = inFlight with
        {
            Packs = inFlight.Packs.SetItem(action.To.Value, working.Insert(0, action.Item)),
            SupplySent = true,
        };

        var newState = state with
        {
            Player = state.Player with { Gold = state.Player.Gold - fee },
            Heroes = state.Heroes.SetItem(action.To.Value, hero with { Pack = hero.Pack.Insert(0, action.Item) }),
            InFlight = state.InFlight.SetItem(index, newInFlight),
        };

        events.Emit(new SupplyDelivered(action.To, action.Item, fee));
        return (newState, null);
    }

    /// <summary>
    /// Recall: ring the bell for the party containing <see cref="RecallPartyAction.Member"/>. The Deep
    /// tick banks stage-1 clears/ore and surfaces without rolling deeper floors (v1 bank-and-surface —
    /// the unfulfilled bounty refunds via the verified expiry path; no new bounty mechanics).
    /// </summary>
    private static (GameState, RejectedAction?) ApplyRecall(GameState state, RecallPartyAction action, IEventSink events)
    {
        var index = state.InFlight.FindIndex(f => f.Party.Contains(action.Member));
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No party is camped with {action.Member}."));
        }

        var inFlight = state.InFlight[index];
        if (inFlight.Recalled)
        {
            return (state, new RejectedAction(action, "The recall bell has already rung for this party."));
        }

        var newState = state with
        {
            InFlight = state.InFlight.SetItem(index, inFlight with { Recalled = true }),
        };

        events.Emit(new PartyRecalled(inFlight.Party));
        return (newState, null);
    }
}
