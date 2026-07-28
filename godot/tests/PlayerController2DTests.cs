#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U5: <see cref="PlayerController2D"/> coverage. Mirrors <c>PlayerController3DTests</c>'
/// conventions where they still apply (the <see cref="PlayerController2D.SetDirectInput"/> test
/// seam instead of pushing synthetic input through the OS/viewport — that path is a recorded
/// dead-end per the pivot plan's playtest-harness section) and uses <see
/// cref="PumpWorldFrames"/> (already proven safe for 2D physics by the U8 world-rework work) since
/// a bare 2D <see cref="Node2D"/> tree never trips the 3D-headless-render-hang KTD that gated the
/// 3D suite's "disable the viewport first" dance.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlayerController2DTests
{
    private static PlayerController2D Mount()
    {
        var player = new PlayerController2D { Name = "Player2D" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(player);
        return player;
    }

    [TestCase]
    public void SpawnAt_SetsPosition()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(120, 340));
            AssertThat(player.Position).IsEqual(new Vector2(120, 340));
        }
        finally
        {
            player.QueueFree();
        }
    }

    [TestCase]
    public async Task Wasd_Right_MovesPlayerPositive()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(100, 100));
            var start = player.Position;

            player.SetDirectInput(new Vector2(1, 0)); // deterministic; no OS input dependency
            try
            {
                await PumpWorldFrames(player, 20);
            }
            finally
            {
                player.SetDirectInput(null);
            }

            AssertThat(player.Position.X > start.X)
                .OverrideFailureMessage($"no rightward move: {start} -> {player.Position}").IsTrue();
            AssertThat(Mathf.Abs(player.Position.Y - start.Y) < 0.5f)
                .OverrideFailureMessage($"unexpected vertical drift: {start} -> {player.Position}").IsTrue();
        }
        finally
        {
            player.QueueFree();
        }
    }

    /// <summary>M3 (animation plan Part 1): the player previously had no left-facing at all — <see
    /// cref="PlayerController2D._Process"/> now flips the CHILD <see cref="Sprite"/> via <see
    /// cref="Sprite2D.FlipH"/> off <see cref="CharacterBody2D.Velocity"/>.X.</summary>
    [TestCase]
    public async Task Wasd_Left_FlipsSpriteToFaceLeft_Right_UnflipsIt()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(100, 100));

            player.SetDirectInput(new Vector2(-1, 0));
            try
            {
                await PumpWorldFrames(player, 10);
            }
            finally
            {
                player.SetDirectInput(null);
            }

            AssertThat(player.Sprite.FlipH)
                .OverrideFailureMessage("moving left did not flip the sprite to face left").IsTrue();

            player.SetDirectInput(new Vector2(1, 0));
            try
            {
                await PumpWorldFrames(player, 10);
            }
            finally
            {
                player.SetDirectInput(null);
            }

            AssertThat(player.Sprite.FlipH)
                .OverrideFailureMessage("moving right did not restore the base (unflipped) facing").IsFalse();
        }
        finally
        {
            player.QueueFree();
        }
    }

    /// <summary>M3: an idle player (no WASD, no seek) still breathes — <see cref="SpriteMotion"/>'s
    /// idle pose oscillates <see cref="Sprite2D.Scale"/> even at zero <see
    /// cref="CharacterBody2D.Velocity"/> — while the actor's own <see cref="Node2D.Position"/> (the
    /// Y-sort key/feet baseline) never moves, since the driver only ever touches the CHILD
    /// sprite.</summary>
    [TestCase]
    public async Task Idle_SpriteBreathes_ButPositionNeverMoves()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(200, 200));
            var start = player.Position;

            var minScaleY = float.MaxValue;
            var maxScaleY = float.MinValue;
            for (var i = 0; i < 90; i++)
            {
                await PumpWorldFrames(player, 1);
                minScaleY = Mathf.Min(minScaleY, player.Sprite.Scale.Y);
                maxScaleY = Mathf.Max(maxScaleY, player.Sprite.Scale.Y);
            }

            var swing = maxScaleY - minScaleY;
            AssertThat(swing > 0.3f * SpriteMotion.BreathAmplitude)
                .OverrideFailureMessage(
                    $"idle sprite never showed a breathing scale swing (min={minScaleY}, max={maxScaleY})").IsTrue();
            AssertThat(swing < 3f * SpriteMotion.BreathAmplitude)
                .OverrideFailureMessage(
                    $"idle breathing swing implausibly large (min={minScaleY}, max={maxScaleY})").IsTrue();

            AssertThat(player.Position.DistanceTo(start) < 0.01f)
                .OverrideFailureMessage($"idle breathing moved Position: {start} -> {player.Position}").IsTrue();
        }
        finally
        {
            player.QueueFree();
        }
    }

    /// <summary>
    /// M3 feet-compensation contract (<see cref="SpriteMotion.Pose"/> doc comment): whenever the
    /// footfall squash ISN'T active (<see cref="Sprite2D.Scale"/>.Y == 1, the overwhelming majority
    /// of walk frames), the offset formula collapses exactly to <c>Offset.Y == -h/2 + BobY</c> —
    /// i.e. the sprite's bottom edge tracks <c>Position.Y + BobY</c>, never drifting off <see
    /// cref="Node2D.Position"/> (the Y-sort key/feet baseline) by more than the walk bob's own
    /// amplitude. Inverts the SAME documented formula the production code applies (using only
    /// <see cref="SpriteMotion"/>'s own public tuning constants as the independent expectation) so
    /// a sign flip, a dropped term, or a forgotten compensation would fail this even though the
    /// test never reasons about engine-internal rendering geometry.
    /// </summary>
    [TestCase]
    public async Task Walking_FeetOffsetInvariant_TracksPositionPlusBobY()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(300, 300));
            var h = player.Sprite.Texture.GetHeight();

            player.SetDirectInput(new Vector2(1, 0)); // steady walk — well above WalkSpeedThreshold
            var minImpliedBobY = float.MaxValue;
            try
            {
                for (var i = 0; i < 60; i++)
                {
                    await PumpWorldFrames(player, 1);

                    var scaleY = player.Sprite.Scale.Y;
                    var offsetY = player.Sprite.Offset.Y;

                    // Invert the documented formula:
                    // offsetY == -h/2 + bobY + h/2*(1-scaleY)  =>  bobY == offsetY + h/2*scaleY
                    var impliedBobY = offsetY + h / 2f * scaleY;
                    minImpliedBobY = Mathf.Min(minImpliedBobY, impliedBobY);

                    // Generous bound: BobY itself never exceeds [-BobAmplitude, 0], plus slack for
                    // the footfall-squash cross-term the linear compensation doesn't fully cancel.
                    var slack = SpriteMotion.BobAmplitude + 3f;
                    AssertThat(impliedBobY > -slack && impliedBobY < slack)
                        .OverrideFailureMessage(
                            $"feet drifted off Position by an implausible amount at frame {i}: " +
                            $"impliedBobY={impliedBobY}, offsetY={offsetY}, scaleY={scaleY}, h={h}").IsTrue();
                }
            }
            finally
            {
                player.SetDirectInput(null);
            }

            AssertThat(minImpliedBobY < -0.3f)
                .OverrideFailureMessage(
                    $"walk bob never showed up in the applied offset (minImpliedBobY={minImpliedBobY})").IsTrue();
        }
        finally
        {
            player.QueueFree();
        }
    }

    [TestCase]
    public async Task SetInputEnabled_False_IgnoresWasdAndZeroesVelocity()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(100, 100));
            var start = player.Position;

            player.SetInputEnabled(false);
            player.SetDirectInput(new Vector2(1, 0));
            try
            {
                await PumpWorldFrames(player, 10);
            }
            finally
            {
                player.SetDirectInput(null);
            }

            AssertThat(player.Velocity).IsEqual(Vector2.Zero);
            AssertThat(player.Position.DistanceTo(start) < 0.01f)
                .OverrideFailureMessage($"moved while input disabled: {start} -> {player.Position}").IsTrue();
        }
        finally
        {
            player.QueueFree();
        }
    }

    /// <summary>Click-to-move: a straight-line seek toward the target that stops once within the
    /// arrival tolerance, and does not overshoot into oscillation.</summary>
    [TestCase]
    public async Task MoveToTile_SeeksTargetThenStops()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(0, 0));
            var target = new Vector2(60, 0);
            player.MoveToTile(target);

            await PumpWorldFrames(player, 90);

            AssertThat(player.Position.DistanceTo(target) < 10f)
                .OverrideFailureMessage($"seek never arrived: ended at {player.Position}").IsTrue();

            var settled = player.Position;
            await PumpWorldFrames(player, 10);
            AssertThat(player.Position.DistanceTo(settled) < 0.5f)
                .OverrideFailureMessage($"kept drifting after arrival: {settled} -> {player.Position}").IsTrue();
        }
        finally
        {
            player.QueueFree();
        }
    }

    /// <summary>T6 rule ported to 2D: real WASD grabbed mid-seek cancels the click-move outright
    /// rather than fighting it for control of <see cref="CharacterBody2D.Velocity"/> — mirrors
    /// <c>town3d.PlayerController</c>'s <c>ClickMove_ThenWasd_CancelsClickMove</c>.</summary>
    [TestCase]
    public async Task MoveToTile_ThenWasd_CancelsSeek()
    {
        var player = Mount();
        try
        {
            player.SpawnAt(new Vector2(0, 0));
            player.MoveToTile(new Vector2(500, 0));
            await PumpWorldFrames(player, 3);

            player.SetDirectInput(new Vector2(0, -1)); // up — orthogonal to the queued seek
            try
            {
                await PumpWorldFrames(player, 10);
            }
            finally
            {
                player.SetDirectInput(null);
            }

            AssertThat(player.Position.X < 50f)
                .OverrideFailureMessage($"seek was not cancelled by WASD: {player.Position}").IsTrue();
            AssertThat(player.Position.Y < -0.5f)
                .OverrideFailureMessage($"WASD input did not take effect: {player.Position}").IsTrue();
        }
        finally
        {
            player.QueueFree();
        }
    }

    /// <summary>
    /// Y-sort code-level assertion (U5 required coverage, pivot-plan Risk #1), against the REAL
    /// <see cref="Building2D"/> (U3 landed in this worktree while U5 was in progress — its
    /// <c>Configure</c> sets <see cref="Node2D.Position"/> to the building's door row/sort line
    /// directly, exactly the convention this test needs). Godot's <c>YSortEnabled</c> parent sorts
    /// child <see cref="CanvasItem"/>s by their own <see cref="Node2D.Position"/>.Y ascending
    /// (drawn back-to-front) — NOT by the child <see cref="Sprite2D"/>'s visual offset. Two things
    /// have to hold for "player south of the door row draws in front of the building" to actually
    /// be true:
    /// <list type="number">
    /// <item>the player body's <see cref="Node2D.Position"/>.Y (the sort key) tracks which side of
    /// the building's door row it is standing on;</item>
    /// <item>the player's <see cref="Sprite2D"/> visual FEET (bottom edge) sit at that same
    /// <see cref="Node2D.Position"/>, via <see cref="Sprite2D.Offset"/> — otherwise a sort key
    /// that's numerically correct would still look wrong (sprite floating above/through the
    /// building).</item>
    /// </list>
    /// </summary>
    [TestCase]
    public void YSort_PlayerAboveVsBelowBuildingDoorRow_SortOrderMatchesPosition()
    {
        var building = new Building2D { Name = "Building_forge" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(building);
        building.Configure("forge", "Forge", null!, new Vector2(300, 200)); // null texture -> FallbackSize
        var doorRowY = building.Position.Y; // the building's own Y-sort key

        var north = Mount(); // "behind" the building visually
        var south = Mount(); // "in front of" the building visually
        try
        {
            north.SpawnAt(new Vector2(300, doorRowY - 40));
            south.SpawnAt(new Vector2(300, doorRowY + 40));

            // The sort key itself: ascending Position.Y == drawn later == in front, compared
            // directly against the building's own door-row sort line.
            AssertThat(north.Position.Y < doorRowY)
                .OverrideFailureMessage("player north of the door row must sort BEHIND the building").IsTrue();
            AssertThat(south.Position.Y > doorRowY)
                .OverrideFailureMessage("player south of the door row must sort IN FRONT of the building").IsTrue();
            AssertThat(south.Position.Y > north.Position.Y)
                .OverrideFailureMessage("south instance must draw after (in front of) the north instance").IsTrue();

            // The feet-alignment: the sprite's bottom edge (offset.Y + texture height / 2, since
            // Sprite2D is Centered by default) must sit at local Y=0 — i.e. exactly on this node's
            // own Position, the sort key just asserted above. Same convention Building2D.Configure
            // uses for its own sprite (offset.Y = -size.Y/2).
            AssertThat(north.Sprite.Offset.Y).IsLess(0f);
            var textureHeight = north.Sprite.Texture.GetHeight();
            var feetLocalY = north.Sprite.Offset.Y + textureHeight / 2f;
            AssertThat(Mathf.Abs(feetLocalY) < 0.5f)
                .OverrideFailureMessage(
                    $"sprite feet not aligned to Position (sort key): offset={north.Sprite.Offset}, " +
                    $"textureHeight={textureHeight}, feetLocalY={feetLocalY}").IsTrue();
        }
        finally
        {
            north.QueueFree();
            south.QueueFree();
            building.QueueFree();
        }
    }
}
#endif
