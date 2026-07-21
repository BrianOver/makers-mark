#if GDUNIT_TESTS
using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// PA8 (spec DB4/PKD8): the two active-professions stations (<c>Town3D.StationLayout</c> —
/// "forge-station" / "counter-station") are ordinary <see cref="Building3D"/> instances added to
/// the SAME <see cref="Town3D.Buildings"/> list every venue lives in, so proximity/highlight/
/// interact/click-picking are already fully generic (<see cref="WorldInput3D"/>, <see
/// cref="PlayerController"/>) — these tests exercise that shared plumbing against the new station
/// keys rather than re-deriving it, mirroring <c>Building3DInteractionTests</c>/
/// <c>PlayerController3DTests</c>'s own house pattern (including the T3 render-disable learning
/// mandatory before pumping physics frames near the town's 3D <see cref="SubViewport"/>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownStationTests
{
    private static Town3D Mount()
    {
        var town = new Town3D { Name = "Town3D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(2026));
        return town;
    }

    private static async Task PumpPhysicsOnly(Node ctx, int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await ctx.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    private static async Task WaitUntil(Node ctx, Func<bool> predicate, int maxFrames, string what)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (predicate())
            {
                return;
            }

            await PumpPhysicsOnly(ctx, 1);
        }

        throw new Exception($"{what} did not become true within {maxFrames} physics frames.");
    }

    [TestCase]
    public async Task EnterForgeStationZone_ThenInteract_RaisesForgeStation()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            string? raised = null;
            town.BuildingClicked += k => raised = k;

            var station = town.FindBuilding("forge-station");
            town.Player.GlobalPosition = station.DoorAnchorGlobal;

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == station, 60,
                "in zone but no active target");

            town.WorldInputNode.SetTarget(station); // deterministic (Building3DInteractionTests precedent)
            town.WorldInputNode.TriggerInteract();

            AssertThat(raised).IsEqual("ForgeStation");
        }
        finally
        {
            town.QueueFree();
        }
    }

    [TestCase]
    public async Task EnterCounterStationZone_ThenInteract_RaisesCounterStation()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            string? raised = null;
            town.BuildingClicked += k => raised = k;

            var station = town.FindBuilding("counter-station");
            town.Player.GlobalPosition = station.DoorAnchorGlobal;

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == station, 60,
                "in zone but no active target");

            town.WorldInputNode.SetTarget(station);
            town.WorldInputNode.TriggerInteract();

            AssertThat(raised).IsEqual("CounterStation");
        }
        finally
        {
            town.QueueFree();
        }
    }

    [TestCase]
    public async Task Interact_OutsideZone_IsNoOp()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            string? raised = null;
            town.BuildingClicked += k => raised = k;

            town.Player.GlobalPosition = new Vector3(0f, 0f, 40f); // clear of every interact zone
            await PumpPhysicsOnly(town, 5);

            AssertThat(town.WorldInputNode.ActiveTarget).IsNull();
            town.WorldInputNode.TriggerInteract(); // no active target — must be a no-op

            AssertThat(raised).IsNull();
        }
        finally
        {
            town.QueueFree();
        }
    }

    [TestCase]
    public async Task Proximity_HighlightsAndPromptsForgeStation_ClearsOnExit()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var station = town.FindBuilding("forge-station");
            town.Player.GlobalPosition = station.DoorAnchorGlobal;

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == station, 60,
                "in zone but no active target");

            AssertThat(station.IsHighlighted)
                .OverrideFailureMessage("entered the forge-station zone but it never highlighted").IsTrue();
            AssertThat(town.WorldInputNode.PromptText).IsEqual("E · Anvil");

            town.Player.GlobalPosition = new Vector3(0f, 0f, 40f); // clear of every interact zone

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == null, 60,
                "left every zone but a target is still active");

            AssertThat(station.IsHighlighted)
                .OverrideFailureMessage("left the forge-station zone but it stayed highlighted").IsFalse();
            AssertThat(town.WorldInputNode.PromptText).IsEqual(string.Empty);
        }
        finally
        {
            town.QueueFree();
        }
    }

    /// <summary>
    /// Test scenario: "two stations → nearest wins deterministically". The two real, PRODUCTION
    /// stations sit 16 units apart (see <c>Town3D.StationLayout</c>) — far enough that their
    /// interact zones never simultaneously overlap in real play, so a genuine two-way tie-break
    /// needs its own pair of deliberately-close probes. Mirrors the bare
    /// <c>Building3DInteractionTests.PrimitiveFallback_SetHighlighted_TogglesActiveMaterial</c>
    /// test seam (a <see cref="Building3D"/> built and <see cref="Building3D.Configure"/>d
    /// directly) — <see cref="WorldInput3D.FindNearestOverlapping"/> itself is pre-existing,
    /// fully generic logic (unchanged by PA8); this proves the new station keys share it exactly
    /// like every venue building already does.
    /// </summary>
    [TestCase]
    public async Task NearestOverlappingStation_WinsDeterministically()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var near = new Building3D();
            near.Configure("test-near", "Near", "TestNear", new Vector3(-1.5f, 0f, 0f), null);
            var far = new Building3D();
            far.Configure("test-far", "Far", "TestFar", new Vector3(1.5f, 0f, 0f), null);
            town.Buildings.AddChild(near);
            town.Buildings.AddChild(far);
            town.WorldInputNode.Configure(town.Player, new[] { near, far }, town.Player.Cam);

            town.Player.GlobalPosition = new Vector3(-0.5f, 0f, 0f); // inside both zones, nearer 'near'

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == near, 60,
                "nearer the 'near' probe but it did not win");

            town.Player.GlobalPosition = new Vector3(0.5f, 0f, 0f); // inside both zones, nearer 'far'

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == far, 60,
                "nearer the 'far' probe but it did not win");
        }
        finally
        {
            town.QueueFree();
        }
    }

    /// <summary>T6 (KTD12) precedent, ported to the new stations: clicking/walking to a station
    /// must never open it instantly — only arrival (within 1.2 units of its door anchor) raises
    /// <see cref="Town3D.BuildingClicked"/>.</summary>
    [TestCase]
    public async Task ClickForgeStation_WalksThenOpens_NeverInstant()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            string? raised = null;
            town.BuildingClicked += k => raised = k;
            var station = town.FindBuilding("forge-station");
            town.Player.GlobalPosition = new Vector3(0, 0, 0);
            town.Player.MoveToAndInteract(station);
            AssertThat(raised).IsNull().OverrideFailureMessage("opened instantly — must walk first (KTD12)");

            await WalkUntilArrived3D(town, town.Player, station.DoorAnchorGlobal, 600);

            // Same post-walk settle allowance as PlayerController3DTests.ClickForge_WalksThenOpens_
            // NeverInstant — WalkUntilArrived3D's 1.2-unit tolerance is looser than the agent's
            // own TargetDesiredDistance (1.0f) gating IsNavigationFinished.
            const int maxSettleFrames = 120;
            var settleFrames = 0;
            while (raised == null && settleFrames < maxSettleFrames)
            {
                await PumpPhysicsOnly(town, 1);
                settleFrames++;
            }

            if (raised == null)
            {
                throw new Exception($"BuildingClicked did not fire within {maxSettleFrames} settle frames");
            }

            AssertThat(raised).IsEqual("ForgeStation");
        }
        finally
        {
            town.QueueFree();
        }
    }
}
#endif
