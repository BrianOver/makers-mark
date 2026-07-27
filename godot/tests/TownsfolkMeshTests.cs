#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The market-square townsfolk now have bespoke AI-gen meshes (baker/elder/weaver/tanner) via
/// TownsfolkNpcs' roster, chosen over the generic Kenney figure by TownAssets.InstantiateGenCharacter.
/// Asserts each resolves + loads, and that the shared loader returns null for a missing file (so the
/// Kenney fallback for Little Pib and any un-gen'd NPC stays intact).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownsfolkMeshTests
{
    [TestCase("townsfolk-baker.glb")]
    [TestCase("townsfolk-elder.glb")]
    [TestCase("townsfolk-weaver.glb")]
    [TestCase("townsfolk-tanner.glb")]
    public void Townsperson_GenMesh_Loads(string fileName)
    {
        var mesh = TownAssets.InstantiateGenCharacter(fileName);
        AssertThat(mesh).IsNotNull();
        mesh!.QueueFree();
    }

    [TestCase]
    public void GenCharacter_MissingFile_ResolvesNull_KeepsKenneyFallback()
    {
        AssertThat(TownAssets.InstantiateGenCharacter("no-such-townsperson.glb")).IsNull();
    }

    [TestCase]
    public void TownsfolkGroup_Builds_WithoutThrowing()
    {
        var group = TownsfolkNpcs.Build();
        AssertThat(group).IsNotNull();
        group.QueueFree();
    }
}
#endif
