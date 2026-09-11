using System.Collections.Immutable;
using System.Linq;
using GameSim.Materials;

namespace GameSim.Tests.Materials;

/// <summary>
/// P2-HONEST-05: the guard that makes <c>MaterialDefinition.DisplayName</c> mean something, mirroring
/// <c>ClassConformanceTests.Identity_IdMatchesKey_AndDisplayNamePresent</c>'s shape (P3) one field
/// further. Phrased against the PROPERTY, not the instance — <see cref="AllMaterialIds"/> walks
/// <see cref="MaterialRegistry.All"/> itself, so a future material earns coverage by being registered,
/// never by someone remembering to add a sixth (now twenty-second) row to a hand list. That is the
/// exact shape this repo has paid for missing four times already (see
/// <c>hand-listed-fixtures-go-green</c>): a literal id array in a test stops covering the family the
/// moment a new entry lands.
///
/// <para><b>What "not merely its Id" means here.</b> A blank <see cref="MaterialDefinition.DisplayName"/>
/// can't compile (the record's constructor requires a value), so the realistic miss isn't an empty
/// string — it's copy-pasting <see cref="MaterialDefinition.Id"/> itself into the field, or leaving its
/// kebab-case dashes in place. <see cref="IsWellFormedDisplayName"/> therefore rejects THREE shapes:
/// blank/whitespace, an exact match to <see cref="MaterialDefinition.Id"/>, and any residual
/// <c>-</c>/<c>_</c> separator (a real display spelling is always plain words and spaces — "Drowned
/// Silver", never "drowned-silver" or "Drowned-Silver"). Dash-to-space-plus-capitalize IS ruled
/// SUFFICIENT distinctness (not merely stripped) — the point is a presentational spelling to render,
/// not a wholly invented alternate name, and every multi-word entry in the registry today
/// (<c>quench-salt</c> → "Quench Salt", <c>drowned-silver</c> → "Drowned Silver") is exactly that
/// transform plus Title Case.</para>
/// </summary>
public class MaterialDisplayNameTests
{
    public static TheoryData<string> AllMaterialIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in MaterialRegistry.All.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    /// <summary>The one predicate both tests below share, so the live-registry sweep and the
    /// reflexive failure-shape proof can never quietly drift into checking two different things.</summary>
    private static bool IsWellFormedDisplayName(string id, string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name != id
        && !name.Contains('-')
        && !name.Contains('_')
        && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => char.IsUpper(word[0]));

    [Theory]
    [MemberData(nameof(AllMaterialIds))]
    public void DisplayName_IsPresent_AndDistinctFromTheRawId(string id)
    {
        var def = MaterialRegistry.All[id];
        Assert.Equal(id, def.Id);
        Assert.True(
            IsWellFormedDisplayName(id, def.DisplayName),
            $"{id}: DisplayName '{def.DisplayName}' is blank, equals the raw id, keeps a kebab " +
            "separator, or isn't Title Case");
    }

    /// <summary>The reflexive half (P2-HONEST-05's own brief: "a test that would go red if a NEW
    /// material shipped without one"). A hand-authored <see cref="MaterialDefinition"/> that copies its
    /// raw <see cref="MaterialDefinition.Id"/> into <see cref="MaterialDefinition.DisplayName"/> — the
    /// exact lazy-placeholder shape the constructor's required parameter cannot itself catch, since a
    /// STRING is still a string whether it is a real name or the id again — fails the SAME predicate
    /// <see cref="DisplayName_IsPresent_AndDistinctFromTheRawId"/> runs over the live registry, proving
    /// the guard actually fires rather than vacuously passing today's twenty-one well-formed entries.</summary>
    [Fact]
    public void ForgottenDisplayName_OnANewMaterial_WouldFailTheGuard()
    {
        var placeholder = new MaterialDefinition(
            Id: "star-iron",
            DisplayName: "star-iron", // the miss: raw id copied straight in, never Title Cased
            UnitPrice: 90,
            Grade: 17,
            Tags: ImmutableArray<string>.Empty,
            SourceVenue: "");

        Assert.False(IsWellFormedDisplayName(placeholder.Id, placeholder.DisplayName));

        // A second miss (dashes left in place rather than the id copied outright) fails the same way.
        var halfFixed = placeholder with { DisplayName = "star-Iron" };
        Assert.False(IsWellFormedDisplayName(halfFixed.Id, halfFixed.DisplayName));

        // The actual fix passes — proving the predicate isn't just permanently false.
        var fixedDef = placeholder with { DisplayName = "Star Iron" };
        Assert.True(IsWellFormedDisplayName(fixedDef.Id, fixedDef.DisplayName));
    }
}
