using System.Collections.Immutable;
using GameSim.Classes;
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

    /// <summary>A class's flat attack contribution — pure data read (P3). Kept as a named
    /// seam so an add-on class's <see cref="ClassDefinition.BaseAttack"/> flows through the
    /// same math the built-ins use.</summary>
    public static int RoleBaseAttack(ClassDefinition heroClass) => heroClass.BaseAttack;

    /// <summary>Hero attack with the class resolved from the registry (production path).</summary>
    public static int HeroAttack(Hero hero, ImmutableSortedDictionary<int, Item> items) =>
        HeroAttack(hero, ClassRegistry.Require(hero.ClassId), items);

    /// <summary>Hero attack for an explicit class definition — lets an unregistered (e.g.
    /// test/add-on) class flow through the exact same formula.</summary>
    public static int HeroAttack(Hero hero, ClassDefinition heroClass, ImmutableSortedDictionary<int, Item> items) =>
        RoleBaseAttack(heroClass) + hero.Level * 2 + StatOf(hero.Gear.Weapon, items, s => s.Attack);

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
