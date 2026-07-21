#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// PA8 (spec DB4/PKD8): <see cref="CameraRig.PushIn"/>/<see cref="CameraRig.Release"/> dolly
/// math. Driven by calling the rig's own <c>_Ready</c>/<c>_Process</c> directly (both are plain
/// public methods) rather than pumping engine frames — the ease is pure arithmetic with no
/// Area3D/physics dependency (unlike <c>Building3D</c> proximity, which genuinely needs the
/// physics server; see <c>Building3DInteractionTests</c>/<c>PlayerController3DTests</c> for that
/// pattern), so there is zero risk of the headless-hang trap (memory: godot-3d-headless-test-hang
/// is specifically about pumping frames near a RENDERING <see cref="SubViewport"/> — nothing here
/// mounts one, or even enters a live <see cref="SceneTree"/>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CameraRigTests
{
    private static CameraRig BuildRig(Node3D target)
    {
        var rig = new CameraRig { Target = target, Distance = 22f, FollowSpeed = 5f };
        rig._Ready();
        return rig;
    }

    [TestCase]
    public void PushIn_ConvergesTowardFocusAndOverrideDistance()
    {
        var target = new Node3D { Position = Vector3.Zero };
        var focus = new Node3D { Position = new Vector3(20f, 0f, 20f) };
        var rig = BuildRig(target);
        try
        {
            AssertThat(rig.IsPushedIn).IsFalse();

            rig.PushIn(focus, 6f);
            AssertThat(rig.IsPushedIn).IsTrue();

            for (var i = 0; i < 200; i++)
            {
                rig._Process(0.05);
            }

            AssertThat(rig.GlobalPosition.DistanceTo(focus.GlobalPosition) < 0.1f)
                .OverrideFailureMessage($"rig did not converge on the pushed-in focus: {rig.GlobalPosition}").IsTrue();
            AssertThat(Mathf.Abs(rig.CurrentDistance - 6f) < 0.05f)
                .OverrideFailureMessage($"camera distance did not converge to the override: {rig.CurrentDistance}").IsTrue();
        }
        finally
        {
            rig.Free();
            focus.Free();
            target.Free();
        }
    }

    [TestCase]
    public void Release_RestoresTargetFollowAndDefaultDistance()
    {
        var target = new Node3D { Position = new Vector3(5f, 0f, 0f) };
        var focus = new Node3D { Position = new Vector3(40f, 0f, 40f) };
        var rig = BuildRig(target);
        try
        {
            rig.PushIn(focus, 4f);
            for (var i = 0; i < 120; i++)
            {
                rig._Process(0.05);
            }

            AssertThat(rig.GlobalPosition.DistanceTo(focus.GlobalPosition) < 0.5f)
                .OverrideFailureMessage("push-in did not converge before Release — test setup invalid").IsTrue();

            rig.Release();
            AssertThat(rig.IsPushedIn).IsFalse();

            for (var i = 0; i < 200; i++)
            {
                rig._Process(0.05);
            }

            AssertThat(rig.GlobalPosition.DistanceTo(target.GlobalPosition) < 0.1f)
                .OverrideFailureMessage($"rig did not return to Target after Release: {rig.GlobalPosition}").IsTrue();
            AssertThat(Mathf.Abs(rig.CurrentDistance - 22f) < 0.05f)
                .OverrideFailureMessage($"camera distance did not return to the default: {rig.CurrentDistance}").IsTrue();
        }
        finally
        {
            rig.Free();
            focus.Free();
            target.Free();
        }
    }

    /// <summary>PA8 test scenario: "no NaNs at zero delta" — a zero-length frame must be a
    /// pure no-op ease (both <c>Mathf.Exp(0) == 1</c> branches collapse to <c>t == 0</c>), never
    /// a divide-by-zero or NaN propagation.</summary>
    [TestCase]
    public void ZeroDelta_NeverProducesNaN()
    {
        var target = new Node3D { Position = new Vector3(3f, 0f, 4f) };
        var rig = BuildRig(target);
        try
        {
            rig.PushIn(target, 8f);
            rig._Process(0.0);

            AssertThat(float.IsNaN(rig.GlobalPosition.X)).IsFalse();
            AssertThat(float.IsNaN(rig.GlobalPosition.Y)).IsFalse();
            AssertThat(float.IsNaN(rig.GlobalPosition.Z)).IsFalse();
            AssertThat(float.IsNaN(rig.CurrentDistance)).IsFalse();
        }
        finally
        {
            rig.Free();
            target.Free();
        }
    }
}
#endif
