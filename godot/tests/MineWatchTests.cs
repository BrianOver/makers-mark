#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// LW5 depths watch: the lit strip mounted above <see cref="DepthsPanel"/>'s venue grid.
/// Covers the four contracted scenarios (plan §LW5) — visibility per phase, the march/camp
/// state machine (including the party cache surviving phases that emit no fresh
/// <see cref="PartyDeparted"/>), the graceful-degrade path, and that
/// <see cref="VenueHubTests"/>' pre-LW5 asserts on <c>DepthsPanel</c> stay green. Most scenarios
/// drive <see cref="MineWatch"/> directly (its own public <see cref="MineWatch.Refresh"/>) —
/// deterministic and RNG-free, unlike ticking a real expedition through combat — mirroring
/// <c>TownSceneTests</c>' standalone <c>LitOverlay_MissingAsset_DegradesToNoSpriteNoCrash</c>
/// technique for the degrade path.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MineWatchTests
{
    [TestCase]
    public void Morning_StripHidden_DepthsPanelVenueTileStillRenders()
    {
        // Integration: through the real DepthsPanel/MainUi wiring, at the fresh campaign's
        // starting phase (Morning) — proves the LW5 wiring didn't disturb VenueHubTests' contract.
        var ui = MountMainUi(new SimAdapter(StagedWorld()));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            var watch = ui.Depths.Watch;
            AssertThat(watch).IsNotNull();
            AssertThat(watch!.State).IsEqual(MineWatch.WatchState.Hidden);
            AssertThat(watch.Visible).IsFalse();
            AssertThat(watch.CustomMinimumSize).IsEqual(Vector2.Zero);

            AssertThat(RenderedText(ui.Depths)).Contains("The Mine");
            AssertThat(ui.Depths.FindChildren("VenueTile_mine", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Marching_CachesPartyAcrossPhases_HiddenClearsIt()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld();
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2));

            // Expedition tick just fired PartyDeparted — marches with the known party.
            watch.Refresh(state with { Phase = DayPhase.Expedition }, departed);
            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Marching);
            AssertThat(watch.Visible).IsTrue();
            AssertThat(watch.CustomMinimumSize.Y).IsGreater(0f);
            AssertThat(watch.FigureCount).IsEqual(3);

            // ExpeditionDeep tick: no fresh PartyDeparted (Camp/Deep ticks never emit one) — the
            // cached party keeps marching.
            watch.Refresh(state with { Phase = DayPhase.ExpeditionDeep }, ImmutableList<GameEvent>.Empty);
            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Marching);
            AssertThat(watch.FigureCount).IsEqual(3);

            // Evening: the day exits the live window — hidden, cache cleared.
            watch.Refresh(state with { Phase = DayPhase.Evening }, ImmutableList<GameEvent>.Empty);
            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Hidden);
            AssertThat(watch.Visible).IsFalse();
            AssertThat(watch.FigureCount).IsEqual(0);

            // Next day's Expedition phase, no PartyDeparted seen yet — marching resumes empty
            // (ambient-only), never stale-shows yesterday's party.
            watch.Refresh(state with { Phase = DayPhase.Expedition }, ImmutableList<GameEvent>.Empty);
            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Marching);
            AssertThat(watch.FigureCount).IsEqual(0);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void CampPhase_ReadsInFlightHp_LowHpHeroSlumps_FullHpHeroUpright()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedParty();
            var state = StagedWorld() with
            {
                Phase = DayPhase.Camp,
                InFlight = ImmutableList.Create(camp),
            };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Camped);
            AssertThat(watch.Visible).IsTrue();
            AssertThat(watch.FigureCount).IsEqual(3);

            // Hero 3 (index 2, 5/40 hp — well under the slump threshold) slumps; Hero 1
            // (index 0, full hp) stays upright.
            var slumped = Find<Sprite2D>(watch, "MineHero_2");
            AssertThat(slumped.RotationDegrees).IsGreater(0f);
            var upright = Find<Sprite2D>(watch, "MineHero_0");
            AssertThat(upright.RotationDegrees).IsEqual(0f);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void MissingBackdrop_DegradesWholeStrip_HiddenRegardlessOfPhase()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build("does-not-exist-in-any-manifest"); // injectable degrade path (LitTownOverlay precedent)
            AssertThat(watch.HasContent).IsFalse();

            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2));
            watch.Refresh(StagedWorld() with { Phase = DayPhase.Expedition }, departed);

            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Hidden);
            AssertThat(watch.Visible).IsFalse();
            AssertThat(watch.CustomMinimumSize).IsEqual(Vector2.Zero);
            AssertThat(watch.FigureCount).IsEqual(0);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void MarchingParty_UnshippedClassArt_SkipsThatFigureOnly_NoCrash()
    {
        // "skirmisher" is a real registered class (GameSim.Classes.Skirmisher) with no committed
        // lit art yet (LW-art's occultist/sentinel/skirmisher figures are a separate in-flight
        // unit) — the per-figure graceful degrade this proves is independent of that unit landing.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "V1", "vanguard"))
                .Add(2, Delver(2, "K1", "skirmisher"));
            var state = GameFactory.NewGame(9099) with { Heroes = heroes };
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2)), 2));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, departed);

            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Marching);
            AssertThat(watch.FigureCount).IsEqual(1); // only the vanguard resolved
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void FloorRecordEvent_FlashesMonsterSlideAndBark_EvenOutsideLivePhase()
    {
        // Confirmed against GameSim.Drama.ExpeditionRevealSystem (type remarks): FloorRecordSet
        // fires ONLY at the Evening tick, by which point Phase has already rolled to next-day
        // Morning — outside the live gate. The milestone flash is the deliberate exception.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with { Phase = DayPhase.Morning };
            var events = ImmutableList.Create<GameEvent>(new FloorRecordSet(new HeroId(1), 3));

            watch.Refresh(state, events);

            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Hidden); // march/camp gate stays closed...
            AssertThat(watch.Visible).IsTrue();                          // ...but the flash forces a brief show
            AssertThat(watch.CustomMinimumSize.Y).IsGreater(0f);
        }
        finally
        {
            watch.Free();
        }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Hero Delver(int id, string name, string classId, int deepestFloor = 1) => new(
        new HeroId(id), name, classId, Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepestFloor, DiedOnDay: null);

    /// <summary>Three shipped-art classes, each already a floor deep — <c>ExpeditionSystem</c>
    /// would stage (not finalize) their next run, but no test here actually ticks combat; this is
    /// just a stable, real-shaped <see cref="GameState"/> for <see cref="MineWatch.Refresh"/> to read.</summary>
    private static GameState StagedWorld()
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "V1", "vanguard"))
            .Add(2, Delver(2, "S1", "striker"))
            .Add(3, Delver(3, "M1", "mystic"));
        return GameFactory.NewGame(9098) with { Heroes = heroes };
    }

    private static InFlightExpedition CampedParty() => new(
        Party: ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)),
        TargetFloor: 2,
        CheckpointFloor: 1,
        VenueId: "mine",
        Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 40).Add(2, 30).Add(3, 5),
        Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
        Gold: ImmutableSortedDictionary<int, int>.Empty,
        Dead: ImmutableSortedSet<int>.Empty,
        Floors: ImmutableList<FloorOutcome>.Empty,
        Loot: ImmutableList<OreLoot>.Empty,
        DeepestFloorCleared: 1);
}
#endif
