using System.Linq;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// Wave 4c (U18, farewell rite): the player's <see cref="HonorMemorialAction"/> handler — an
/// earned goodbye, not just an economy event (R6). Evening-legal: <see cref="Memorial"/>s are
/// raised by <see cref="ExpeditionRevealSystem"/> during THIS SAME phase's system pass (step 2 of
/// <c>GameKernel.Tick</c> runs after step 1's player actions), so a hero who dies this Evening is
/// only actionable starting the NEXT Evening tick — a memorial from an earlier Evening is
/// actionable any Evening after.
///
/// IDEMPOTENT (per the contract doc on <see cref="HonorMemorialAction"/>): a second rite for an
/// already-<see cref="Memorial.Honored"/> memorial is a clean no-op — no event, no state change,
/// and NOT a <see cref="RejectedAction"/> (the player didn't do anything wrong asking twice).
/// Missing memorial IS a typed rejection (there is nothing to honor). Draws no RNG, no wall clock.
/// </summary>
public sealed class FarewellHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is HonorMemorialAction or PlaceGraveMarkerAction or ChooseRemembranceAction && phase == DayPhase.Evening;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        switch (action)
        {
            case PlaceGraveMarkerAction marker:
                return ApplyMarker(state, marker, events);
            case ChooseRemembranceAction remembrance:
                return ApplyRemembrance(state, remembrance, events);
            case HonorMemorialAction:
                break;
            default:
                return (state, new RejectedAction(action, $"FarewellHandlers cannot apply {action.GetType().Name}."));
        }

        var honor = (HonorMemorialAction)action;

        var index = state.Drama.Memorials.FindIndex(m => m.Hero == honor.Hero);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No memorial recorded for {honor.Hero} — nothing to honor."));
        }

        var memorial = state.Drama.Memorials[index];
        if (memorial.Honored)
        {
            return (state, null); // idempotent no-op — already honored, this is not an error
        }

        var newState = state with
        {
            Drama = state.Drama with
            {
                Memorials = state.Drama.Memorials.SetItem(index, memorial with { Honored = true }),
            },
        };

        events.Emit(new MemorialHonored(honor.Hero, memorial.HeroName));

        return (newState, null);
    }

    /// <summary>P2-PEOPLE-05, wake verb one. Check order fixed (memorial, one marker ever, item exists, yours,
    /// not worn, not shelved, not another grave's marker) so rejection reasons are stable. The item stays in
    /// GameState.Items — its history is permanent (R5); the memorial now points at it.</summary>
    private static (GameState, RejectedAction?) ApplyMarker(GameState state, PlaceGraveMarkerAction action, IEventSink events)
    {
        var index = state.Drama.Memorials.FindIndex(m => m.Hero == action.Hero);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No memorial recorded for {action.Hero} — nowhere to set a marker."));
        }

        var memorial = state.Drama.Memorials[index];
        if (memorial.MarkerItem is not null)
        {
            return (state, new RejectedAction(action, $"{memorial.HeroName}'s grave already has its marker."));
        }

        if (!state.Items.TryGetValue(action.Item.Value, out var item))
        {
            return (state, new RejectedAction(action, $"Item {action.Item} does not exist."));
        }

        if (!item.PlayerCrafted)
        {
            return (state, new RejectedAction(action, $"{item.Name} is not your work — a marker carries your mark."));
        }

        if (state.Heroes.Values.Any(h => WoreItem(h.Gear, action.Item)))
        {
            return (state, new RejectedAction(action, $"{item.Name} is on someone's back."));
        }

        if (state.Player.Shelf.Any(e => e.Item == action.Item))
        {
            return (state, new RejectedAction(action, $"{item.Name} is still on your shelf — take it down first."));
        }

        if (state.Drama.Memorials.Any(m => m.MarkerItem == action.Item))
        {
            return (state, new RejectedAction(action, $"{item.Name} already marks another grave."));
        }

        var newState = state with
        {
            Drama = state.Drama with
            {
                Memorials = state.Drama.Memorials.SetItem(index, memorial with { MarkerItem = action.Item }),
            },
        };
        events.Emit(new GraveMarkerPlaced(action.Hero, memorial.HeroName, action.Item));
        return (newState, null);
    }

    /// <summary>P2-PEOPLE-05, wake verb two. The chosen event must be in the log and must truly name the fallen
    /// (<see cref="RemembranceQuery.NamesHero"/>); one remembrance per memorial.</summary>
    private static (GameState, RejectedAction?) ApplyRemembrance(GameState state, ChooseRemembranceAction action, IEventSink events)
    {
        var index = state.Drama.Memorials.FindIndex(m => m.Hero == action.Hero);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"No memorial recorded for {action.Hero} — nothing to remember them by."));
        }

        var memorial = state.Drama.Memorials[index];
        if (memorial.Remembrance is not null)
        {
            return (state, new RejectedAction(action, $"{memorial.HeroName} is already remembered by one thing."));
        }

        var source = state.EventLog.FirstOrDefault(e => e.Id == action.Source);
        if (source is null)
        {
            return (state, new RejectedAction(action, $"Event {action.Source} is not in the record."));
        }

        if (!RemembranceQuery.NamesHero(source, action.Hero))
        {
            return (state, new RejectedAction(action, $"That is not {memorial.HeroName}'s story — it never names them."));
        }

        var newState = state with
        {
            Drama = state.Drama with
            {
                Memorials = state.Drama.Memorials.SetItem(index, memorial with { Remembrance = action.Source }),
            },
        };
        events.Emit(new RemembranceChosen(action.Hero, memorial.HeroName, action.Source));
        return (newState, null);
    }

    private static bool WoreItem(GearSet gear, ItemId item) =>
        gear.Weapon == item || gear.Shield == item || gear.Armor == item || gear.Trinket == item;
}
