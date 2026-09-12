using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// P2-HONEST-06: the ONE place a <see cref="QualityGrade"/> or an <see cref="ItemSlot"/> becomes
/// player-facing text. <c>PlayerVocabularyCensusTests</c>' generalized enum-hole scan (the same
/// idiom <see cref="PhaseVocab"/> and <see cref="BeatVocab"/> already established for <see
/// cref="DayPhase"/> and <see cref="BeatType"/>) found both interpolated raw at a dozen call sites
/// — <c>$"[{item.Quality}]"</c>, <c>$"{slot}: ..."</c> — with no declared display standing behind
/// them.
///
/// <para><b>Why this still needed declaring even though the rendered word never changes.</b>
/// Every one of those call sites already agreed on the same spelling ("Fine", "Masterwork",
/// "Weapon"), so there was no live split-brain to point at the way <see cref="PhaseVocab"/>'s own
/// class doc could. The jargon rule (P2-R29) does not carve out an exception for "agrees by
/// accident, so far": <c>MainUi</c>'s campaign-act chip and tooltip made exactly that mistake with
/// <see cref="GameSim.Contracts.CampaignAct"/> (chip via a roman-numeral table, tooltip raw) before
/// this unit's own first run caught it. One table per enum, one spelling, so a future rename or a
/// new grade/slot costs one edit here instead of an audit of every call site.</para>
///
/// <para>Both tables are exhaustive with a <c>ToString()</c> discard arm (unlike <see
/// cref="BeatVocab"/>'s deliberately-throwing one): a grade or slot added later renders honestly
/// under its own enum name on day one rather than crashing the panel that forgot to update this
/// file, and <c>ItemVocabTests</c>' reflective sweep over <see cref="System.Enum.GetValues{T}"/>
/// still catches the gap by name so it does not stay silent forever.</para>
/// </summary>
public static class ItemVocab
{
    /// <summary>The word a quality grade renders as. Never render <see cref="QualityGrade"/> raw.</summary>
    public static string Display(QualityGrade grade) => grade switch
    {
        QualityGrade.Poor => "Poor",
        QualityGrade.Common => "Common",
        QualityGrade.Fine => "Fine",
        QualityGrade.Superior => "Superior",
        QualityGrade.Masterwork => "Masterwork",
        _ => grade.ToString(),
    };

    /// <summary>The word an equipment slot renders as. Never render <see cref="ItemSlot"/> raw.</summary>
    public static string Display(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => "Weapon",
        ItemSlot.Shield => "Shield",
        ItemSlot.Armor => "Armor",
        ItemSlot.Consumable => "Consumable",
        ItemSlot.Trinket => "Trinket",
        _ => slot.ToString(),
    };
}
