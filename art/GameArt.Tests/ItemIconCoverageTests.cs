using GameArt;
using GameSim.Professions;

namespace GameArt.Tests;

/// <summary>
/// The tripwire that closes the hole this file was born from: six forward-ladder recipes
/// (<c>gloomsteel-blade</c> … <c>emberglass-draught</c>) shipped craftable with no icon and rendered as a
/// generic slot glyph plus the recipe name, for as long as it took a human to notice. Nothing was broken,
/// nothing warned, and no test could tell — the art lane and the recipe table had no assertion connecting
/// them.
///
/// <para>These two facts now hold in BOTH directions. Add a recipe without an <see cref="AssetSpec"/> and
/// the art suite goes red before the recipe can ship; delete or rename a recipe and its orphaned icon spec
/// goes red too, so <c>art/build/</c> and <c>godot/assets/art/</c> cannot quietly accumulate icons for
/// items that no longer exist. This is a SPEC-level pin, not a pixel-level one: it proves an icon was
/// designed for every recipe, not that its PNG is committed — that half is
/// <c>ArtRenderFreshCheckoutTests</c> and the manifest guard's job.</para>
/// </summary>
public class ItemIconCoverageTests
{
    private const string IconPrefix = "item-";

    public static TheoryData<string> AllRecipeIds()
    {
        var data = new TheoryData<string>();
        foreach (var recipeId in ProfessionRegistry.AllRecipes.Keys)
        {
            data.Add(recipeId);
        }

        return data;
    }

    public static TheoryData<string> AllItemIconIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in AssetRegistry.All.Keys.Where(k => k.StartsWith(IconPrefix, StringComparison.Ordinal)))
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllRecipeIds))]
    public void EveryCraftableRecipe_HasAnIconSpec(string recipeId)
    {
        var iconId = IconPrefix + recipeId;

        Assert.True(
            AssetRegistry.IsRegistered(iconId),
            $"recipe '{recipeId}' is craftable but has no icon spec '{iconId}' — it will render as a "
                + "generic slot glyph. Add an AssetSpec to a module under art/specs/items/.");
    }

    [Theory]
    [MemberData(nameof(AllItemIconIds))]
    public void EveryItemIconSpec_NamesARealRecipe(string iconId)
    {
        var recipeId = iconId[IconPrefix.Length..];

        Assert.True(
            ProfessionRegistry.AllRecipes.ContainsKey(recipeId),
            $"icon spec '{iconId}' names recipe '{recipeId}', which no registered profession crafts — "
                + "the recipe was renamed or deleted and the icon is now an orphan.");
    }

    [Fact]
    public void FixtureAssumption_BothSidesAreNonEmpty()
    {
        // Guards the vacuous-green failure mode: if either collection silently emptied (a broken
        // reflection discovery, a profession registry that failed to compose), both Theories above
        // would pass by having nothing to assert.
        Assert.NotEmpty(ProfessionRegistry.AllRecipes);
        var iconIds = AssetRegistry.All.Keys.Count(k => k.StartsWith(IconPrefix, StringComparison.Ordinal));
        Assert.True(iconIds > 0, "no item icon specs discovered — the reflection registry found no items module");
    }

    [Theory]
    [MemberData(nameof(AllItemIconIds))]
    public void EveryItemIcon_IsAFlatMenuIcon_NotAWorldSprite(string iconId)
    {
        // The three items modules each state this convention in prose; this is the one place it is
        // enforced. Icons render flat in menus, never under Light2D, so a normal map would be dead
        // weight in LFS and a lie in the manifest.
        var spec = AssetRegistry.All[iconId];

        Assert.Equal(AssetKind.Item, spec.Kind);
        Assert.Equal(ArtTrack.Active, spec.Track);
        Assert.False(spec.NormalMap, $"{iconId}: item icons are flat menu art — NormalMap must stay false");
    }
}
