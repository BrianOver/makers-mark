using System;
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
}
