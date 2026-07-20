using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T4: the player avatar — a <see cref="CharacterBody3D"/> driven by camera-relative WASD
/// movement. <see cref="IsClickMoving"/> gates WASD off once click-to-move nav lands (T6); it
/// stays <c>false</c> here so <see cref="ApplyWasd"/> always runs.
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

    /// <summary>True while a queued click-to-move nav path (T6) owns the body; WASD is a no-op
    /// during that time. Always <c>false</c> in T4 — no nav state machine exists yet.</summary>
    public bool IsClickMoving { get; protected set; }

    private Vector2? _directInput;

    /// <summary>
    /// Deterministic test seam mirroring the old 2D <c>WorldInput.SetDirectInput</c>: when
    /// non-null, overrides <see cref="Input.GetVector"/> in <see cref="ApplyWasd"/> so tests
    /// don't depend on OS input state. Pass <c>null</c> to fall back to real input.
    /// </summary>
    public void SetDirectInput(Vector2? value) => _directInput = value;

    public override void _PhysicsProcess(double delta)
    {
        if (IsClickMoving)
        {
            return;
        }

        ApplyWasd(delta);
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
