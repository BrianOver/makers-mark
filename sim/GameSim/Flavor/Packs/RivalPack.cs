using System.Collections.Immutable;

namespace GameSim.Flavor.Packs;

/// <summary>
/// P2-LONG-19: the rival smith's one spoken line — "the absence of proof," rendered through
/// <see cref="FlavorEngine"/> exactly like every other content pack (data only, no behavior, no
/// RNG). Fires once per hero who died carrying nothing the player made (see
/// <see cref="GameSim.Drama.RivalAbsenceQuery"/>) — never about a hero who died in player-marked
/// gear (that moment belongs to the death card, <see cref="LedgerPack.Died"/>) and never twice
/// about the same hero (permadeath, R7: there is only ever one qualifying death per hero).
///
/// <para><b>Deliberately NOT crossed with <see cref="VoiceProfile"/></b>, unlike every other pack
/// here. The rival is one fixed, unwavering voice, not a hero's assigned personality, and the
/// design's own constraint is dignity, not variety — the dramatic/omen registers other packs use
/// would read as gloating or portentous, exactly what this line must never be.</para>
///
/// <para><b>Slots:</b> <c>{hero}</c> only — the plan's own quoted line names nothing else.</para>
///
/// <para><b>Register (load-bearing, MAKERS-MARK.md §11):</b> no gloating, no I-told-you-so, no
/// lesson for the player. Every variant states a plain fact about iron. Sad, not instructive.
/// <c>RivalPackTests</c> pins the vocabulary shut the same way <c>FactionPackTests</c> pins the
/// discount-only vocabulary.</para>
///
/// <para><b>A new pool, not an addition to one</b> (per this unit's own instructions): touches no
/// existing pack's variant count, so no other pack's stable-hash pick shifts.</para>
/// </summary>
public static class RivalPack
{
    /// <summary>The pack's one base key.</summary>
    public const string Absence = "rivalAbsence";

    /// <summary>The slot names this base key's line provides — shared by the caller and the
    /// conformance tests, same shape as every other pack's <c>SlotNames</c>.</summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [Absence] = ["hero"],
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The pack itself. Static readonly: built once, immutable forever.</summary>
    public static readonly FlavorPack Pack = FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            [Absence] = ImmutableList.Create(
                "{hero} fell wearing store-bought iron. It did what iron does. Nobody's name was on it.",
                "{hero} carried nothing anyone signed. The iron held as long as iron holds. No further claim on it.",
                "Nothing {hero} wore came off my anvil, or anyone's. It did the work iron does, and no more.",
                "{hero} went down in plain stock. It served, the way plain stock serves. There was no maker to tell."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Absence] = "{hero} fell wearing store-bought iron. It did what iron does. Nobody's name was on it.",
        });
}
