using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U37 (§11, R25/R27, "she breathes"): the idle-breath treatment for a STATIC station sprite —
/// mounted as a child of one <see cref="Building2D"/> station (mirrors <see
/// cref="ForgeEmberGlowSprite2D"/>'s own "attach at BuildStations, tear down for free" shape:
/// freeing the station frees this along with every other child it already has). Drives <see
/// cref="Building2D.Sprite"/>'s <c>Offset</c>/<c>Scale</c> straight off <see cref="SpriteMotion"/>
/// — the SAME driver <see cref="HeroActor2D"/>/<see cref="TownsfolkNpc2D"/> already use for every
/// walking actor's idle breath, reused rather than a second accumulator invented for one station.
///
/// <para><b>Always the idle pose, never a walk.</b> A station never moves (<see
/// cref="Node2D.Position"/> only ever changes across a room REBUILD, never within one frame), so
/// this always feeds <see cref="SpriteMotion.Advance"/> a zero velocity — <see
/// cref="SpriteMotion.IdlePose"/> is the only branch that can ever fire, which is exactly the
/// breathing squash/stretch the spec asks for and nothing else (no bob, no lean, no footstep
/// frame).</para>
///
/// <para><b>Feet-compensation, per <see cref="SpriteMotion.Pose"/>'s own contract.</b> <see
/// cref="Building2D.Configure"/> sets the sprite's resting <c>Offset</c> to <c>(0, -h/2)</c> so the
/// sprite's BOTTOM edge lands on the station's Y-sort row; this class reads that once at <see
/// cref="Init"/> and re-derives <c>h</c> from it, so a breathing frame's nonzero <c>Scale.Y</c>
/// never makes her feet appear to sink or float (the same correction <c>HeroActor2D.ApplySpritePose</c>
/// already applies for a walking actor).</para>
/// </summary>
public sealed class StationBreath2D
{
    private readonly Sprite2D _sprite;
    private readonly SpriteMotion _motion;
    private readonly float _restOffsetY;

    /// <param name="sprite">The station's own <see cref="Building2D.Sprite"/> — already Configured
    /// (resting Offset/Scale set), so this class only ever ADDS the breath on top each frame.</param>
    /// <param name="phaseSeed">Per-instance phase offset (radians) — mirrors <see
    /// cref="HeroActor2D"/>/<see cref="TownsfolkNpc2D"/>'s own id-derived seed, so a future second
    /// breathing station never breathes in lockstep with this one.</param>
    public StationBreath2D(Sprite2D sprite, float phaseSeed)
    {
        _sprite = sprite;
        _restOffsetY = sprite.Offset.Y;
        _motion = new SpriteMotion(phaseSeed);
    }

    /// <summary>Advance one frame and repaint the sprite's <c>Offset</c>/<c>Scale</c>. <paramref
    /// name="delta"/> is the raw frame delta (seconds) — same accumulate-and-repaint shape every
    /// sibling cosmetic animator in this namespace already follows unconditionally, independent of
    /// <c>PhaseClock.Playing</c> (<see cref="ForgeEmberGlowSprite2D"/>'s own class doc names this
    /// carve-out: "particles + <c>_Process</c> flicker are fine").</summary>
    public void Advance(double delta)
    {
        var pose = _motion.Advance(delta, Vector2.Zero, walkSpeed: 1f);
        var halfHeight = -_restOffsetY;
        _sprite.Scale = pose.Scale;
        _sprite.Offset = new Vector2(0f, _restOffsetY + pose.BobY + halfHeight * (1f - pose.Scale.Y));
    }
}
