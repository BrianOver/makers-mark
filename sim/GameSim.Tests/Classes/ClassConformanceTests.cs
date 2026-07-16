using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using Xunit;

namespace GameSim.Tests.Classes;

/// <summary>
/// The add-on conformance harness (P3, mirrors <c>ProfessionConformanceTests</c>): every class in
/// <see cref="ClassRegistry.All"/> is validated structurally, so an add-on Claude's definition of
/// done is mechanical — register the class and make THIS suite green. New classes get covered
/// automatically; no edits needed here. The final <see cref="AddOnClass_FlowsThroughGeneralizedSystems_WithoutJoiningRecruitPool"/>
/// fact is the extensibility proof (mirrors P1's two-profession test that shipped with only
/// blacksmith live): a test-only fourth class flows through the generalized hero systems without
/// ever being registered or added to the recruit pool.
/// </summary>
public class ClassConformanceTests
{
    public static TheoryData<string> AllClassIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in ClassRegistry.All.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllClassIds))]
    public void Identity_IdMatchesKey_AndDisplayNamePresent(string id)
    {
        var def = ClassRegistry.All[id];
        Assert.Equal(id, def.Id);
        Assert.False(string.IsNullOrWhiteSpace(def.DisplayName));
    }

    [Theory]
    [MemberData(nameof(AllClassIds))]
    public void Stats_AreSaneIntegers(string id)
    {
        var def = ClassRegistry.All[id];
        Assert.InRange(def.BaseHp, 1, 1000);
        Assert.InRange(def.BaseAttack, 0, 1000);
        if (def.MaxItemWeight is { } cap)
        {
            Assert.True(cap >= 1, $"{id}: MaxItemWeight must be >= 1 when set");
        }
    }

    [Theory]
    [MemberData(nameof(AllClassIds))]
    public void ColorRgb_ChannelsInByteRange(string id)
    {
        var (r, g, b) = ClassRegistry.All[id].ColorRgb;
        Assert.InRange(r, 0, 255);
        Assert.InRange(g, 0, 255);
        Assert.InRange(b, 0, 255);
    }

    [Fact]
    public void RecruitPool_IsNonEmpty_AndSubsetOfAll()
    {
        Assert.NotEmpty(ClassRegistry.RecruitPool);
        foreach (var id in ClassRegistry.RecruitPool)
        {
            Assert.True(ClassRegistry.IsRegistered(id), $"recruit-pool id '{id}' is not a registered class");
        }
    }

    [Fact]
    public void RecruitPool_IsTheThreeBuiltIns_InDrawOrder()
    {
        // The recruit determinism contract: order maps 0→vanguard, 1→striker, 2→mystic so the
        // draw is byte-identical to the old (role)rng.NextInt(0, 3). A guard against reordering.
        Assert.Equal(
            new[] { ClassRegistry.VanguardId, ClassRegistry.StrikerId, ClassRegistry.MysticId },
            ClassRegistry.RecruitPool);
    }

    // ---- Extensibility proof (no live fourth class in this core) --------------

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Test Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static Hero MakeHero(string classId, int level = 1, GearSet? gear = null) => new(
        new HeroId(1), "Ward", classId, level, MaxHp: 40, Gold: 100,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void AddOnClass_FlowsThroughGeneralizedSystems_WithoutJoiningRecruitPool()
    {
        // A test-only fourth class that combines traits NO built-in has: it bears a shield
        // (like a vanguard) AND caps item weight (like a mystic). If the generalized systems
        // read the definition — not a hardcoded role — every rule below holds from data alone.
        var warden = new ClassDefinition(
            Id: "warden",
            DisplayName: "Warden",
            BaseHp: 33,
            BaseAttack: 8,
            IsAnchor: true,
            AllowsShield: true,
            MaxItemWeight: 3,
            ColorRgb: (10, 200, 120));

        // Defined and used, but NEVER registered or recruitable — the add-on shape.
        Assert.False(ClassRegistry.IsRegistered(warden.Id));
        Assert.DoesNotContain(warden.Id, ClassRegistry.RecruitPool);

        var hero = MakeHero(warden.Id, level: 2);
        Assert.Equal(warden.Id, hero.ClassId); // a Hero carries an arbitrary class id string

        // CombatMath: the class's BaseAttack is the base of the attack formula.
        Assert.Equal(8, CombatMath.RoleBaseAttack(warden));
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 5, defense: 0, weight: 3, name: "Blade");
        var items = Catalog(sword);
        Assert.Equal(8 + 2 * 2, CombatMath.HeroAttack(hero, warden, Catalog())); // base + level*2, no gear

        // ShoppingAi shield gate reads AllowsShield: a shield-bearing class does NOT reject a shield.
        var shield = MakeItem(2, ItemSlot.Shield, attack: 0, defense: 5, weight: 2, name: "Kite Shield");
        var shieldVerdict = ShoppingAi.EvaluateItem(hero, warden, shield, price: 5, Catalog(shield));
        Assert.NotEqual(PassReasonKind.RoleMismatch, shieldVerdict.PassReason);

        // ShoppingAi weight gate reads MaxItemWeight — and the prose generalizes to the class name.
        var greatsword = MakeItem(3, ItemSlot.Weapon, attack: 9, defense: 0, weight: 10, name: "Greatsword");
        var heavyVerdict = ShoppingAi.EvaluateItem(hero, warden, greatsword, price: 5, Catalog(greatsword));
        Assert.Equal(PassReasonKind.TooHeavy, heavyVerdict.PassReason);
        Assert.Contains("too heavy for a warden", heavyVerdict.Reason);
        Assert.Contains("carries at most 3", heavyVerdict.Reason);

        // A within-cap weapon upgrade is a Buy — the gates do not over-block.
        var buyVerdict = ShoppingAi.EvaluateItem(hero, warden, sword, price: 5, items);
        Assert.Equal(ShoppingVerdictKind.Buy, buyVerdict.Kind);
    }
}
