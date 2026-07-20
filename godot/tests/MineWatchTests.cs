#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
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
        // AssetCatalog.HeroPortrait(classId) is a plain string lookup (IconRegistry.Lit($"hero-
        // {classId}")) with no ClassRegistry validation, so a deliberately-unregistered classId
        // exercises the "no lit art for this class" branch forever, independent of which real
        // sim classes currently have shipped art (LW-art parity shipped occultist/sentinel/
        // skirmisher's figures, which used to be this test's example -- see art/build/hero-
        // skirmisher.build.json). The per-figure graceful degrade this proves (skip that one
        // figure, don't crash) is what's under test, not any particular class's art status.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "V1", "vanguard"))
                .Add(2, Delver(2, "K1", "unshipped-test-class"));
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

    [TestCase(1024f, 2)]
    [TestCase(1900f, 3)]
    [TestCase(2560f, 4)]
    public void Backdrop_TileCountMatchesFormula_AndCoversFullWidthThroughScrollCycle(float containerWidth, int expectedTiles)
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            watch.Size = new Vector2(containerWidth, 260f);
            watch._Process(0.0); // no signal wired (repo convention) — width picked up by polling

            AssertThat(watch.BackdropTileCount).IsEqual(expectedTiles);
            AssertThat((int)Mathf.Ceil(containerWidth / MineWatch.BackdropTileWidth) + 1).IsEqual(expectedTiles);

            // Full-width coverage at many offsets across a full scroll cycle (period = tileCount *
            // tileWidth / speed) — the defect this guards: a fixed 2-tile strip left a growing
            // right-edge gap on wide windows for most of each cycle.
            const int Samples = 40;
            for (var s = 0; s < Samples; s++)
            {
                watch._Process(0.37); // odd, non-period-aligned step — exercises every phase of the cycle
                AssertCoversFullWidth(watch, containerWidth);
            }
        }
        finally
        {
            watch.Free();
        }
    }

    private static void AssertCoversFullWidth(MineWatch watch, float containerWidth)
    {
        var spans = watch.BackdropTileX
            .Select(x => (Start: x, End: x + MineWatch.BackdropTileWidth))
            .OrderBy(span => span.Start)
            .ToList();

        var covered = 0f;
        foreach (var span in spans)
        {
            var start = Mathf.Max(span.Start, covered);
            var end = Mathf.Min(span.End, containerWidth);
            if (end > start)
            {
                covered = Mathf.Max(covered, end);
            }
        }

        AssertThat(covered).IsGreaterEqual(containerWidth);
    }

    // ── U16: the in-panel journey feed (MineWatch evolves to carry it — KTD11/AE2) ─────────────

    [TestCase]
    public void CampPhase_FeedRevealsBeats_MonsterNameFromCombatEvent()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);
            watch._Process(100.0); // force full reveal — comfortably past any phase duration

            AssertThat(watch.CurrentBeats.Any(b => b.Contains("cave-rat"))).IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Clock_Paused_FeedHoldsStill_Played_ItAdvances()
    {
        // U25 follow-up (a): the feed pauses with the clock (paused != engaged — an engaged
        // surface, e.g. a drawer open over the world, keeps the feed flowing per KTD3; this test
        // covers only the Play/Pause half of that contract).
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };
            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            var clock = new PhaseClock(new SimAdapter(state));
            clock.Pause();
            watch.Clock = clock;

            watch._Process(100.0); // would force full reveal if the feed were still advancing
            AssertThat(watch.CurrentBeats.IsEmpty).IsTrue();

            clock.Play();
            watch._Process(100.0);
            AssertThat(watch.CurrentBeats.Any(b => b.Contains("cave-rat"))).IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void DeathRound_NeverAppearsInMineWatchFeed_RendersCloudInstead()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var floors = ImmutableList.Create(
                new FloorOutcome(1, false, ImmutableList.Create(
                    new CombatEvent(1, new HeroId(1), "tunnel-spider", ImmutableList.Create(1), 0, 40, false, null))));
            var result = new ExpeditionResult(
                Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 1, DeepestFloorCleared: 0, Floors: floors,
                Survivors: ImmutableList<HeroId>.Empty, Deaths: ImmutableList.Create(new HeroId(1)),
                Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);
            var state = StagedWorld() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);
            watch._Process(100.0);

            AssertThat(watch.CurrentBeats.Any(b => b.Contains("is lost from sight"))).IsTrue();
            AssertThat(watch.CurrentBeats.Any(b => b.Contains("died"))).IsFalse();
            AssertThat(watch.CurrentBeats.Any(b => b.Contains("takes 40"))).IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void SaveLoad_MidExpedition_FreshMineWatch_CloudsOnReload_NoCrash()
    {
        // KTD11: a fresh MineWatch (post-reload scene rebuild) has no memory of prior reveals —
        // the very first Refresh/Process must not throw, and nothing is revealed yet.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.CurrentBeats.IsEmpty).IsTrue(); // clouded on reload — nothing revealed yet
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

    /// <summary>Same shape as <see cref="CampedParty"/> but with a real stage-1 floor (U16 feed
    /// tests need combat data to reveal).</summary>
    private static InFlightExpedition CampedPartyWithFloors() => CampedParty() with
    {
        Floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null)))),
    };
}
#endif
