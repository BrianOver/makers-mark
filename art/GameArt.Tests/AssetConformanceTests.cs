using System.Collections.Immutable;
using GameArt;

namespace GameArt.Tests;

/// <summary>
/// The add-on conformance harness for the art lane (mirrors <c>FactionConformanceTests</c>): every spec
/// in <see cref="AssetRegistry.All"/> is validated structurally through <see cref="AssetSpecRules"/>, so
/// an add-on module's definition of done is mechanical — add the module file under <c>art/specs/</c> and
/// make THIS suite green. New modules are discovered automatically (reflection); no edits needed here.
///
/// The extensibility proof (mirrors the faction/venue/class pattern) is a test-only <see cref="AssetSpec"/>
/// that flows through the SAME <see cref="AssetSpecRules.Validate"/> path yet never joins the registry.
/// </summary>
public class AssetConformanceTests
{
    public static TheoryData<string> AllAssetIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in AssetRegistry.All.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Fact]
    public void Registry_IsNonEmpty_AndDiscoversModules()
    {
        // The reflection registry found the town module's specs (the dogfood). If this fails, the
        // glob-include or the IAssetModule discovery is broken.
        Assert.NotEmpty(AssetRegistry.All);
        Assert.NotEmpty(AssetRegistry.DiscoverModules());
    }

    [Theory]
    [MemberData(nameof(AllAssetIds))]
    public void Identity_IdMatchesKey_AndModulePresent(string id)
    {
        var spec = AssetRegistry.All[id];
        Assert.Equal(id, spec.Id);
        Assert.False(string.IsNullOrWhiteSpace(spec.Module));
    }

    [Theory]
    [MemberData(nameof(AllAssetIds))]
    public void Spec_PassesStructuralRules(string id)
    {
        var spec = AssetRegistry.All[id];
        var errors = AssetSpecRules.Validate(spec);
        Assert.True(errors.Count == 0, $"{id}: {string.Join("; ", errors)}");
    }

    [Theory]
    [MemberData(nameof(AllAssetIds))]
    public void Spec_ComposesNonEmptyPromptContainingSubject(string id)
    {
        var spec = AssetRegistry.All[id];
        var prompt = ArtTrackProfiles.ComposePrompt(spec);
        var negative = ArtTrackProfiles.ComposeNegative(spec);

        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.False(string.IsNullOrWhiteSpace(negative));
        // The subject's leading word survives into the composed prompt (master prefix + subject).
        var firstWord = spec.Subject.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.Contains(firstWord, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AllIds_AreUnique()
    {
        // The registry throws on a duplicate at build; this pins the invariant explicitly too.
        var ids = AssetRegistry.All.Values.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Registry_TryGet_IsRegistered_Require_Behave()
    {
        var first = AssetRegistry.All.Keys.First();

        Assert.True(AssetRegistry.TryGet(first, out var found));
        Assert.NotNull(found);
        Assert.Equal(first, found!.Id);
        Assert.True(AssetRegistry.IsRegistered(first));
        Assert.Same(AssetRegistry.All[first], AssetRegistry.Require(first));

        Assert.False(AssetRegistry.TryGet("no-such-asset", out var missing));
        Assert.Null(missing);
        Assert.False(AssetRegistry.IsRegistered("no-such-asset"));
        Assert.Throws<KeyNotFoundException>(() => AssetRegistry.Require("no-such-asset"));
    }

    [Fact]
    public void Validator_RejectsMalformedSpecs()
    {
        // The validator must actually bite: a bad id, blank subject, and an out-of-range step count.
        var bad = new AssetSpec(
            Id: "Bad_Id",                    // uppercase + underscore: illegal grammar
            Module: "test",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "   ",                  // blank
            Steps: 999);                     // outside the Active range

        var errors = AssetSpecRules.Validate(bad);
        Assert.Contains(errors, e => e.Contains("Id", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Subject", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Steps", StringComparison.Ordinal));
    }

    // ---- Extensibility proof (no live add-on module in this core) ------------------------------

    /// <summary>
    /// A test-only spec with a shape the town set does not use (painterly portrait, a class-figure hint,
    /// a legal size/step override). It validates through the SAME <see cref="AssetSpecRules.Validate"/>
    /// the registered specs do, yet is never registered — the add-on shape.
    /// </summary>
    private static AssetSpec GuildPortrait() => new(
        Id: "guild-master-portrait",
        Module: "guild-pack",
        Track: ArtTrack.Painterly,
        Kind: AssetKind.Portrait,
        Subject: "a stern guildmaster portrait, half-length, candlelit",
        Steps: 40,
        CfgMilli: 7000);

    [Fact]
    public void AddOnSpec_ValidatesThroughSameRules_WithoutJoiningRegistry()
    {
        var spec = GuildPortrait();

        // Defined and validated, but NEVER registered — a test-assembly IAssetModule is not reflected
        // (only the GameArt assembly is), and this bare spec isn't in a module at all.
        Assert.Empty(AssetSpecRules.Validate(spec));
        Assert.False(AssetRegistry.IsRegistered(spec.Id));
        Assert.DoesNotContain(spec.Id, AssetRegistry.All.Keys);

        // It composes a real prompt on its (painterly) track through the same code path.
        Assert.Contains("guildmaster", ArtTrackProfiles.ComposePrompt(spec), StringComparison.Ordinal);
    }
}
