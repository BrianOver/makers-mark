using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Expedition;

/// <summary>
/// The pure expedition function (KTD5): (party, gear, floor, rng) → ExpeditionResult,
/// computed at departure, revealed at Evening. Every roll is recorded into the event
/// log (KTD6) so attribution recomputes counterfactually without touching RNG.
/// </summary>
public static class ExpeditionResolver
{
    public static ExpeditionResult Resolve(
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        int targetFloor,
        IDeterministicRng rng)
    {
        if (party.IsEmpty)
        {
            throw new ArgumentException("Expedition party cannot be empty.", nameof(party));
        }

        targetFloor = Math.Clamp(targetFloor, 1, MonsterTable.FloorCount);

        var hp = party.ToDictionary(h => h.Id.Value, h => h.MaxHp);
        var gold = party.ToDictionary(h => h.Id.Value, _ => 0);
        var dead = new HashSet<int>();
        var floors = ImmutableList.CreateBuilder<FloorOutcome>();
        var loot = ImmutableList.CreateBuilder<OreLoot>();
        var deepestCleared = 0;

        for (var floor = 1; floor <= targetFloor; floor++)
        {
            var fighters = party.Where(h => !dead.Contains(h.Id.Value)).ToList();
            if (fighters.Count == 0)
            {
                break;
            }

            // STRUCTURAL gate (AE3): under-geared parties retreat at the gate — no roll involved.
            if (CombatMath.PartyAveragePower(fighters, items) < MonsterTable.Gate(floor))
            {
                break;
            }

            var combats = ImmutableList.CreateBuilder<CombatEvent>();
            var floorCleared = true;

            foreach (var hero in fighters) // HeroId order — deterministic
            {
                var outcome = FightMonster(hero, items, floor, hp, rng, combats);
                if (outcome == FightOutcome.HeroDied)
                {
                    dead.Add(hero.Id.Value);
                    floorCleared = false; // a death leaves the hero's monster alive — floor uncleared
                }
                else if (outcome == FightOutcome.MonsterKilled)
                {
                    gold[hero.Id.Value] += MonsterTable.GoldPerKill(floor);
                }
                else
                {
                    floorCleared = false; // a flee leaves the floor uncleared
                }
            }

            var anyoneStanding = party.Any(h => !dead.Contains(h.Id.Value));
            floorCleared &= anyoneStanding;
            floors.Add(new FloorOutcome(floor, floorCleared, combats.ToImmutable()));

            if (!floorCleared)
            {
                break;
            }

            deepestCleared = floor;

            // Ore loot for standing survivors (R6): quantity 1-3, rarity by floor.
            foreach (var hero in party.Where(h => !dead.Contains(h.Id.Value)))
            {
                loot.Add(new OreLoot(hero.Id, MonsterTable.OreKey(floor), rng.NextInt(1, 4)));
            }

            // Anyone too hurt to continue ends the expedition after banking the clear.
            if (party.Where(h => !dead.Contains(h.Id.Value))
                     .Any(h => CombatMath.ShouldFlee(hp[h.Id.Value], h.MaxHp)))
            {
                break;
            }
        }

        var survivors = party.Where(h => !dead.Contains(h.Id.Value)).Select(h => h.Id).ToImmutableList();
        var deaths = party.Where(h => dead.Contains(h.Id.Value)).Select(h => h.Id).ToImmutableList();
        var allFloors = floors.ToImmutable();

        // Attribution runs AFTER resolution, over recorded rolls only (KTD6).
        var beats = AttributionEngine.ComputeBeats(allFloors, party, items);

        return new ExpeditionResult(
            party.Select(h => h.Id).ToImmutableList(),
            targetFloor,
            deepestCleared,
            allFloors,
            survivors,
            deaths,
            beats,
            loot.ToImmutable(),
            gold.ToImmutableSortedDictionary());
    }

    private enum FightOutcome
    {
        MonsterKilled,
        HeroFled,
        HeroDied,
    }

    private static FightOutcome FightMonster(
        Hero hero,
        ImmutableSortedDictionary<int, Item> items,
        int floor,
        Dictionary<int, int> hp,
        IDeterministicRng rng,
        ImmutableList<CombatEvent>.Builder combats)
    {
        var monsterHp = MonsterTable.MonsterHp(floor);
        var heroAttack = CombatMath.HeroAttack(hero, items);
        var heroDefense = CombatMath.HeroDefense(hero, items);

        while (true)
        {
            var rolls = ImmutableList.CreateBuilder<int>();

            var heroRoll = rng.NextInt(0, CombatMath.RollSides);
            rolls.Add(heroRoll);
            var dealt = CombatMath.HeroDamage(heroAttack, heroRoll, MonsterTable.MonsterDefense(floor));
            monsterHp -= dealt;
            var monsterKilled = monsterHp <= 0;

            var taken = 0;
            if (!monsterKilled)
            {
                var monsterRoll = rng.NextInt(0, CombatMath.RollSides);
                rolls.Add(monsterRoll);
                taken = CombatMath.MonsterDamage(MonsterTable.MonsterAttack(floor), monsterRoll, heroDefense);
                hp[hero.Id.Value] -= taken;
            }

            combats.Add(new CombatEvent(
                floor,
                hero.Id,
                MonsterTable.MonsterKind(floor),
                rolls.ToImmutable(),
                dealt,
                taken,
                monsterKilled,
                monsterKilled ? hero.Gear.Weapon : null));

            if (monsterKilled)
            {
                return FightOutcome.MonsterKilled;
            }

            if (hp[hero.Id.Value] <= 0)
            {
                return FightOutcome.HeroDied;
            }

            if (CombatMath.ShouldFlee(hp[hero.Id.Value], hero.MaxHp))
            {
                return FightOutcome.HeroFled;
            }
        }
    }
}
