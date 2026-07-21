#if GDUNIT_TESTS
using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

[TestSuite]
[RequireGodotRuntime]
public class Building3DInteractionTests
{
    private static Town3D Mount()
    {
        var town = new Town3D { Name = "Town3D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(2026));
        return town;
    }

    /// <summary>
    /// T3 learning (mandatory for T4+, see <c>PlayerController3DTests.PumpPhysicsOnly</c>
    /// precedent): pumping frames while the 3D <see cref="SubViewport"/> is rendering hangs the
    /// headless gdUnit runner. Callers disable render right after mount, then physics still
    /// advances so Area3D proximity overlap can be exercised without ever pumping a render frame.
    /// </summary>
    private static async Task PumpPhysicsOnly(Node ctx, int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await ctx.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>Capped physics-frame wait for a predicate (the overlap settling after a teleport)
    /// — throws on exhaustion instead of hanging, same discipline as every other tick-loop helper
    /// in this suite.</summary>
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
    public async Task EnterForgeZone_ThenInteract_RaisesForge()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            string? raised = null;
            town.BuildingClicked += k => raised = k;

            var forge = town.FindBuilding("forge");
            town.Player.GlobalPosition = forge.DoorAnchorGlobal;

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == forge, 60,
                "in zone but no active target");

            town.WorldInputNode.SetTarget(forge); // deterministic (mirrors the brief's Step 1)
            town.WorldInputNode.TriggerInteract(); // headless ActionPress proved unreliable — test seam instead

            AssertThat(raised).IsEqual("Forge");
        }
        finally
        {
            town.QueueFree();
        }
    }

    [TestCase]
    public async Task Proximity_HighlightsAndPromptsOnEnter_ClearsOnExit()
    {
        var town = Mount();
        try
        {
            town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var forge = town.FindBuilding("forge");
            town.Player.GlobalPosition = forge.DoorAnchorGlobal;

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == forge, 60,
                "in zone but no active target");

            AssertThat(forge.IsHighlighted)
                .OverrideFailureMessage("entered the forge zone but it never highlighted").IsTrue();
            AssertThat(town.WorldInputNode.PromptText).IsEqual("E · Forge");

            town.Player.GlobalPosition = new Vector3(0f, 0f, 40f); // clear of every building's interact zone

            await WaitUntil(town, () => town.WorldInputNode.ActiveTarget == null, 60,
                "left every zone but a target is still active");

            AssertThat(forge.IsHighlighted)
                .OverrideFailureMessage("left the forge zone but it stayed highlighted").IsFalse();
            AssertThat(town.WorldInputNode.PromptText).IsEqual(string.Empty);
        }
        finally
        {
            town.QueueFree();
        }
    }

    /// <summary>
    /// Regression for the primitive-wedge highlight bug: <c>BuildPrimitiveWedge</c> used to stamp
    /// its base color onto <see cref="GeometryInstance3D.MaterialOverride"/>, which renders ahead
    /// of the per-surface override <see cref="Building3D.SetHighlighted"/> installs — so the glow
    /// silently never showed on the fallback used whenever <see cref="TownAssets.BuildingScene"/>
    /// returns null. Never parented into a mounted tree (no render/physics needed to exercise a
    /// material swap), so this asserts synchronously with no frame pump and frees the node
    /// directly rather than via <c>QueueFree</c>.
    /// </summary>
    [TestCase]
    public void PrimitiveFallback_SetHighlighted_TogglesActiveMaterial()
    {
        var building = new Building3D();
        try
        {
            building.Configure("forge", "Forge", "Forge", Vector3.Zero, null); // null scene forces the wedge fallback

            var mesh = (MeshInstance3D)building.Mesh;
            AssertThat(mesh.MaterialOverride)
                .OverrideFailureMessage("fallback mesh set MaterialOverride, which shadows SetSurfaceOverrideMaterial")
                .IsNull();

            building.SetHighlighted(true);
            var onMaterial = mesh.GetActiveMaterial(0) as StandardMaterial3D;
            AssertThat(onMaterial).IsNotNull();
            AssertThat(onMaterial!.EmissionEnabled)
                .OverrideFailureMessage("SetHighlighted(true) did not enable emission on the fallback's active material")
                .IsTrue();

            building.SetHighlighted(false);
            var offMaterial = mesh.GetActiveMaterial(0) as StandardMaterial3D;
            AssertThat(offMaterial).IsNotNull();
            AssertThat(offMaterial!.EmissionEnabled)
                .OverrideFailureMessage("SetHighlighted(false) did not disable emission on the fallback's active material")
                .IsFalse();
        }
        finally
        {
            building.Free(); // never parented into a mounted tree — free it directly, no leaked orphan
        }
    }
}
#endif
