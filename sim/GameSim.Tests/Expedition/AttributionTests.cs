using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

/// <summary>
/// The product's core promise (R11, KTD6): attribution is a computed fact over
/// recorded rolls — never a heuristic, never a fresh RNG draw.
/// </summary>
public class AttributionTests
{
    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "shortsword", "Fine Shortsword", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item PlayerArmor(int id, int defense) => new(
        new ItemId(id), "chain-vest", "Fine Chain Vest", ItemSlot.Armor, QualityGrade.Fine,
        new ItemStats(0, defense, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item RivalArmor(int id, int defense) => new(
        new ItemId(id), "chain-vest", "Common Chain Vest", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 4), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero HeroWith(int id, GearSet gear, int hp = 30) => new(
        new HeroId(id), "Torvald", HeroRole.Vanguard, Level: 3, MaxHp: hp, Gold: 50,
        gear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 4, DiedOnDay: null);

    [Fact]
    public void Ae1_PlayerBladeKillingBlow_ProducesKillingBlowBeat_WithItemId()
    {
        var weapon = PlayerWeapon(1, attack: 40);
        var hero = HeroWith(1, new GearSet(weapon.Id, null, null));
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon);

        var result = ExpeditionResolver.Resolve(
            ImmutableList.Create(hero), items, targetFloor: 2, new Pcg32(RngState.FromSeed(7)));

        var kill = result.Beats.FirstOrDefault(b => b.Beat == BeatType.KillingBlow);
        Assert.NotNull(kill);
        Assert.Equal(weapon.Id, kill!.Item);
        Assert.Equal(hero.Id, kill.Hero);
    }

    [Fact]
    public void Ae2_LethalWithoutPlayerArmor_SurvivableWithIt_ProducesLethalSaveBeat()
    {
        // Low HP hero + strong player armor: some hit must be lethal-without, survivable-with.
        var armor = PlayerArmor(1, defense: 12);
        var hero = HeroWith(1, new GearSet(null, null, armor.Id), hp: 14);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, armor);

        // Sweep seeds until the scenario occurs — deterministic given the seed, so the
        // found seed is stable forever. The engine must detect it via recorded rolls.
        for (ulong seed = 0; seed < 200; seed++)
        {
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            if (result.Beats.Any(b => b.Beat == BeatType.LethalSave && b.Item == armor.Id))
            {
                return; // proven
            }
        }

        Assert.Fail("No lethal-save beat found across 200 seeds — engine or scenario broken.");
    }

    [Fact]
    public void Ae2_NoFalseCredit_WhenHitSurvivableEitherWay()
    {
        // Massive HP: no hit can be lethal, so no lethal-save beat may ever appear.
        var armor = PlayerArmor(1, defense: 12);
        var hero = HeroWith(1, new GearSet(null, null, armor.Id), hp: 500);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, armor);

        for (ulong seed = 0; seed < 50; seed++)
        {
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));
            Assert.DoesNotContain(result.Beats, b => b.Beat == BeatType.LethalSave);
        }
    }

    [Fact]
    public void NoBeats_ForRivalGear_EvenWhenDecisive()
    {
        var armor = RivalArmor(1, defense: 12);
        var hero = HeroWith(1, new GearSet(null, null, armor.Id), hp: 14);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, armor);

        for (ulong seed = 0; seed < 50; seed++)
        {
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));
            Assert.Empty(result.Beats.Where(b => b.Item == armor.Id));
        }
    }

    [Fact]
    public void CounterfactualPurity_ConsumesZeroRngState()
    {
        var weapon = PlayerWeapon(1, attack: 40);
        var armor = PlayerArmor(2, defense: 10);
        var hero = HeroWith(1, new GearSet(weapon.Id, null, armor.Id), hp: 20);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon).Add(2, armor);

        var rng = new Pcg32(RngState.FromSeed(11));
        var result = ExpeditionResolver.Resolve(ImmutableList.Create(hero), items, 3, rng);
        var streamAfterResolve = rng.Snapshot();

        // Re-running attribution over the same result must not touch any RNG and must
        // reproduce identical beats from the recorded rolls alone.
        var beatsAgain = AttributionEngine.ComputeBeats(result.Floors, ImmutableList.Create(hero), items);
        Assert.Equal(streamAfterResolve, rng.Snapshot());
        Assert.Equal(result.Beats, beatsAgain);
    }

    [Fact]
    public void Ae3_RivalOnlyGear_NeverClearsFloor5_PlayerGearCan()
    {
        // Rival-typical loadout: Common tier-2 equivalents, no marks.
        var rivalItems = ImmutableSortedDictionary<int, Item>.Empty
            .Add(1, new Item(new ItemId(1), "longsword", "Common Longsword", ItemSlot.Weapon, QualityGrade.Common, new ItemStats(20, 0, 5), null, ImmutableList<ItemHistoryEntry>.Empty))
            .Add(2, new Item(new ItemId(2), "kite-shield", "Common Kite Shield", ItemSlot.Shield, QualityGrade.Common, new ItemStats(0, 16, 6), null, ImmutableList<ItemHistoryEntry>.Empty))
            .Add(3, new Item(new ItemId(3), "hauberk", "Common Hauberk", ItemSlot.Armor, QualityGrade.Common, new ItemStats(0, 18, 9), null, ImmutableList<ItemHistoryEntry>.Empty));

        // Player Masterwork tier-3 loadout: strictly above the floor-5 gate.
        var playerItems = ImmutableSortedDictionary<int, Item>.Empty
            .Add(1, new Item(new ItemId(1), "greatsword", "Masterwork Greatsword", ItemSlot.Weapon, QualityGrade.Masterwork, new ItemStats(64, 0, 10), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty))
            .Add(2, new Item(new ItemId(2), "bulwark", "Masterwork Bulwark", ItemSlot.Shield, QualityGrade.Masterwork, new ItemStats(0, 54, 12), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty))
            .Add(3, new Item(new ItemId(3), "full-plate", "Masterwork Full Plate", ItemSlot.Armor, QualityGrade.Masterwork, new ItemStats(0, 60, 15), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty));

        var gear = new GearSet(new ItemId(1), new ItemId(2), new ItemId(3));

        var rivalCleared = 0;
        var playerCleared = 0;
        for (ulong seed = 0; seed < 100; seed++)
        {
            var rivalParty = ImmutableList.Create(HeroWith(1, gear), HeroWith(2, gear) with { Id = new HeroId(2) }, HeroWith(3, gear) with { Id = new HeroId(3) });
            var r1 = ExpeditionResolver.Resolve(rivalParty, rivalItems, 5, new Pcg32(RngState.FromSeed(seed)));
            if (r1.DeepestFloorCleared >= 5) rivalCleared++;

            var playerParty = ImmutableList.Create(HeroWith(1, gear), HeroWith(2, gear) with { Id = new HeroId(2) }, HeroWith(3, gear) with { Id = new HeroId(3) });
            var r2 = ExpeditionResolver.Resolve(playerParty, playerItems, 5, new Pcg32(RngState.FromSeed(seed)));
            if (r2.DeepestFloorCleared >= 5) playerCleared++;
        }

        Assert.Equal(0, rivalCleared);   // structural wipe/retreat — every seed (AE3)
        Assert.True(playerCleared > 50, $"player-geared parties cleared only {playerCleared}/100");
    }
}
