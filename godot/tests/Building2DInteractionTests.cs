#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U3: <see cref="Building2D"/>'s click/highlight/door-anchor surface — the 2D counterpart of
/// <c>Building3DInteractionTests.PrimitiveFallback_SetHighlighted_TogglesActiveMaterial</c>. Never
/// parented into a mounted <see cref="SceneTree"/> (no render/physics needed to exercise the
/// event/property surface tested here — Godot computes <see cref="Node2D.GlobalPosition"/> from
/// the local parent-child transform chain regardless of tree membership), so each test frees its
/// building directly rather than via <c>QueueFree</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Building2DInteractionTests
{
    private static readonly Vector2 WorldPos = new(160f, 96f);

    private static Building2D BuildConfigured(string key = "forge", string nametag = "Forge")
    {
        var building = new Building2D();
        building.Configure(key, nametag, new PlaceholderTexture2D { Size = new Vector2(64f, 80f) }, WorldPos);
        return building;
    }

    [TestCase]
    public void RaisePick_FiresPickedWithKey()
    {
        var building = BuildConfigured("forge", "Forge");
        try
        {
            string? raised = null;
            building.Picked += k => raised = k;

            building.RaisePick();

            AssertThat(raised).IsEqual("forge");
        }
        finally
        {
            building.Free();
        }
    }

    [TestCase]
    public void RaisePick_UsesConfiguredKey_NotNametag()
    {
        var building = BuildConfigured("tavern", "The Rusty Tankard");
        try
        {
            string? raised = null;
            building.Picked += k => raised = k;

            building.RaisePick();

            AssertThat(raised).IsEqual("tavern");
        }
        finally
        {
            building.Free();
        }
    }

    [TestCase]
    public void DoorAnchorGlobal_IsOffsetFromWorldPos()
    {
        var building = BuildConfigured();
        try
        {
            AssertThat(building.DoorAnchorGlobal)
                .OverrideFailureMessage("DoorAnchor must sit in front of the door, not on top of the building's own Position")
                .IsNotEqual(WorldPos);

            // "In front of the door" means further along +Y (down-screen) from the Y-sort line,
            // never coincident with it (a body-radius clearance so nothing contests the footprint).
            AssertThat(building.DoorAnchorGlobal.Y)
                .OverrideFailureMessage("DoorAnchor should sit below (world +Y) the building's Y-sort line")
                .IsGreater(WorldPos.Y);
        }
        finally
        {
            building.Free();
        }
    }

    [TestCase]
    public void SetHighlighted_TogglesSpriteModulate()
    {
        var building = BuildConfigured();
        try
        {
            AssertThat(building.IsHighlighted).IsFalse();
            AssertThat(building.Sprite.Modulate).IsEqual(Colors.White);

            building.SetHighlighted(true);

            AssertThat(building.IsHighlighted).IsTrue();
            AssertThat(building.Sprite.Modulate)
                .OverrideFailureMessage("SetHighlighted(true) must brighten the sprite's modulate away from white")
                .IsNotEqual(Colors.White);

            building.SetHighlighted(false);

            AssertThat(building.IsHighlighted).IsFalse();
            AssertThat(building.Sprite.Modulate)
                .OverrideFailureMessage("SetHighlighted(false) must restore the sprite's modulate to white")
                .IsEqual(Colors.White);
        }
        finally
        {
            building.Free();
        }
    }
}
#endif
