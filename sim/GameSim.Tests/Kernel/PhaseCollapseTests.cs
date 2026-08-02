using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// KTD-D(1) (2026-08-02 loop-legibility plan, U1): <see cref="GameKernel"/>'s phase machine collapses
/// Expedition/Camp/ExpeditionDeep to a single fold-to-Evening whenever nobody will go underground that
/// day — a pure, zero-RNG function of the post-Morning-systems roster (<see cref="Heroes.PartyFormation"/>'s
/// own party-formation predicate, the same one <see cref="Heroes.MusterSystem"/> already uses to predict
/// the Expedition tick one phase early). The collapse is strictly the "nobody-down-there" case: once
/// ANY party forms — however it resolves — the day walks all five phases exactly as before.
/// </summary>
public class PhaseCollapseTests
{
    private static readonly ImmutableList<PlayerAction> NoActions = ImmutableList<PlayerAction>.Empty;

    private static Hero Strong(int id, int deepest = 1) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepest, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static readonly ImmutableSortedDictionary<int, Item> StrongGear =
        new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static GameState World(Hero[] heroes, ulong seed) => GameFactory.NewGame(seed) with
    {
        Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = StrongGear,
    };

    // (a) No party forms: the day is Morning -> Evening exactly — never Expedition/Camp/ExpeditionDeep.

    [Fact]
    public void NoAliveHeroes_MorningCollapsesStraightToEvening_NeverEntersARaidPhase()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(seed: 11);
        Assert.Empty(state.Heroes); // nobody to raid — the true "nobody-down-there" case

        var observed = new List<DayPhase>();
        for (var i = 0; i < 6; i++) // three collapsed days = six ticks (Morning<->Evening only)
        {
            state = kernel.Tick(state, NoActions).NewState;
            observed.Add(state.Phase);
        }

        Assert.Equal(
            new[]
            {
                DayPhase.Evening, DayPhase.Morning,
                DayPhase.Evening, DayPhase.Morning,
                DayPhase.Evening, DayPhase.Morning,
            },
            observed);
        Assert.DoesNotContain(DayPhase.Expedition, observed);
        Assert.DoesNotContain(DayPhase.Camp, observed);
        Assert.DoesNotContain(DayPhase.ExpeditionDeep, observed);
        Assert.Equal(4, state.Day); // day 1 + three collapsed days
        Assert.Empty(state.InFlight);
        Assert.Empty(state.PendingExpeditions);
    }

    [Fact]
    public void AllHeroesDead_MorningCollapsesStraightToEvening_SameAsNoHeroesAtAll()
    {
        // PartyFormation.FormParties filters dead heroes internally — a roster of nothing-but-corpses
        // must collapse exactly like an empty roster (the actual predicate ExpeditionSystem itself
        // would use two ticks later, not a hand re-derivation of "alive").
        var deadRoster = new[]
        {
            Strong(1) with { Alive = false },
            Strong(2) with { Alive = false },
        };
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList<IActionHandler>.Empty);
        var state = World(deadRoster, seed: 12);

        state = kernel.Tick(state, NoActions).NewState;

        Assert.Equal((1, DayPhase.Evening), (state.Day, state.Phase));
    }

    // (b) A party forms: the day still walks all five phases, exactly as before U1.

    [Fact]
    public void PartyForms_WalksAllFivePhases_ThenNextDay()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList<IActionHandler>.Empty);
        var state = World(new[] { Strong(1), Strong(2), Strong(3) }, seed: 13);

        var observed = new List<DayPhase>();
        for (var i = 0; i < 5; i++)
        {
            state = kernel.Tick(state, NoActions).NewState;
            observed.Add(state.Phase);
        }

        Assert.Equal(
            new[] { DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening, DayPhase.Morning },
            observed);
        Assert.Equal(2, state.Day);
    }

    // (c) Recall during Camp still enters ExpeditionDeep — the collapse never reaches this boundary
    // once a party has actually formed (InFlight is populated, Camp -> ExpeditionDeep is unconditional,
    // exactly as before U1).

    [Fact]
    public void RecallDuringCamp_StillEntersExpeditionDeep_CollapseNeverAppliesOnceAPartyFormed()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList.Create<IActionHandler>(new CampHandlers()));
        var state = World(new[] { Strong(1) }, seed: 14);

        state = kernel.Tick(state, NoActions).NewState; // Morning -> Expedition
        state = kernel.Tick(state, NoActions).NewState; // Expedition -> Camp (parked)
        Assert.Equal(DayPhase.Camp, state.Phase);
        Assert.NotEmpty(state.InFlight);

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new RecallPartyAction(new HeroId(1))));
        Assert.Empty(result.Rejected);
        Assert.Contains(result.Events, e => e is PartyRecalled);

        // The recall bell rang; the party is still IN FLIGHT (Recalled=true is a flag, not a
        // departure from InFlight) — Camp -> ExpeditionDeep fires unconditionally, same as before.
        Assert.Equal(DayPhase.ExpeditionDeep, result.NewState.Phase);
        Assert.NotEmpty(result.NewState.InFlight);
        Assert.True(result.NewState.InFlight[0].Recalled);
    }

    // (d) Determinism holds across the collapse boundary.

    [Fact]
    public void SameSeed_NoParty_AcrossTheCollapseBoundary_ByteIdentical()
    {
        GameState Run()
        {
            var kernel = new GameKernel(
                ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
                ImmutableList<IActionHandler>.Empty);
            var state = GameFactory.NewGame(seed: 15);
            for (var i = 0; i < 10; i++) // five collapsed days
            {
                state = kernel.Tick(state, NoActions).NewState;
            }

            return state;
        }

        Assert.Equal(SaveCodec.Serialize(Run()), SaveCodec.Serialize(Run()));
    }
}
