#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// ANTI-SCATTER GUARD (docs/design/build-provenance-and-never-lost.md). Every generated GLB under
/// <c>res://assets/models/gen/</c> must be EITHER wired into the running game (placed by
/// <c>BuildingKit</c>/<c>Town3D</c>, loadable via <see cref="TownAssets.InstantiateGen"/>) or
/// explicitly logged as PENDING with a reason — never a silent orphan on disk.
///
/// <para>This is the check that would have caught the 11 gen assets that shipped to disk but were
/// never placed in the town (the same class of "built but not in the build" failure as the whole
/// 3D town living off-trunk). It is FILESYSTEM-DRIVEN, not a hand-list of "what we remembered to
/// wire": a new gen GLB that nobody wires FAILS this test until it is wired here or listed pending.
/// Closing out a pending asset = wire it, move its name from <see cref="Pending"/> to
/// <see cref="Wired"/>.</para>
///
/// <para>Property-only by design — no frame pump, no viewport render (3D-render-hang rule); nodes
/// are created orphaned and freed.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GenAssetCoverageTests
{
    /// <summary>Gen GLBs actually PLACED in the running town.</summary>
    private static readonly string[] Wired =
    {
        // BuildingKit venue bodies (gen-first, BuildingKit.cs)
        "forge.glb", "market.glb", "tavern.glb", "minegate.glb", "noticeboard.glb",
        // Town3D.BuildProps decoration
        "well.glb", "barrel.glb", "ore-cart.glb", "market-stall.glb", "bounty-board.glb",
        // Town3D forge-station anvil
        "anvil.glb",
        // MonsterView3D spectate stage (MineWatch milestone flash, AssetCatalog.MonsterModelFile)
        "monster-cave-rat.glb", "monster-spider.glb", "monster-ghoul.glb", "monster-ore-golem.glb",
        // InteriorRoom3D furniture (InteriorRoom3D.Rooms per-venue prop lists)
        "weapon-rack.glb", "crate.glb", "cauldron.glb", "table.glb", "chest.glb",
        "brazier.glb", "bookshelf.glb", "stool.glb", // batch 2 interior furniture
        // Town3D.BuildProps outdoor dressing (batch 2)
        "signpost.glb", "haybale.glb",
        // Town3D.BuildProps outdoor variety (batch 3)
        "statue.glb", "lamp-post.glb", "tree-stump.glb", "trough.glb",
        // Furniture wave — interior (InteriorRoom3D.Rooms) + outdoor (Town3D.BuildProps)
        "wall-sconce.glb", "potion-shelf.glb", "apple-barrel.glb", "chair.glb", "bed.glb", "wall-banner.glb",
        "standing-lantern.glb", "grain-sack.glb", "shop-sign.glb", "bucket.glb",
        // Overnight staged batch — outdoor props (Town3D.BuildProps)
        "scarecrow.glb", "flower-planter.glb",
    };

    /// <summary>Gen GLBs finished but not yet placeable — each needs a surface that does not exist
    /// yet. KEEP THE REASON; remove an entry only by wiring the asset (then add it to
    /// <see cref="Wired"/>).</summary>
    private static readonly Dictionary<string, string> Pending = new()
    {
        // Venue (non-Mine) monster meshes. The only 3D monster spectate surface today is
        // MineWatch's milestone flash, whose MonsterRoster is Mine-only (cave-rat..forgeworm) and
        // whose events (FloorRecordSet/AttributionBeatEvent) carry no venue/kind. These three are
        // finished + normalized (on disk); they light up the instant a venue-aware spectate passes
        // the real MonsterKind to
        // MonsterView3D.ShowMonster (kind slug already resolves via AssetCatalog once mapped).
        ["monster-bramble-boar.glb"] = "Gloomwood F1 monster — no venue-aware 3D spectate surface yet (MineWatch is Mine-only).",
        ["monster-lantern-moth.glb"] = "Gloomwood F2 monster — no venue-aware 3D spectate surface yet (MineWatch is Mine-only).",
        ["monster-crypt-crab.glb"] = "Sunken Crypt F1 monster — no venue-aware 3D spectate surface yet (MineWatch is Mine-only).",
    };

    private static List<string> GenGlbsOnDisk()
    {
        var dir = DirAccess.Open(TownAssets.GenModels);
        AssertThat(dir).IsNotNull();
        var names = new List<string>();
        foreach (var file in dir!.GetFiles())
        {
            if (file.EndsWith(".glb")) // ignore the .import / _N.png sidecars
            {
                names.Add(file);
            }
        }

        return names;
    }

    [TestCase]
    public void EveryGenGlb_IsWiredOrPending_NoSilentOrphans()
    {
        var onDisk = GenGlbsOnDisk();
        var declared = new HashSet<string>(Wired);
        declared.UnionWith(Pending.Keys);

        // (1) No silent orphan: every GLB on disk is accounted for.
        foreach (var file in onDisk)
        {
            AssertBool(declared.Contains(file))
                .OverrideFailureMessage(
                    $"Gen asset '{file}' is on disk but neither WIRED nor PENDING. Wire it into the " +
                    "town (Town3D/BuildingKit) or add it to Pending with a reason — anti-scatter guard.")
                .IsTrue();
        }

        // (2) No stale declaration: every wired/pending name still exists on disk.
        var disk = new HashSet<string>(onDisk);
        foreach (var name in declared)
        {
            AssertBool(disk.Contains(name))
                .OverrideFailureMessage($"'{name}' is declared (wired/pending) but not on disk — remove the stale entry.")
                .IsTrue();
        }
    }

    [TestCase]
    public void EveryWiredGlb_InstantiatesAsRealMesh()
    {
        foreach (var file in Wired)
        {
            var node = TownAssets.InstantiateGen(file);
            try
            {
                AssertThat(node)
                    .OverrideFailureMessage($"wired gen asset '{file}' failed to instantiate")
                    .IsNotNull();
            }
            finally
            {
                node?.Free();
            }
        }
    }
}
#endif
