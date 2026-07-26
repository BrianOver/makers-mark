namespace GameSim.Contracts;

/// <summary>
/// The drama-director pacing machine's phase (Phase C, U-C3). A classic BuildUp → Peak → Relax
/// tension cycle: tension accumulates in BuildUp, incidents fire at Peak, the town cools in Relax.
/// APPEND-ONLY enum (int-serialized) — never reorder or reuse a value (save-compat, KTD4).
/// </summary>
public enum DirectorPhase
{
    /// <summary>Tension is accumulating toward the Peak threshold; incidents do not fire here
    /// (only the drought pity can force one). The resting/opening state of a fresh campaign.</summary>
    BuildUp = 0,

    /// <summary>High tension — the window in which the director fires an incident (subject to the
    /// refire guard). Firing releases tension, so the next daily step cools the town into Relax.</summary>
    Peak = 1,

    /// <summary>Post-incident cooldown; tension decays and no incident fires until the machine
    /// cycles back to BuildUp after the minimum relax dwell.</summary>
    Relax = 2,
}

/// <summary>
/// The CATEGORY of a director incident (Phase C, U-C3). Ascending categories unlock by the town's
/// PROGRESSION TIER (deepest floor reached) — NEVER by shop wealth, the RimWorld wealth-spiral this
/// design deliberately avoids (U-C3 hard invariant). APPEND-ONLY enum (int-serialized).
/// </summary>
public enum IncidentCategory
{
    /// <summary>A low-stakes omen — always eligible (tier 0), the baseline incident.</summary>
    Rumor = 0,

    /// <summary>A minor probing raid from the den's shallow denizens.</summary>
    Skirmish = 1,

    /// <summary>The den's population swells — unlocked once the town has pushed past the surface.</summary>
    Infestation = 2,

    /// <summary>A deeper warren breaks open — a mid-depth escalation.</summary>
    Breakout = 3,

    /// <summary>The den's apex stirs — the deepest, rarest category.</summary>
    Cataclysm = 4,
}

/// <summary>
/// The MAGNITUDE of a director incident (Phase C, U-C3). Higher magnitudes unlock by the town's
/// SURVIVED-COUNT (how many delvers have come back from the den) — NEVER by shop wealth. APPEND-ONLY
/// enum (int-serialized).
/// </summary>
public enum IncidentMagnitude
{
    /// <summary>A small incident — always available.</summary>
    Minor = 0,

    /// <summary>A stronger incident — unlocked as survivors accumulate.</summary>
    Notable = 1,

    /// <summary>A major incident — the town must be battle-tested to draw this.</summary>
    Severe = 2,
}

/// <summary>
/// The drama director's serializable pacing state (Phase C, U-C3), carried on
/// <see cref="GameState.Director"/>. Evolves deterministically each Morning by a single seeded poll
/// on the EXISTING kernel RNG stream (KTD4) — integer-only, no wall clock, no transcendental Math.*.
/// </summary>
/// <param name="Tension">The 0–1000 tension accumulator: raised by yesterday's dramatic events,
/// lowered by a fixed daily decay, and released when an incident fires.</param>
/// <param name="Phase">The BuildUp/Peak/Relax pacing machine's current phase.</param>
/// <param name="PhaseEnteredDay">The day the current <see cref="Phase"/> began — the source of the
/// min-duration dwell counters that gate every phase transition.</param>
/// <param name="LastFiredDay">The day the director last fired an incident (0 = never), read by the
/// refire guard (<c>MinRefireDays</c>).</param>
/// <param name="DroughtDays">Consecutive days the director has polled without firing — the pity
/// counter that force-fires an incident once it reaches the drought threshold.</param>
public sealed record DirectorState(
    int Tension,
    DirectorPhase Phase,
    int PhaseEnteredDay,
    int LastFiredDay,
    int DroughtDays)
{
    /// <summary>A fresh campaign's director: no tension, BuildUp, nothing fired yet.</summary>
    public static readonly DirectorState Empty = new(
        Tension: 0,
        Phase: DirectorPhase.BuildUp,
        PhaseEnteredDay: 0,
        LastFiredDay: 0,
        DroughtDays: 0);
}
