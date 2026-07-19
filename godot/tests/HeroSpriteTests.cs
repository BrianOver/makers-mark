#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// LW1 direct unit coverage for <see cref="HeroSprite"/>'s motion state machine — no MainUi/
/// SimAdapter wiring needed, so these stay fast and isolate the never-static idle bob, walk
/// bob, facing, arrival squash, and anchor-vignette determinism from the full town-scene
/// choreography (covered separately in TownSceneTests via the real phase/event pipeline).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroSpriteTests
{
    private static Hero MakeHero(int id, string classId = "vanguard") => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 20, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static HeroSprite MakeSprite(int id, Vector2 home, Vector2 gate)
    {
        var sprite = new HeroSprite();
        sprite.Setup(MakeHero(id), home, gate);
        return sprite;
    }

    [TestCase]
    public void Setup_PlacesSpriteAtHome()
    {
        var sprite = MakeSprite(1, new Vector2(400, 300), new Vector2(900, 350));
        try
        {
            AssertThat(sprite.Position).IsEqual(new Vector2(400, 300));
            AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Wandering);
        }
        finally
        {
            sprite.Free();
        }
    }

    [TestCase]
    public void Wandering_NeverStatic_PositionDriftsOverTime()
    {
        // heroValue=2, day=0 (default) is not an anchor-visit day (VisitsAnchorOn(2,0) is
        // false) — isolates the plain wander+breath drift from the anchor-pause branch.
        AssertThat(HeroSprite.VisitsAnchorOn(2, 0)).IsFalse();

        var sprite = MakeSprite(2, new Vector2(400, 300), new Vector2(900, 350));
        try
        {
            sprite.Advance(0.016, 0.016);
            var first = sprite.Position;
            sprite.Advance(0.5, 0.516);
            var second = sprite.Position;
            AssertThat(first != second).IsTrue(); // breath bob + wander keep it moving
        }
        finally
        {
            sprite.Free();
        }
    }

    [TestCase]
    public void Facing_FlipsWithTravelDirection()
    {
        var home = new Vector2(400, 300);

        var spriteRight = MakeSprite(3, home, new Vector2(900, 350));
        var figureRight = spriteRight.FindChild("Sprite", true, false) as TextureRect;
        AssertThat(figureRight).IsNotNull();
        spriteRight.BeginDeparture(home + new Vector2(60, 0), 0f); // rally point to the RIGHT
        spriteRight.Advance(0.2, 0.2);
        AssertThat(figureRight!.FlipH).IsFalse(); // faces right when moving right

        var spriteLeft = MakeSprite(4, home, new Vector2(900, 350));
        var figureLeft = spriteLeft.FindChild("Sprite", true, false) as TextureRect;
        spriteLeft.BeginDeparture(home - new Vector2(60, 0), 0f); // rally point to the LEFT
        spriteLeft.Advance(0.2, 0.2);
        AssertThat(figureLeft!.FlipH).IsTrue(); // faces left when moving left

        spriteRight.Free();
        spriteLeft.Free();
    }

    [TestCase]
    public void ArrivalSquash_TriggersOnArrivalThenEasesBackToOne()
    {
        var home = new Vector2(400, 300);
        var sprite = MakeSprite(5, home, new Vector2(900, 350));
        var figure = sprite.FindChild("Sprite", true, false) as TextureRect;
        try
        {
            // Rally point == Home: arrives on the very first Advance call, so the squash
            // trigger is isolated from any dependence on the gate/home walking distance.
            sprite.BeginDeparture(home, 0f);
            sprite.Advance(0.016, 0.016);
            AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Rallying); // now dwelling
            AssertThat(figure!.Scale != Vector2.One).IsTrue(); // squashed, mid-ease

            sprite.Advance(0.3, 0.316); // more than SquashDuration (0.2s) later
            AssertThat(figure.Scale).IsEqual(Vector2.One); // settled bit-exact
        }
        finally
        {
            sprite.Free();
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
        var early = MakeSprite(10, home, gate);
        var late = MakeSprite(11, home, gate);
        try
        {
            early.BeginDeparture(home, 0f);
            late.BeginDeparture(home, HeroSprite.RallyDwellSeconds); // one dwell-length further back in file

            var t = HeroSprite.RallyDwellSeconds + 0.05; // just past the EARLY slot's total wait
            early.Advance(t, t);
            late.Advance(t, t);

            AssertThat(early.State).IsEqual(HeroSprite.TownState.WalkingOut); // already peeled off
            AssertThat(late.State).IsEqual(HeroSprite.TownState.Rallying); // still waiting its turn
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
        while (!HeroSprite.VisitsAnchorOn(heroValue, day))
        {
            day++;
        }

        var home = new Vector2(400, 300);
        var sprite = MakeSprite(heroValue, home, new Vector2(900, 350));
        sprite.Day = day;
        try
        {
            sprite.Advance(0.016, 0.016); // early in the window (< AnchorPauseSeconds)
            var anchor = HeroSprite.AnchorFor(heroValue, day);
            AssertThat(sprite.Position.DistanceTo(anchor) < 3f).IsTrue(); // breath bob only, ±1.5px
        }
        finally
        {
            sprite.Free();
        }
    }

    [TestCase]
    public void BeginRecruitWalkIn_SpawnsOffscreenLeftThenWalksHome()
    {
        var home = new Vector2(400, 300);
        var sprite = MakeSprite(6, home, new Vector2(900, 350));
        try
        {
            sprite.BeginRecruitWalkIn();
            AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingIn);
            AssertThat(sprite.Visible).IsTrue();
            AssertThat(sprite.Position.X < 0f).IsTrue(); // off-screen left, never popped in at Home

            sprite.Advance(10, 10.0); // one big fast-forward step covers the whole walk
            AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Wandering);
            AssertThat(sprite.Position.DistanceTo(home) < 3f).IsTrue();
        }
        finally
        {
            sprite.Free();
        }
    }
}
#endif
