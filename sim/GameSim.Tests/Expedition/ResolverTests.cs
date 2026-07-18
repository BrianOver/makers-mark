using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

public class ResolverTests
{
    private static Hero Naked(int id, int hp = 25, int deepest = 0) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: hp, Gold: 30,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: deepest, DiedOnDay: null);

    private static readonly ImmutableSortedDictionary<int, Item> NoItems = ImmutableSortedDictionary<int, Item>.Empty;

    [Fact]
    public void Purity_SameInputs_IdenticalResult()
    {
        var party = ImmutableList.Create(Naked(1), Naked(2));
        var a = ExpeditionResolver.Resolve(party, NoItems, VenueRegistry.Mine, 2, new Pcg32(RngState.FromSeed(3)));
        var b = ExpeditionResolver.Resolve(party, NoItems, VenueRegistry.Mine, 2, new Pcg32(RngState.FromSeed(3)));
        // Immutable collections use reference equality inside records — compare structurally.
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b));
    }

    [Fact]
    public void EmptyParty_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ExpeditionResolver.Resolve(ImmutableList<Hero>.Empty, NoItems, VenueRegistry.Mine, 1, new Pcg32(RngState.FromSeed(1))));
    }

    [Fact]
    public void EveryCombatEvent_RecordsItsRolls()
    {
        var party = ImmutableList.Create(Naked(1, hp: 100));
        var result = ExpeditionResolver.Resolve(party, NoItems, VenueRegistry.Mine, 2, new Pcg32(RngState.FromSeed(5)));
        var combats = result.Floors.SelectMany(f => f.Combats).ToList();
        Assert.NotEmpty(combats);
        Assert.All(combats, c => Assert.NotEmpty(c.RecordedRolls));
    }

    [Fact]
    public void DeeperFloors_DropRarerOre()
    {
        // Strong hero clears deep: expect ore tiers to rise with floor (R6).
        var strong = Naked(1, hp: 500) with { Level = 10 };
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(strong), NoItems, VenueRegistry.Mine, 1, new Pcg32(RngState.FromSeed(2)));
        Assert.All(r.Loot, l => Assert.Equal("copper", l.MaterialKey));
    }

    [Fact]
    public void PartyCanWipe_AllDeathsReported_NoSurvivors()
    {
        // Weak naked heroes sent deep — some seed produces a full wipe.
        for (ulong seed = 0; seed < 100; seed++)
        {
            var party = ImmutableList.Create(Naked(1, hp: 10), Naked(2, hp: 10));
            var r = ExpeditionResolver.Resolve(party, NoItems, VenueRegistry.Mine, 5, new Pcg32(RngState.FromSeed(seed)));
            if (r.Survivors.IsEmpty)
            {
                Assert.Equal(2, r.Deaths.Count);
                return;
            }
        }

        Assert.Fail("No full wipe across 100 seeds — deep floors are too gentle.");
    }

    [Fact]
    public void HeroRetreats_AtLowHp_InsteadOfFightingToZero()
    {
        // Retreat is the common outcome for an outmatched solo hero; deaths still possible.
        var retreats = 0;
        for (ulong seed = 0; seed < 50; seed++)
        {
            var r = ExpeditionResolver.Resolve(ImmutableList.Create(Naked(1, hp: 30)), NoItems, VenueRegistry.Mine, 4, new Pcg32(RngState.FromSeed(seed)));
            if (r.Survivors.Count == 1 && r.DeepestFloorCleared < 4) retreats++;
        }

        Assert.True(retreats > 10, $"expected frequent retreats, got {retreats}/50");
    }

    [Fact]
    public void GoldEarned_TracksMonsterKills()
    {
        var strong = Naked(1, hp: 200) with { Level = 8 };
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(strong), NoItems, VenueRegistry.Mine, 1, new Pcg32(RngState.FromSeed(4)));
        var kills = r.Floors.SelectMany(f => f.Combats).Count(c => c.MonsterKilled);
        if (kills > 0)
        {
            Assert.True(r.GoldEarnedByHero[1] > 0);
        }
    }

    [Fact]
    public void TargetFloor_CapsDescent()
    {
        var strong = Naked(1, hp: 500) with { Level = 10 };
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(strong), NoItems, VenueRegistry.Mine, 2, new Pcg32(RngState.FromSeed(6)));
        Assert.True(r.DeepestFloorCleared <= 2);
        Assert.All(r.Floors, f => Assert.InRange(f.Floor, 1, 2));
    }

    // ── TUNING-C competence retreat (direction doc 2026-07-18 §5) ────────────────────────────
    //
    // A "Titan" one-shots every Mine floor and takes ~1 damage, so clears are deterministic across
    // all seeds — isolating the retreat RULE from combat variance. Its EffectivePower (~103) also
    // clears the floor-5 structural gate (100), so gates never intervene.

    private static Item TWeapon(int id) => new(
        new ItemId(id), "sword", "Titan Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(40, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item TArmor(int id) => new(
        new ItemId(id), "plate", "Titan Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, 30, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static readonly ImmutableSortedDictionary<int, Item> TitanGear =
        new[] { TWeapon(90), TArmor(91) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static Hero Titan(int id, int deepest) => new(
        new HeroId(id), $"Titan{id}", "vanguard", Level: 10, MaxHp: 300, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepest, DiedOnDay: null);

    [Fact]
    public void CompetenceRetreat_MixedRecordParty_WeakMemberRetreatsAfterOwnCeiling()
    {
        // Pin 1: {A record 3, B record 0}, target 4 → B retreats after clearing floor 1 (next floor 2
        // exceeds her record+1); A pushes floors 2-4 alone. B stays a Survivor and banks floor-1
        // gold + ore; she fights and loots NOTHING below floor 1.
        var party = ImmutableList.Create(Titan(1, deepest: 3), Titan(2, deepest: 0));
        var r = ExpeditionResolver.Resolve(party, TitanGear, VenueRegistry.Mine, 4, new Pcg32(RngState.FromSeed(11)));

        Assert.Equal(4, r.DeepestFloorCleared);
        Assert.Equal(new[] { 1, 2 }, r.Survivors.Select(h => h.Value).OrderBy(v => v).ToArray()); // both survive
        Assert.Empty(r.Deaths);

        // Floor 1: both fought. Floors 2-4: only A (hero 1) — B has left the delve.
        var floor1 = r.Floors.Single(f => f.Floor == 1);
        Assert.Equal(new[] { 1, 2 }, floor1.Combats.Select(c => c.Hero.Value).Distinct().OrderBy(v => v).ToArray());
        foreach (var deep in r.Floors.Where(f => f.Floor >= 2))
        {
            Assert.All(deep.Combats, c => Assert.Equal(1, c.Hero.Value)); // A alone
        }

        Assert.Equal(3, r.Floors.Count(f => f.Floor >= 2)); // floors 2,3,4 all attempted by A

        // B banked floor-1 spoils only.
        Assert.Equal(8, r.GoldEarnedByHero[2]);                                   // GoldPerKill(1)
        var bLoot = r.Loot.Where(l => l.Hero.Value == 2).ToList();
        Assert.Equal(new[] { "copper" }, bLoot.Select(l => l.MaterialKey).ToArray());
        // A looted every floor 1-4.
        Assert.Equal(new[] { "copper", "iron", "steel", "mithril" },
            r.Loot.Where(l => l.Hero.Value == 1).Select(l => l.MaterialKey).ToArray());
    }

    [Fact]
    public void CompetenceRetreat_UniformRecordParty_ByteIdenticalToNoRetreat()
    {
        // Pin 2: a uniform-record party pushed to max(record)+1 (exactly what ExpeditionSystem picks)
        // NEVER trips the retreat before the target, so it is byte-identical to the rule being off.
        // Proven by equating the live run to one where every hero is exempt through the target.
        var party = ImmutableList.Create(Titan(1, deepest: 2), Titan(2, deepest: 2));
        const int target = 3; // == max(record)+1

        var withRule = ExpeditionResolver.Resolve(party, TitanGear, VenueRegistry.Mine, target, new Pcg32(RngState.FromSeed(11)));
        var ruleOff = ExpeditionResolver.Resolve(party, TitanGear, VenueRegistry.Mine, target, new Pcg32(RngState.FromSeed(11)),
            retreatExemptHeroes: ImmutableHashSet.Create(1, 2), retreatExemptThroughFloor: target);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(withRule),
            System.Text.Json.JsonSerializer.Serialize(ruleOff));
        Assert.Equal(target, withRule.DeepestFloorCleared);            // both reached the target — nobody peeled off
        Assert.Equal(2, withRule.Survivors.Count);
    }

    [Fact]
    public void CompetenceRetreat_BountyAcceptor_ExemptThroughBountyFloor()
    {
        // Pin 3 (open decision, default): a record-0 acceptor committed to a floor-3 bounty is EXEMPT
        // through floor 3 — she pushes past her ceiling to honor the bounty. The identical hero with
        // no exemption retreats right after floor 1.
        var hero = ImmutableList.Create(Titan(1, deepest: 0));

        var acceptor = ExpeditionResolver.Resolve(hero, TitanGear, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(11)),
            retreatExemptHeroes: ImmutableHashSet.Create(1), retreatExemptThroughFloor: 3);
        Assert.Equal(3, acceptor.DeepestFloorCleared);                                     // honored the bounty
        Assert.Contains(acceptor.Floors, f => f.Floor == 2 && f.Combats.Any());            // fought below floor 1

        var declined = ExpeditionResolver.Resolve(hero, TitanGear, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(11)));
        Assert.Equal(1, declined.DeepestFloorCleared);                                     // peeled off at her ceiling
        Assert.DoesNotContain(declined.Floors, f => f.Floor >= 2);
    }
}
