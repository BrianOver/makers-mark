#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U1: <see cref="Town2D"/>'s build/contract smoke tests — the 2.5D twin of
/// <c>Town3DSceneTests</c>. Property-only assertions everywhere (no frame pump): a 2D
/// <see cref="SubViewport"/> render doesn't trip the 3D-headless-render-hang KTD the pivot plan
/// calls out, but there is still no reason to pump frames for state <see cref="Town2D.Build"/>
/// already sets synchronously.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Town2DSceneTests
{
    private static Town2D Mount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 42));
        return town;
    }

    [TestCase]
    public void Town2D_Built_HasForgeBuilding()
    {
        var town = Mount();
        try
        {
            AssertThat(town.FindBuilding("forge"))
                .OverrideFailureMessage("Build() must place a 'forge' venue — TownLayout2D.Venues regressed")
                .IsNotNull();
        }
        finally { town.Free(); }
    }

    /// <summary>
    /// Every venue's sprite id must resolve to committed art, never to the flat-colour placeholder.
    ///
    /// <para>This exists because of a real, embarrassing miss: the <c>town2d-*</c> pixel buildings
    /// were committed and imported, and the town went on drawing the older SDXL-era set for weeks
    /// because <see cref="TownLayout2D.Venues"/> still named the bare ids. Nothing failed —
    /// <c>TownAssets2D.ForVenue</c> is deliberately null-tolerant, so a wrong id silently degrades
    /// to a coloured box, and a coloured box in a stylised town does not announce itself. The owner
    /// found it by noticing the Forge roof was magenta.</para>
    ///
    /// <para><b>UPDATE (2026-08-01 building-exterior receipt): the family pin flipped BACK.</b>
    /// #316 (the fix above) pinned every id to the <c>town2d-</c> family. But the owner's playtest
    /// verdict on that pixel set was "these look WORSE, we only asked for interior changes" — he
    /// prefers the SDXL look and never asked for an exterior swap, so <see
    /// cref="TownLayout2D.Venues"/> reverted to the bare SDXL ids (with the one genuine defect,
    /// the Forge's magenta roof, fixed in place — see <c>art/pipeline/recolor-forge-roof.py</c>).
    /// This is exactly the "test pins a taste decision, taste changed, test must change with it"
    /// case the U3 unit anticipated: the family assertion below now pins the OPPOSITE direction
    /// (bare ids, NOT <c>town2d-</c>) so an accidental drift back to the pixel set — the same
    /// silent-degrade shape, just reversed — still fails loudly instead of shipping unnoticed.
    /// The resolves-to-real-art assertion is unchanged: it never encoded a taste, only "the id
    /// isn't a typo/missing manifest entry," which holds regardless of which family is active.</para>
    /// </summary>
    [TestCase]
    public void EveryVenueSpriteId_IsInTheSdxlSet_AndResolvesToCommittedArt()
    {
        foreach (var venue in TownLayout2D.Venues)
        {
            AssertThat(venue.SpriteId.StartsWith("town2d-"))
                .OverrideFailureMessage(
                    $"venue '{venue.Key}' draws sprite '{venue.SpriteId}', which IS in the "
                    + "town2d-* pixel set — but the 2026-08-01 receipt reverted every venue back to "
                    + "the SDXL set at the owner's request (see TownLayout2D.Venues). Either this id "
                    + "regressed back to the pixel family by accident, or the owner deliberately "
                    + "chose to switch (options A/C) and this test's pin needs to flip again.")
                .IsFalse();

            AssertThat(IconRegistry.Art(venue.SpriteId))
                .OverrideFailureMessage(
                    $"venue '{venue.Key}' asks for sprite '{venue.SpriteId}' and got no art, so it "
                    + "draws a flat placeholder box with nothing warning about it — either the id is "
                    + "wrong or the PNG/manifest entry is missing")
                .IsNotNull();
        }
    }

    [TestCase]
    public void Town2D_Built_GroundHasPaintedCells()
    {
        var town = Mount();
        try
        {
            AssertThat(town.Ground.GetUsedCells().Count)
                .OverrideFailureMessage("Ground TileMapLayer must have painted cells — BuildGround regressed")
                .IsGreater(0);
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_Built_HeroActorCount_MatchesAliveHeroesInState()
    {
        var town = Mount();
        try
        {
            var expected = town.Adapter!.CurrentState.Heroes.Values.Count(h => h.Alive);

            AssertThat(town.HeroActorCount())
                .OverrideFailureMessage("HeroActorCount must mirror the adapter's alive-hero count — ReconcileHeroes regressed")
                .IsEqual(expected);
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_ForgeRaisePick_FiresBuildingClickedWithForgeKey()
    {
        var town = Mount();
        try
        {
            string? raised = null;
            town.BuildingClicked += key => raised = key;

            town.FindBuilding("forge").RaisePick();

            AssertThat(raised)
                .OverrideFailureMessage("Building2D.Picked('forge') must re-emit as Town2D.BuildingClicked('forge')")
                .IsEqual("forge");
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_Built_PlazaTileIsCobbled()
    {
        var town = Mount();
        try
        {
            // Plaza center (see TownLayout2D.PathRects' plaza rect) must be cobble, not grass — the
            // cozy-village cluster reads as a paved square, not a gap in a grass field. The rich
            // pixel-art ground atlas (town2d-ground-atlas, always imported in the engine-test env)
            // places cobble at atlas coord (3,0); the code-built flat fallback used (1,0).
            var cell = town.Ground.GetCellAtlasCoords(new Vector2I(20, 15));

            AssertThat(cell)
                .OverrideFailureMessage("Plaza tile (20,15) must be cobbled — TownLayout2D.PathRects or the ground atlas regressed")
                .IsEqual(new Vector2I(3, 0));
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Town2D_Built_PlacesEveryConfiguredProp()
    {
        var town = Mount();
        try
        {
            var propNodes = town.YSort.GetChildren()
                .Count(child => child.Name.ToString().StartsWith("Prop_"));

            AssertThat(propNodes)
                .OverrideFailureMessage("Every TownLayout2D.Props entry must mount a Prop_* node under YSort — BuildProps regressed")
                .IsEqual(TownLayout2D.Props.Length);
        }
        finally { town.Free(); }
    }

    /// <summary>U4 (asset-completion wave): the eight warm-hub town props (docs/design/ASSETS.md
    /// — "11.87MB of finished, committed, resolution-tested art... never mounted"). <see
    /// cref="ArtWiringCoverageTests.TownProps_ResolveWithNormal"/> already proves every one of
    /// these ids resolves through <see cref="TownAssets2D.ForProp"/> — that test alone would have
    /// passed for MONTHS while nothing on screen ever drew them (the exact MineWatch-shaped bug
    /// this unit's own packet warns against: "an id resolves" and "something draws it" had
    /// silently drifted apart). Kept as its own array (not a shared reference to <see
    /// cref="ArtWiringCoverageTests.TownPropIds"/>, a different test class/assembly-visibility
    /// concern) — same eight literals, same repo convention as that class's own copy.</summary>
    private static readonly string[] WarmHubPropIds =
    [
        "props-noticeboard", "props-town-well", "props-ore-cart", "props-string-lanterns",
        "props-market-crates", "props-laundry-line", "props-tavern-cat", "props-forge-salamander",
    ];

    [TestCase]
    public void WarmHubProps_ResolveToRealArt_AndAppearInTheTownNodeTree()
    {
        var town = Mount();
        try
        {
            foreach (var id in WarmHubPropIds)
            {
                AssertThat(IconRegistry.Art(id))
                    .OverrideFailureMessage($"'{id}' must resolve to committed art, not fall through to the loud magenta placeholder")
                    .IsNotNull();

                var entry = TownLayout2D.Props.FirstOrDefault(p => p.SpriteId == id);
                AssertThat(entry.SpriteId)
                    .OverrideFailureMessage($"TownLayout2D.Props has no entry for '{id}' — resolving through ForProp is not the same as being drawn (see MineWatch's own precedent, where ids resolved fine but nothing on screen ever used them)")
                    .IsEqual(id);

                var nodeName = $"Prop_{entry.SpriteId}_{entry.Tile.X}_{entry.Tile.Y}";
                AssertThat(town.YSort.GetChildren().Any(child => child.Name.ToString() == nodeName))
                    .OverrideFailureMessage($"expected a '{nodeName}' node under Town2D.YSort — BuildProps must mount every TownLayout2D.Props entry, not just resolve its id")
                    .IsTrue();
            }
        }
        finally { town.Free(); }
    }

    /// <summary>This repo has died of cumulative orphan-node leaks before (see
    /// <c>PanelRebuildDoesNotLeakNodesTests</c>'s own doc: ~468,000 stranded nodes across one
    /// suite, eventually crashing the shared gdUnit runtime mid-session). <see cref="Town2D.Free"/>
    /// is a real <see cref="Node.Free"/> (not a <see cref="Node.QueueFree"/> of an already-detached
    /// subtree — the specific shape that bug needed), so a mount/teardown cycle SHOULD cascade to
    /// zero every time; this pins that invariant now that <see cref="TownLayout2D.Props"/> mounts
    /// 8 more children per cycle than it used to. Same measurement idiom as
    /// <c>PanelRebuildDoesNotLeakNodesTests.OrphanNodeCount</c> — the engine's own live-node-with-
    /// no-tree counter, not a node count that can't distinguish "in the tree" from "leaked".</summary>
    private const int MountTeardownCycles = 10;

    /// <summary>Ceiling on nodes still orphaned after <see cref="MountTeardownCycles"/> full
    /// town mount/teardown cycles. The fixed shape lands at zero; a per-cycle leak on a town this
    /// size (buildings, ~28 props, interior rooms, townsfolk, hero actors) would clear this budget
    /// almost immediately, the same way <c>PanelRebuildDoesNotLeakNodesTests</c>' 200-node budget
    /// stayed tiny relative to the ~9,000-per-cycle bug it was written to catch.</summary>
    private const int OrphanLeakBudget = 100;

    [TestCase]
    public void Town2D_RepeatedMountTeardown_LeavesNoOrphanNodes()
    {
        var before = OrphanNodeCount();

        for (var i = 0; i < MountTeardownCycles; i++)
        {
            var town = Mount();
            town.Free();
        }

        var leaked = OrphanNodeCount() - before;

        AssertThat(leaked)
            .OverrideFailureMessage(
                $"{MountTeardownCycles} town mount/teardown cycles left {leaked} nodes still alive "
                + $"and parented to nothing (budget {OrphanLeakBudget}). Town2D.Free() should cascade "
                + "synchronously to every child it owns, including every TownLayout2D.Props entry's "
                + "Sprite2D/SwayingTreeSprite2D — a regression here is exactly the shape that killed "
                + "the shared gdUnit runtime mid-session before (see PanelRebuildDoesNotLeakNodesTests).")
            .IsLess(OrphanLeakBudget);
    }

    /// <summary>Live nodes belonging to no tree — the engine's own counter, mirrors
    /// <c>PanelRebuildDoesNotLeakNodesTests.OrphanNodeCount</c> exactly (that test lives in a
    /// different class; this is a deliberate small duplication, not a shared helper, matching this
    /// repo's own "no cross-class reach-in" convention documented on <see cref="Building2D.Tell"/>
    /// et al.).</summary>
    private static int OrphanNodeCount() => (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

    /// <summary>Lane clearance is a requirement of this unit, not a nicety: solid objects blocking
    /// the walkable approach into a building is a known live complaint in this town. Checked
    /// against <see cref="TownLayout2D.PathRects"/>'s SPUR/road entries only (index 1 onward) —
    /// index 0 (the plaza square) is deliberately excluded, matching this file's own established
    /// precedent that the wide-open plaza already legitimately hosts decor (the well sits dead
    /// center of it, corner lanterns flank it): a decoration on an 11×5 open square doesn't block
    /// anything, but the SAME decoration on a 1-2-tile spur is the only way into a door.</summary>
    [TestCase]
    public void WarmHubProps_NeverSitOnABuildingApproachLane()
    {
        var lanes = TownLayout2D.PathRects.Skip(1).ToArray();

        foreach (var id in WarmHubPropIds)
        {
            var entry = TownLayout2D.Props.First(p => p.SpriteId == id);

            foreach (var lane in lanes)
            {
                AssertThat(lane.HasPoint(entry.Tile))
                    .OverrideFailureMessage($"'{id}' sits at tile {entry.Tile}, inside approach lane {lane} — it blocks a building's only way in")
                    .IsFalse();
            }
        }
    }

    /// <summary>The task packet's first placement problem: <c>props-noticeboard</c> collides in
    /// NAME with <see cref="TownLayout2D.Venues"/>'s "noticeboard" venue key — the Bounties
    /// building. They must never share a tile, or the prop reads as a second copy of the same
    /// building rather than the distinct market flyer board it was mounted as (see
    /// <see cref="TownLayout2D.Props"/>'s own U4 doc note).</summary>
    [TestCase]
    public void PropsNoticeboard_SitsAtADifferentTileThanTheNoticeboardBuilding()
    {
        var noticeboardBuilding = TownLayout2D.Venues.First(v => v.Key == "noticeboard");
        var noticeboardProp = TownLayout2D.Props.First(p => p.SpriteId == "props-noticeboard");

        AssertThat(noticeboardProp.Tile)
            .OverrideFailureMessage($"props-noticeboard must not sit on the noticeboard/Bounties BUILDING's own tile {noticeboardBuilding.Tile} — they are two different objects")
            .IsNotEqual(noticeboardBuilding.Tile);
    }
}
#endif
