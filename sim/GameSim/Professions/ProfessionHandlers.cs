using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Professions;

/// <summary>
/// Action handler for profession selection (P1): <see cref="SetProfessionsAction"/>. The
/// player picks 1–2 professions; the selection gates which recipes they may craft
/// (see <c>CraftingHandlers</c>). Selection is legal in all phases.
///
/// Determinism note (KTD4): this handler draws no RNG — selection is pure state. It rebuilds
/// the stored set with the default comparer (the codebase convention for serialized player
/// collections) so serialization is byte-stable regardless of the comparer the caller's set
/// carried.
///
/// REGISTRATION: this handler is NOT yet wired into <c>GameComposition.BuildKernel</c> (that
/// file is orchestrator-owned). Until the orchestrator registers it, <c>SetProfessionsAction</c>
/// is a no-op in the composed kernel; tests compose their own kernel including it.
/// </summary>
public sealed class ProfessionHandlers : IActionHandler
{
    /// <summary>Max professions a save may select at once (P1: pick 1–2).</summary>
    public const int MaxSelected = 2;

    public bool CanHandle(PlayerAction action, DayPhase phase) => action is SetProfessionsAction;

    public (GameState State, RejectedAction? Rejected) Apply(GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            SetProfessionsAction set => ApplySet(state, set),
            _ => (state, new RejectedAction(action, $"ProfessionHandlers cannot apply {action.GetType().Name}.")),
        };

    private static (GameState, RejectedAction?) ApplySet(GameState state, SetProfessionsAction action)
    {
        if (action.Professions.Count < 1)
        {
            return (state, new RejectedAction(action, "Must select at least one profession."));
        }

        if (action.Professions.Count > MaxSelected)
        {
            return (state, new RejectedAction(action, $"Cannot select more than {MaxSelected} professions (got {action.Professions.Count})."));
        }

        foreach (var professionId in action.Professions)
        {
            if (!ProfessionRegistry.IsRegistered(professionId))
            {
                return (state, new RejectedAction(action, $"Unknown profession '{professionId}'."));
            }
        }

        var selected = ImmutableSortedSet.CreateRange(action.Professions);
        var newState = state with { Player = state.Player with { SelectedProfessions = selected } };
        return (newState, null);
    }
}
