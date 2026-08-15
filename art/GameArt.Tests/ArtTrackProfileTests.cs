using GameArt;
using Xunit;

namespace GameArt.Tests;

/// <summary>
/// The prompt composer's kind clause (§11.10 U1, KTD-A).
///
/// <para><b>Why this exists.</b> <see cref="ArtTrackProfiles.Active"/>'s master prompt was authored
/// for BUILDINGS — it says "one structure centered" and negates "multiple buildings" — and every
/// one of the 48 committed item icons inherits it verbatim. Measured 2026-08-14: an unattended
/// batch returned a cake stand and a lidded urn for a buckler and a full armoured figure for a
/// hauberk, because "structure" is an architecture word and SDXL reads it as one. Per-item Subject
/// strings ("a small round copper buckler, domed central boss, riveted rim") are specific and
/// good; the master prefix was fighting them.</para>
///
/// <para><b>The half of this file that matters most is the byte-identity pin.</b> The obvious fix —
/// editing the shared master prompt — would silently change the composed prompt of every building,
/// backdrop, venue and monster spec already committed, so the next regeneration of any of them
/// would drift for reasons nobody could trace to a commit. The clause is therefore spliced per
/// <see cref="AssetKind"/>, the same mechanism <see cref="ArtTrackProfiles.ComposePrompt"/> already
/// uses for the palette family, and every non-item kind must compose exactly what it composed
/// before this unit existed.</para>
/// </summary>
public class ArtTrackProfileTests
{
    private static AssetSpec SpecOfKind(AssetKind kind) => new(
        Id: $"test-{kind.ToString().ToLowerInvariant()}",
        Module: "tests",
        Track: ArtTrack.Active,
        Kind: kind,
        Subject: "a subject");

    // ---- the item clause -------------------------------------------------------------------

    [Fact]
    public void ItemPrompt_CarriesTheItemClause_AndDropsTheArchitectureWording()
    {
        var prompt = ArtTrackProfiles.ComposePrompt(SpecOfKind(AssetKind.Item));

        Assert.Contains(ArtTrackProfiles.ItemClause, prompt, System.StringComparison.Ordinal);
        Assert.DoesNotContain("one structure centered", prompt, System.StringComparison.Ordinal);
        Assert.DoesNotContain("multiple buildings", ArtTrackProfiles.ComposeNegative(SpecOfKind(AssetKind.Item)),
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void ItemNegative_ExcludesTheFurnitureTheBatchActuallyReturned()
    {
        var negative = ArtTrackProfiles.ComposeNegative(SpecOfKind(AssetKind.Item));

        // Not a generic list: each of these is a shape a real 2026-08-14 candidate came back as.
        foreach (var banned in new[] { "furniture", "table", "vase", "urn", "bowl", "candlestick", "pedestal" })
        {
            Assert.Contains(banned, negative, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ItemSubject_SurvivesTheSplice()
    {
        var spec = SpecOfKind(AssetKind.Item) with { Subject = "a small round copper buckler" };
        Assert.Contains("a small round copper buckler", ArtTrackProfiles.ComposePrompt(spec),
            System.StringComparison.Ordinal);
    }

    // ---- the byte-identity pin -------------------------------------------------------------

    /// <summary>
    /// Every kind that is NOT an item composes exactly the string it composed before the kind
    /// clause existed: master prompt, palette clause, subject — nothing inserted, nothing moved.
    /// Asserted by reconstructing that string from the profile itself rather than pasting a
    /// literal, so the pin survives a legitimate future edit to the master prompt while still
    /// failing the moment a kind clause leaks into a kind that should not have one.
    /// </summary>
    [Theory]
    [InlineData(AssetKind.Building)]
    [InlineData(AssetKind.Prop)]
    [InlineData(AssetKind.Sprite)]
    [InlineData(AssetKind.ClassFigure)]
    [InlineData(AssetKind.Portrait)]
    [InlineData(AssetKind.Monster)]
    [InlineData(AssetKind.Backdrop)]
    public void EveryNonItemKind_ComposesTheUnchangedPreClauseString(AssetKind kind)
    {
        var spec = SpecOfKind(kind);
        var profile = ArtTrackProfiles.For(ArtTrack.Active);
        var palette = PaletteRegistry.Require(spec.PaletteId).Clause;

        var expected = $"{profile.MasterPrompt}, {palette}, {spec.Subject}";

        Assert.Equal(expected, ArtTrackProfiles.ComposePrompt(spec));
        Assert.Equal(profile.MasterNegative, ArtTrackProfiles.ComposeNegative(spec));
    }

    /// <summary>Vacuous-green guard: the theory above is only meaningful if it actually covers
    /// every non-item kind the enum defines. A new kind added without a row here would otherwise
    /// slip through untested.</summary>
    [Fact]
    public void TheByteIdentityPin_CoversEveryNonItemKind()
    {
        var pinned = new[]
        {
            AssetKind.Building, AssetKind.Prop, AssetKind.Sprite, AssetKind.ClassFigure,
            AssetKind.Portrait, AssetKind.Monster, AssetKind.Backdrop,
        };

        var all = System.Enum.GetValues<AssetKind>();
        var expected = System.Linq.Enumerable.Where(all, k => k != AssetKind.Item);

        Assert.Equal(
            System.Linq.Enumerable.OrderBy(expected, k => k),
            System.Linq.Enumerable.OrderBy(pinned, k => k));
    }

    // ---- the real committed specs ----------------------------------------------------------

    [Fact]
    public void EveryCommittedItemSpec_GetsTheClause_AndNoOtherSpecDoes()
    {
        var items = 0;
        var others = 0;

        foreach (var spec in AssetRegistry.All.Values)
        {
            var hasClause = ArtTrackProfiles.ComposePrompt(spec)
                .Contains(ArtTrackProfiles.ItemClause, System.StringComparison.Ordinal);

            if (spec.Kind == AssetKind.Item)
            {
                Assert.True(hasClause, $"{spec.Id} is an Item and must carry the item clause");
                items++;
            }
            else
            {
                Assert.False(hasClause, $"{spec.Id} is {spec.Kind} and must NOT carry the item clause");
                others++;
            }
        }

        // Vacuous-green guard on both arms — a registry that failed to load would pass silently.
        Assert.True(items >= 40, $"only {items} item specs seen; the registry did not load");
        Assert.True(others >= 40, $"only {others} non-item specs seen; the registry did not load");
    }
}
