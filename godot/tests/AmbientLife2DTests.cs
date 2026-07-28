#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Presentation-only ambient life for the 2.5D town (see <see cref="AmbientLife2D"/>'s class doc):
/// chimney smoke, dusk fireflies, flickering lamp glow. No frame pump inside a live render — every
/// fact here is settled either by inspecting the built node graph directly, or by calling <see
/// cref="AmbientLife2D._Process"/> with an accumulated delta (same no-live-render convention as
/// <c>HeroActor2DTests</c>/<c>AmbientLifeTests</c>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AmbientLife2DTests
{
    private static readonly Rect2 SampleTownRect = new(0f, 0f, 640f, 448f);

    [TestCase]
    public void Build_CreatesChimneySmokeGroup_WithForgeAndTavernPuffs_BothEmitting()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), new Vector2(220, 300), SampleTownRect, new[] { new Vector2(250, 210) });

            var smoke = life.GetNode<Node2D>("ChimneySmoke");
            AssertThat(smoke.GetChildCount()).IsEqual(2);

            foreach (var child in smoke.GetChildren())
            {
                var puff = (CpuParticles2D)child;
                AssertThat(puff.Emitting).IsTrue();
                AssertThat(puff.Amount > 0).IsTrue();
                AssertThat(float.IsNaN(puff.Position.X)).IsFalse();
                AssertThat(float.IsNaN(puff.Position.Y)).IsFalse();
            }
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    public void Build_WithoutTavernPosition_SkipsSecondPuff_NoCrash()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, SampleTownRect, null);

            var smoke = life.GetNode<Node2D>("ChimneySmoke");
            AssertThat(smoke.GetChildCount())
                .OverrideFailureMessage("A null tavern position must skip the second puff, not throw")
                .IsEqual(1);
            AssertThat(((CpuParticles2D)smoke.GetChild(0)).Name.ToString()).IsEqual("ForgeChimney");
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    public void Build_CreatesFireflyField_TwoEmittingClustersOverTownRect()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, SampleTownRect, null);

            var fireflies = life.GetNode<Node2D>("Fireflies");
            AssertThat(fireflies.GetChildCount()).IsEqual(2);

            var names = fireflies.GetChildren().Select(c => c.Name.ToString()).ToList();
            AssertThat(names.Contains("EmberMotes")).IsTrue();
            AssertThat(names.Contains("TealMotes")).IsTrue();

            foreach (var child in fireflies.GetChildren())
            {
                var field = (CpuParticles2D)child;
                AssertThat(field.Emitting).IsTrue();
                AssertThat(field.Amount > 0).IsTrue();
                AssertThat(field.EmissionShape).IsEqual(CpuParticles2D.EmissionShapeEnum.Rectangle);
                // The field must actually span the town rect, not a token sliver.
                AssertThat(field.EmissionRectExtents.X).IsGreater(0f);
                AssertThat(field.EmissionRectExtents.Y).IsGreater(0f);
            }
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    public void Build_CreatesOneLampGlowPerPosition()
    {
        var life = new AmbientLife2D();
        try
        {
            var lanterns = new[] { new Vector2(100, 100), new Vector2(200, 100), new Vector2(300, 200) };
            life.Build(new Vector2(200, 190), null, SampleTownRect, lanterns);

            var lampGroup = life.GetNode<Node2D>("LampGlow");
            AssertThat(lampGroup.GetChildCount()).IsEqual(lanterns.Length);
            AssertThat(life.LampGlowCount()).IsEqual(lanterns.Length);

            foreach (var child in lampGroup.GetChildren())
            {
                var sprite = (Sprite2D)child;
                AssertThat(sprite.Texture).IsNotNull();
                AssertThat(sprite.Modulate.A).IsGreater(0f);
            }
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    public void Build_WithEmptyLanternList_CreatesNoLampGlows_NoCrash()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, SampleTownRect, System.Array.Empty<Vector2>());

            var lampGroup = life.GetNode<Node2D>("LampGlow");
            AssertThat(lampGroup.GetChildCount()).IsEqual(0);
            AssertThat(life.LampGlowCount()).IsEqual(0);

            // A subsequent _Process tick must be a safe no-op with zero lamps.
            life._Process(0.5);
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    public void Process_FlickersLampAlpha_AroundBaseline_StaysInRange()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, SampleTownRect, new[] { new Vector2(100, 100) });

            var lamp = (Sprite2D)life.GetNode<Node2D>("LampGlow").GetChild(0);
            var initialAlpha = lamp.Modulate.A;

            // Advance several accumulated-delta ticks — pure cosmetic, no wall-clock read.
            for (var i = 0; i < 30; i++)
            {
                life._Process(0.1);
            }

            var laterAlpha = lamp.Modulate.A;
            AssertThat(laterAlpha).IsGreater(0f);
            AssertThat(laterAlpha).IsLess(1f);
            // The point of the flicker is that it MOVES — assert it isn't frozen at the baseline.
            AssertThat(laterAlpha == initialAlpha).IsFalse();
        }
        finally
        {
            life.QueueFree();
        }
    }
}
#endif
