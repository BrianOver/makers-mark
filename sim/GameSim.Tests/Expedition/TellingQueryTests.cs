using System.Collections.Immutable;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// P2-PROOF-02: the Telling's pure read model. Every scenario here is a HAND-BUILT recorded
/// fight (no RNG, no resolver) — <see cref="TellingQuery.Build"/> must reconstruct the exact
/// same staging AttributionEngine's own formulas would produce, never a parallel guess.
/// </summary>
public class TellingQueryTests
{
    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "shortsword", "Fine Shortsword", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item PlayerArmor(int id, int defense) => new(
        new ItemId(id), "chain-vest", "Fine Chain Vest", ItemSlot.Armor, QualityGrade.Fine,
        new ItemStats(0, defense, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Salve(int id, int magnitude = 6, bool marked = true) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), marked ? new MakersMark("You", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, magnitude));

    private static HeroAtDeparture Departure(
        int id, int level, int maxHp, ItemId? weapon = null, ItemId? shield = null, ItemId? armor = null,
        string name = "Torvald") =>
        new(new HeroId(id), name, "vanguard", level, maxHp, weapon, shield, armor);

    /// <summary>Re-hydrates a departure snapshot into the shape <c>CombatMath</c>/<c>AttributionEngine</c>
    /// expect — used ONLY to build the ground-truth expectation these tests pin against, never fed
    /// to <see cref="TellingQuery.Build"/> itself (which never sees a live <see cref="Hero"/>).</summary>
    private static Hero AsHero(HeroAtDeparture d) => new(
        d.Id, d.Name, d.ClassId, d.Level, d.MaxHp, Gold: 0,
        new GearSet(d.Weapon, d.Shield, d.Armor), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>A LIVE hero fixture (for driving the real <see cref="ExpeditionResolver"/> in the
    /// consistency-pin sweep below — <see cref="TellingQuery.Build"/> never sees this directly,
    /// only the <see cref="HeroAtDeparture"/> snapshot <see cref="ExpeditionResolver.BuildResult"/>
    /// takes from it).</summary>
    private static Hero LiveHeroWith(int id, GearSet gear, int hp = 30, int level = 3, string name = "Torvald") => new(
        new HeroId(id), name, "vanguard", level, hp, Gold: 50, gear, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 4, DiedOnDay: null);

    private static VenueDefinition SingleFloorVenue(
        int gate = 0, int monsterHp = 100, int monsterAttack = 10, int monsterDefense = 0,
        string monsterKind = "Cave Rat") =>
        new("test-venue", "Test Venue", ImmutableArray.Create(
            new VenueFloor(1, gate, monsterKind, monsterHp, monsterAttack, monsterDefense, GoldPerKill: 5, OreKey: "iron")));

    private static ExpeditionResult MakeResult(HeroAtDeparture hero, FloorOutcome floor, params AttributionBeat[] beats) =>
        MakeResult(ImmutableList.Create(hero), ImmutableList.Create(floor), beats);

    private static ExpeditionResult MakeResult(
        ImmutableList<HeroAtDeparture> party, ImmutableList<FloorOutcome> floors, params AttributionBeat[] beats)
    {
        var ids = party.Select(h => h.Id).ToImmutableList();
        return new ExpeditionResult(
            ids, TargetFloor: floors.Count, DeepestFloorCleared: floors.Count, floors,
            Survivors: ids, Deaths: ImmutableList<HeroId>.Empty, Beats: beats.ToImmutableList(),
            Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = party,
        };
    }

    // ---- Shape: KillingBlow (recorded fact, no counterfactual replay) --------------------------

    [Fact]
    public void KillingBlow_StagesRecordedFact_WithHonestEpilogueNumber_NeverAReplay()
    {
        var weapon = PlayerWeapon(1, attack: 40);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon);
        var hero = Departure(1, level: 3, maxHp: 30, weapon: weapon.Id);
        var venue = SingleFloorVenue(monsterHp: 50, monsterAttack: 10, monsterDefense: 5);

        var killRound = new CombatEvent(
            1, hero.Id, "Cave Rat", ImmutableList.Create(4), DamageDealt: 50, DamageTaken: 0,
            MonsterKilled: true, KillingItem: weapon.Id);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(killRound));
        var beat = new AttributionBeat(BeatType.KillingBlow, weapon.Id, hero.Id, 1, "detail");
        var result = MakeResult(hero, floor, beat);

        var script = TellingQuery.Build(result, beat, items, venue);

        Assert.Equal(TellingShape.KillingBlowShape, script.Shape);
        Assert.Null(script.DivergenceRound);
        Assert.Empty(script.CounterfactualTail);
        Assert.Equal("Cave Rat", script.MonsterKind);

        var payload = Assert.IsType<KillingBlowPayload>(script.Payload);
        Assert.Equal(1, payload.KillRound);
        Assert.Equal(4, payload.HeroRoll);
        Assert.Equal(50, payload.DamageDealtWithItem);
        Assert.Equal(50, payload.MonsterHpBeforeKillRound);

        // Ground truth via the SAME CombatMath functions the engine itself uses — never a
        // hand-typed constant that could silently drift from a class-balance change.
        var live = AsHero(hero);
        var attackWithout = CombatMath.HeroAttack(live, items.Remove(weapon.Id.Value));
        var expectedDealtWithout = CombatMath.HeroDamage(attackWithout, roll: 4, monsterDefense: 5);
        Assert.Equal(expectedDealtWithout, payload.DamageDealtWithoutItem);
        Assert.Equal(50 - expectedDealtWithout, payload.MonsterHpWithoutItem);
        Assert.True(payload.DamageDealtWithoutItem < payload.DamageDealtWithItem);
    }

    // ---- Shape: LethalSave (the flagship counterfactual) --------------------------------------

    [Fact]
    public void LethalSave_StagesTrueCounterfactual_HeroFallsWithoutItem_StandsWithIt()
    {
        var armor = PlayerArmor(1, defense: 12);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, armor);
        var hero = Departure(1, level: 1, maxHp: 14, armor: armor.Id);
        var venue = SingleFloorVenue(monsterHp: 999, monsterAttack: 20, monsterDefense: 0);

        // Round 1: hero rolls 3 (deals 3, irrelevant here), monster rolls 5. With the armor's 12
        // defense the hit reads 12 and the hero stands at 2; without it the SAME roll reads 24 and
        // the hero falls.
        var round = new CombatEvent(
            1, hero.Id, "Cave Rat", ImmutableList.Create(3, 5), DamageDealt: 3, DamageTaken: 12,
            MonsterKilled: false, KillingItem: null);
        var floor = new FloorOutcome(1, Cleared: false, ImmutableList.Create(round));
        var beat = new AttributionBeat(BeatType.LethalSave, armor.Id, hero.Id, 1, "detail");
        var result = MakeResult(hero, floor, beat);

        var script = TellingQuery.Build(result, beat, items, venue);

        Assert.Equal(TellingShape.LethalSaveShape, script.Shape);
        Assert.Equal(1, script.DivergenceRound);

        var payload = Assert.IsType<LethalSavePayload>(script.Payload);
        Assert.Equal(ItemSlot.Armor, payload.Slot);
        Assert.Equal(5, payload.MonsterRoll);
        Assert.Equal(25, payload.RawBlow); // monsterAttack(20) + roll(5)
        Assert.Equal(12, payload.ItemDefenseStat);
        Assert.Equal(12, payload.DamageTakenWithItem);
        Assert.Equal(24, payload.DamageTakenWithoutItem);
        Assert.Equal(14, payload.HeroHpBeforeRound);
        Assert.Equal(2, payload.HeroHpAfterWithItem);
        Assert.Equal(-10, payload.HeroHpAfterWithoutItem);

        var tail = Assert.Single(script.CounterfactualTail);
        Assert.Equal(1, tail.Round);
        Assert.False(tail.MonsterKilled);
        Assert.Equal(3, tail.DamageDealt); // the hero's own damage is unaffected by removing armor
        Assert.Equal(24, tail.DamageTaken);
        Assert.Equal(14, tail.HeroHpBefore);
        Assert.Equal(-10, tail.HeroHpAfter);
        Assert.Equal(venue.MonsterHp(1) - 3, tail.MonsterHpAfter);
    }

    // ---- Shape: BreakpointClear (structural, no round to replay) ------------------------------

    [Fact]
    public void BreakpointClear_StagesGateMath_NoRoundReplay()
    {
        var weapon = PlayerWeapon(1, attack: 50);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon);
        var hero1 = Departure(1, level: 1, maxHp: 30, weapon: weapon.Id);
        var hero2 = Departure(2, level: 1, maxHp: 30, name: "Halvar");

        var live1 = AsHero(hero1);
        var live2 = AsHero(hero2);
        var avgWithExpected = (CombatMath.EffectivePower(live1, items) + CombatMath.EffectivePower(live2, items)) / 2;
        var withoutItem = items.Remove(weapon.Id.Value);
        var avgWithoutExpected = (CombatMath.EffectivePower(live1, withoutItem) + CombatMath.EffectivePower(live2, withoutItem)) / 2;
        var gate = avgWithoutExpected + (avgWithExpected - avgWithoutExpected) / 2;
        Assert.True(gate > avgWithoutExpected && gate <= avgWithExpected); // fixture sanity

        var floor = new FloorOutcome(1, Cleared: true, ImmutableList<CombatEvent>.Empty);
        var venue = SingleFloorVenue(gate: gate);
        var beat = new AttributionBeat(BeatType.BreakpointClear, weapon.Id, hero1.Id, 1, "detail");
        var result = MakeResult(ImmutableList.Create(hero1, hero2), ImmutableList.Create(floor), beat);

        var script = TellingQuery.Build(result, beat, items, venue);

        Assert.Equal(TellingShape.BreakpointClearShape, script.Shape);
        Assert.Null(script.DivergenceRound);
        Assert.Empty(script.CounterfactualTail);

        var payload = Assert.IsType<BreakpointClearPayload>(script.Payload);
        Assert.Equal(gate, payload.Gate);
        Assert.Equal(avgWithExpected, payload.PartyAveragePowerWithItem);
        Assert.Equal(avgWithoutExpected, payload.PartyAveragePowerWithoutItem);
        Assert.True(payload.PartyAveragePowerWithItem >= payload.Gate);
        Assert.True(payload.PartyAveragePowerWithoutItem < payload.Gate);
    }

    // ---- Shape: Provisioned ("did not matter", said out loud) ---------------------------------

    [Fact]
    public void Provisioned_StagesDidNotMatter_NoDivergence()
    {
        var salve = Salve(10);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve);
        var hero = Departure(1, level: 1, maxHp: 30);
        var venue = SingleFloorVenue();

        var round1 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(2, 4), DamageDealt: 5, DamageTaken: 8, MonsterKilled: false, KillingItem: null);
        var round2 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(2, 4), DamageDealt: 5, DamageTaken: 3, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
        };
        var round3 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(4), DamageDealt: 5, DamageTaken: 0, MonsterKilled: true, KillingItem: null);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(round1, round2, round3));

        var realBeats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(floor), ImmutableList.Create(AsHero(hero)), items, venue);
        var beat = Assert.Single(realBeats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.Provisioned, beat.Beat); // fixture-authenticity check

        var result = MakeResult(hero, floor, beat);
        var script = TellingQuery.Build(result, beat, items, venue);

        Assert.Equal(TellingShape.ProvisionedShape, script.Shape);
        Assert.Null(script.DivergenceRound);
        Assert.Empty(script.CounterfactualTail);

        var payload = Assert.IsType<ProvisionedPayload>(script.Payload);
        Assert.Equal(2, payload.QuaffRound);
        Assert.Equal(5, payload.HpBeforeQuaff);
        Assert.Equal(11, payload.HpAfterQuaff);
        Assert.Equal(2, payload.NaiveHpWithoutHeal); // 5 - (3 + 0), matches AE's own naive sum
        Assert.True(payload.NaiveHpWithoutHeal > 0);
    }

    // ---- Shape: PotionLifesave (true counterfactual life saved) -------------------------------

    [Fact]
    public void PotionLifesave_StagesTrueDivergence_RestOfTheNightNeverHappens()
    {
        var salve = Salve(10);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve);
        var hero = Departure(1, level: 1, maxHp: 13);
        var venue = SingleFloorVenue();

        var round1 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(2, 4), DamageDealt: 5, DamageTaken: 8, MonsterKilled: false, KillingItem: null);
        var round2 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(2, 4), DamageDealt: 5, DamageTaken: 8, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
        };
        var round3 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(4), DamageDealt: 5, DamageTaken: 0, MonsterKilled: true, KillingItem: null);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(round1, round2, round3));

        var realBeats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(floor), ImmutableList.Create(AsHero(hero)), items, venue);
        var beat = Assert.Single(realBeats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.PotionLifesave, beat.Beat); // fixture-authenticity check

        var result = MakeResult(hero, floor, beat);
        var script = TellingQuery.Build(result, beat, items, venue);

        Assert.Equal(TellingShape.PotionLifesaveShape, script.Shape);
        Assert.Equal(2, script.DivergenceRound);

        var payload = Assert.IsType<PotionLifesavePayload>(script.Payload);
        Assert.Equal(2, payload.QuaffRound);
        Assert.Equal(5, payload.HpBeforeQuaff);
        Assert.Equal(11, payload.HpAfterQuaff);
        Assert.Equal(2, payload.DivergenceRound);
        Assert.Equal(-3, payload.HpAtDivergence);
        Assert.True(payload.HpAtDivergence <= 0);

        var tail = Assert.Single(script.CounterfactualTail);
        Assert.Equal(2, tail.Round);
        Assert.Equal(8, tail.DamageTaken);   // the actual recorded damage, unchanged
        Assert.Equal(5, tail.DamageDealt);
        Assert.Equal(-3, tail.HeroHpAfter);
        Assert.False(tail.MonsterKilled);
    }

    // ---- The finding-5 trap: MarginOnly downgrade ----------------------------------------------

    [Fact]
    public void PotionLifesave_LaterIndependentQuaffPreventsDeath_DowngradesToMarginOnly()
    {
        // AttributionEngine's OWN naive check (recorded damage from the quaff round onward,
        // against HpBefore, ignoring every OTHER heal) says potionA's removal would have killed
        // the hero. A strict round-by-round replay — which DOES honor the later, independent
        // potionB quaff — never actually crosses zero. Staging a death here would contradict the
        // replay, so the query must downgrade rather than soften or contradict.
        var potionA = Salve(20, magnitude: 8);
        var potionB = Salve(21, magnitude: 15);
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(potionA.Id.Value, potionA)
            .Add(potionB.Id.Value, potionB);
        var hero = Departure(1, level: 1, maxHp: 30);
        var venue = SingleFloorVenue();

        var round1 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(1, 2), DamageDealt: 5, DamageTaken: 5, MonsterKilled: false, KillingItem: null);
        var round2 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(1, 2), DamageDealt: 5, DamageTaken: 10, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(new ConsumableUse(potionA.Id, Round: 2, HpBefore: 25, HpAfter: 33)),
        };
        var round3 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(1, 2), DamageDealt: 5, DamageTaken: 20, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(new ConsumableUse(potionB.Id, Round: 3, HpBefore: 23, HpAfter: 38)),
        };
        var round4 = new CombatEvent(1, hero.Id, "Cave Rat", ImmutableList.Create(3), DamageDealt: 5, DamageTaken: 0, MonsterKilled: true, KillingItem: null);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(round1, round2, round3, round4));

        var realBeats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(floor), ImmutableList.Create(AsHero(hero)), items, venue);
        var beat = Assert.Single(realBeats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.PotionLifesave, beat.Beat); // the engine claims this WOULD have been fatal
        Assert.Equal(potionA.Id, beat.Item); // potionB earns no beat of its own (first-use rule)

        var result = MakeResult(hero, floor, beat);
        var script = TellingQuery.Build(result, beat, items, venue);

        Assert.Equal(TellingShape.MarginOnly, script.Shape);
        Assert.Null(script.DivergenceRound);
        Assert.Empty(script.CounterfactualTail);

        var payload = Assert.IsType<MarginOnlyPayload>(script.Payload);
        Assert.Equal("PotionLifesave", payload.DowngradedFromBeat);
        Assert.Equal(10, payload.MinHpReached);
        Assert.Equal(3, payload.MinHpRound);
        Assert.True(payload.MinHpReached > 0);
        Assert.False(string.IsNullOrWhiteSpace(payload.Reason));
    }

    // ---- Hard requirement: no RNG parameter -----------------------------------------------------

    [Fact]
    public void Build_TakesNoRngParameter_ByReflection()
    {
        var method = typeof(TellingQuery).GetMethod(nameof(TellingQuery.Build), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        foreach (var parameter in method!.GetParameters())
        {
            Assert.False(
                typeof(IDeterministicRng).IsAssignableFrom(parameter.ParameterType),
                $"TellingQuery.Build gained an RNG-typed parameter: {parameter.ParameterType.Name} {parameter.Name}");
        }
    }

    // ---- Hard requirement: raid-time snapshot immunity to a post-reveal level-up ----------------

    [Fact]
    public void RaidTimeSnapshot_FrozenAtDeparture_ImmuneToWhateverHappensToTheLiveHeroLater()
    {
        var weapon = PlayerWeapon(1, attack: 40);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon);
        var venue = SingleFloorVenue(monsterHp: 50, monsterAttack: 10, monsterDefense: 5);
        var killRound = new CombatEvent(
            1, new HeroId(1), "Cave Rat", ImmutableList.Create(4), DamageDealt: 50, DamageTaken: 0,
            MonsterKilled: true, KillingItem: weapon.Id);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(killRound));
        var beat = new AttributionBeat(BeatType.KillingBlow, weapon.Id, new HeroId(1), 1, "detail");

        var departedAtLevel3 = Departure(1, level: 3, maxHp: 30, weapon: weapon.Id);
        var result = MakeResult(departedAtLevel3, floor, beat);
        var scriptFromDeparture = TellingQuery.Build(result, beat, items, venue);

        // Sanity: Level DOES feed the counterfactual math (CombatMath.HeroAttack reads it), so this
        // pin is not vacuous — a result departed at a different level genuinely stages differently.
        var departedAtLevel9 = Departure(1, level: 9, maxHp: 30, weapon: weapon.Id);
        var resultIfDepartedLater = MakeResult(departedAtLevel9, floor, beat);
        var scriptIfDepartedAtLevel9 = TellingQuery.Build(resultIfDepartedLater, beat, items, venue);
        Assert.NotEqual(scriptFromDeparture.Payload, scriptIfDepartedAtLevel9.Payload);

        // The actual pin: the Evening reveal levels the bearer's LIVE Hero record (HeroAtDeparture's
        // own doc comment) — but `result` is frozen at departure and TellingQuery.Build's signature
        // never accepts a live Hero at all, so nothing reachable from `result` can move.
        var scriptAgain = TellingQuery.Build(result, beat, items, venue);
        Assert.Equal(scriptFromDeparture.Shape, scriptAgain.Shape);
        Assert.Equal(scriptFromDeparture.DivergenceRound, scriptAgain.DivergenceRound);
        Assert.Equal(scriptFromDeparture.Payload, scriptAgain.Payload);
        Assert.Equal(3, result.PartyAtDeparture.Single().Level); // the snapshot itself never moved
    }

    // ---- The consistency pin ---------------------------------------------------------------------

    /// <summary>
    /// The most important test in this file: over a seed sweep of REAL <see cref="ExpeditionResolver"/>
    /// runs (a zero-player-action kernel idle trace mints no MakersMark and so can never itself
    /// produce a beat — beats require player-crafted gear), the query's staged verdict must never
    /// contradict <see cref="AttributionEngine.ComputeBeats"/>. Every staged fall corresponds to a
    /// beat; every beat gets either its own staging or an explicit <see cref="TellingShape.MarginOnly"/>
    /// downgrade (PotionLifesave only). If this ever disagrees, the fix belongs in the query, never
    /// in the engine.
    /// </summary>
    [Fact]
    public void ConsistencyPin_StagedVerdictNeverContradictsComputeBeats_AcrossASeedSweep()
    {
        var beatsChecked = 0;

        void Check(ExpeditionResult result, ImmutableSortedDictionary<int, Item> items)
        {
            Assert.False(result.PartyAtDeparture.IsEmpty); // Unit A's own guarantee, sanity-checked here

            foreach (var beat in result.Beats)
            {
                beatsChecked++;
                var script = TellingQuery.Build(result, beat, items, VenueRegistry.Mine);

                switch (beat.Beat)
                {
                    case BeatType.KillingBlow:
                        Assert.Equal(TellingShape.KillingBlowShape, script.Shape);
                        break;

                    case BeatType.LethalSave:
                        Assert.Equal(TellingShape.LethalSaveShape, script.Shape);
                        var lethal = Assert.IsType<LethalSavePayload>(script.Payload);
                        Assert.True(lethal.HeroHpAfterWithItem > 0);
                        Assert.True(lethal.HeroHpAfterWithoutItem <= 0);
                        break;

                    case BeatType.BreakpointClear:
                        Assert.Equal(TellingShape.BreakpointClearShape, script.Shape);
                        var bp = Assert.IsType<BreakpointClearPayload>(script.Payload);
                        Assert.True(bp.PartyAveragePowerWithItem >= bp.Gate);
                        Assert.True(bp.PartyAveragePowerWithoutItem < bp.Gate);
                        break;

                    case BeatType.Provisioned:
                        Assert.Equal(TellingShape.ProvisionedShape, script.Shape);
                        var prov = Assert.IsType<ProvisionedPayload>(script.Payload);
                        Assert.True(prov.NaiveHpWithoutHeal > 0);
                        break;

                    case BeatType.PotionLifesave:
                        Assert.True(script.Shape is TellingShape.PotionLifesaveShape or TellingShape.MarginOnly);
                        if (script.Shape == TellingShape.PotionLifesaveShape)
                        {
                            var life = Assert.IsType<PotionLifesavePayload>(script.Payload);
                            Assert.True(life.HpAtDivergence <= 0);
                        }
                        else
                        {
                            var margin = Assert.IsType<MarginOnlyPayload>(script.Payload);
                            Assert.True(margin.MinHpReached > 0);
                        }

                        break;

                    default:
                        Assert.Fail($"Beat type {beat.Beat} has no TellingQuery staging.");
                        break;
                }
            }
        }

        // KillingBlow + BreakpointClear font: a strong player weapon reliably lands kills, and a
        // rival-only shield/armor pairing leaves the gate riding on the weapon alone at deeper floors.
        var killWeapon = PlayerWeapon(101, attack: 40);
        var killItems = ImmutableSortedDictionary<int, Item>.Empty.Add(101, killWeapon);
        for (ulong seed = 0; seed < 60; seed++)
        {
            var hero = LiveHeroWith(1, new GearSet(killWeapon.Id, null, null));
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), killItems, VenueRegistry.Mine, targetFloor: 4, new Pcg32(RngState.FromSeed(seed)));
            Check(result, killItems);
        }

        // LethalSave font (the flagship shape): low-hp hero, strong player armor — proven scenario
        // shape from AttributionTests.Ae2, swept wide for volume.
        var lethalArmor = PlayerArmor(102, defense: 12);
        var lethalItems = ImmutableSortedDictionary<int, Item>.Empty.Add(102, lethalArmor);
        for (ulong seed = 0; seed < 300; seed++)
        {
            var hero = LiveHeroWith(1, new GearSet(null, null, lethalArmor.Id), hp: 14);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), lethalItems, VenueRegistry.Mine, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            Check(result, lethalItems);
        }

        // Provisioned / PotionLifesave font: a marked salve in the pack — proven scenario shape from
        // ConsumableAttributionTests.ResolverIntegration, swept wide for volume.
        var salve = Salve(103);
        var salveItems = ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve);
        for (ulong seed = 0; seed < 300; seed++)
        {
            var hero = LiveHeroWith(1, GearSet.Empty, hp: 30) with { Pack = ImmutableList.Create(salve.Id) };
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), salveItems, VenueRegistry.Mine, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            Check(result, salveItems);
        }

        Assert.True(beatsChecked > 20, $"sweep exercised only {beatsChecked} beats — widen the sweep.");
    }
}
