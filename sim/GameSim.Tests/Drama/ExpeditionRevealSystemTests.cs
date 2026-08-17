using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

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

    // ---- Ladder graduation (owner ruling 2026-08-10, plan 2026-08-10-003 L1, §11.8's fix) ----

    [Fact]
    public void ClearingTheBottomFloor_PromotesSurvivingSameRankMembers_EmitsOneVenueGraduated()
    {
        // Fresh heroes start at LadderRank 0 (NewWorld default) — the Mine's own rank. Clearing its
        // bottom floor (5) graduates every surviving member to rank 1, in ONE event naming both.
        var state = NewWorld();
        var result = Result(party: [1, 2], survivors: [1, 2], deaths: [], targetFloor: 5, deepestCleared: 5);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(1, tick.NewState.Heroes[1].LadderRank);
        Assert.Equal(1, tick.NewState.Heroes[2].LadderRank);

        var graduated = Assert.Single(tick.Events.OfType<VenueGraduated>());
        Assert.Equal("mine", graduated.VenueId);
        Assert.Equal(1, graduated.NewRank);
        Assert.Equal(new[] { new HeroId(1), new HeroId(2) }, graduated.Graduates);
    }

    [Fact]
    public void DeadHeroes_NeverGraduate_EvenOnABottomFloorClear()
    {
        var state = NewWorld();
        var result = Result(
            party: [1, 2], survivors: [1], deaths: [2],
            targetFloor: 5, deepestCleared: 5,
            floors: [new FloorOutcome(5, false, [Combat(5, 2, "The Forgeworm", taken: 40)])]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(1, tick.NewState.Heroes[1].LadderRank); // survivor graduates
        Assert.Equal(0, tick.NewState.Heroes[2].LadderRank); // dead hero never does

        var graduated = Assert.Single(tick.Events.OfType<VenueGraduated>());
        Assert.Equal(new[] { new HeroId(1) }, graduated.Graduates); // the dead hero is not named
    }

    [Fact]
    public void HeroAlreadyAboveTheVenuesRank_DoesNotReGraduate_Idempotent()
    {
        // Monotonicity guard: a hero already past the Mine's rank (0) — say she graduated Gloomwood
        // already — must not increment again just because a Mine run cleared floor 5. This is also
        // the multi-clear-same-day guard: a teammate's earlier result this same Evening already
        // promoted her.
        var state = NewWorld();
        state = state with { Heroes = state.Heroes.SetItem(1, state.Heroes[1] with { LadderRank = 1 }) };
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 5);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(1, tick.NewState.Heroes[1].LadderRank); // unchanged — NOT bumped to 2
        Assert.Empty(tick.Events.OfType<VenueGraduated>()); // nobody qualified — no event at all
    }

    [Fact]
    public void ClearingShortOfTheBottomFloor_NeverGraduates()
    {
        // The Mine is 5 floors deep — clearing floor 4 (its own competence-retreat ceiling for a
        // fresh party) is real progress but not a graduation.
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 4, deepestCleared: 4);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(0, tick.NewState.Heroes[1].LadderRank);
        Assert.Empty(tick.Events.OfType<VenueGraduated>());
    }

    [Fact]
    public void ClearingAHigherRungsBottomFloor_UsesThatVenuesRank()
    {
        // Graduation reads the CLEARED venue's own rank, not always the Mine's — a rank-1 hero who
        // clears Gloomwood's bottom floor (4) graduates to rank 2, the same mechanism one rung up.
        var state = NewWorld();
        state = state with { Heroes = state.Heroes.SetItem(1, state.Heroes[1] with { LadderRank = 1 }) };
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 4, deepestCleared: 4)
            with { VenueId = "gloomwood" };

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(2, tick.NewState.Heroes[1].LadderRank);
        var graduated = Assert.Single(tick.Events.OfType<VenueGraduated>());
        Assert.Equal("gloomwood", graduated.VenueId);
        Assert.Equal(2, graduated.NewRank);
    }

    [Fact]
    public void RankOnlyEverIncrements_AcrossASequenceOfReveals()
    {
        // A hero below the venue's rank never graduates (only same-rank does), and a hero at or
        // above never regresses — pinned across a short SEQUENCE of reveals, not just one call.
        var state = NewWorld();
        state = state with { Heroes = state.Heroes.SetItem(1, state.Heroes[1] with { LadderRank = 0 }) };

        // Day 1: clears the Mine's bottom floor — 0 -> 1.
        var afterFirst = TickEvening(AtEvening(state, Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 5)));
        Assert.Equal(1, afterFirst.NewState.Heroes[1].LadderRank);

        // Day 2: another Mine clear — she is already rank 1, above the Mine's rank 0, so this is a
        // no-op (idempotent), never a decrement or a second bump.
        var afterSecond = TickEvening(AtEvening(afterFirst.NewState, Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 5)));
        Assert.Equal(1, afterSecond.NewState.Heroes[1].LadderRank);
        Assert.Empty(afterSecond.Events.OfType<VenueGraduated>());

        // Day 3: clears Gloomwood's bottom floor (rank 1, matching her current rank) — 1 -> 2.
        var afterThird = TickEvening(AtEvening(
            afterSecond.NewState,
            Result(party: [1], survivors: [1], deaths: [], targetFloor: 4, deepestCleared: 4) with { VenueId = "gloomwood" }));
        Assert.Equal(2, afterThird.NewState.Heroes[1].LadderRank);
    }

    [Fact]
    public void BountyDrivenClear_AtTheMine_StillGraduates()
    {
        // L1 scope item 5: the pre-router bounty short-circuit is unaffected by the ladder — a
        // bounty-driven expedition is still an ordinary ExpeditionResult (VenueId "mine" by
        // contract default), so clearing floor 5 under a bounty graduates exactly like any other
        // Mine clear. Graduation reads ONLY the result's venue/floor/survivors — never whether a
        // bounty drove the trip.
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 5);
        Assert.Equal("mine", result.VenueId); // contract default — this IS the bounty-scoped venue

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(1, tick.NewState.Heroes[1].LadderRank);
        Assert.Single(tick.Events.OfType<VenueGraduated>());
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

    // ---- XP + cosmetic rank (Phase B B1c, R-B3) ----

    [Fact]
    public void Survivor_AccruesXp_ForSurvivalAndDepth_NoBeats()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 3, deepestCleared: 3);

        var tick = TickEvening(AtEvening(state, result));

        // 10 (survive) + 3 floors * 5 = 25. No beats credited (none in this result).
        Assert.Equal(25, tick.NewState.Heroes[1].Xp);
        Assert.Empty(tick.Events.OfType<HeroRankUp>()); // 25 XP stays under the Delver (50) threshold
    }

    [Fact]
    public void Survivor_AccruesExtraXp_ForCreditedKillsAndSaves_NotFromLifetimeMemories()
    {
        var blade = PlayerItem(30, "Fine Iron Blade", ItemSlot.Weapon, attack: 8, defense: 0);
        var state = Equip(NewWorld(), heroId: 1, blade);
        var result = Result(
            party: [1], survivors: [1], deaths: [],
            targetFloor: 1, deepestCleared: 1,
            beats:
            [
                new AttributionBeat(BeatType.KillingBlow, blade.Id, new HeroId(1), 1, "detail"),
                new AttributionBeat(BeatType.LethalSave, blade.Id, new HeroId(1), 1, "detail"),
            ]);

        var tick = TickEvening(AtEvening(state, result));

        // 10 (survive) + 1 floor * 5 + 2 credited beats * 15 = 45 — computed off THIS expedition's
        // beats, not off the hero's post-reveal Memories tally (which would also read 1 kill + 1
        // save here, but must never be double-summed against a running lifetime total).
        Assert.Equal(45, tick.NewState.Heroes[1].Xp);
    }

    [Fact]
    public void BreakpointBeat_IsNotCreditedAsXpBeat()
    {
        // Only KillingBlow/LethalSave count — BreakpointClear has no per-item tally either (see the
        // BreakpointBeat_EmitsEvent_ButNoHistoryOrMemoryTally test above) and must not inflate XP.
        var blade = PlayerItem(31, "Gatebreaker", ItemSlot.Weapon, attack: 9, defense: 0);
        var state = Equip(NewWorld(), heroId: 1, blade);
        var result = Result(
            party: [1], survivors: [1], deaths: [],
            targetFloor: 1, deepestCleared: 1,
            beats: [new AttributionBeat(BeatType.BreakpointClear, blade.Id, new HeroId(1), 1, "detail")]);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(15, tick.NewState.Heroes[1].Xp); // 10 (survive) + 1 floor * 5, zero beat credit
    }

    [Fact]
    public void DeadHeroes_NeverAccrueXp()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [], deaths: [1], targetFloor: 3, deepestCleared: 2);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(0, tick.NewState.Heroes[1].Xp);
    }

    [Fact]
    public void CrossingARankThreshold_EmitsNamedHeroRankUp()
    {
        var state = NewWorld();
        // 10 (survive) + 10 floors * 5 = 60 — crosses the Delver (50) threshold from Novice (0).
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 10, deepestCleared: 10);

        var tick = TickEvening(AtEvening(state, result));

        var rankUp = Assert.Single(tick.Events.OfType<HeroRankUp>());
        Assert.Equal(new HeroId(1), rankUp.Hero);
        Assert.Equal("Delver", rankUp.Rank);
    }

    [Fact]
    public void XpAccrual_WritesLevelInLockstepWithRank_PhaseCFlip()
    {
        // Phase C (U-C6): the flip. CombatMath.cs:29,32 read Hero.Level into Attack/Defense —
        // crossing the Delver (50 XP) threshold now ALSO sets the real Level to 2 (rank index 1 + 1),
        // off the SAME HeroRank ladder that names the rank. This is the deliberate Class-2/Balance
        // break KTD-B2 deferred to Phase C.
        var state = NewWorld();
        var levelBefore = state.Heroes[1].Level;
        // 10 (survive) + 10 floors * 5 = 60 — crosses the Delver (50) threshold from Novice (0).
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 10, deepestCleared: 10);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(1, levelBefore);
        Assert.Equal(2, tick.NewState.Heroes[1].Level);
    }

    [Fact]
    public void XpAccrual_BelowFirstThreshold_LevelStaysAtNovice()
    {
        // No rank-up yet (still Novice) — Level must stay 1, not drift ahead of rank.
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 1, deepestCleared: 1);

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(1, tick.NewState.Heroes[1].Level);
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
        // §11.14.8: one DecisionExplained per result, same order as the PartyReturned pair above.
        Assert.Equal(2, tick.Events.OfType<DecisionExplained>().Count());
    }

    // ---- §11.14.8 ("the reveal deletes its own evidence") ----

    /// <summary>
    /// The named defect this unit fixes: before this, <see cref="ExpeditionResult.Halt"/> and its
    /// recorded rolls died with <see cref="GameState.PendingExpeditions"/> the same tick this system
    /// cleared it, so nothing in <see cref="GameState"/> ever said why a party stopped short of its
    /// target once Evening passed. The persisted <see cref="DecisionExplained"/> is that fix — every
    /// field it carries already lived on <see cref="ExpeditionResult"/>, only the destination is new.
    /// </summary>
    [Fact]
    public void Reveal_EmitsDecisionExplained_NamingTheHaltAndSummary()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 3)
            with { Halt = ExpeditionHalt.TooHurt, VenueId = "mine" };

        var tick = TickEvening(AtEvening(state, result));

        var explained = Assert.Single(tick.Events.OfType<DecisionExplained>());
        Assert.Equal("expedition-halt:mine", explained.What);
        Assert.Equal("TooHurt", explained.Chosen);
        Assert.Equal("1 survived, 0 dead, cleared 3/5, 0 floors fought", explained.Reason);
        Assert.Equal(-1, explained.Candidates); // no meaningful candidate count for a halt reason
    }

    /// <summary>Unconditional emission (mirrors the removed client-only predecessor's own behavior,
    /// <c>godot/scripts/DecisionEvents.cs</c>'s former <c>LogRevealed</c>): a clean TargetReached is
    /// exactly as worth a durable record as a limp home — no special-casing the success path away.</summary>
    [Fact]
    public void Reveal_EmitsDecisionExplained_EvenOnACleanTargetReached()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 3, deepestCleared: 3);

        var tick = TickEvening(AtEvening(state, result));

        var explained = Assert.Single(tick.Events.OfType<DecisionExplained>());
        Assert.Equal(ExpeditionHalt.TargetReached.ToString(), explained.Chosen);
    }

    /// <summary>
    /// The golden re-baseline's own proof obligation (§11.14.10 process notes): this event must add
    /// zero RNG draws. <see cref="ExpeditionRevealSystem"/> draws none by construction — its
    /// <c>Reveal</c> helper never touches the <c>rng</c> parameter <c>Process</c> receives at all —
    /// so the kernel's RNG position after this tick is bit-identical to before it.
    /// </summary>
    [Fact]
    public void Reveal_DrawsNoRng_RngStateUnchangedAcrossTheEveningTick()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 3)
            with { Halt = ExpeditionHalt.TooHurt };
        var before = state.Rng;

        var tick = TickEvening(AtEvening(state, result));

        Assert.Equal(before, tick.NewState.Rng);
    }

    /// <summary>The whole point: unlike <see cref="DecisionTrace"/> (never serialized — see that
    /// type's own doc), <see cref="DecisionExplained"/> is a normal <see cref="GameEvent"/> and MUST
    /// survive a save round-trip, or the reveal's evidence is still lost, just one tick later.</summary>
    [Fact]
    public void Reveal_DecisionExplained_SurvivesASaveRoundTrip()
    {
        var state = NewWorld();
        var result = Result(party: [1], survivors: [1], deaths: [], targetFloor: 5, deepestCleared: 3)
            with { Halt = ExpeditionHalt.GateHeld, VenueId = "mine" };

        var tick = TickEvening(AtEvening(state, result));
        var json = SaveCodec.Serialize(tick.NewState);

        Assert.Contains("expedition-halt:mine", json);
        Assert.Contains("GateHeld", json);

        var reloaded = SaveCodec.Deserialize(json);
        var explained = Assert.Single(reloaded.EventLog.OfType<DecisionExplained>());
        Assert.Equal("expedition-halt:mine", explained.What);
        Assert.Equal("GateHeld", explained.Chosen);
    }
}
