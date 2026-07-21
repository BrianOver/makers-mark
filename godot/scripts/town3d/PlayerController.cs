using System;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T4/T6: the player avatar — a <see cref="CharacterBody3D"/> driven by camera-relative WASD
/// movement OR a navmesh-following click-to-move path. <see cref="IsClickMoving"/> gates WASD off
/// while a click-move is in progress; WASD input mid-move cancels the nav path and falls back to
/// <see cref="ApplyWasd"/> the same frame (a real player grabbing the stick always wins).
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    [Export] public float Speed = 5f;
    [Export] public float TurnSpeed = 12f;

    /// <summary>The body's resting Y — matches the top surface of <c>Town3D.BuildGround</c>'s
    /// ground plane (y=0) and the (unoffset) Y of the <see cref="CharacterBody3D"/> root that
    /// <c>Town3D.BuildPlayer</c> constructs. <see cref="FollowNav"/> deliberately lets the body
    /// climb toward the baked navmesh's corner height (~0.5 above ground, a Recast voxelization
    /// artifact) WHILE a click-move is in progress — <see cref="NavigationAgent3D"/> tracks path
    /// progress against the body's own real position, so it needs that climb to advance past
    /// each corner (see <see cref="FollowNav"/>'s comment for the empirical proof forcing this
    /// horizontal-only doesn't work). Both places a click-move ends — arrival (<see
    /// cref="FollowNav"/>'s <see cref="NavigationAgent3D.IsNavigationFinished"/> branch) and a
    /// WASD interrupt (<see cref="_PhysicsProcess"/>) — pin <see cref="Node3D.GlobalPosition"/>
    /// back to this value, so the climb is always transient and never a permanent state
    /// change.</summary>
    private const float GroundY = 0f;

    /// <summary>Set by <c>Town3D.Build</c> to the rig's <see cref="Camera3D"/> so WASD input can
    /// be interpreted camera-relative (movement always means "relative to what's on screen",
    /// regardless of the fixed <see cref="CameraRig"/> pitch).</summary>
    public Camera3D Cam = null!;

    /// <summary>The visual child (Kenney GLB instance or primitive capsule fallback) that gets
    /// turned to face the travel direction; the <see cref="CharacterBody3D"/> root itself never
    /// rotates so its collision shape stays axis-aligned.</summary>
    public Node3D Mesh = null!;

    /// <summary>T6: set by <c>Town3D.BuildPlayer</c> — drives <see cref="FollowNav"/>. Baked from
    /// the same isolated <c>World3D</c> the town's <c>NavigationRegion3D</c> lives in (the
    /// <c>SubViewport</c>'s <c>OwnWorld3D</c>), so its navigation map always matches that region's.
    /// </summary>
    public NavigationAgent3D Agent = null!;

    /// <summary>True while a queued click-to-move nav path (T6) owns the body; WASD is a no-op
    /// while true, EXCEPT that nonzero WASD input cancels the click-move first (see
    /// <see cref="_PhysicsProcess"/>).</summary>
    public bool IsClickMoving { get; protected set; }

    /// <summary>T6: the building a <see cref="MoveToAndInteract"/> walk is heading for, or
    /// <c>null</c> for a plain <see cref="MoveTo"/> (ground click) — read by <see
    /// cref="FollowNav"/> on arrival to decide whether to raise <see cref="ArrivedAtBuilding"/>.
    /// </summary>
    private Building3D? _pending;

    /// <summary>Raised on arrival (within 1.2 units of <see cref="Building3D.DoorAnchorGlobal"/>)
    /// at a <see cref="MoveToAndInteract"/> target, carrying its <see cref="Building3D.ClickKey"/>
    /// — <c>Town3D</c> re-emits this into <c>BuildingClicked</c> unchanged (KTD2:
    /// presentation-only, never opens instantly).</summary>
    public event Action<string>? ArrivedAtBuilding;

    private Vector2? _directInput;

    /// <summary>
    /// Deterministic test seam mirroring the old 2D <c>WorldInput.SetDirectInput</c>: when
    /// non-null, overrides <see cref="Input.GetVector"/> in <see cref="ApplyWasd"/> so tests
    /// don't depend on OS input state. Pass <c>null</c> to fall back to real input.
    /// </summary>
    public void SetDirectInput(Vector2? value) => _directInput = value;

    /// <summary>T6: queue a navmesh path to a bare ground point (no building) — <see
    /// cref="_pending"/> stays null so arrival never raises <see cref="ArrivedAtBuilding"/>.
    /// Presentation-only (KTD2): only ever mutates <see cref="CharacterBody3D.GlobalPosition"/>.
    /// </summary>
    public void MoveTo(Vector3 point)
    {
        _pending = null;
        Agent.TargetPosition = point;
        IsClickMoving = true;
    }

    /// <summary>T6: queue a navmesh path to <paramref name="building"/>'s door anchor; on arrival
    /// (see <see cref="FollowNav"/>) raises <see cref="ArrivedAtBuilding"/> with its <see
    /// cref="Building3D.ClickKey"/>. Never opens instantly (KTD12) — <see cref="IsClickMoving"/>
    /// flips true here and the walk always happens before the signal fires.</summary>
    public void MoveToAndInteract(Building3D building)
    {
        _pending = building;
        Agent.TargetPosition = building.DoorAnchorGlobal;
        IsClickMoving = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsClickMoving)
        {
            var wasd = _directInput ?? Input.GetVector("move_left", "move_right", "move_up", "move_down");
            if (wasd.LengthSquared() > 0.0001f)
            {
                // A real player grabbing WASD mid-walk wins outright — drop the nav path instead
                // of trying to blend it with manual input. Ground the body first: FollowNav may
                // have been mid-climb toward the navmesh's corner height (see FollowNav and
                // GroundY) when this cancel lands, and ApplyWasd's Velocity.Y=0 only preserves
                // whatever Y is already there rather than resetting it — without this pin, a
                // WASD-cancelled click-move would leave the player floating from then on.
                IsClickMoving = false;
                _pending = null;
                GlobalPosition = new Vector3(GlobalPosition.X, GroundY, GlobalPosition.Z);
                ApplyWasd(delta);
                return;
            }

            FollowNav(delta);
            return;
        }

        ApplyWasd(delta);
    }

    /// <summary>
    /// T6 nav follow loop (research §2/§3). Guards the map-not-ready window: even a synchronous
    /// bake leaves <see cref="NavigationServer3D.MapGetIterationId"/> at 0 until the server's
    /// first per-frame sync pass commits it, and querying the agent before that is meaningless —
    /// so this returns early rather than acting on a stale/empty path. <see
    /// cref="NavigationAgent3D.IsNavigationFinished"/> is checked BEFORE <see
    /// cref="NavigationAgent3D.GetNextPathPosition"/> (calling the latter on a finished path is
    /// undefined), and the latter is called at most once per frame.
    /// </summary>
    private void FollowNav(double delta)
    {
        if (NavigationServer3D.MapGetIterationId(Agent.GetNavigationMap()) == 0)
        {
            return;
        }

        if (Agent.IsNavigationFinished())
        {
            Velocity = Vector3.Zero;
            MoveAndSlide();
            GlobalPosition = new Vector3(GlobalPosition.X, GroundY, GlobalPosition.Z);
            IsClickMoving = false;

            var pending = _pending;
            _pending = null;
            if (pending != null && GlobalPosition.DistanceTo(pending.DoorAnchorGlobal) <= 1.2f)
            {
                ArrivedAtBuilding?.Invoke(pending.ClickKey);
            }

            return;
        }

        var next = Agent.GetNextPathPosition();
        var toNext = next - GlobalPosition;

        // Deliberately NOT flattened to the XZ plane — verified empirically, not just by
        // inspection, that flattening deadlocks navigation entirely: the navmesh's very first
        // path point sits ~0.5 directly above the body's own start (a Recast voxelization
        // artifact — its XZ delta is ~0), and NavigationAgent3D tracks path progress against the
        // body's own real GlobalPosition (including Y). Forcing Velocity.Y=0 means the body can
        // never physically close that ~0.5 vertical gap, so the "reached this corner" check
        // (which needs that gap closed) never fires and the path permanently stalls at corner
        // zero — reproduced directly: 596 identical frames, zero net movement, no error. Driving
        // the true 3D direction lets the body climb toward navmesh height as a byproduct of
        // normal movement (every corner in this bake shares ~0.5 elevation, so the climb holds
        // for the rest of the walk, same as Godot's own navigation-demo convention) — GroundY
        // pins the body back to the visual ground the instant the click-move ends (see
        // IsNavigationFinished above and the WASD-cancel branch in _PhysicsProcess), so this
        // climb is always transient and never a permanent state change.
        var dir = toNext.LengthSquared() > 0.0001f ? toNext.Normalized() : Vector3.Zero;

        Velocity = dir * Speed;
        MoveAndSlide();

        var flatDir = new Vector3(dir.X, 0, dir.Z);
        Face(flatDir.LengthSquared() > 0.0001f ? flatDir.Normalized() : Vector3.Zero, delta);
    }

    /// <summary>
    /// Reads the WASD axis (or the direct-input test seam), rotates it into camera space so
    /// "forward" always tracks what the player sees on screen, flattens the result onto the
    /// ground plane, and drives the body through the normal <see cref="Velocity"/>/
    /// <see cref="CharacterBody3D.MoveAndSlide"/> physics step. Flattening a camera-relative
    /// vector shrinks its length by however much the fixed <see cref="CameraRig"/> pitch tilts
    /// it into Y, so the flattened direction is re-normalized and rescaled by the original input
    /// magnitude to keep movement speed consistent regardless of camera angle.
    /// </summary>
    public void ApplyWasd(double delta)
    {
        var input = _directInput ?? Input.GetVector("move_left", "move_right", "move_up", "move_down");

        var camRelative = Cam.GlobalBasis * new Vector3(input.X, 0, input.Y);
        var flat = new Vector3(camRelative.X, 0, camRelative.Z);

        var magnitude = input.Length();
        var dir = flat.LengthSquared() > 0.0001f ? flat.Normalized() * magnitude : Vector3.Zero;

        Velocity = new Vector3(dir.X * Speed, 0, dir.Z * Speed);
        MoveAndSlide();

        Face(dir, delta);
    }

    /// <summary>Turns <see cref="Mesh"/> toward the travel direction at <see cref="TurnSpeed"/>
    /// radians/sec via <see cref="Mathf.LerpAngle"/>; a no-op with zero input so the mesh keeps
    /// facing whichever way it last moved instead of snapping back to a default heading.</summary>
    private void Face(Vector3 dir, double delta)
    {
        if (Mesh == null || dir.LengthSquared() < 0.0001f)
        {
            return;
        }

        var targetYaw = Mathf.Atan2(-dir.X, -dir.Z);
        var rotation = Mesh.Rotation;
        rotation.Y = Mathf.LerpAngle(rotation.Y, targetYaw, Mathf.Clamp((float)delta * TurnSpeed, 0f, 1f));
        Mesh.Rotation = rotation;
    }
}
