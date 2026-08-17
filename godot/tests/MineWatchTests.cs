#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Panels;
using GodotClient.Tools;
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
/// deterministic and RNG-free, unlike ticking a real expedition through combat — the same
/// standalone-degrade-path technique used elsewhere in this suite.
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
            // U9 (KTD-4): the shared instance lives on MainUi now — ui.Depths.Watch also resolves
            // it (Depths is the resting host), see MineWatchRehostTests for that contract directly.
            var watch = ui.Watch;
            AssertThat(watch).IsNotNull();
            AssertThat(ReferenceEquals(ui.Depths.Watch, watch)).IsTrue();
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
        EngineDistress.ResetForTests();
        var watch = new MineWatch();
        try
        {
            watch.Build("does-not-exist-in-any-manifest"); // injectable degrade path
            AssertThat(watch.HasContent).IsFalse();

            // U1 (loud-failures-and-quiet-channels plan): the runtime half of the fix — a missing
            // backdrop must say so somewhere a real playtest run's own anomaly report
            // (EngineLogAnomalies.Scan) can see, not only a pre-merge census test. Before this unit
            // HasContent going false was entirely silent.
            AssertThat(EngineDistress.Messages.Any(m => m.Contains("does-not-exist-in-any-manifest")))
                .OverrideFailureMessage(
                    $"Build() set HasContent=false for a missing backdrop but recorded no "
                    + $"EngineDistress message. Recorded: [{string.Join(" | ", EngineDistress.Messages)}]")
                .IsTrue();

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

    /// <summary>The healthy path must not change: a resolvable backdrop (the real committed
    /// "mine-backdrop") still reports <see cref="MineWatch.HasContent"/> true and records no
    /// distress message — the guard above must only fire on an actual miss.</summary>
    [TestCase]
    public void ResolvableBackdrop_HasContentTrue_NoDistressMessage()
    {
        EngineDistress.ResetForTests();
        var watch = new MineWatch();
        try
        {
            watch.Build(); // the real committed default: AssetCatalog.VenueBackdropId("mine")

            AssertThat(watch.HasContent).IsTrue();
            AssertThat(EngineDistress.Messages.Any(m => m.Contains("mine-backdrop")))
                .OverrideFailureMessage(
                    $"A resolvable backdrop should never trip the missing-backdrop warning. "
                    + $"Recorded: [{string.Join(" | ", EngineDistress.Messages)}]")
                .IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

    // ── U-T5-8 ("the watch stops being blurry, identical, and outcome-blind") ──────────────────

    [TestCase]
    public void Viewport_PinsNearestTextureFilter()
    {
        // MineWatch's SubViewport used to be the one 2D surface in the game NOT forced to nearest-
        // neighbour (Town2D.cs/UiKit.cs/ForgeMinigame.cs/AlchemyBrewPuzzle.cs all pin it explicitly)
        // -- Godot 4 defaults a fresh Viewport to bilinear, so pixel art drawn at 3-6x (ScaleToWidth)
        // read as a blur here specifically. Pinned as a regression tripwire so it can never silently
        // revert.
        var watch = new MineWatch();
        try
        {
            watch.Build();

            var viewport = Find<SubViewport>(watch, "MineViewport");

            AssertThat(viewport.CanvasItemDefaultTextureFilter)
                .OverrideFailureMessage("MineWatch's SubViewport is not pinned to Nearest -- pixel art here will read as a blur.")
                .IsEqual(Viewport.DefaultCanvasItemTextureFilter.Nearest);
        }
        finally
        {
            watch.Free();
        }
    }

    // ── U9 (world-and-interiors plan, KTD-4): venue-true backdrop ──────────────────────────────
    // MineWatch.cs used to hardcode "mine" (KTD-4's own diagnosis); the backdrop now follows
    // whichever venue the tracked party actually raided.

    [TestCase]
    public void Refresh_NoPartyYet_BackdropStaysMine()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            AssertThat(watch.BackdropVenueId).IsEqual("mine");

            // Neither InFlight nor PendingExpeditions has a party to read a venue from.
            watch.Refresh(StagedWorld() with { Phase = DayPhase.Morning }, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.BackdropVenueId)
                .OverrideFailureMessage("Backdrop swapped with no party in InFlight/PendingExpeditions to read a venue from.")
                .IsEqual("mine");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Refresh_InFlightGloomwoodParty_SwapsBackdropToGloomwood()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedParty() with { VenueId = "gloomwood" };
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.BackdropVenueId).IsEqual("gloomwood");
            AssertThat(watch.HasContent)
                .OverrideFailureMessage(
                    "gloomwood-backdrop is a committed art id (art-manifest.json) -- it must resolve, not degrade.")
                .IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Refresh_ResolvedSunkenCryptParty_SwapsBackdrop_ThroughTheArtIdMapping()
    {
        // AssetCatalog.VenueArtId maps the sim id "sunken-crypt" to the committed art id
        // "sunkencrypt" (a past silent-fallback bug, per that method's own doc) -- BackdropVenueId
        // reports the SIM id (what ResolveVenueId read straight off ExpeditionResult.VenueId),
        // while HasContent proves the ART id actually resolved through that mapping.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var result = new ExpeditionResult(
                Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 1, DeepestFloorCleared: 1,
                Floors: ImmutableList<FloorOutcome>.Empty, Survivors: ImmutableList.Create(new HeroId(1)),
                Deaths: ImmutableList<HeroId>.Empty, Beats: ImmutableList<AttributionBeat>.Empty,
                Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty,
                VenueId: "sunken-crypt");
            var state = StagedWorld() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.BackdropVenueId).IsEqual("sunken-crypt");
            AssertThat(watch.HasContent).IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    // ── repo fix/u8-mount-the-unmounted-props: venue props are MOUNTED, not merely resolvable ──
    // ArtWiringCoverageTests.VenueProps_ResolveWithNormal already proves gloomwood-mushroom-
    // cluster/gloomwood-toll-booth/sunkencrypt-donation-plate resolve to a texture through
    // IconRegistry.Lit. That is a different claim from "something draws it" -- these three
    // committed, normal-mapped props sat generated-but-unmounted (no scene ever instantiated
    // them) until MineWatch.ApplyVenueProps, and the coverage test above could not have caught
    // that gap because it never asked whether a scene drew anything. These tests walk the LIVE
    // scene tree (Find<Sprite2D>/FindChild), the same technique CampPhase_ReadsInFlightHp... uses
    // to prove a hero figure is actually there, not just that BuildFigureSprite didn't throw.

    [TestCase]
    public void Refresh_InFlightGloomwoodParty_MountsBothGloomwoodProps()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedParty() with { VenueId = "gloomwood" };
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            var mushroomCluster = Find<Sprite2D>(watch, "VenueProp_gloomwood-mushroom-cluster");
            AssertThat(mushroomCluster.Texture).IsNotNull();
            AssertThat(mushroomCluster.Visible).IsTrue();

            var tollBooth = Find<Sprite2D>(watch, "VenueProp_gloomwood-toll-booth");
            AssertThat(tollBooth.Texture).IsNotNull();
            AssertThat(tollBooth.Visible).IsTrue();

            // Sunken Crypt's own prop must never leak into a Gloomwood run.
            AssertThat(watch.FindChild("VenueProp_sunkencrypt-donation-plate", recursive: true, owned: false))
                .IsNull();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Refresh_ResolvedSunkenCryptParty_MountsDonationPlate_ThroughTheArtIdMapping()
    {
        // Same art-id-mapping subtlety as Refresh_ResolvedSunkenCryptParty_SwapsBackdrop above
        // (sim id "sunken-crypt", committed art id "sunkencrypt-donation-plate") -- the prop must
        // mount through the very same VenuePropIdsByVenueId key BackdropVenueId reports, proving
        // the two tables agree.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var result = new ExpeditionResult(
                Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 1, DeepestFloorCleared: 1,
                Floors: ImmutableList<FloorOutcome>.Empty, Survivors: ImmutableList.Create(new HeroId(1)),
                Deaths: ImmutableList<HeroId>.Empty, Beats: ImmutableList<AttributionBeat>.Empty,
                Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty,
                VenueId: "sunken-crypt");
            var state = StagedWorld() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            var donationPlate = Find<Sprite2D>(watch, "VenueProp_sunkencrypt-donation-plate");
            AssertThat(donationPlate.Texture).IsNotNull();
            AssertThat(donationPlate.Visible).IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Refresh_VenueSwapsAway_UnmountsThePreviousVenuesProps()
    {
        // Regression guard for ApplyVenueProps' own clear-then-rebuild contract, mirroring
        // Refresh_VenueUnchanged_NeverRebuildsBackdropTiles below for the backdrop itself -- a
        // party's venue props must not survive into the NEXT venue's strip (the Mine owns none).
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var gloomwoodCamp = CampedParty() with { VenueId = "gloomwood" };
            var gloomwoodState = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(gloomwoodCamp) };
            watch.Refresh(gloomwoodState, ImmutableList<GameEvent>.Empty);
            AssertThat(watch.FindChild("VenueProp_gloomwood-mushroom-cluster", recursive: true, owned: false))
                .IsNotNull();

            var mineCamp = CampedParty(); // VenueId "mine" -- owns no props
            var mineState = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(mineCamp) };
            watch.Refresh(mineState, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.FindChild("VenueProp_gloomwood-mushroom-cluster", recursive: true, owned: false))
                .OverrideFailureMessage("Gloomwood's mushroom cluster survived a venue swap back to the Mine.")
                .IsNull();
            AssertThat(watch.FindChild("VenueProp_gloomwood-toll-booth", recursive: true, owned: false))
                .OverrideFailureMessage("Gloomwood's toll booth survived a venue swap back to the Mine.")
                .IsNull();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Refresh_VenueUnchanged_NeverRebuildsBackdropTiles()
    {
        // Regression guard for ApplyVenueBackdrop's own contract ("only pay for a rebuild when the
        // raided venue actually changes") -- repeated same-venue Refresh calls must not reset the
        // backdrop's scroll position every tick.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedParty(); // VenueId "mine" -- same as the strip's own default
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);
            watch._Process(0.5); // let the backdrop scroll a bit
            var xBefore = watch.BackdropTileX.ToList();

            watch.Refresh(state, ImmutableList<GameEvent>.Empty); // same venue, second tick

            AssertThat(watch.BackdropTileX.SequenceEqual(xBefore))
                .OverrideFailureMessage("A same-venue Refresh rebuilt the backdrop tiles, resetting their scroll position.")
                .IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void TwoHeroesOfTheSameClass_ResolveDifferentBodies()
    {
        // U-T5-8 ("every hero of a class is the same person"): ResolveWalkFrames now resolves the
        // body through ArtVariants.Pick keyed on the hero's OWN id -- the same call
        // TownAssets2D.HeroBodyId already makes for the town plaza -- so two heroes sharing a class
        // must NOT wear the same one of the 5 committed bodies (base + -v2..-v5). The class list is
        // read straight off the real recruit registry, never a hand-listed array, and the pair of
        // hero ids is DISCOVERED (not assumed) by scanning ArtVariants.Pick itself, so this fails
        // loudly rather than passing on a lucky coincidence if the committed variant art ever
        // changes shape.
        var (classId, heroA, heroB) = FindTwoHeroIdsWithDifferentBodies();

        var watch = new MineWatch();
        try
        {
            watch.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(heroA, Delver(heroA, "A", classId))
                .Add(heroB, Delver(heroB, "B", classId));
            var state = GameFactory.NewGame(9111) with { Heroes = heroes };
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(heroA), new HeroId(heroB)), 2));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, departed);

            var spriteA = Find<Sprite2D>(watch, "MineHero_0");
            var spriteB = Find<Sprite2D>(watch, "MineHero_1");

            AssertThat(spriteA.Texture)
                .OverrideFailureMessage(
                    $"Two heroes of the same class ('{classId}', ids {heroA}/{heroB}) resolved the SAME " +
                    "body texture -- ResolveWalkFrames must call ArtVariants.Pick keyed on the hero's " +
                    "own id, not just the class id.")
                .IsNotEqual(spriteB.Texture);
        }
        finally
        {
            watch.Free();
        }
    }

    /// <summary>Scans <see cref="GameSim.Classes.ClassRegistry.RecruitPool"/> (the real registry,
    /// never a hand-listed class array) for a class + hero-id pair whose <see
    /// cref="ArtVariants.Pick"/> resolution differs, so the test above exercises an actually-proven
    /// pair rather than an assumed one.</summary>
    private static (string ClassId, int HeroA, int HeroB) FindTwoHeroIdsWithDifferentBodies()
    {
        foreach (var classId in GameSim.Classes.ClassRegistry.RecruitPool)
        {
            var baseId = $"town2d-hero-{classId}";
            string? first = null;
            var firstId = 0;
            for (var heroId = 1; heroId <= 64; heroId++)
            {
                var bodyId = ArtVariants.Pick(baseId, "hero", heroId);
                if (first is null)
                {
                    first = bodyId;
                    firstId = heroId;
                    continue;
                }

                if (bodyId != first)
                {
                    return (classId, firstId, heroId);
                }
            }
        }

        throw new InvalidOperationException(
            "No class in ClassRegistry.RecruitPool resolved more than one body variant across 64 " +
            "sampled hero ids -- either the committed art variant pool collapsed to 1, or ArtVariants " +
            "stopped varying by hero id.");
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

    // ── chore/kill-3d-residue: DenThreatShifted rides the same milestone flash ──────────────────
    // (DirectorSystem.TickDens writes VenueState.ThreatTier/Closed every Morning; before this,
    // nothing in the client ever read the DenThreatShifted EVENT itself — DepthsPanel shows the
    // steady-state tier, but the moment it changes was silent. MineWatch is the natural home: it
    // already owns the milestone-flash mechanism and is Mine-scoped like the event filter below.)

    [TestCase]
    public void DenThreatShifted_Mine_FlashesBarkWithTier_EvenOutsideLivePhase()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with { Phase = DayPhase.Morning };
            var events = ImmutableList.Create<GameEvent>(
                new DenThreatShifted("mine", ThreatPermille: 260, ThreatTier: 1, Lockdown: false));

            watch.Refresh(state, events);

            AssertThat(watch.Visible).IsTrue(); // flash forces a brief show, same as FloorRecordSet
            var bark = Find<Label>(watch, "RecordBark");
            AssertThat(bark.Visible).IsTrue();
            AssertThat(bark.Text).Contains("tier 1");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void DenThreatShifted_Lockdown_BarksTheOverrunMessage()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with { Phase = DayPhase.Morning };
            var events = ImmutableList.Create<GameEvent>(
                new DenThreatShifted("mine", ThreatPermille: 1000, ThreatTier: 3, Lockdown: true));

            watch.Refresh(state, events);

            var bark = Find<Label>(watch, "RecordBark");
            AssertThat(bark.Text).Contains("locked down");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void DenThreatShifted_OtherVenue_NeverFlashesTheMineStrip()
    {
        // MineWatch is the Mine's OWN spectate strip — a Gloomwood den shift is Gloomwood's story,
        // not the Mine's; DepthsPanel's Gloomwood tile is where that steady-state tier already
        // shows (U-C4). No filter leak: the strip must stay fully hidden, same as no event at all.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with { Phase = DayPhase.Morning };
            var events = ImmutableList.Create<GameEvent>(
                new DenThreatShifted("gloomwood", ThreatPermille: 260, ThreatTier: 1, Lockdown: false));

            watch.Refresh(state, events);

            AssertThat(watch.Visible).IsFalse();
            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Hidden);
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
    public void ExpeditionPhase_PartyDeparted_FeedShowsRoster_SlateNamesCraftedGear()
    {
        // U-EXP1 (Expedition-watchable, owner-flagged twice: "the player just sits there"):
        // before that unit, EVERY tick of the entire Expedition phase rendered nothing here but a
        // content-free "Rumor has it a party sets out for floor N…" placeholder — no name, no
        // gear, the whole premise's payoff ("the gear you forged, out in the world") invisible for
        // the one phase the player is actually watching a raid depart.
        //
        // U2 (the send-off unit) split this in two: the crafted item used to live in THIS same
        // feed label (capped, and swept away by combat beats once Camp started — "burial, not
        // ceremony") and now lives on the departure slate instead, uncapped and persistent for as
        // long as the party is tracked (see DepartureSlate_* tests below for that surface's own
        // coverage). This test now pins only what the scrolling feed still owns: the roll call.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var weapon = new ItemId(1);
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", "vanguard") with { Gear = new GearSet(weapon, null, null) });
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(weapon, "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty));
            var state = GameFactory.NewGame(9098) with { Heroes = heroes, Items = items };
            var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 2, VenueId: "mine");
            var events = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1)), 2),
                new PartiesFormed(ImmutableList.Create(plan)));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, events);

            AssertThat(watch.State).IsEqual(MineWatch.WatchState.Marching); // figures still march (unchanged)
            var feed = Find<Label>(watch, "JourneyFeedLabel");
            AssertThat(feed.Visible).IsTrue();
            AssertThat(feed.Text).Contains("Torvald");

            // The crafted item is no longer part of the scrolling feed — it is the departure
            // slate's whole job now (RenderedText reaches it regardless of SubViewport nesting;
            // DepartureSlate_EscapesTheSubViewportBoundary below pins that it ALSO reaches a
            // production playtest walker, not just this test helper).
            AssertThat(feed.Text.Contains("Fine Iron Blade")).IsFalse();
            AssertThat(watch.DepartureSlateLines.Single()).IsEqual("Torvald carries your Fine Iron Blade.");
        }
        finally
        {
            watch.Free();
        }
    }

    // ── U2 (the send-off unit): the departure slate ─────────────────────────────────────────────
    // The naive version of "at departure the slate names that hero and that item" already passed
    // before this unit (single hero, single item never hits any cap). These pin the three real
    // deltas the plan called out: the FeedVisibleLines-1 cap silently dropping a party's 3rd
    // carried item, an honest empty state where the old code had none, and that the slate
    // actually escapes MineWatch's own SubViewport boundary (ScreenObservation.Descendants stops
    // at one) so a production playtest walker — not just this test file's own Find<T>/RenderedText
    // helpers, which never respected that boundary either way — can read it.

    [TestCase]
    public void ExpeditionPhase_PartyOfThree_EachCarryingCraftedGear_AllThreeNamedOnSlate()
    {
        // THE failing-first test for this unit. The primary assertion below (RenderedText, an
        // existing helper that walks the WHOLE node tree, slate or no slate) is deliberately
        // written against API that already existed pre-change, so it is checkable by inspection
        // against the actual pre-change source rather than only against a hook this unit adds:
        // pre-change, MineWatch's only manifest renderer was RumoredLines, which computed
        // card.Manifest.Take(FeedVisibleLines - 1) with FeedVisibleLines == 3, i.e. Take(2) — for
        // a party of three heroes (roster order 1,2,3) each carrying one player-crafted item, that
        // keeps only hero 1 and hero 2's lines and silently drops hero 3's "Fine Iron Staff" line,
        // which would appear NOWHERE in the tree (confirmed against git HEAD's MineWatch.cs before
        // touching it — this session cannot run gdUnit/tools/engine-test.ps1 to observe it fail
        // live, so this is a traced-by-hand red, not an executed one; said plainly in the report).
        // Post-fix, the departure slate carries every manifest line, uncapped, which the
        // DepartureSlateLines assertions below additionally pin at the new surface directly.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var w1 = new ItemId(1);
            var w2 = new ItemId(2);
            var w3 = new ItemId(3);
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", "vanguard") with { Gear = new GearSet(w1, null, null) })
                .Add(2, Delver(2, "Elowen", "striker") with { Gear = new GearSet(w2, null, null) })
                .Add(3, Delver(3, "Brask", "mystic") with { Gear = new GearSet(w3, null, null) });
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(w1, "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty))
                .Add(2, new Item(w2, "recipe", "Fine Iron Bow", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty))
                .Add(3, new Item(w3, "recipe", "Fine Iron Staff", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty));
            var state = GameFactory.NewGame(9097) with { Heroes = heroes, Items = items };
            var plan = new PartyPlan(
                ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), TargetFloor: 2, VenueId: "mine");
            var events = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2),
                new PartiesFormed(ImmutableList.Create(plan)));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, events);

            // Primary assertion — checkable against pre-change code by inspection (RenderedText
            // already existed; it walks the whole tree regardless of where the text lives).
            var everywhere = RenderedText(watch);
            AssertThat(everywhere).Contains("Fine Iron Blade");
            AssertThat(everywhere).Contains("Fine Iron Bow");
            AssertThat(everywhere)
                .OverrideFailureMessage(
                    "Hero 3's carried item ('Fine Iron Staff') was dropped nowhere in the tree — this " +
                    "is the FeedVisibleLines-1 cap bug this unit fixes (pre-change: card.Manifest.Take(2)).")
                .Contains("Fine Iron Staff");

            // Post-fix precision: the same three lines on the new surface, uncapped.
            AssertThat(watch.DepartureSlateLines.Count).IsEqual(3);
            AssertThat(watch.DepartureSlateLines.Any(l => l.Contains("Fine Iron Blade"))).IsTrue();
            AssertThat(watch.DepartureSlateLines.Any(l => l.Contains("Fine Iron Bow"))).IsTrue();
            AssertThat(watch.DepartureSlateLines.Any(l => l.Contains("Fine Iron Staff"))).IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void ExpeditionPhase_NoPlayerCraftedGear_SlateShowsHonestEmptyState_NotBarePlaceholder()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld(); // Delver's default GearSet.Empty — a bare-handed party
            var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 2, VenueId: "mine");
            var events = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1)), 2),
                new PartiesFormed(ImmutableList.Create(plan)));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, events);

            AssertThat(watch.DepartureSlateLines.Count).IsEqual(1);
            AssertThat(watch.DepartureSlateLines.Single())
                .IsEqual("Nobody in this party carries anything you forged.");
            // The icon+text shape LedgerModal.AddEmptyState uses, not the bare "A party sets out…"
            // JourneyStream.DepartureLine falls back to (that fallback stays PipDock's own, untouched).
            var icon = Find<TextureRect>(watch, "DepartureSlateEmptyIcon");
            AssertThat(icon.Texture).IsNotNull();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void ExpeditionPhase_OneHeroTwoCraftedItems_BothNamed_NoDuplication()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var weapon = new ItemId(1);
            var armor = new ItemId(2);
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", "vanguard") with { Gear = new GearSet(weapon, null, armor) });
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(weapon, "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty))
                .Add(2, new Item(armor, "recipe", "Fine Iron Plate", ItemSlot.Armor, QualityGrade.Fine,
                    new ItemStats(0, 2, 0), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty));
            var state = GameFactory.NewGame(9095) with { Heroes = heroes, Items = items };
            var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 2, VenueId: "mine");
            var events = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1)), 2),
                new PartiesFormed(ImmutableList.Create(plan)));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, events);

            AssertThat(watch.DepartureSlateLines.Count).IsEqual(2);
            AssertThat(watch.DepartureSlateLines.Count(l => l.Contains("Fine Iron Blade"))).IsEqual(1);
            AssertThat(watch.DepartureSlateLines.Count(l => l.Contains("Fine Iron Plate"))).IsEqual(1);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void ExpeditionPhase_RivalBoughtGear_NeverNamedOnSlate()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var weapon = new ItemId(1); // player-crafted
            var rivalShield = new ItemId(2); // vendor stock, no MakersMark
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", "vanguard") with { Gear = new GearSet(weapon, rivalShield, null) });
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(weapon, "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty))
                .Add(2, new Item(rivalShield, "recipe", "Rival Shield", ItemSlot.Shield, QualityGrade.Common,
                    new ItemStats(0, 1, 0), Mark: null, History: ImmutableList<ItemHistoryEntry>.Empty));
            var state = GameFactory.NewGame(9094) with { Heroes = heroes, Items = items };
            var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 2, VenueId: "mine");
            var events = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1)), 2),
                new PartiesFormed(ImmutableList.Create(plan)));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, events);

            AssertThat(watch.DepartureSlateLines.Count).IsEqual(1); // the rival shield earns no line
            AssertThat(watch.DepartureSlateLines.Any(l => l.Contains("Fine Iron Blade"))).IsTrue();
            AssertThat(watch.DepartureSlateLines.Any(l => l.Contains("Rival Shield"))).IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void DepartureSlate_EscapesTheSubViewportBoundary_VisibleToScreenObservation()
    {
        // U2 verification note: MineWatch's OWN content built before this unit (_recordBark,
        // _feedLabel, DelveStage) all live inside `_viewport` (a child SubViewport) and are
        // therefore invisible to ScreenObservation.Descendants — it deliberately stops at a
        // SubViewport boundary and never looks inside one (see that method's own remarks), which
        // is also the walk AgentPlaytest's digest and HumanPlayer.Screen() are both built on. The
        // departure slate is a sibling of _viewport (a direct child of `this`, the
        // SubViewportContainer) specifically so this unit's "the AgentPlaytest digest can see the
        // slate's text" scenario is actually true — proven here against the real shared walker,
        // not a hand-rolled substitute.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var weapon = new ItemId(1);
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "Torvald", "vanguard") with { Gear = new GearSet(weapon, null, null) });
            var items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, new Item(weapon, "recipe", "Fine Iron Blade", ItemSlot.Weapon, QualityGrade.Fine,
                    new ItemStats(1, 0, 1), new MakersMark("Player", 1), ImmutableList<ItemHistoryEntry>.Empty));
            var state = GameFactory.NewGame(9096) with { Heroes = heroes, Items = items };
            var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 2, VenueId: "mine");
            var events = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1)), 2),
                new PartiesFormed(ImmutableList.Create(plan)));

            watch.Refresh(state with { Phase = DayPhase.Expedition }, events);

            var reachableOutsideViewports = ScreenObservation.AllTextNodes(watch).Select(n => n.Text).ToList();

            AssertThat(reachableOutsideViewports.Any(t => t.Contains("Fine Iron Blade")))
                .OverrideFailureMessage(
                    "The departure slate's text was not reachable outside the SubViewport boundary — " +
                    "AgentPlaytest/HumanPlayer would never see it.")
                .IsTrue();

            // Contrast: the roll-call feed lives INSIDE _viewport (unchanged by this unit) and stays
            // invisible to the very same walk — confirming the slate's placement is what makes the
            // difference, not a change to how ScreenObservation itself works.
            AssertThat(reachableOutsideViewports.Any(t => t.Contains("set out for floor"))).IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

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
    public void ForceRevealWhilePaused_KeepsRevealingEvenWhileClockIsPaused()
    {
        // U9 (KTD-4): ScryingMirror sets this true for as long as it borrows the strip, because
        // opening the Mirror unconditionally force-pauses PhaseClock (MainUi.OnMirrorVisibilityChanged).
        // Without this override, "press Watch to see the show" would freeze the show at the exact
        // moment a player opened it -- the same bug ScryingMirror's own feed was already fixed for.
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
            watch.ForceRevealWhilePaused = true;

            watch._Process(100.0); // would stay clouded (see the test above) if the override failed
            AssertThat(watch.CurrentBeats.Any(b => b.Contains("cave-rat")))
                .OverrideFailureMessage(
                    "ForceRevealWhilePaused did not override a paused Clock -- opening the Mirror " +
                    "would freeze the show it exists to display.")
                .IsTrue();
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

    // ── repo task #67: honest narration when nobody is actually camped ─────────────────────────
    // Owner playtest: "Lower into the mine has them return??? what logic is that lol" — the
    // kernel still ticks Camp/ExpeditionDeep even when a party's whole trip already resolved
    // inside the Expedition tick (floor-1 unstaged, or a bad stage-1 ending), so InFlight comes
    // out empty and there is nothing to camp over or resolve. No kernel change was made (see
    // EmptyMineScenarioTests on the sim side for why, and for the InFlight truth table this flag
    // reads) — this is the Godot-side fix: AlreadyBackThisCycle names the case, and
    // UpdateFeedLabel prepends an honest caption instead of leaving the strip to replay an
    // already-decided run with nothing telling the player the outcome was already sealed.

    [TestCase]
    public void AlreadyBackThisCycle_True_CampPhase_ResolvedPartyButNothingCamped()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with
            {
                Phase = DayPhase.Camp,
                PendingExpeditions = ImmutableList.Create(ResolvedResult()),
            };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.AlreadyBackThisCycle).IsTrue();
            var feed = Find<Label>(watch, "JourneyFeedLabel");
            AssertThat(feed.Text).Contains("Already back");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void AlreadyBackThisCycle_True_ExpeditionDeepPhase_ResolvedPartyButNothingCamped()
    {
        // Same shape one phase later — the Deep tick has nothing left to resolve either.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with
            {
                Phase = DayPhase.ExpeditionDeep,
                PendingExpeditions = ImmutableList.Create(ResolvedResult()),
            };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.AlreadyBackThisCycle).IsTrue();
            var feed = Find<Label>(watch, "JourneyFeedLabel");
            AssertThat(feed.Text).Contains("Already back");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void AlreadyBackThisCycle_False_WhenAPartyIsGenuinelyCamped()
    {
        // The contrast case: InFlight non-empty — a real vigil, no caption needed or wanted.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            AssertThat(watch.AlreadyBackThisCycle).IsFalse();
            var feed = Find<Label>(watch, "JourneyFeedLabel");
            AssertThat(feed.Text).NotContains("Already back");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void AlreadyBackThisCycle_False_DuringExpeditionPhase_EvenBeforeTheOutcomeIsKnown()
    {
        // The Marching figures at the Expedition phase itself are genuinely still in progress —
        // this flag is Camp/ExpeditionDeep-only by construction (MineWatch.Refresh), regardless
        // of what that tick is about to decide.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2));

            watch.Refresh(StagedWorld() with { Phase = DayPhase.Expedition }, departed);

            AssertThat(watch.AlreadyBackThisCycle).IsFalse();
        }
        finally
        {
            watch.Free();
        }
    }

    /// <summary>A clean (no-death) resolved run — the same shape a floor-1 unstaged first trip
    /// produces, just without the death-round noise <see cref="DeathRound_NeverAppearsInMineWatchFeed_RendersCloudInstead"/>
    /// already covers.</summary>
    private static ExpeditionResult ResolvedResult() => new(
        Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 1, DeepestFloorCleared: 1,
        Floors: ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null)))),
        Survivors: ImmutableList.Create(new HeroId(1)), Deaths: ImmutableList<HeroId>.Empty,
        Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
        GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);

    // ── A2 (+A3 FX): the beat-driven DelveStage overlay, wired through the real Refresh/_Process
    // playhead (DelveStageTests exercises DelveStage directly with handcrafted beats; these prove
    // the FULL wiring — GameState → RefreshDelveBeats → the playhead → DelveStage.RenderBeat).

    [TestCase]
    public void CampPhase_StagedParty_DelveStagePlaysFloorAndMonster_AsBeatsReveal()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);
            watch._Process(100.0); // force full reveal — same convention as the feed tests above

            AssertThat(watch.Delve.CurrentFloor).IsEqual(1);
            AssertThat(watch.Delve.CurrentMonsterKind).IsEqual("cave-rat");
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void PendingExpedition_UnstagedResolvedParty_DeathRound_CloudsOnDelveStage_NeverPipsOrHp()
    {
        // The ONLY source that can ever carry a SwallowedByDark beat (InFlightExpedition.Dead is
        // always empty in v1) — a fully resolved (never staged) party in PendingExpeditions.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var floors = ImmutableList.Create(
                new FloorOutcome(1, true, ImmutableList.Create(
                    new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))),
                new FloorOutcome(2, false, ImmutableList.Create(
                    new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(1), 0, 10, false, null),
                    new CombatEvent(2, new HeroId(1), "tunnel-spider", ImmutableList.Create(9), 0, 40, false, null))));
            var result = new ExpeditionResult(
                Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 2, DeepestFloorCleared: 1, Floors: floors,
                Survivors: ImmutableList<HeroId>.Empty, Deaths: ImmutableList.Create(new HeroId(1)),
                Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);
            var state = StagedWorld() with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };

            watch.Refresh(state, ImmutableList<GameEvent>.Empty);
            watch._Process(100.0); // force full reveal

            AssertThat(watch.Delve.IsClouded(1)).IsTrue();
            AssertThat(watch.Delve.HasPips(1)).IsFalse();
            AssertThat(watch.Delve.CurrentFloor).IsEqual(2); // played all the way to the fatal floor
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

    // ── link3 ("the watch becomes a fight"): the walk cycle + weight/secondary motion ───────────

    [TestCase]
    public void Marching_VanguardParty_WalkFrameTextureCyclesAcrossMultipleFrames()
    {
        // Vanguard ships the full 4-frame town2d-hero-vanguard pixel walk (base/_walk2/_step/
        // _walk4) — the live path for every real class today (ResolveWalkFrames' own doc). A
        // static single-frame figure would leave sprite.Texture unchanged for all 30 samples.
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2));
            watch.Refresh(StagedWorld() with { Phase = DayPhase.Expedition }, departed);

            var sprite = Find<Sprite2D>(watch, "MineHero_0");
            var seen = new HashSet<Texture2D>();
            for (var i = 0; i < 30; i++)
            {
                watch._Process(0.05);
                seen.Add(sprite.Texture);
            }

            AssertThat(seen.Count)
                .OverrideFailureMessage(
                    "Marching figures never cycled walk-frame textures -- still a single static pose, " +
                    "not a real walk cycle.")
                .IsGreater(1);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Marching_FigureBobsVerticallyOverAccumulatedDelta()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2));
            watch.Refresh(StagedWorld() with { Phase = DayPhase.Expedition }, departed);

            var sprite = Find<Sprite2D>(watch, "MineHero_0");
            var baseY = sprite.Position.Y;

            var sawOffBase = false;
            for (var i = 0; i < 20; i++)
            {
                watch._Process(0.05);
                if (!Mathf.IsEqualApprox(sprite.Position.Y, baseY))
                {
                    sawOffBase = true;
                }
            }

            AssertThat(sawOffBase)
                .OverrideFailureMessage("Marching figures never bobbed vertically -- SpriteMotion isn't wired.")
                .IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Camped_IdleBreathing_ScaleVariesFromRest_ButNeverCyclesWalkFrames()
    {
        // The breathing pose (SpriteMotion.IdlePose) is deliberately a different read from the
        // walk bob (G4 — "breathing at rest that differs from the walk bob"): it squashes/
        // stretches Scale, never touches WalkFrame (always 0, the base texture).
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(CampedParty()) };
            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            var sprite = Find<Sprite2D>(watch, "MineHero_0"); // full-hp hero, never slumped
            var restingScaleY = sprite.Scale.Y;
            var baseTexture = sprite.Texture;

            var sawBreath = false;
            for (var i = 0; i < 40; i++)
            {
                watch._Process(0.05);
                if (!Mathf.IsEqualApprox(sprite.Scale.Y, restingScaleY))
                {
                    sawBreath = true;
                }

                AssertThat(sprite.Texture)
                    .OverrideFailureMessage("A camped (idle) figure must never cycle walk frames.")
                    .IsEqual(baseTexture);
            }

            AssertThat(sawBreath)
                .OverrideFailureMessage("A camped figure never showed the idle breathing squash/stretch.")
                .IsTrue();
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void Clock_Paused_NoNewDelveBeatsRender_Played_TheyDo()
    {
        // Extends the existing feed-pause coverage (Clock_Paused_FeedHoldsStill_Played_ItAdvances)
        // to this unit's own trigger point: BeginCombatPose/ImpactPulse only ever fire from
        // RenderBeat, which only runs off the SAME gated _delveHead reveal — so a genuine pause
        // must never start a NEW combat beat/animation, only let an already-started one finish.
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

            watch._Process(100.0); // would force full reveal if the beat loop were still advancing
            AssertThat(watch.Delve.CurrentFloor).IsEqual(0);

            clock.Play();
            watch._Process(100.0);
            AssertThat(watch.Delve.CurrentFloor).IsEqual(1);
        }
        finally
        {
            watch.Free();
        }
    }

    /// <summary>
    /// U6: the monster's idle-breathe is the newest thing riding this pause contract, and it is
    /// wired one level down (<c>DelveStage.Process(delta, paused)</c>) rather than in this file's
    /// own loop — so <see cref="DelveStageTests"/> proving the breath freezes when told to does NOT
    /// prove the game ever tells it to. This is the integration half: a real paused
    /// <see cref="PhaseClock"/>, driven through the real <c>_Process</c>, must hold the monster's
    /// pose still, and playing must let it move again. Dropping the <c>paused:</c> argument at the
    /// call site compiles fine and would leave a monster breathing through a stopped clock.
    /// </summary>
    [TestCase]
    public void Clock_Paused_MonsterBreathHoldsStill_Played_ItResumes()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };
            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            var clock = new PhaseClock(new SimAdapter(state));
            clock.Play();
            watch.Clock = clock;
            watch._Process(100.0);

            // Put a monster on stage explicitly. The camped fixture above reveals its beats but
            // engages nothing, and a hidden monster holds its rest pose whether the clock runs or
            // not — which would make the frozen-pose assertion below pass for the wrong reason. This
            // is the same real DelveStage MineWatch owns, reached through its own public render
            // path; only the beat is supplied by hand.
            watch.Delve.RenderBeat(
                new DelveBeat(
                    DelveBeatKind.Engage, 1, null, "cave-rat", 0, 0,
                    ImmutableSortedDictionary<int, int>.Empty, false),
                state.Heroes);

            clock.Pause();
            watch._Process(0.17); // settle onto the frozen pose
            var paused = watch.Delve.MonsterScale;

            // 3 x 0.17s deliberately does NOT sum to a multiple of the 2s breathe cycle. An earlier
            // version of this test advanced 8 x 0.25s = exactly one full cycle, so the pose returned
            // to precisely where it started and the assertion passed even with the pause argument
            // removed at the call site. A pause test that a full period can satisfy tests nothing.
            for (var i = 0; i < 3; i++)
            {
                watch._Process(0.17);
            }

            AssertThat(watch.Delve.MonsterScale)
                .OverrideFailureMessage(
                    $"The monster kept breathing through a paused clock: {paused} -> " +
                    $"{watch.Delve.MonsterScale}. MineWatch._Process must pass its own feedPaused " +
                    "down to DelveStage.Process, the same contract _feed and _delveHead already use.")
                .IsEqual(paused);

            clock.Play();
            watch._Process(0.5);

            AssertThat(watch.Delve.MonsterScale)
                .OverrideFailureMessage(
                    "The monster's pose did not change after the clock resumed, so this test could " +
                    "not have detected a stuck breath either way — check that a monster is on stage " +
                    "at all in this fixture before trusting the paused half above.")
                .IsNotEqual(paused);
        }
        finally
        {
            watch.Free();
        }
    }

    /// <summary>
    /// U-T5-12 (§11.14.7): "add the camera its CameraHint field was written for" — the integration
    /// half of <c>DelveStageTests</c>' FocusAnchor/FocusIntensity coverage. Proving DelveStage
    /// resolves the right anchor does NOT prove MineWatch's own <c>_Process</c> ever turns that into
    /// an actual camera reaction, exactly the same gap <c>Clock_Paused_MonsterBreathHoldsStill</c>
    /// above closes for the breathe pause. Drives the real <c>_Process</c> loop; only the beat/focus
    /// map are supplied by hand, same convention as that test.
    /// </summary>
    [TestCase]
    public void CameraFocus_ZoomsInWhileAFloorIsFlared_ThenEasesBackWhenCleared()
    {
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var camp = CampedPartyWithFloors();
            var state = StagedWorld() with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp) };
            watch.Refresh(state, ImmutableList<GameEvent>.Empty);

            var clock = new PhaseClock(new SimAdapter(state));
            clock.Play();
            watch.Clock = clock;

            AssertThat(watch.WorldZoom).IsEqualApprox(1f, 0.001f);

            watch.Delve.FloorFocusByFloor = ImmutableDictionary<int, DelveStage.FloorFocus>.Empty
                .Add(1, new DelveStage.FloorFocus(new HeroId(1), 1f));
            watch.Delve.RenderBeat(
                new DelveBeat(DelveBeatKind.Engage, 1, null, "cave-rat", 0, 0,
                    ImmutableSortedDictionary<int, int>.Empty, false),
                state.Heroes);

            for (var i = 0; i < 30; i++)
            {
                watch._Process(0.1);
            }

            AssertThat(watch.WorldZoom)
                .OverrideFailureMessage("A flared floor never zoomed the camera in -- CameraHint reached DelveStage but not MineWatch's own world scale.")
                .IsGreater(1f);

            // Clear the focus (a Surface beat, the fight's over) and let it ease back to rest.
            watch.Delve.RenderBeat(
                new DelveBeat(DelveBeatKind.Surface, 1, null, "TargetReached", 0, 0,
                    ImmutableSortedDictionary<int, int>.Empty, false),
                state.Heroes);

            for (var i = 0; i < 30; i++)
            {
                watch._Process(0.1);
            }

            AssertThat(watch.WorldZoom)
                .OverrideFailureMessage("The camera never let go of the flared floor -- a stuck zoom is worse than none.")
                .IsEqualApprox(1f, 0.01f);
        }
        finally
        {
            watch.Free();
        }
    }

    [TestCase]
    public void ManyMarchCampCycles_LeaveNoOrphanNodesAfterTeardown()
    {
        // Same technique PanelRebuildDoesNotLeakNodesTests uses against Godot's own orphan
        // counter — RenderMarch/RenderCamp both call ClearFigures on every rebuild, and this proves
        // the Figure class (record struct -> class this unit finished) still frees cleanly through
        // repeated march<->camp swaps.
        //
        // Budgeted, not zero, for the SAME documented reason PanelRebuildDoesNotLeakNodesTests
        // budgets rather than asserts zero: every Refresh() with a live tracked party also calls
        // UpdateDepartureSlate (U2, unrelated to this unit), which detaches and PanelGraveyard.Buries
        // its previous rows unconditionally — QueueFree, not an immediate Free. PanelGraveyard.Drain
        // is the only thing that forces those through, and only MainUi calls it (on mount/unmount);
        // a standalone MineWatch never mounted under one — exactly this test — never drains, so 30
        // Refresh() calls leave ~1-2 QueueFree'd-but-not-yet-destroyed rows apiece as measured
        // "orphans" until a real frame (or a Drain) arrives. That is accepted, pre-existing, and not
        // this unit's figures/combat-pose churn to fix; LeakBudget stays far below what a genuine
        // per-iteration leak in THIS unit's own new code would produce (hundreds to thousands, per
        // PanelRebuildDoesNotLeakNodesTests' own measured history).
        const int LeakBudget = 90;

        var before = OrphanNodeCount();
        var watch = new MineWatch();
        try
        {
            watch.Build();
            var marchState = StagedWorld() with { Phase = DayPhase.Expedition };
            var departed = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)), 2));
            var campState = StagedWorld() with
            {
                Phase = DayPhase.Camp,
                InFlight = ImmutableList.Create(CampedPartyWithFloors()),
            };

            for (var i = 0; i < 15; i++)
            {
                watch.Refresh(marchState, departed);
                watch._Process(0.1);
                watch.Refresh(campState, ImmutableList<GameEvent>.Empty);
                watch._Process(0.1);
            }
        }
        finally
        {
            watch.Free();
        }

        var leaked = OrphanNodeCount() - before;
        AssertThat(leaked)
            .OverrideFailureMessage(
                $"{leaked} nodes leaked across repeated march/camp Refresh cycles (budget {LeakBudget}) -- " +
                "check ClearFigures/RenderMarch/RenderCamp and DelveStage.ResetState still free every node " +
                "they own; see this test's own comment for the separate, accepted UpdateDepartureSlate/" +
                "PanelGraveyard baseline noise this budget already covers.")
            .IsLess(LeakBudget);
    }

    private static int OrphanNodeCount() => (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

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
