using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3: fixed-angle follow camera for the 3D town. A child <see cref="Camera3D"/> is offset
/// up+back along its own rotated local +Z axis (after pitching down by <see cref="Pitch"/>
/// degrees) so it looks down at the rig's origin from <see cref="Distance"/> units away, then
/// the whole rig glides toward <see cref="Target"/> every frame — an exponential ease
/// (<c>1 - e^-k*dt</c>) rather than a fixed-step Lerp so the follow speed is frame-rate
/// independent.
/// </summary>
public partial class CameraRig : Node3D
{
    [Export] public Node3D? Target;
    [Export] public float Pitch = -42f;
    [Export] public float Distance = 22f;
    [Export] public float FollowSpeed = 5f;

    private Camera3D _cam = null!;

    public override void _Ready()
    {
        _cam = new Camera3D { Name = "Camera3D", Fov = 45f, Near = 0.5f, Far = 200f };
        AddChild(_cam);
        _cam.RotationDegrees = new Vector3(Pitch, 0, 0);
        _cam.Position = _cam.Basis.Z * Distance;
        _cam.Current = true;
        if (Target != null)
        {
            GlobalPosition = Target.GlobalPosition;
        }
    }

    public override void _Process(double delta)
    {
        if (Target == null)
        {
            return;
        }

        var t = 1f - Mathf.Exp(-FollowSpeed * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(Target.GlobalPosition, t);
    }
}
