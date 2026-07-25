using GameSim.Heroes;

namespace GameSim.Tests.Heroes;

/// <summary>Phase B (B1c): the pure XP formula and rank ladder, independent of the reveal system's
/// wiring (covered end-to-end in <c>ExpeditionRevealSystemTests</c>).</summary>
public class HeroXpTests
{
    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(1, 0, 15)]
    [InlineData(3, 0, 25)]
    [InlineData(0, 2, 40)]
    [InlineData(2, 3, 65)]
    public void ForExpedition_IsSurviveBaseline_PlusDepth_PlusBeats(int deepestFloorCleared, int creditedBeats, int expected)
    {
        Assert.Equal(expected, HeroXp.ForExpedition(deepestFloorCleared, creditedBeats));
    }

    [Fact]
    public void ForExpedition_NeverGoesNegative_OnDefensiveNegativeInputs()
    {
        Assert.Equal(HeroXp.SurviveXp, HeroXp.ForExpedition(deepestFloorCleared: -5, creditedBeats: -2));
    }

    [Theory]
    [InlineData(0, "Novice")]
    [InlineData(49, "Novice")]
    [InlineData(50, "Delver")]
    [InlineData(149, "Delver")]
    [InlineData(150, "Journeyman")]
    [InlineData(300, "Veteran")]
    [InlineData(500, "Champion")]
    [InlineData(800, "Legend")]
    [InlineData(100_000, "Legend")]
    public void RankFor_ReturnsTheHighestThresholdNotExceeded(int xp, string expectedRank)
    {
        Assert.Equal(expectedRank, HeroRank.For(xp));
    }

    [Fact]
    public void Ladder_IsStrictlyAscendingByThreshold()
    {
        // A hand-authored constant — pin the invariant HeroRank.For relies on (a simple
        // ascending scan) rather than the specific thresholds, so the ladder can grow safely.
        for (var i = 1; i < HeroRank.Ladder.Length; i++)
        {
            Assert.True(
                HeroRank.Ladder[i].Threshold > HeroRank.Ladder[i - 1].Threshold,
                $"ladder entry {i} ({HeroRank.Ladder[i].Name}) must exceed entry {i - 1} ({HeroRank.Ladder[i - 1].Name})");
        }
    }
}
