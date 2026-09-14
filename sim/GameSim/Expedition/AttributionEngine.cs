using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Expedition;

/// <summary>
/// Proves "your item mattered" as a computed fact (R11, KTD6). Recomputes combat math
/// over the RECORDED rolls with a player item's stats removed — never draws RNG, never
/// estimates. Beats are only emitted for player-crafted items (maker's mark present).
/// Multi-item overlap rule (v1): each defensive item is evaluated independently; two
/// independently-decisive items each earn a beat for the same survival.
/// The <c>venue</c> supplies the SAME floor numbers (monster attack, structural gate) the
/// forward resolver used — the counterfactual pass MUST read identical venue data or attribution
/// diverges (KTD6/P4).
/// </summary>
public static class AttributionEngine
{
    public static ImmutableList<AttributionBeat> ComputeBeats(
        ImmutableList<FloorOutcome> floors,
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue)
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

                // AE1 — killing blow by a player-crafted weapon. This beat is a RECORDED FACT, not
                // a threshold (it fires on every player-crafted kill, decisive or not) — but the
                // claim still gets its own arithmetic (P2-PROOF-14): the SAME recorded hero roll,
                // replayed once with the weapon's Attack stat removed. No further rounds are ever
                // rolled — the beat never claims what the fight would have become, only what this
                // one recorded swing would have dealt (matches TellingQuery's KillingBlowPayload
                // exactly, so the ledger and the "Ask how it happened" modal can never disagree).
                if (combat.MonsterKilled
                    && combat.KillingItem is { } killer
                    && IsPlayerCrafted(killer, items))
                {
                    var heroRoll = combat.RecordedRolls[0];
                    var attackWithoutItem = CombatMath.HeroAttack(hero, items.Remove(killer.Value));
                    var dealtWithoutItem = CombatMath.HeroDamage(
                        attackWithoutItem, heroRoll, venue.MonsterDefense(combat.Floor));

                    beats.Add(new AttributionBeat(
                        BeatType.KillingBlow, killer, hero.Id, combat.Floor,
                        $"{items[killer.Value].Name} landed the killing blow on {MonsterName.Definite(combat.MonsterKind)} " +
                        $"-- the blow read {heroRoll}. Without it, the swing deals {dealtWithoutItem}, not {combat.DamageDealt}."));
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
                            venue.MonsterAttack(combat.Floor), monsterRoll, defWithout);

                        if (actualAfter > 0 && hpBefore - takenWithout <= 0)
                        {
                            // The flagship counterfactual (P2-PROOF-14): the SAME recorded monster
                            // roll, replayed with the item's Defense stat removed. rawBlow is what
                            // the roll itself demanded before either version of the fight absorbed
                            // it; the beat gives the raw number, what the item drank, and where the
                            // hero actually stood -- never a share or a rating of the save.
                            var rawBlow = venue.MonsterAttack(combat.Floor) + monsterRoll;

                            beats.Add(new AttributionBeat(
                                BeatType.LethalSave, defId, hero.Id, combat.Floor,
                                $"{items[defId.Value].Name} turned a lethal {MonsterName.AttributiveBlow(combat.MonsterKind)} " +
                                $"-- the blow read {rawBlow}. {items[defId.Value].Name} drank {items[defId.Value].Stats.Defense} of it. " +
                                $"{hero.Name} stood at {actualAfter}. Without it, {hero.Name} falls."));
                        }
                    }
                }

                hp[hero.Id.Value] -= combat.DamageTaken;

                // Phase C U-C1: replay the craft-modifier HP change (e.g. a Leech heal on a kill)
                // exactly as the forward pass applied it — end of the exchange, after damage. 0 when
                // the bearer carries no firing modifier, so pre-U-C1 traces are unaffected (KTD6).
                hp[hero.Id.Value] += combat.ModifierHpDelta;

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
                var gate = venue.Gate(floor.Floor);
                var avg = CombatMath.PartyAveragePower(floorStartFighters, items);

                foreach (var hero in floorStartFighters)
                {
                    // P2-HONEST-11 (owner ruling 2026-09-03, P2-OQ7 resolved honesty over teeth):
                    // Trinket is deliberately ABSENT from this array. #667 (see git history) added
                    // it here to close the T10 U48 worn-check-completeness gap, but that made this
                    // loop false coverage rather than real coverage: CombatMath.EffectivePower
                    // (:60-61 of CombatMath.cs) sums HeroAttack (Weapon only) + HeroDefense
                    // (Shield+Armor only) and never reads Gear.Trinket, so removing a player-crafted
                    // trinket from `items` below cannot move PartyAveragePower by even one point —
                    // the arm could never fire, for any trinket, ever. The owner ruled honesty over
                    // giving the trinket slot real combat stats (the other arm, P2-HONEST-11's
                    // "teeth"): the trinket stays the modifier-only slot, so this loop stays
                    // Weapon/Shield/Armor — the exact slots the formula above actually reads.
                    // GearWornCheckCensusTests pins BOTH directions: a dedicated fact proves this
                    // array can never silently regain Trinket without CombatMath also reading it,
                    // and a cited exception lets this be the one worn-gear-group array that is
                    // allowed to omit a slot. Do not "fix" this back without giving CombatMath a
                    // real trinket-stat reader first.
                    foreach (var itemId in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor })
                    {
                        if (itemId is not { } id || !IsPlayerCrafted(id, items))
                        {
                            continue;
                        }

                        var without = items.Remove(id.Value);
                        var avgWithoutItem = CombatMath.PartyAveragePower(floorStartFighters, without);
                        if (avg >= gate && avgWithoutItem < gate)
                        {
                            // No round to replay here -- the counterfactual is the same
                            // PartyAveragePower recomputation the condition above just used, said out
                            // loud (P2-PROOF-14): the party's power WITH this item against the gate,
                            // and what it reads with the item's stats pulled back out.
                            beats.Add(new AttributionBeat(
                                BeatType.BreakpointClear, id, hero.Id, floor.Floor,
                                $"{items[id.Value].Name} carried the party past the floor {floor.Floor} gate " +
                                $"-- the party's power read {avg} against the gate at {gate}. Without it, {avgWithoutItem} -- under the gate."));
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

                    // naiveHpWithoutHeal / survivedHp are the same two numbers the condition below
                    // reads (P2-PROOF-14) -- named so the Detail below states the arithmetic that
                    // actually decided the beat, never a re-derived or estimated one.
                    var naiveHpWithoutHeal = use.HpBefore - damageFromRound;
                    var survivedHp = use.HpAfter - damageFromRound + laterHeals;
                    var wouldHaveDied = naiveHpWithoutHeal <= 0;
                    var survivedFight = survivedHp > 0;

                    beats.Add(wouldHaveDied && survivedFight
                        ? new AttributionBeat(
                            BeatType.PotionLifesave, use.Item, hero.Id, combat.Floor,
                            $"{items[use.Item.Value].Name} saved {hero.Name}'s life -- the recorded damage from round " +
                            $"{use.Round} on alone reads {naiveHpWithoutHeal} without it. {hero.Name} drank it at " +
                            $"{use.HpBefore} to {use.HpAfter}, and closed the fight at {survivedHp}.")
                        : new AttributionBeat(
                            BeatType.Provisioned, use.Item, hero.Id, combat.Floor,
                            // Deliberately no "still standing" claim here (no participation credit,
                            // stated honestly either way): this branch also covers the hero dying in
                            // the fight regardless of the quaff, where survivedHp is <= 0 too -- the
                            // number is reported either way, never spun into a verdict the beat did
                            // not earn.
                            $"{items[use.Item.Value].Name} kept {hero.Name} fighting on floor {combat.Floor} -- " +
                            $"{hero.Name} drank it at round {use.Round}, {use.HpBefore} to {use.HpAfter}. Without it, " +
                            $"the fight's own recorded numbers read {naiveHpWithoutHeal} from there. No credit taken."));
                }
            }
        }
    }

    private static bool IsPlayerCrafted(ItemId id, ImmutableSortedDictionary<int, Item> items) =>
        items.TryGetValue(id.Value, out var item) && item.PlayerCrafted;
}
