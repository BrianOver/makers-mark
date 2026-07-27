#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The 3 gameplay hero classes (Vanguard, Striker, Mystic) now have class-distinctive AI-gen 3D
/// meshes, chosen by <c>HeroActor3D.BuildMesh</c> over the generic Kenney variant. Asserts each
/// class resolves + loads its mesh, and a class with no gen mesh still returns null (so the Kenney
/// fallback path stays intact).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroClassMeshTests
{
    [TestCase("vanguard")]
    [TestCase("striker")]
    [TestCase("mystic")]
    public void HeroClass_ResolvesToGenMesh_ThatLoads(string classId)
    {
        var mesh = TownAssets.InstantiateGenHero(classId);
        AssertThat(mesh).IsNotNull();
        mesh!.QueueFree();
    }

    [TestCase("")]
    [TestCase("occultist")] // portrait exists but no gameplay class / gen mesh — Kenney fallback
    public void HeroClass_NoGenMesh_ResolvesNull_KeepsKenneyFallback(string classId)
    {
        AssertThat(TownAssets.InstantiateGenHero(classId)).IsNull();
    }
}
#endif
