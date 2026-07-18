using System.Collections.Immutable;
using System.Text.Json;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// Staged resolution (U3): stage 1 resolves floors [1..checkpoint] at the Expedition tick and
/// parks an <see cref="InFlightExpedition"/>; stage 2 finalizes [checkpoint+1..target] at the
/// ExpeditionDeep tick on the live kernel stream. The keystone is DIFFERENTIAL PARITY — a
/// single-party staged run is byte-identical to the unstaged <see cref="ExpeditionResolver.Resolve"/>,
/// because the floor loop and its draws are shared verbatim and nothing else draws between the ticks.
/// </summary>
public class StagedResolutionTests
{
    private static readonly ImmutableSortedDictionary<int, Item> NoItems = ImmutableSortedDictionary<int, Item>.Empty;

    private static Hero Naked(int id, int hp = 25, int deepest = 0, GearSet? gear = null, params ItemId[] pack) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: hp, Gold: 30,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: deepest, DiedOnDay: null)
    {
        Pack = pack.ToImmutableList(),
    };

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

    private static Item Salve(int id, int magnitude = 6) => new(
        new ItemId(id), "salve", "Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, magnitude));

    private static readonly ImmutableSortedDictionary<int, Item> StrongGear =
        new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    // ── The keystone: single-party staged == unstaged, byte-for-byte ──────────────────────

    [Fact]
    public void DifferentialParity_SingleParty_StagedEqualsUnstaged_ByteForByte()
    {
        // For any party/seed/target, ResolveStage1(checkpoint)+ResolveStage2 on ONE continuing
        // stream must equal Resolve(target) on an identically-seeded stream. The staged path draws
        // the same rolls in the same order (stage-1 prefix + stage-2 suffix = the whole run).
        (Hero[] party, int target)[] scenarios =
        {
            (new[] { Naked(1, hp: 40) }, 3),
            (new[] { Naked(1, hp: 25), Naked(2, hp: 25) }, 5),
            (new[] { Naked(1, hp: 200, deepest: 0) with { Level = 8 } }, 4),
            (new[] { Naked(1, hp: 12) }, 2), // often wipes/floor-lost in stage 1 → immediate finalize
        };

        var items = new[] { Weapon(90, 12) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

        foreach (var (heroes, target) in scenarios)
        {
            var party = heroes.ToImmutableList();
            const int checkpoint = 1; // D1: CampCheckpointDepth = 1 (every scenario targets >= 2 → staged)
            Assert.True(target >= 2, "scenario target must stage");

            for (ulong seed = 0; seed < 60; seed++)
            {
                var unstaged = ExpeditionResolver.Resolve(party, items, VenueRegistry.Mine, target, new Pcg32(RngState.FromSeed(seed)));

                var stream = new Pcg32(RngState.FromSeed(seed));
                var (completed, inFlight) = ExpeditionResolver.ResolveStage1(party, items, VenueRegistry.Mine, target, checkpoint, stream);
                var staged = completed ?? ExpeditionResolver.ResolveStage2(inFlight!, party, items, VenueRegistry.Mine, stream);

                Assert.Equal(Json(unstaged), Json(staged));
            }
        }
    }

    // ── D4 halt precedence ────────────────────────────────────────────────────────────────

    [Fact]
    public void HaltPrecedence_DeepestEqualsTarget_IsAlwaysTargetReached()
    {
        // The normative invariant: whenever a run reaches its target floor, the halt is
        // TargetReached regardless of which exit path ended the loop.
        // Record 4 so the TUNING-C competence retreat never preempts a target of 2/3/4 — this test
        // is about halt classification, not the retreat rule.
        var party = ImmutableList.Create(Naked(1, hp: 200, deepest: 4) with { Level = 8 });
        var items = new[] { Weapon(90, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

        for (ulong seed = 0; seed < 100; seed++)
        {
            foreach (var target in new[] { 2, 3, 4 })
            {
                var r = ExpeditionResolver.Resolve(party, items, VenueRegistry.Mine, target, new Pcg32(RngState.FromSeed(seed)));
                if (r.DeepestFloorCleared == r.TargetFloor)
                {
                    Assert.Equal(ExpeditionHalt.TargetReached, r.Halt);
                }
            }
        }
    }

    [Fact]
    public void HaltPrecedence_TooHurtCheckFiresAtTargetFloor_StillTargetReached()
    {
        // A hero that clears the TARGET floor and only then trips the too-hurt check has fully
        // succeeded — Halt == TargetReached. A post-floor quaff on the target floor (a use recorded
        // with Round past the fight's rounds) proves the too-hurt exit path was taken there, yet the
        // run still classifies as TargetReached (D4 precedence). Ending a floor below the flee
        // threshold while alive requires mid-fight mag-1 quaffs (a floor-clearing hero that dipped
        // below the threshold without a salve would have fled), so the hero carries a stack of them;
        // the config grid threads the needle of "grind the target floor down while staying alive".
        const int target = 2;
        var salves = Enumerable.Range(20, 14).Select(i => Salve(i, magnitude: 1)).ToArray();
        var pack = salves.Select(s => s.Id).ToArray();

        foreach (var (maxHp, attack, defense) in new[] { (40, 2, 15), (44, 3, 15), (48, 2, 14), (40, 3, 16), (52, 4, 15) })
        {
            var weapon = Weapon(90, attack);
            var armor = Armor(91, defense);
            var items = new[] { weapon, armor }.Concat(salves).ToImmutableSortedDictionary(i => i.Id.Value, i => i);
            var gear = new GearSet(weapon.Id, null, armor.Id);

            for (ulong seed = 0; seed < 2500; seed++)
            {
                // Record 1 so the TUNING-C competence retreat does not preempt a target of 2 — the
                // scenario needs the hero to actually reach and grind the target floor.
                var hero = Naked(1, hp: maxHp, deepest: 1, gear: gear, pack: pack);
                var r = ExpeditionResolver.Resolve(ImmutableList.Create(hero), items, VenueRegistry.Mine, target, new Pcg32(RngState.FromSeed(seed)));

                if (r.DeepestFloorCleared != target)
                {
                    continue;
                }

                var targetFloor = r.Floors.First(f => f.Floor == target);
                var rounds = targetFloor.Combats.Count(c => c.Hero == hero.Id);
                if (targetFloor.Combats.SelectMany(c => c.Uses).Any(u => u.Round > rounds))
                {
                    Assert.Equal(ExpeditionHalt.TargetReached, r.Halt);
                    return; // proven
                }
            }
        }

        Assert.Fail("No too-hurt-at-target case across the config grid — scenario needs retuning.");
    }

    // ── Recall (verb arrives in U4; the flag is honored here) ───────────────────────────────

    [Fact]
    public void Recalled_BanksStageOne_DrawsNothing_HaltRecalled()
    {
        // A recalled party surfaces from its stage-1 state without rolling deeper floors.
        var items = StrongGear;
        var strong = ImmutableList.Create(Strong(1));

        var stream = new Pcg32(RngState.FromSeed(7));
        var (completed, inFlight) = ExpeditionResolver.ResolveStage1(strong, items, VenueRegistry.Mine, targetFloor: 4, checkpointFloor: 1, stream);
        Assert.Null(completed);
        var parked = inFlight! with { Recalled = true };

        var beforeRecall = stream.Snapshot();
        var result = ExpeditionResolver.ResolveStage2(parked, strong, items, VenueRegistry.Mine, stream);

        // No draws: the stream is untouched by a recalled party.
        Assert.Equal(Json(beforeRecall), Json(stream.Snapshot()));
        Assert.Equal(ExpeditionHalt.Recalled, result.Halt);
        Assert.Equal(parked.Floors.Count, result.Floors.Count);          // no deep floors rolled
        Assert.Equal(parked.DeepestFloorCleared, result.DeepestFloorCleared);
        Assert.Equal(Json(parked.Loot), Json(result.Loot));              // stage-1 ore banked
    }

    // ── System: park vs immediate finalize ──────────────────────────────────────────────────

    private static GameState World(Hero[] heroes, ImmutableSortedDictionary<int, Item> items, ulong seed) =>
        GameFactory.NewGame(seed) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            Items = items,
        };

    private static (GameState State, ImmutableList<GameEvent> Events) Expedition(GameState morning, GameKernel kernel)
    {
        var afterMorning = kernel.Tick(morning, ImmutableList<PlayerAction>.Empty).NewState;
        var tick = kernel.Tick(afterMorning, ImmutableList<PlayerAction>.Empty);
        return (tick.NewState, tick.Events);
    }

    [Fact]
    public void HealthyParty_Parks_InFlightPopulated_CampReportMatchesFacts_PendingEmpty()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Three strong vanguards, deepest 1 → target 2, checkpoint 1: they clear floor 1 clean and park.
        var heroes = new[] { Strong(1), Strong(2), Strong(3) };
        var (state, events) = Expedition(World(heroes, StrongGear, seed: 3), kernel);

        Assert.Equal(DayPhase.Camp, state.Phase);
        Assert.Empty(state.PendingExpeditions);           // parked, nothing finalized yet
        var parked = Assert.Single(state.InFlight);
        Assert.Equal(2, parked.TargetFloor);
        Assert.Equal(1, parked.CheckpointFloor);
        Assert.Equal(1, parked.DeepestFloorCleared);       // == checkpoint under the v1 invariant
        Assert.Empty(parked.Dead);                         // v1: parked ⇒ nobody died
        Assert.Equal(3, parked.Party.Count);

        var report = Assert.Single(events.OfType<PartyCampReport>());
        Assert.Equal(1, report.CampedBelowFloor);
        Assert.Equal(2, report.TargetFloor);
        Assert.Equal(Json(parked.Hp), Json(report.HpByHero)); // report HP == parked working HP
        Assert.Equal(parked.Party.Count, report.HpByHero.Count);
        Assert.Contains(events, e => e is PartyDeparted);
    }

    [Fact]
    public void CampReport_HealsLeft_CountsRemainingHealConsumables()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Strong hero carrying two salves; clears floor 1 without needing to drink → 2 heals left.
        var salveA = Salve(10);
        var salveB = Salve(11);
        var items = StrongGear
            .Add(salveA.Id.Value, salveA)
            .Add(salveB.Id.Value, salveB);
        var hero = Strong(1) with { Pack = ImmutableList.Create(salveA.Id, salveB.Id) };

        var (_, events) = Expedition(World(new[] { hero }, items, seed: 4), kernel);

        var report = Assert.Single(events.OfType<PartyCampReport>());
        Assert.Equal(2, report.HealsLeftByHero[1]);
    }

    [Fact]
    public void Stage1Wipe_FinalizesImmediately_NoCampReport_NoInFlight()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Frail naked solo, deepest 1 → target 2: dies on floor 1, finalizes at the Expedition tick.
        var frail = Naked(1, hp: 8, deepest: 1);
        var (state, events) = Expedition(World(new[] { frail }, NoItems, seed: 1), kernel);

        Assert.Empty(state.InFlight);
        var result = Assert.Single(state.PendingExpeditions);
        Assert.Equal(ExpeditionHalt.PartyWiped, result.Halt);
        Assert.Empty(result.Survivors);
        Assert.DoesNotContain(events, e => e is PartyCampReport); // deaths reveal only at Evening
    }

    [Fact]
    public void MixedMultiPartyDay_OnePartyParks_OtherFinalizes()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Heroes 1-3 strong (park), 4-6 frail (wipe on floor 1). All deepest 1 → target 2.
        // All vanguard so PartyFormation deterministically groups {1,2,3} and {4,5,6}.
        var heroes = new[]
        {
            Strong(1), Strong(2), Strong(3),
            Naked(4, hp: 8, deepest: 1), Naked(5, hp: 8, deepest: 1), Naked(6, hp: 8, deepest: 1),
        };
        var (state, events) = Expedition(World(heroes, StrongGear, seed: 2), kernel);

        var parked = Assert.Single(state.InFlight);
        Assert.Equal(new[] { 1, 2, 3 }, parked.Party.Select(id => id.Value).ToArray());

        var finalized = Assert.Single(state.PendingExpeditions);
        Assert.Equal(ExpeditionHalt.PartyWiped, finalized.Halt);

        Assert.Single(events.OfType<PartyCampReport>()); // exactly one — only the parked party
        Assert.Equal(2, events.OfType<PartyDeparted>().Count());
    }

    [Fact]
    public void TargetFloorOne_IsUnstaged_NoInFlight_NoCampReport()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Fresh heroes (deepest 0) → target 1 → checkpoint 0 → unstaged: result lands in Pending now.
        var heroes = new[] { Strong(1, deepest: 0) };
        var (state, events) = Expedition(World(heroes, StrongGear, seed: 5), kernel);

        Assert.Empty(state.InFlight);
        Assert.Single(state.PendingExpeditions);
        Assert.DoesNotContain(events, e => e is PartyCampReport);
    }

    // ── System: the Deep tick finalizes the parked party ────────────────────────────────────

    [Fact]
    public void PendingPopulatedOnlyAfterDeepTick_ThenInFlightCleared()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
            ImmutableList<IActionHandler>.Empty);

        var heroes = new[] { Strong(1) };
        var afterExpedition = Expedition(World(heroes, StrongGear, seed: 6), kernel).State;
        Assert.Single(afterExpedition.InFlight);
        Assert.Empty(afterExpedition.PendingExpeditions);

        var afterCamp = kernel.Tick(afterExpedition, ImmutableList<PlayerAction>.Empty).NewState; // Camp: no systems
        Assert.Equal(DayPhase.ExpeditionDeep, afterCamp.Phase);
        Assert.Single(afterCamp.InFlight);
        Assert.Empty(afterCamp.PendingExpeditions);

        var afterDeep = kernel.Tick(afterCamp, ImmutableList<PlayerAction>.Empty).NewState; // Deep: finalize
        Assert.Empty(afterDeep.InFlight);
        var result = Assert.Single(afterDeep.PendingExpeditions);
        Assert.Equal(2, result.TargetFloor);
        Assert.True(result.DeepestFloorCleared >= 1);
    }

    [Fact]
    public void FullStagedDay_Reveal_AppliesFinalizedResult()
    {
        // End-to-end: a parked party's finalized result flows through the untouched Evening reveal —
        // survivors return, depth records advance. Proves the staged result reveals like any other.
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem(), new ExpeditionRevealSystem()),
            ImmutableList<IActionHandler>.Empty);

        var heroes = new[] { Strong(1) };
        var state = World(heroes, StrongGear, seed: 8);
        var revealEvents = ImmutableList<GameEvent>.Empty;
        for (var i = 0; i < 5; i++) // Morning → Expedition → Camp → Deep → Evening
        {
            var tick = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = tick.NewState;
            revealEvents = revealEvents.AddRange(tick.Events);
        }

        Assert.Empty(state.InFlight);
        Assert.Empty(state.PendingExpeditions);          // consumed by the reveal
        Assert.Contains(revealEvents, e => e is PartyReturned);
        Assert.True(state.Heroes[1].DeepestFloorReached >= 2); // reveal advanced the record past the checkpoint
    }

    // ── Determinism + save/load across the Camp seam ────────────────────────────────────────

    [Fact]
    public void ComposedStaging_RunTwice_ByteIdentical()
    {
        string Run()
        {
            var kernel = new GameKernel(
                ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem(), new ExpeditionRevealSystem()),
                ImmutableList<IActionHandler>.Empty);
            var state = World(new[] { Strong(1), Strong(2), Strong(3) }, StrongGear, seed: 2026);
            for (var i = 0; i < 15; i++) // 3 full staged days
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void SaveLoad_ParkedAtCamp_ThenContinue_EqualsUninterrupted()
    {
        // A mid-day save with a party parked in InFlight, loaded and continued, must equal an
        // uninterrupted run — the parked record carries no RNG, so the kernel stream is the sole
        // authority (KTD4) and stage 2 resumes identically.
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem(), new ExpeditionRevealSystem()),
            ImmutableList<IActionHandler>.Empty);

        GameState Fresh() => World(new[] { Strong(1), Strong(2) }, StrongGear, seed: 55);

        // Uninterrupted: two full staged days (10 ticks).
        var uninterrupted = Fresh();
        for (var i = 0; i < 10; i++)
        {
            uninterrupted = kernel.Tick(uninterrupted, ImmutableList<PlayerAction>.Empty).NewState;
        }

        // Interrupted: run to the Camp tick of day 1 (Morning, Expedition → now at Camp, party parked),
        // save, load, then finish both days.
        var interrupted = Fresh();
        interrupted = kernel.Tick(interrupted, ImmutableList<PlayerAction>.Empty).NewState; // Morning → Expedition
        interrupted = kernel.Tick(interrupted, ImmutableList<PlayerAction>.Empty).NewState; // Expedition → Camp (parked)
        Assert.Equal(DayPhase.Camp, interrupted.Phase);
        Assert.NotEmpty(interrupted.InFlight);

        var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(interrupted));
        Assert.Equal(SaveCodec.Serialize(interrupted), SaveCodec.Serialize(loaded)); // byte-identical round-trip
        for (var i = 0; i < 8; i++) // Camp → … → end of day 2
        {
            loaded = kernel.Tick(loaded, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.Equal(SaveCodec.Serialize(uninterrupted), SaveCodec.Serialize(loaded));
    }
}
