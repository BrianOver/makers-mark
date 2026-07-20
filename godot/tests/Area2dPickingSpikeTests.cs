#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// World-rework U8 (plan 2026-07-19-002) — HARD GATE G1 spike: can gdUnit4Net on Godot
/// 4.6.3 drive Area2D world picking headlessly? Plan 2026-07-17-003 rejected Area2D
/// because <see cref="UiTestSupport.Click"/> emits <c>GuiInput</c>, which Area2D never
/// sees (it listens to viewport physics picking, not Control signals).
///
/// VERDICT: NO. <see cref="UiTestSupport.ClickWorld"/> — a world-transformed
/// <see cref="InputEventMouseButton"/> pushed through <see cref="SubViewport.PushInput"/>
/// — was built and run against a real Godot_v4.6.3-stable_mono_win64 binary via GODOT_BIN
/// (not just "no runner found locally"): 2 of 3 cases came back red even with
/// PhysicsObjectPicking on, a preceding mouse-motion event, and physics frames settled both
/// before and after the push. Area2D physics picking does not reach through gdUnit4Net's
/// headless run on this engine build. <c>ClickWorld_...</c> tests below are therefore NOT
/// part of the suite — they are the failing repro kept in <see cref="UiTestSupport.ClickWorld"/>'s
/// doc comment for the next person who wonders if it was tried.
///
/// FALLBACK (adopted): <see cref="UiTestSupport.TryClickArea"/> reimplements the same
/// rectangle hit-test the engine's picking pass would do and fires the identical
/// <c>Area2D.InputEvent</c> signal on a hit — so production click-handling code (U14+)
/// does not need a test-only branch. This is the seam future world-click tests should use.
/// A manual-smoke recipe (<see cref="UiTestSupport.ManualSmokeRecipe"/>) covers the gap
/// this fallback cannot: whether real physics picking itself still works for a real player.
///
/// The rig is a scratch, fully code-built scene (LitTownOverlay precedent — engine pin
/// rule 2, no .tscn authoring): SubViewport(PhysicsObjectPicking) &gt; Node2D world &gt;
/// Area2D + RectangleShape2D at a known world position, optional current Camera2D.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Area2dPickingSpikeTests
{
    /// <summary>Area center in WORLD coordinates — deliberately off the viewport origin.</summary>
    private static readonly Vector2 AreaWorldPos = new(200f, 120f);

    /// <summary>Half-extent of the 64x64 pick shape (miss case aims half + 5px outside).</summary>
    private const float ShapeHalf = 32f;

    private sealed class Rig
    {
        public SubViewport Viewport = null!;
        public Area2D Target = null!;
        public Camera2D? Camera;
        public List<InputEvent> Hits = null!;
    }

    /// <summary>
    /// Code-built scratch scene. With <paramref name="withCamera"/> a Camera2D is made
    /// current at <paramref name="cameraPos"/> so the world→screen canvas transform is
    /// no longer identity — the exact condition that breaks naive position math.
    /// </summary>
    private static async Task<Rig> MountRig(bool withCamera = false, Vector2 cameraPos = default)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var viewport = new SubViewport
        {
            Name = "SpikeViewport",
            Size = new Vector2I(640, 360),
            PhysicsObjectPicking = true, // OFF by default on SubViewport — the U14 wiring must set it too
        };
        var world = new Node2D { Name = "SpikeWorld" };
        var target = new Area2D
        {
            Name = "SpikeTarget",
            Position = AreaWorldPos,
            InputPickable = true,
        };
        target.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(ShapeHalf * 2f, ShapeHalf * 2f) },
        });
        world.AddChild(target);

        Camera2D? camera = null;
        if (withCamera)
        {
            camera = new Camera2D { Name = "SpikeCamera", Position = cameraPos };
            world.AddChild(camera);
        }

        viewport.AddChild(world);
        tree.Root.AddChild(viewport);
        camera?.MakeCurrent(); // scoped to the SubViewport only (LitTownOverlay precedent)

        var hits = new List<InputEvent>();
        target.InputEvent += (_, ev, _) => hits.Add(ev);

        // Let the physics space ingest the new shape and the camera commit its scroll
        // before any click is pushed.
        await SettlePhysics(viewport);
        return new Rig { Viewport = viewport, Target = target, Camera = camera, Hits = hits };
    }

    private static void Unmount(Rig rig)
    {
        rig.Viewport.GetParent()?.RemoveChild(rig.Viewport);
        rig.Viewport.Free();
    }

    [TestCase]
    public async Task TryClickArea_AreaAtKnownWorldPos_FiresInputEvent()
    {
        var rig = await MountRig();
        try
        {
            var hit = TryClickArea(rig.Target, AreaWorldPos);

            AssertThat(hit)
                .OverrideFailureMessage("TryClickArea reported a miss at the area's own center.")
                .IsTrue();
            AssertThat(rig.Hits.Count > 0)
                .OverrideFailureMessage("Area2D received no InputEvent from TryClickArea.")
                .IsTrue();
            var press = rig.Hits.OfType<InputEventMouseButton>().FirstOrDefault(e => e.Pressed);
            AssertThat(press).IsNotNull();
            AssertThat(press!.ButtonIndex).IsEqual(MouseButton.Left);
        }
        finally
        {
            Unmount(rig);
        }
    }

    [TestCase]
    public async Task TryClickArea_CameraOffset_StillHitsSameWorldPos()
    {
        // Camera dragged off the area (but keeping it on-screen): world (200,120) no
        // longer renders at screen (200,120). TryClickArea hit-tests in world space, so a
        // scrolled Camera2D must not desync it — the case a screen-coordinate helper (like
        // the rejected ClickWorld) gets wrong.
        var rig = await MountRig(withCamera: true, cameraPos: new Vector2(260f, 80f));
        try
        {
            var hit = TryClickArea(rig.Target, AreaWorldPos);

            AssertThat(hit)
                .OverrideFailureMessage("TryClickArea missed once a current Camera2D offset the canvas transform.")
                .IsTrue();
            AssertThat(rig.Hits.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(rig);
        }
    }

    [TestCase]
    public async Task TryClickArea_FivePxOutsideShape_DoesNotFire()
    {
        var rig = await MountRig();
        try
        {
            // 5px past the shape's right edge: inside the viewport, outside the pick shape.
            var hit = TryClickArea(rig.Target, AreaWorldPos + new Vector2(ShapeHalf + 5f, 0f));

            AssertThat(hit).IsFalse();
            AssertThat(rig.Hits.Count).IsEqual(0);
        }
        finally
        {
            Unmount(rig);
        }
    }
}
#endif
