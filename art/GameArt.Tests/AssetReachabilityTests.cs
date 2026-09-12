using System.Reflection;
using System.Text.Json;
using GameArt;
using GameSim.Classes;

namespace GameArt.Tests;

/// <summary>
/// P2-SCREEN-13: the one question none of this project's other censuses ask. <see
/// cref="AssetProvenanceTests"/> asks "does this committed id have a traceable origin"; <see
/// cref="ItemIconCoverageTests"/> and <c>godot/tests/AssetResolutionCensusTests.cs</c> ask "does
/// every id the render path composes actually resolve". None of them ask "does anything in the
/// shipped game ever actually ASK for this id" — which is exactly the gap that let
/// <c>town2d-player.png</c> sit committed and unreferenced for weeks (found by hand,
/// <c>docs/reference/surfaces-census.md</c> §14.1, not by any test).
///
/// <para><b>Deliberately NOT a blanket <c>town2d-</c> prefix exemption.</b> Every neighbouring
/// census in this project (<see cref="AssetProvenanceTests.HandAuthoredPrefix"/>) treats
/// <c>town2d-</c> as one hand-authored track and stops there — correct for "does it have
/// provenance", but a blanket prefix would wave <c>town2d-player</c> straight through here too,
/// since it obviously starts with <c>town2d-</c>. So this test enumerates that family's real
/// sub-shapes (hero bodies, townsfolk bodies, mine monster bodies, and the closed set of
/// hand-placed furniture/signage/shell ids) instead of trusting the prefix.</para>
///
/// <para><b>Ground truth.</b> <see cref="AssetRegistry.All"/> — every <c>AssetSpec</c>-declared id
/// (items/heroes/monsters/venues/factions/props/mine-backdrop/shop-interior), reflection-built, so
/// a new module needs no edit here. Composed families this project can reach directly through the
/// documented one-way <c>GameArt → GameSim</c> reference (<c>GameArt.csproj</c>'s own header) read
/// straight off the real registry (<see cref="ClassRegistry"/>) rather than a hand-copied class
/// list. What remains — ids that predate or sit outside the <c>AssetSpec</c> grammar entirely —
/// is a small, closed, individually-traced set, several shared verbatim with <see
/// cref="AssetProvenanceTests"/>'s own exception lists (duplicated, not shared code, because the
/// two files answer different questions about the same handful of ids).</para>
/// </summary>
public class AssetReachabilityTests
{
    // Same "walk up to Game.sln" seam as AssetProvenanceTests.RepoRoot (itself borrowed from
    // sim/GameSim.Tests/ConstitutionTests.cs) — duplicated rather than shared across test classes.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }

    /// <summary>Every committed asset id, exactly as <c>art/pipeline/gen-manifest.ps1</c> writes
    /// them — already base ids (a normal map is the "normal" bool field, never a second key), but
    /// still carrying <c>-v&lt;n&gt;</c> variant and <c>_step</c>/<c>_walk2</c>/<c>_walk4</c> frame
    /// suffixes as distinct keys, which <see cref="StripSuffixes"/> below removes.</summary>
    private static IReadOnlyList<string> ManifestIds()
    {
        var path = Path.Combine(RepoRoot(), "godot", "assets", "art", "art-manifest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }

    // ---- non-AssetSpec, still-reachable families (every one individually traced) --------------

    /// <summary>town2d-hero-&lt;classId&gt; — <c>TownAssets2D.HeroBodyId</c>
    /// (<c>godot/scripts/town2d/TownAssets2D.cs:160</c>), read off the real registry rather than a
    /// hand-copied class list.</summary>
    private static IEnumerable<string> Town2dHeroBases => ClassRegistry.All.Keys.Select(c => $"town2d-hero-{c}");

    /// <summary>town2d-townsfolk-&lt;civilianId&gt; — <c>TownsfolkNpc2D.CivilianIds</c>
    /// (<c>godot/scripts/town2d/TownsfolkNpc2D.cs:106</c>): "broad"/"slight" only, a fixed
    /// 2-entry array in that file, not a sim registry.</summary>
    private static readonly string[] Town2dTownsfolkBases = ["town2d-townsfolk-broad", "town2d-townsfolk-slight"];

    /// <summary>town2d-monster-&lt;slug&gt; — <c>DelveStage.MonsterBodyId</c>
    /// (<c>godot/scripts/panels/DelveStage.cs:717</c>). Only the Mine's five floors have a
    /// committed town2d pixel body today; Gloomwood/Sunken Crypt/Emberfall monsters resolve
    /// through the venue-prefixed <c>AssetCatalog</c> family instead (already in
    /// <see cref="AssetRegistry"/>), so they need no entry here.</summary>
    private static readonly string[] Town2dMonsterBases =
    [
        "town2d-monster-cave-rat", "town2d-monster-tunnel-spider", "town2d-monster-deep-ghoul",
        "town2d-monster-ore-golem", "town2d-monster-forgeworm",
    ];

    /// <summary>Hand-placed town2d furniture/signage/shell/ground ids — literal strings in
    /// <c>Town2D.cs</c> / <c>TownLayout2D.cs</c> / <c>InteriorLayout2D.cs</c>, none composed from a
    /// registry (a profession's station set is fixed layout data, not sim data).</summary>
    private static readonly string[] Town2dLiteralBases =
    [
        "town2d-ground-atlas", "town2d-well",
        "town2d-forge-interior-shell", "town2d-gatehouse-interior-shell",
        "town2d-market-interior-shell", "town2d-tavern-interior-shell",
        "town2d-prop-crate", "town2d-prop-lantern", "town2d-prop-tree",
        "town2d-sign-alchemy", "town2d-sign-blacksmith", "town2d-sign-engineering", "town2d-sign-tanning",
        "town2d-station-alch-cauldron", "town2d-station-alch-herbs", "town2d-station-alch-rack",
        "town2d-station-alch-shelf", "town2d-station-alch-still",
        "town2d-station-anvil", "town2d-station-bellows", "town2d-station-furnace", "town2d-station-quench",
        "town2d-station-rack", "town2d-station-shelf",
        "town2d-station-eng-bench", "town2d-station-eng-crate", "town2d-station-eng-flywheel",
        "town2d-station-eng-gears",
        "town2d-station-gate-bounty", "town2d-station-gate-muster", "town2d-station-gate-overlook",
        "town2d-station-gate-winch",
        "town2d-station-market-counter", "town2d-station-market-crates", "town2d-station-market-ledger",
        "town2d-station-market-shelf",
        "town2d-station-tan-frame", "town2d-station-tan-hides", "town2d-station-tan-rack",
        "town2d-station-tan-vats",
        "town2d-station-tavern-bar", "town2d-station-tavern-hearth", "town2d-station-tavern-storywall",
        "town2d-station-tavern-table",
    ];

    /// <summary>The player-smith body — no <c>town2d-</c> prefix. <c>TownAssets2D.ForPlayer</c> /
    /// <c>PlayerController2D.PlayerSpriteId</c>.</summary>
    private const string PlayerBase = "player_smith";

    /// <summary>Bare literal ids with no composed family at all: the town's four main buildings
    /// plus the noticeboard (<c>TownLayout2D.Venues</c>' spriteId column — a naming split from
    /// their OWN (unbuilt, describe-only) <c>town-*</c> <c>AssetSpec</c> entries, not a second copy
    /// of the same asset), the four panel banners (<c>UiKit.SceneBanner</c> call sites), the wood
    /// UI frame (<c>GameTheme.WoodFramePath</c>), and the three rival-catalog category icons
    /// (<c>IconRegistry.RivalCategoryArtId</c> — generated by <c>gen-rival-icons.py</c>, predates
    /// the <c>AssetSpec</c> convention). Same ids <see cref="AssetProvenanceTests"/>'s own
    /// <c>DocumentedUnreproducibleIds</c>/<c>HandAuthoredExactIds</c> name, under the provenance
    /// question rather than this reachability one.</summary>
    private static readonly string[] BareLiteralIds =
    [
        "forge", "market", "tavern", "mine-gate", "noticeboard",
        "panel_banner_bounties", "panel_banner_heroes", "panel_banner_shop", "panel_banner_tavern",
        "ui-frame-wood",
        "item-rival-weapon", "item-rival-shield", "item-rival-armor",
    ];

    /// <summary>Committed, but deliberately never drawn by the client: palette-source inputs the
    /// ground/interior generators sample from (<c>art/pipeline/gen-*-interior.py</c> — 7 scripts
    /// read them; <c>grep town2d-tile godot/scripts/</c> is 0 hits), not renderable ids. Deleting
    /// these breaks the GENERATOR, not the game. <c>docs/reference/surfaces-census.md</c> §14.1 is
    /// what first told these apart from <c>town2d-player</c>, which has zero hits either place and
    /// really is dead weight (deleted alongside this test, P2-SCREEN-13).</summary>
    private static readonly string[] PipelineInputOnlyIds = ["town2d-tile-cobble", "town2d-tile-grass", "town2d-tile-path"];

    /// <summary>Strips one trailing animation-frame suffix, then one trailing <c>-v&lt;n&gt;</c>
    /// variant suffix, mirroring <c>ArtVariants</c>'s own id convention (base id first, siblings a
    /// contiguous <c>-v2</c>.. run) — never both at once; no committed id today combines them.</summary>
    private static string StripSuffixes(string id)
    {
        foreach (var frameSuffix in new[] { "_step", "_walk2", "_walk4" })
        {
            if (id.EndsWith(frameSuffix, StringComparison.Ordinal))
            {
                id = id[..^frameSuffix.Length];
                break;
            }
        }

        var variantCut = id.LastIndexOf("-v", StringComparison.Ordinal);
        if (variantCut > 0)
        {
            var digits = id[(variantCut + 2)..];
            if (digits.Length > 0 && digits.All(char.IsDigit))
            {
                id = id[..variantCut];
            }
        }

        return id;
    }

    [Fact]
    public void EveryCommittedAssetId_IsReachableBySomething()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        reachable.UnionWith(AssetRegistry.All.Keys);
        reachable.UnionWith(Town2dHeroBases);
        reachable.UnionWith(Town2dTownsfolkBases);
        reachable.UnionWith(Town2dMonsterBases);
        reachable.UnionWith(Town2dLiteralBases);
        reachable.Add(PlayerBase);
        reachable.UnionWith(BareLiteralIds);
        reachable.UnionWith(PipelineInputOnlyIds);

        var unreachable = ManifestIds()
            .Select(StripSuffixes)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !reachable.Contains(id))
            .ToList();

        Assert.True(unreachable.Count == 0,
            "Committed art id(s) with no known reader anywhere — not an AssetSpec, not a listed "
            + "town2d-* sub-family, not a documented bare-literal id: " + string.Join(", ", unreachable)
            + ". If this is genuinely new, reachable art, add its family to this file (cite the real "
            + "call site). If it is dead weight, delete the PNG + its .import sidecar and its "
            + "art-manifest.json row.");
    }
}
