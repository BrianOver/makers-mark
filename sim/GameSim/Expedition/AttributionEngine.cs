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

            foreach (var combat in floor.Combats)
            {
                var hero = heroesById[combat.Hero.Value];

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

        return beats.ToImmutable();
    }

    private static bool IsPlayerCrafted(ItemId id, ImmutableSortedDictionary<int, Item> items) =>
        items.TryGetValue(id.Value, out var item) && item.PlayerCrafted;
}
