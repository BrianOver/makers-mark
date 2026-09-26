using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// PA4 (plan 2026-07-21-002, PKD6/PKD7): the live haggle — HoldFirm's round-shift value, the
/// Patience walk, the Pin/fleece mood outcomes, the per-class anti-solved-meta pin, gold
/// conservation extended to counter sales, the PKD7 influence-never-orders pin, and full-script
/// determinism with zero RNG drawn anywhere in the haggle path.
/// </summary>
public class HaggleEconomicsTests
{
    // Phase B (B2): named "Lc{id}" rather than "Hero{id}" so this fixture's derived traits
    // (StableHash(HeroId, Name)) stay OFF the two axes this file's pinned floor/ceiling/patience
    // numbers depend on (Price Sensitivity, Haggle Patience) — verified by scan for ids 1-4:
    // Lc1=Discerning+Reckless, Lc2=Unfussy+Sentimental, Lc3=Unfussy+Practical, Lc4=Sentimental+Reckless,
    // none of which this file's rookie/bare-gear fixtures ever trigger (no worn gear, no Memories,
    // DeepestFloorReached always 0). Keeps every pinned number in this file byte-identical post-B2.
    private static Hero MakeHero(int id, string classId, int gold, int mood = 0) => new(
        new HeroId(id), $"Lc{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    {
        MoodPermille = mood,
    };

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    /// <summary>Scoped to the counter's own systems (mirrors CounterQueueSystemTests.Kernel) — the
    /// full production composition pulls in RNG-drawing/id-allocating systems that would collide
    /// with these hand-picked fixtures and contaminate the "zero RNG" assertions.</summary>
    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    private static GameState BaseState(ImmutableSortedDictionary<int, Hero> heroes, params Item[] items) =>
        GameFactory.NewGame(seed: 900) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    private static GameState WithShelf(GameState state, int gold, params ShelfEntry[] shelf) =>
        state with { Player = PlayerState.NewGame(gold) with { Shelf = shelf.ToImmutableList() } };

    [Fact]
    public void HoldFirm_Round2BandAccepts_WhatRound1Refused()
    {
        // Same countered price (100), same neutral-class fixture — fleeced in round 1 (exceeds the
        // round-1 ceiling of 98, per WillingnessModelTests' pinned table), but a clean PIN once the
        // player HoldFirms into round 2 (ceiling climbs to 107). The Recettear shift is real.
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);

        GameState Fresh() => WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        // Round 1: counter at 100 — fleeced (exceeds ceiling 98).
        var round1State = kernel.Tick(Fresh(), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        round1State = kernel.Tick(round1State, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        var round1Result = kernel.Tick(round1State, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 100)));
        var fleeced = Assert.Single(round1Result.Events.OfType<CounterSaleClosed>());
        Assert.False(fleeced.Pinned);
        // P2-PEOPLE-29: the flat -FleeceMoodPenalty is gone — the live resolver must land exactly
        // where the pure model says a 100g counter against a 98g ceiling (true willingness 100g)
        // scales to, a partial penalty short of the cap.
        Assert.Equal(WillingnessModel.FleeceMoodDelta(100, 98, 100), round1Result.NewState.Heroes[1].MoodPermille);
        Assert.True(round1Result.NewState.Heroes[1].MoodPermille > -WillingnessModel.FleeceMoodPenalty);

        // Round 2 (fresh run, HoldFirm once first): the SAME counter price of 100 is now a pin.
        var round2State = kernel.Tick(Fresh(), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        round2State = kernel.Tick(round2State, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        round2State = kernel.Tick(round2State, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
        Assert.Equal(2, round2State.Counter!.Round);
        var round2Result = kernel.Tick(round2State, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 100)));
        var pinned = Assert.Single(round2Result.Events.OfType<CounterSaleClosed>());
        Assert.True(pinned.Pinned);
        Assert.Equal(100, pinned.Price);
        // 100g against true willingness 100g is a zero-gap pin — the least generous pin possible,
        // still short of the PinMoodBonus cap but strictly above an in-band FairDealMood close.
        Assert.Equal(WillingnessModel.PinMoodDelta(100, 100), round2Result.NewState.Heroes[1].MoodPermille);
        Assert.True(round2Result.NewState.Heroes[1].MoodPermille > WillingnessModel.FairDealMood);
        Assert.True(round2Result.NewState.Heroes[1].MoodPermille < WillingnessModel.PinMoodBonus);
    }

    [Fact]
    public void Patience_ThreeHoldFirms_CustomerWalks_NoFourthRound()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        Assert.Equal(1, state.Counter!.Round);
        Assert.Equal(3, state.Counter.PatienceRounds);

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
        Assert.Equal(2, state.Counter!.Round);
        Assert.Equal(2, state.Counter.PatienceRounds);

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
        Assert.Equal(3, state.Counter!.Round); // capped at MaxRounds
        Assert.Equal(1, state.Counter.PatienceRounds);

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)));
        var walked = Assert.Single(result.Events.OfType<CustomerWalked>());
        Assert.Equal(hero.Id, walked.Hero);
        Assert.Contains("patience", walked.Reason);
        Assert.Empty(result.Events.OfType<CounterSaleClosed>());
        // The lone customer walking empties the queue, which closes the session AND (per
        // GameKernel.Advance) advances the day out of Morning this SAME tick — Counter is torn
        // down (null), not left open with hero1 recorded in Served.
        Assert.Null(result.NewState.Counter);
        Assert.Equal(DayPhase.Expedition, result.NewState.Phase);
        // P2-HONEST-42: walking away from the HAGGLE doesn't take the sword off the shelf, and
        // walking no longer bars this hero from the browse pass that runs the same tick — so the
        // hero, still shopping, buys the very sword it just wouldn't haggle over, at the plain
        // listed price (100), through the ordinary (non-haggle) shelf evaluation.
        var sold = Assert.Single(result.Events.OfType<ItemSold>());
        Assert.Equal(hero.Id, sold.Buyer);
        Assert.Equal(sword.Id, sold.Item);
        Assert.Equal(100, sold.Price);
        Assert.Equal(900, result.NewState.Heroes[1].Gold); // 1000 - 100: bought at listed price, not haggled
    }

    [Fact]
    public void Accept_RoundOne_ClosesSaleAtStandingOffer_Unpinned()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        Assert.Equal(82, state.Counter!.StandingOfferGold); // round-1 floor, per the pinned band table

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept)));

        var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
        Assert.Equal(82, sale.Price);
        Assert.False(sale.Pinned);
        Assert.Equal(1000 - 82, result.NewState.Heroes[1].Gold);
        Assert.Equal(0, result.NewState.Heroes[1].MoodPermille); // Accept never pins/fleeces
    }

    [Fact]
    public void Counter_WithinPinWindow_ClosesPinnedSale_WithMoodBonus()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;

        // True willingness is 100; the round-1 band [82,98] overlaps the pin window [94,106] in
        // [94,98] — 96 lands there.
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 96)));

        var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
        Assert.True(sale.Pinned);
        Assert.Equal(96, sale.Price);
        Assert.Equal(WillingnessModel.PinMoodDelta(96, 100), result.NewState.Heroes[1].MoodPermille);
    }

    [Fact]
    public void Counter_WithinBandOutsidePinWindow_ClosesNormalSale_EarnsFairDealMood()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;

        // 85 is inside the round-1 band [82,98] but below the pin window [94,106].
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 85)));

        var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
        Assert.False(sale.Pinned);
        Assert.Equal(85, sale.Price);
        // P2-PEOPLE-29: an in-band close is not nothing anymore — it earns the flat FairDealMood,
        // strictly smaller than the smallest possible pin (WillingnessModelTests pins that ordering).
        Assert.Equal(WillingnessModel.FairDealMood, result.NewState.Heroes[1].MoodPermille);
    }

    [Fact]
    public void Counter_AboveCeiling_Fleeces_SaleStillCloses_ButGoodwillAndMoodPenalized()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;

        // 99 exceeds the round-1 ceiling of 98 — a fleece.
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 99)));

        var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
        Assert.False(sale.Pinned);
        Assert.Equal(99, sale.Price); // the hero still pays — begrudgingly
        // P2-PEOPLE-29: 99g barely clears the 98g ceiling — a shallow fleece, well short of the cap.
        Assert.Equal(WillingnessModel.FleeceMoodDelta(99, 98, 100), result.NewState.Heroes[1].MoodPermille);
        Assert.True(result.NewState.Heroes[1].MoodPermille < 0);
        Assert.True(result.NewState.Heroes[1].MoodPermille > -WillingnessModel.FleeceMoodPenalty);
    }

    [Fact]
    public void Counter_PriceExceedsHeroGold_RejectedTyped_ZeroRng_NoStateChange()
    {
        var hero = MakeHero(1, "striker", gold: 100);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 50, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        var beforeRng = state.Rng;

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 500)));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<HaggleResponseAction>(rejected.Action);
        Assert.Contains("afford", rejected.Reason);
        Assert.Equal(beforeRng, result.NewState.Rng);
        Assert.Equal(100, result.NewState.Heroes[1].Gold); // untouched
    }

    [Fact]
    public void AntiSolvedMeta_OneGlobalMarkup_FleecesSkirmisher_ButLeavesVanguardNormal_RoleFitMoodPinBeatsBoth()
    {
        // The pin (PA4 anti-solved-meta): a naive "always ask 107% of list price" strategy treats
        // every class the same and pays for it. Vanguard (overpaying, high factor) tolerates 107%
        // fine; Skirmisher (stingy, low factor) does NOT — the exact same number fleeces one and
        // merely under-reads the other. Reading each hero's own band (role-fit + mood aware) pins
        // BOTH with a mood bonus instead, at only a modest gold cost — the design's claim that "one
        // global markup leaves money/loyalty behind."
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);

        GameState Setup(string classId) => WithShelf(
            BaseState(Roster(MakeHero(1, classId, gold: 150)), sword),
            gold: 0, new ShelfEntry(sword.Id, 100));

        (CounterSaleClosed Sale, int Mood) RunCounter(string classId, int price)
        {
            var kernel = Kernel();
            var state = kernel.Tick(Setup(classId), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, price)));
            var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
            return (sale, result.NewState.Heroes[1].MoodPermille);
        }

        // Global-107 strategy: same price (107) fired at both classes.
        var vanguardGlobal = RunCounter(GameSim.Classes.ClassRegistry.VanguardId, 107);
        var skirmisherGlobal = RunCounter(GameSim.Classes.SkirmisherClass.Id, 107);

        Assert.False(vanguardGlobal.Sale.Pinned);   // within band [94,112] but outside pin window [108,121] — a flat, unremarkable sale
        // P2-PEOPLE-29: unremarkable no longer means zero — an in-band close earns FairDealMood.
        Assert.Equal(WillingnessModel.FairDealMood, vanguardGlobal.Mood);
        Assert.False(skirmisherGlobal.Sale.Pinned);
        // 107 > ceiling 80 by enough (true willingness 82) that FleeceMoodDelta is already maxed —
        // the flat cap is still the right number here, just no longer the ONLY number this file pins.
        Assert.Equal(WillingnessModel.FleeceMoodDelta(107, 80, 82), skirmisherGlobal.Mood);
        Assert.Equal(-WillingnessModel.FleeceMoodPenalty, skirmisherGlobal.Mood);

        // Role-fit + mood aware strategy: a DIFFERENT price per class, each landing in ITS OWN pin window.
        var vanguardAware = RunCounter(GameSim.Classes.ClassRegistry.VanguardId, 111);
        var skirmisherAware = RunCounter(GameSim.Classes.SkirmisherClass.Id, 79);

        Assert.True(vanguardAware.Sale.Pinned);
        // P2-PEOPLE-29: both reads are real pins, but neither counter sat at the window's edge, so
        // neither earns the full PinMoodBonus cap anymore — the margin-scaled amount the pure model
        // computes for its own (price, trueWillingness) gap.
        Assert.Equal(WillingnessModel.PinMoodDelta(111, 115), vanguardAware.Mood);
        Assert.True(skirmisherAware.Sale.Pinned);
        Assert.Equal(WillingnessModel.PinMoodDelta(79, 82), skirmisherAware.Mood);

        var globalGold = vanguardGlobal.Sale.Price + skirmisherGlobal.Sale.Price;
        var globalMood = vanguardGlobal.Mood + skirmisherGlobal.Mood;
        var awareGold = vanguardAware.Sale.Price + skirmisherAware.Sale.Price;
        var awareMood = vanguardAware.Mood + skirmisherAware.Mood;

        // The blind global markup nets a little more raw gold up front...
        Assert.True(globalGold > awareGold);
        // ...but it costs FAR more in loyalty than the small gold gap it bought — precisely the
        // "leaves money/loyalty behind" claim: reading each hero wins on total value even though
        // it isn't the single highest-grossing move available.
        Assert.True(awareMood > globalMood);
        Assert.True(awareMood - globalMood > globalGold - awareGold);
    }

    [Fact]
    public void Conservation_GoldNeverMintedOrDestroyed_AcrossAMixedSteppedMorning()
    {
        // Extends U7's gold-conservation law (GoldConservationTests) to the counter path: every
        // player<->hero transfer this session — an accept, a hold-then-counter pin, a role-mismatch
        // walk, and a patience-exhausted walk — moves gold exactly, never mints or burns it.
        var heroes = new[]
        {
            MakeHero(1, "vanguard", gold: 200), // will Accept
            MakeHero(2, "striker", gold: 200),  // will HoldFirm then Counter-pin
            MakeHero(3, "striker", gold: 200),  // presented a shield — role mismatch, instant walk
            MakeHero(4, "striker", gold: 200),  // will HoldFirm three times — patience walk
        };
        var sword1 = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Sword1");
        var sword2 = MakeItem(2, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Sword2");
        var shield = MakeItem(3, ItemSlot.Shield, attack: 0, defense: 5, weight: 3, name: "Shield");
        var sword4 = MakeItem(4, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Sword4");
        var state = WithShelf(
            BaseState(Roster(heroes), sword1, sword2, shield, sword4),
            gold: 500,
            new ShelfEntry(sword1.Id, 100), new ShelfEntry(sword2.Id, 100),
            new ShelfEntry(shield.Id, 20), new ShelfEntry(sword4.Id, 100));
        var kernel = Kernel();

        static long TotalGold(GameState s) => s.Player.Gold + s.Heroes.Values.Sum(h => (long)h.Gold);

        var script = ImmutableList.Create(
            ImmutableList.Create<PlayerAction>(new OpenCounterAction()),
            ImmutableList.Create<PlayerAction>(new PresentItemAction(sword1.Id)),
            ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept)), // hero1 buys
            ImmutableList.Create<PlayerAction>(new PresentItemAction(sword2.Id)),
            ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)),
            ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 96)), // hero2 pinned sale
            ImmutableList.Create<PlayerAction>(new PresentItemAction(shield.Id)),                        // hero3 role mismatch — instant walk
            ImmutableList.Create<PlayerAction>(new PresentItemAction(sword4.Id)),
            ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)),
            ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)),
            ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))); // hero4 patience walk

        foreach (var actions in script)
        {
            var before = TotalGold(state);
            var tick = kernel.Tick(state, actions);
            Assert.Empty(tick.Rejected);
            Assert.Equal(before, TotalGold(tick.NewState)); // every tick: neither minted nor destroyed
            state = tick.NewState;
        }

        // Hero4's patience-exhausted walk empties the queue, which closes the session AND (per
        // GameKernel.Advance) advances the day out of Morning in this SAME tick — the session is
        // torn down (Counter null) rather than left open with Closed:true.
        Assert.Null(state.Counter);
        Assert.Equal(DayPhase.Expedition, state.Phase);
        Assert.Equal(sword1.Id, state.Heroes[1].Gear.Weapon);
        Assert.Equal(sword2.Id, state.Heroes[2].Gear.Weapon);
        Assert.Null(state.Heroes[3].Gear.Shield);  // walked, never bought
        Assert.Null(state.Heroes[4].Gear.Weapon);  // walked, never bought
    }

    [Fact]
    public void MoodPermille_NeverAffectsPartyFormation_FloorChoice_OrExpeditionResult()
    {
        // PKD7, the counter's pin: two states differing ONLY in hero 1's persistent mood (as if two
        // separate counter sessions had left very different standing) run the SAME empty-action
        // script through a full production day. Comparing the whole GameState after zeroing mood
        // (the one field allowed to differ) proves everything downstream — muster's prediction,
        // ExpeditionSystem's actual party formation and target floor, and the revealed
        // ExpeditionResult — is untouched. Mood only ever moves gold/relationship surfaces
        // (Counter/, future gossip); it is not even READ by PartyFormation/ExpeditionSystem/
        // MusterSystem today (verified by inspection), and this test pins that as a regression.
        var baseState = GameComposition.NewCampaign(seed: 4242);

        static ImmutableSortedDictionary<int, Hero> WithHero1Mood(GameState state, int mood) =>
            state.Heroes.SetItem(1, state.Heroes[1] with { MoodPermille = mood });

        static GameState ZeroAllMood(GameState state) => state with
        {
            Heroes = state.Heroes.ToImmutableSortedDictionary(
                kv => kv.Key,
                kv => kv.Value with { MoodPermille = 0 }),
            // Wave 3 (commissions, plan 2026-07-24-003): a commission's MinQuality/PremiumGold are
            // INTENTIONALLY scaled by RelationshipBand, which is itself partly mood-derived (KTD4:
            // price/verdict scaling is a legal expression of mood — same list "gossip prose" already
            // lived on). That legitimately makes the commission board — AND the CommissionPosted/
            // Expired/Fulfilled/Lapsed events already stamped into the log this run — differ between
            // two starting moods even though this test never opens the counter. This test's actual job —
            // proving party formation, target floor, and expedition resolution are mood-independent
            // (PKD7) — is unaffected by that; strip the board and those events here the same way
            // MoodPermille itself is zeroed above, rather than widen the invariant to cover a surface
            // it was never about.
            Commissions = ImmutableList<Commission>.Empty,
            EventLog = state.EventLog
                .Where(e => e is not CommissionPosted and not CommissionExpired and not CommissionFulfilled
                    and not CommissionLapsed)
                .ToImmutableList(),
        };

        var warm = baseState with { Heroes = WithHero1Mood(baseState, 900) };
        var soured = baseState with { Heroes = WithHero1Mood(baseState, -900) };
        Assert.NotEqual(warm.Heroes[1].MoodPermille, soured.Heroes[1].MoodPermille); // the ONE input difference

        var kernel = GameComposition.BuildKernel();

        static GameState RunFullDay(GameKernel kernel, GameState start)
        {
            var state = start;
            for (var i = 0; i < 5; i++) // Morning, Expedition, Camp, ExpeditionDeep, Evening — never opens the counter
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return state;
        }

        var warmResult = RunFullDay(kernel, warm);
        var souredResult = RunFullDay(kernel, soured);

        Assert.Equal(SaveCodec.Serialize(ZeroAllMood(warmResult)), SaveCodec.Serialize(ZeroAllMood(souredResult)));
    }

    [Fact]
    public void SteppedScript_TwiceByteIdentical_ZeroRngDrawnAcrossTheEntireHagglePath()
    {
        // Open, present, suggest, hold-firm, counter-pin — every kind of haggle step in one script,
        // run twice from the same seed/state. The RNG stream position (state.Rng) must be
        // byte-identical to what it was BEFORE the script even started — zero draws anywhere in
        // the haggle path (PA4 hard constraint) — and the whole serialized state must match
        // run-to-run (KTD4).
        GameState Run()
        {
            var hero1 = MakeHero(1, "vanguard", gold: 200);
            var hero2 = MakeHero(2, "striker", gold: 200);
            var shield = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 5, weight: 3, name: "Round Shield");
            var sword = MakeItem(2, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Steel Sword");
            var state = WithShelf(
                BaseState(Roster(hero1, hero2), shield, sword),
                gold: 200,
                new ShelfEntry(shield.Id, 40), new ShelfEntry(sword.Id, 100));
            var kernel = Kernel();

            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new SuggestItemAction(sword.Id))).NewState; // no empty complementary slot for a shield-first pitch, still a legal no-op
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(shield.Id))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 46))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept))).NewState;
            return state;
        }

        var beforeRng = GameFactory.NewGame(seed: 900).Rng;

        var first = Run();
        var second = Run();

        Assert.Equal(SaveCodec.Serialize(first), SaveCodec.Serialize(second));
        Assert.Equal(beforeRng, first.Rng); // zero RNG drawn across the ENTIRE stepped script
        Assert.Equal(beforeRng, second.Rng);
        Assert.True(first.Counter is null || first.Counter.Served.Count > 0); // the script actually exercised resolution
    }
}
