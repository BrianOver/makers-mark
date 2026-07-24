using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim; // GameComposition

namespace GameSim.Tests.Heroes;

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U13): <see cref="CommissionSystem"/> posts a commission
/// for ANY alive hero with an empty/sub-par gear slot (WIDENED design — not gated on
/// <see cref="RelationshipBand"/>), scaled by target floor + band, capped at
/// <see cref="CommissionSystem.MaxOpenCommissions"/> concurrently OPEN commissions, and carries the
/// U1 held-Morning guard.
/// </summary>
public class CommissionSystemTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, GearSet? gear = null, int deepestFloor = 0, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: 200,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: deepestFloor, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, QualityGrade quality, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, quality,
        new ItemStats(1, 1, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState BaseState(params Hero[] heroes) =>
        GameFactory.NewGame(seed: 900) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        };

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var system = new CommissionSystem();
        var sink = new TestSink();
        var after = system.Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    [Fact]
    public void HeroWithEmptyWeaponSlot_MusteringToFloor1_GetsCommissionPosted_WithExpectedValues()
    {
        var hero = MakeHero(1); // GearSet.Empty, DeepestFloorReached 0 -> target floor 1, Stranger band
        var state = BaseState(hero);

        var (after, events) = Run(state);

        var posted = Assert.Single(events.OfType<CommissionPosted>());
        Assert.Equal(hero.Id, posted.Hero);
        Assert.Equal(ItemSlot.Weapon, posted.Slot); // first gap in fixed Weapon/Shield/Armor order
        Assert.Equal(QualityGrade.Common, posted.MinQuality); // floor 1 + Stranger band -> Common
        Assert.Equal(state.Day + CommissionSystem.DeadlineWindowDays, posted.DeadlineDay);
        Assert.Equal(CommissionSystem.BasePremiumGold + CommissionSystem.PremiumPerFloor, posted.PremiumGold); // 15 + 10*1 + 0

        var commission = Assert.Single(after.Commissions);
        Assert.Equal(hero.Id, commission.Hero);
        Assert.False(commission.Accepted);
    }

    [Fact]
    public void FullyKittedHero_AtOrAboveFloorQuality_GetsNoCommission()
    {
        var weapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var shield = MakeItem(2, ItemSlot.Shield, QualityGrade.Common);
        var armor = MakeItem(3, ItemSlot.Armor, QualityGrade.Common);
        var gear = new GearSet(weapon.Id, shield.Id, armor.Id);
        var hero = MakeHero(1, gear);
        var state = BaseState(hero) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, weapon).Add(2, shield).Add(3, armor),
        };

        var (after, events) = Run(state);

        Assert.Empty(events.OfType<CommissionPosted>());
        Assert.Empty(after.Commissions);
    }

    [Fact]
    public void WornGear_BelowFloorImpliedQuality_CountsAsSubPar_GetsCommissioned()
    {
        // Floor 5 (DeepestFloorReached 4 -> target floor 5) demands Superior; a Poor-quality worn
        // weapon is "as good as empty" for commission purposes, even though every slot is filled.
        var poorWeapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Poor);
        var fineShield = MakeItem(2, ItemSlot.Shield, QualityGrade.Superior);
        var fineArmor = MakeItem(3, ItemSlot.Armor, QualityGrade.Superior);
        var gear = new GearSet(poorWeapon.Id, fineShield.Id, fineArmor.Id);
        var hero = MakeHero(1, gear, deepestFloor: 4);
        var state = BaseState(hero) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(1, poorWeapon).Add(2, fineShield).Add(3, fineArmor),
        };

        var (after, events) = Run(state);

        var posted = Assert.Single(events.OfType<CommissionPosted>());
        Assert.Equal(ItemSlot.Weapon, posted.Slot);
        Assert.Equal(QualityGrade.Superior, posted.MinQuality); // floor 5 bar
        Assert.Single(after.Commissions);
    }

    [Fact]
    public void StrangerHero_NotGatedByBand_StillGetsCommissioned()
    {
        // Widened design (2026-07-24 walkthrough): posting is NOT gated on relationship band —
        // a Stranger with a gap gets a commission exactly like a Sworn regular would.
        var hero = MakeHero(1); // mood 0, no purchases -> Stranger band
        var state = BaseState(hero);
        Assert.Equal(RelationshipBand.Stranger, RelationshipBands.For(hero.Id, state));

        var (_, events) = Run(state);

        Assert.Single(events.OfType<CommissionPosted>());
    }

    [Fact]
    public void CapAtMaxOpenCommissions_AcrossMultipleEligibleHeroes()
    {
        var heroes = Enumerable.Range(1, 5).Select(id => MakeHero(id)).ToArray();
        var state = BaseState(heroes);

        var (after, events) = Run(state);

        var posted = events.OfType<CommissionPosted>().ToList();
        Assert.Equal(CommissionSystem.MaxOpenCommissions, posted.Count);
        Assert.Equal(CommissionSystem.MaxOpenCommissions, after.Commissions.Count);

        // Deterministic: the lowest HeroIds win the capped slots (ascending scan order).
        var commissionedIds = posted.Select(p => p.Hero.Value).OrderBy(v => v).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, commissionedIds);
    }

    [Fact]
    public void CapReached_NoMoreCommissionsPosted_EvenWithMoreEligibleHeroes()
    {
        var heroes = Enumerable.Range(1, 5).Select(id => MakeHero(id)).ToArray();
        var state = BaseState(heroes) with
        {
            Commissions = ImmutableList.Create(
                new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25),
                new Commission(new HeroId(2), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25),
                new Commission(new HeroId(3), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25)),
        };

        var (after, events) = Run(state);

        Assert.Empty(events.OfType<CommissionPosted>());
        Assert.Equal(3, after.Commissions.Count);
    }

    [Fact]
    public void HeldMorning_OpenUnclosedCounter_GuardBlocks_NoEventsNoCommissions()
    {
        var hero = MakeHero(1);
        var state = BaseState(hero) with
        {
            Counter = CounterState.Empty with { Closed = false },
        };

        var (after, events) = Run(state);

        Assert.Empty(events);
        Assert.Empty(after.Commissions);
    }

    [Fact]
    public void HeldCounterMorning_AcrossMultipleTicks_CommissionPostedOnlyOnce()
    {
        // Mirrors RentHeldMorningTests' shape: open the counter, sit through several held Morning
        // ticks (no items presented, so nothing closes the session), then close explicitly. Across
        // the whole held session a single eligible hero must be commissioned exactly once, not once
        // per tick.
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 9001);

        var totalPosted = 0;

        var open = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));
        state = open.NewState;
        totalPosted += open.Events.OfType<CommissionPosted>().Count();
        Assert.Equal(DayPhase.Morning, state.Phase);

        for (var i = 0; i < 3; i++)
        {
            var held = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = held.NewState;
            totalPosted += held.Events.OfType<CommissionPosted>().Count();
            Assert.Equal(DayPhase.Morning, state.Phase);
        }

        var close = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CloseCounterAction()));
        totalPosted += close.Events.OfType<CommissionPosted>().Count();

        // The starting roster has empty-slot heroes, so at least one commission should exist, but
        // never more than the cap, and never a duplicate for the same hero across the held session.
        Assert.True(totalPosted <= CommissionSystem.MaxOpenCommissions);
        var distinctHeroesCommissioned = close.NewState.Commissions.Select(c => c.Hero).Distinct().Count();
        Assert.Equal(totalPosted, distinctHeroesCommissioned);
    }

    [Fact]
    public void SameSeed_TwoRuns_IdenticalCommissions_ByteMatch()
    {
        string RunDay()
        {
            var kernel = GameComposition.BuildKernel();
            var state = GameComposition.NewCampaign(seed: 12345);
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Morning
            return SaveCodec.Serialize(state);
        }

        Assert.Equal(RunDay(), RunDay());
    }
}
