using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Drama;

/// <summary>
/// Morning recruit trickle (R10/AE6): while the alive roster is short of six, a level-1
/// recruit walks into town — at most one per Morning, gated so consecutive arrivals sit
/// <see cref="RecruitGateDays"/> mornings apart. The gate prevents both dead-end saves
/// (deaths are always eventually replaced) and death-spiral immunity (a wipe is not
/// refilled overnight).
///
/// Per Morning, in order: the gate decrements when positive; then, if the gate sits at
/// zero and the alive count is below <see cref="RosterCap"/>, a recruit is minted via
/// <c>HeroRoster.CreateRecruit</c> (three RNG draws — name, role, gold) and the gate
/// resets. An idle gate rests at zero, so the first death after a quiet stretch is
/// answered the very next Morning.
/// </summary>
public sealed class RecruitSystem : IPhaseSystem
{
    /// <summary>Recruits never push the alive roster past this (R7's six).</summary>
    public const int RosterCap = 6;

    /// <summary>Mornings between consecutive recruits. Tuned in U10's balance gate.</summary>
    public const int RecruitGateDays = 3;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "recruit-trickle";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var gate = state.Drama.DaysUntilNextRecruit;
        if (gate > 0)
        {
            gate--;
            state = state with { Drama = state.Drama with { DaysUntilNextRecruit = gate } };
        }

        if (gate > 0)
        {
            return state;
        }

        var alive = state.Heroes.Values.Count(h => h.Alive);
        if (alive >= RosterCap)
        {
            return state; // full house — recruits never overshoot six
        }

        var recruit = HeroRoster.CreateRecruit(state.NextHeroId, rng);
        events.Emit(new RecruitArrived(recruit.Id));
        return state with
        {
            Heroes = state.Heroes.Add(recruit.Id.Value, recruit),
            NextHeroId = state.NextHeroId + 1,
            Drama = state.Drama with { DaysUntilNextRecruit = RecruitGateDays },
        };
    }
}
