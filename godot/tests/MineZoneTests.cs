#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The mine-zone cave mouth (<see cref="MineZone"/>) — the dark destination the village road
/// leads to, well beyond the mine gate (z≈-26) and the prowling-enemy band (z≈-30..-43).
///
/// <para>Property-only by design (3D-render-hang rule): <see cref="MineZone.Build"/> returns an
/// orphaned <see cref="Node3D"/> tree with no viewport involved, so every assertion below runs
/// synchronously against the built tree — no frame pump, ever. Every node created with
/// <c>new</c> is freed in a <c>finally</c>.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MineZoneTests
{
    [TestCase]
    public void Build_ReturnsNodeNamedMineZone_PositionedBeyondTheGateAndEnemyBand()
    {
        var zone = MineZone.Build();
        try
        {
            AssertThat(zone.Name.ToString()).IsEqual("MineZone");
            // Gate is z≈-26, enemies prowl z≈-30..-43 — the zone must sit further out still.
            AssertFloat(zone.Position.Z).IsLess(-40f);
        }
        finally
        {
            zone.Free();
        }
    }

    [TestCase]
    public void Build_ContainsExpectedStructuralChildren()
    {
        var zone = MineZone.Build();
        try
        {
            foreach (var name in new[] { "RockMound", "TunnelOpening", "Timbers", "Boulders", "RailHint", "GroundMist" })
            {
                AssertThat(zone.GetNodeOrNull<Node3D>(name))
                    .OverrideFailureMessage($"MineZone is missing expected child '{name}'")
                    .IsNotNull();
            }
        }
        finally
        {
            zone.Free();
        }
    }

    [TestCase]
    public void Build_RockMound_HasMultipleChunks()
    {
        var zone = MineZone.Build();
        try
        {
            var mound = zone.GetNode<Node3D>("RockMound");
            AssertInt(mound.GetChildCount()).IsGreaterEqual(4);
            foreach (var child in mound.GetChildren())
            {
                AssertBool(child is MeshInstance3D { Mesh: not null }).IsTrue();
            }
        }
        finally
        {
            zone.Free();
        }
    }

    [TestCase]
    public void Build_TunnelOpening_HasMouthMeshAndDeepGlowLight()
    {
        var zone = MineZone.Build();
        try
        {
            var opening = zone.GetNode<Node3D>("TunnelOpening");
            AssertThat(opening.GetNodeOrNull<MeshInstance3D>("Mouth")).IsNotNull();
            AssertThat(opening.GetNodeOrNull<OmniLight3D>("DepthLight")).IsNotNull();

            // Cold cyan/green per the mood brief, kept low-energy so it's a hint, not a beacon.
            var light = opening.GetNode<OmniLight3D>("DepthLight");
            AssertFloat(light.LightEnergy).IsLess(1.5f);
            AssertFloat(light.LightColor.R).IsLess(light.LightColor.G + 0.05f);
        }
        finally
        {
            zone.Free();
        }
    }

    [TestCase]
    public void Build_Timbers_HasTwoUprightsAndALintel()
    {
        var zone = MineZone.Build();
        try
        {
            var timbers = zone.GetNode<Node3D>("Timbers");
            AssertThat(timbers.GetNodeOrNull<MeshInstance3D>("TimberLeft")).IsNotNull();
            AssertThat(timbers.GetNodeOrNull<MeshInstance3D>("TimberRight")).IsNotNull();
            AssertThat(timbers.GetNodeOrNull<MeshInstance3D>("Lintel")).IsNotNull();
        }
        finally
        {
            zone.Free();
        }
    }

    [TestCase]
    public void Build_NoColliderOrNavigationNodes_Anywhere()
    {
        // Presentation-only contract: the player never walks into this zone in this pass, so
        // there must be no StaticBody3D/CollisionShape3D/NavigationRegion3D anywhere in the tree.
        var zone = MineZone.Build();
        try
        {
            AssertBool(HasColliderOrNav(zone))
                .OverrideFailureMessage("MineZone must be decoration-only: no collider or nav nodes")
                .IsFalse();
        }
        finally
        {
            zone.Free();
        }
    }

    [TestCase]
    public void Build_IsDeterministic_TwoBuildsHaveIdenticalChildCountsAndPositions()
    {
        var a = MineZone.Build();
        var b = MineZone.Build();
        try
        {
            AssertInt(a.GetChildCount()).IsEqual(b.GetChildCount());
            AssertThat(a.Position).IsEqual(b.Position);

            var moundA = a.GetNode<Node3D>("RockMound");
            var moundB = b.GetNode<Node3D>("RockMound");
            AssertInt(moundA.GetChildCount()).IsEqual(moundB.GetChildCount());
            for (var i = 0; i < moundA.GetChildCount(); i++)
            {
                var childA = (Node3D)moundA.GetChild(i);
                var childB = (Node3D)moundB.GetChild(i);
                AssertThat(childA.Position).IsEqual(childB.Position);
            }
        }
        finally
        {
            a.Free();
            b.Free();
        }
    }

    private static bool HasColliderOrNav(Node node)
    {
        if (node is CollisionShape3D or StaticBody3D or NavigationRegion3D or NavigationAgent3D)
        {
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (HasColliderOrNav(child))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
