using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>
/// Pure quality roll (R4). Integer math only, exactly ONE <see cref="IDeterministicRng.Roll100"/>
/// draw per craft — draw count is part of the determinism contract (KTD4).
///
/// THE THRESHOLD TABLE (tests assert this exact table — change both together):
///
///   effective = Roll100() + shift          // Roll100 is uniform in [0, 100)
///
///   shift = 8 * (materialGrade + (material-mastery unlocked ? 1 : 0) - recipe.Tier)
///         + (keen-eye unlocked          ? 5 : 0)
///         + (master-touch unlocked      ? 7 : 0)
///         + (legendary-craft unlocked   ? 8 : 0)
///         + (weapon-specialist unlocked and recipe.Slot == Weapon ? 5 : 0)
///
///   grade:  effective &lt;= 14   → Poor
///           15 .. 64          → Common
///           65 .. 89          → Fine
///           90 .. 98          → Superior
///           effective &gt;= 99   → Masterwork
///
/// Base odds at shift 0 (material grade == recipe tier, no quality talents):
/// Poor 15%, Common 50%, Fine 25%, Superior 9%, Masterwork 1%.
/// Each material grade above (below) the recipe tier shifts the roll +8 (-8):
/// e.g. mithril (4) on a tier-1 recipe → +24; copper (1) on a tier-3 recipe → -16,
/// which makes Superior/Masterwork unreachable — cheap materials cap the ceiling.
/// Talents not listed above (material-efficiency, tier unlocks) never touch the roll,
/// and locked nodes contribute nothing: only ids present in <paramref name="unlockedTalents"/> count.
/// </summary>
public static class QualityRoller
{
    public static QualityGrade Roll(Recipe recipe, int materialGrade, ImmutableSortedSet<string> unlockedTalents, IDeterministicRng rng)
    {
        var effectiveGrade = materialGrade + (unlockedTalents.Contains(TalentTree.MaterialMastery) ? 1 : 0);
        var shift = 8 * (effectiveGrade - recipe.Tier);

        if (unlockedTalents.Contains(TalentTree.KeenEye))
        {
            shift += 5;
        }

        if (unlockedTalents.Contains(TalentTree.MasterTouch))
        {
            shift += 7;
        }

        if (unlockedTalents.Contains(TalentTree.LegendaryCraft))
        {
            shift += 8;
        }

        if (recipe.Slot == ItemSlot.Weapon && unlockedTalents.Contains(TalentTree.WeaponSpecialist))
        {
            shift += 5;
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
