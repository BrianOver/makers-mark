using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Expedition;

/// <summary>
/// The integer combat formulas, shared by the forward resolver and the counterfactual
/// attribution engine (KTD6) so "what if the item weren't there" recomputes EXACTLY
/// the same math over the same recorded rolls. No floats, no RNG in here.
/// </summary>
public static class CombatMath
{
    public const int RollSides = 6;          // rolls are NextInt(0, 6)
    public const int FleeThresholdPct = 25;  // hero flees below 25% MaxHp

    public static int RoleBaseAttack(HeroRole role) => role switch
    {
        HeroRole.Vanguard => 4,
        HeroRole.Striker => 6,
        HeroRole.Mystic => 3,
        _ => 0,
    };

    public static int HeroAttack(Hero hero, ImmutableSortedDictionary<int, Item> items) =>
        RoleBaseAttack(hero.Role) + hero.Level * 2 + StatOf(hero.Gear.Weapon, items, s => s.Attack);

    public static int HeroDefense(Hero hero, ImmutableSortedDictionary<int, Item> items) =>
        hero.Level
        + StatOf(hero.Gear.Shield, items, s => s.Defense)
        + StatOf(hero.Gear.Armor, items, s => s.Defense);

    /// <summary>Attack + defense: the number floor gates check (breakpoint beats recompute this).</summary>
    public static int EffectivePower(Hero hero, ImmutableSortedDictionary<int, Item> items) =>
        HeroAttack(hero, items) + HeroDefense(hero, items);

    public static int PartyAveragePower(IEnumerable<Hero> party, ImmutableSortedDictionary<int, Item> items)
    {
        var list = party.ToList();
        return list.Count == 0 ? 0 : list.Sum(h => EffectivePower(h, items)) / list.Count;
    }

    /// <summary>Damage a hero deals with a recorded roll. Pure — reused counterfactually.</summary>
    public static int HeroDamage(int heroAttack, int roll, int monsterDefense) =>
        Math.Max(1, heroAttack + roll - monsterDefense);

    /// <summary>Damage a monster deals with a recorded roll. Pure — reused counterfactually.</summary>
    public static int MonsterDamage(int monsterAttack, int roll, int heroDefense) =>
        Math.Max(1, monsterAttack + roll - heroDefense);

    public static bool ShouldFlee(int hp, int maxHp) => hp * 100 < FleeThresholdPct * maxHp;

    public static int StatOf(ItemId? id, ImmutableSortedDictionary<int, Item> items, Func<ItemStats, int> pick) =>
        id is { } real && items.TryGetValue(real.Value, out var item) ? pick(item.Stats) : 0;
}
