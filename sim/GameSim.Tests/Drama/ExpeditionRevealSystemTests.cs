using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// The Evening reveal (U8): pending <see cref="ExpeditionResult"/>s become world
/// changes and ledger events. Covers AE1's surface half and AE6's death half.
/// </summary>
public class ExpeditionRevealSystemTests
{
    // ---- Attribution beats (F3, AE1 surface half) ----

    [Fact]
    public void KillingBlowBeat_EmitsEvent_AppendsKillHistory_AndHeroMemory()
    {
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, attack: 8, defense: 0);
        var state = Equip(NewWorld(), heroId: 1, blade);
        var result = Result(
            party: [1], survivors: [1], deaths: [],
            targetFloor: 2, deepestCleared: 2,
            beats: [new AttributionBeat(BeatType.KillingBlow, blade.Id, new HeroId(1), 2, "Fine Iron Blade landed the killing blow on the Tunnel Spider")]);

        var tick = TickEvening(AtEvening(state, result));

        var beat = Assert.Single(tick.Events.OfType<AttributionBeatEvent>());
        Assert.Equal(BeatType.KillingBlow, beat.Beat);
        Assert.Equal(blade.Id, beat.Item);
        Assert.Equal(new HeroId(1), beat.Hero);
        Assert.Equal(2, beat.Floor);

        var history = Assert.Single(tick.NewState.Items[10].History);
        Assert.Equal("kill", history.Kind);
        Assert.Equal(1, history.Day);

        var memory = Assert.Single(tick.NewState.Heroes[1].Memories);
        Assert.Equal(blade.Id, memory.Item);
        Assert.Equal(1, memory.Kills);
        Assert.Equal(0, memory.Saves);
    }

    [Fact]
    public void LethalSaveBeat_AppendsSaveHistory_AndSaveMemory()
    {
        var shield = PlayerItem(11, "Oathkeeper Aegis", ItemSlot.Shield, attack: 0, defense: 7);
        var state = Equip(NewWorld(), heroId: 1, shield);
        var result = Result(
            party: [1], survivors: [1], deaths: [],
            beats: [new AttributionBeat(BeatType.LethalSave, shield.Id, new HeroId(1), 1, "Oathkeeper Aegis turned a lethal Cave Rat hit")]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(BeatType.LethalSave, Assert.Single(tick.Events.OfType<AttributionBeatEvent>()).Beat);
        Assert.Equal("save", Assert.Single(tick.NewState.Items[11].History).Kind);
        var memory = Assert.Single(tick.NewState.Heroes[1].Memories);
        Assert.Equal(0, memory.Kills);
        Assert.Equal(1, memory.Saves);
    }

    [Fact]
    public void BreakpointBeat_EmitsEvent_ButNoHistoryOrMemoryTally()
    {
        // Documented policy: breakpoint clears surface as events (and gossip) only —
        // per-item tallies count kills and saves (R12), and ItemMemory has no third counter.
        var blade = PlayerItem(12, "Gatebreaker", ItemSlot.Weapon, attack: 9, defense: 0);
        var state = Equip(NewWorld(), heroId: 1, blade);
        var result = Result(
            party: [1], survivors: [1], deaths: [],
            beats: [new AttributionBeat(BeatType.BreakpointClear, blade.Id, new HeroId(1), 1, "Gatebreaker carried the party past the floor 1 gate")]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Single(tick.Events.OfType<AttributionBeatEvent>());
        Assert.Empty(tick.NewState.Items[12].History);
        Assert.Empty(tick.NewState.Heroes[1].Memories);
    }

    // ---- Deaths (R13/F4, AE6 death half) ----

    [Fact]
    public void Death_EmitsHeroDiedNamingWornGear_FlipsAlive_AddsMemorial()
    {
        var rusty = RivalItem(20, "Rusty Sword", ItemSlot.Weapon, attack: 3, defense: 0);
        var plate = PlayerItem(21, "Oathkeeper Plate", ItemSlot.Armor, attack: 0, defense: 6);
        var state = Equip(Equip(NewWorld(), 1, rusty), 1, plate);
        var wornGear = state.Heroes[1].Gear;
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            targetFloor: 2, deepestCleared: 1,
            floors:
            [
                new FloorOutcome(1, true, [Combat(1, 1, "Cave Rat", monsterKilled: true, killingItem: 20)]),
                new FloorOutcome(2, false, [Combat(2, 1, "Tunnel Spider", taken: 30)]),
            ]);

        var tick = TickEvening(AtEvening(state, result));

        var died = Assert.Single(tick.Events.OfType<HeroDied>());
        Assert.Equal(new HeroId(1), died.Hero);
        Assert.Equal(2, died.Floor); // deepest floor attempted — where the fatal combat happened
        Assert.Equal("slain by a Tunnel Spider", died.Cause);
        Assert.Equal(wornGear, died.WornGear);

        var hero = tick.NewState.Heroes[1];
        Assert.False(hero.Alive);
        Assert.Equal(1, hero.DiedOnDay);

        var memorial = Assert.Single(tick.NewState.Drama.Memorials);
        Assert.Equal(new HeroId(1), memorial.Hero);
        Assert.Equal("Torvald", memorial.HeroName);
        Assert.Equal(1, memorial.Day);
        // Player-crafted pieces lead the epitaph (R13).
        Assert.Contains("Oathkeeper Plate", memorial.GearNamed);
        Assert.Contains("Rusty Sword", memorial.GearNamed);
        Assert.True(
            memorial.GearNamed.IndexOf("Oathkeeper Plate", StringComparison.Ordinal)
                < memorial.GearNamed.IndexOf("Rusty Sword", StringComparison.Ordinal),
            $"player-crafted piece must be named first: '{memorial.GearNamed}'");
    }

    [Fact]
    public void TheForgeworm_CauseSkipsTheArticle()
    {
        var state = NewWorld();
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            targetFloor: 5, deepestCleared: 4,
            floors: [new FloorOutcome(5, false, [Combat(5, 1, "The Forgeworm", taken: 40)])]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal("slain by The Forgeworm", Assert.Single(tick.Events.OfType<HeroDied>()).Cause);
    }

    // ---- Gold (R17) ----

    [Fact]
    public void SurvivorGold_AppliedViaLootIncome()
    {
        var state = NewWorld(); // Torvald starts with 40g
        var result = Result(party: [1], survivors: [1], deaths: [], gold: [(1, 16)]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(56, tick.NewState.Heroes[1].Gold);
    }

    // ---- Depths board (R15) ----

    [Fact]
    public void DepthsBoard_UpdatesOnlyOnNewPersonalRecords()
    {
        var state = NewWorld();
        var veteran = state.Heroes[1] with { DeepestFloorReached = 3 };
        state = state with
        {
            Heroes = state.Heroes.SetItem(1, veteran),
            Drama = state.Drama with { DepthsBoard = state.Drama.DepthsBoard.SetItem(1, 3) },
        };

        // Shallower run: no record, no event, board untouched.
        var shallow = TickEvening(AtEvening(state, Result(party: [1], survivors: [1], deaths: [], targetFloor: 2, deepestCleared: 2)));
        Assert.Empty(shallow.Events.OfType<FloorRecordSet>());
        Assert.Equal(3, shallow.NewState.Heroes[1].DeepestFloorReached);
        Assert.Equal(3, shallow.NewState.Drama.DepthsBoard[1]);

        // Deeper run: record set, event emitted, board updated.
        var deeper = TickEvening(AtEvening(
            shallow.NewState,
            Result(party: [1], survivors: [1], deaths: [], targetFloor: 4, deepestCleared: 4)));
        var record = Assert.Single(deeper.Events.OfType<FloorRecordSet>());
        Assert.Equal(new HeroId(1), record.Hero);
        Assert.Equal(4, record.Floor);
        Assert.Equal(4, deeper.NewState.Heroes[1].DeepestFloorReached);
        Assert.Equal(4, deeper.NewState.Drama.DepthsBoard[1]);
    }

    [Fact]
    public void DeadHeroes_SetNoRecords()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [], deaths: [1], targetFloor: 2, deepestCleared: 2);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Empty(tick.Events.OfType<FloorRecordSet>());
        Assert.Equal(0, tick.NewState.Heroes[1].DeepestFloorReached);
    }

    // ---- Ore market (R6) ----

    [Fact]
    public void Loot_BecomesFloorScaledOreOffers()
    {
        var state = NewWorld();
        var result = Result(
            party: [1], survivors: [1], deaths: [],
            targetFloor: 5, deepestCleared: 5,
            loot: [new OreLoot(new HeroId(1), "copper", 2), new OreLoot(new HeroId(1), "adamant", 1)]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(2, tick.NewState.OpenOreOffers.Count);
        var copper = tick.NewState.OpenOreOffers[0];
        Assert.Equal("copper", copper.MaterialKey);
        Assert.Equal(2, copper.Quantity);
        Assert.Equal(3, copper.UnitPrice);
        var adamant = tick.NewState.OpenOreOffers[1];
        Assert.Equal("adamant", adamant.MaterialKey);
        Assert.Equal(18, adamant.UnitPrice);

        // Mirrored as events for the ledger/log (R6).
        Assert.Equal(2, tick.Events.OfType<OreOffered>().Count());
    }

    [Fact]
    public void StaleOffers_ClearedAfterOneEvening()
    {
        var state = NewWorld() with
        {
            OpenOreOffers = ImmutableList.Create(
                new OreOffered(new HeroId(1), "copper", 3, 3) { Day = 1 }),
        };

        // A quiet Evening (no returning parties) still sweeps yesterday's market.
        var tick = TickEvening(state with { Phase = DayPhase.Evening });

        Assert.Empty(tick.NewState.OpenOreOffers);
    }

    [Fact]
    public void DeadHeroesLoot_NeverReachesTheMarket()
    {
        var state = NewWorld();
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            floors: [new FloorOutcome(1, false, [Combat(1, 1, "Cave Rat", taken: 40)])],
            loot: [new OreLoot(new HeroId(1), "copper", 2)]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Empty(tick.NewState.OpenOreOffers);
        Assert.Empty(tick.Events.OfType<OreOffered>());
    }

    // ---- Wipe and bookkeeping edges ----

    [Fact]
    public void FullPartyWipe_CoherentOutput()
    {
        var state = NewWorld();
        var goldBefore = new[] { 1, 2, 3 }.ToDictionary(id => id, id => state.Heroes[id].Gold);
        var result = Result(
            party: [1, 2, 3], survivors: [], deaths: [1, 2, 3],
            targetFloor: 2, deepestCleared: 0,
            floors:
            [
                new FloorOutcome(1, false,
                [
                    Combat(1, 1, "Cave Rat", taken: 40),
                    Combat(1, 2, "Cave Rat", taken: 40),
                    Combat(1, 3, "Cave Rat", taken: 40),
                ]),
            ],
            loot: [new OreLoot(new HeroId(1), "copper", 2)],
            gold: [(1, 8), (3, 5)]);

        var tick = TickEvening(AtEvening(state, result));

        var returned = Assert.Single(tick.Events.OfType<PartyReturned>());
        Assert.Empty(returned.Survivors);
        Assert.Equal(3, tick.Events.OfType<HeroDied>().Count());
        Assert.Equal(3, tick.NewState.Drama.Memorials.Count);
        foreach (var id in new[] { 1, 2, 3 })
        {
            var hero = tick.NewState.Heroes[id];
            Assert.False(hero.Alive);
            Assert.Equal(1, hero.DiedOnDay);
            Assert.Equal(goldBefore[id], hero.Gold); // no survivor gold — it dies with the party
        }

        Assert.Empty(tick.Events.OfType<FloorRecordSet>());
        Assert.Empty(tick.NewState.OpenOreOffers);
        Assert.Empty(tick.NewState.PendingExpeditions);
    }

    [Fact]
    public void MultipleResults_AllRevealedInOrder_AndPendingCleared()
    {
        var state = NewWorld();
        var first = Result(party: [1, 2, 3], survivors: [1, 2, 3], deaths: []);
        var second = Result(party: [4, 5, 6], survivors: [4, 5, 6], deaths: []);

        var tick = TickEvening(AtEvening(state, first, second));

        var returns = tick.Events.OfType<PartyReturned>().ToList();
        Assert.Equal(2, returns.Count);
        Assert.Equal(new[] { 1, 2, 3 }, returns[0].Survivors.Select(h => h.Value));
        Assert.Equal(new[] { 4, 5, 6 }, returns[1].Survivors.Select(h => h.Value));
        Assert.Empty(tick.NewState.PendingExpeditions);
    }
}
