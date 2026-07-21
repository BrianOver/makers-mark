using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;

namespace GameSim.Counter;

/// <summary>
/// Counter-service action handling (PKD5/PKD6): validates and records intent for
/// <see cref="OpenCounterAction"/>/<see cref="PresentItemAction"/>/<see cref="SuggestItemAction"/>/
/// <see cref="HaggleResponseAction"/>/<see cref="CloseCounterAction"/>. Morning-only — a stepped
/// counter session can only exist while the day is holding at Morning (PKD5).
///
/// <para>PA3 split resolution math out to <see cref="CounterQueueSystem"/> (the systems pass, which
/// runs once AFTER every action in the batch is applied) so a single deferred field
/// (<see cref="CounterState.Presented"/>) could carry "what was shown" across that boundary. PA4's
/// <see cref="HaggleResponseAction"/> has no equivalent spare field to stash intent across that same
/// boundary — <c>Contracts/</c> is frozen (deny-list) — so <see cref="ApplyHaggle"/> resolves
/// IMMEDIATELY here instead, reading the standing offer <see cref="CounterQueueSystem"/> set up on a
/// PRIOR tick's Present. Practical consequence: submit <c>PresentItemAction</c> and
/// <c>HaggleResponseAction</c> in SEPARATE ticks (the natural UX anyway — you see the offer, then
/// respond to it); bundling both in one batch would read a stale/absent standing offer. Every
/// rejection here happens BEFORE any RNG — nothing in this file draws RNG at all (PA3/PA4 hard
/// constraint).</para>
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
            HaggleResponseAction haggle => ApplyHaggle(state, haggle, events),
            CloseCounterAction close => ApplyClose(state, close),
            _ => (state, new RejectedAction(action, $"CounterHandlers cannot apply {action.GetType().Name}.")),
        };

    /// <summary>
    /// Opens a fresh stepped session: queue = alive heroes in HeroId order (the existing
    /// deterministic shopping order — <see cref="Heroes.HeroShoppingSystem"/>), first customer
    /// becomes Active with a full Patience budget, <see cref="CustomerApproached"/> fires. An empty
    /// queue (no living heroes) is a valid open session with Active null — the player is only
    /// arranging (PKD6).
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
        var counter = CounterQueueSystem.PromoteActive(CounterState.Empty, queue, active);

        if (active is { } hero)
        {
            events.Emit(new CustomerApproached(hero));
        }

        return (state with { Counter = counter }, null);
    }

    /// <summary>Shows a shelved item to the active customer (opener move). Records the intent on
    /// <see cref="CounterState.Presented"/>; <see cref="CounterQueueSystem"/> resolves it this same
    /// tick (walk immediately on a Pass verdict, else open a haggle round). Presenting a DIFFERENT
    /// item while a round is already open abandons that round (a fresh pitch starts clean); re-
    /// presenting the SAME item mid-round is a harmless no-op — the round stays exactly as it was.</summary>
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

        var abandonPriorRound = session!.Round > 0 && session.Presented != action.Item;
        var updated = abandonPriorRound
            ? session with { Round = 0, InterestPermille = 0, StandingOfferGold = null, Presented = action.Item }
            : session with { Presented = action.Item };

        return (state with { Counter = updated }, null);
    }

    /// <summary>Upsell a complementary item: bumps the session Interest meter
    /// (<see cref="HaggleResolver.ApplySuggestBonus"/>) when the suggestion lands on a slot the
    /// hero would plausibly wear and currently has empty. A legal no-op on any other item — PA3's
    /// contract that a well-formed Suggest never rejects holds (only wrong-phase/no-session/no-item
    /// rejects).</summary>
    private static (GameState, RejectedAction?) ApplySuggest(GameState state, SuggestItemAction action)
    {
        var (session, rejection) = RequireActiveSession(state, action);
        if (rejection is not null)
        {
            return (state, rejection);
        }

        if (!state.Items.TryGetValue(action.Item.Value, out var item))
        {
            return (state, new RejectedAction(action, $"No such item {action.Item}."));
        }

        var hero = state.Heroes[session!.Active!.Value.Value];
        var heroClass = ClassRegistry.Require(hero.ClassId);
        var updated = HaggleResolver.ApplySuggestBonus(session, hero, heroClass, item);

        return (state with { Counter = updated }, null);
    }

    /// <summary>Respond to the standing offer: Accept, HoldFirm, or Counter at a named price
    /// (<see cref="HaggleResolver.ResolveHaggleResponse"/>) — band math, patience, and the pin bonus
    /// all resolve HERE, immediately (see the type-level remarks for why). Rejects (before any
    /// mutation) when no round has been opened yet, when a Counter price is missing/non-positive, or
    /// when it exceeds the hero's gold on hand.</summary>
    private static (GameState, RejectedAction?) ApplyHaggle(GameState state, HaggleResponseAction action, IEventSink events)
    {
        var (session, rejection) = RequireActiveSession(state, action);
        if (rejection is not null)
        {
            return (state, rejection);
        }

        var counter = session!;
        if (counter.Round == 0 || counter.StandingOfferGold is null || counter.Presented is not { } presentedId)
        {
            return (state, new RejectedAction(action, "No standing offer to respond to — present an item first."));
        }

        var hero = state.Heroes[counter.Active!.Value.Value];

        if (!state.Items.TryGetValue(presentedId.Value, out var item))
        {
            return (state, new RejectedAction(action, $"No such item {presentedId}."));
        }

        var shelfEntry = state.Player.Shelf.FirstOrDefault(e => e.Item == presentedId);
        if (shelfEntry is null)
        {
            return (state, new RejectedAction(action, $"Item {presentedId} is no longer on the shelf."));
        }

        return HaggleResolver.ResolveHaggleResponse(state, counter, hero, item, shelfEntry, action, events);
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
