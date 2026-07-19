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
/// and <c>IconRegistryTests</c> — so a future generated asset extends coverage by one array entry,
/// not new test code (the art wave 2 batch below — Gloomwood/Sunken Crypt monsters+props, town
/// props, faction crests, the new <c>mine-backdrop</c> — is exactly that extension).
///
/// <para>Coverage set (representative, not exhaustive — the full inventory lives in
/// <c>docs/design/art-pipeline-health-2026-07-18.md</c> plus the art wave 2 session): one item
/// icon per profession (blacksmith/tanning/engineering/alchemy), all 5 Mine monster portraits,
/// both venues' backdrop+entrance (gloomwood/sunkencrypt) plus their floor monsters and props, all
/// 8 town props, both faction crests, all 3 hero portraits, all 4 town buildings, and the Mine's
/// own hub-tile backdrop.</para>
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

    // --- art wave 2 (long-tail deferred specs + mine-backdrop) --------------------------------

    // GloomwoodVenue.Build()'s 4 floor kinds (sim/GameSim/Venues/Gloomwood/GloomwoodVenue.cs).
    private static readonly string[] GloomwoodMonsterKinds =
    [
        "Bramble Boar", "Lantern Moth", "The Wicker Shepherd", "Old Mossjaw",
    ];

    // SunkenCryptVenue.Build()'s 5 floor kinds (sim/GameSim/Venues/SunkenCrypt/SunkenCryptVenue.cs).
    private static readonly string[] SunkenCryptMonsterKinds =
    [
        "Crypt Crab", "Bog-Wight", "Choir of Teeth", "Reliquary Mimic", "The Undertow",
    ];

    // Venue props (GloomwoodSpecs.cs + SunkenCryptSpecs.cs) — no typed AssetCatalog resolver (same
    // shape as TownBuildingIds below), all AssetKind.Prop with NormalMap: true.
    private static readonly string[] VenuePropIds =
    [
        "gloomwood-mushroom-cluster", "gloomwood-toll-booth", "sunkencrypt-donation-plate",
    ];

    // Warm-hub town props (art/specs/props/PropsSpecs.cs) — Prop or Sprite kind, all NormalMap: true.
    private static readonly string[] TownPropIds =
    [
        "props-noticeboard", "props-town-well", "props-ore-cart", "props-string-lanterns",
        "props-market-crates", "props-laundry-line", "props-tavern-cat", "props-forge-salamander",
    ];

    // Faction crests (art/specs/factions/FactionSpecs.cs) — AssetKind.Item, flat (NormalMap: false).
    private static readonly string[] FactionCrestIds =
    [
        "faction-deepvein-emblem", "faction-crownsguard-emblem",
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
    public void AllGloomwoodAndSunkenCryptMonsters_ResolveWithNormal()
    {
        foreach (var kind in GloomwoodMonsterKinds)
        {
            AssertMonsterResolvesWithNormal(kind, "gloomwood");
        }

        foreach (var kind in SunkenCryptMonsterKinds)
        {
            AssertMonsterResolvesWithNormal(kind, "sunkencrypt");
        }
    }

    [TestCase]
    public void VenueProps_ResolveWithNormal()
    {
        foreach (var id in VenuePropIds)
        {
            AssertThat(AssetCatalog.Has(id)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(id)).IsTrue();
            var lit = IconRegistry.Lit(id);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNotNull();
        }
    }

    [TestCase]
    public void TownProps_ResolveWithNormal()
    {
        foreach (var id in TownPropIds)
        {
            AssertThat(AssetCatalog.Has(id)).IsTrue();
            AssertThat(AssetCatalog.HasNormal(id)).IsTrue();
            var lit = IconRegistry.Lit(id);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNotNull();
        }
    }

    [TestCase]
    public void FactionCrests_ResolveWithoutNormal()
    {
        foreach (var id in FactionCrestIds)
        {
            AssertThat(AssetCatalog.Has(id)).IsTrue();

            // Faction crests are flat menu-style icons (AssetSpec.NormalMap=false), same
            // diffuse-only contract as item icons.
            AssertThat(AssetCatalog.HasNormal(id)).IsFalse();
            var lit = IconRegistry.Lit(id);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNull();
        }
    }

    [TestCase]
    public void MineBackdrop_ResolvesWithoutNormal()
    {
        // The Mine's own venue-map hub tile (DepthsPanel.BuildMineTile) — flat far plane, same
        // diffuse-only contract as the gloomwood/sunkencrypt backdrops (AssetSpec.NormalMap=false).
        const string mineVenueId = "mine";
        var backdropId = AssetCatalog.VenueBackdropId(mineVenueId);
        AssertThat(backdropId).IsEqual("mine-backdrop");
        AssertThat(AssetCatalog.VenueBackdrop(mineVenueId)).IsNotNull();
        AssertThat(AssetCatalog.Has(backdropId)).IsTrue();
        AssertThat(AssetCatalog.HasNormal(backdropId)).IsFalse();
        var lit = IconRegistry.Lit(backdropId);
        AssertThat(lit).IsNotNull();
        AssertThat(lit!.NormalTexture).IsNull();
    }

    [TestCase]
    public void NeverRegisteredIds_AbsentFromManifest_ResolversNullNoThrow()
    {
        // KTD3 graceful-degrade contract, pinned against synthetic ids that will never be
        // registered (the real long tail this suite once pinned — props-noticeboard,
        // faction-deepvein-emblem — is now fully generated above; art wave 2 closed out every
        // spec the repo had authored, so no genuine "registered but ungenerated" id remains to
        // probe with). AssetCatalogTests.UnknownConcept_HasFalseAndNullReturn_NoThrow covers the
        // same code path via typed resolvers (venue/monster/item/hero ids); this pins it for a
        // bare art id resolved straight through IconRegistry too.
        const string neverRegisteredProp = "props-nonexistent-prop";
        const string neverRegisteredFaction = "faction-nonexistent-emblem";

        foreach (var id in new[] { neverRegisteredProp, neverRegisteredFaction })
        {
            AssertThat(AssetCatalog.Has(id)).IsFalse();
            AssertThat(AssetCatalog.HasNormal(id)).IsFalse();
            AssertThat(IconRegistry.Art(id)).IsNull();
            AssertThat(IconRegistry.Lit(id)).IsNull();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static void AssertMonsterResolvesWithNormal(string kind, string venuePrefix)
    {
        var id = AssetCatalog.MonsterPortraitId(kind, venuePrefix);
        var lit = AssetCatalog.MonsterPortrait(kind, venuePrefix);
        AssertThat(lit).IsNotNull();
        AssertThat(lit!.DiffuseTexture).IsNotNull();
        AssertThat(lit.NormalTexture).IsNotNull();

        AssertThat(AssetCatalog.Has(id)).IsTrue();
        AssertThat(AssetCatalog.HasNormal(id)).IsTrue();
    }
}
#endif
