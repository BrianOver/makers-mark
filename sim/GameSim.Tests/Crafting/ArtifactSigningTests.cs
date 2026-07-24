using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

/// <summary>Wave 4 (U19, "Signed Works"): the rare, deterministic signing proc.</summary>
public class ArtifactSigningTests
{
    private static Item MasterworkItem(ImmutableList<int>? subScores, string? signedName = null) =>
        ItemForge.Forge(new ItemId(1), RecipeTable.All["longsword"], QualityGrade.Masterwork, day: 5, subScores)
            with
        { SignedName = signedName };

    [Fact]
    public void Qualifies_MasterworkWithAllHighSubScores_True()
    {
        var item = MasterworkItem(ImmutableList.Create(950, 970, 999));

        Assert.True(ArtifactSigning.Qualifies(item));
    }

    [Theory]
    [InlineData(QualityGrade.Superior)]
    [InlineData(QualityGrade.Fine)]
    [InlineData(QualityGrade.Common)]
    [InlineData(QualityGrade.Poor)]
    public void Qualifies_NonMasterwork_NeverQualifies_EvenWithHighSubScores(QualityGrade quality)
    {
        var item = ItemForge.Forge(new ItemId(1), RecipeTable.All["longsword"], quality, day: 5,
            ImmutableList.Create(999, 999, 999));

        Assert.False(ArtifactSigning.Qualifies(item));
    }

    [Fact]
    public void Qualifies_MasterworkWithoutSubScores_NeverQualifies()
    {
        // Auto-craft / passive-profession path: CraftSubScores is always empty (never 3 entries).
        var item = MasterworkItem(subScores: null);

        Assert.False(ArtifactSigning.Qualifies(item));
    }

    [Fact]
    public void Qualifies_MasterworkWithOneLowSubScore_NeverQualifies()
    {
        var item = MasterworkItem(ImmutableList.Create(950, 949, 999)); // one point under the floor

        Assert.False(ArtifactSigning.Qualifies(item));
    }

    [Fact]
    public void Qualifies_AlreadySigned_NeverReQualifies()
    {
        var item = MasterworkItem(ImmutableList.Create(999, 999, 999), signedName: "Emberfall");

        Assert.False(ArtifactSigning.Qualifies(item));
    }

    [Fact]
    public void LegendName_IsDeterministic_SameInputsSameName()
    {
        var a = ArtifactSigning.LegendName(campaignId: 42, new ItemId(7), "longsword", day: 3);
        var b = ArtifactSigning.LegendName(campaignId: 42, new ItemId(7), "longsword", day: 3);

        Assert.Equal(a, b);
    }

    [Fact]
    public void LegendName_DifferentInputs_CanDiffer()
    {
        var names = new HashSet<string>
        {
            ArtifactSigning.LegendName(1, new ItemId(1), "longsword", 1),
            ArtifactSigning.LegendName(1, new ItemId(2), "longsword", 1),
            ArtifactSigning.LegendName(1, new ItemId(3), "longsword", 1),
            ArtifactSigning.LegendName(1, new ItemId(4), "longsword", 1),
            ArtifactSigning.LegendName(1, new ItemId(5), "longsword", 1),
        };

        Assert.True(names.Count > 1, "expected item-id variety to produce more than one legend name");
    }

    [Fact]
    public void LegendName_NeverEmpty()
    {
        var name = ArtifactSigning.LegendName(0, new ItemId(0), string.Empty, 0);

        Assert.False(string.IsNullOrWhiteSpace(name));
    }
}
