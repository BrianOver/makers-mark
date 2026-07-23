using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// Idling cedes market share (Game-Feel Plan G3, docs/design/2026-07-21-game-feel-plan.md §G3):
/// registered LAST in the Evening group so it reads <see cref="GameState.ActionSlotsRemaining"/>
/// AFTER every handler for the day has had its chance to spend one, but BEFORE the kernel resets
/// the budget for tomorrow (that reset is the very last step of the Evening tick, in
/// <c>GameKernel.Tick</c> — this system's read is safe by construction: systems run before the
/// kernel's own post-tick bookkeeping).
///
/// A day where NO slot was spent (<see cref="ActionBudget.ConsumesSlot"/> never fired — the player
/// crafted, restocked, and negotiated nothing) raises <see cref="GameState.RivalMarketSharePermille"/>
/// toward the rival; any real-work day claws it back toward the player. <see cref="RivalRestockSystem"/>
/// (Morning) reads the resulting share to discount newly-minted rival stock — the visible, economic
/// consequence of a day spent doing nothing.
///
/// Determinism: pure integer, no RNG, no wall clock — a fixed step per day, clamped to [0, 1000].
/// </summary>
public sealed class MarketShareSystem : IPhaseSystem
{
    /// <summary>Per-mille gained toward the rival on a fully idle day.</summary>
    public const int IdleGainPerMille = 150;

    /// <summary>Per-mille clawed back toward the player on any real-work day.</summary>
    public const int ActiveRecoveryPerMille = 100;

    public DayPhase Phase => DayPhase.Evening;

    public string Name => "market-share";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var wasIdle = state.ActionSlotsRemaining >= ActionBudget.SlotsPerDay;
        var current = state.RivalMarketSharePermille;
        var next = wasIdle
            ? Math.Min(1000, current + IdleGainPerMille)
            : Math.Max(0, current - ActiveRecoveryPerMille);

        if (next == current)
        {
            return state; // already at the clamp in the direction today would move it — no-op
        }

        events.Emit(new MarketShareShifted(next, RivalGained: wasIdle));
        return state with { RivalMarketSharePermille = next };
    }
}
