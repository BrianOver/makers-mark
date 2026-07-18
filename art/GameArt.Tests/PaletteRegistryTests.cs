using GameArt;
using Xunit;

namespace GameArt.Tests;

/// <summary>Palette-family conformance (variety-tone §2).</summary>
public class PaletteRegistryTests
{
    [Fact]
    public void AllFamilies_HaveUniqueIds_AndNonBlankClauses()
    {
        Assert.Equal(5, PaletteRegistry.All.Count);
        foreach (var (id, def) in PaletteRegistry.All)
        {
            Assert.Equal(id, def.Id);
            Assert.False(string.IsNullOrWhiteSpace(def.Clause), $"{id}: blank clause");
        }
    }

    [Fact]
    public void House_IsPresent_AndCarriesTheAnchorPalette()
    {
        Assert.True(PaletteRegistry.IsRegistered("house"));
        Assert.Contains("void-purple", PaletteRegistry.Require("house").Clause);
    }

    [Fact]
    public void ComposePrompt_SplicesTheSpecsFamilyClause()
    {
        var spec = new AssetSpec(
            Id: "test-hearth-thing", Module: "test", Track: ArtTrack.Active,
            Kind: AssetKind.Building, Subject: "a warm test cottage") with
        { PaletteId = "hearth" };
        var prompt = ArtTrackProfiles.ComposePrompt(spec);
        Assert.Contains("honey-amber", prompt);
        Assert.DoesNotContain("void-purple", prompt);
        Assert.Contains("a warm test cottage", prompt);
    }

    [Fact]
    public void ComposePrompt_UnknownFamily_Throws()
    {
        var spec = new AssetSpec(
            Id: "test-bad-palette", Module: "test", Track: ArtTrack.Active,
            Kind: AssetKind.Building, Subject: "x") with
        { PaletteId = "no-such-family" };
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => ArtTrackProfiles.ComposePrompt(spec));
    }
}
