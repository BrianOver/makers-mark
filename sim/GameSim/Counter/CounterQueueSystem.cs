using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Counter;

/// <summary>
/// Morning stepped-queue resolution (PKD5/PKD6). Runs AFTER every action in the tick's batch has
/// been applied (systems pass, step 2 of <c>GameKernel.Tick</c>) — so a batch that presents an item
/// resolves that customer's role-fit verdict the SAME tick it was shown.
///
/// PA4 (plan 2026-07-21-002) replaces PA3's placeholder ("present a strict upgrade → instant sale
/// at list price") with the real haggle economics: a presented item that fails
/// <see cref="ShoppingAi.EvaluateItem"/> (role mismatch, too heavy, unaffordable, not an upgrade)
/// still walks immediately — there is no round to negotiate over an item the hero doesn't want.
/// A presented item that PASSES that verdict no longer closes instantly; it OPENS a haggle round
/// (<see cref="HaggleResolver.OpenRound"/>) with a standing offer the player then Accepts, HoldFirms,
/// or Counters via <see cref="HaggleResponseAction"/> (resolved synchronously in
/// <see cref="CounterHandlers"/> — see that file's remarks for why haggle resolution does NOT defer
/// to this system). Draws ZERO RNG throughout (PA4 hard constraint).
///
/// A tick with nothing NEWLY presented does nothing here: once a round is open
/// (<see cref="CounterState.Round"/> &gt; 0) this system is a no-op for that customer — the
/// haggle handler owns every subsequent step until the sale closes or the customer walks.
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

        if (counter.Round > 0)
        {
            return state; // a round is already open for this presentment — the haggle handler owns it now
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

        var heroClass = ClassRegistry.Require(hero.ClassId);
        var verdict = ShoppingAi.EvaluateItem(hero, heroClass, item, shelfEntry.Price, state.Items);

        if (verdict.Kind != ShoppingVerdictKind.Buy)
        {
            var walkedState = Walk(state, hero, item, verdict, events);
            return Advance(walkedState, counter, activeId, events);
        }

        var openedCounter = HaggleResolver.OpenRound(counter, hero, heroClass, item, shelfEntry.Price, events);
        return state with { Counter = openedCounter };
    }

    /// <summary>Dequeues the just-resolved customer, promotes the next queue head to Active
    /// (resetting the per-customer meters — Round/Interest/Patience — and emitting
    /// <see cref="CustomerApproached"/> unless the session is already closing), and marks the
    /// session Closed once the queue runs dry — the trigger for the atomic fallback pass
    /// (<see cref="Heroes.HeroShoppingSystem"/>). <see cref="CounterState.GoodwillPermille"/> is
    /// NOT reset here — it is a whole-session fleece memory (PA4), reset only by
    /// <see cref="CounterHandlers"/>'s <c>OpenCounterAction</c> handling.
    /// Internal (not private): <see cref="HaggleResolver"/> calls this to advance the queue after
    /// a haggle response resolves a sale or a walk — the same dequeue logic either path takes.</summary>
    internal static GameState Advance(GameState state, CounterState counter, HeroId resolvedHero, IEventSink events)
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

        var promoted = PromoteActive(counter, nextQueue, nextActive);

        return state with
        {
            Counter = promoted with
            {
                Served = counter.Served.Add(resolvedHero.Value),
                Closed = closed,
            },
        };
    }

    /// <summary>Resets the per-customer meters (Round, Interest, Patience, Presented, standing
    /// offer) for a newly-active customer (or for the very first customer at
    /// <see cref="CounterHandlers"/>'s <c>OpenCounterAction</c> handling) — everything except
    /// <see cref="CounterState.GoodwillPermille"/>, which is session-wide.</summary>
    internal static CounterState PromoteActive(CounterState counter, ImmutableList<HeroId> queue, HeroId? active) =>
        counter with
        {
            Queue = queue,
            Active = active,
            Round = 0,
            InterestPermille = 0,
            PatienceRounds = active is not null ? WillingnessModel.InitialPatienceRounds : 0,
            Presented = null,
            StandingOfferGold = null,
        };

    private static GameState Walk(GameState state, Hero hero, Item item, ShoppingVerdict verdict, IEventSink events)
    {
        events.Emit(new CustomerWalked(hero.Id, item.Id, verdict.Reason));
        return state;
    }
}
