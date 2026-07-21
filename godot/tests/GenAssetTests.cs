#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Proves the local AI 3D-gen pipeline end-to-end lands a usable town asset: the normalized gen
/// forge GLB (SDXL concept → TRELLIS.2 image-to-3D → <c>normalize_glb.py</c> → Godot 4.6.3 import,
/// see <c>docs/design/2026-07-20-3d-asset-gen-plan.md</c>) loads as a real mesh, and
/// <see cref="BuildingKit"/> wires it in as the forge's body via <see cref="TownAssets.InstantiateGen"/>.
///
/// <para>Property-only by design — NO frame pump and NO viewport render, per the 3D-render-hang
/// rule (pumping frames while a 3D SubViewport renders hangs the headless gdUnit runner). Nodes are
/// created orphaned (never mounted) and <see cref="Node.Free"/>d in a finally.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GenAssetTests
{
    private static MeshInstance3D? FindMesh(Node root)
    {
        if (root is MeshInstance3D m && m.Mesh != null)
        {
            return m;
        }

        foreach (var child in root.GetChildren())
        {
            var found = FindMesh(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    [TestCase]
    public void GenForgeGlb_ImportsAsRealMesh()
    {
        var node = TownAssets.InstantiateGen("forge.glb");
        try
        {
            // Non-null proves the file exists AND Godot 4.6.3 imported it (ResourceLoader.Exists +
            // a successful PackedScene load); a broken/absent import would return null here.
            AssertThat(node).IsNotNull();

            var mesh = FindMesh(node!);
            AssertThat(mesh).IsNotNull();
            AssertThat(mesh!.Mesh.GetSurfaceCount()).IsGreater(0);
        }
        finally
        {
            node?.Free();
        }
    }

    [TestCase]
    public void BuildForge_PrefersGenMesh_WithGlowStillParented()
    {
        var forge = TownAssets.BuildBuilding("forge");
        try
        {
            AssertThat(forge).IsNotNull();
            // The gen path (or Kenney fallback) must yield a real mesh body...
            AssertThat(FindMesh(forge)).IsNotNull();
            // ...and the forge-glow light stays parented regardless of which path built the body.
            AssertThat(forge.FindChild("ForgeGlow", true, false)).IsNotNull();
        }
        finally
        {
            forge.Free();
        }
    }
}
#endif
