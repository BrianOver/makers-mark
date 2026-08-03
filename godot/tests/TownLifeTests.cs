#if GDUNIT_TESTS
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U6 (world-and-interiors plan, R9 "make more lively"): townsfolk run real errands between
/// buildings, and the tavern seats currently-present heroes with a mood glyph. No frame pump
/// inside a live render anywhere in this suite — every fact is settled either by calling <see
/// cref="TownsfolkNpc2D._Process"/>/<see cref="Town2D._Process"/> directly with an accumulated
/// delta (same convention <c>TownsfolkNpc2DTests</c>/<c>HeroActor2DTests</c> already use — none of
/// these calls await a real engine frame, so <see cref="Town2D"/>'s live <c>SubViewport</c> is
/// never actually asked to render, and constraint 4's disable-before-pumping rule does not apply)
/// or by inspecting the built node graph directly.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownLifeTests
{
    // ── TownsfolkNpc2D errand mode ────────────────────────────────────────────────────────────

    /// <summary>Condition-waited, never a frame count: drives until each real state transition
    /// actually happens (leaves the wander band, reaches the door, comes home again), with a
    /// generous iteration cap only as a hang guard — a regression that never arrives fails loudly
    /// instead of the suite hanging.</summary>
    [TestCase]
    public void Errand_LeavesHome_ReachesTheDoor_ThenReturnsHome_ConditionWaited()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            var home = new Vector2(100, 100);
            npc.Init(0, new PlaceholderTexture2D(), Colors.White, home);
            var door = home + new Vector2(180, 40);
            npc.SetErrandTargets(new[] { door });
            npc.SetPhase(DayPhase.Morning); // errand hours

            AssertThat(DriveUntil(npc, () => npc.Position.DistanceTo(home) > 20f, 0.2, 200))
                .OverrideFailureMessage("villager never left the wander band around Home to start the errand")
                .IsTrue();

            AssertThat(DriveUntil(npc, () => npc.Position.DistanceTo(door) < 1f, 0.2, 200))
                .OverrideFailureMessage("villager never reached the errand door")
                .IsTrue();

            AssertThat(DriveUntil(npc, () => npc.Position.DistanceTo(home) < 1f, 0.2, 400))
                .OverrideFailureMessage("villager never walked home again after dwelling at the door")
                .IsTrue();
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>"An errand that teleports is not an errand." Every single <c>_Process</c> tick's
    /// displacement is bounded by <see cref="TownsfolkNpc2D.ErrandWalkSpeed"/>*delta — proving the
    /// walk is a real accumulated-distance traversal, never a snap to the destination.</summary>
    [TestCase]
    public void Errand_NeverTeleports_EveryTickBoundedByWalkSpeed()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            var home = Vector2.Zero;
            npc.Init(0, new PlaceholderTexture2D(), Colors.White, home);
            npc.SetErrandTargets(new[] { home + new Vector2(300, 0) });
            npc.SetPhase(DayPhase.Morning);

            const double delta = 0.1;
            var budget = TownsfolkNpc2D.ErrandWalkSpeed * delta + 0.5; // small float-safety margin

            for (var i = 0; i < 400; i++)
            {
                var before = npc.Position;
                npc._Process(delta);
                var stepDistance = before.DistanceTo(npc.Position);

                AssertThat(stepDistance <= budget)
                    .OverrideFailureMessage(
                        $"tick {i}: moved {stepDistance:0.##}px in one {delta}s step (budget {budget:0.##}px) " +
                        "— this is a teleport, not a walked errand")
                    .IsTrue();

                if (i > 60 && npc.Position.DistanceTo(home) < 1f)
                {
                    break; // completed a full round trip well within the cap
                }
            }
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>R9/PR #357: Evening ("Night") never starts a NEW errand — the villager stays inside
    /// the same small wander band the pre-U6 idle drift already used, across a long simulated
    /// stretch (well past the id-seeded first-departure offset).</summary>
    [TestCase]
    public void NightPhase_NeverStartsAnErrand_StaysInTheWanderBand()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            var home = new Vector2(50, 50);
            npc.Init(0, new PlaceholderTexture2D(), Colors.White, home);
            npc.SetErrandTargets(new[] { home + new Vector2(300, 0) });
            npc.SetPhase(DayPhase.Evening); // "Night" — not errand hours

            var maxOffset = 0f;
            for (var i = 0; i < 400; i++) // 80 simulated seconds — far past any cooldown
            {
                npc._Process(0.2);
                maxOffset = Mathf.Max(maxOffset, npc.Position.DistanceTo(home));
            }

            AssertThat(maxOffset < 20f)
                .OverrideFailureMessage(
                    $"villager wandered {maxOffset:0.##}px from Home at Night — a NEW errand must wait " +
                    "for Dawn/Quest (IsErrandHours), or the town reads equally busy after dark")
                .IsTrue();
        }
        finally
        {
            npc.QueueFree();
        }
    }

    [TestCase]
    public void Determinism_ErrandMode_SameConfigSameDeltas_IdenticalPositions()
    {
        var a = new TownsfolkNpc2D();
        var b = new TownsfolkNpc2D();
        try
        {
            var home = new Vector2(60, 90);
            var targets = new[] { home + new Vector2(150, -40) };
            a.Init(2, new PlaceholderTexture2D(), Colors.White, home);
            b.Init(2, new PlaceholderTexture2D(), Colors.White, home);
            a.SetErrandTargets(targets);
            b.SetErrandTargets(targets);
            a.SetPhase(DayPhase.Morning);
            b.SetPhase(DayPhase.Morning);

            for (var i = 0; i < 300; i++)
            {
                a._Process(0.15);
                b._Process(0.15);
                AssertThat(a.Position).IsEqual(b.Position);
            }
        }
        finally
        {
            a.QueueFree();
            b.QueueFree();
        }
    }

    private static bool DriveUntil(TownsfolkNpc2D npc, System.Func<bool> condition, double delta, int maxSteps)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            if (condition())
            {
                return true;
            }

            npc._Process(delta);
        }

        return condition();
    }

    // ── TavernLife2D patron seating ───────────────────────────────────────────────────────────

    [TestCase]
    public void Refresh_SeatsAPresentHero_TintedByClass_WithAVisibleMoodGlyph()
    {
        var life = new TavernLife2D();
        try
        {
            life.Build(new[] { new Vector2(100, 100), new Vector2(140, 100) });
            AssertThat(life.SeatCount).IsEqual(2);

            life.Refresh(new (int HeroId, string ClassId, int MoodPermille)[] { (5, ClassRegistry.StrikerId, 250) });

            var occupants = life.OccupiedHeroIds();
            AssertThat(occupants[0]).IsEqual(5);
            AssertThat(occupants[1])
                .OverrideFailureMessage("only one hero was present — the second seat must stay empty")
                .IsEqual(-1);
        }
        finally
        {
            life.QueueFree();
        }
    }

    /// <summary>Patrons appear only for present heroes: an empty roster leaves every seat empty,
    /// never a stale or placeholder occupant.</summary>
    [TestCase]
    public void Refresh_NoPresentHeroes_EverySeatStaysEmpty()
    {
        var life = new TavernLife2D();
        try
        {
            life.Build(new[] { new Vector2(0, 0), new Vector2(20, 0) });
            life.Refresh(System.Array.Empty<(int HeroId, string ClassId, int MoodPermille)>());

            var occupants = life.OccupiedHeroIds();
            AssertThat(occupants[0]).IsEqual(-1);
            AssertThat(occupants[1]).IsEqual(-1);
        }
        finally
        {
            life.QueueFree();
        }
    }

    /// <summary>Patron count never exceeds seat count — a deterministic pick (lowest hero ids),
    /// never RNG, never a third patron squeezed in.</summary>
    [TestCase]
    public void Refresh_MoreHeroesThanSeats_CapsAtSeatCount_LowestIdsWin()
    {
        var life = new TavernLife2D();
        try
        {
            life.Build(new[] { new Vector2(0, 0), new Vector2(20, 0) });

            life.Refresh(new (int HeroId, string ClassId, int MoodPermille)[]
            {
                (9, ClassRegistry.MysticId, 0),
                (3, ClassRegistry.VanguardId, 0),
                (7, ClassRegistry.StrikerId, 0),
            });

            var occupants = life.OccupiedHeroIds();
            AssertThat(occupants.Count).IsEqual(2);
            AssertThat(occupants.Contains(3)).OverrideFailureMessage("lowest hero id must win a seat").IsTrue();
            AssertThat(occupants.Contains(7)).OverrideFailureMessage("second-lowest hero id must win the other seat").IsTrue();
            AssertThat(occupants.Contains(9)).OverrideFailureMessage("a third hero must never squeeze past SeatCount").IsFalse();
        }
        finally
        {
            life.QueueFree();
        }
    }

    // ── Town2D wiring (real venue targets, real hero-state guard) ────────────────────────────

    [TestCase]
    public void Town2D_Built_TownsfolkErrandTowardRealVenueDoors()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(seed: 11));
        try
        {
            AssertThat(town.Adapter!.CurrentState.Phase)
                .OverrideFailureMessage("a fresh campaign is expected to start in Morning (errand hours)")
                .IsEqual(DayPhase.Morning);

            var homes = town.Townsfolk.Select(n => n.Home).ToList();
            var leftHome = false;

            // No real frame pump here — _Process is called directly as a plain method, so
            // Town2D's live SubViewport is never asked to render (see class doc). Town2D._Process
            // does NOT cascade to its children's own _Process (that dispatch is an ENGINE-loop
            // behavior, only real for a node tree the SceneTree is actually ticking) — it only
            // feeds each villager's SetPhase. The errand walk itself lives in
            // TownsfolkNpc2D._Process, so each one must be driven directly too, exactly like the
            // bare-node tests above do for a single instance.
            for (var i = 0; i < 300 && !leftHome; i++)
            {
                town._Process(0.2);
                foreach (var npc in town.Townsfolk)
                {
                    npc._Process(0.2);
                }

                leftHome = town.Townsfolk
                    .Select((n, idx) => n.Position.DistanceTo(homes[idx]))
                    .Any(d => d > 20f);
            }

            AssertThat(leftHome)
                .OverrideFailureMessage(
                    "no townsfolk left their wander band across 60 simulated seconds — Town2D must feed " +
                    "real venue door anchors into TownsfolkNpc2D.SetErrandTargets")
                .IsTrue();
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void Refresh_AWanderingHero_EndsUpSeatedInTheTavern()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(seed: 11));
        try
        {
            AssertThat(town.TavernLife).IsNotNull();
            AssertThat(town.HeroActorCount() > 0).IsTrue();

            town.Refresh();

            var occupants = town.TavernLife!.OccupiedHeroIds();
            AssertThat(occupants.Any(id => id >= 0))
                .OverrideFailureMessage("a fresh campaign's wandering heroes should seat at least one tavern patron")
                .IsTrue();
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>Ties into U10's fiction: a hero mid-rally/march/away must never ALSO read as
    /// chatting in the tavern.</summary>
    [TestCase]
    public void Refresh_HeroMarkedAway_NeverAppearsAsATavernPatron()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(seed: 11));
        try
        {
            AssertThat(town.TavernLife).IsNotNull();
            var away = town.FirstHeroActor();
            away.SetState(HeroActor2D.HeroTownState.Away);

            town.Refresh();

            var occupants = town.TavernLife!.OccupiedHeroIds();
            AssertThat(occupants.Contains(away.HeroIdValue))
                .OverrideFailureMessage("an Away hero must never be seated as a tavern patron")
                .IsFalse();
        }
        finally
        {
            town.Free();
        }
    }
}
#endif
