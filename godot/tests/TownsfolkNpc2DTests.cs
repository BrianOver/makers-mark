#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Cosmetic-villager coverage for the 2.5D town's <see cref="TownsfolkNpc2D"/> — mirrors
/// <c>HeroActor2DTests</c>'s no-frame-pump style (every fact settled by calling <see
/// cref="TownsfolkNpc2D._Process"/> directly with an accumulated delta) but asserts the villager-
/// specific contract instead: no state machine, no pick zone, and wander stays within a small
/// bounded band around <see cref="TownsfolkNpc2D.Home"/> rather than travelling anywhere.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownsfolkNpc2DTests
{
    [TestCase]
    public void Init_SetsNpcIndexAndHome_AndBuildsSprite()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            var home = new Vector2(80, 120);
            npc.Init(2, new PlaceholderTexture2D(), Colors.White, home);

            AssertThat(npc.NpcIndex).IsEqual(2);
            AssertThat(npc.Home).IsEqual(home);
            AssertThat(npc.Position).IsEqual(home);
            AssertThat(npc.Sprite).IsNotNull();
            AssertThat(npc.Visible).IsTrue();
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>Coverage for the sprite-alignment/tint contract: the reused hero-body art must stay
    /// tinted with whatever civilian color the caller passes (never a class color — <see
    /// cref="TownsfolkNpc2D"/> is never handed one), and the feet-offset must derive from the
    /// RESOLVED texture's own height, not a fixed constant — mirrors
    /// <c>HeroActor2DTests.Init_TintsSpriteWithClassColor_AndSetsDynamicFeetOffsetFromTextureHeight</c>.</summary>
    [TestCase]
    public void Init_TintsSpriteWithGivenTint_AndSetsDynamicFeetOffsetFromTextureHeight()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            var tint = new Color(0.4f, 0.38f, 0.34f); // a civilian tint — distinctive, non-white
            var texture = new PlaceholderTexture2D { Size = new Vector2(20, 48) };
            npc.Init(0, texture, tint, new Vector2(50, 50));

            AssertThat(npc.Sprite.Modulate).IsEqual(tint);

            var textureHeight = npc.Sprite.Texture.GetHeight();
            AssertThat(textureHeight).IsEqual(48);
            AssertThat(npc.Sprite.Offset.Y).IsEqual(-24f); // -textureHeight/2

            var feetLocalY = npc.Sprite.Offset.Y + textureHeight / 2f;
            AssertThat(Mathf.Abs(feetLocalY) < 0.5f)
                .OverrideFailureMessage(
                    $"sprite feet not aligned to Position (sort key): offset={npc.Sprite.Offset}, " +
                    $"textureHeight={textureHeight}, feetLocalY={feetLocalY}").IsTrue();
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>Villagers have no pick zone at all — no <c>Area2D</c> child, no <c>Picked</c>
    /// event — confirming the cosmetic-only contract (never gameplay-interactable).</summary>
    [TestCase]
    public void Init_NeverBuildsAPickZone()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            npc.Init(1, new PlaceholderTexture2D(), Colors.White, Vector2.Zero);

            var areaChildren = npc.GetChildren().OfType<Area2D>().ToList();
            AssertThat(areaChildren.Count)
                .OverrideFailureMessage("TownsfolkNpc2D must have no Area2D/pick child — cosmetic only, never clickable")
                .IsEqual(0);
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>Drives many frames of ambient drift and asserts two things at once, mirroring
    /// <c>HeroActor2DTests.PoseApplication_NeverMovesPosition_ButIdleActorSpriteScaleStillBreathes</c>'s
    /// intent but for a villager that is ALWAYS wandering (no frozen state to isolate breathing in):
    /// (1) the idle-breathe pose still visibly oscillates the CHILD <see cref="TownsfolkNpc2D.Sprite"/>'s
    /// Scale, and (2) <see cref="Node2D.Position"/> (the Y-sort key) never leaves a small bounded
    /// band around <see cref="TownsfolkNpc2D.Home"/> — villagers putter in place, they don't travel.
    /// </summary>
    [TestCase]
    public void Wander_StaysWithinBoundedBandAroundHome_WhileSpriteIdleBreathes()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            var home = new Vector2(200, 150);
            npc.Init(3, new PlaceholderTexture2D(), Colors.White, home);

            var minScaleY = float.MaxValue;
            var maxScaleY = float.MinValue;
            var maxOffsetFromHome = 0f;

            for (var i = 0; i < 200; i++)
            {
                npc._Process(0.1);

                var offset = npc.Position.DistanceTo(home);
                maxOffsetFromHome = Mathf.Max(maxOffsetFromHome, offset);

                minScaleY = Mathf.Min(minScaleY, npc.Sprite.Scale.Y);
                maxScaleY = Mathf.Max(maxScaleY, npc.Sprite.Scale.Y);
            }

            AssertThat(maxOffsetFromHome < 20f)
                .OverrideFailureMessage($"villager drifted too far from Home: max offset={maxOffsetFromHome}")
                .IsTrue();

            AssertThat(maxScaleY - minScaleY > 0.001f)
                .OverrideFailureMessage(
                    $"villager sprite should breathe (Sprite.Scale.Y oscillate): min={minScaleY}, max={maxScaleY}")
                .IsTrue();
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>Determinism (KTD2/KTD4): same index + home + delta sequence must land at the same
    /// Position every step, no RNG — mirrors <c>HeroActor2DTests.Determinism_...</c>.</summary>
    [TestCase]
    public void Determinism_TwoNpcsSameConfig_IdenticalPositionsAfterSameProcessSequence()
    {
        var a = new TownsfolkNpc2D();
        var b = new TownsfolkNpc2D();
        try
        {
            var home = new Vector2(60, 90);
            a.Init(1, new PlaceholderTexture2D(), Colors.White, home);
            b.Init(1, new PlaceholderTexture2D(), Colors.White, home);

            for (var i = 0; i < 30; i++)
            {
                a._Process(0.1);
                b._Process(0.1);
            }

            AssertThat(a.Position).IsEqual(b.Position);
        }
        finally
        {
            a.QueueFree();
            b.QueueFree();
        }
    }

    /// <summary>Town2D-level smoke check (Town2DSceneTests style — same <c>Mount</c> pattern) that
    /// villager spawn-in doesn't break town construction and lands the expected count. Kept here
    /// rather than in <c>Town2DSceneTests.cs</c> itself: that file is outside this unit's owned
    /// scope, so its own coverage stays untouched; this test only READS <see cref="Town2D"/>'s
    /// public surface (<see cref="Town2D.Build"/>, <see cref="Town2D.TownsfolkCount"/>).</summary>
    [TestCase]
    public void Town2D_Built_SpawnsExpectedTownsfolkCount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 7));
        try
        {
            AssertThat(town.TownsfolkCount())
                .OverrideFailureMessage("Town2D.Build must spawn a bounded set of cosmetic villagers — BuildTownsfolk regressed")
                .IsEqual(Town2D.TownsfolkHomeTileCount);

            AssertThat(town.TownsfolkRoot.GetChildren().All(c => c is TownsfolkNpc2D))
                .OverrideFailureMessage("Every TownsfolkRoot child must be a TownsfolkNpc2D")
                .IsTrue();
        }
        finally
        {
            town.Free();
        }
    }
}
#endif
