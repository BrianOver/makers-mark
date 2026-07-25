#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Ambient mine-approach monsters (<see cref="MineApproach"/>) — presentation-only decoration
/// beyond the mine gate, no sim reads, no nav-participating collider. PROPERTY-ONLY by design
/// (3D-render-hang rule): every node here is built orphaned, asserted synchronously, and freed in
/// finally — no live SubViewport, no frame pump. Patrol motion is checked by calling <see
/// cref="MineCreature._Process"/> directly a few times rather than pumping the scene tree.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MineApproachTests
{
    // Brief's stated convention: the mine gate sits at z≈-26; every creature must be further out.
    private const float MineGateZ = -26f;

    [TestCase]
    public void Build_ReturnsNamedRoot_WithCreatureChildren()
    {
        var root = MineApproach.Build();
        try
        {
            AssertThat(root.Name.ToString()).IsEqual("MineApproach");
            AssertThat(root.GetChildCount()).IsGreaterEqual(5);
            foreach (var child in root.GetChildren())
            {
                AssertThat(child).IsInstanceOf<MineCreature>();
            }
        }
        finally
        {
            root.Free();
        }
    }

    [TestCase]
    public void Build_EachCreature_HasVisualMesh_AndSitsBeyondTheGate()
    {
        var root = MineApproach.Build();
        try
        {
            foreach (var node in root.GetChildren())
            {
                var creature = (MineCreature)node;

                AssertThat(creature.Mesh).IsNotNull();
                AssertBool(HasMeshInstance(creature.Mesh))
                    .OverrideFailureMessage($"creature '{creature.Name}' has no visual mesh")
                    .IsTrue();

                AssertThat(creature.Position.Z)
                    .OverrideFailureMessage(
                        $"creature '{creature.Name}' must sit beyond the mine gate (z < {MineGateZ}), got {creature.Position.Z}")
                    .IsLess(MineGateZ);
                AssertThat(creature.Post.Z).IsLess(MineGateZ);
            }
        }
        finally
        {
            root.Free();
        }
    }

    [TestCase]
    public void Build_NoCreature_HasANavParticipatingCollider()
    {
        var root = MineApproach.Build();
        try
        {
            foreach (var node in root.GetChildren())
            {
                AssertBool(HasCollider(node))
                    .OverrideFailureMessage($"creature '{node.Name}' must be pure decoration (no collider)")
                    .IsFalse();
            }
        }
        finally
        {
            root.Free();
        }
    }

    [TestCase]
    public void Process_StandaloneCreature_PatrolsNearItsPost_ZNeverDrifts()
    {
        var creature = new MineCreature();
        try
        {
            var post = new Vector3(4f, 0f, -35f);
            creature.Configure(2, post, "monster-cave-rat.glb", 1.2f);

            for (var i = 0; i < 5; i++)
            {
                creature._Process(0.4);

                AssertThat(creature.Position.DistanceTo(post)).IsLessEqual(2.5f);
                AssertThat(creature.Position.Z).IsEqual(post.Z); // patrol swings X/Y only, never Z
            }
        }
        finally
        {
            creature.Free();
        }
    }

    [TestCase]
    public void Process_DifferentIndices_PatrolOutOfPhase_NoTwoIdenticalPaths()
    {
        // Deterministic-but-varied contract: index alone must change the motion (no
        // System.Random anywhere), so two creatures at the same post/time diverge.
        var post = new Vector3(0f, 0f, -32f);
        var a = new MineCreature();
        var b = new MineCreature();
        try
        {
            a.Configure(0, post, "monster-spider.glb", 1.6f);
            b.Configure(3, post, "monster-spider.glb", 1.6f);

            a._Process(0.7);
            b._Process(0.7);

            AssertThat(a.Position.X).IsNotEqual(b.Position.X);
        }
        finally
        {
            a.Free();
            b.Free();
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

    private static bool HasCollider(Node node)
    {
        if (node is CollisionObject3D or CollisionShape3D)
        {
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (HasCollider(child))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
