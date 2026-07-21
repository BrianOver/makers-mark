using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Counter;

/// <summary>
/// Morning stepped-queue resolution (PKD5/PKD6). Runs AFTER every action in the tick's batch has
/// been applied (systems pass, step 2 of <c>GameKernel.Tick</c>) — so a batch that presents an item
/// resolves that customer the SAME tick it was shown.
///
/// PA3 ships the MINIMAL deterministic placeholder resolution the plan calls for (PA4 replaces it
/// with real willingness-band haggle economics): a presented item resolves via the EXISTING
/// <see cref="ShoppingAi.EvaluateItem"/> verdict at the shelf's list price — Buy closes the sale
/// (<see cref="CounterSaleClosed"/>, unpinned), anything else walks with that verdict's legible
/// reason (<see cref="CustomerWalked"/>). Draws ZERO RNG — the verdict is pure integer math, and
/// dequeuing/closing is pure bookkeeping (PA3 hard constraint).
///
/// A tick with nothing presented does nothing here: the active customer simply keeps waiting
/// (the phase-hold in <c>GameKernel.Advance</c> is what lets the player take as many ticks as they
/// like to arrange, suggest, or haggle before presenting — PA4 wires real per-round consequence
/// for those other actions; PA3 keeps them legal no-ops so the wrong-phase/wrong-state rejection
/// contract is uniform across all five counter actions).
/// </summary>
public sealed class CounterQueueSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "counter-queue";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var counter = state.Counter;
        if (counter is null || counter.Active is not { } activeId || counter.Presented is not { } presentedId)
        {
            return state; // no session, nobody at the counter, or nothing presented yet this tick
        }

        if (!state.Heroes.TryGetValue(activeId.Value, out var hero)
            || !state.Items.TryGetValue(presentedId.Value, out var item))
        {
            // Defensive: the hero or item vanished between Present and resolution (should not
            // happen in-phase, but never crash the morning). Drop the stale present; the customer
            // stays active for the next step.
            return state with { Counter = counter with { Presented = null } };
        }

        var shelfEntry = state.Player.Shelf.FirstOrDefault(e => e.Item == presentedId);
        if (shelfEntry is null)
        {
            // The item left the shelf between Present and resolution (e.g. unstocked this same
            // tick) — same defensive drop, no resolution.
            return state with { Counter = counter with { Presented = null } };
        }

        var verdict = ShoppingAi.EvaluateItem(hero, item, shelfEntry.Price, state.Items);
        var resolvedState = verdict.Kind == ShoppingVerdictKind.Buy
            ? ApplySale(state, hero, item, shelfEntry, events)
            : Walk(state, hero, item, verdict, events);

        return Advance(resolvedState, counter, activeId, events);
    }

    /// <summary>Dequeues the just-resolved customer, promotes the next queue head to Active
    /// (emitting <see cref="CustomerApproached"/> unless the session is already closing), and marks
    /// the session Closed once the queue runs dry — the trigger for the atomic fallback pass
    /// (<see cref="Heroes.HeroShoppingSystem"/>).</summary>
    private static GameState Advance(GameState state, CounterState counter, HeroId resolvedHero, IEventSink events)
    {
        var nextQueue = counter.Queue.Count > 0 && counter.Queue[0] == resolvedHero
            ? counter.Queue.RemoveAt(0)
            : counter.Queue.Remove(resolvedHero); // defensive: head invariant should always hold
        var nextActive = nextQueue.Count > 0 ? nextQueue[0] : (HeroId?)null;
        var closed = counter.Closed || nextActive is null;

        if (nextActive is { } approaching && !counter.Closed)
        {
            events.Emit(new CustomerApproached(approaching));
        }

        return state with
        {
            Counter = counter with
            {
                Queue = nextQueue,
                Active = nextActive,
                Presented = null,
                StandingOfferGold = null,
                Served = counter.Served.Add(resolvedHero.Value),
                Closed = closed,
            },
        };
    }

    /// <summary>Placeholder buy resolution (PA4 replaces with real haggle pricing): the sale closes
    /// at the shelf's list price, unpinned. Mirrors <see cref="Heroes.HeroShoppingSystem"/>'s
    /// purchase application — gold moves exactly (conservation), gear equips / consumables pack.</summary>
    private static GameState ApplySale(GameState state, Hero hero, Item item, ShelfEntry shelfEntry, IEventSink events)
    {
        var updatedHero = item.Effect is not null
            ? hero with { Gold = hero.Gold - shelfEntry.Price, Pack = hero.Pack.Add(item.Id) }
            : hero with { Gold = hero.Gold - shelfEntry.Price, Gear = hero.Gear.WithSlot(item.Slot, item.Id) };

        var newState = state with
        {
            Heroes = state.Heroes.SetItem(hero.Id.Value, updatedHero),
            Player = state.Player with
            {
                Gold = state.Player.Gold + shelfEntry.Price,
                Shelf = state.Player.Shelf.Remove(shelfEntry),
            },
        };

        events.Emit(new CounterSaleClosed(hero.Id, item.Id, shelfEntry.Price, Pinned: false));
        return newState;
    }

    private static GameState Walk(GameState state, Hero hero, Item item, ShoppingVerdict verdict, IEventSink events)
    {
        events.Emit(new CustomerWalked(hero.Id, item.Id, verdict.Reason));
        return state;
    }
}
