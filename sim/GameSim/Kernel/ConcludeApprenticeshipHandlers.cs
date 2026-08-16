using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>
/// §11.13 amendment (U4a, R12 ruled yes): accepts <see cref="ConcludeApprenticeshipAction"/> in
/// every phase and mutates nothing — its entire meaning is its own presence in the kernel's
/// <see cref="GameState.ActionLog"/>, which every <see cref="GameKernel.Tick"/>/
/// <see cref="GameKernel.ApplyNow"/> call appends to UNCONDITIONALLY regardless of which handler
/// ran (see either method's own final <c>with</c> expression), so this handler's only job is to
/// exist — accept the action, decline to reject it, hand the state back unchanged. Idempotent by
/// construction: a second submit runs this same no-op Apply and appends a second, harmless
/// ActionLog entry (<c>GameSim.Expedition.ApprenticeWarrant.Concluded</c> is a durable "has this
/// ever appeared" scan, so a repeat entry changes nothing it reads).
/// </summary>
public sealed class ConcludeApprenticeshipHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) => action is ConcludeApprenticeshipAction;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        (state, null);
}
