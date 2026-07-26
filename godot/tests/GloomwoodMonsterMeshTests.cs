#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-C4 Gloomwood completeness: the two deep bosses (Wicker Shepherd F3, Old Mossjaw F4) now have
/// AI-gen 3D meshes, so the whole Gloomwood monster set resolves to a real GLB (no 2D-portrait
/// fallback). Asserts the sim MonsterKind display names slug to the mapped files and the meshes
/// actually load from res://assets/models/gen/.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GloomwoodMonsterMeshTests
{
    [TestCase("The Wicker Shepherd", "monster-wicker-shepherd.glb")]
    [TestCase("Old Mossjaw", "monster-old-mossjaw.glb")]
    public void GloomwoodBoss_ResolvesToGenMesh_ThatLoads(string kind, string expectedFile)
    {
        var file = AssetCatalog.MonsterModelFile(kind);
        AssertThat(file).IsEqual(expectedFile);

        var mesh = TownAssets.InstantiateGen(file!);
        AssertThat(mesh).IsNotNull();

        mesh!.QueueFree();
    }
}
#endif
