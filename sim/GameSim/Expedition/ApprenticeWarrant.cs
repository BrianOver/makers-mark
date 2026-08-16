using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Expedition;

/// <summary>
/// §11.13 amendment (U4): "no hero dies while the apprenticeship holds" — a taught, dated town
/// rule the player can also walk out of (R12 ruled yes). Reused verbatim from the rejected R9(c)
/// build's mechanism (<c>ModifierHpDelta</c>, the Leech rune's own channel) with the ONE change
/// R9(c) needed: the trigger is a date plus one confirmed opt-out, never a behavior-fed predicate
/// (KTD-C′ — that is what inverted mortality 2.6x in the rejected build).
///
/// <para><b>The trigger.</b> <see cref="Covers"/> = <c>state.Day &lt;= LastGraceDay &amp;&amp;
/// !Concluded(state)</c>. No purchase, sale, provision, or tutorial progress moves this window —
/// the ONLY player behavior that does is one explicit, confirmed opt-out
/// (<see cref="ConcludeApprenticeshipAction"/>), logged the same way every other decision is.</para>
///
/// <para><b>Fixture protection by construction (KTD-D).</b> This class answers "does the warrant
/// cover this state" and "was this specific clamp the warrant's doing" — it is never threaded
/// automatically; only the two Expedition-phase systems (<see cref="ExpeditionSystem"/>,
/// <see cref="ExpeditionDeepSystem"/>) call <see cref="Covers"/> and pass the answer down as an
/// opaque bool, the same shape as the bounty <c>retreatExemptHeroes</c> parameter already threads.
/// Every direct <see cref="ExpeditionResolver"/> call (every existing fixture, every balance
/// micro-test) defaults the parameter OFF and is untouched by this unit's existence.</para>
///
/// <para><b>One legibility source (KTD-E).</b> <see cref="FiredIn"/> is the SAME classification
/// the resolver's own clamp uses to decide whether to intervene (a lethal blow, i.e.
/// <c>!MonsterKilled</c>, with a positive <see cref="CombatEvent.ModifierHpDelta"/>) — the Leech
/// rune's own heal-on-kill only ever fires when <c>MonsterKilled</c> is true, so the two channels
/// can never collide on the same <see cref="CombatEvent"/>, and this predicate can never disagree
/// with what the resolver actually did.</para>
/// </summary>
public static class ApprenticeWarrant
{
    /// <summary>
    /// The apprenticeship's own span (R11, confirmed): three days, ending where the tutorial's own
    /// backstop does. <c>GodotClient.Ui.TutorialFlow.BackstopDay</c> is pinned to
    /// <c>LastGraceDay + 1</c> by <c>TutorialRegistryConformanceTests</c> so the two can never drift
    /// apart — the warrant and the taught chain that promises its end are one fact, not two.
    /// </summary>
    public const int LastGraceDay = 3;

    /// <summary>
    /// Whether the warrant protects a fight resolved against <paramref name="state"/> right now.
    /// Pure, deterministic, draws no RNG: a date check plus a durable-log scan. Recomputed at BOTH
    /// resolution ticks (Expedition and ExpeditionDeep) by the caller — a vigil resupply, or a
    /// mid-day <see cref="ConcludeApprenticeshipAction"/>, can land between them, so neither tick
    /// may cache the other's answer.
    /// </summary>
    public static bool Covers(GameState state) => state.Day <= LastGraceDay && !Concluded(state);

    /// <summary>
    /// Whether the player has ever submitted <see cref="ConcludeApprenticeshipAction"/> this
    /// campaign — the same durable-fact idiom <c>GodotClient.Ui.TutorialFlow</c>'s Commission step
    /// already reads off <see cref="GameState.ActionLog"/> rather than a dedicated event. Monotonic:
    /// the log is append-only, so once true this can never read false again for the same campaign.
    /// </summary>
    public static bool Concluded(GameState state) =>
        state.ActionLog.Any(batch => batch.Actions.Any(a => a is ConcludeApprenticeshipAction));

    /// <summary>
    /// The clamp itself (KTD-D's own "clamp helper the resolver calls"): given the hp a lethal
    /// blow would leave a hero at, returns whether the warrant intervenes and, if so, the survival
    /// hp (always 1) plus the signed delta to record as <see cref="CombatEvent.ModifierHpDelta"/>
    /// (positive — a rescue, never a cost). A no-op (returns false) when the blow was not actually
    /// lethal — the warrant only ever answers a death, never pads an already-survived hit.
    /// </summary>
    public static bool TryClamp(int hpAfterDamage, out int hpFinal, out int modifierHpDelta)
    {
        if (hpAfterDamage > 0)
        {
            hpFinal = hpAfterDamage;
            modifierHpDelta = 0;
            return false;
        }

        hpFinal = 1;
        modifierHpDelta = 1 - hpAfterDamage;
        return true;
    }

    /// <summary>One hero's blow survived only because the warrant held — the ledger card's own
    /// content (the true roll, floor, monster) so the client never re-derives a number the resolver
    /// already computed.</summary>
    public readonly record struct WarrantSave(HeroId Hero, int Floor, string MonsterKind, int DamageTaken);

    /// <summary>
    /// Every warrant-held save inside <paramref name="result"/> (KTD-E: the SAME classification the
    /// resolver's own clamp used, replayed off the recorded <see cref="CombatEvent"/> stream rather
    /// than a second computation) — a <c>!MonsterKilled</c> exchange carrying a positive
    /// <see cref="CombatEvent.ModifierHpDelta"/> is, by construction, a blow the resolver would
    /// otherwise have let kill the hero. Pure projection; draws no RNG, mutates nothing.
    /// </summary>
    public static ImmutableList<WarrantSave> FiredIn(ExpeditionResult result)
    {
        var saves = ImmutableList.CreateBuilder<WarrantSave>();
        foreach (var floor in result.Floors)
        {
            foreach (var combat in floor.Combats)
            {
                if (!combat.MonsterKilled && combat.ModifierHpDelta > 0)
                {
                    saves.Add(new WarrantSave(combat.Hero, combat.Floor, combat.MonsterKind, combat.DamageTaken));
                }
            }
        }

        return saves.ToImmutable();
    }
}
