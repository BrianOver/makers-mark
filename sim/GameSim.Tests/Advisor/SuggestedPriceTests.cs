using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;

namespace GameSim.Tests.Advisor;

/// <summary>
/// <see cref="SuggestedPrice"/> — what the game tells a player their craft is worth.
///
/// <para>The bug this pins: the old formula was <c>max(1, (Attack + Defense) * 2)</c>, so anything
/// without combat stats priced at exactly 1 gold. The playtest ticker read "Your Field Poultice sold to
/// Torvald for 1g" next to "Rival's Soldier's Longsword sold for 40g", and because the ADVISOR shared
/// the formula, the game was recommending that price to a real player — below the cost of the material
/// that went into it.</para>
/// </summary>
public class SuggestedPriceTests
{
    private static Item Make(
        QualityGrade quality,
        int attack = 0,
        int defense = 0,
        ConsumableEffect? effect = null) =>
        new(
            new ItemId(1), "recipe", "Thing", ItemSlot.Weapon, quality,
            new ItemStats(attack, defense, Weight: 1), Mark: null,
            ImmutableList<ItemHistoryEntry>.Empty, effect);

    /// <summary>The regression itself: a Consumable has no attack and no defense, and must not be
    /// valued at a token gold piece.</summary>
    [Fact]
    public void AConsumableWithNoCombatStats_IsNotPricedAtOneGold()
    {
        var poultice = Make(QualityGrade.Common, effect: new ConsumableEffect(ConsumableKind.Heal, Magnitude: 12));

        var price = SuggestedPrice.For(poultice);

        Assert.True(price > 1, $"a healing consumable was priced at {price}g");
        Assert.True(price >= 24, $"expected the heal magnitude to drive the price, got {price}g");
    }

    /// <summary>Even a stat-less, effect-less item (a Trinket) clears the quality floor.</summary>
    [Fact]
    public void AnItemWithNothingToMeasure_StillClearsTheQualityFloor()
    {
        Assert.True(SuggestedPrice.For(Make(QualityGrade.Poor)) >= 4);
        Assert.True(SuggestedPrice.For(Make(QualityGrade.Masterwork)) >= 34);
    }

    /// <summary>
    /// Combat gear must price EXACTLY as it did before. This is the safety property that lets the change
    /// land without re-baselining the economy: if a weapon's suggested price moved, every downstream
    /// willingness-to-pay figure would move with it.
    /// </summary>
    [Fact]
    public void CombatGear_PricesIdenticallyToTheOldFormula()
    {
        foreach (var (attack, defense) in new[] { (9, 0), (0, 12), (7, 5), (30, 30) })
        {
            var item = Make(QualityGrade.Common, attack, defense);
            var old = (attack + defense) * 2;

            // Only assert equality where the old formula was already above the floor — below it, the
            // floor is the deliberate change, not a regression.
            if (old >= 8)
            {
                Assert.Equal(old, SuggestedPrice.For(item));
            }
        }
    }

    [Fact]
    public void BetterQuality_IsNeverWorthLess()
    {
        var grades = new[]
        {
            QualityGrade.Poor, QualityGrade.Common, QualityGrade.Fine,
            QualityGrade.Superior, QualityGrade.Masterwork,
        };

        var last = 0;
        foreach (var grade in grades)
        {
            var price = SuggestedPrice.For(Make(grade));
            Assert.True(price >= last, $"{grade} priced {price}g, below the grade beneath it ({last}g)");
            last = price;
        }
    }

    [Fact]
    public void ThePriceIsAlwaysAtLeastOne()
    {
        Assert.True(SuggestedPrice.For(Make(QualityGrade.Poor, attack: 0, defense: 0)) >= 1);
    }
}
