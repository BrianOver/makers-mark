#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U17 (world-rework plan): the adventure ticker's own presentation logic, driven directly
/// (mirrors <see cref="MineWatchTests"/>' technique of calling the component's public method
/// with a hand-built <see cref="GameState"/> + event batch rather than ticking a real
/// expedition through combat) — deterministic and fast, and it lets the KTD5 death-guard
/// scenario construct the one input shape (a <see cref="HeroDied"/> event stamped to a
/// non-Evening phase) that the real kernel would never itself produce, proving the ticker's
/// redundant lock holds even under that hypothetical.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AdventureTickerTests
{
    [TestCase]
    public void ScriptedDay_RendersItemSold_PartyDeparted_FloorRecordSet_Gossip()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var events = ImmutableList.Create<GameEvent>(
                new ItemSold(new ItemId(1), new HeroId(2), 42, FromPlayerShop: true),
                new PartyDeparted(ImmutableList.Create(new HeroId(1), new HeroId(2)), TargetFloor: 3),
                new FloorRecordSet(new HeroId(1), Floor: 5),
                new GossipEmitted(new EventId(1), "The forge ran hot all night."));

            ticker.OnPhaseCompleted(DayPhase.Evening, completedDay: 1, state, events);

            AssertThat(ticker.Lines.Count).IsEqual(4);
            AssertThat(ticker.DisplayText).Contains("Dagger sold to S1 for 42g.");
            AssertThat(ticker.DisplayText).Contains("A party of 2 departs for floor 3.");
            AssertThat(ticker.DisplayText).Contains("V1 sets a new depth record — floor 5.");
            AssertThat(ticker.DisplayText).Contains("The forge ran hot all night.");
            AssertThat(ticker.DisplayText).StartsWith("Day 1:");
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void HeroDied_BeforeEvening_NeverRenders()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var death = ImmutableList.Create<GameEvent>(
                new HeroDied(new HeroId(1), Floor: 4, Cause: "goblin", WornGear: GearSet.Empty));

            // Morning/Expedition/Camp/ExpeditionDeep — none of these is the Evening reveal tick.
            ticker.OnPhaseCompleted(DayPhase.Morning, 1, state, death);
            ticker.OnPhaseCompleted(DayPhase.Expedition, 1, state, death);
            ticker.OnPhaseCompleted(DayPhase.Camp, 1, state, death);
            ticker.OnPhaseCompleted(DayPhase.ExpeditionDeep, 1, state, death);

            AssertThat(ticker.Lines.Count).IsEqual(0);
            AssertThat(ticker.DisplayText).IsEqual(string.Empty);
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void HeroDied_AtEvening_Renders()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var death = ImmutableList.Create<GameEvent>(
                new HeroDied(new HeroId(1), Floor: 4, Cause: "goblin", WornGear: GearSet.Empty));

            ticker.OnPhaseCompleted(DayPhase.Evening, 1, state, death);

            AssertThat(ticker.Lines.Count).IsEqual(1);
            AssertThat(ticker.DisplayText).Contains("V1 did not return from floor 4.");
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void SameDayRepeat_Dedupes()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var record = new FloorRecordSet(new HeroId(1), Floor: 5);

            ticker.OnPhaseCompleted(DayPhase.Expedition, 2, state, ImmutableList.Create<GameEvent>(record));
            ticker.OnPhaseCompleted(DayPhase.ExpeditionDeep, 2, state, ImmutableList.Create<GameEvent>(record));

            AssertThat(ticker.Lines.Count).IsEqual(1);
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void EmptyDay_RendersNothing_NoPlaceholderNoise()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();

            ticker.OnPhaseCompleted(DayPhase.Camp, 1, state, ImmutableList<GameEvent>.Empty);

            AssertThat(ticker.Lines.Count).IsEqual(0);
            AssertThat(ticker.DisplayText).IsEqual(string.Empty);
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void IrrelevantEventTypes_NeverRender()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            var events = ImmutableList.Create<GameEvent>(
                new RecruitArrived(new HeroId(9)),
                new BountyPosted(new BountyId(1), TargetFloor: 3, RewardGold: 5));

            ticker.OnPhaseCompleted(DayPhase.Morning, 1, state, events);

            AssertThat(ticker.Lines.Count).IsEqual(0);
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void Tick_ScrollsMarquee_AccumulatedDeltaNoTween()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            var state = StagedWorld();
            ticker.OnPhaseCompleted(
                DayPhase.Evening, 1, state,
                ImmutableList.Create<GameEvent>(new GossipEmitted(new EventId(1), "A long enough line to actually scroll across the strip.")));

            AssertThat(ticker.Line.Position.X).IsEqual(0f);
            ticker.Tick(0.5);
            AssertThat(ticker.Line.Position.X).IsLess(0f); // scrolled left, no Tween involved
        }
        finally
        {
            ticker.Free();
        }
    }

    [TestCase]
    public void Tick_NoLines_StaysParked()
    {
        var ticker = new AdventureTicker();
        try
        {
            ticker.Build();
            ticker.Tick(1.0);
            AssertThat(ticker.Line.Position).IsEqual(Vector2.Zero);
        }
        finally
        {
            ticker.Free();
        }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Hero Delver(int id, string name, string classId, int deepestFloor = 1) => new(
        new HeroId(id), name, classId, Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepestFloor, DiedOnDay: null);

    private static Item Dagger() => new(
        new ItemId(1), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(4, 0, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>A stable, real-shaped world for the ticker's formatter to read hero/item names
    /// from — no expedition or combat is ever ticked here (mirrors <c>MineWatchTests.StagedWorld</c>).</summary>
    private static GameState StagedWorld()
    {
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "V1", "vanguard"))
            .Add(2, Delver(2, "S1", "striker"));
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, Dagger());
        return GameFactory.NewGame(9098) with { Heroes = heroes, Items = items };
    }
}
#endif
