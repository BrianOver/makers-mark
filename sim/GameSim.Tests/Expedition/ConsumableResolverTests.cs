using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// The auto-quaff rule (P2, re-ordered 2026-08-01): at the top of a round, a hero at the flee
/// line LEAVES — no salve cancels that — and a hero who is merely wounded (below the drink line,
/// or one worst-case blow from death) drinks the first Heal item in pack order and fights on.
/// Preparation is insurance for a fight the hero was already taking, never a gamble substituted
/// for a safe exit. Deterministic, no RNG drawn for the quaff itself; with no Heal item in the
/// pack, behavior is byte-identical to the pre-P2 resolver.
/// </summary>
public class ConsumableResolverTests
{
    private static Item Salve(int id, int magnitude = 6, bool marked = true) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), marked ? new MakersMark("You", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, magnitude));

    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "shortsword", "Shortsword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Packed(int id, int hp, GearSet? gear = null, params ItemId[] pack) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: hp, Gold: 30,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    {
        Pack = pack.ToImmutableList(),
    };

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static IEnumerable<ConsumableUse> AllUses(ExpeditionResult result) =>
        result.Floors.SelectMany(f => f.Combats).SelectMany(c => c.Uses);

    [Fact]
    public void QuaffFires_WhileStillAboveTheFleeLine_NeverInsteadOfFleeing()
    {
        // RE-POINTED (2026-08-01, owner ruling "prefer more prepared heroes"): this test used to
        // assert the OPPOSITE — that the quaff fires AT the flee line and cancels the flee. That
        // rule made carrying a salve strictly lethal (a guaranteed-survival exit swapped for a
        // fight the hero could lose; Prepared measured 73% mortality vs Reckless's 55%). The rule
        // is now flee-FIRST: a hero at the flee line leaves, and a salve is drunk EARLIER, while
        // the hero still has a margin. So the invariant flips — every recorded in-fight use must
        // land ABOVE the flee line. Seed-swept: deterministic per seed, so the found seed is
        // stable forever (existing suite convention).
        var salve = Salve(10);
        var items = Catalog(salve);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Packed(1, hp: 30, gear: null, salve.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            // In-fight uses only: the post-floor quaff has its own test and its own timing.
            var use = result.Floors
                .SelectMany(f => f.Combats.Select(c => new { Combat = c, Rounds = f.Combats.Count(x => x.Hero == c.Hero) }))
                .SelectMany(x => x.Combat.Uses.Where(u => u.Round <= x.Rounds))
                .FirstOrDefault();
            if (use is null)
            {
                continue;
            }

            // The drink happened while the hero was still SAFE to keep fighting — insurance,
            // not a replacement for the exit.
            Assert.False(CombatMath.ShouldFlee(use.HpBefore, hero.MaxHp),
                $"quaff at {use.HpBefore}/{hero.MaxHp} — at or below the flee line, so it cancelled a survivable flee");

            // Never spent for a zero-point heal: a full-health hero has no headroom to restore.
            Assert.True(use.HpBefore < hero.MaxHp, "quaff burned at full health — heal capped to nothing");

            Assert.Equal(Math.Min(use.HpBefore + 6, hero.MaxHp), use.HpAfter);
            Assert.Equal(salve.Id, use.Item);
            return; // proven
        }

        Assert.Fail("No quaff across 200 seeds — the auto-quaff rule never fired.");
    }

    [Fact]
    public void Quaff_CapsAtMaxHp()
    {
        // An oversized heal can never push hp past MaxHp.
        var megaSalve = Salve(10, magnitude: 999);
        var items = Catalog(megaSalve);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Packed(1, hp: 30, gear: null, megaSalve.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            var use = AllUses(result).FirstOrDefault();
            if (use is null)
            {
                continue;
            }

            Assert.Equal(hero.MaxHp, use.HpAfter);
            return;
        }

        Assert.Fail("No quaff across 200 seeds — cannot prove the MaxHp cap.");
    }

    [Fact]
    public void PackDepletion_NeverMoreUsesThanStock()
    {
        var salveA = Salve(10);
        var salveB = Salve(11);
        var items = Catalog(salveA, salveB);

        var sawTwo = false;
        for (ulong seed = 0; seed < 300; seed++)
        {
            var oneStock = Packed(1, hp: 30, gear: null, salveA.Id);
            var one = ExpeditionResolver.Resolve(
                ImmutableList.Create(oneStock), items, VenueRegistry.Mine, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            Assert.True(AllUses(one).Count() <= 1, $"seed {seed}: one salve, multiple uses");

            var twoStock = Packed(1, hp: 30, gear: null, salveA.Id, salveB.Id);
            var two = ExpeditionResolver.Resolve(
                ImmutableList.Create(twoStock), items, VenueRegistry.Mine, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            var uses = AllUses(two).ToList();
            Assert.True(uses.Count <= 2, $"seed {seed}: two salves, {uses.Count} uses");
            if (uses.Count == 2)
            {
                sawTwo = true;
                // Pack order is the consumption order: front item drinks first.
                Assert.Equal(salveA.Id, uses[0].Item);
                Assert.Equal(salveB.Id, uses[1].Item);
            }
        }

        Assert.True(sawTwo, "no seed produced two uses — multiple quaffs per expedition unproven");
    }

    [Fact]
    public void NoHealInPack_ByteIdenticalToEmptyPack()
    {
        // A pack holding a consumable with no effect changes nothing: the quaff rule
        // keys off ConsumableEffect DATA, and an ineligible pack must not perturb a
        // single roll or outcome.
        var inert = new Item(
            new ItemId(10), "trail-biscuit", "Trail Biscuit", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 0), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var items = Catalog(inert);

        for (ulong seed = 0; seed < 20; seed++)
        {
            var emptyPack = ExpeditionResolver.Resolve(
                ImmutableList.Create(Packed(1, hp: 30)), items, VenueRegistry.Mine, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            var inertPack = ExpeditionResolver.Resolve(
                ImmutableList.Create(Packed(1, hp: 30, gear: null, inert.Id)), items, VenueRegistry.Mine, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));

            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(emptyPack),
                System.Text.Json.JsonSerializer.Serialize(inertPack));
        }
    }

    [Fact]
    public void DeathFromAboveFleeThreshold_IsNotSaved()
    {
        // A hero one-shot from above the flee threshold never gets to quaff: the rule
        // fires at the top of a round, not on the killing blow. MaxHp 12 on floor 1
        // (flee below 3): a hit of 12+ kills from full health with the salve unopened.
        var salve = Salve(10);
        var items = Catalog(salve);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Packed(1, hp: 12, gear: null, salve.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 1, new Pcg32(RngState.FromSeed(seed)));

            if (result.Deaths.Contains(hero.Id) && result.Floors[0].Combats.Count == 1)
            {
                // Died to the first hit — straight from 12/12, no quaff recorded.
                Assert.Empty(AllUses(result));
                return; // proven
            }
        }

        Assert.Fail("No first-hit death across 200 seeds — scenario needs retuning.");
    }

    [Fact]
    public void PostFloorTooHurtCheck_QuaffsBySameRule_AndRecordsPastFightRound()
    {
        // The post-floor "too hurt to continue" check drinks by the same rule; the use
        // is recorded on the hero's last combat event with Round past the fight's
        // rounds (it healed after the fight's damage). Tuned so an in-fight +1 heal
        // leaves the hero below the threshold when the monster dies.
        //
        // 2026-08-01: the post-floor bar is now the DRINK line (50%), not the flee line — with
        // flee checked first and never cancelled, a hero can no longer END a cleared floor below
        // the flee line (the killing round deals them no damage), so the old bar made this whole
        // branch unreachable. This test failing with "no post-floor quaff across 500 seeds" is
        // exactly how that was caught; it asserts the drink-line rule now.
        var weapon = PlayerWeapon(20, attack: 5);
        var drops = new[] { Salve(10, magnitude: 1), Salve(11, magnitude: 1), Salve(12, magnitude: 1) };
        var items = Catalog([weapon, .. drops]);
        var gear = new GearSet(weapon.Id, null, null);

        for (ulong seed = 0; seed < 500; seed++)
        {
            var hero = Packed(1, hp: 36, gear, drops.Select(d => d.Id).ToArray());
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            foreach (var floor in result.Floors)
            {
                var rounds = floor.Combats.Count(c => c.Hero == hero.Id);
                var postFloorUse = floor.Combats
                    .SelectMany(c => c.Uses)
                    .FirstOrDefault(u => u.Round > rounds);
                if (postFloorUse is not null)
                {
                    // Same rule: it fired below the DRINK line (the post-floor bar) and healed capped.
                    Assert.True(CombatMath.ShouldDrink(postFloorUse.HpBefore, hero.MaxHp, 0));
                    Assert.Equal(Math.Min(postFloorUse.HpBefore + 1, hero.MaxHp), postFloorUse.HpAfter);
                    // And it sits on the hero's LAST event of the floor.
                    Assert.Contains(postFloorUse, floor.Combats.Last(c => c.Hero == hero.Id).Uses);
                    return; // proven
                }
            }
        }

        Assert.Fail("No post-floor quaff across 500 seeds — scenario needs retuning.");
    }

    [Fact]
    public void Purity_SamePacks_IdenticalResult()
    {
        var salve = Salve(10);
        var items = Catalog(salve);
        var party = ImmutableList.Create(Packed(1, hp: 30, gear: null, salve.Id), Packed(2, hp: 25));

        var a = ExpeditionResolver.Resolve(party, items, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(9)));
        var b = ExpeditionResolver.Resolve(party, items, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(9)));

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(a),
            System.Text.Json.JsonSerializer.Serialize(b));
    }

    [Fact]
    public void FleesInsteadOfDrinking_WhenTheOnlyHealCannotClearTheWorstCase()
    {
        // link1/link2 fix (2026-08-17): found by instrumenting a 90-seed balance sweep — 6 of 6
        // recorded deaths carrying a Heal item were a hero who quaffed correctly, then died the
        // SAME round anyway because the salve's Magnitude never had a chance of covering that
        // floor's worst-case hit. A hero one worst-case blow from death, holding a Heal item too
        // weak to clear that SAME worst case even after drinking, must flee instead of gambling a
        // fight the item cannot secure — a hero whose heal WOULD clear the risk must still drink
        // and fight (unchanged; see QuaffFires_WhileStillAboveTheFleeLine_NeverInsteadOfFleeing).
        //
        // Floor 1: MonsterAttack 11, MonsterDefense 4; no gear -> heroDefense 1, so the worst-case
        // hit is CombatMath.MonsterDamage(11, 5, 1) = 15. A magnitude-1 salve heals for nothing
        // useful against that gap. MaxHp 40 puts the ordinary flee line at hp < 10 (25%) while the
        // at-risk line (CouldDieNextRound) is hp <= 15 — a real 6-value band (hp 10..15) where a
        // hero is above the flee line yet still one worst-case blow from death, and the pack's
        // one Heal item (+1) cannot lift them clear of that same 15-point gap.
        var weakSalve = Salve(10, magnitude: 1);
        var items = Catalog(weakSalve);

        for (ulong seed = 0; seed < 2000; seed++)
        {
            var hero = Packed(1, hp: 40, gear: null, weakSalve.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            if (!result.Deaths.Contains(hero.Id) && result.Halt == ExpeditionHalt.FloorLost)
            {
                var fledFloor = result.Floors.Last();
                var hpAtFlee = hero.MaxHp - fledFloor.Combats.Where(c => c.Hero == hero.Id).Sum(c => c.DamageTaken)
                    - result.Floors.Where(f => f.Floor < fledFloor.Floor)
                        .SelectMany(f => f.Combats).Where(c => c.Hero == hero.Id).Sum(c => c.DamageTaken);

                if (hpAtFlee <= 0 || CombatMath.ShouldFlee(hpAtFlee, hero.MaxHp))
                {
                    continue; // an ordinary below-the-line flee — not the scenario under test
                }

                // Above the ordinary flee line, but still at risk, with a heal too weak to help:
                // proves the NEW branch fired, not the pre-existing 25%-line flee.
                Assert.True(CombatMath.CouldDieNextRound(hpAtFlee, VenueRegistry.Mine.MonsterAttack(fledFloor.Floor), 1),
                    $"fled at {hpAtFlee}/{hero.MaxHp} without being at risk — not the scenario under test");
                Assert.DoesNotContain(AllUses(result), u => u.Item == weakSalve.Id);
                return; // proven
            }
        }

        Assert.Fail("No above-the-line, at-risk, insufficient-heal flee across 2000 seeds — scenario needs retuning.");
    }

    [Fact]
    public void StillDrinksAndFights_WhenTheHealWouldClearTheWorstCase()
    {
        // The mirror of the test above: a hero at risk whose Heal item WOULD clear that same
        // worst-case hit must still drink and fight on, exactly as before this fix. Default
        // Salve magnitude (6) against floor 1's worst-case-15 gap comfortably clears from most
        // at-risk starting points, so this is the common case, not a corner one.
        var goodSalve = Salve(10, magnitude: 6);
        var items = Catalog(goodSalve);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Packed(1, hp: 60, gear: null, goodSalve.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            var use = AllUses(result).FirstOrDefault(u => u.Item == goodSalve.Id);
            if (use is not null && CombatMath.CouldDieNextRound(use.HpBefore, VenueRegistry.Mine.MonsterAttack(2), 1))
            {
                // Quaffed while genuinely at risk (not just the 50% comfort line) and the fight
                // continued — the salve was strong enough, so no flee was needed.
                Assert.False(CombatMath.ShouldFlee(use.HpBefore, hero.MaxHp));
                return; // proven
            }
        }

        Assert.Fail("No at-risk drink-and-fight across 200 seeds — scenario needs retuning.");
    }
}
