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

    /// <summary>Hero drinks a Heal consumable below 50% MaxHp — while still ABOVE the flee line
    /// (<see cref="FleeThresholdPct"/>), so preparation is INSURANCE that keeps a hero out of the
    /// danger zone, never a gamble that replaces a safe exit. See <see cref="ShouldDrink"/>.</summary>
    public const int DrinkThresholdPct = 50;

    /// <summary>
    /// The post-floor "too hurt to press deeper" line: 30% MaxHp, strictly between the flee line
    /// (<see cref="FleeThresholdPct"/>, 25) and the drink line (<see cref="DrinkThresholdPct"/>, 50).
    /// See <see cref="IsTooHurtToContinue"/> for why it is its own constant rather than either
    /// neighbour.
    /// </summary>
    public const int TooHurtThresholdPct = 30;

    /// <summary>
    /// Whether a hero who has just CLEARED a floor is hurt enough that the party stops there
    /// instead of descending. Its own line, deliberately: the three bars mean three different
    /// things, and fusing any two of them deletes a decision.
    ///
    /// <para>History, because both neighbours have held this job and both were wrong.
    /// Until #328 (2026-08-01) this was <see cref="ShouldFlee"/> (25%), which the flee-first
    /// ordering made UNREACHABLE — with flee checked at the top of every round and never
    /// cancelled, a hero still standing on a cleared floor took no damage in the killing round,
    /// so they finished at or above the flee line by construction. #328 fixed that by moving the
    /// bar to <see cref="ShouldDrink"/> (50%), which restored reachability but fused "too hurt to
    /// go on" to "wounded enough to drink" — and that lifted the camp's park floor from 25% to
    /// 50%, straight through the <c>[25%,40%)</c> band the vigil's send-the-runner verb aims at.
    /// The verb stopped delivering anything at all (measured: 62 deliveries on 2026-07-18, zero on
    /// 2026-09-02 over the identical sweep), because a camped hero was never under 50%.</para>
    ///
    /// <para>A third constant is what both facts want at once (owner ruling 2026-09-03,
    /// P2-LONG-25). Strictly above the flee line, so the halt stays reachable and #328's bug does
    /// not come back; strictly below the send verb's 40% band, so a party CAN camp genuinely hurt
    /// and the player's sixth decision has two live arms again. The post-floor quaff keeps the
    /// DRINK line — a hero with a salve still tops up after a hard floor — so the heroes who now
    /// camp in the send band are precisely the ones whose packs are empty. That is the audience
    /// the runner exists for.</para>
    ///
    /// <para>Shares <paramref name="thresholdDeltaPct"/> with <see cref="ShouldFlee"/> and
    /// <see cref="ShouldDrink"/> so a Coward's/Braveheart oil shifts all three lines together and
    /// their ordering never inverts. Integer-only, no RNG.</para>
    /// </summary>
    public static bool IsTooHurtToContinue(int hp, int maxHp, int thresholdDeltaPct)
    {
        var threshold = Math.Clamp(TooHurtThresholdPct + thresholdDeltaPct, 0, 100);
        return hp * 100 < threshold * maxHp;
    }

    /// <summary>
    /// Whether the monster's WORST-CASE next blow (max roll) would drop this hero to 0 — the
    /// hero is one hit from death right now. Pure integer arithmetic over the same
    /// <see cref="MonsterDamage"/> formula the fight uses, so it can never disagree with the
    /// damage actually dealt; draws no RNG (it asks "what is the worst the dice could do",
    /// it does not roll them).
    ///
    /// <para>This is the trigger that makes preparation actually protective. A hero can die
    /// while still ABOVE the flee line — hp falls from a "safe" 40% straight through 0 on one
    /// deep-floor hit — so a fixed wounded-% drink line spends salves on scratches that were
    /// never going to kill and leaves the pack empty at the moment that does. Measured: a plain
    /// 50%-line drink left Prepared only 0.9pp better than Reckless on an independent seed
    /// block (inside noise); gating the drink on real lethal risk is what converts the trait
    /// into insurance.</para>
    /// </summary>
    public static bool CouldDieNextRound(int hp, int monsterAttack, int heroDefense) =>
        hp <= MonsterDamage(monsterAttack, RollSides - 1, heroDefense);

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

    public static bool ShouldFlee(int hp, int maxHp) => ShouldFlee(hp, maxHp, 0);

    /// <summary>
    /// Flee predicate with a craft-modifier threshold shift (Phase C U-C1): a Coward's oil raises the
    /// wound line (positive delta → breaks off sooner), a Braveheart oil lowers it (negative delta →
    /// presses on). The effective threshold is clamped to [0,100]. With <paramref name="thresholdDeltaPct"/>
    /// = 0 (no modifier) this is byte-identical to the base predicate. Integer-only, no RNG.
    /// </summary>
    public static bool ShouldFlee(int hp, int maxHp, int thresholdDeltaPct)
    {
        var threshold = Math.Clamp(FleeThresholdPct + thresholdDeltaPct, 0, 100);
        return hp * 100 < threshold * maxHp;
    }

    /// <summary>
    /// Whether a wounded-but-not-fleeing hero drinks a Heal consumable: below
    /// <see cref="DrinkThresholdPct"/> of MaxHp and NOT yet at the flee line. Callers check
    /// <see cref="ShouldFlee"/> FIRST — a hero at the flee line leaves, and no salve talks them
    /// out of it (owner ruling 2026-08-01: "prefer more prepared heroes").
    ///
    /// <para>The quaff used to fire AT the flee line, replacing a guaranteed-survival exit with a
    /// fight the hero could lose — which made the "Prepared" trait actively LETHAL (measured 73%
    /// mortality vs Reckless's 55%: Reckless fled home, Prepared stayed and died). Drinking
    /// EARLIER, while there is still a healthy margin, is the fiction the trait names: the salve
    /// improves a fight the hero was already taking, and fleeing stays the answer to real danger.</para>
    ///
    /// <para>Shares <paramref name="thresholdDeltaPct"/> with <see cref="ShouldFlee"/> so a
    /// Coward's/Braveheart oil shifts both lines together and the drink band never inverts past
    /// the flee band (a Coward's oil raising flee above 50 would otherwise make the hero flee
    /// before ever drinking — correct, and this ordering preserves it). Integer-only, no RNG.</para>
    /// </summary>
    public static bool ShouldDrink(int hp, int maxHp, int thresholdDeltaPct)
    {
        var threshold = Math.Clamp(DrinkThresholdPct + thresholdDeltaPct, 0, 100);
        return hp * 100 < threshold * maxHp;
    }

    public static int StatOf(ItemId? id, ImmutableSortedDictionary<int, Item> items, Func<ItemStats, int> pick) =>
        id is { } real && items.TryGetValue(real.Value, out var item) ? pick(item.Stats) : 0;
}
