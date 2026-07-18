#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>Proves the U3 (P006, R10) by-id seam: <see cref="AssetCatalog"/>'s id composition
/// matches the documented <c>art/specs/&lt;module&gt;</c> conventions, its resolvers delegate to
/// the existing null-tolerant <see cref="IconRegistry"/> loader (committed ids resolve, unknown
/// ones degrade to null with no throw), and <c>Has</c>/<c>HasNormal</c> reflect the generated
/// manifest exactly — theory-over-ids, the same shape as <c>IconRegistryTests</c>.</summary>
[TestSuite]
[RequireGodotRuntime]
public class AssetCatalogTests
{
    [TestCase]
    public void IdComposition_MatchesDocumentedConventions()
    {
        // item-<recipeId> (ItemSpecs.cs / ItemSpecsExtra.cs header).
        AssertThat(AssetCatalog.ItemIconId("longsword")).IsEqual("item-longsword");
        AssertThat(AssetCatalog.ItemIconId("dagger")).IsEqual("item-dagger");

        // hero-<classId> (HeroSpecs.cs header) — ClassRegistry ids are already plain slugs.
        AssertThat(AssetCatalog.HeroPortraitId("vanguard")).IsEqual("hero-vanguard");
        AssertThat(AssetCatalog.HeroPortraitId("striker")).IsEqual("hero-striker");
        AssertThat(AssetCatalog.HeroPortraitId("mystic")).IsEqual("hero-mystic");

        // <venue>-backdrop / <venue>-entrance (GloomwoodSpecs.cs / SunkenCryptSpecs.cs headers).
        AssertThat(AssetCatalog.VenueBackdropId("gloomwood")).IsEqual("gloomwood-backdrop");
        AssertThat(AssetCatalog.VenueEntranceId("gloomwood")).IsEqual("gloomwood-entrance");
        AssertThat(AssetCatalog.VenueBackdropId("sunkencrypt")).IsEqual("sunkencrypt-backdrop");
        AssertThat(AssetCatalog.VenueEntranceId("sunkencrypt")).IsEqual("sunkencrypt-entrance");

        // monster-<slug> for the plain Mine convention (MonsterSpecs.cs header) — a display name
        // ("Cave Rat") and an already-slugged fragment ("cave-rat") compose the same id.
        AssertThat(AssetCatalog.MonsterPortraitId("Cave Rat")).IsEqual("monster-cave-rat");
        AssertThat(AssetCatalog.MonsterPortraitId("cave-rat")).IsEqual("monster-cave-rat");

        // Leading "The " is dropped so "The Forgeworm" matches the committed monster-forgeworm id.
        AssertThat(AssetCatalog.MonsterPortraitId("The Forgeworm")).IsEqual("monster-forgeworm");

        // Venue-prefixed variant (Gloomwood/SunkenCrypt monsters, e.g. GloomwoodSpecs.cs).
        AssertThat(AssetCatalog.MonsterPortraitId("Bramble Boar", "gloomwood")).IsEqual("gloomwood-bramble-boar");
        AssertThat(AssetCatalog.MonsterPortraitId("Crypt Crab", "sunkencrypt")).IsEqual("sunkencrypt-crypt-crab");
    }

    [TestCase]
    public void CommittedIds_ResolveNonNull()
    {
        // The 3 committed hero figures (V3/P006 U1-U2) resolve through HeroPortrait with both
        // diffuse and normal — the same pair IconRegistryTests already proves for town-tavern.
        foreach (var classId in new[] { "vanguard", "striker", "mystic" })
        {
            var lit = AssetCatalog.HeroPortrait(classId);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNotNull();
        }

        // Has(id) is a generic manifest presence check, not tied to a typed resolver — it holds
        // for every committed id, including the town buildings AssetCatalog has no resolver for.
        foreach (var id in new[]
                 {
                     "hero-vanguard", "hero-striker", "hero-mystic",
                     "town-forge", "town-market", "town-mine-gate", "town-tavern",
                 })
        {
            AssertThat(AssetCatalog.Has(id)).IsTrue();
        }
    }

    [TestCase]
    public void UnknownConcept_HasFalseAndNullReturn_NoThrow()
    {
        AssertThat(AssetCatalog.ItemIcon("no-such-recipe")).IsNull();
        AssertThat(AssetCatalog.MonsterPortrait("no-such-monster")).IsNull();
        AssertThat(AssetCatalog.VenueBackdrop("no-such-venue")).IsNull();
        AssertThat(AssetCatalog.VenueEntrance("no-such-venue")).IsNull();
        AssertThat(AssetCatalog.HeroPortrait("no-such-class")).IsNull();

        AssertThat(AssetCatalog.Has(AssetCatalog.ItemIconId("no-such-recipe"))).IsFalse();
        AssertThat(AssetCatalog.Has(AssetCatalog.MonsterPortraitId("no-such-monster"))).IsFalse();
        AssertThat(AssetCatalog.Has(AssetCatalog.VenueBackdropId("no-such-venue"))).IsFalse();
        AssertThat(AssetCatalog.Has(AssetCatalog.VenueEntranceId("no-such-venue"))).IsFalse();
        AssertThat(AssetCatalog.Has(AssetCatalog.HeroPortraitId("no-such-class"))).IsFalse();
    }

    [TestCase]
    public void Has_ReflectsManifestExactly()
    {
        foreach (var id in new[] { "hero-vanguard", "hero-striker", "hero-mystic", "town-tavern" })
        {
            AssertThat(AssetCatalog.Has(id)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(id)).IsTrue(); // all 7 currently-committed ids ship a normal map
        }

        AssertThat(AssetCatalog.Has("does_not_exist_yet")).IsFalse();
        AssertThat(AssetCatalog.HasNormal("does_not_exist_yet")).IsFalse();
    }

    [TestCase]
    public void DiffuseOnlyManifestEntry_ReportsNormalFalse()
    {
        // Synthetic fixture (U3 test scenario): a future flat item icon / backdrop entry has no
        // normal map. IconRegistry.ParseManifest is a pure function (no I/O), so this proves
        // manifest fidelity without touching the real committed art-manifest.json — none of the
        // 7 ids committed so far are diffuse-only.
        const string synthetic = """
        {
          "item-longsword": { "diffuse": true, "normal": false },
          "hero-vanguard": { "diffuse": true, "normal": true }
        }
        """;

        var parsed = IconRegistry.ParseManifest(synthetic);

        AssertThat(parsed.ContainsKey("item-longsword")).IsTrue();
        AssertThat(parsed["item-longsword"].Diffuse).IsTrue();
        AssertThat(parsed["item-longsword"].Normal).IsFalse();

        AssertThat(parsed["hero-vanguard"].Diffuse).IsTrue();
        AssertThat(parsed["hero-vanguard"].Normal).IsTrue();

        AssertThat(parsed.ContainsKey("does-not-exist")).IsFalse();
    }

    [TestCase]
    public void ParseManifest_MalformedOrMissingFlags_DefaultFalse_NoThrow()
    {
        const string synthetic = """{ "some-id": { } }""";
        var parsed = IconRegistry.ParseManifest(synthetic);
        AssertThat(parsed["some-id"].Diffuse).IsFalse();
        AssertThat(parsed["some-id"].Normal).IsFalse();

        AssertThat(IconRegistry.ParseManifest("").Count).IsEqual(0);
    }
}
#endif
