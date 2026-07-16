using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Crafting;

/// <summary>
/// Pure quality roll (R4), now profession-parameterized (P1): the talent-driven shifts are
/// read from the profession's <see cref="ProfessionQualityModel"/> instead of hardcoded
/// blacksmith node ids. The universal quality math — the ±8-per-grade material step and the
/// grade threshold table — is shared by every profession and stays here. Integer math only,
/// exactly ONE <see cref="IDeterministicRng.Roll100"/> draw per craft — draw count is part of
/// the determinism contract (KTD4). Iterating the shift maps consumes no RNG and, being
/// integer addition, is order-independent, so the distribution is byte-identical.
///
/// THE THRESHOLD TABLE (tests assert this exact table — change both together):
///
///   effective = Roll100() + shift          // Roll100 is uniform in [0, 100)
///
///   shift = 8 * (materialGrade + (material-mastery node unlocked ? 1 : 0) - recipe.Tier)
///         + sum of quality.FlatShifts[node] for each unlocked flat node
///         + sum of quality.SlotShifts[node].Shift for each unlocked slot node whose slot matches
///
///   grade:  effective &lt;= 14   → Poor
///           15 .. 64          → Common
///           65 .. 89          → Fine
///           90 .. 98          → Superior
///           effective &gt;= 99   → Masterwork
///
/// For the blacksmith the model reproduces the exact original numbers (keen-eye +5,
/// master-touch +7, legendary-craft +8, weapon-specialist +5 on weapons, material-mastery
/// +1 grade). Base odds at shift 0 (material grade == recipe tier, no quality talents):
/// Poor 15%, Common 50%, Fine 25%, Superior 9%, Masterwork 1%. Each material grade above
/// (below) the recipe tier shifts the roll +8 (-8). Nodes not in the quality model (material
/// efficiency, tier unlocks) never touch the roll, and locked nodes contribute nothing: only
/// ids present in <paramref name="unlockedTalents"/> count.
/// </summary>
public static class QualityRoller
{
    public static QualityGrade Roll(Recipe recipe, int materialGrade, ImmutableSortedSet<string> unlockedTalents, ProfessionQualityModel quality, IDeterministicRng rng)
    {
        var masteryGrade = quality.MaterialMasteryNode is { } mastery && unlockedTalents.Contains(mastery) ? 1 : 0;
        var effectiveGrade = materialGrade + masteryGrade;
        var shift = 8 * (effectiveGrade - recipe.Tier);

        foreach (var (nodeId, amount) in quality.FlatShifts)
        {
            if (unlockedTalents.Contains(nodeId))
            {
                shift += amount;
            }
        }

        foreach (var (nodeId, slotShift) in quality.SlotShifts)
        {
            if (recipe.Slot == slotShift.Slot && unlockedTalents.Contains(nodeId))
            {
                shift += slotShift.Shift;
            }
        }

        var effective = rng.Roll100() + shift;
        if (effective <= 14)
        {
            return QualityGrade.Poor;
        }

        if (effective <= 64)
        {
            return QualityGrade.Common;
        }

        if (effective <= 89)
        {
            return QualityGrade.Fine;
        }

        if (effective <= 98)
        {
            return QualityGrade.Superior;
        }

        return QualityGrade.Masterwork;
    }
}
