#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U5 (world-and-interiors plan, KTD-8): <see cref="MarketLife2D"/>'s customer choreography — the
/// market room's own retirement replacement for the deleted <c>ShopStage</c>. Headless-safe
/// assertions only (properties/positions/queued-run data, never pixels — the emote glyph's
/// <c>_Draw()</c> is decoration and stays untested here, same contract the deleted
/// <c>ShopStageTests</c> held). Every <see cref="MarketLife2D.Advance"/> call here is a DIRECT
/// method call (never a pumped engine frame), so this suite has no SubViewport-hang exposure
/// (standing constraint 4) despite <see cref="Mount"/> building a real <see cref="Town2D"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarketLifeTests
{
    private static Town2D Mount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(seed: 2026));
        return town;
    }

    [TestCase]
    public void QueueDay_SoldAndPassedEvents_QueuesOneStaggeredRunPerRelevantEvent()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;

            var dayEvents = new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
                new HeroPassedOnItem(new HeroId(2), new ItemId(101), "can't afford at 50g — has 10g"),
                new ItemSold(new ItemId(102), new HeroId(2), 40, FromPlayerShop: false), // rival sale — ignored
                new RecruitArrived(new HeroId(3)), // unrelated event type — ignored
            };

            life.QueueDay(state, dayEvents);

            AssertThat(life.QueuedRuns.Count).IsEqual(2);

            var bought = life.QueuedRuns[0];
            AssertThat(bought.Bought).IsTrue();
            AssertThat(bought.Hero).IsEqual(new HeroId(1));
            AssertThat(bought.StartDelay).IsEqual(0.0);

            var passed = life.QueuedRuns[1];
            AssertThat(passed.Bought).IsFalse();
            AssertThat(passed.Hero).IsEqual(new HeroId(2));
            AssertThat(passed.StartDelay).IsGreater(bought.StartDelay); // staggered, not simultaneous
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void QueueDay_CounterEvents_StageIsCounterCustomerRuns()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;

            var dayEvents = new GameEvent[]
            {
                new CounterSaleClosed(new HeroId(1), new ItemId(101), 8, Pinned: true),
                new CustomerWalked(new HeroId(2), new ItemId(102), "the customer's patience ran out"),
            };

            life.QueueDay(state, dayEvents);

            AssertThat(life.QueuedRuns.Count).IsEqual(2);
            AssertThat(life.QueuedRuns.All(r => r.IsCounterCustomer)).IsTrue();
            AssertThat(life.QueuedRuns[0].Bought).IsTrue();
            AssertThat(life.QueuedRuns[0].Emote).IsEqual(MarketLife2D.EmoteKind.Heart); // pinned sale
            AssertThat(life.QueuedRuns[1].Bought).IsFalse();
            AssertThat(life.QueuedRuns[1].Emote).IsEqual(MarketLife2D.EmoteKind.Frown); // patience walk
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void QueueDay_UnresolvableIds_DegradesToNoRunNoCrash()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;

            var dayEvents = new GameEvent[]
            {
                new ItemSold(new ItemId(999), new HeroId(1), 8, FromPlayerShop: true), // no such item
                new HeroPassedOnItem(new HeroId(999), new ItemId(101), "can't afford"), // no such hero
            };

            life.QueueDay(state, dayEvents);

            AssertThat(life.QueuedRuns.Count).IsEqual(0);
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void QueueDay_NoShopEvents_NoCustomersEverSpawn()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;

            life.QueueDay(state, new GameEvent[] { new RecruitArrived(new HeroId(9)) });

            AssertThat(life.QueuedRuns.Count).IsEqual(0);
            for (var i = 0; i < 50; i++)
            {
                life.Advance(0.1);
            }

            AssertThat(life.ActiveCustomerCount).IsEqual(0);
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void ClassifySale_UndercutsSuggestedPrice_IsHeart_AtOrAboveIsSmile()
    {
        // Baseline is GameSim.Advisor.SuggestedPrice.For — for a stat-heavy Common weapon its
        // combat-stat term ((Attack + Defense) * 2 = 20) dominates the quality floor (8), so this
        // case reads identically to the pre-fix (Attack + Defense) * 2 baseline it replaced.
        var item = TestWeapon(attack: 10, defense: 0); // SuggestedPrice.For == 20

        AssertThat(MarketLife2D.ClassifySale(item, 15)).IsEqual(MarketLife2D.EmoteKind.Heart);
        AssertThat(MarketLife2D.ClassifySale(item, 20)).IsEqual(MarketLife2D.EmoteKind.Smile); // boundary
        AssertThat(MarketLife2D.ClassifySale(item, 25)).IsEqual(MarketLife2D.EmoteKind.Smile);
    }

    [TestCase]
    public void ClassifySale_Trinket_JudgesAgainstSuggestedPrice_NotDeadCombatStats()
    {
        // P2-HONEST-11 (#685/#688) / this unit (2026-09-03): a Trinket's Attack/Defense are real
        // numbers that feed SuggestedPrice.For's pricing floor but contribute NOTHING to
        // CombatMath.EffectivePower. The OLD baseline here was the raw (Attack + Defense) * 2 —
        // for this trinket that is 6, so a 7g sale never read as a bargain (7 is not < 6). The
        // fixed baseline is SuggestedPrice.For (quality floor 8 dominates the stat term here), so
        // the SAME 7g sale now correctly reads as undercutting what the item is actually suggested
        // to be worth.
        var item = TestTrinket(attack: 3, defense: 0); // stat term 6; SuggestedPrice.For == 8 (Common floor)

        AssertThat(MarketLife2D.ClassifySale(item, 7)).IsEqual(MarketLife2D.EmoteKind.Heart);
        AssertThat(MarketLife2D.ClassifySale(item, 8)).IsEqual(MarketLife2D.EmoteKind.Smile); // boundary
    }

    [TestCase]
    public void ClassifyPass_PinnedReasonMapping_UnaffordableIsFrown_EveryOtherReasonIsShrug()
    {
        AssertThat(MarketLife2D.ClassifyPass("can't afford at 45g — has 30g"))
            .IsEqual(MarketLife2D.EmoteKind.Frown);
        AssertThat(MarketLife2D.ClassifyPass("shields don't suit a striker"))
            .IsEqual(MarketLife2D.EmoteKind.Shrug);
        AssertThat(MarketLife2D.ClassifyPass("too heavy for a mystic — 5 weight, carries at most 4"))
            .IsEqual(MarketLife2D.EmoteKind.Shrug);
    }

    [TestCase]
    public void Advance_QueuedRun_WalksInJudgesAndWalksOutThenIsFreed()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;

            life.QueueDay(state, new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
            });

            AssertThat(life.ActiveCustomerCount).IsEqual(0); // still pending, not yet started

            life.Advance(0.01); // crosses the (zero) start delay — spawns the customer
            AssertThat(life.ActiveCustomerCount).IsEqual(1);
            AssertThat(life.FindChild("MarketCustomer_1", true, false)).IsNotNull();

            // One state transition can complete per Advance call by design (mirrors the deleted
            // ShopStage's own test), so a single huge delta would only cross ONE boundary — capped
            // so a stuck machine fails the test instead of looping forever.
            for (var i = 0; i < 200 && life.ActiveCustomerCount > 0; i++)
            {
                life.Advance(0.1);
            }

            AssertThat(life.ActiveCustomerCount).IsEqual(0);
            AssertThat(life.FindChild("MarketCustomer_1", true, false)).IsNull();
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>
    /// U-audio-3 (verbs that resolved silently): this whole choreography — a customer walking in,
    /// browsing, and walking out with the goods — used to play out in total silence. The coin arc
    /// IS the sale landing, and it must actually make a sound. An <see cref="AudioDirector"/> is
    /// mounted at the tree root alongside <see cref="Town2D"/> (never a bare <c>new
    /// MarketLife2D()</c>) because <c>AudioDirector.For</c> is a tree lookup from
    /// <c>GetTree().Root</c> — exactly how the real game finds it from any node — so this proves
    /// the SAME lookup production uses, not a shortcut that could pass while the real wiring is
    /// broken.
    /// </summary>
    [TestCase]
    public void Advance_SoldRun_PlaysTheCoinCue_WhenTheSaleLands()
    {
        var director = new AudioDirector();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;
            life.QueueDay(state, new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
            });

            life.Advance(0.01); // crosses the (zero) start delay — spawns the customer
            director.ClearRecentCues();

            // Capped the same way Advance_QueuedRun_WalksInJudgesAndWalksOutThenIsFreed already is:
            // enough steps to clear walk-in + the Judging dwell + walk-out, so a stuck machine fails
            // this test instead of looping forever.
            for (var i = 0; i < 200 && life.ActiveCustomerCount > 0; i++)
            {
                life.Advance(0.1);
            }

            AssertThat(life.ActiveCustomerCount)
                .OverrideFailureMessage("Precondition: the run never finished within the step budget.")
                .IsEqual(0);

            AssertThat(director.RecentCues)
                .OverrideFailureMessage(
                    $"A player-shelf sale played [{string.Join(", ", director.RecentCues)}] — Coin was "
                    + "never among them. The sale landing is the payoff of the shelf channel and must "
                    + "be audible, the same way Cue.Shelve already marks stocking it.")
                .Contains(Cue.Coin);
        }
        finally
        {
            town.Free();
            director.Free();
        }
    }

    [TestCase]
    public void Spawn_CustomerArtRoot_CarriesTheSameWorldScaleEveryTownActorUses()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;

            life.QueueDay(state, new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
            });
            life.Advance(0.01);

            var customer = life.FindChild("MarketCustomer_1", true, false) as Node2D;
            AssertThat(customer).IsNotNull();
            var art = customer!.FindChild("Art", false, false) as Node2D;
            AssertThat(art).IsNotNull();
            AssertThat(art!.Scale).IsEqual(new Vector2(TownLayout2D.CharacterSpriteScale, TownLayout2D.CharacterSpriteScale));
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void Advance_SameQueueSameDeltas_LandsAtTheSamePositions_Deterministic()
    {
        // Two full Town2D mounts, one after the other (never simultaneously — standing constraint
        // 4 is about pumping frames while a SubViewport renders, not about a live SubViewport
        // existing at all, but this sequences them anyway so there is never a moment with two
        // live at once): record the whole position trace from a fresh run, free it, repeat with
        // an identical fresh run, then compare the two traces.
        var eventsSnapshot = new GameEvent[]
        {
            new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
            new HeroPassedOnItem(new HeroId(2), new ItemId(102), "shields don't suit a striker"),
        };

        var traceA = RecordPositionTrace(eventsSnapshot);
        var traceB = RecordPositionTrace(eventsSnapshot);

        AssertThat(traceA.Count).IsEqual(traceB.Count);
        for (var step = 0; step < traceA.Count; step++)
        {
            AssertThat(traceA[step].Count).IsEqual(traceB[step].Count);
            for (var c = 0; c < traceA[step].Count; c++)
            {
                AssertThat(traceA[step][c].DistanceTo(traceB[step][c])).IsLess(0.001f);
            }
        }
    }

    private static List<List<Vector2>> RecordPositionTrace(GameEvent[] events)
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;
            life.QueueDay(state, events);

            var trace = new List<List<Vector2>>();
            for (var i = 0; i < 15; i++)
            {
                life.Advance(0.13);
                trace.Add(life.ActivePositions.ToList());
            }

            return trace;
        }
        finally
        {
            town.Free();
        }
    }

    [TestCase]
    public void CustomerJourneys_NeverEnterAnyStationFootprint_NeverLeaveTheRoomRect()
    {
        var town = Mount();
        try
        {
            var state = TwoHeroTwoItemWorld();
            var life = town.MarketLife!;
            var room = town.FindInteriorRoom("market");
            var footprints = room.Stations.Select(FootprintRect).ToArray();

            // Exercise both non-counter (alternating shelf-a/shelf-b) and counter-session paths.
            life.QueueDay(state, new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
                new HeroPassedOnItem(new HeroId(2), new ItemId(102), "shields don't suit a striker"),
                new CounterSaleClosed(new HeroId(1), new ItemId(101), 8, Pinned: false),
                new CustomerWalked(new HeroId(2), new ItemId(102), "the price never met their willingness"),
            });

            // 400 steps * 0.05s = 20 simulated seconds — comfortably past four staggered runs'
            // worth of walk-in/judge/walk-out (each well under 5s at this unit's own WalkSpeed),
            // so a stuck machine fails this test instead of looping forever. Invariants are
            // checked EVERY step, not just at the end, so a mid-journey excursion into a
            // footprint is caught even if the customer wanders back out by the next check.
            for (var i = 0; i < 400; i++)
            {
                life.Advance(0.05);

                foreach (var pos in life.ActivePositions)
                {
                    AssertThat(room.RoomRect.HasPoint(pos))
                        .OverrideFailureMessage($"customer at {pos} left the market's own RoomRect {room.RoomRect}")
                        .IsTrue();

                    foreach (var rect in footprints)
                    {
                        AssertThat(rect.HasPoint(pos))
                            .OverrideFailureMessage($"customer at {pos} entered a station footprint {rect}")
                            .IsFalse();
                    }
                }
            }

            AssertThat(life.ActiveCustomerCount).IsEqual(0); // every run actually finished within the budget
        }
        finally
        {
            town.Free();
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Rect2 FootprintRect(Building2D station)
    {
        var shape = station.Footprint.GetNode<CollisionShape2D>("FootprintShape");
        var rect = (RectangleShape2D)shape.Shape;
        var center = shape.GlobalPosition;
        var half = rect.Size / 2f;
        return new Rect2(center - half, rect.Size);
    }

    private static Item TestWeapon(int attack, int defense) => new(
        new ItemId(1), "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, defense, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item TestTrinket(int attack, int defense) => new(
        new ItemId(1), "test-recipe-trinket", "Test Charm", ItemSlot.Trinket, QualityGrade.Common,
        new ItemStats(attack, defense, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Buyer(int id) => new(
        new HeroId(id), $"Buyer{id}", "striker", Level: 1, MaxHp: 24, Gold: 100,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameState TwoHeroTwoItemWorld()
    {
        var heroes = new[] { Buyer(1), Buyer(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var items = new[]
        {
            new Item(new ItemId(101), "test-recipe-a", "Test Dagger", ItemSlot.Weapon, QualityGrade.Common,
                new ItemStats(5, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty),
            new Item(new ItemId(102), "test-recipe-b", "Test Shield", ItemSlot.Shield, QualityGrade.Common,
                new ItemStats(0, 4, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty),
        }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

        return GameFactory.NewGame(5150) with { Heroes = heroes, Items = items };
    }
}
#endif
