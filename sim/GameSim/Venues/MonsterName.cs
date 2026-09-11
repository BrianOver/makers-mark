using System;

namespace GameSim.Venues;

/// <summary>
/// P2-PROOF-12 (§11.15): the ONE place a recorded <c>MonsterKind</c> becomes a noun phrase in
/// player copy — the monster-name sibling of <c>GodotClient.Ui.PhaseVocab</c>, and it exists for
/// exactly the reason that table does.
///
/// <para><b>The defect.</b> Every venue's bottom floor is a named boss — "The Forgeworm", "The
/// Undertow", "The Bellows-Mad", "The Undying Forge-Heart" — and a proper name already carries its
/// article. <c>ExpeditionRevealSystem.DeathReport</c> knew that and tested for it;
/// <c>AttributionEngine</c> did not, at either of its two sites. So the deepest, most dramatic beat
/// the game can produce read <i>"Greataxe landed the killing blow on the The Forgeworm"</i>, and the
/// lethal save read <i>"turned a lethal The Forgeworm hit"</i> — both measured verbatim in a
/// 20-seed sweep, inside the exact sentence <c>CLAUDE.md</c>'s epigraph is written from.</para>
///
/// <para><b>Why a helper rather than a fix at each site.</b> Patching the two call sites would have
/// left three hand-written copies of the same three-line rule and no reason a fourth would not
/// appear — which is precisely how this one arrived. <c>MonsterNameCensusTests</c> makes a fourth
/// copy a red build.</para>
///
/// <para>Pure string shaping over a fact the venue already recorded: no state, no RNG, no clock,
/// no transcendental math — sim-pure by construction (KTD2).</para>
/// </summary>
public static class MonsterName
{
    /// <summary>The marker a venue uses to say "this is a name, not a kind". Every venue's boss
    /// floor already writes it and nothing else does, so the rule needs no second registry: the
    /// name IS the declaration.</summary>
    private const string ProperNamePrefix = "The ";

    /// <summary>True when <paramref name="kind"/> is a proper name carrying its own article — the
    /// bottom-floor boss of every venue.</summary>
    public static bool IsProperName(string kind) =>
        kind.StartsWith(ProperNamePrefix, StringComparison.Ordinal);

    /// <summary>
    /// The definite form, for "…landed the killing blow on <b>{this}</b>": "the Cave Rat", but
    /// "The Forgeworm" — a proper name takes no second article.
    /// </summary>
    public static string Definite(string kind) => IsProperName(kind) ? kind : $"the {kind}";

    /// <summary>
    /// The indefinite form, for "…slain by <b>{this}</b>": "a Cave Rat", but "The Forgeworm". This
    /// is <c>ExpeditionRevealSystem.DeathReport</c>'s own original rule, now this method rather
    /// than a second copy of it.
    /// </summary>
    public static string Indefinite(string kind) => IsProperName(kind) ? kind : $"a {kind}";

    /// <summary>
    /// The attributive form, for "…turned a lethal <b>{this}</b>". A proper name cannot sit
    /// attributively without reading as a typo ("a lethal The Forgeworm hit"), so it becomes a
    /// prepositional phrase instead — the shape stays in here rather than at the call site, because
    /// a call site deciding its own grammar is the whole defect this class closes.
    /// </summary>
    public static string AttributiveBlow(string kind) =>
        IsProperName(kind) ? $"blow from {kind}" : $"{kind} hit";
}
