using GameArt;

namespace GameArt.Tests;

/// <summary>
/// The seed derivation is a pure function of the id (reusing <c>StableHash</c>). These pin the
/// properties that make it collision-safe by construction: deterministic, positive, and injective
/// across the registry — so nobody can hand-pick a colliding seed.
/// </summary>
public class AssetSeedTests
{
    [Theory]
    [InlineData("town-forge")]
    [InlineData("town-tavern")]
    [InlineData("guild-master-portrait")]
    public void SeedFor_IsDeterministic_AndPositive(string id)
    {
        var a = AssetSeed.SeedFor(id);
        var b = AssetSeed.SeedFor(id);

        Assert.Equal(a, b);
        Assert.InRange(a, 1u, 0x7FFF_FFFFu);
    }

    [Fact]
    public void SeedFor_DiffersById()
    {
        Assert.NotEqual(AssetSeed.SeedFor("town-forge"), AssetSeed.SeedFor("town-tavern"));
    }

    [Fact]
    public void Seeds_AreCollisionFree_AcrossTheRegistry()
    {
        var seeds = AssetRegistry.All.Keys.Select(AssetSeed.SeedFor).ToList();
        Assert.Equal(seeds.Count, seeds.Distinct().Count());
    }
}
