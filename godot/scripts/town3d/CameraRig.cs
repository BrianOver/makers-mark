using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3: fixed-angle follow camera for the 3D town. A child <see cref="Camera3D"/> is offset
/// up+back along its own rotated local +Z axis (after pitching down by <see cref="Pitch"/>
/// degrees) so it looks down at the rig's origin from <see cref="Distance"/> units away, then
/// the whole rig glides toward <see cref="Target"/> every frame — an exponential ease
/// (<c>1 - e^-k*dt</c>) rather than a fixed-step Lerp so the follow speed is frame-rate
/// independent.
///
/// <para>PA8 (spec DB4/PKD8): <see cref="PushIn"/>/<see cref="Release"/> layer a station
/// dolly-in on top of the same follow — while pushed in, the rig eases toward the station's
/// <see cref="Node3D"/> (instead of <see cref="Target"/>) and the camera's own local offset
/// eases toward the override distance (instead of <see cref="Distance"/>), using the identical
/// exponential ease so the two states are visually indistinguishable in feel. <see
/// cref="Release"/> hands both back to the normal <see cref="Target"/>/<see cref="Distance"/>
/// follow — a plain reassignment, so a future Phase C in-world manipulation can swap what
/// opens on station arrival without ever touching this rig.</para>
/// </summary>
public partial class CameraRig : Node3D
{
    [Export] public Node3D? Target;
    [Export] public float Pitch = -42f;
    [Export] public float Distance = 22f;
    [Export] public float FollowSpeed = 5f;

    private Camera3D _cam = null!;

    /// <summary>PA8: non-null while a station dolly-in (<see cref="PushIn"/>) overrides the
    /// normal <see cref="Target"/> follow — <see cref="Release"/> clears it.</summary>
    private Node3D? _pushFocus;

    /// <summary>PA8: the per-frame eased camera distance — starts at <see cref="Distance"/>,
    /// converges toward <see cref="_targetDistance"/> every <see cref="_Process"/> tick.</summary>
    private float _currentDistance;

    /// <summary>PA8: the distance <see cref="_currentDistance"/> is easing toward — <see
    /// cref="Distance"/> normally, or <see cref="PushIn"/>'s override while pushed in.</summary>
    private float _targetDistance;

    /// <summary>The per-frame eased camera pitch (degrees) and the value it eases toward. Normally
    /// <see cref="Pitch"/> (the town's -42 top-down follow); a <see cref="PushIn"/> can override it
    /// so interiors are framed nearer eye level (~-15) and read as 3D rooms rather than floor plans.
    /// Eased with the same exponential as distance so the tilt feels identical to the dolly.</summary>
    private float _currentPitch;
    private float _targetPitch;

    /// <summary>The pitch last written to the camera node. We only re-assign
    /// <c>_cam.RotationDegrees</c> when the eased pitch actually moves, so the common no-override
    /// case (town/station follow) never touches the camera transform per frame — keeps the pure-math
    /// dolly detached-node-test-safe (writing a child transform every frame errors on a rig that was
    /// never added to a SceneTree).</summary>
    private float _appliedPitch;

    /// <summary>PA8 test/inspection surface: true while a station dolly-in is active.</summary>
    public bool IsPushedIn => _pushFocus != null;

    /// <summary>PA8 test/inspection surface: the live eased camera distance.</summary>
    public float CurrentDistance => _currentDistance;

    /// <summary>Test/inspection surface: the live eased camera pitch (degrees).</summary>
    public float CameraPitch => _currentPitch;

    public override void _Ready()
    {
        _cam = new Camera3D { Name = "Camera3D", Fov = 45f, Near = 0.5f, Far = 200f };
        AddChild(_cam);
        _currentPitch = Pitch;
        _targetPitch = Pitch;
        _appliedPitch = Pitch;
        _cam.RotationDegrees = new Vector3(_currentPitch, 0, 0);
        _currentDistance = Distance;
        _targetDistance = Distance;
        _cam.Position = _cam.Basis.Z * _currentDistance;
        _cam.Current = true;
        if (Target != null)
        {
            GlobalPosition = Target.GlobalPosition;
        }
    }

    /// <summary>
    /// PA8: dolly the rig in on <paramref name="focus"/> (a station's <see cref="Node3D"/>,
    /// e.g. the forge anvil cluster or the shop counter) at <paramref name="distance"/> units —
    /// both the follow target and the camera distance ease toward the new values over
    /// subsequent frames (never an instant cut). Calling this again (a different station, or a
    /// re-press) simply re-targets the same ease — safe to call repeatedly.
    /// </summary>
    public void PushIn(Node3D focus, float distance, float? pitch = null)
    {
        _pushFocus = focus;
        _targetDistance = distance;
        _targetPitch = pitch ?? Pitch;
    }

    /// <summary>
    /// PA8: hand the rig back to its normal <see cref="Target"/> follow at the default <see
    /// cref="Distance"/> — eases back over subsequent frames, same as <see cref="PushIn"/>.
    /// Safe to call even when no <see cref="PushIn"/> is active (a no-op ease toward the state
    /// the rig is already in) — the town host calls this unconditionally on every drawer close.
    /// </summary>
    public void Release()
    {
        _pushFocus = null;
        _targetDistance = Distance;
        _targetPitch = Pitch;
    }

    public override void _Process(double delta)
    {
        var followTarget = _pushFocus ?? Target;
        if (followTarget == null)
        {
            return;
        }

        var t = 1f - Mathf.Exp(-FollowSpeed * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(followTarget.GlobalPosition, t);
        _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, t);
        if (!Mathf.IsEqualApprox(_currentPitch, _appliedPitch))
        {
            _cam.RotationDegrees = new Vector3(_currentPitch, 0, 0); // before Position: it reads Basis.Z
            _appliedPitch = _currentPitch;
        }

        _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, t);
        _cam.Position = _cam.Basis.Z * _currentDistance;
    }
}
