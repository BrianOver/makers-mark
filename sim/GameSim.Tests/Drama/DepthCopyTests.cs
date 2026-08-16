using GameSim.Drama;
using Xunit;

namespace GameSim.Tests.Drama;

/// <summary>
/// #166b: <c>Hero.DeepestFloorReached</c> == 0 means "never delved", a legitimate sim value every
/// hero carries on day 1 — but every surface that rendered it verbatim as "floor 0" fabricated a
/// floor that does not exist, the same defect family #166 fixed for the Evening Ledger. This pins
/// the one shared answer both the sim-side advisor and the Godot client route through.
/// </summary>
public class DepthCopyTests
{
    [Fact]
    public void Deepest_AtZero_ReadsNotYet_NeverFloorZero()
    {
        Assert.Equal("not yet", DepthCopy.Deepest(0));
    }

    [Fact]
    public void Deepest_BelowZero_AlsoReadsNotYet()
    {
        // Defensive: DeepestFloorReached is never negative in practice, but the helper must not
        // fabricate an ordinal for any non-positive input.
        Assert.Equal("not yet", DepthCopy.Deepest(-1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Deepest_AtPositiveFloor_NamesIt(int floor)
    {
        Assert.Equal($"floor {floor}", DepthCopy.Deepest(floor));
    }
}
