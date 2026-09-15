using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// P2-MEMORY-01 (P2-R16's vocabulary, P2-R29's worst instance): the ONE place a <see
/// cref="BeatType"/> becomes player-facing text. Before this table existed, the Ledger's beat
/// rows rendered the raw enum member name as a prefix — "KillingBlow: Emberbite turned the
/// killing blow (floor 3)" — which is not merely jargon, it is REDUNDANT, since <c>Detail</c>
/// already carries the full sentence. `LedgerModal` drops the prefix outright rather than
/// translating it in place (see that file's beat-row builder); this table exists so that WHEN a
/// surface needs a short caption instead of the full sentence — the Chronicle Night and the
/// commendation, both later units — every one of them reads the same six phrases. A second copy
/// of this table would drift the moment either surface changed a word.
///
/// <para><b>Deliberately exhaustive with no discard arm</b> (the same idiom as
/// <c>MixBudget.CategoryFor</c>): a future <see cref="BeatType"/> added without an entry here is
/// a compiler warning the moment this file builds, and a <c>SwitchExpressionException</c> the
/// moment anything — including <c>BeatVocabTests</c>'s reflective <see
/// cref="Enum.GetValues{TEnum}"/> sweep — actually asks for its label. Deny-by-default: a new
/// beat type ships silent, never mislabeled.</para>
///
/// <para><see cref="BeatType.ToolAssist"/>'s label ships INERT — the member is reserved for the
/// Engineering add-on with no emitter yet (see the enum's own doc comment); its narrative voice
/// is `P2-LONG-03`'s to add (D10's pin). This table only guarantees that once it starts emitting,
/// it never renders as a raw enum name — it does not itself wire ToolAssist into anything that
/// runs today.</para>
/// </summary>
public static class BeatVocab
{
    /// <summary>The short-label vocabulary (P2-R16). Never render <see cref="BeatType"/> raw.</summary>
    public static string Label(BeatType beat) => beat switch
    {
        BeatType.KillingBlow => "the killing blow",
        BeatType.LethalSave => "a life saved",
        BeatType.BreakpointClear => "the way opened",
        BeatType.Provisioned => "kept them standing",
        BeatType.PotionLifesave => "saved by the draught",
        BeatType.ToolAssist => "the tool that turned it",
    };

    /// <summary>
    /// P2-PROOF-15: how much a beat PROVES, as an integer weight — the second thing a <see
    /// cref="BeatType"/> is, after its label, and it lives here for the same reason the label
    /// does (one table, no drift). Higher leads. Same exhaustive no-discard-arm idiom as <see
    /// cref="Label"/>: a new <see cref="BeatType"/> without an arm here fails to compile rather
    /// than silently ranking last.
    ///
    /// <para><b>Why this order.</b> The ranking is "how much of a person's fate provably turned
    /// on it", read straight off what <c>AttributionEngine</c> actually emits:</para>
    /// <list type="bullet">
    /// <item><see cref="BeatType.PotionLifesave"/> / <see cref="BeatType.LethalSave"/> (<see
    /// cref="LifeSavedRank"/>) — the counterfactual ends in a permanent death. Peers, deliberately:
    /// the draught and the shield save the same hero from the same recorded blow, and a tie between
    /// them falls to depth, never to which mechanism the engine happened to check first.</item>
    /// <item><see cref="BeatType.BreakpointClear"/> — the party could not have taken that floor at
    /// all without the item; a structural gate opened.</item>
    /// <item><see cref="BeatType.Provisioned"/> and <see cref="BeatType.ToolAssist"/> — the fight
    /// was kept going where it would otherwise have broken off. (ToolAssist ships INERT — no
    /// emitter yet, see this class's own note — ranked now only so it can never arrive unranked.)</item>
    /// <item><see cref="BeatType.KillingBlow"/> (<see cref="KillingBlowRank"/>, the family floor) —
    /// the ONE beat with no counterfactual second pass: <c>TellingQuery</c>'s KillingBlowPayload
    /// recomputes one swing and stops ("there the record ends"), and the engine emits it on EVERY
    /// player-crafted kill, decisive or not. It is both the commonest beat and the least
    /// evidentiary, so it must never be what a night opens on while anything else is on the card.
    /// <c>BeatVocabTests</c> pins that as a property over the whole family, not as this pair of
    /// numbers.</item>
    /// </list>
    /// </summary>
    public static int Rank(BeatType beat) => beat switch
    {
        BeatType.PotionLifesave => LifeSavedRank,
        BeatType.LethalSave => LifeSavedRank,
        BeatType.BreakpointClear => 3,
        BeatType.Provisioned => 2,
        BeatType.ToolAssist => 2,
        BeatType.KillingBlow => KillingBlowRank,
    };

    /// <summary>The family floor (see <see cref="Rank"/>): no beat type ranks below a killing blow.</summary>
    public const int KillingBlowRank = 1;

    /// <summary>The family ceiling: the counterfactual ends in a permanent death.</summary>
    public const int LifeSavedRank = 4;

    /// <summary>
    /// P2-PROOF-15: the beats of one night, strongest first — by <see cref="Rank"/>, then by the
    /// deepest floor it happened on, then (stably) in the order the sim emitted them. The sort is
    /// STABLE, so two beats that tie all the way down keep the engine's own emission order; nothing
    /// is dropped, merged, or summarised (law 4 — every beat the sim decided still renders, this
    /// only chooses which one the eye lands on first).
    ///
    /// <para><b>Depth, not a venue's bottom floor.</b> An earlier draft of this unit wanted a
    /// separate sub-tier for "a killing blow on the venue's BOTTOM floor". Two measurements killed
    /// it. First, it is almost entirely subsumed: the bottom floor IS the deepest floor, so the
    /// depth tiebreak below already leads with it in every single-venue night. Second, and fatally,
    /// a beat carries no venue — <c>AttributionBeatEvent</c> has (Beat, Item, Hero, Floor, Detail)
    /// and nothing else — so the venue would have to be read off <c>SimAdapter.LastRevealedExpeditions</c>,
    /// which holds ONE night. Reopening an older ledger day would then silently re-order its beats,
    /// which is exactly the drift the beat-channel clause's own note forbids ("the clause reads the
    /// same historical fact however much later the card is reopened"). Floor is on the beat forever;
    /// the venue is not.</para>
    /// </summary>
    public static ImmutableList<AttributionBeatEvent> LeadFirst(ImmutableList<AttributionBeatEvent> beats) =>
        beats
            .OrderByDescending(beat => Rank(beat.Beat))
            .ThenByDescending(beat => beat.Floor)
            .ToImmutableList();
}
