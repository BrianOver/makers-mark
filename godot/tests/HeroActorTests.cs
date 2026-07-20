#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// World-rework U19 bridge of <c>HeroSpriteTests</c> (LW1's direct unit coverage) onto
/// <see cref="HeroActor"/> — no MainUi/SimAdapter wiring needed, so these stay fast and isolate
/// the never-static idle bob, walk bob, facing, arrival squash, and anchor-vignette determinism
/// from the full town-scene choreography (covered separately in TownSceneTests via the real
/// phase/event pipeline). Every motion assertion below is byte-identical to its
/// <c>HeroSpriteTests</c> predecessor — <see cref="HeroActor"/> ports the state machine
/// verbatim; only the figure lookup type (<see cref="Sprite2D"/>, not <c>TextureRect</c>)
/// and the new painted-portrait/SVG-fallback resolution are new coverage.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroActorTests
{
    private static Hero MakeHero(int id, string classId = "vanguard") => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 20, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static HeroActor MakeActor(int id, Vector2 home, Vector2 gate, string classId = "vanguard")
    {
        var actor = new HeroActor();
        actor.Setup(MakeHero(id, classId), home, gate);
        return actor;
    }

    [TestCase]
    public void Setup_PlacesActorAtHome()
    {
        var actor = MakeActor(1, new Vector2(400, 300), new Vector2(900, 350));
        try
        {
            AssertThat(actor.Position).IsEqual(new Vector2(400, 300));
            AssertThat(actor.State).IsEqual(HeroActor.TownState.Wandering);
        }
        finally
        {
            actor.Free();
        }
    }

    [TestCase]
    public void Wandering_NeverStatic_PositionDriftsOverTime()
    {
        // heroValue=2, day=0 (default) is not an anchor-visit day (VisitsAnchorOn(2,0) is
        // false) — isolates the plain wander+breath drift from the anchor-pause branch.
        AssertThat(HeroActor.VisitsAnchorOn(2, 0)).IsFalse();

        var actor = MakeActor(2, new Vector2(400, 300), new Vector2(900, 350));
        try
        {
            actor.Advance(0.016, 0.016);
            var first = actor.Position;
            actor.Advance(0.5, 0.516);
            var second = actor.Position;
            AssertThat(first != second).IsTrue(); // breath bob + wander keep it moving
        }
        finally
        {
            actor.Free();
        }
    }

    [TestCase]
    public void Facing_FlipsWithTravelDirection()
    {
        var home = new Vector2(400, 300);

        var actorRight = MakeActor(3, home, new Vector2(900, 350));
        var figureRight = actorRight.FindChild("Sprite", true, false) as Sprite2D;
        AssertThat(figureRight).IsNotNull();
        actorRight.BeginDeparture(home + new Vector2(60, 0), 0f); // rally point to the RIGHT
        actorRight.Advance(0.2, 0.2);
        AssertThat(figureRight!.FlipH).IsFalse(); // faces right when moving right

        var actorLeft = MakeActor(4, home, new Vector2(900, 350));
        var figureLeft = actorLeft.FindChild("Sprite", true, false) as Sprite2D;
        actorLeft.BeginDeparture(home - new Vector2(60, 0), 0f); // rally point to the LEFT
        actorLeft.Advance(0.2, 0.2);
        AssertThat(figureLeft!.FlipH).IsTrue(); // faces left when moving left

        actorRight.Free();
        actorLeft.Free();
    }

    [TestCase]
    public void ArrivalSquash_TriggersOnArrivalThenEasesBackToBaseScale()
    {
        var home = new Vector2(400, 300);
        var actor = MakeActor(5, home, new Vector2(900, 350));
        var figure = actor.FindChild("Sprite", true, false) as Sprite2D;
        try
        {
            var baseScale = figure!.Scale;

            // Rally point == Home: arrives on the very first Advance call, so the squash
            // trigger is isolated from any dependence on the gate/home walking distance.
            actor.BeginDeparture(home, 0f);
            actor.Advance(0.016, 0.016);
            AssertThat(actor.State).IsEqual(HeroActor.TownState.Rallying); // now dwelling
            AssertThat(figure.Scale != baseScale).IsTrue(); // squashed, mid-ease

            actor.Advance(0.3, 0.316); // more than SquashDuration (0.2s) later
            AssertThat(figure.Scale).IsEqual(baseScale); // settled bit-exact back to fit-scale
        }
        finally
        {
            actor.Free();
        }
    }

    [TestCase]
    public void RallyFileExit_LaterSlotPeelsOffAfterEarlierSlot()
    {
        // Rally point == Home (zero travel distance) for both, so the ONLY difference is
        // fileDelaySeconds — isolates the staggered peel-off from Home-to-rally travel time
        // (which TownSceneTests can't pin precisely since HomeFor spreads heroes out).
        var home = new Vector2(400, 300);
        var gate = new Vector2(900, 350);
        var early = MakeActor(10, home, gate);
        var late = MakeActor(11, home, gate);
        try
        {
            early.BeginDeparture(home, 0f);
            late.BeginDeparture(home, HeroActor.RallyDwellSeconds); // one dwell-length further back in file

            var t = HeroActor.RallyDwellSeconds + 0.05; // just past the EARLY slot's total wait
            early.Advance(t, t);
            late.Advance(t, t);

            AssertThat(early.State).IsEqual(HeroActor.TownState.WalkingOut); // already peeled off
            AssertThat(late.State).IsEqual(HeroActor.TownState.Rallying); // still waiting its turn
        }
        finally
        {
            early.Free();
            late.Free();
        }
    }

    [TestCase]
    public void AnchorVignette_DeterministicHeroAndDay_PausesAtLandmark()
    {
        // Find a (heroValue, day) pair the formula actually visits, then confirm the
        // wandering position lands on that landmark within the pause window (not the
        // home+wander drift) — deterministic, no RNG.
        var heroValue = 0;
        var day = 0;
        while (!HeroActor.VisitsAnchorOn(heroValue, day))
        {
            day++;
        }

        var home = new Vector2(400, 300);
        var actor = MakeActor(heroValue, home, new Vector2(900, 350));
        actor.Day = day;
        try
        {
            actor.Advance(0.016, 0.016); // early in the window (< AnchorPauseSeconds)
            var anchor = HeroActor.AnchorFor(heroValue, day);
            AssertThat(actor.Position.DistanceTo(anchor) < 3f).IsTrue(); // breath bob only, ±1.5px
        }
        finally
        {
            actor.Free();
        }
    }

    [TestCase]
    public void BeginRecruitWalkIn_SpawnsOffscreenLeftThenWalksHome()
    {
        var home = new Vector2(400, 300);
        var actor = MakeActor(6, home, new Vector2(900, 350));
        try
        {
            actor.BeginRecruitWalkIn();
            AssertThat(actor.State).IsEqual(HeroActor.TownState.WalkingIn);
            AssertThat(actor.Visible).IsTrue();
            AssertThat(actor.Position.X < 0f).IsTrue(); // off-screen left, never popped in at Home

            actor.Advance(10, 10.0); // one big fast-forward step covers the whole walk
            AssertThat(actor.State).IsEqual(HeroActor.TownState.Wandering);
            AssertThat(actor.Position.DistanceTo(home) < 3f).IsTrue();
        }
        finally
        {
            actor.Free();
        }
    }

    // ── U19: painted portrait / SVG-fallback figure resolution ───────────────────────────────

    [TestCase]
    public void PaintedPortrait_BoundPerClass_UntintedWhite_ForAllSixRegisteredClasses()
    {
        // The 6 registered classes all ship a painted hero-<classId> PNG (P3/art wave) — every
        // one of them must resolve the painted portrait, untinted (a painted figure carries its
        // own color; only the SVG fallback below gets the role tint).
        foreach (var classId in ClassRegistry.All.Keys)
        {
            var actor = MakeActor(20, new Vector2(400, 300), new Vector2(900, 350), classId);
            try
            {
                var figure = actor.FindChild("Sprite", true, false) as Sprite2D;
                AssertThat(figure).IsNotNull();
                // AssetCatalog.HeroPortrait wraps a fresh CanvasTexture per call, so compare the
                // cached DIFFUSE resource underneath (GD.Load caches by path — the same instance
                // every call) rather than the wrapper object identity.
                var canvasTexture = figure!.Texture as CanvasTexture;
                AssertThat(canvasTexture).IsNotNull();
                AssertThat(canvasTexture!.DiffuseTexture).IsEqual(IconRegistry.Art(AssetCatalog.HeroPortraitId(classId)));
                AssertThat(canvasTexture.DiffuseTexture).IsNotNull();
                AssertThat(figure.Modulate).IsEqual(Colors.White);
            }
            finally
            {
                actor.Free();
            }
        }
    }

    [TestCase]
    public void UnknownClassId_FallsBackToTintedSvg_NeverThrows()
    {
        // No painted PNG and no hand-authored SVG exists for this id — ResolveFigure must still
        // degrade gracefully (null texture, never a throw) AND take the SVG-fallback branch
        // (proven by the role tint: a painted-branch hit would leave Modulate White).
        var actor = MakeActor(21, new Vector2(400, 300), new Vector2(900, 350), "no-such-class");
        try
        {
            AssertThat(AssetCatalog.HeroPortrait("no-such-class")).IsNull();
            var figure = actor.FindChild("Sprite", true, false) as Sprite2D;
            AssertThat(figure).IsNotNull();
            AssertThat(figure!.Modulate).IsEqual(HeroActor.RoleColor("no-such-class"));
        }
        finally
        {
            actor.Free();
        }
    }

    // U19: click routing itself (Clicked event → TownScene.HeroClicked → hero-inspect tab) is
    // covered end-to-end in TownSceneTests, against a LIVE, mounted actor under the real
    // SubViewport tree via UiTestSupport.TryClickArea — mirroring the building click-zone
    // precedent rather than firing signals on a freestanding, untreed node here.

    // ── U19: name label shows on hover/selected only ──────────────────────────────────────────

    [TestCase]
    public void NameLabel_HiddenByDefault_ShownOnHoverOrSelected_HiddenAgainWhenBothClear()
    {
        var actor = MakeActor(32, new Vector2(400, 300), new Vector2(900, 350));
        try
        {
            var label = actor.FindChild("NameLabel", true, false) as Label;
            AssertThat(label).IsNotNull();
            AssertThat(label!.Visible).IsFalse();

            actor.SetHovered(true);
            AssertThat(label.Visible).IsTrue();
            actor.SetHovered(false);
            AssertThat(label.Visible).IsFalse();

            actor.SetSelected(true);
            AssertThat(label.Visible).IsTrue();
            actor.SetSelected(false);
            AssertThat(label.Visible).IsFalse();
        }
        finally
        {
            actor.Free();
        }
    }

    // ── U19: determinism (accumulated-delta pin) ──────────────────────────────────────────────

    [TestCase]
    public void Determinism_TwoActorsSameSeed_IdenticalPositionsAtFrameN()
    {
        // Same hero id + home + gate, driven through the identical departure/return script with
        // the identical delta sequence — a pure function of (id, accumulated time), no RNG, no
        // wall clock (KTD2/KTD4) — must land at bit-identical positions every step.
        var home = new Vector2(410, 512);
        var gate = new Vector2(900, 480);
        var a = MakeActor(42, home, gate);
        var b = MakeActor(42, home, gate);
        try
        {
            void Script(HeroActor actor)
            {
                actor.Day = 3;
                actor.Advance(0.016, 0.016);
                actor.Advance(0.5, 0.516);
                actor.BeginDeparture(home + new Vector2(80, 0), 0.1f);
                actor.Advance(0.25, 0.766);
                actor.Advance(1.5, 2.266);
            }

            Script(a);
            Script(b);

            AssertThat(a.Position).IsEqual(b.Position);
            AssertThat(a.State).IsEqual(b.State);
        }
        finally
        {
            a.Free();
            b.Free();
        }
    }
}
#endif
