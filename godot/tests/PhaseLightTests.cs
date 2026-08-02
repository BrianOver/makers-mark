#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U11 ("lamps glow at a fixed alpha all day, no window light, no darkness"): <see
/// cref="AmbientLife2D.SetPhase"/> feeds the sim's <see cref="DayPhase"/> to the lamp-glow and
/// window-glow groups every frame, and <see cref="AmbientLife2D.LampAlphaFor"/> is the pure
/// function both read. No frame-count waits anywhere here (per this plan's own discipline) — the
/// pure-function cases drive the phase input directly with no scene tree at all, and the live-node
/// cases call <see cref="AmbientLife2D._Process"/> with an explicit accumulated delta exactly
/// once (mirrors <c>AmbientLife2DTests</c>'s existing "no live render" convention). No SubViewport
/// is involved anywhere in this file, so none of the headless-hang hazard applies.
/// </summary>
[TestSuite]
public class PhaseLightTests
{
    [TestCase]
    public void LampAlphaFor_Morning_IsNearlySnuffed()
    {
        AssertFloat(AmbientLife2D.LampAlphaFor(DayPhase.Morning)).IsEqual(0.06f);
    }

    [TestCase]
    public void LampAlphaFor_Expedition_IsAFaintDaytimePilotGlow()
    {
        AssertFloat(AmbientLife2D.LampAlphaFor(DayPhase.Expedition)).IsEqual(0.25f);
    }

    [TestCase]
    public void LampAlphaFor_EveningCampAndDeep_ShareTheSameStrongNightBand()
    {
        // "Evening/Camp/Deep 0.7-0.85" (KTD-6b): all three genuinely-dark phases read as equally
        // lit — the lamps don't care whether the party is above ground (Evening) or camped below
        // the checkpoint (Camp/ExpeditionDeep), only whether it is dark out.
        var evening = AmbientLife2D.LampAlphaFor(DayPhase.Evening);
        var camp = AmbientLife2D.LampAlphaFor(DayPhase.Camp);
        var deep = AmbientLife2D.LampAlphaFor(DayPhase.ExpeditionDeep);

        AssertFloat(evening).IsEqual(camp);
        AssertFloat(camp).IsEqual(deep);
        AssertFloat(evening).IsBetween(0.7f, 0.85f);
    }

    [TestCase]
    public void LampAlphaFor_MonotonicallyBrightensFromNightToDay()
    {
        var night = AmbientLife2D.LampAlphaFor(DayPhase.Evening);
        var expedition = AmbientLife2D.LampAlphaFor(DayPhase.Expedition);
        var morning = AmbientLife2D.LampAlphaFor(DayPhase.Morning);

        AssertFloat(night).IsGreater(expedition);
        AssertFloat(expedition).IsGreater(morning);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SetPhase_ThenProcess_DrivesLampAlphaToThePhaseBaseline()
    {
        var life = new AmbientLife2D();
        try
        {
            // Lamp index 0 carries a zero flicker-phase-offset (AmbientLife2D.Build: "i * 0.9f"
            // with i=0), and _elapsed starts at 0f, so calling _Process with delta 0 immediately
            // after Build leaves sin(0) = 0 — the flicker term vanishes and alpha lands EXACTLY on
            // the phase baseline, no tolerance band needed.
            life.Build(new Vector2(200, 190), null, new Rect2(0, 0, 640, 448), new[] { new Vector2(100, 100) });

            life.SetPhase(DayPhase.Morning);
            life._Process(0.0);
            var lamp = (Sprite2D)life.GetNode<Node2D>("LampGlow").GetChild(0);
            AssertFloat(lamp.Modulate.A).IsEqual(AmbientLife2D.LampAlphaFor(DayPhase.Morning));

            life.SetPhase(DayPhase.Evening);
            life._Process(0.0);
            AssertFloat(lamp.Modulate.A).IsEqual(AmbientLife2D.LampAlphaFor(DayPhase.Evening));

            // The whole point of the fix: the SAME lamp must read drastically brighter at night
            // than at dawn, not sit at one constant alpha all day.
            AssertFloat(AmbientLife2D.LampAlphaFor(DayPhase.Evening) - AmbientLife2D.LampAlphaFor(DayPhase.Morning))
                .IsGreater(0.5f);
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Build_CreatesOneWindowGlowPerPosition()
    {
        var windows = new[] { new Vector2(150, 120), new Vector2(400, 140) };
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, new Rect2(0, 0, 640, 448), null,
                windowGlowPositions: windows);

            var windowGroup = life.GetNode<Node2D>("WindowGlow");
            AssertThat(windowGroup.GetChildCount()).IsEqual(windows.Length);
            AssertThat(life.WindowGlowCount()).IsEqual(windows.Length);

            foreach (var child in windowGroup.GetChildren())
            {
                var sprite = (Sprite2D)child;
                AssertThat(sprite.Texture).IsNotNull();
            }
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Build_WithoutWindowGlowPositions_SkipsAll_NoCrash()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, new Rect2(0, 0, 640, 448), null);

            var windowGroup = life.GetNode<Node2D>("WindowGlow");
            AssertThat(windowGroup.GetChildCount())
                .OverrideFailureMessage("No window-glow positions must skip the group, not throw")
                .IsEqual(0);
            AssertThat(life.WindowGlowCount()).IsEqual(0);

            // A subsequent _Process tick (with a live phase set) must still be a safe no-op.
            life.SetPhase(DayPhase.Evening);
            life._Process(0.2);
        }
        finally
        {
            life.QueueFree();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SetPhase_ThenProcess_DrivesWindowAlphaToThePhaseBaseline_NoFlicker()
    {
        var life = new AmbientLife2D();
        try
        {
            life.Build(new Vector2(200, 190), null, new Rect2(0, 0, 640, 448), null,
                windowGlowPositions: new[] { new Vector2(150, 120) });
            var window = (Sprite2D)life.GetNode<Node2D>("WindowGlow").GetChild(0);

            life.SetPhase(DayPhase.Morning);
            life._Process(0.0);
            AssertFloat(window.Modulate.A).IsEqual(AmbientLife2D.LampAlphaFor(DayPhase.Morning));

            life.SetPhase(DayPhase.Evening);
            life._Process(0.0);
            AssertFloat(window.Modulate.A).IsEqual(AmbientLife2D.LampAlphaFor(DayPhase.Evening));

            // Unlike lamps, windows carry no per-instance flicker offset — repeated ticks on the
            // SAME phase must hold exactly steady (a lit window doesn't flicker like an open
            // flame).
            var before = window.Modulate.A;
            for (var i = 0; i < 10; i++)
            {
                life._Process(0.1);
            }

            AssertFloat(window.Modulate.A).IsEqual(before);
        }
        finally
        {
            life.QueueFree();
        }
    }
}
#endif
