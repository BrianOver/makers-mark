using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Expedition;

/// <summary>
/// Proves "your item mattered" as a computed fact (R11, KTD6). Recomputes combat math
/// over the RECORDED rolls with a player item's stats removed — never draws RNG, never
/// estimates. Beats are only emitted for player-crafted items (maker's mark present).
/// Multi-item overlap rule (v1): each defensive item is evaluated independently; two
/// independently-decisive items each earn a beat for the same survival.
/// </summary>
public static class AttributionEngine
{
    public static ImmutableList<AttributionBeat> ComputeBeats(
        ImmutableList<FloorOutcome> floors,
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items)
    {
        var beats = ImmutableList.CreateBuilder<AttributionBeat>();
        var heroesById = party.ToDictionary(h => h.Id.Value);

        // Replay per-hero HP from the recorded event stream.
        var hp = party.ToDictionary(h => h.Id.Value, h => h.MaxHp);

        foreach (var floor in floors)
        {
            // The roster alive when this floor began — the population the resolver's
            // structural gate actually faced (ExpeditionResolver). Snapshot before the
            // combat replay below mutates hp, so the breakpoint counterfactual (KTD6)
            // recomputes over the same set, not the post-combat survivors.
            var floorStartFighters = party.Where(h => hp[h.Id.Value] > 0).ToList();

            // 1-based round number per hero within this floor's fight (one combat
            // event per round) — orders recorded heals against recorded damage (P2).
            var roundsByHero = new Dictionary<int, int>();

            foreach (var combat in floor.Combats)
            {
                var hero = heroesById[combat.Hero.Value];
                var roundNumber = roundsByHero.TryGetValue(combat.Hero.Value, out var prior) ? prior + 1 : 1;
                roundsByHero[combat.Hero.Value] = roundNumber;

                // P2: a quaff at the top of this round lands BEFORE the monster's hit;
                // the post-floor quaff (Round past the fight) lands after it, below.
                foreach (var use in combat.Uses)
                {
                    if (use.Round <= roundNumber)
                    {
                        hp[hero.Id.Value] += use.HpAfter - use.HpBefore;
                    }
                }

                // AE1 — killing blow by a player-crafted weapon.
                if (combat.MonsterKilled
                    && combat.KillingItem is { } killer
                    && IsPlayerCrafted(killer, items))
                {
                    beats.Add(new AttributionBeat(
                        BeatType.KillingBlow, killer, hero.Id, combat.Floor,
                        $"{items[killer.Value].Name} landed the killing blow on the {combat.MonsterKind}"));
                }

                // AE2 — lethal save: recompute the taken hit without each defensive player item.
                if (combat.DamageTaken > 0 && combat.RecordedRolls.Count >= 2)
                {
                    var monsterRoll = combat.RecordedRolls[1];
                    var hpBefore = hp[hero.Id.Value];
                    var actualAfter = hpBefore - combat.DamageTaken;

                    foreach (var slot in new[] { ItemSlot.Shield, ItemSlot.Armor })
                    {
                        var itemId = hero.Gear.Slot(slot);
                        if (itemId is not { } defId || !IsPlayerCrafted(defId, items))
                        {
                            continue;
                        }

                        var defWithout = CombatMath.HeroDefense(hero, items)
                                         - items[defId.Value].Stats.Defense;
                        var takenWithout = CombatMath.MonsterDamage(
                            MonsterTable.MonsterAttack(combat.Floor), monsterRoll, defWithout);

                        if (actualAfter > 0 && hpBefore - takenWithout <= 0)
                        {
                            beats.Add(new AttributionBeat(
                                BeatType.LethalSave, defId, hero.Id, combat.Floor,
                                $"{items[defId.Value].Name} turned a lethal {combat.MonsterKind} hit"));
                        }
                    }
                }

                hp[hero.Id.Value] -= combat.DamageTaken;

                foreach (var use in combat.Uses)
                {
                    if (use.Round > roundNumber)
                    {
                        hp[hero.Id.Value] += use.HpAfter - use.HpBefore;
                    }
                }
            }

            // AE-adjacent breakpoint beat: floor cleared, and removing a player item's stats
            // would have dropped the party average below the structural gate.
            if (floor.Cleared)
            {
                var gate = MonsterTable.Gate(floor.Floor);
                var avg = CombatMath.PartyAveragePower(floorStartFighters, items);

                foreach (var hero in floorStartFighters)
                {
                    foreach (var itemId in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor })
                    {
                        if (itemId is not { } id || !IsPlayerCrafted(id, items))
                        {
                            continue;
                        }

                        var without = items.Remove(id.Value);
                        if (avg >= gate && CombatMath.PartyAveragePower(floorStartFighters, without) < gate)
                        {
                            beats.Add(new AttributionBeat(
                                BeatType.BreakpointClear, id, hero.Id, floor.Floor,
                                $"{items[id.Value].Name} carried the party past the floor {floor.Floor} gate"));
                        }
                    }
                }
            }
        }

        AddConsumableBeats(floors, heroesById, items, beats);

        return beats.ToImmutable();
    }

    /// <summary>
    /// P2 consumable beats, computed from recorded <see cref="ConsumableUse"/> data only —
    /// never a fresh RNG draw. Every use happened where the hero would otherwise have
    /// fled, so every use is Provisioned-eligible; ONE beat is emitted per hero per
    /// expedition, for the hero's first player-marked use. It upgrades to
    /// <see cref="BeatType.PotionLifesave"/> when replaying the SAME fight's subsequent
    /// recorded DamageTaken from the use's HpBefore would have reached hp &lt;= 0 while
    /// the hero actually survived the fight.
    /// </summary>
    private static void AddConsumableBeats(
        ImmutableList<FloorOutcome> floors,
        Dictionary<int, Hero> heroesById,
        ImmutableSortedDictionary<int, Item> items,
        ImmutableList<AttributionBeat>.Builder beats)
    {
        var credited = new HashSet<int>(); // heroes already given their one beat

        foreach (var floor in floors)
        {
            // A hero fights at most once per floor; their events on the floor ARE the
            // fight, in round order (one event per round).
            var fights = new Dictionary<int, List<CombatEvent>>();
            foreach (var combat in floor.Combats)
            {
                if (!fights.TryGetValue(combat.Hero.Value, out var fight))
                {
                    fight = [];
                    fights[combat.Hero.Value] = fight;
                }

                fight.Add(combat);
            }

            foreach (var combat in floor.Combats)
            {
                foreach (var use in combat.Uses)
                {
                    if (credited.Contains(combat.Hero.Value) || !IsPlayerCrafted(use.Item, items))
                    {
                        continue;
                    }

                    credited.Add(combat.Hero.Value);

                    var hero = heroesById[combat.Hero.Value];
                    var fight = fights[combat.Hero.Value];

                    // Counterfactual: recorded damage from the use's round onward (a
                    // use's own round lands its damage after the quaff; a post-floor
                    // use sits past every round, so it sums nothing). Survival check
                    // replays the actual trajectory, so later heals count there.
                    var damageFromRound = 0;
                    var laterHeals = 0;
                    for (var r = 0; r < fight.Count; r++)
                    {
                        if (r + 1 >= use.Round)
                        {
                            damageFromRound += fight[r].DamageTaken;
                        }

                        foreach (var other in fight[r].Uses)
                        {
                            if (other.Round > use.Round)
                            {
                                laterHeals += other.HpAfter - other.HpBefore;
                            }
                        }
                    }

                    var wouldHaveDied = use.HpBefore - damageFromRound <= 0;
                    var survivedFight = use.HpAfter - damageFromRound + laterHeals > 0;

                    beats.Add(wouldHaveDied && survivedFight
                        ? new AttributionBeat(
                            BeatType.PotionLifesave, use.Item, hero.Id, combat.Floor,
                            $"{items[use.Item.Value].Name} saved {hero.Name}'s life")
                        : new AttributionBeat(
                            BeatType.Provisioned, use.Item, hero.Id, combat.Floor,
                            $"{items[use.Item.Value].Name} kept {hero.Name} fighting on floor {combat.Floor}"));
                }
            }
        }
    }

    private static bool IsPlayerCrafted(ItemId id, ImmutableSortedDictionary<int, Item> items) =>
        items.TryGetValue(id.Value, out var item) && item.PlayerCrafted;
}
