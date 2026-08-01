using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Materials;

namespace GameSim.Tests.Materials;

/// <summary>
/// M1 acceptance: the material registry is the single source of truth for price + grade, and the two
/// surfaces that used to hold that data independently (<see cref="OrePricing"/>,
/// <see cref="RecipeTable.MaterialGrades"/>) now derive from it WITHOUT moving a single band (R4:
/// lookup-only, draw-neutral). The heart of this suite is the byte-identical proof — the registry's
/// five Mine values equal the exact values the old hand-written switches held.
///
/// The registry conformance suite proper (uniqueness / positive-price / id-grammar sweeps) and the
/// addon-guide "Adding a material" note are deliberately out of M1 scope — they ride a later PR.
/// </summary>
public class MaterialRegistryTests
{
    /// <summary>The exact prior values from the hand-written switches, restated here as the frozen
    /// oracle: <c>OrePricing.UnitPrice</c> (copper 3 … adamant 18) and
    /// <c>RecipeTable.MaterialGrades</c> (copper 1 … adamant 5).</summary>
    public static readonly TheoryData<string, int, int> MineOracle = new()
    {
        { "copper", 3, 1 },
        { "iron", 5, 2 },
        { "steel", 8, 3 },
        { "mithril", 12, 4 },
        { "adamant", 18, 5 },
    };

    [Theory]
    [MemberData(nameof(MineOracle))]
    public void Registry_HoldsTheExactPriorMineValues(string key, int expectedPrice, int expectedGrade)
    {
        // Byte-identical proof: the registry seed equals the old OrePricing / MaterialGrades values.
        Assert.Equal(expectedPrice, MaterialRegistry.UnitPrice(key));
        Assert.Equal(expectedGrade, MaterialRegistry.Grade(key));
    }

    [Theory]
    [MemberData(nameof(MineOracle))]
    public void OrePricingAndMaterialGrades_DelegateToTheRegistry_ForEveryPricedMaterial(
        string key, int expectedPrice, int expectedGrade)
    {
        // Delegation, not duplication: both derived surfaces return exactly the registry value, which
        // is exactly the prior value — so no consumer of either surface observes any change.
        Assert.Equal(expectedPrice, OrePricing.UnitPrice(key));
        Assert.Equal(MaterialRegistry.UnitPrice(key), OrePricing.UnitPrice(key));

        Assert.Equal(expectedGrade, RecipeTable.MaterialGrades[key]);
        Assert.Equal(MaterialRegistry.Grade(key), RecipeTable.MaterialGrades[key]);
    }

    [Fact]
    public void PricedPool_IsEveryLiveVenueOreLadder_AndMaterialGradesMirrorsIt()
    {
        // A venue's ores join the pool in the SAME re-baseline that makes the venue live — a
        // returning hero's loot must be priceable at the Evening reveal (OrePricing throws
        // otherwise). T1 content flip (relands PR #242): Sunken Crypt's and Emberfall's ladders
        // join the Mine's and the Gloomwood's.
        Assert.Equal(
            new[]
            {
                "copper", "iron", "steel", "mithril", "adamant",
                "greenheart", "amberpitch", "moonresin", "heartwood",
                "verdigris", "saltglass", "bonechalk", "drowned-silver", "abyss-pearl",
                "firebrick", "slagiron", "quench-salt", "emberglass", "heartcoal",
            },
            MaterialRegistry.PricedPool);

        // RecipeTable.MaterialGrades is byte-identical to the pool: same count, same keys.
        Assert.Equal(19, RecipeTable.MaterialGrades.Count);
        Assert.Equal(MaterialRegistry.PricedPool.OrderBy(k => k, StringComparer.Ordinal), RecipeTable.MaterialGrades.Keys);
    }

    [Theory]
    [InlineData("greenheart", 36, 8)]
    [InlineData("amberpitch", 42, 9)]
    [InlineData("moonresin", 48, 10)]
    [InlineData("heartwood", 54, 11)]
    public void GloomwoodOres_ArePriced_AndGraded(string key, int expectedPrice, int expectedGrade)
    {
        Assert.True(MaterialRegistry.IsPriced(key));
        Assert.Equal(expectedPrice, MaterialRegistry.UnitPrice(key));
        Assert.Equal(expectedGrade, MaterialRegistry.Grade(key));
        Assert.Equal(expectedPrice, OrePricing.UnitPrice(key)); // no throw — the reveal path is safe
        Assert.Equal(expectedGrade, RecipeTable.MaterialGrades[key]);
    }

    [Fact]
    public void UnknownKey_StillThrows_ExactlyAsBefore()
    {
        // OrePricing throws ArgumentOutOfRangeException for a genuinely unknown key (unchanged type).
        Assert.Throws<ArgumentOutOfRangeException>(() => OrePricing.UnitPrice("no-such-ore"));

        // The crafting grade gate still rejects it — MaterialGrades has no such key.
        Assert.False(RecipeTable.MaterialGrades.ContainsKey("no-such-ore"));

        // Registry lookups: unregistered id is a loud defect.
        Assert.False(MaterialRegistry.IsRegistered("no-such-ore"));
        Assert.Throws<KeyNotFoundException>(() => MaterialRegistry.Require("no-such-ore"));
        Assert.False(MaterialRegistry.TryGet("no-such-ore", out var missing));
        Assert.Null(missing);
    }

    [Theory]
    [InlineData("electrum", 24, 6)]
    [InlineData("orichalcum", 30, 7)]
    public void RegalMaterials_AreRegistered_ButInert_SoTheyCannotMoveBands(string key, int price, int grade)
    {
        // Registered in the source of truth with their chosen price/grade …
        Assert.True(MaterialRegistry.IsRegistered(key));
        Assert.Equal(price, MaterialRegistry.UnitPrice(key));
        Assert.Equal(grade, MaterialRegistry.Grade(key));

        // … but NOT in the frozen priced pool, so neither live surface exposes them: OrePricing throws
        // and the crafting grade gate rejects them, exactly as before M1. No live path reaches them
        // (no venue mints them, the Crownsguard faction is unregistered), so they are provably
        // draw-neutral (R4).
        Assert.False(MaterialRegistry.IsPriced(key));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrePricing.UnitPrice(key));
        Assert.False(RecipeTable.MaterialGrades.ContainsKey(key));
    }
}
