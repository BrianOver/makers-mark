#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U7 (asset-completion wave, "the workshop is not switched off"): the forge's furnace/anvil
/// heat-glow pulse. Mirrors <c>TreeSwayTests</c>'s split — the pure <see cref="EmberPulse"/>
/// accumulator needs no <see cref="RequireGodotRuntimeAttribute"/> at all, only the node-wiring
/// half (mounting through a real <c>Town2D</c>, reading a live <see cref="Sprite2D.Modulate"/>)
/// does.
/// </summary>
[TestSuite]
public class EmberPulseTests
{
    [TestCase]
    public void Advance_SameInputSequence_ProducesIdenticalAlphaSequence()
    {
        // Determinism (KTD2/KTD4): identical (tuning, phaseSeed, delta sequence) must land on
        // identical alpha values, frame for frame.
        var deltas = new[] { 0.016, 0.016, 0.033, 0.05, 0.016, 0.1 };

        var a = new EmberPulse(phaseSeed: 0.7f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);
        var b = new EmberPulse(phaseSeed: 0.7f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);

        for (var i = 0; i < deltas.Length; i++)
        {
            AssertThat(a.Advance(deltas[i])).IsEqual(b.Advance(deltas[i]));
        }
    }

    [TestCase]
    public void Advance_OverAFullCycle_ActuallyMoves_NotFrozen()
    {
        var pulse = new EmberPulse(phaseSeed: 0f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);
        var min = float.MaxValue;
        var max = float.MinValue;

        for (var i = 0; i < 200; i++)
        {
            var alpha = pulse.Advance(0.05);
            min = Mathf.Min(min, alpha);
            max = Mathf.Max(max, alpha);
        }

        AssertThat(max - min > 0.001f)
            .OverrideFailureMessage("the furnace/anvil glow must actually pulse over time, not stay frozen")
            .IsTrue();
    }

    [TestCase]
    public void Advance_NeverExceedsBaseAlphaPlusAmplitude()
    {
        var pulse = new EmberPulse(phaseSeed: 0.4f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);

        for (var i = 0; i < 300; i++)
        {
            var alpha = pulse.Advance(0.033);
            AssertFloat(alpha).IsLessEqual(0.6f + 0.0001f);
            AssertFloat(alpha).IsGreaterEqual(0.2f - 0.0001f);
        }
    }

    [TestCase]
    public void Advance_DesyncsAcrossDifferentPhaseSeeds()
    {
        // Same per-instance-phase idiom AmbientLife2D's lamp flicker / Building2D's Tell already
        // use: two stations with different phaseSeed must not pulse in lockstep.
        var furnace = new EmberPulse(phaseSeed: 0f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);
        var anvil = new EmberPulse(phaseSeed: 2.1f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);

        var sawDivergence = false;
        for (var i = 0; i < 20; i++)
        {
            var a = furnace.Advance(0.1);
            var b = anvil.Advance(0.1);
            if (Mathf.Abs(a - b) > 0.0001f)
            {
                sawDivergence = true;
            }
        }

        AssertThat(sawDivergence).IsTrue();
    }

    [TestCase]
    public void Advance_ZeroDelta_HoldsCurrentAlpha()
    {
        var pulse = new EmberPulse(phaseSeed: 0.8f, baseAlpha: 0.4f, amplitude: 0.2f, hz: 0.35f);
        var first = pulse.Advance(0.05);
        var held = pulse.Advance(0.0);

        AssertThat(held).IsEqual(first);
    }
}

/// <summary>Node-wiring half: the furnace/anvil stations actually carry the glow, driven by real
/// accumulated per-frame delta, torn down cleanly on room rebuild. Mirrors
/// <c>InteriorRoomTests.Mount</c>'s exact town-build convention.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeEmberGlow2DTests
{
    private static Town2D Mount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 2026));
        return town;
    }

    [TestCase]
    public void FurnaceAndAnvilStations_CarryAnEmberGlowChild()
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom("forge");
            var furnace = room.Stations.First(s => s.Key == "furnace");
            var anvil = room.Stations.First(s => s.Key == "anvil");

            AssertThat(furnace.GetNodeOrNull<Sprite2D>("EmberGlow"))
                .OverrideFailureMessage("the furnace station has no EmberGlow child — it reads as switched off")
                .IsNotNull();
            AssertThat(anvil.GetNodeOrNull<Sprite2D>("EmberGlow"))
                .OverrideFailureMessage("the anvil station has no EmberGlow child — it reads as switched off")
                .IsNotNull();
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void EveryOtherForgeStation_CarriesNoEmberGlow()
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom("forge");
            foreach (var station in room.Stations.Where(s => s.Key is not ("furnace" or "anvil")))
            {
                AssertThat(station.GetNodeOrNull<Sprite2D>("EmberGlow"))
                    .OverrideFailureMessage($"station '{station.Key}' unexpectedly carries an EmberGlow — only the furnace and anvil should")
                    .IsNull();
            }
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void Process_PulsesFurnaceGlowAlpha_AroundBaseline_NotFrozen()
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom("forge");
            var furnace = room.Stations.First(s => s.Key == "furnace");
            var glow = furnace.GetNode<Sprite2D>("EmberGlow");

            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = 0; i < 60; i++)
            {
                glow._Process(0.1);
                min = Mathf.Min(min, glow.Modulate.A);
                max = Mathf.Max(max, glow.Modulate.A);
            }

            AssertThat(max - min > 0.001f)
                .OverrideFailureMessage("the furnace glow must visibly pulse, not sit at one fixed alpha")
                .IsTrue();
            AssertThat(min).IsGreaterEqual(0f);
            AssertThat(max).IsLessEqual(1f);
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void RepeatedForgeRoomMountAndTeardown_LeavesNoOrphanNodes()
    {
        // Mirrors PanelRebuildDoesNotLeakNodesTests' own idiom (Performance's own orphan
        // counter) — proves the EmberGlow child this unit attaches to the furnace/anvil
        // stations is torn down by the SAME Free() cascade as the rest of the station, not
        // stranded as a live, parentless node.
        var before = OrphanNodeCount();
        var scratch = new Node2D();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(scratch);
        try
        {
            for (var i = 0; i < 25; i++)
            {
                var room = new InteriorRoom2D();
                scratch.AddChild(room);
                room.Build(InteriorLayout2D.Rooms["forge"]);
                foreach (var station in room.Stations)
                {
                    scratch.AddChild(station);
                }

                foreach (var station in room.Stations.ToArray())
                {
                    scratch.RemoveChild(station);
                    station.Free();
                }

                scratch.RemoveChild(room);
                room.Free();
            }
        }
        finally
        {
            scratch.Free();
        }

        var leaked = OrphanNodeCount() - before;
        AssertThat(leaked)
            .OverrideFailureMessage($"{leaked} nodes survived {25} forge-room mount/teardown cycles as live, parentless orphans")
            .IsEqual(0);
    }

    private static int OrphanNodeCount() =>
        (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
}
#endif
