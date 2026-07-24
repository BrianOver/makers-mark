using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using Xunit;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Wave 5 (U23b): <see cref="ForgePath"/> is the single source of truth for the forging target
/// line — the sim scorer and (later) the Godot overlay both regenerate it and must agree
/// byte-for-byte, so determinism, monotonic x, and bounded y are load-bearing properties, not
/// just nice-to-haves.
/// </summary>
public class ForgePathTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Generate_IsDeterministic_SameInputsSameList(int tier)
    {
        var a = ForgePath.Generate(tier, ItemSlot.Weapon, baseWeight: 5, pathSeed: 42);
        var b = ForgePath.Generate(tier, ItemSlot.Weapon, baseWeight: 5, pathSeed: 42);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Generate_IsStableAcrossManyRepeatedCalls()
    {
        var first = ForgePath.Generate(2, ItemSlot.Armor, baseWeight: 9, pathSeed: 777);
        for (var i = 0; i < 25; i++)
        {
            var again = ForgePath.Generate(2, ItemSlot.Armor, baseWeight: 9, pathSeed: 777);
            Assert.Equal(first, again);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Generate_XIsStrictlyIncreasing_FromZeroToOneThousand(int tier)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var path = ForgePath.Generate(tier, ItemSlot.Shield, baseWeight: 8, pathSeed: seed);
            var vertexCount = path.Count / 2;

            Assert.Equal(0, path[0]);
            Assert.Equal(1000, path[(vertexCount - 1) * 2]);

            for (var i = 1; i < vertexCount; i++)
            {
                var prevX = path[(i - 1) * 2];
                var x = path[i * 2];
                Assert.True(x > prevX, $"x not strictly increasing at vertex {i} for tier {tier} seed {seed}: {prevX} -> {x}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Generate_YIsAlwaysWithinPermilleRange(int tier)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var path = ForgePath.Generate(tier, ItemSlot.Consumable, baseWeight: 3, pathSeed: seed);
            var vertexCount = path.Count / 2;

            for (var i = 0; i < vertexCount; i++)
            {
                var y = path[i * 2 + 1];
                Assert.InRange(y, 0, 1000);
            }
        }
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 7)]
    [InlineData(3, 8)]
    public void Generate_VertexCount_ScalesWithTier(int tier, int expectedVertexCount)
    {
        var path = ForgePath.Generate(tier, ItemSlot.Weapon, baseWeight: 5, pathSeed: 1);
        Assert.Equal(expectedVertexCount * 2, path.Count);
    }

    [Fact]
    public void Generate_HigherTier_DiffersFromLowerTier_SamePathSeed()
    {
        var t1 = ForgePath.Generate(1, ItemSlot.Weapon, baseWeight: 5, pathSeed: 10);
        var t2 = ForgePath.Generate(2, ItemSlot.Weapon, baseWeight: 5, pathSeed: 10);
        var t3 = ForgePath.Generate(3, ItemSlot.Weapon, baseWeight: 5, pathSeed: 10);

        Assert.NotEqual(t1, t2);
        Assert.NotEqual(t2, t3);
        Assert.NotEqual(t1, t3);
    }

    [Fact]
    public void Generate_DifferentPathSeed_ProducesDifferentPath()
    {
        var a = ForgePath.Generate(2, ItemSlot.Armor, baseWeight: 9, pathSeed: 1);
        var b = ForgePath.Generate(2, ItemSlot.Armor, baseWeight: 9, pathSeed: 2);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Generate_TierIsClamped_OutOfRangeNeverThrows()
    {
        var low = ForgePath.Generate(0, ItemSlot.Weapon, baseWeight: 5, pathSeed: 3);
        var clampedLow = ForgePath.Generate(1, ItemSlot.Weapon, baseWeight: 5, pathSeed: 3);
        Assert.Equal(clampedLow, low);

        var high = ForgePath.Generate(99, ItemSlot.Weapon, baseWeight: 5, pathSeed: 3);
        var clampedHigh = ForgePath.Generate(3, ItemSlot.Weapon, baseWeight: 5, pathSeed: 3);
        Assert.Equal(clampedHigh, high);
    }

    [Fact]
    public void HeatAt_AtEachVertex_ReturnsThatVertexsExactY()
    {
        var path = ForgePath.Generate(2, ItemSlot.Weapon, baseWeight: 6, pathSeed: 55);
        var vertexCount = path.Count / 2;

        for (var i = 0; i < vertexCount; i++)
        {
            var x = path[i * 2];
            var y = path[i * 2 + 1];
            Assert.Equal(y, ForgePath.HeatAt(path, x));
        }
    }

    [Fact]
    public void HeatAt_AtMidpoint_LinearlyInterpolates()
    {
        // A synthetic two-vertex path is enough to pin the exact integer interpolation formula.
        var path = ImmutableList.Create(0, 200, 1000, 1200 /* out of normal range but exercises the math */);
        Assert.Equal(200, ForgePath.HeatAt(path, 0));
        Assert.Equal(1200, ForgePath.HeatAt(path, 1000));
        Assert.Equal(700, ForgePath.HeatAt(path, 500)); // 200 + (1200-200)*500/1000 = 700
        Assert.Equal(450, ForgePath.HeatAt(path, 250)); // 200 + 1000*250/1000 = 450
    }

    [Fact]
    public void HeatAt_BeyondDomain_ClampsToEndpoints()
    {
        var path = ForgePath.Generate(1, ItemSlot.Weapon, baseWeight: 5, pathSeed: 8);
        var vertexCount = path.Count / 2;
        var firstY = path[1];
        var lastY = path[(vertexCount - 1) * 2 + 1];

        Assert.Equal(firstY, ForgePath.HeatAt(path, -500));
        Assert.Equal(lastY, ForgePath.HeatAt(path, 5000));
    }

    [Fact]
    public void HeatAt_DefensiveOnMalformedPath_NeverThrows()
    {
        Assert.Equal(500, ForgePath.HeatAt(null!, 500));
        Assert.Equal(500, ForgePath.HeatAt(ImmutableList<int>.Empty, 500));
        Assert.Equal(500, ForgePath.HeatAt(ImmutableList.Create(0, 100), 500)); // single vertex, too short
        Assert.Equal(500, ForgePath.HeatAt(ImmutableList.Create(0, 100, 500), 250)); // odd length
    }
}
