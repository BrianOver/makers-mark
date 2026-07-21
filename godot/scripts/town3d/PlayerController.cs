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
                // of trying to blend it with manual input.
                IsClickMoving = false;
                _pending = null;
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

        // Deliberately NOT flattened to the XZ plane (matches Godot's own navigation-demo
        // convention): the baked navmesh surface sits a small, fixed distance above the visual
        // ground the body starts on, so a Y-flattened direction can compute to ~zero right at
        // the very first corner (its XZ is essentially the agent's own starting point) and never
        // let the body's tracked Y close that gap — which then keeps <see
        // cref="NavigationAgent3D.GetNextPathPosition"/>'s own internal "corner reached" check
        // (a full 3D distance test) from ever advancing past corner zero. Driving Velocity by the
        // true 3D direction lets that small vertical settle happen as a byproduct of normal
        // movement, same as the horizontal travel.
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
        rotation.Y = Mathf.LerpAngle(rotation.Y, targetYaw, (float)delta * TurnSpeed);
        Mesh.Rotation = rotation;
    }
}
