using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using Xunit;

namespace GameSim.Tests.Classes.Occultist;

/// <summary>
/// Behaviour tests for the Occultist add-on class. Everything here exercises the class's
/// <see cref="ClassDefinition"/> and the shared pure hero pipeline (<see cref="CombatMath"/>,
/// <see cref="ShoppingAi"/>) DIRECTLY, never the registry — so the suite is green whether or not
/// the orchestrator has applied the registration line (the class is inert until registered).
/// Structural conformance is covered automatically by <c>ClassConformanceTests</c> once registered.
/// </summary>
public class OccultistClassTests
{
    private static readonly ClassDefinition Def = OccultistClass.Definition;

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Test Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static Hero MakeHero(int level = 1, GearSet? gear = null) => new(
        new HeroId(1), "Ward", OccultistClass.Id, level, MaxHp: 40, Gold: 100,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void Identity_IsKebabId_AndDisplayName()
    {
        Assert.Equal("occultist", Def.Id);
        Assert.Equal("Occultist", Def.DisplayName);
    }

    [Fact]
    public void RiskDamageLean_GlassierAndHarderHittingThanMystic_KeepsCasterProfile()
    {
        // The brief: mystic-adjacent, higher risk/damage lean.
        Assert.True(Def.BaseHp < ClassRegistry.Mystic.BaseHp, "Occultist should be glassier than a Mystic");
        Assert.True(Def.BaseAttack > ClassRegistry.Mystic.BaseAttack, "Occultist should hit harder than a Mystic");

        // Keeps the Mystic's caster profile: no shield, no anchor, same light-carry cap.
        Assert.False(Def.IsAnchor);
        Assert.False(Def.AllowsShield);
        Assert.Equal(ClassRegistry.Mystic.MaxItemWeight, Def.MaxItemWeight);
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
        Assert.NotEqual(ClassRegistry.Mystic.ColorRgb, Def.ColorRgb); // darker than the Mystic's bright violet
    }

    [Fact]
    public void Combat_BaseAttack_FlowsThroughSharedFormula()
    {
        var hero = MakeHero(level: 2);
        Assert.Equal(Def.BaseAttack, CombatMath.RoleBaseAttack(Def));
        Assert.Equal(Def.BaseAttack + 2 * 2, CombatMath.HeroAttack(hero, Def, Catalog()));
    }

    [Fact]
    public void Shopping_RejectsShield_GatesOnWeight_BuysWithinCap()
    {
        var hero = MakeHero();

        // No shield: role mismatch, prose names the class.
        var shield = MakeItem(2, ItemSlot.Shield, attack: 0, defense: 5, weight: 2, name: "Kite Shield");
        var shieldVerdict = ShoppingAi.EvaluateItem(hero, Def, shield, price: 5, Catalog(shield));
        Assert.Equal(PassReasonKind.RoleMismatch, shieldVerdict.PassReason);
        Assert.Contains("shields don't suit a occultist", shieldVerdict.Reason);

        // Over the (Mystic-tight) weight cap: TooHeavy, prose names the cap.
        var greatsword = MakeItem(3, ItemSlot.Weapon, attack: 9, defense: 0, weight: 10, name: "Greatsword");
        var heavyVerdict = ShoppingAi.EvaluateItem(hero, Def, greatsword, price: 5, Catalog(greatsword));
        Assert.Equal(PassReasonKind.TooHeavy, heavyVerdict.PassReason);
        Assert.Contains("too heavy for a occultist", heavyVerdict.Reason);
        Assert.Contains($"carries at most {Def.MaxItemWeight}", heavyVerdict.Reason);

        // Within cap and a real upgrade: Buy.
        var wand = MakeItem(4, ItemSlot.Weapon, attack: 5, defense: 0, weight: 3, name: "Bone Wand");
        var buyVerdict = ShoppingAi.EvaluateItem(hero, Def, wand, price: 5, Catalog(wand));
        Assert.Equal(ShoppingVerdictKind.Buy, buyVerdict.Kind);
    }
}
