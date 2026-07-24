using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Covers R8/AE4: every pass carries a typed, human-readable reason; role-fit,
/// affordability, and gear-score rules are all individually provable.
/// </summary>
public class ShoppingAiTests
{
    private static Hero MakeHero(string classId, int gold = 100, GearSet? gear = null) => new(
        new HeroId(1), "Testa", classId, Level: 1, MaxHp: 25, Gold: gold,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Test Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    [Fact]
    public void AE4_Mystic_PassesOnHeavyTwoHandedSword_ReasonNamesRoleAndWeight()
    {
        // Rogue-analog per AE4: the heavy two-hander fails the mystic's weight limit.
        var mystic = MakeHero("mystic", gold: 100);
        var greatsword = MakeItem(10, ItemSlot.Weapon, attack: 9, defense: 0,
            weight: ShoppingAi.MysticMaxWeight + 4, name: "Iron Greatsword");

        var verdict = ShoppingAi.EvaluateItem(mystic, greatsword, price: 20, Catalog(greatsword));

        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
        Assert.Equal(PassReasonKind.TooHeavy, verdict.PassReason);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
        Assert.Contains("mystic", verdict.Reason);
        Assert.Contains("heavy", verdict.Reason);
    }

    [Fact]
    public void Striker_PassesOnShield_ReasonNamesStriker()
    {
        var striker = MakeHero("striker", gold: 100);
        var shield = MakeItem(11, ItemSlot.Shield, attack: 0, defense: 5, weight: 3, name: "Oak Shield");

        var verdict = ShoppingAi.EvaluateItem(striker, shield, price: 10, Catalog(shield));

        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
        Assert.Equal(PassReasonKind.RoleMismatch, verdict.PassReason);
        Assert.Contains("striker", verdict.Reason);
    }

    [Fact]
    public void Mystic_PassesOnShield_ReasonNamesMystic()
    {
        var mystic = MakeHero("mystic", gold: 100);
        var shield = MakeItem(12, ItemSlot.Shield, attack: 0, defense: 5, weight: 2, name: "Oak Shield");

        var verdict = ShoppingAi.EvaluateItem(mystic, shield, price: 10, Catalog(shield));

        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
        Assert.Equal(PassReasonKind.RoleMismatch, verdict.PassReason);
        Assert.Contains("mystic", verdict.Reason);
    }

    [Fact]
    public void OverBudget_PassesWithAffordabilityReason_NamingBothAmounts()
    {
        var hero = MakeHero("vanguard", gold: 30);
        var sword = MakeItem(13, ItemSlot.Weapon, attack: 5, defense: 0, weight: 3, name: "Iron Sword");

        var verdict = ShoppingAi.EvaluateItem(hero, sword, price: 45, Catalog(sword));

        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
        Assert.Equal(PassReasonKind.CannotAfford, verdict.PassReason);
        Assert.Contains("45g", verdict.Reason);
        Assert.Contains("30g", verdict.Reason);
    }

    [Fact]
    public void NoGearScoreImprovement_Passes_ReasonNamesCurrentItem()
    {
        var current = MakeItem(20, ItemSlot.Weapon, attack: 8, defense: 0, weight: 3, name: "Steel Blade");
        var candidate = MakeItem(21, ItemSlot.Weapon, attack: 4, defense: 0, weight: 3, name: "Rusty Blade");
        var hero = MakeHero("vanguard", gold: 100,
            gear: GearSet.Empty.WithSlot(ItemSlot.Weapon, current.Id));

        var verdict = ShoppingAi.EvaluateItem(hero, candidate, price: 5, Catalog(current, candidate));

        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
        Assert.Equal(PassReasonKind.NotAnUpgrade, verdict.PassReason);
        Assert.Contains("Steel Blade", verdict.Reason);
        Assert.Contains("better", verdict.Reason);
    }

    [Fact]
    public void AffordableUpgrade_ReturnsBuy_WithPositiveGain()
    {
        var hero = MakeHero("vanguard", gold: 50);
        var sword = MakeItem(22, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");

        var verdict = ShoppingAi.EvaluateItem(hero, sword, price: 25, Catalog(sword));

        Assert.Equal(ShoppingVerdictKind.Buy, verdict.Kind);
        Assert.Equal(6, verdict.GearScoreGain);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Fact]
    public void Veteran_PassesOnPoorItem_ReasonNamesFloorAndGrade()
    {
        // U9: a hero who has reached the veteran floor threshold refuses Poor-grade gear outright,
        // regardless of price or gear-score gain — the reason names both the hero's depth and the
        // item's grade so the player sees exactly why the sale didn't land.
        var veteran = MakeHero("vanguard", gold: 100) with { DeepestFloorReached = ShoppingAi.VeteranFloorThreshold };
        var poorSword = MakeItem(40, ItemSlot.Weapon, attack: 9, defense: 0, weight: 3, name: "Dull Blade")
            with { Quality = QualityGrade.Poor };

        var verdict = ShoppingAi.EvaluateItem(veteran, poorSword, price: 5, Catalog(poorSword));

        Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
        Assert.Equal(PassReasonKind.QualityTooLow, verdict.PassReason);
        Assert.Contains("veteran", verdict.Reason);
        Assert.Contains("poor", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Veteran_AcceptsFineOrBetter_ReturnsBuy()
    {
        // U9: Fine is the veteran's floor of acceptance — the same item at Fine (or better) is a
        // normal Buy, proving the gate is quality-specific, not a blanket veteran refusal.
        var veteran = MakeHero("vanguard", gold: 100) with { DeepestFloorReached = ShoppingAi.VeteranFloorThreshold };
        var fineSword = MakeItem(41, ItemSlot.Weapon, attack: 9, defense: 0, weight: 3, name: "Fine Blade")
            with { Quality = QualityGrade.Fine };

        var verdict = ShoppingAi.EvaluateItem(veteran, fineSword, price: 5, Catalog(fineSword));

        Assert.Equal(ShoppingVerdictKind.Buy, verdict.Kind);
    }

    [Fact]
    public void Rookie_BelowVeteranThreshold_NeverGatedOnQuality_EvenOnPoorGear()
    {
        // KD3 no-softlock guard (U9): a rookie's DeepestFloorReached is below the veteran
        // threshold, so a Poor item that is a genuine upgrade still buys — a fresh game's only
        // stock (often Poor/Common auto-craft) can never quality-softlock a new hero.
        var rookie = MakeHero("vanguard", gold: 100);
        Assert.True(rookie.DeepestFloorReached < ShoppingAi.VeteranFloorThreshold);
        var poorSword = MakeItem(42, ItemSlot.Weapon, attack: 9, defense: 0, weight: 3, name: "Dull Blade")
            with { Quality = QualityGrade.Poor };

        var verdict = ShoppingAi.EvaluateItem(rookie, poorSword, price: 5, Catalog(poorSword));

        Assert.Equal(ShoppingVerdictKind.Buy, verdict.Kind);
    }

    [Fact]
    public void EveryPassPath_CarriesANonEmptyReason()
    {
        // U5 verification clause: every rejection path proven to carry a reason string.
        var catalogItems = new[]
        {
            MakeItem(30, ItemSlot.Shield, 0, 5, 2, "Shield"),
            MakeItem(31, ItemSlot.Weapon, 9, 0, ShoppingAi.MysticMaxWeight + 1, "Greatsword"),
            MakeItem(32, ItemSlot.Weapon, 1, 0, 1, "Twig"),
        };
        var catalog = Catalog(catalogItems);
        var poorMystic = MakeHero("mystic", gold: 0);

        foreach (var item in catalogItems)
        {
            var verdict = ShoppingAi.EvaluateItem(poorMystic, item, price: 10, catalog);
            Assert.Equal(ShoppingVerdictKind.Pass, verdict.Kind);
            Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
        }
    }
}
