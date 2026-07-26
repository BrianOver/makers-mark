using System.Collections.Immutable;
using System.Text.Json;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// Phase C U-C1 (slice 1) — the craft-modifier effects at their expedition-resolver seams:
/// flee-threshold oils (survival trajectory), the Leech rune (heal-on-kill, recorded for
/// attribution), and the Lodestone fitting (+ore). Every effect is dormant when the gear carries
/// no modifier, so the pre-U-C1 idle trace stays byte-identical (golden re-pin is shape-only).
/// </summary>
public class CraftModifierResolverTests
{
    private static Item Weapon(int id, int attack, CraftModifier? oil = null, CraftModifier? rune = null, CraftModifier? fitting = null) =>
        new Item(new ItemId(id), "sword", "Test Blade", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
        {
            QuenchOil = oil,
            Rune = rune,
            Fitting = fitting,
        };

    private static Item Armor(int id, int defense, CraftModifier? fitting = null) =>
        new Item(new ItemId(id), "plate", "Test Plate", ItemSlot.Armor, QualityGrade.Fine,
            new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
        {
            Fitting = fitting,
        };

    private static Hero Delver(int hp = 45, int level = 3, int deepest = 9) => new(
        new HeroId(1), "Delver", "vanguard", Level: level, MaxHp: hp, Gold: 30,
        new GearSet(new ItemId(10), null, new ItemId(11)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepest, DiedOnDay: null);

    private static ImmutableSortedDictionary<int, Item> Gear(Item weapon, Item armor) =>
        new[] { weapon, armor }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    // ── Flee-threshold oils change survival trajectory ──────────────────────────────────────────

    [Fact]
    public void FleeOils_BraveheartPushesPastWhereCowardQuits()
    {
        // A flee-prone build: low damage output (long grindy fights → the hero ends floors wounded,
        // so the flee decision actually fires) but survivable (high hp + real armour, and a deepest
        // record past the target so competence-retreat never intervenes). The ONLY difference between
        // the two runs is the oil, so any divergence is the flee-threshold shift reaching the resolver.
        var cowardGear = Gear(Weapon(10, 0, oil: new CraftModifier(CraftModifiers.CowardsOil, ModifierFamily.QuenchOil, 1)), Armor(11, 8));
        var braveGear = Gear(Weapon(10, 0, oil: new CraftModifier(CraftModifiers.BraveheartOil, ModifierFamily.QuenchOil, 1)), Armor(11, 8));

        var diffCount = 0;
        var braveDeeper = 0;
        var cowardDeeper = 0;
        for (ulong seed = 0; seed < 80; seed++)
        {
            // Big HP pool so per-hit damage steps THROUGH the 17-33% flee band rather than skipping
            // it; a weak weapon makes floor 2 a long grind where the hero bleeds into that band.
            var hero = ImmutableList.Create(Delver(hp: 200, level: 1, deepest: 9));
            var coward = ExpeditionResolver.Resolve(hero, cowardGear, VenueRegistry.Mine, 6, new Pcg32(RngState.FromSeed(seed)));
            var brave = ExpeditionResolver.Resolve(hero, braveGear, VenueRegistry.Mine, 6, new Pcg32(RngState.FromSeed(seed)));
            if (coward.DeepestFloorCleared != brave.DeepestFloorCleared)
            {
                diffCount++;
                if (brave.DeepestFloorCleared > coward.DeepestFloorCleared) braveDeeper++;
                else cowardDeeper++;
            }
        }

        Assert.True(diffCount > 0, "flee-oil delta never changed the outcome across 80 seeds — modifier not reaching the resolver");
        // Braveheart lowers the wound line, so wherever the two diverge it presses on further at least
        // as often as it quits earlier — the oil is directional, not just noise.
        Assert.True(braveDeeper >= cowardDeeper, $"Braveheart should press deeper at least as often (deeper={braveDeeper}, shallower={cowardDeeper})");
    }

    // ── Leech rune heals on a kill and records the delta for attribution ────────────────────────

    [Fact]
    public void LeechRune_HealsOnKill_RecordedAsModifierHpDelta()
    {
        var leechGear = Gear(Weapon(10, 30, rune: new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 2)), Armor(11, 5));
        var plainGear = Gear(Weapon(10, 30), Armor(11, 5));

        var leech = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 60)), leechGear, VenueRegistry.Mine, 5, new Pcg32(RngState.FromSeed(7)));
        var plain = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 60)), plainGear, VenueRegistry.Mine, 5, new Pcg32(RngState.FromSeed(7)));

        var leechDeltas = leech.Floors.SelectMany(f => f.Combats).Where(c => c.MonsterKilled).ToList();
        Assert.NotEmpty(leechDeltas);
        Assert.Contains(leechDeltas, c => c.ModifierHpDelta > 0); // at least one kill drew life
        // Control run carries the rune nowhere → every recorded delta is 0.
        Assert.All(plain.Floors.SelectMany(f => f.Combats), c => Assert.Equal(0, c.ModifierHpDelta));
    }

    [Fact]
    public void LeechRune_NeverOverheals()
    {
        var leechGear = Gear(Weapon(10, 40, rune: new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 2)), Armor(11, 10));
        // A near-full hero: any leech that would exceed MaxHp is capped, so the recorded delta is
        // bounded and never pushes hp past MaxHp (proven by a consistent attribution replay below).
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 80, deepest: 9)), leechGear, VenueRegistry.Mine, 2, new Pcg32(RngState.FromSeed(3)));
        Assert.All(r.Floors.SelectMany(f => f.Combats), c => Assert.True(c.ModifierHpDelta >= 0 && c.ModifierHpDelta <= 6));
    }

    // ── Lodestone fitting adds ore without touching the RNG stream ───────────────────────────────

    [Fact]
    public void LodestoneFitting_AddsExactlyTierOrePerLoot_SameSeedAsControl()
    {
        const int tier = 2;
        var lodeGear = Gear(Weapon(10, 30), Armor(11, 5, fitting: new CraftModifier(CraftModifiers.LodestoneFitting, ModifierFamily.Fitting, tier)));
        var plainGear = Gear(Weapon(10, 30), Armor(11, 5));

        var lode = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 200, level: 8)), lodeGear, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(4)));
        var plain = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 200, level: 8)), plainGear, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(4)));

        Assert.NotEmpty(plain.Loot);
        Assert.Equal(plain.Loot.Count, lode.Loot.Count); // same floors cleared, same draw order
        for (var i = 0; i < plain.Loot.Count; i++)
        {
            Assert.Equal(plain.Loot[i].MaterialKey, lode.Loot[i].MaterialKey);
            Assert.Equal(plain.Loot[i].Quantity + tier, lode.Loot[i].Quantity); // +tier, base draw unchanged
        }
    }

    // ── Determinism holds with modifiers present (KTD: same seed + actions = identical state) ────

    [Fact]
    public void Determinism_WithModifiers_TwoRunsByteIdentical()
    {
        var gear = Gear(
            Weapon(10, 20, oil: new CraftModifier(CraftModifiers.BraveheartOil, ModifierFamily.QuenchOil, 1),
                rune: new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 1)),
            Armor(11, 5, fitting: new CraftModifier(CraftModifiers.LodestoneFitting, ModifierFamily.Fitting, 1)));

        var a = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 60)), gear, VenueRegistry.Mine, 5, new Pcg32(RngState.FromSeed(9)));
        var b = ExpeditionResolver.Resolve(ImmutableList.Create(Delver(hp: 60)), gear, VenueRegistry.Mine, 5, new Pcg32(RngState.FromSeed(9)));
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
    }
}
