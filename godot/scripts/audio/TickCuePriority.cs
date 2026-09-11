using System.Collections.Generic;
using System.Linq;

namespace GodotClient.Audio;

/// <summary>
/// P2-SCREEN-16 (§11.15, P2-KTD11): every outcome a game tick can carry that is worth its own sound.
/// <see cref="GodotClient.MainUi.SoundTheTick"/> plays exactly one cue per tick — a busy Evening can
/// carry a dozen sales and a death, and a sound per event turns the day's most dramatic moment into a
/// burst of noise — so these are candidates for ONE slot, never additive. A ceremony staged after this
/// unit adds its own member here and a matching <see cref="TickCueDeclaration"/> in <see
/// cref="TickCuePriority.Declarations"/>, in that unit's own body (P2-R33), rather than a new branch
/// hand-inserted into <c>SoundTheTick</c> itself.
/// </summary>
public enum TickOutcomeKind
{
    /// <summary>The sim refused at least one queued action this phase — the worst news a tick can
    /// carry, and the one the player most needs to notice (P2-R3).</summary>
    Refusal,

    /// <summary>Morning just ended and the party is actually leaving — the send-off beat.</summary>
    Departure,

    /// <summary>The day's own bell: any other phase completing.</summary>
    DayBell,
}

/// <summary>
/// One <see cref="TickOutcomeKind"/>'s declared place in the tick's priority order and the <see
/// cref="Audio.Cue"/> it plays when it wins.
///
/// <para><b><see cref="Rank"/> has no default, on purpose</b> — the same choice <see
/// cref="GodotClient.Ui.SurfaceClaim.OwnsScreen"/> made for the identical reason (P2-SCREEN-04's own
/// doc): a declaration that could omit its own rank would let a future outcome silently sort wherever
/// the list happened to place it, which is exactly the "hand-written <c>if</c> cascade nobody can see
/// the whole order of" defect this unit exists to retire. Omitting it is a compiler error, not a
/// silent default of 0.</para>
/// </summary>
public readonly record struct TickCueDeclaration(TickOutcomeKind Kind, int Rank, Cue CueId);

/// <summary>
/// The declared priority table <see cref="GodotClient.MainUi.SoundTheTick"/> dispatches through, and
/// the pure resolver over it. Plain C# with no Godot dependency — the same reason <see
/// cref="GodotClient.Ui.SurfaceArbiter.Resolve"/> stays plain: a precedence rule provable against every
/// candidate combination a test can construct, not just the ones a session happens to reach by
/// playing.
///
/// <para><b>Today's three outcomes, ranked worst news first (P2-KTD11):</b> a refusal beats a
/// departure beats the plain day bell — the exact order <c>SoundTheTick</c>'s own doc has always
/// stated in prose, now data instead of position-in-a-function.</para>
/// </summary>
public static class TickCuePriority
{
    /// <summary>Every declared outcome, worst news first by construction (see each member's own
    /// comment) — <see cref="Resolve"/> does not rely on this array's ORDER, only on <see
    /// cref="TickCueDeclaration.Rank"/>, so a future addition may be appended anywhere in this list
    /// without disturbing the ones already here.</summary>
    public static readonly IReadOnlyList<TickCueDeclaration> Declarations = new[]
    {
        new TickCueDeclaration(TickOutcomeKind.Refusal, Rank: 0, CueId: Cue.Rejected),
        new TickCueDeclaration(TickOutcomeKind.Departure, Rank: 1, CueId: Cue.PartyDepart),
        new TickCueDeclaration(TickOutcomeKind.DayBell, Rank: 2, CueId: Cue.Bell),
    };

    /// <summary>
    /// The single declaration for this tick, or <see langword="null"/> if none of <paramref
    /// name="candidates"/> is a declared outcome — <c>SoundTheTick</c>'s own "nothing completed, say
    /// nothing" case (an immediate action with no rejection). Lowest <see
    /// cref="TickCueDeclaration.Rank"/> among the candidates wins; a cue is never additive.
    /// </summary>
    public static TickCueDeclaration? Resolve(IReadOnlyCollection<TickOutcomeKind> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        return Declarations
            .Where(declaration => candidates.Contains(declaration.Kind))
            .OrderBy(declaration => declaration.Rank)
            .Select(declaration => (TickCueDeclaration?)declaration)
            .FirstOrDefault();
    }
}
