#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U7 (P006) end-to-end wiring proof: the by-id seam (<see cref="AssetCatalog"/> +
/// <c>art-manifest.json</c>, U3/R10) actually resolves the gameplay-critical committed set from a
/// fresh checkout (R8), and <c>Has</c> agrees with the manifest exactly. Data-driven theory over id
/// arrays — mirrors <c>TownSceneTests.LitOverlay_ShippedAssets_MountFourBuildingsThreeHeroesAndWarmLights</c>
/// and <c>IconRegistryTests</c> — so a future generated asset (the deferred long tail: venue floor
/// monsters, props, faction crests) extends coverage by one array entry, not new test code.
///
/// <para>Coverage set (representative, not exhaustive — the full inventory lives in
/// <c>docs/design/art-pipeline-health-2026-07-18.md</c>): one item icon per profession (blacksmith/
/// tanning/engineering/alchemy), all 5 Mine monster portraits, both venues' backdrop+entrance
/// (gloomwood/sunkencrypt), all 3 hero portraits, all 4 town buildings.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ArtWiringCoverageTests
{
    // recipeId (NOT the composed "item-<id>") — one per registered profession.
    private static readonly string[] ItemIconRecipeIds =
    [
        "dagger", // blacksmith (no profession prefix)
        "tanning-hide-jerkin",
        "engineering-utility-multitool",
        "alchemy-healing-draught",
    ];

    // VenueFloor.MonsterKind display names for the Mine's 5 floors (VenueRegistry.BuildMine).
    private static readonly string[] MineMonsterKinds =
    [
        "Cave Rat", "Tunnel Spider", "Deep Ghoul", "Ore Golem", "The Forgeworm",
    ];

    private static readonly string[] VenueIds = ["gloomwood", "sunkencrypt"];

    private static readonly string[] HeroClassIds = ["vanguard", "striker", "mystic"];

    private static readonly string[] TownBuildingIds =
    [
        "town-forge", "town-market", "town-mine-gate", "town-tavern",
    ];

    [TestCase]
    public void ItemIcons_OnePerProfession_ResolveAndHaveNoNormal()
    {
        foreach (var recipeId in ItemIconRecipeIds)
        {
            var id = AssetCatalog.ItemIconId(recipeId);
            AssertThat(AssetCatalog.ItemIcon(recipeId)).IsNotNull();
            AssertThat(AssetCatalog.Has(id)).IsTrue();

            // Edge: item icons are flat menu art (AssetSpec.NormalMap=false) — Lit still degrades
            // to a diffuse-only CanvasTexture (lights read flat), never null/crash.
            AssertThat(AssetCatalog.HasNormal(id)).IsFalse();
            var lit = IconRegistry.Lit(id);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNull();
        }
    }

    [TestCase]
    public void AllFiveMineMonsters_ResolveWithNormal()
    {
        foreach (var kind in MineMonsterKinds)
        {
            var id = AssetCatalog.MonsterPortraitId(kind);
            var lit = AssetCatalog.MonsterPortrait(kind);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNotNull();

            AssertThat(AssetCatalog.Has(id)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(id)).IsTrue();
        }
    }

    [TestCase]
    public void BothVenues_BackdropAndEntrance_ResolveWithCorrectNormalContract()
    {
        foreach (var venueId in VenueIds)
        {
            // Backdrops are flat far planes (NormalMap=false) — diffuse-only, like item icons.
            var backdropId = AssetCatalog.VenueBackdropId(venueId);
            AssertThat(AssetCatalog.VenueBackdrop(venueId)).IsNotNull();
            AssertThat(AssetCatalog.Has(backdropId)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(backdropId)).IsFalse();
            var litBackdrop = IconRegistry.Lit(backdropId);
            AssertThat(litBackdrop).IsNotNull();
            AssertThat(litBackdrop!.NormalTexture).IsNull();

            // Entrances are lit foreground buildings (NormalMap=true) — full diffuse+normal pair.
            var entranceId = AssetCatalog.VenueEntranceId(venueId);
            var litEntrance = AssetCatalog.VenueEntrance(venueId);
            AssertThat(litEntrance).IsNotNull();
            AssertThat(litEntrance!.DiffuseTexture).IsNotNull();
            AssertThat(litEntrance.NormalTexture).IsNotNull();
            AssertThat(AssetCatalog.Has(entranceId)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(entranceId)).IsTrue();
        }
    }

    [TestCase]
    public void AllThreeHeroes_ResolveWithNormal()
    {
        foreach (var classId in HeroClassIds)
        {
            var id = AssetCatalog.HeroPortraitId(classId);
            var lit = AssetCatalog.HeroPortrait(classId);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNotNull();
            AssertThat(AssetCatalog.Has(id)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(id)).IsTrue();
        }
    }

    [TestCase]
    public void AllFourTownBuildings_PresentInManifestWithNormal()
    {
        // AssetCatalog has no typed resolver for town buildings (V4a predates U3) — Has/Lit still
        // cover them, proving the manifest-backed presence check is generic, not resolver-bound.
        foreach (var id in TownBuildingIds)
        {
            AssertThat(AssetCatalog.Has(id)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(id)).IsTrue();
            var lit = IconRegistry.Lit(id);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.NormalTexture).IsNotNull();
        }
    }

    [TestCase]
    public void DeferredLongTailIds_AbsentFromManifest_ResolversNullNoThrow()
    {
        // Real authored-but-NOT-yet-generated specs (deferred long tail: venue props / faction
        // crests — art/specs/props/PropSpecs.cs, art/specs/factions/FactionSpecs.cs) — proves the
        // graceful-degrade contract holds for genuine future work, not just a made-up string.
        const string deferredProp = "props-noticeboard";
        const string deferredFaction = "faction-deepvein-emblem";

        foreach (var id in new[] { deferredProp, deferredFaction })
        {
            AssertThat(AssetCatalog.Has(id)).IsFalse();
            AssertThat(AssetCatalog.HasNormal(id)).IsFalse();
            AssertThat(IconRegistry.Art(id)).IsNull();
            AssertThat(IconRegistry.Lit(id)).IsNull();
        }
    }
}
#endif
