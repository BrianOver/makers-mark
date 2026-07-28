#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U4: hero actors in the 2.5D town. Ports <c>HeroActor3DTests</c>'s assertions onto
/// <see cref="HeroActor2D"/>'s own API shape (<c>Init</c>/<c>SetState</c>/<c>RallyTo</c>/
/// <c>MarchOutTo</c>/<c>ReturnTo</c> replace <c>Configure</c>/<c>BeginDeparture</c>/
/// <c>BeginReturn</c>/<c>SetAway</c>/<c>SnapHome</c>). No frame pump: every fact here (state
/// transitions, a raised click, deterministic per-frame positions) is settled by calling
/// <see cref="HeroActor2D._Process"/> directly with an accumulated delta, same as the 3D
/// precedent's synchronous <c>Advance</c> calls — nothing here needs the node in a live
/// <c>SceneTree</c> or an actual rendered frame.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroActor2DTests
{
    [TestCase]
    public void Init_SetsHeroIdValue()
    {
        var actor = new HeroActor2D();
        try
        {
            actor.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), new Vector2(40, 60));
            AssertThat(actor.HeroIdValue).IsEqual(7);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Wandering);
        }
        finally
        {
            actor.QueueFree();
        }
    }

    /// <summary>Coverage for the sprite-alignment fix: real gen'd hero body art varies in pixel
    /// size (no longer a fixed 16x24), and hero bodies are neutral light-grey art that MUST stay
    /// class-tinted to read apart (unlike the player, which carries its own full-color art and
    /// stays untinted). Uses a distinctive non-white <c>classColor</c> and an off-placeholder
    /// texture height so neither assertion could pass by coincidence.</summary>
    [TestCase]
    public void Init_TintsSpriteWithClassColor_AndSetsDynamicFeetOffsetFromTextureHeight()
    {
        var actor = new HeroActor2D();
        try
        {
            var classColor = new Color(0.2f, 0.6f, 0.9f); // distinctive — proves the tint is real, not a White no-op
            var texture = new PlaceholderTexture2D { Size = new Vector2(40, 64) }; // taller than the old 24px constant
            actor.Init(9, "vanguard", classColor, texture, new Vector2(100, 100));

            AssertThat(actor.Sprite.Modulate).IsEqual(classColor);

            var textureHeight = actor.Sprite.Texture.GetHeight();
            AssertThat(textureHeight).IsEqual(64);
            AssertThat(actor.Sprite.Offset.Y).IsEqual(-32f); // -textureHeight/2, not the old hardcoded -12f

            var feetLocalY = actor.Sprite.Offset.Y + textureHeight / 2f;
            AssertThat(Mathf.Abs(feetLocalY) < 0.5f)
                .OverrideFailureMessage(
                    $"sprite feet not aligned to Position (sort key): offset={actor.Sprite.Offset}, " +
                    $"textureHeight={textureHeight}, feetLocalY={feetLocalY}").IsTrue();
        }
        finally
        {
            actor.QueueFree();
        }
    }

    [TestCase]
    public void RaisePick_RaisesPickedWithHeroIdValue()
    {
        var actor = new HeroActor2D();
        try
        {
            actor.Init(42, "mystic", Colors.White, new PlaceholderTexture2D(), Vector2.Zero);

            var clicked = -1; // sentinel: no hero id is ever negative
            actor.Picked += id => clicked = id;
            actor.RaisePick();

            AssertThat(clicked).IsEqual(actor.HeroIdValue);
        }
        finally
        {
            actor.QueueFree();
        }
    }

    [TestCase]
    public void SetState_Away_HidesActor_Wandering_ShowsAtHome()
    {
        var actor = new HeroActor2D();
        try
        {
            var home = new Vector2(10, 10);
            actor.Init(1, "vanguard", Colors.White, new PlaceholderTexture2D(), home);

            actor.SetState(HeroActor2D.HeroTownState.Away);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Away);
            AssertThat(actor.Visible).IsFalse();

            actor.SetState(HeroActor2D.HeroTownState.Wandering);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Wandering);
            AssertThat(actor.Visible).IsTrue();
            AssertThat(actor.Position).IsEqual(home);
        }
        finally
        {
            actor.QueueFree();
        }
    }

    /// <summary>Drives the full departure/return cycle through the public travel API and asserts
    /// every state transition lands: Wandering → Rallying (idle at rally point) → WalkingOut →
    /// Away (hidden) → WalkingIn → Wandering (home).</summary>
    [TestCase]
    public void FullCycle_Wandering_Rallying_WalkingOut_Away_WalkingIn_Wandering()
    {
        var actor = new HeroActor2D();
        try
        {
            var home = new Vector2(50, 50);
            actor.Init(3, "striker", Colors.White, new PlaceholderTexture2D(), home);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Wandering);

            var rallyPoint = new Vector2(200, 40);
            actor.RallyTo(rallyPoint);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Rallying);

            // 5s accumulated at 260px/sec is comfortably past any of this test's travel legs.
            for (var i = 0; i < 50; i++)
            {
                actor._Process(0.1);
            }

            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Rallying); // idles, doesn't auto-advance
            AssertThat(actor.Position).IsEqual(rallyPoint);

            var mineDoor = new Vector2(200, -100);
            actor.MarchOutTo(mineDoor);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.WalkingOut);

            for (var i = 0; i < 50; i++)
            {
                actor._Process(0.1);
            }

            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Away);
            AssertThat(actor.Visible).IsFalse();

            var townEdge = new Vector2(200, -90);
            actor.ReturnTo(townEdge);
            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.WalkingIn);
            AssertThat(actor.Visible).IsTrue();

            // 5s at 260px/sec is comfortably past the WalkingIn leg, but NOT continued once
            // Wandering resumes — the lissajous wander drift moves Position away from Home again
            // (by design), so this loop stops as soon as the transition lands instead of running
            // on and making a false claim about the drifted position.
            for (var i = 0; i < 50 && actor.State != HeroActor2D.HeroTownState.Wandering; i++)
            {
                actor._Process(0.1);
            }

            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Wandering);
            AssertThat(actor.Position).IsEqual(home); // arrival snaps exactly to Home before wander resumes
        }
        finally
        {
            actor.QueueFree();
        }
    }

    [TestCase]
    public void Determinism_TwoActorsSameConfig_IdenticalPositionsAfterSameProcessSequence()
    {
        var a = new HeroActor2D();
        var b = new HeroActor2D();
        try
        {
            var home = new Vector2(40, 60);
            a.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), home);
            b.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), home);

            // Identical delta sequence, no RNG, no wall-clock — same id + home must land at the
            // same Position every step (KTD2/KTD4).
            for (var i = 0; i < 25; i++)
            {
                a._Process(0.1);
                b._Process(0.1);
            }

            AssertThat(a.Position).IsEqual(b.Position);
            AssertThat(a.State).IsEqual(b.State);
        }
        finally
        {
            a.QueueFree();
            b.QueueFree();
        }
    }

    /// <summary>Mirrors <c>HeroActor3DTests</c>'s departure-determinism test: two identically
    /// <see cref="HeroActor2D.Init"/>'d actors driven through the same rally/march-out sequence
    /// with the same delta sequence must match position and state at every step, not just the
    /// end.</summary>
    [TestCase]
    public void Determinism_TwoActorsSameDeparture_IdenticalThroughRallyWalkOutAway()
    {
        var a = new HeroActor2D();
        var b = new HeroActor2D();
        try
        {
            var home = new Vector2(40, 60);
            a.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), home);
            b.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), home);

            var rallyPoint = new Vector2(20, -150);
            a.RallyTo(rallyPoint);
            b.RallyTo(rallyPoint);

            for (var i = 0; i < 60; i++)
            {
                a._Process(0.1);
                b._Process(0.1);
                AssertThat(a.Position).IsEqual(b.Position);
                AssertThat(a.State).IsEqual(b.State);
            }

            var mineDoor = new Vector2(20, -170);
            a.MarchOutTo(mineDoor);
            b.MarchOutTo(mineDoor);

            for (var i = 0; i < 60; i++)
            {
                a._Process(0.1);
                b._Process(0.1);
                AssertThat(a.Position).IsEqual(b.Position);
                AssertThat(a.State).IsEqual(b.State);
            }

            AssertThat(a.State).IsEqual(HeroActor2D.HeroTownState.Away); // sanity: actually reached Away
        }
        finally
        {
            a.QueueFree();
            b.QueueFree();
        }
    }

    /// <summary>M2 coverage: the <c>SpriteMotion</c> pose (idle breathing, in this case) is applied
    /// to the CHILD <see cref="HeroActor2D.Sprite"/>'s Scale only — the actor's own <see
    /// cref="Node2D.Position"/> (the Y-sort key/feet baseline) must stay exactly where the state
    /// machine put it, frame after frame, even while the sprite visibly breathes. Freezes the actor
    /// in Away (no wander drift, no travel) so any Position movement across frames could only come
    /// from pose application, isolating the invariant the plan calls out as hard.</summary>
    [TestCase]
    public void PoseApplication_NeverMovesPosition_ButIdleActorSpriteScaleStillBreathes()
    {
        var actor = new HeroActor2D();
        try
        {
            var home = new Vector2(30, 40);
            actor.Init(5, "vanguard", Colors.White, new PlaceholderTexture2D(), home);
            actor.SetState(HeroActor2D.HeroTownState.Away); // frozen: no drift, no travel

            var minScaleY = float.MaxValue;
            var maxScaleY = float.MinValue;
            for (var i = 0; i < 20; i++)
            {
                actor._Process(0.1);

                AssertThat(actor.Position).IsEqual(home); // Y-sort key untouched by pose, every frame
                minScaleY = Mathf.Min(minScaleY, actor.Sprite.Scale.Y);
                maxScaleY = Mathf.Max(maxScaleY, actor.Sprite.Scale.Y);
            }

            AssertThat(maxScaleY - minScaleY > 0.001f)
                .OverrideFailureMessage(
                    $"idle actor should breathe (Sprite.Scale.Y oscillate) even though Position holds: " +
                    $"min={minScaleY}, max={maxScaleY}").IsTrue();
        }
        finally
        {
            actor.QueueFree();
        }
    }

    /// <summary>M2 coverage: walking applies a nonzero lean + a non-1 bob/squash scale to the child
    /// <see cref="HeroActor2D.Sprite"/> while <see cref="HeroActor2D.Position"/> keeps landing
    /// exactly where the pre-existing <c>StepToward</c> arithmetic (unaffected by M2) would put it —
    /// proving the pose driver is actually wired into the walk path, not just idle.</summary>
    [TestCase]
    public void Walking_AppliesLeanToSprite_WhilePositionMatchesStepTowardExactly()
    {
        var actor = new HeroActor2D();
        try
        {
            var home = new Vector2(0, 0);
            actor.Init(2, "vanguard", Colors.White, new PlaceholderTexture2D(), home);

            actor.RallyTo(new Vector2(1000, 0)); // far enough that 0.5s of travel never arrives

            for (var i = 0; i < 5; i++)
            {
                actor._Process(0.1); // 260px/s * 0.1s = 26px/frame, straight along +X
            }

            AssertThat(actor.State).IsEqual(HeroActor2D.HeroTownState.Rallying); // still travelling
            var expected = home + new Vector2(HeroActor2D.WalkSpeed * 0.5f, 0f);
            AssertThat(Mathf.Abs(actor.Position.X - expected.X) < 0.5f)
                .OverrideFailureMessage($"Position should match StepToward's own arithmetic exactly: " +
                    $"got={actor.Position}, expected={expected}").IsTrue();

            AssertThat(Mathf.Abs(actor.Sprite.Rotation) > 0f)
                .OverrideFailureMessage("walking at full pace toward +X should lean the sprite")
                .IsTrue();
        }
        finally
        {
            actor.QueueFree();
        }
    }
}
#endif
