using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using Xunit;

namespace GameSim.Tests.Classes.Sentinel;

/// <summary>
/// Behaviour tests for the Sentinel add-on class. Everything here exercises the class's
/// <see cref="ClassDefinition"/> and the shared pure hero pipeline (<see cref="CombatMath"/>,
/// <see cref="ShoppingAi"/>) DIRECTLY, never the registry — so the suite is green whether or not
/// the orchestrator has applied the registration line (the class is inert until registered).
/// Structural conformance (id==key, sane stats, colour range) is covered automatically by
/// <c>ClassConformanceTests</c> once the class is in <c>ClassRegistry.All</c>.
/// </summary>
public class SentinelClassTests
{
    private static readonly ClassDefinition Def = SentinelClass.Definition;

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Test Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static Hero MakeHero(int level = 1, GearSet? gear = null) => new(
        new HeroId(1), "Ward", SentinelClass.Id, level, MaxHp: 40, Gold: 100,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void Identity_IsKebabId_AndDisplayName()
    {
        Assert.Equal("sentinel", Def.Id);
        Assert.Equal("Sentinel", Def.DisplayName);
    }

    [Fact]
    public void DefensiveLean_TankierAndSlowerThanVanguard_StillAnchorsAndShields()
    {
        // The brief: a heavier defensive lean than the Vanguard, slower offense.
        Assert.True(Def.BaseHp > ClassRegistry.Vanguard.BaseHp, "Sentinel should soak more than a Vanguard");
        Assert.True(Def.BaseAttack < ClassRegistry.Vanguard.BaseAttack, "Sentinel should hit slower than a Vanguard");
        Assert.True(Def.IsAnchor);
        Assert.True(Def.AllowsShield);
        Assert.Null(Def.MaxItemWeight); // unlimited carry — a heavy defender hauls anything
    }

    [Fact]
    public void ColorRgb_InRange_AndDistinctFromBuiltIns()
    {
        var (r, g, b) = Def.ColorRgb;
        Assert.InRange(r, 0, 255);
        Assert.InRange(g, 0, 255);
        Assert.InRange(b, 0, 255);
        Assert.NotEqual(ClassRegistry.Vanguard.ColorRgb, Def.ColorRgb);
        Assert.NotEqual(ClassRegistry.Striker.ColorRgb, Def.ColorRgb);
        Assert.NotEqual(ClassRegistry.Mystic.ColorRgb, Def.ColorRgb);
    }

    [Fact]
    public void Combat_BaseAttack_FlowsThroughSharedFormula()
    {
        var hero = MakeHero(level: 2);
        Assert.Equal(Def.BaseAttack, CombatMath.RoleBaseAttack(Def));
        // base + level*2, no gear.
        Assert.Equal(Def.BaseAttack + 2 * 2, CombatMath.HeroAttack(hero, Def, Catalog()));
    }

    [Fact]
    public void Shopping_BearsShield_AndCarriesHeavyGear()
    {
        var hero = MakeHero();

        // Shield-bearer: a shield is NOT a role mismatch.
        var shield = MakeItem(2, ItemSlot.Shield, attack: 0, defense: 5, weight: 2, name: "Kite Shield");
        var shieldVerdict = ShoppingAi.EvaluateItem(hero, Def, shield, price: 5, Catalog(shield));
        Assert.NotEqual(PassReasonKind.RoleMismatch, shieldVerdict.PassReason);

        // No weight cap: a heavy weapon is never TooHeavy, and a real upgrade is a Buy.
        var greatsword = MakeItem(3, ItemSlot.Weapon, attack: 9, defense: 0, weight: 10, name: "Greatsword");
        var verdict = ShoppingAi.EvaluateItem(hero, Def, greatsword, price: 5, Catalog(greatsword));
        Assert.NotEqual(PassReasonKind.TooHeavy, verdict.PassReason);
        Assert.Equal(ShoppingVerdictKind.Buy, verdict.Kind);
    }
}
