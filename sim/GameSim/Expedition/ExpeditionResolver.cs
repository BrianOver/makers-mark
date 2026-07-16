using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Expedition;

/// <summary>
/// The pure expedition function (KTD5): (party, gear, venue, floor, rng) → ExpeditionResult,
/// computed at departure, revealed at Evening. Every roll is recorded into the event
/// log (KTD6) so attribution recomputes counterfactually without touching RNG.
/// The <paramref name="venue"/> supplies all floor NUMBERS (gate, monster stats, gold, ore,
/// floor count); the combat math and RNG draw order are unchanged, so for the Mine the result
/// is byte-identical to the pre-P4 static-table resolver (P4).
/// </summary>
public static class ExpeditionResolver
{
    public static ExpeditionResult Resolve(
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        int targetFloor,
        IDeterministicRng rng)
    {
        if (party.IsEmpty)
        {
            throw new ArgumentException("Expedition party cannot be empty.", nameof(party));
        }

        targetFloor = Math.Clamp(targetFloor, 1, venue.FloorCount);

        var hp = party.ToDictionary(h => h.Id.Value, h => h.MaxHp);
        // Working copy of each hero's pack (P2): quaffs consume from the FRONT-most
        // matching item in list order; the persistent Hero.Pack is depleted at the
        // Evening reveal from the recorded ConsumableUses.
        var packs = party.ToDictionary(h => h.Id.Value, h => h.Pack.ToList());
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
            if (CombatMath.PartyAveragePower(fighters, items) < venue.Gate(floor))
            {
                break;
            }

            var combats = ImmutableList.CreateBuilder<CombatEvent>();
            var floorCleared = true;

            foreach (var hero in fighters) // HeroId order — deterministic
            {
                var outcome = FightMonster(hero, items, venue, floor, hp, packs, rng, combats);
                if (outcome == FightOutcome.HeroDied)
                {
                    dead.Add(hero.Id.Value);
                    floorCleared = false; // a death leaves the hero's monster alive — floor uncleared
                }
                else if (outcome == FightOutcome.MonsterKilled)
                {
                    gold[hero.Id.Value] += venue.GoldPerKill(floor);
                }
                else
                {
                    floorCleared = false; // a flee leaves the floor uncleared
                }
            }

            var anyoneStanding = party.Any(h => !dead.Contains(h.Id.Value));
            floorCleared &= anyoneStanding;

            // The post-floor "too hurt to continue" check, evaluated BEFORE the floor
            // outcome is sealed so a quaff (P2) can be recorded into this floor's
            // combat events. A too-hurt hero drinks by the same rule as in-fight
            // (first Heal item in pack order), THEN the check is evaluated. Draws no
            // RNG, so evaluating here instead of after the loot rolls leaves the
            // stream untouched (the loot is granted either way on a cleared floor).
            var tooHurtToContinue = false;
            if (floorCleared)
            {
                foreach (var hero in party.Where(h => !dead.Contains(h.Id.Value)))
                {
                    if (CombatMath.ShouldFlee(hp[hero.Id.Value], hero.MaxHp))
                    {
                        QuaffAfterFight(hero, items, hp, packs, combats);
                    }

                    tooHurtToContinue |= CombatMath.ShouldFlee(hp[hero.Id.Value], hero.MaxHp);
                }
            }

            floors.Add(new FloorOutcome(floor, floorCleared, combats.ToImmutable()));

            if (!floorCleared)
            {
                break;
            }

            deepestCleared = floor;

            // Ore loot for standing survivors (R6): quantity 1-3, rarity by floor.
            foreach (var hero in party.Where(h => !dead.Contains(h.Id.Value)))
            {
                loot.Add(new OreLoot(hero.Id, venue.OreKey(floor), rng.NextInt(1, 4)));
            }

            // Anyone too hurt to continue ends the expedition after banking the clear.
            if (tooHurtToContinue)
            {
                break;
            }
        }

        var survivors = party.Where(h => !dead.Contains(h.Id.Value)).Select(h => h.Id).ToImmutableList();
        var deaths = party.Where(h => dead.Contains(h.Id.Value)).Select(h => h.Id).ToImmutableList();
        var allFloors = floors.ToImmutable();

        // Attribution runs AFTER resolution, over recorded rolls only (KTD6). It is handed the
        // SAME venue the forward pass used, so the counterfactual recompute reads identical floor
        // data — a divergence here would corrupt attribution (KTD6).
        var beats = AttributionEngine.ComputeBeats(allFloors, party, items, venue);

        return new ExpeditionResult(
            party.Select(h => h.Id).ToImmutableList(),
            targetFloor,
            deepestCleared,
            allFloors,
            survivors,
            deaths,
            beats,
            loot.ToImmutable(),
            gold.ToImmutableSortedDictionary(),
            venue.Id);
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
        VenueDefinition venue,
        int floor,
        Dictionary<int, int> hp,
        Dictionary<int, List<ItemId>> packs,
        IDeterministicRng rng,
        ImmutableList<CombatEvent>.Builder combats)
    {
        var monsterHp = venue.MonsterHp(floor);
        var heroAttack = CombatMath.HeroAttack(hero, items);
        var heroDefense = CombatMath.HeroDefense(hero, items);
        var round = 0;

        while (true)
        {
            round++;

            // Top of the round (P2): a hero who would flee quaffs the first Heal item
            // in pack order instead and fights on; with an empty pack the hero flees
            // exactly as before. The quaff itself draws NO RNG — draw counts change
            // only because the fight continues, a deterministic function of state.
            // At most one quaff per round; round 1 never triggers (heroes enter a
            // floor above the flee threshold — the post-floor check guarantees it).
            var uses = ImmutableList<ConsumableUse>.Empty;
            if (CombatMath.ShouldFlee(hp[hero.Id.Value], hero.MaxHp))
            {
                if (TryQuaff(hero, items, hp, packs, round) is { } use)
                {
                    uses = uses.Add(use);
                }
                else
                {
                    return FightOutcome.HeroFled;
                }
            }

            var rolls = ImmutableList.CreateBuilder<int>();

            var heroRoll = rng.NextInt(0, CombatMath.RollSides);
            rolls.Add(heroRoll);
            var dealt = CombatMath.HeroDamage(heroAttack, heroRoll, venue.MonsterDefense(floor));
            monsterHp -= dealt;
            var monsterKilled = monsterHp <= 0;

            var taken = 0;
            if (!monsterKilled)
            {
                var monsterRoll = rng.NextInt(0, CombatMath.RollSides);
                rolls.Add(monsterRoll);
                taken = CombatMath.MonsterDamage(venue.MonsterAttack(floor), monsterRoll, heroDefense);
                hp[hero.Id.Value] -= taken;
            }

            combats.Add(new CombatEvent(
                floor,
                hero.Id,
                venue.MonsterKind(floor),
                rolls.ToImmutable(),
                dealt,
                taken,
                monsterKilled,
                monsterKilled ? hero.Gear.Weapon : null)
            {
                Uses = uses,
            });

            if (monsterKilled)
            {
                return FightOutcome.MonsterKilled;
            }

            if (hp[hero.Id.Value] <= 0)
            {
                return FightOutcome.HeroDied;
            }

            // The flee decision now happens at the top of the next round, where a
            // quaff can override it — identical outcomes and draws when packs are empty.
        }
    }

    /// <summary>
    /// Consume the FIRST item with a Heal effect in the hero's pack order (P2), capping
    /// at MaxHp. Keyed off <see cref="ConsumableEffect"/> DATA, never recipe ids, so
    /// add-on consumables heal through this identical path. Returns null when the pack
    /// holds no Heal item. Draws no RNG.
    /// </summary>
    private static ConsumableUse? TryQuaff(
        Hero hero,
        ImmutableSortedDictionary<int, Item> items,
        Dictionary<int, int> hp,
        Dictionary<int, List<ItemId>> packs,
        int round)
    {
        var pack = packs[hero.Id.Value];
        for (var i = 0; i < pack.Count; i++)
        {
            if (!items.TryGetValue(pack[i].Value, out var item)
                || item.Effect is not { Kind: ConsumableKind.Heal } effect)
            {
                continue;
            }

            var before = hp[hero.Id.Value];
            var after = Math.Min(before + effect.Magnitude, hero.MaxHp);
            hp[hero.Id.Value] = after;
            pack.RemoveAt(i);
            return new ConsumableUse(item.Id, round, before, after);
        }

        return null;
    }

    /// <summary>
    /// The post-floor quaff (P2): same rule as in-fight, recorded onto the hero's LAST
    /// combat event of this floor with Round = rounds fought + 1, marking a heal that
    /// landed after the fight's damage (attribution reads Round to order heals against
    /// recorded damage). No-op if the hero fought no rounds this floor (defensive).
    /// </summary>
    private static void QuaffAfterFight(
        Hero hero,
        ImmutableSortedDictionary<int, Item> items,
        Dictionary<int, int> hp,
        Dictionary<int, List<ItemId>> packs,
        ImmutableList<CombatEvent>.Builder combats)
    {
        var lastIndex = -1;
        var roundsFought = 0;
        for (var i = 0; i < combats.Count; i++)
        {
            if (combats[i].Hero == hero.Id)
            {
                lastIndex = i;
                roundsFought++;
            }
        }

        if (lastIndex < 0)
        {
            return;
        }

        if (TryQuaff(hero, items, hp, packs, roundsFought + 1) is { } use)
        {
            combats[lastIndex] = combats[lastIndex] with { Uses = combats[lastIndex].Uses.Add(use) };
        }
    }
}
