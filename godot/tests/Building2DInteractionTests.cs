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

    // U12 (world-and-interiors plan, "stations you can read across the room"): Configure's
    // showTell flag defaults to false so every existing caller (Town2D's outdoor buildings,
    // every other test in this suite) is unaffected by construction — this is the regression pin.
    [TestCase]
    public void Configure_DefaultsShowTellFalse_NoTellNode()
    {
        var building = BuildConfigured();
        try
        {
            AssertThat(building.Tell)
                .OverrideFailureMessage("Configure() with no showTell argument must never build a Tell node — town buildings must render unchanged.")
                .IsNull();
        }
        finally
        {
            building.Free();
        }
    }

    [TestCase]
    public void Configure_ShowTellTrue_BuildsATellNode()
    {
        var building = new Building2D();
        building.Configure("anvil", "Anvil", new PlaceholderTexture2D { Size = new Vector2(64f, 80f) }, WorldPos, showTell: true);
        try
        {
            AssertThat(building.Tell)
                .OverrideFailureMessage("Configure(showTell: true) must build a Tell node — a verb station's sight-level cue.")
                .IsNotNull();
            AssertThat(building.Tell!.Modulate.A).IsGreater(0f);
        }
        finally
        {
            building.Free();
        }
    }

    /// <summary>
    /// Mirrors <c>AmbientLife2DTests.Process_FlickersLampAlpha_AroundBaseline_StaysInRange</c>'s
    /// own idiom exactly: <see cref="Building2D"/> is never added to a live <c>SceneTree</c>, so
    /// <c>_Process</c> is called directly (a plain public method once overridden) rather than
    /// awaiting real engine frames — no SubViewport rendering is ever pumped here (constraint 4
    /// does not even apply; there is no viewport in this test at all).
    /// </summary>
    [TestCase]
    public void Process_PulsesTellAlpha_StaysInDocumentedRange_NeverFrozen()
    {
        var building = new Building2D();
        building.Configure("anvil", "Anvil", new PlaceholderTexture2D { Size = new Vector2(64f, 80f) }, WorldPos, showTell: true);
        try
        {
            var initialAlpha = building.Tell!.Modulate.A;

            var seenAlphas = new System.Collections.Generic.List<float> { initialAlpha };
            for (var i = 0; i < 40; i++)
            {
                building._Process(0.1);
                seenAlphas.Add(building.Tell!.Modulate.A);
            }

            foreach (var alpha in seenAlphas)
            {
                // Class doc's TellBaseAlpha/TellPulseAmplitude contract: roughly 0.25..0.85, with
                // slack on both ends for float sine precision. Bounds intentionally NOT hardcoded
                // to the exact constants (0.55/0.30) so a future retune doesn't need this test
                // edited in lockstep — only that the pulse stays a bounded fraction, never clamps
                // to 0 or blows past 1.
                AssertThat(alpha).IsGreater(0.15f);
                AssertThat(alpha).IsLess(0.95f);
            }

            // The point of a pulse is that it MOVES — assert the alpha actually varies across the
            // sampled frames, not frozen at whatever Configure() seeded it to (the same "isn't
            // frozen at the baseline" pin AmbientLife2DTests uses for the lamp flicker).
            AssertThat(seenAlphas.TrueForAll(a => a == initialAlpha))
                .OverrideFailureMessage("Tell's alpha never changed across 40 _Process ticks — the pulse is frozen, not animating.")
                .IsFalse();
        }
        finally
        {
            building.Free();
        }
    }

    [TestCase]
    public void Process_WithNoTellNode_IsANoOp_NoCrash()
    {
        var building = BuildConfigured();
        try
        {
            // Every ordinary building/flavor station has no Tell — _Process must tolerate that
            // (the null-guard at the top of the override) rather than NullReferenceException.
            building._Process(0.1);

            AssertThat(building.Tell).IsNull();
        }
        finally
        {
            building.Free();
        }
    }
}
#endif
