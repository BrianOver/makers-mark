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

    /// <summary>Mornings between consecutive recruits. Tuned in U10's balance gate —
    /// lowered from 3 to 2 after the death-clears-floor correctness fix made combat
    /// deadlier, so the town would otherwise dwindle to a single survivor by day 100.</summary>
    public const int RecruitGateDays = 2;

    /// <summary>Wave 4 (U22, "kin-of-the-dead"): the starting <see cref="Hero.MoodPermille"/> bump
    /// a recruit carries in when a qualifying famous-dead legend exists (<see
    /// cref="LegendQuery.HasFamousDeadLegend"/>) — deliberately SMALL (well under
    /// <c>RelationshipBands.RegularMinMood</c> = 80, and the same order of magnitude as
    /// <c>WillingnessModel.PinMoodBonus</c> = 60) so it nudges a recruit toward Regular without
    /// ever dominating the band math or moving the balance gate out of band.</summary>
    public const int KinOfDeadMoodBonus = 60;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "recruit-trickle";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // U1 held-Morning guard (see RentSystem): fire once per calendar Morning, not once per
        // stepped-counter tick. Skip while a session is open; CounterQueueSystem runs ahead of
        // this system so the closing tick sees Closed==true and the recruit trickle fires once.
        if (state.Counter is { Closed: false })
        {
            return state;
        }

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

        // U22 (kin-of-the-dead): a famous fallen hero's legend reaches new arrivals — pure
        // derivation over already-recorded memorials/attribution beats (LegendQuery), no per-hero
        // tick (KTD5), no RNG draw (doesn't touch the determinism contract's draw count), and
        // NEVER read by muster/floor/expedition (PKD7) — Hero.MoodPermille's own doc pins that.
        if (LegendQuery.HasFamousDeadLegend(state))
        {
            recruit = recruit with { MoodPermille = recruit.MoodPermille + KinOfDeadMoodBonus };
        }

        events.Emit(new RecruitArrived(recruit.Id));
        return state with
        {
            Heroes = state.Heroes.Add(recruit.Id.Value, recruit),
            NextHeroId = state.NextHeroId + 1,
            Drama = state.Drama with { DaysUntilNextRecruit = RecruitGateDays },
        };
    }
}
