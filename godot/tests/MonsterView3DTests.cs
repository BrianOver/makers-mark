#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The 3D gen-monster spectate stage: kind→GLB resolution (<see cref="AssetCatalog.MonsterModelFile"/>),
/// the <see cref="MonsterView3D"/> stage itself (real mesh, height-fit, headless render gate,
/// no-model fallback), and <see cref="MineWatch"/>'s milestone-flash wiring (3D-first, 2D
/// silhouette fallback for the one kind with no GLB).
///
/// <para>Property-only by design (3D-render-hang rule): every node is built orphaned, asserted
/// synchronously, and freed in finally — no frame pump, and the viewport under test stays
/// <see cref="SubViewport.UpdateMode.Disabled"/> for the entire headless run (asserted below).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MonsterView3DTests
{
    // ── AssetCatalog.MonsterModelFile: the kind→GLB map ─────────────────────────────────────────

    [TestCase("Cave Rat", "monster-cave-rat.glb")]
    [TestCase("Tunnel Spider", "monster-spider.glb")]
    [TestCase("Deep Ghoul", "monster-ghoul.glb")]
    [TestCase("Ore Golem", "monster-ore-golem.glb")]
    [TestCase("cave-rat", "monster-cave-rat.glb")] // already-slugged input passes through Slugify
    public void MonsterModelFile_MapsMineKindsToGenGlbs(string kind, string expected) =>
        AssertThat(AssetCatalog.MonsterModelFile(kind)).IsEqual(expected);

    [TestCase("The Forgeworm")]
    [TestCase("forgeworm")]
    [TestCase("no-such-monster")]
    public void MonsterModelFile_NoGenModel_ResolvesNull(string kind) =>
        AssertThat(AssetCatalog.MonsterModelFile(kind)).IsNull();

    // ── MonsterView3D: the stage ────────────────────────────────────────────────────────────────

    [TestCase("Cave Rat")]
    [TestCase("Tunnel Spider")]
    [TestCase("Deep Ghoul")]
    [TestCase("Ore Golem")]
    public void ShowMonster_KindWithGlb_InstantiatesRealMesh_HeightFitToFrame(string kind)
    {
        var view = new MonsterView3D();
        try
        {
            view.Build();

            AssertBool(view.ShowMonster(kind))
                .OverrideFailureMessage($"'{kind}' has a gen GLB — ShowMonster must succeed")
                .IsTrue();
            AssertThat(view.HasMonster).IsTrue();
            AssertThat(view.CurrentKind).IsEqual(kind);

            // A REAL 3D mesh, not an empty scene root.
            var monster = view.FindChild("Monster", recursive: true, owned: false);
            AssertThat(monster).IsNotNull();
            AssertBool(HasMeshInstance(monster!))
                .OverrideFailureMessage($"'{kind}' instantiated without any MeshInstance3D descendant")
                .IsTrue();

            // Height-fit contract: rescaled from its OWN AABB to the framed height.
            AssertFloat(view.FittedHeight).IsBetween(
                MonsterView3D.FramedHeight - 0.01f, MonsterView3D.FramedHeight + 0.01f);
        }
        finally
        {
            view.Free();
        }
    }

    [TestCase]
    public void ShowMonster_Forgeworm_NoGlb_ReturnsFalse_StageStaysEmpty()
    {
        var view = new MonsterView3D();
        try
        {
            view.Build();

            AssertThat(view.ShowMonster("The Forgeworm")).IsFalse();
            AssertThat(view.HasMonster).IsFalse();
            AssertThat(view.CurrentKind).IsNull();
            AssertThat(view.FindChild("Monster", recursive: true, owned: false)).IsNull();
            AssertThat(view.RenderTargetUpdateMode).IsEqual(SubViewport.UpdateMode.Disabled);
        }
        finally
        {
            view.Free();
        }
    }

    [TestCase]
    public void ShowMonster_HeadlessRun_NeverSchedulesA3DRender()
    {
        // The 3D-render-hang guard itself: in a headless run (which this suite always is on CI,
        // and locally via the .runsettings runner) the viewport must stay Disabled even WHILE a
        // monster is on stage; in a windowed editor run it may legitimately be Always.
        var view = new MonsterView3D();
        try
        {
            view.Build();
            view.ShowMonster("Cave Rat");

            var expected = DisplayServer.GetName() == "headless"
                ? SubViewport.UpdateMode.Disabled
                : SubViewport.UpdateMode.Always;
            AssertThat(view.RenderTargetUpdateMode).IsEqual(expected);

            view.ClearMonster();
            AssertThat(view.RenderTargetUpdateMode).IsEqual(SubViewport.UpdateMode.Disabled);
            AssertThat(view.HasMonster).IsFalse();
        }
        finally
        {
            view.Free();
        }
    }

    // ── MineWatch wiring: the milestone flash prefers 3D, falls back to the 2D silhouette ──────

    [TestCase]
    public void Milestone_KindWithGlb_Slides3DMesh_2DSilhouetteStaysHidden()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            // Floor 5 → MonsterRoster[5 % 5] = "cave-rat" — a kind WITH a gen GLB.
            watch.Refresh(
                GameFactory.NewGame(9101) with { Phase = DayPhase.Morning },
                ImmutableList.Create<GameEvent>(new FloorRecordSet(new HeroId(1), 5)));

            AssertThat(watch.Visible).IsTrue(); // the flash force-shows the strip
            AssertThat(watch.MonsterView.HasMonster).IsTrue();
            AssertThat(watch.MonsterView.CurrentKind).IsEqual("cave-rat");
            AssertThat(watch.Monster3DSlideVisible).IsTrue();
            AssertThat(watch.Monster2DSlideVisible).IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Milestone_Forgeworm_NoGlb_FallsBackTo2DSilhouette()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            // Floor 4 → MonsterRoster[4] = "forgeworm" — the one Mine kind with NO gen GLB.
            watch.Refresh(
                GameFactory.NewGame(9102) with { Phase = DayPhase.Morning },
                ImmutableList.Create<GameEvent>(new FloorRecordSet(new HeroId(1), 4)));

            AssertThat(watch.Visible).IsTrue();
            AssertThat(watch.MonsterView.HasMonster).IsFalse();
            AssertThat(watch.Monster3DSlideVisible).IsFalse();
            // The committed 2D forgeworm portrait exists (art manifest), so the silhouette path
            // still renders — the pre-3D behavior, untouched.
            AssertThat(watch.Monster2DSlideVisible).IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Milestone_Expires_3DStageCleared_RenderingOff()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            watch.Refresh(
                GameFactory.NewGame(9103) with { Phase = DayPhase.Morning },
                ImmutableList.Create<GameEvent>(new FloorRecordSet(new HeroId(1), 5)));
            AssertThat(watch.MonsterView.HasMonster).IsTrue();

            watch._Process(10.0); // direct call, comfortably past MilestoneSeconds — no frame pump

            AssertThat(watch.Monster3DSlideVisible).IsFalse();
            AssertThat(watch.MonsterView.HasMonster).IsFalse();
            AssertThat(watch.MonsterView.RenderTargetUpdateMode)
                .IsEqual(SubViewport.UpdateMode.Disabled);
        }
        finally
        {
            watch.Free();
        }
    }

    private static bool HasMeshInstance(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null })
        {
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (HasMeshInstance(child))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
