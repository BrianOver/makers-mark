using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// Animation-gap fix (#2): the CHILD sprite node <see cref="Town2D.BuildProps"/> mounts for every
/// <c>"town2d-prop-tree"</c> entry instead of a bare <see cref="Sprite2D"/> — owns a <see
/// cref="TreeSway"/> pose driver and applies its rotation every frame. Kept as its own tiny node
/// type (rather than inlining sway math into <see cref="Town2D"/>'s own <c>_Process</c>) so each
/// tree ticks its own independent phase without <see cref="Town2D"/> having to track a list of
/// (sprite, phase) pairs itself — the node IS the state, mirroring how <see cref="HeroActor2D"/>/
/// <see cref="TownsfolkNpc2D"/> each own their own <see cref="SpriteMotion"/> instance rather than
/// a parent driving them centrally.
/// </summary>
public partial class SwayingTreeSprite2D : Sprite2D
{
    private TreeSway _sway = null!;

    /// <summary>Must be called once right after construction, before this node starts
    /// processing — mirrors every other actor's <c>Init(...)</c> convention in this namespace.
    /// <paramref name="phaseSeed"/> should be per-instance (e.g. a tree index) so a grove of trees
    /// doesn't sway in lockstep.</summary>
    public void Init(float phaseSeed)
    {
        _sway = new TreeSway(phaseSeed);
    }

    public override void _Process(double delta)
    {
        Rotation = _sway.Advance(delta);
    }
}
