using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>
/// Phase C U-C1 — the craft-modifier registry (slice 1). Pure static data + integer math: no RNG,
/// no IO, no clock, no transcendental <c>Math.*</c> (KTD2). Every modifier's effect is an integer
/// delta or flag on a hero-AI decision threshold read by the expedition resolver, never a passive
/// stat bump — so composing modifiers changes BEHAVIOUR (who retreats, who heals through a fight,
/// who banks extra ore), which is what makes craft combinatorial rather than a bigger-number ladder.
///
/// Slice 1 ships four modifiers across all three families with clean resolver seams and zero
/// attribution-counterfactual rework (see <c>ExpeditionResolver</c> / <c>AttributionEngine</c>):
///   • Coward's / Braveheart quench oils — shift the flee threshold (survival trajectory).
///   • Leech rune — heals the bearer on a monster kill, recorded as <c>CombatEvent.ModifierHpDelta</c>.
///   • Lodestone fitting — +ore per loot roll (post-draw integer add; no RNG reorder).
/// Slice 2 wires player-facing composition at the forge + the remaining families (elemental oils,
/// damage runes, movement fittings) with the attribution threading those combat runes require.
/// </summary>
public static class CraftModifiers
{
    public const string CowardsOil = "cowards-oil";
    public const string BraveheartOil = "braveheart-oil";
    public const string LeechRune = "leech-rune";
    public const string LodestoneFitting = "lodestone-fitting";

    /// <summary>Display metadata for a modifier id (order-screen + attribution readouts). DATA only.</summary>
    public sealed record ModifierDef(string Id, ModifierFamily Family, string DisplayName, string Tooltip);

    private static readonly ImmutableDictionary<string, ModifierDef> Registry =
        new[]
        {
            new ModifierDef(CowardsOil, ModifierFamily.QuenchOil, "Coward's Oil",
                "The bearer breaks off sooner — retreats at a higher wound line."),
            new ModifierDef(BraveheartOil, ModifierFamily.QuenchOil, "Braveheart Oil",
                "The bearer presses on through wounds that would send others home."),
            new ModifierDef(LeechRune, ModifierFamily.Rune, "Leech Rune",
                "Draws a little life from each felled foe."),
            new ModifierDef(LodestoneFitting, ModifierFamily.Fitting, "Lodestone Fitting",
                "Pulls the bearer toward richer seams — more ore per haul."),
        }.ToImmutableDictionary(d => d.Id);

    /// <summary>The registered modifier ids, family-tagged (slice-1 set). Used by the dominance test
    /// and the (slice-2) forge composer to enumerate the pool.</summary>
    public static ImmutableArray<string> All { get; } =
        [CowardsOil, BraveheartOil, LeechRune, LodestoneFitting];

    public static ModifierDef? Definition(string id) => Registry.GetValueOrDefault(id);

    /// <summary>True iff <paramref name="id"/> is a registered modifier of the given family.</summary>
    public static bool IsFamily(string id, ModifierFamily family) =>
        Registry.TryGetValue(id, out var def) && def.Family == family;

    /// <summary>
    /// The aggregate integer effect a hero's equipped gear grants this expedition — summed over every
    /// modifier on every equipped item. Read ONCE per hero by the resolver (never in the hot combat
    /// loop's inner rounds). Pure.
    /// </summary>
    public readonly record struct HeroModifierEffect(int FleeThresholdDeltaPct, int HealOnKill, int BonusOrePerLoot)
    {
        public static readonly HeroModifierEffect None = new(0, 0, 0);

        public HeroModifierEffect Add(HeroModifierEffect other) => new(
            FleeThresholdDeltaPct + other.FleeThresholdDeltaPct,
            HealOnKill + other.HealOnKill,
            BonusOrePerLoot + other.BonusOrePerLoot);
    }

    /// <summary>Effect of a single stamped modifier, scaled by its (material-tier-capped) tier.</summary>
    public static HeroModifierEffect EffectOf(CraftModifier m)
    {
        var t = Math.Max(1, m.Tier);
        return m.Id switch
        {
            CowardsOil => new HeroModifierEffect(FleeThresholdDeltaPct: 8 * t, 0, 0),
            BraveheartOil => new HeroModifierEffect(FleeThresholdDeltaPct: -8 * t, 0, 0),
            LeechRune => new HeroModifierEffect(0, HealOnKill: 3 * t, 0),
            LodestoneFitting => new HeroModifierEffect(0, 0, BonusOrePerLoot: 1 * t),
            _ => HeroModifierEffect.None, // unknown id (forward-compat with future/add-on modifiers)
        };
    }

    /// <summary>Aggregate effect across every modifier on every item a hero has equipped. Pure.</summary>
    public static HeroModifierEffect ForGear(GearSet gear, ImmutableSortedDictionary<int, Item> items)
    {
        var acc = HeroModifierEffect.None;
        foreach (var slot in new[] { gear.Weapon, gear.Shield, gear.Armor, gear.Trinket })
        {
            if (slot is { } id && items.TryGetValue(id.Value, out var item))
            {
                foreach (var mod in item.Modifiers)
                {
                    acc = acc.Add(EffectOf(mod));
                }
            }
        }

        return acc;
    }

    // ---- Composition rules (Vagrant-Story trinity; consumed by the slice-2 forge composer, tested now) ----

    /// <summary>Material tier caps the modifier tier an item can hold (iron T1, mithril T2). Unknown
    /// keys default to T1 — the conservative floor. Slice 2 expands this table with the full ore set.</summary>
    public static int MaterialTierCap(string materialKey) => materialKey switch
    {
        "mithril" or "adamant" or "orichalcum" => 2,
        _ => 1,
    };

    /// <summary>
    /// How many modifier slots a craft grade unlocks: Poor holds none, Common one, Fine two, Superior
    /// all three; Masterwork holds all three AND grants a +1 potency step (see
    /// <see cref="MasterworkPotencyStep"/>). This is the craft-execution → composition linkage: a
    /// better craft is a wider canvas, never just a bigger number.
    /// </summary>
    public static int SlotsForGrade(QualityGrade grade) => grade switch
    {
        QualityGrade.Poor => 0,
        QualityGrade.Common => 1,
        QualityGrade.Fine => 2,
        _ => 3, // Superior + Masterwork
    };

    /// <summary>Masterwork overshoot: one modifier on a masterwork item may step up +1 tier beyond the
    /// material cap (Hades "S" bonus). Slice-2 composition applies it to the highest-value modifier.</summary>
    public static bool MasterworkPotencyStep(QualityGrade grade) => grade == QualityGrade.Masterwork;

    /// <summary>
    /// Validity of stamping <paramref name="modifier"/> onto an item of the given grade + material,
    /// already carrying <paramref name="existing"/> modifiers: the modifier must be registered, its
    /// tier within the material cap (+1 on a masterwork), and its family not already occupied
    /// (slot exclusivity — no stacking degeneracy). Pure predicate.
    /// </summary>
    public static bool CanApply(
        CraftModifier modifier,
        QualityGrade grade,
        string materialKey,
        IEnumerable<CraftModifier> existing)
    {
        if (Definition(modifier.Id) is not { } def || def.Family != modifier.Family)
        {
            return false;
        }

        var cap = MaterialTierCap(materialKey) + (MasterworkPotencyStep(grade) ? 1 : 0);
        if (modifier.Tier < 1 || modifier.Tier > cap)
        {
            return false;
        }

        var occupied = existing.Count();
        if (occupied >= SlotsForGrade(grade))
        {
            return false;
        }

        return existing.All(e => e.Family != modifier.Family);
    }
}
