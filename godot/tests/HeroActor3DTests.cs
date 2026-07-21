#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// T7: hero actors in the standalone 3D town. Mirrors <c>CampPanelTests</c>'s
/// <c>GameFactory.NewGame(seed) with { ... }</c> fixture pattern rather than
/// <c>MountMainUi</c> (this task never touches <c>MainUi</c>). Every assertion here is
/// synchronous — no frame pump: pumping a rendering 3D <c>SubViewport</c> hangs the headless
/// gdUnit runner (<c>Town3DSceneTests</c> precedent), and every fact these tests check
/// (reconcile counts, a raised click, deterministic <c>Advance</c> positions) is already settled
/// synchronously by <see cref="Town3D.Build"/>/<see cref="HeroActor3D.Advance"/> themselves.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroActor3DTests
{
    private static Hero Alive(int id) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 20, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Hero Dead(int id) => new(
        new HeroId(id), $"Fallen{id}", "vanguard", Level: 1, MaxHp: 20, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: false, DeepestFloorReached: 0, DiedOnDay: 1);

    /// <summary>A day-1 world with 2 alive heroes + 1 dead hero (with a matching memorial stone)
    /// — some alive, some dead, per the brief's <c>MidGameWorld</c> requirement.</summary>
    private static GameState MidGameWorld()
    {
        var state = GameFactory.NewGame(2026) with
        {
            Heroes = new[] { Alive(1), Alive(2), Dead(3) }
                .ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        };

        return state with
        {
            Drama = state.Drama with
            {
                Memorials = state.Drama.Memorials.Add(new Memorial(new HeroId(3), "Fallen3", 1, "plate")),
            },
        };
    }

    private static Town3D Mount()
    {
        var town = new Town3D { Name = "Town3D" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(MidGameWorld()));
        return town;
    }

    [TestCase]
    public void Reconcile_MatchesAliveHeroes_DeadGetNoActor_MemorialStoneAdded_AndClickRaises()
    {
        var town = Mount();
        try
        {
            // Build() already calls ReconcileHeroes() once (T7: standalone scaffold) — no frame
            // pump needed, everything here is settled synchronously.
            AssertThat(town.HeroActorCount()).IsEqual(2); // Alive(1), Alive(2) only — Dead(3) excluded
            AssertThat(town.MemorialStoneCount).IsEqual(1); // one stone for Dead(3)

            var clicked = -1; // sentinel: no hero id is ever negative
            town.HeroClicked += id => clicked = id;
            var first = town.FirstHeroActor();
            first.RaisePick();
            AssertThat(clicked).IsEqual(first.HeroIdValue);
        }
        finally
        {
            town.QueueFree(); // plain QueueFree, no awaited signal (native-crash precedent)
        }
    }

    [TestCase]
    public void Determinism_TwoActorsSameConfig_IdenticalPositionsAfterSameAdvanceSequence()
    {
        var a = new HeroActor3D();
        var b = new HeroActor3D();
        try
        {
            var home = new Vector3(4f, 0f, 6f);
            a.Configure(7, "Hero7", 3, home);
            b.Configure(7, "Hero7", 3, home);

            // Identical delta sequence, no RNG, no wall-clock — same id + home must land at the
            // same GlobalPosition every step (KTD2/KTD4).
            for (var i = 0; i < 25; i++)
            {
                a.Advance(0.1);
                b.Advance(0.1);
            }

            AssertThat(a.GlobalPosition).IsEqual(b.GlobalPosition);
            AssertThat(a.State).IsEqual(b.State);
        }
        finally
        {
            a.QueueFree();
            b.QueueFree();
        }
    }
}
#endif
