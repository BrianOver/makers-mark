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
    [TestCase("The Forgeworm", "monster-forgeworm.glb")]
    [TestCase("Bog-Wight", "monster-bog-wight.glb")]
    [TestCase("Choir of Teeth", "monster-choir-of-teeth.glb")]
    [TestCase("Reliquary Mimic", "monster-reliquary-mimic.glb")]
    [TestCase("The Undertow", "monster-undertow.glb")]
    [TestCase("Molten Archivist", "monster-molten-archivist.glb")]
    [TestCase("Slag Hound", "monster-slag-hound.glb")]
    [TestCase("The Bellows-Mad", "monster-bellows-mad.glb")]
    [TestCase("The Undying Forge-Heart", "monster-undying-forge-heart.glb")]
    public void VenueMonster_ResolvesToGenMesh_ThatLoads(string kind, string expectedFile)
    {
        var file = AssetCatalog.MonsterModelFile(kind);
        AssertThat(file).IsEqual(expectedFile);

        var mesh = TownAssets.InstantiateGen(file!);
        AssertThat(mesh).IsNotNull();

        mesh!.QueueFree();
    }
}
#endif
