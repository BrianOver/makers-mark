using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Counter;

/// <summary>
/// Counter-service action handling (PKD5/PKD6): validates and records intent for
/// <see cref="OpenCounterAction"/>/<see cref="PresentItemAction"/>/<see cref="SuggestItemAction"/>/
/// <see cref="HaggleResponseAction"/>/<see cref="CloseCounterAction"/>. Morning-only — a stepped
/// counter session can only exist while the day is holding at Morning (PKD5).
///
/// This handler does NOT resolve the active customer's fate (buy/walk) — <see cref="CounterQueueSystem"/>
/// does that in the systems pass (step 2 of <c>GameKernel.Tick</c>), after every action in the batch
/// has been applied. That split keeps rejection legality (this file) separate from resolution math
/// (the system), matching the CraftingHandlers/QualityRoller precedent. Every rejection here happens
/// BEFORE any RNG — in fact nothing in this file draws RNG at all (PA3 hard constraint).
/// </summary>
public sealed class CounterHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        phase == DayPhase.Morning && action is OpenCounterAction or PresentItemAction
            or SuggestItemAction or HaggleResponseAction or CloseCounterAction;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            OpenCounterAction open => ApplyOpen(state, open, events),
            PresentItemAction present => ApplyPresent(state, present),
            SuggestItemAction suggest => ApplySuggest(state, suggest),
            HaggleResponseAction haggle => ApplyHaggle(state, haggle),
            CloseCounterAction close => ApplyClose(state, close),
            _ => (state, new RejectedAction(action, $"CounterHandlers cannot apply {action.GetType().Name}.")),
        };

    /// <summary>
    /// Opens a fresh stepped session: queue = alive heroes in HeroId order (the existing
    /// deterministic shopping order — <see cref="Heroes.HeroShoppingSystem"/>), first customer
    /// becomes Active, <see cref="CustomerApproached"/> fires. An empty queue (no living heroes) is
    /// a valid open session with Active null — the player is only arranging (PKD6).
    /// </summary>
    private static (GameState, RejectedAction?) ApplyOpen(GameState state, OpenCounterAction action, IEventSink events)
    {
        if (state.Counter is { Closed: false })
        {
            return (state, new RejectedAction(action, "The counter is already open this morning."));
        }

        var queue = state.Heroes.Values
            .Where(h => h.Alive)
            .OrderBy(h => h.Id.Value)
            .Select(h => h.Id)
            .ToImmutableList();

        var active = queue.Count > 0 ? queue[0] : (HeroId?)null;
        var counter = CounterState.Empty with { Queue = queue, Active = active };

        if (active is { } hero)
        {
            events.Emit(new CustomerApproached(hero));
        }

        return (state with { Counter = counter }, null);
    }

    /// <summary>Shows a shelved item to the active customer (opener move). Records the intent on
    /// <see cref="CounterState.Presented"/>; <see cref="CounterQueueSystem"/> resolves it this same tick.</summary>
    private static (GameState, RejectedAction?) ApplyPresent(GameState state, PresentItemAction action)
    {
        var (session, rejection) = RequireActiveSession(state, action);
        if (rejection is not null)
        {
            return (state, rejection);
        }

        if (!state.Items.ContainsKey(action.Item.Value))
        {
            return (state, new RejectedAction(action, $"No such item {action.Item}."));
        }

        if (!state.Player.Shelf.Any(e => e.Item == action.Item))
        {
            return (state, new RejectedAction(action, $"Item {action.Item} is not on the shelf."));
        }

        return (state with { Counter = session! with { Presented = action.Item } }, null);
    }

    /// <summary>Upsell a complementary item (PA4 seam: Interest bonus on an empty fitting slot).
    /// PA3 validates legality only — the economics are a PA4 placeholder-replacement, so this is a
    /// legal no-op today (never rejects a well-formed suggest just because PA4 hasn't landed).</summary>
    private static (GameState, RejectedAction?) ApplySuggest(GameState state, SuggestItemAction action)
    {
        var (_, rejection) = RequireActiveSession(state, action);
        if (rejection is not null)
        {
            return (state, rejection);
        }

        if (!state.Items.ContainsKey(action.Item.Value))
        {
            return (state, new RejectedAction(action, $"No such item {action.Item}."));
        }

        return (state, null);
    }

    /// <summary>Respond to the standing offer (PA4 seam: band math, patience, pin bonus). PA3
    /// validates legality only — a legal no-op today; PA4 replaces this with the real resolution.</summary>
    private static (GameState, RejectedAction?) ApplyHaggle(GameState state, HaggleResponseAction action)
    {
        var (_, rejection) = RequireActiveSession(state, action);
        return rejection is not null ? (state, rejection) : (state, null);
    }

    /// <summary>Ends stepped service. Unserved heroes fall back to the atomic pass on this same tick
    /// (<see cref="Heroes.HeroShoppingSystem"/>, gated on <see cref="CounterState.Served"/>).</summary>
    private static (GameState, RejectedAction?) ApplyClose(GameState state, CloseCounterAction action)
    {
        if (state.Counter is null)
        {
            return (state, new RejectedAction(action, "No counter session is open."));
        }

        if (state.Counter.Closed)
        {
            return (state, null); // idempotent — already closing this tick
        }

        return (state with { Counter = state.Counter with { Closed = true } }, null);
    }

    /// <summary>Shared legality gate for every action that targets the active customer: a session
    /// must be open (and not already closing) and a customer must actually be at the counter.</summary>
    private static (CounterState? Session, RejectedAction? Rejected) RequireActiveSession(GameState state, PlayerAction action)
    {
        if (state.Counter is not { Closed: false } session)
        {
            return (null, new RejectedAction(action, "No counter session is open."));
        }

        if (session.Active is null)
        {
            return (null, new RejectedAction(action, "No active customer is at the counter."));
        }

        return (session, null);
    }
}
