using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

/// <summary>
/// The P2 auto-quaff rule: at the top of a round, a hero who would flee drinks the
/// first Heal item in pack order instead and fights on. Deterministic, no RNG drawn
/// for the quaff itself; with no Heal item in the pack, behavior is byte-identical
/// to the pre-P2 resolver.
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
        new HeroId(id), $"Hero{id}", HeroRole.Vanguard, Level: 1, MaxHp: hp, Gold: 30,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    {
        Pack = pack.ToImmutableList(),
    };

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static IEnumerable<ConsumableUse> AllUses(ExpeditionResult result) =>
        result.Floors.SelectMany(f => f.Combats).SelectMany(c => c.Uses);

    [Fact]
    public void QuaffInsteadOfFlee_RecordsUse_AtFleeThreshold_AndFightContinues()
    {
        // A hero who would flee (hp below 25% MaxHp) with a salve in the pack quaffs
        // and fights on. Seed-swept: deterministic per seed, so the found seed is
        // stable forever (existing suite convention).
        var salve = Salve(10);
        var items = Catalog(salve);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Packed(1, hp: 30, gear: null, salve.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            var use = AllUses(result).FirstOrDefault();
            if (use is null)
            {
                continue;
            }

            // The quaff fired exactly at the flee rule and healed by the magnitude, capped.
            Assert.True(CombatMath.ShouldFlee(use.HpBefore, hero.MaxHp),
                $"quaff at {use.HpBefore}/{hero.MaxHp} — above the flee threshold");
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
                ImmutableList.Create(hero), items, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

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
                ImmutableList.Create(oneStock), items, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            Assert.True(AllUses(one).Count() <= 1, $"seed {seed}: one salve, multiple uses");

            var twoStock = Packed(1, hp: 30, gear: null, salveA.Id, salveB.Id);
            var two = ExpeditionResolver.Resolve(
                ImmutableList.Create(twoStock), items, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
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
                ImmutableList.Create(Packed(1, hp: 30)), items, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            var inertPack = ExpeditionResolver.Resolve(
                ImmutableList.Create(Packed(1, hp: 30, gear: null, inert.Id)), items, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));

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
                ImmutableList.Create(hero), items, targetFloor: 1, new Pcg32(RngState.FromSeed(seed)));

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
        var weapon = PlayerWeapon(20, attack: 5);
        var drops = new[] { Salve(10, magnitude: 1), Salve(11, magnitude: 1), Salve(12, magnitude: 1) };
        var items = Catalog([weapon, .. drops]);
        var gear = new GearSet(weapon.Id, null, null);

        for (ulong seed = 0; seed < 500; seed++)
        {
            var hero = Packed(1, hp: 36, gear, drops.Select(d => d.Id).ToArray());
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            foreach (var floor in result.Floors)
            {
                var rounds = floor.Combats.Count(c => c.Hero == hero.Id);
                var postFloorUse = floor.Combats
                    .SelectMany(c => c.Uses)
                    .FirstOrDefault(u => u.Round > rounds);
                if (postFloorUse is not null)
                {
                    // Same rule: it fired below the flee threshold and healed capped.
                    Assert.True(CombatMath.ShouldFlee(postFloorUse.HpBefore, hero.MaxHp));
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

        var a = ExpeditionResolver.Resolve(party, items, 3, new Pcg32(RngState.FromSeed(9)));
        var b = ExpeditionResolver.Resolve(party, items, 3, new Pcg32(RngState.FromSeed(9)));

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(a),
            System.Text.Json.JsonSerializer.Serialize(b));
    }
}
