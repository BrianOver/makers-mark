using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

/// <summary>
/// The Camp-phase verbs (U4 staged resolution): SendSupply drops one held consumable to the FRONT of
/// a camped hero's pack (drinks before anything the hero already carried), Recall banks stage 1 and
/// surfaces without rolling deeper floors. Both are legal only during <see cref="DayPhase.Camp"/>
/// against a live matching <see cref="InFlightExpedition"/>, draw NO RNG, and carry typed rejections.
///
/// The marquee proves the architecture's core claim end-to-end: a camp-delivered player salve, quaffed
/// front-first in stage 2, surfaces as a PotionLifesave beat at Evening with ZERO changes to
/// <see cref="AttributionEngine"/> — attribution is pure over recorded data, so a camp delivery is just
/// an ordinary front-of-pack drink to it.
/// </summary>
public class CampHandlersTests
{
    private static readonly ImmutableList<PlayerAction> Empty = ImmutableList<PlayerAction>.Empty;

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────

    private static Hero Strong(int id, int deepest = 1, params ItemId[] pack) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepest, DiedOnDay: null)
    {
        Pack = pack.ToImmutableList(),
    };

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Salve(int id, int magnitude = 6, bool marked = true) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), marked ? new MakersMark("You", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, magnitude));

    private static readonly ImmutableSortedDictionary<int, Item> StrongGear =
        new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] extra) =>
        extra.Aggregate(StrongGear, (acc, i) => acc.SetItem(i.Id.Value, i));

    private static GameState World(Hero[] heroes, ImmutableSortedDictionary<int, Item> items, ulong seed, int? gold = null)
    {
        var world = GameFactory.NewGame(seed) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            Items = items,
        };
        return gold is { } g ? world with { Player = world.Player with { Gold = g } } : world;
    }

    private static GameKernel CampKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem(), new ExpeditionRevealSystem()),
        ImmutableList.Create<IActionHandler>(new CampHandlers()));

    /// <summary>Advance Morning → Expedition so a healthy party parks; returns the state at Camp.</summary>
    private static GameState ParkedAtCamp(GameKernel kernel, GameState world)
    {
        var s = kernel.Tick(world, Empty).NewState; // Morning → Expedition
        s = kernel.Tick(s, Empty).NewState;         // Expedition → Camp (parked)
        Assert.Equal(DayPhase.Camp, s.Phase);
        Assert.NotEmpty(s.InFlight);
        return s;
    }

    private const int FloorOneFee = 9; // SupplyFeeBase 6 + SupplyFeePerFloor 3 × checkpoint 1 (D5 knobs)

    // ── Fee value ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeliveryFee_AtFloorOneCamp_IsNineGold_AbovePinnedSalvePrice()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        var delivery = Assert.Single(tick.Events.OfType<SupplyDelivered>());
        Assert.Equal(FloorOneFee, delivery.Fee);
        Assert.True(delivery.Fee > 8, "the runner fee must sit above the 8g salve sale price (rationing tension)");
    }

    // ── Front-insert (deterministic core of the send verb) ───────────────────────────────────

    [Fact]
    public void SendSupply_FrontInsertsDeliveredItem_AheadOfRivalSalve_InBothPacks_ChargesFee()
    {
        var rival = Salve(10, marked: false);
        var delivered = Salve(11);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1, pack: rival.Id) }, Catalog(rival, delivered), seed: 3));

        var beforeGold = camp.Player.Gold;
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), delivered.Id)));
        Assert.Empty(tick.Rejected);

        // Delivered item is at the FRONT of both the working pack (what stage 2 quaffs) and the hero's
        // persistent pack, ahead of the rival salve the hero already carried.
        var parked = Assert.Single(tick.NewState.InFlight);
        Assert.Equal(new[] { delivered.Id, rival.Id }, parked.Packs[1].ToArray());
        Assert.Equal(delivered.Id, tick.NewState.Heroes[1].Pack[0]);
        Assert.True(parked.SupplySent);

        // Fee charged as a recorded sink.
        Assert.Equal(beforeGold - FloorOneFee, tick.NewState.Player.Gold);
        var delivery = Assert.Single(tick.Events.OfType<SupplyDelivered>());
        Assert.Equal(new HeroId(1), delivery.To);
        Assert.Equal(delivered.Id, delivery.Item);
        Assert.Equal(FloorOneFee, delivery.Fee);
    }

    // ── Marquee: delivered salve → quaffed first in stage 2 → PotionLifesave at Evening ───────

    [Fact]
    public void DeliveredSalve_QuaffedFirstInStageTwo_YieldsPotionLifesave_AtEvening_ZeroAttributionEdits()
    {
        // The architecture's keystone. A frail hero parks after floor 1 (clean clear), the player pays
        // the runner to drop a marked salve to the FRONT of the pack, and in stage 2 that salve drinks
        // before the rival salve the hero already carried and provably saves the hero's life. The beat
        // is produced by the UNCHANGED AttributionEngine — a camp delivery is an ordinary front-of-pack
        // drink to it. Combat is deterministic per seed, so the found (config, seed) is stable forever.
        var rival = Salve(10, marked: false, magnitude: 6);
        var delivered = Salve(11, marked: true, magnitude: 8);

        foreach (var (maxHp, weaponAtk, armorDef) in new[]
                 {
                     (22, 6, 8), (20, 6, 8), (24, 8, 8), (20, 8, 10), (26, 4, 6), (22, 10, 8),
                 })
        {
            var weapon = Weapon(80, weaponAtk);
            var armor = Armor(81, armorDef);
            var items = new[] { weapon, armor, rival, delivered }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

            for (ulong seed = 0; seed < 1500; seed++)
            {
                var kernel = CampKernel();
                var hero = new Hero(
                    new HeroId(1), "Kess", "vanguard", Level: 3, MaxHp: maxHp, Gold: 30,
                    new GearSet(weapon.Id, null, armor.Id), ImmutableList<ItemMemory>.Empty,
                    Alive: true, DeepestFloorReached: 1, DiedOnDay: null)
                {
                    Pack = ImmutableList.Create(rival.Id),
                };

                var world = World(new[] { hero }, items, seed);
                var afterMorning = kernel.Tick(world, Empty).NewState;
                var afterExpedition = kernel.Tick(afterMorning, Empty).NewState;
                if (afterExpedition.Phase != DayPhase.Camp || afterExpedition.InFlight.IsEmpty)
                {
                    continue; // didn't park (died / too hurt / gate on floor 1)
                }

                var campTick = kernel.Tick(afterExpedition, ImmutableList.Create<PlayerAction>(
                    new SendSupplyAction(new HeroId(1), delivered.Id)));
                if (campTick.Rejected.Any())
                {
                    continue;
                }

                var afterDeep = kernel.Tick(campTick.NewState, Empty); // ExpeditionDeep: stage 2 resolves
                var result = afterDeep.NewState.PendingExpeditions.SingleOrDefault();
                if (result is null)
                {
                    continue;
                }

                // The first quaff in a STAGE-2 floor (floor > checkpoint 1) must be the delivered salve —
                // front-of-pack ordering, ahead of the rival salve the hero carried in.
                var stageTwoUses = result.Floors
                    .Where(f => f.Floor > 1)
                    .SelectMany(f => f.Combats)
                    .SelectMany(c => c.Uses)
                    .ToList();

                var lifesave = result.Beats.FirstOrDefault(b =>
                    b.Beat == BeatType.PotionLifesave && b.Item == delivered.Id);
                if (lifesave is null || stageTwoUses.Count == 0 || stageTwoUses[0].Item != delivered.Id)
                {
                    continue;
                }

                // The lifesave beat surfaces at the Evening reveal as an AttributionBeatEvent.
                var eveningTick = kernel.Tick(afterDeep.NewState, Empty); // Evening reveal
                var beatEvent = Assert.Single(eveningTick.Events.OfType<AttributionBeatEvent>(),
                    e => e.Beat == BeatType.PotionLifesave && e.Item == delivered.Id);
                Assert.Equal(new HeroId(1), beatEvent.Hero);
                Assert.Contains("saved Kess's life", beatEvent.Detail);
                return; // proven — the full camp-delivery → quaff → attribution → reveal spine
            }
        }

        Assert.Fail("No camp-delivered PotionLifesave across the config grid — scenario needs retuning.");
    }

    // ── Recall ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recall_BanksStageOne_NoDeepFloorsRolled_SurvivorsSurfaceWithOre()
    {
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, StrongGear, seed: 6));
        var parked = Assert.Single(camp.InFlight);
        var stageOneFloors = parked.Floors.Count;

        var campTick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new RecallPartyAction(new HeroId(1))));
        Assert.Empty(campTick.Rejected);
        var recalled = Assert.Single(campTick.NewState.InFlight);
        Assert.True(recalled.Recalled);
        Assert.Single(campTick.Events.OfType<PartyRecalled>());

        var afterDeep = kernel.Tick(campTick.NewState, Empty).NewState; // Deep: banks, no roll
        var result = Assert.Single(afterDeep.PendingExpeditions);
        Assert.Equal(ExpeditionHalt.Recalled, result.Halt);
        Assert.Equal(stageOneFloors, result.Floors.Count); // no deep floors rolled
        Assert.Contains(new HeroId(1), result.Survivors);
        Assert.NotEmpty(result.Loot); // stage-1 ore banked
    }

    // ── One delivery per party, across two parties ───────────────────────────────────────────

    [Fact]
    public void OneRunnerPerParty_EachPartyMayReceiveOne_SecondSendToSamePartyRejected()
    {
        var salveA = Salve(10);
        var salveB = Salve(11);
        var salveC = Salve(12);
        var kernel = CampKernel();
        // Six strong vanguards → two parties {1,2,3} and {4,5,6}, both park.
        var heroes = new[] { Strong(1), Strong(2), Strong(3), Strong(4), Strong(5), Strong(6) };
        var camp = ParkedAtCamp(kernel, World(heroes, Catalog(salveA, salveB, salveC), seed: 2));
        Assert.Equal(2, camp.InFlight.Count);

        // One delivery to each party (H1 in party A, H4 in party B) plus a second to party A (H2).
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(
            new SendSupplyAction(new HeroId(1), salveA.Id),   // party A — lands
            new SendSupplyAction(new HeroId(4), salveB.Id),   // party B — lands
            new SendSupplyAction(new HeroId(2), salveC.Id))); // party A again — refused

        Assert.Equal(2, tick.Events.OfType<SupplyDelivered>().Count());
        var rejection = Assert.Single(tick.Rejected);
        Assert.Contains("One runner per party per day", rejection.Reason);
        Assert.All(tick.NewState.InFlight, f => Assert.True(f.SupplySent));
    }

    // ── Rejections: one per reason, in the fixed validation order ────────────────────────────

    [Fact]
    public void SendDuringMorning_RejectedByKernel_NoHandler()
    {
        // Wrong phase: CanHandle is false at Morning and no other handler accepts the action.
        var kernel = CampKernel();
        var world = World(new[] { Strong(1) }, Catalog(Salve(10)), seed: 1);
        var tick = kernel.Tick(world, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), new ItemId(10))));
        var rejection = Assert.Single(tick.Rejected);
        Assert.Contains("No handler accepts", rejection.Reason);
    }

    [Fact]
    public void RecallDuringMorning_RejectedByKernel_NoHandler()
    {
        var kernel = CampKernel();
        var world = World(new[] { Strong(1) }, StrongGear, seed: 1);
        var tick = kernel.Tick(world, ImmutableList.Create<PlayerAction>(new RecallPartyAction(new HeroId(1))));
        var rejection = Assert.Single(tick.Rejected);
        Assert.Contains("No handler accepts", rejection.Reason);
    }

    [Fact]
    public void Send_NoPartyCampedWithHero_Rejected()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(99), salve.Id)));
        Assert.Contains("No party is camped with H99", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_ToDeadStageOneHero_Rejected_Defensive()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        // Force the (v1-unreachable) dead-member state to prove the defensive reason is typed.
        var wounded = camp.InFlight[0] with { Dead = ImmutableSortedSet.Create(1) };
        camp = camp with { InFlight = camp.InFlight.SetItem(0, wounded) };
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Contains("fell below", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_AfterRecall_Rejected()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        var recalled = camp.InFlight[0] with { Recalled = true };
        camp = camp with { InFlight = camp.InFlight.SetItem(0, recalled) };
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Contains("recall bell has rung", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_SecondToSameParty_RejectedAsSupplySpent()
    {
        var salveA = Salve(10);
        var salveB = Salve(11);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1), Strong(2), Strong(3) }, Catalog(salveA, salveB), seed: 3));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(
            new SendSupplyAction(new HeroId(1), salveA.Id),
            new SendSupplyAction(new HeroId(2), salveB.Id)));
        Assert.Single(tick.Events.OfType<SupplyDelivered>());
        Assert.Contains("One runner per party per day", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_UnknownItem_Rejected()
    {
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, StrongGear, seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), new ItemId(777))));
        Assert.Contains("No such item I777", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_NonConsumable_Rejected()
    {
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, StrongGear, seed: 6));
        // I90 is the equipped sword — a real item with no consumable effect.
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), new ItemId(90))));
        Assert.Contains("isn't a consumable", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_UnmarkedConsumable_Rejected_NotYourCraft()
    {
        var rival = Salve(10, marked: false);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(rival), seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), rival.Id)));
        Assert.Contains("isn't your craft to send", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_ShelvedItem_Rejected_UnstockFirst()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        camp = camp with { Player = camp.Player with { Shelf = camp.Player.Shelf.Add(new ShelfEntry(salve.Id, 8)) } };
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Contains("shelved — unstock it first", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_ItemOnRivalShelf_Rejected()
    {
        var salve = Salve(10); // marked, but placed on the rival shelf — not in the player's hands
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        camp = camp with { RivalShelf = camp.RivalShelf.Add(new ShelfEntry(salve.Id, 8)) };
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Contains("on the rival's shelf", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_ItemAlreadyInAHeroPack_Rejected()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        // Salve rides in H1's pack — it's carried, not held.
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1, pack: salve.Id) }, Catalog(salve), seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Contains("already in a hero's pack", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Send_CannotAffordFee_Rejected()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6, gold: 5));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Contains($"Can't pay the {FloorOneFee}g runner", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Recall_NoPartyCampedWithHero_Rejected()
    {
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, StrongGear, seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new RecallPartyAction(new HeroId(99))));
        Assert.Contains("No party is camped with H99", Assert.Single(tick.Rejected).Reason);
    }

    [Fact]
    public void Recall_Twice_Rejected()
    {
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, StrongGear, seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(
            new RecallPartyAction(new HeroId(1)),
            new RecallPartyAction(new HeroId(1))));
        Assert.Single(tick.Events.OfType<PartyRecalled>());
        Assert.Contains("already rung", Assert.Single(tick.Rejected).Reason);
    }

    // ── Determinism: the Camp tick draws NO rng, and camp actions replay byte-identically ─────

    [Fact]
    public void CampTick_WithSend_DrawsNoRng_StreamPositionUntouched()
    {
        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Empty(tick.Rejected);
        // No systems run at Camp and the handler draws nothing — the kernel snapshot equals the input.
        Assert.Equal(camp.Rng, tick.NewState.Rng);
    }

    [Fact]
    public void SameSeed_SameCampActions_RunTwice_ByteIdentical()
    {
        string Run()
        {
            var salve = Salve(10);
            var kernel = CampKernel();
            var state = World(new[] { Strong(1), Strong(2) }, Catalog(salve), seed: 2026);
            var actionsByPhase = new Dictionary<DayPhase, ImmutableList<PlayerAction>>
            {
                [DayPhase.Camp] = ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)),
            };
            for (var i = 0; i < 10; i++) // two full staged days
            {
                var actions = actionsByPhase.GetValueOrDefault(state.Phase, Empty);
                state = kernel.Tick(state, actions).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Run(), Run());
    }

    // ── Gold conservation (KTD3): the runner fee is a conserved town-gold sink ────────────────

    [Fact]
    public void CampRunnerFee_IsAConservedSink_TownGoldDropsByExactlyTheFee()
    {
        long TownGold(GameState s) => s.Player.Gold + s.Heroes.Values.Sum(h => (long)h.Gold);

        var salve = Salve(10);
        var kernel = CampKernel();
        var camp = ParkedAtCamp(kernel, World(new[] { Strong(1) }, Catalog(salve), seed: 6));
        var before = TownGold(camp);

        var tick = kernel.Tick(camp, ImmutableList.Create<PlayerAction>(new SendSupplyAction(new HeroId(1), salve.Id)));
        Assert.Empty(tick.Rejected);

        var delivery = Assert.Single(tick.Events.OfType<SupplyDelivered>());
        // Δ(player + heroes) == −fee, and the recorded event delta matches the actual move.
        Assert.Equal(before - delivery.Fee, TownGold(tick.NewState));
        Assert.Equal(FloorOneFee, delivery.Fee);
    }
}
