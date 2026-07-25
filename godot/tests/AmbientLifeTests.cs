#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Presentation-only ambient life (chimney smoke + firefly motes; see <c>AmbientLife</c>'s class
/// doc). Property-only — no frame pump inside a live 3D <c>SubViewport</c>, per the documented
/// headless-hang trap (memory: godot-3d-headless-test-hang; same pattern as
/// <c>TownsfolkNpcsTests</c>). Just verifies <see cref="AmbientLife.Build"/> assembles the expected
/// emitter set, each already <c>Emitting</c> and positioned sanely, without ever rendering a frame.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AmbientLifeTests
{
    [TestCase]
    public void Build_ReturnsNamedRoot_WithSmokeAndFireflyGroups()
    {
        var root = AmbientLife.Build();
        try
        {
            AssertThat(root.Name.ToString()).IsEqual("AmbientLife");
            AssertThat(root.GetChildCount()).IsEqual(2);

            var smoke = root.GetNodeOrNull<Node3D>("ChimneySmoke");
            var fireflies = root.GetNodeOrNull<Node3D>("Fireflies");
            AssertThat(smoke).IsNotNull();
            AssertThat(fireflies).IsNotNull();
        }
        finally
        {
            root.QueueFree();
        }
    }

    [TestCase]
    public void Build_ChimneySmoke_HasTwoEmittingPuffs_PositionedOverRooftops()
    {
        var root = AmbientLife.Build();
        try
        {
            var smoke = root.GetNode<Node3D>("ChimneySmoke");
            AssertThat(smoke.GetChildCount()).IsEqual(2);

            foreach (var child in smoke.GetChildren())
            {
                var puff = (CpuParticles3D)child;
                AssertThat(puff.Emitting).IsTrue();
                AssertThat(puff.Amount > 0).IsTrue();
                AssertThat(puff.LocalCoords).IsFalse();

                // Rooftop height is well above the ground plane, within the village footprint.
                AssertThat(puff.Position.Y >= 5f && puff.Position.Y <= 10f).IsTrue();
                AssertThat(new Vector2(puff.Position.X, puff.Position.Z).Length() <= 20f).IsTrue();
                AssertThat(float.IsNaN(puff.Position.X)).IsFalse();
                AssertThat(float.IsNaN(puff.Position.Y)).IsFalse();
                AssertThat(float.IsNaN(puff.Position.Z)).IsFalse();
            }
        }
        finally
        {
            root.QueueFree();
        }
    }

    [TestCase]
    public void Build_Fireflies_HasThreeEmittingClusters_PositionedNearGroundLevel()
    {
        var root = AmbientLife.Build();
        try
        {
            var fireflies = root.GetNode<Node3D>("Fireflies");
            AssertThat(fireflies.GetChildCount()).IsEqual(3);

            var names = new List<string>();
            foreach (var child in fireflies.GetChildren())
            {
                var cluster = (CpuParticles3D)child;
                names.Add(cluster.Name.ToString());

                AssertThat(cluster.Emitting).IsTrue();
                AssertThat(cluster.Amount > 0).IsTrue();
                AssertThat(cluster.LocalCoords).IsFalse();

                // Motes hover near head height, well within the village footprint (mine gate at
                // z ~ -26, so this also confirms nothing drifted out past the square/road).
                AssertThat(cluster.Position.Y >= 0f && cluster.Position.Y <= 3f).IsTrue();
                AssertThat(new Vector2(cluster.Position.X, cluster.Position.Z).Length() <= 20f).IsTrue();
                AssertThat(float.IsNaN(cluster.Position.X)).IsFalse();
                AssertThat(float.IsNaN(cluster.Position.Y)).IsFalse();
                AssertThat(float.IsNaN(cluster.Position.Z)).IsFalse();
            }

            AssertThat(names.Contains("SquareMotes")).IsTrue();
            AssertThat(names.Contains("RoadBrazierMotes")).IsTrue();
            AssertThat(names.Contains("TavernMotes")).IsTrue();
        }
        finally
        {
            root.QueueFree();
        }
    }
}
#endif
