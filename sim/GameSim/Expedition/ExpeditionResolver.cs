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
        IDeterministicRng rng,
        ImmutableHashSet<int>? retreatExemptHeroes = null,
        int retreatExemptThroughFloor = 0)
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
        var retreated = new HashSet<int>(); // TUNING-C: heroes who peeled off at their competence ceiling
        var floors = ImmutableList.CreateBuilder<FloorOutcome>();
        var loot = ImmutableList.CreateBuilder<OreLoot>();

        var (deepestCleared, rawHalt) = ResolveFloors(
            party, items, venue, 1, targetFloor, hp, packs, gold, dead, retreated, floors, loot,
            retreatExemptHeroes ?? ImmutableHashSet<int>.Empty, retreatExemptThroughFloor, rng);

        // D4 precedence: clearing the target floor is a full success whatever exit path ended
        // the loop (a too-hurt break AFTER the target is cleared is success, not a limp home).
        var halt = ClassifyHalt(deepestCleared, targetFloor, rawHalt);

        return BuildResult(
            party, items, venue, targetFloor, deepestCleared, floors.ToImmutable(), loot.ToImmutable(), gold, dead, halt);
    }

    /// <summary>
    /// Stage 1 of a staged resolution (expedition-tension verdict §5 step 4): resolve floors
    /// [1..<paramref name="checkpointFloor"/>] at the Expedition tick. Initialises the working
    /// locals exactly as <see cref="Resolve"/> does, then runs the shared floor loop over the
    /// stage-1 range. If the party cleared every stage-1 floor with nobody dead and nobody too
    /// hurt (raw halt == TargetReached), it PARKS as an <see cref="InFlightExpedition"/> for the
    /// Deep tick; ANY other stage-1 ending (wipe / gate / floor-lost / too-hurt-at-checkpoint)
    /// finalises IMMEDIATELY as an <see cref="ExpeditionResult"/> carrying that raw halt — the D4
    /// precedence does NOT apply here (clearing the checkpoint is not clearing the target, and the
    /// checkpoint is always shallower than the target so deepest &lt; target on every finalise).
    /// The RNG draw order is byte-identical to the equivalent prefix of an unstaged
    /// <see cref="Resolve"/> — the loop body and draws are shared verbatim.
    /// </summary>
    public static (ExpeditionResult? Completed, InFlightExpedition? InFlight) ResolveStage1(
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        int targetFloor,
        int checkpointFloor,
        IDeterministicRng rng,
        ImmutableHashSet<int>? retreatExemptHeroes = null,
        int retreatExemptThroughFloor = 0)
    {
        if (party.IsEmpty)
        {
            throw new ArgumentException("Expedition party cannot be empty.", nameof(party));
        }

        targetFloor = Math.Clamp(targetFloor, 1, venue.FloorCount);

        var hp = party.ToDictionary(h => h.Id.Value, h => h.MaxHp);
        var packs = party.ToDictionary(h => h.Id.Value, h => h.Pack.ToList());
        var gold = party.ToDictionary(h => h.Id.Value, _ => 0);
        var dead = new HashSet<int>();
        // TUNING-C: stage-1 retreats are NOT parked on InFlightExpedition (a Contract, frozen). They
        // are a pure function of Hero records + cleared depth, so ResolveStage2 reconstructs the set
        // at the Deep tick (SeedRetreatedThrough) — byte-identical to carrying it. The set here only
        // affects draws for checkpoints deeper than 1 (none live today); at checkpoint 1 a floor-1
        // retreat is the range's last floor, so it changes nothing within stage 1.
        var retreated = new HashSet<int>();
        var floors = ImmutableList.CreateBuilder<FloorOutcome>();
        var loot = ImmutableList.CreateBuilder<OreLoot>();

        var (deepestCleared, rawHalt) = ResolveFloors(
            party, items, venue, 1, checkpointFloor, hp, packs, gold, dead, retreated, floors, loot,
            retreatExemptHeroes ?? ImmutableHashSet<int>.Empty, retreatExemptThroughFloor, rng);

        // Any non-clean stage-1 ending finalises now (no camp window, no report — deaths reveal
        // only at Evening, KTD5). rawHalt is guaranteed != TargetReached here: a fully cleared
        // stage-1 range with nobody too hurt is exactly the parking condition below.
        if (rawHalt != ExpeditionHalt.TargetReached)
        {
            var completed = BuildResult(
                party, items, venue, targetFloor, deepestCleared, floors.ToImmutable(), loot.ToImmutable(), gold, dead, rawHalt);
            return (completed, null);
        }

        // Park: every field is a serializable image of a working local, so stage 2 resumes the
        // loop verbatim on the live kernel stream (no RngState carried — KTD4).
        var inFlight = new InFlightExpedition(
            party.Select(h => h.Id).ToImmutableList(),
            targetFloor,
            checkpointFloor,
            venue.Id,
            hp.ToImmutableSortedDictionary(),
            packs.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value.ToImmutableList()),
            gold.ToImmutableSortedDictionary(),
            dead.ToImmutableSortedSet(),
            floors.ToImmutable(),
            loot.ToImmutable(),
            deepestCleared);

        return (null, inFlight);
    }

    /// <summary>
    /// Stage 2 of a staged resolution: finalise floors [CheckpointFloor+1..TargetFloor] at the
    /// ExpeditionDeep tick. Rehydrates the mutable working state from <paramref name="inFlight"/>
    /// and continues the shared floor loop on the LIVE kernel stream (<paramref name="rng"/>), so
    /// stage-2 rolls were provably undrawn while the party camped. The merged floor/loot lists and
    /// the deepest-cleared value reconstruct exactly what an unstaged <see cref="Resolve"/> would
    /// have produced for the same party/seed; attribution runs once over the MERGED floors (KTD6).
    /// A recalled party (v1 bank-and-surface) short-circuits: no loop, no draws, halt == Recalled.
    /// </summary>
    public static ExpeditionResult ResolveStage2(
        InFlightExpedition inFlight,
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        IDeterministicRng rng,
        ImmutableHashSet<int>? retreatExemptHeroes = null,
        int retreatExemptThroughFloor = 0)
    {
        var exempt = retreatExemptHeroes ?? ImmutableHashSet<int>.Empty;
        var hp = inFlight.Hp.ToDictionary(kv => kv.Key, kv => kv.Value);
        var packs = inFlight.Packs.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var gold = inFlight.Gold.ToDictionary(kv => kv.Key, kv => kv.Value);
        var dead = new HashSet<int>(inFlight.Dead);
        var floors = inFlight.Floors.ToBuilder();
        var loot = inFlight.Loot.ToBuilder();

        int deepestCleared;
        ExpeditionHalt halt;
        if (inFlight.Recalled)
        {
            // The recall bell rang at Camp: bank stage-1 clears/ore and surface without rolling
            // deeper floors. No RNG is drawn — the stream is untouched by a recalled party.
            deepestCleared = inFlight.DeepestFloorCleared;
            halt = ExpeditionHalt.Recalled;
        }
        else
        {
            // TUNING-C: rebuild the retreated set as of the stage-1 clears before resuming. The rule
            // is a pure function of records + cleared depth (SeedRetreatedThrough replays it over
            // floors [1..DeepestFloorCleared]), so this is byte-identical to having carried the set
            // through the camp — InFlightExpedition (a Contract) stays unchanged.
            var retreated = SeedRetreatedThrough(party, inFlight.DeepestFloorCleared, dead, exempt, retreatExemptThroughFloor);

            var (stage2Deepest, rawHalt) = ResolveFloors(
                party, items, venue, inFlight.CheckpointFloor + 1, inFlight.TargetFloor, hp, packs, gold, dead, retreated, floors, loot,
                exempt, retreatExemptThroughFloor, rng);

            // ResolveFloors returns the deepest cleared WITHIN its range (0 if none); fall back to
            // the stage-1 deepest so the merged value equals the unstaged single accumulator.
            deepestCleared = stage2Deepest > 0 ? stage2Deepest : inFlight.DeepestFloorCleared;
            halt = ClassifyHalt(deepestCleared, inFlight.TargetFloor, rawHalt);
        }

        return BuildResult(
            party, items, venue, inFlight.TargetFloor, deepestCleared, floors.ToImmutable(), loot.ToImmutable(), gold, dead, halt);
    }

    /// <summary>
    /// D4 precedence rule: <c>DeepestCleared == TargetFloor</c> is ALWAYS a
    /// <see cref="ExpeditionHalt.TargetReached"/>, whatever exit path ended the loop — a too-hurt
    /// break that fires only after the target floor is cleared is a success, not a limp home.
    /// Only a run that fell short of the target keeps its exit-path classification.
    /// </summary>
    private static ExpeditionHalt ClassifyHalt(int deepestCleared, int targetFloor, ExpeditionHalt rawHalt) =>
        deepestCleared == targetFloor ? ExpeditionHalt.TargetReached : rawHalt;

    /// <summary>
    /// Assemble the <see cref="ExpeditionResult"/> from the resolved working state. Shared by
    /// <see cref="Resolve"/>, <see cref="ResolveStage1"/> (immediate finalise), and
    /// <see cref="ResolveStage2"/> so the three paths build byte-identical results from identical
    /// state. Attribution runs AFTER resolution, over recorded rolls only (KTD6), handed the SAME
    /// venue the forward pass used — a divergence here would corrupt attribution.
    /// </summary>
    private static ExpeditionResult BuildResult(
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        int targetFloor,
        int deepestCleared,
        ImmutableList<FloorOutcome> floors,
        ImmutableList<OreLoot> loot,
        Dictionary<int, int> gold,
        HashSet<int> dead,
        ExpeditionHalt halt)
    {
        var survivors = party.Where(h => !dead.Contains(h.Id.Value)).Select(h => h.Id).ToImmutableList();
        var deaths = party.Where(h => dead.Contains(h.Id.Value)).Select(h => h.Id).ToImmutableList();
        var beats = AttributionEngine.ComputeBeats(floors, party, items, venue);

        return new ExpeditionResult(
            party.Select(h => h.Id).ToImmutableList(),
            targetFloor,
            deepestCleared,
            floors,
            survivors,
            deaths,
            beats,
            loot,
            gold.ToImmutableSortedDictionary(),
            venue.Id,
            halt);
    }

    /// <summary>
    /// Runs the per-floor combat loop over the INCLUSIVE range [fromFloor, toFloor], mutating
    /// the caller's working state (hp/packs/gold/dead) and appending to its floor/loot builders.
    /// Returns the deepest floor cleared WITHIN the range (0 if none) plus the RAW exit-path
    /// <see cref="ExpeditionHalt"/> classification (before the D4 <see cref="ClassifyHalt"/>
    /// precedence check the callers apply). This method is the staged-resolution seam
    /// (expedition-tension verdict §5): <see cref="ResolveStage1"/> runs [1..checkpoint] at the
    /// Expedition tick and <see cref="ResolveStage2"/> runs [checkpoint+1..target] at the Deep
    /// tick, with the parameters here persisting between stages as <c>InFlightExpedition</c> —
    /// every parameter maps 1:1 onto a former <see cref="Resolve"/> method-local, and the body is
    /// a verbatim move of the original loop so the RNG draw order is byte-identical. The halt is a
    /// pure classification of the exit taken; it draws no RNG and changes no forward state.
    /// </summary>
    private static (int DeepestCleared, ExpeditionHalt Halt) ResolveFloors(
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        int fromFloor,
        int toFloor,
        Dictionary<int, int> hp,
        Dictionary<int, List<ItemId>> packs,
        Dictionary<int, int> gold,
        HashSet<int> dead,
        HashSet<int> retreated,
        ImmutableList<FloorOutcome>.Builder floors,
        ImmutableList<OreLoot>.Builder loot,
        ImmutableHashSet<int> retreatExemptHeroes,
        int retreatExemptThroughFloor,
        IDeterministicRng rng)
    {
        var deepestCleared = 0;
        // Default: the loop runs the whole range to completion (range fully cleared, nobody too
        // hurt). Every early exit below overwrites this with its cause.
        var halt = ExpeditionHalt.TargetReached;

        for (var floor = fromFloor; floor <= toFloor; floor++)
        {
            // TUNING-C: a retreated hero has left the delve (banked, still a Survivor) — she is
            // parallel to dead for the fighters filter, the too-hurt sweep, and the ore sweep.
            var fighters = party.Where(h => !dead.Contains(h.Id.Value) && !retreated.Contains(h.Id.Value)).ToList();
            if (fighters.Count == 0)
            {
                halt = ExpeditionHalt.PartyWiped;
                break;
            }

            // STRUCTURAL gate (AE3): under-geared parties retreat at the gate — no roll involved.
            if (CombatMath.PartyAveragePower(fighters, items) < venue.Gate(floor))
            {
                halt = ExpeditionHalt.GateHeld;
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
                foreach (var hero in party.Where(h => !dead.Contains(h.Id.Value) && !retreated.Contains(h.Id.Value)))
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
                // A flee or death left the floor uncleared: FloorLost if anyone still stands,
                // PartyWiped if the whole party fell this floor.
                halt = anyoneStanding ? ExpeditionHalt.FloorLost : ExpeditionHalt.PartyWiped;
                break;
            }

            deepestCleared = floor;

            // Ore loot for standing survivors (R6): quantity 1-3, rarity by floor. A hero who
            // retreated on a SHALLOWER floor already banked her ore there and is gone; a hero
            // retreating on THIS floor still collects it (the retreat check runs below, after this).
            foreach (var hero in party.Where(h => !dead.Contains(h.Id.Value) && !retreated.Contains(h.Id.Value)))
            {
                loot.Add(new OreLoot(hero.Id, venue.OreKey(floor), rng.NextInt(1, 4)));
            }

            // Anyone too hurt to continue ends the expedition after banking the clear.
            if (tooHurtToContinue)
            {
                halt = ExpeditionHalt.TooHurt;
                break;
            }

            // TUNING-C competence retreat (verdict C): each still-standing hero peels off once the
            // NEXT floor would exceed her personal depth record + 1 — she pushes exactly one floor
            // past her best and no further. The just-cleared floor's clear, gold, and ore are already
            // banked (above); she stays a Survivor and fights no deeper floor. Draws no RNG (fewer
            // fighters later = fewer draws, so mixed-record parties shift draw counts — golden
            // re-baseline expected). A bounty acceptor is EXEMPT through the bounty's TargetFloor
            // (accepting the bounty IS the commitment, R18); non-acceptor partymates still retreat.
            ApplyCompetenceRetreat(party, floor + 1, dead, retreated, retreatExemptHeroes, retreatExemptThroughFloor);
        }

        return (deepestCleared, halt);
    }

    /// <summary>
    /// The TUNING-C competence-retreat predicate applied to every still-standing hero after a floor
    /// seals cleared: a hero RETREATS iff <paramref name="nextFloor"/> &gt; her <c>DeepestFloorReached</c>
    /// + 1 AND she is not a retreat-exempt hero within the exempt floor band. Mutates
    /// <paramref name="retreated"/> in place. Pure — reads only Hero records; draws no RNG.
    /// </summary>
    private static void ApplyCompetenceRetreat(
        ImmutableList<Hero> party,
        int nextFloor,
        HashSet<int> dead,
        HashSet<int> retreated,
        ImmutableHashSet<int> retreatExemptHeroes,
        int retreatExemptThroughFloor)
    {
        foreach (var hero in party)
        {
            if (dead.Contains(hero.Id.Value) || retreated.Contains(hero.Id.Value))
            {
                continue;
            }

            var exempt = retreatExemptHeroes.Contains(hero.Id.Value) && nextFloor <= retreatExemptThroughFloor;
            if (!exempt && nextFloor > hero.DeepestFloorReached + 1)
            {
                retreated.Add(hero.Id.Value);
            }
        }
    }

    /// <summary>
    /// Reconstruct the set of heroes who would have already retreated by the time the party cleared
    /// through <paramref name="deepestCleared"/> — used by <see cref="ResolveStage2"/> to rehydrate
    /// the retreated set that stage 1 produced but did not park (InFlightExpedition is a frozen
    /// Contract). Replays <see cref="ApplyCompetenceRetreat"/> over floors [1..deepestCleared], so the
    /// resumed stage-2 loop excludes exactly the heroes an unstaged <see cref="Resolve"/> would have —
    /// keeping the staged/unstaged draw order byte-identical across the camp seam.
    /// </summary>
    private static HashSet<int> SeedRetreatedThrough(
        ImmutableList<Hero> party,
        int deepestCleared,
        HashSet<int> dead,
        ImmutableHashSet<int> retreatExemptHeroes,
        int retreatExemptThroughFloor)
    {
        var retreated = new HashSet<int>();
        for (var floor = 1; floor <= deepestCleared; floor++)
        {
            ApplyCompetenceRetreat(party, floor + 1, dead, retreated, retreatExemptHeroes, retreatExemptThroughFloor);
        }

        return retreated;
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
