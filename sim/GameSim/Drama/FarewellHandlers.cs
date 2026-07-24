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
        action is HonorMemorialAction && phase == DayPhase.Evening;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not HonorMemorialAction honor)
        {
            return (state, new RejectedAction(action, $"FarewellHandlers cannot apply {action.GetType().Name}."));
        }

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
}
