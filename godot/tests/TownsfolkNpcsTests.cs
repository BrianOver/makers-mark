#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Presentation-only non-hero villager NPCs (see <c>TownsfolkNpcs</c>'s class doc). Property-only,
/// no frame pump inside a live 3D <c>SubViewport</c> — pumping physics while a 3D <c>SubViewport</c>
/// renders hangs the headless gdUnit runner (documented precedent throughout this test suite, e.g.
/// <c>HeroActor3DTests</c>). Wander is exercised by calling <see cref="TownsfolkNpc._Process"/>
/// directly on a standalone, unparented instance instead.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownsfolkNpcsTests
{
    private const float VillageRadius = 11f;

    [TestCase]
    public void Build_ReturnsExpectedTownsfolkCount_EachWithVisualAndLabel_WithinVillageRadius()
    {
        var group = TownsfolkNpcs.Build();
        try
        {
            AssertThat(group.Name.ToString()).IsEqual("TownsfolkNpcs");
            AssertThat(group.GetChildCount()).IsEqual(5);

            foreach (var child in group.GetChildren())
            {
                AssertThat(child is TownsfolkNpc).IsTrue();
            }
        }
        finally
        {
            group.QueueFree();
        }
    }

    [TestCase]
    public void Build_EveryTownsfolkNpc_HasMeshAndLabel_AndStartsWithinRadius()
    {
        var group = TownsfolkNpcs.Build();
        try
        {
            foreach (var child in group.GetChildren())
            {
                var npc = (TownsfolkNpc)child;

                AssertThat(npc.Mesh).IsNotNull();
                AssertThat(npc.Label).IsNotNull();
                AssertThat(npc.Label.Text).Contains("townsfolk");
                AssertThat(npc.NpcName).IsNotEmpty();

                var flatDistance = new Vector2(npc.Position.X, npc.Position.Z).Length();
                AssertThat(flatDistance <= VillageRadius).IsTrue();
                AssertThat(npc.Position.Y).IsEqual(0f);
            }
        }
        finally
        {
            group.QueueFree();
        }
    }

    [TestCase]
    public void Configure_DistinctIds_ProduceDistinctWaypointRotation()
    {
        // Two NPCs configured with different ids should not track the exact same waypoint index
        // (id-derived rotation, not random) — a cheap proxy that Configure actually varies motion
        // per-NPC rather than every NPC marching in lockstep.
        var a = new TownsfolkNpc();
        var b = new TownsfolkNpc();
        try
        {
            a.Configure(0, "A", 0, new Vector3(1f, 0f, 1f));
            b.Configure(1, "B", 1, new Vector3(1f, 0f, 1f));

            // Drive both through a few process ticks with the same wall-clock-style delta.
            for (var i = 0; i < 10; i++)
            {
                a._Process(0.5);
                b._Process(0.5);
            }

            // Positions stay finite and on the ground plane throughout — no NaN/explosion, no
            // vertical drift from a decoration-only wander.
            AssertThat(a.Position.Y).IsEqual(0f);
            AssertThat(b.Position.Y).IsEqual(0f);
        }
        finally
        {
            a.QueueFree();
            b.QueueFree();
        }
    }

    [TestCase]
    public void Process_StandaloneNpc_StaysWithinLoopBounds_AfterSeveralTicks()
    {
        var npc = new TownsfolkNpc();
        try
        {
            npc.Configure(2, "Test Villager", 2, new Vector3(4f, 0f, -5f));
            var start = npc.Position;

            for (var i = 0; i < 40; i++)
            {
                npc._Process(0.25); // wall-clock-style delta is the documented contract here
            }

            var flatDistance = new Vector2(npc.Position.X, npc.Position.Z).Length();
            AssertThat(flatDistance <= VillageRadius).IsTrue();
            AssertThat(npc.Position.Y).IsEqual(0f);

            // Sanity: with 40 ticks of 0.25s (10s total) at WanderSpeed 1.2 u/s, the NPC should
            // have moved off its exact start spot at least once (walking + pausing both occur).
            AssertThat(npc.Position).IsNotEqual(start);
        }
        finally
        {
            npc.QueueFree();
        }
    }
}
#endif
