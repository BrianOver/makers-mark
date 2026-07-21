using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// PA3 (plan 2026-07-21-002, PKD5): the stepped Morning counter queue — open/present/close
/// resolution, the HeroId-order queue, the unserved-heroes atomic fallback, and determinism
/// (same stepped sequence twice, and save/load mid-session).
/// </summary>
public class CounterQueueSystemTests
{
    private static Hero MakeHero(int id, string classId, int gold, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    /// <summary>The counter's own slice of the production pipeline, in PRODUCTION order
    /// (GameComposition: CounterQueueSystem before HeroShoppingSystem) — everything else in the
    /// full Morning block (RecruitSystem, RivalRestockSystem, GossipSystem, MusterSystem, ...)
    /// draws RNG and/or allocates fresh hero/item ids, which would collide with these tests'
    /// hand-picked fixture ids and contaminate the "zero RNG" assertions. Scoping to just these
    /// two systems isolates the counter/fallback seam exactly like HeroShoppingSystemTests scopes
    /// to HeroShoppingSystem alone.</summary>
    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    private static GameState BaseState(ImmutableSortedDictionary<int, Hero> heroes, params Item[] items) =>
        GameFactory.NewGame(seed: 601) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    [Fact]
    public void OpenCounter_QueuesAliveHeroesInHeroIdOrder_DeadHeroesNeverQueue()
    {
        var alive1 = MakeHero(1, "vanguard", 100);
        var dead2 = MakeHero(2, "vanguard", 100, alive: false);
        var alive3 = MakeHero(3, "vanguard", 100);
        var state = BaseState(Roster(alive1, dead2, alive3));

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));

        Assert.Empty(result.Rejected);
        Assert.NotNull(result.NewState.Counter);
        Assert.Equal(new[] { new HeroId(1), new HeroId(3) }, result.NewState.Counter!.Queue);
        Assert.Equal(new HeroId(1), result.NewState.Counter.Active);
        var approached = Assert.Single(result.Events.OfType<CustomerApproached>());
        Assert.Equal(new HeroId(1), approached.Hero);
    }

    [Fact]
    public void OpenCounter_NoLivingHeroes_ValidOpenSession_NoActiveCustomer()
    {
        var state = BaseState(Roster()); // empty roster

        var result = Kernel().Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));

        Assert.Empty(result.Rejected);
        Assert.NotNull(result.NewState.Counter);
        Assert.Empty(result.NewState.Counter!.Queue);
        Assert.Null(result.NewState.Counter.Active);
        Assert.False(result.NewState.Counter.Closed);
        Assert.Empty(result.Events); // nobody approached
        Assert.Equal(DayPhase.Morning, result.NewState.Phase); // still holds — session open
    }

    [Fact]
    public void PresentItem_StrictUpgrade_OpensRound_SetsStandingOffer_NoInstantSale()
    {
        // PA4: presenting a genuine upgrade no longer closes the sale on the spot — it opens a
        // haggle round (PA3's placeholder instant-buy is gone). The customer's own opening
        // (lowball) offer is emitted as CustomerCountered; the actual sale waits on a
        // HaggleResponseAction (see the Accept/HoldFirm/Counter tests below and in
        // HaggleEconomicsTests).
        var hero1 = MakeHero(1, "vanguard", gold: 100);
        var hero2 = MakeHero(2, "vanguard", gold: 100);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");
        var state = BaseState(Roster(hero1, hero2), sword) with
        {
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 25)) },
        };
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id)));

        Assert.Empty(result.Events.OfType<CounterSaleClosed>());   // no instant sale
        var offer = Assert.Single(result.Events.OfType<CustomerCountered>());
        Assert.Equal(hero1.Id, offer.Hero);
        Assert.Equal(22, offer.OfferGold);                          // round-1 floor (see HaggleEconomicsTests band table)

        Assert.Equal(100, result.NewState.Heroes[1].Gold);         // nothing spent yet
        Assert.Single(result.NewState.Player.Shelf);               // item still shelved — negotiation, not a sale
        Assert.Equal(1, result.NewState.Counter!.Round);
        Assert.Equal(22, result.NewState.Counter.StandingOfferGold);
        Assert.Equal(new HeroId(1), result.NewState.Counter.Active); // hero1 still active — negotiating
        Assert.Equal(DayPhase.Morning, result.NewState.Phase);
    }

    [Fact]
    public void HaggleCounter_WithinBandOutsidePinWindow_ClosesNormalSale_ConservesGold()
    {
        // Continues the scenario above through to a real sale: Counter at list price (25) lands
        // inside the round-1 band [22, 27] but outside the pin window [26, 29] — a normal,
        // unpinned sale (HaggleEconomicsTests exercises the pinned/fleeced variants directly).
        var hero1 = MakeHero(1, "vanguard", gold: 100);
        var hero2 = MakeHero(2, "vanguard", gold: 100);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");
        var state = BaseState(Roster(hero1, hero2), sword) with
        {
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 25)) },
        };
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, 25)));

        var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
        Assert.Equal(hero1.Id, sale.Hero);
        Assert.Equal(sword.Id, sale.Item);
        Assert.Equal(25, sale.Price);
        Assert.False(sale.Pinned);

        Assert.Equal(75, result.NewState.Heroes[1].Gold);       // hero paid 25
        Assert.Equal(75, result.NewState.Player.Gold);           // player received 25 (50 + 25)
        Assert.Equal(sword.Id, result.NewState.Heroes[1].Gear.Weapon);
        Assert.Empty(result.NewState.Player.Shelf);              // sold item leaves the shelf
        Assert.Equal(DayPhase.Morning, result.NewState.Phase);   // still holding — hero2 is now active
        Assert.Contains(1, result.NewState.Counter!.Served);
        Assert.Equal(new HeroId(2), result.NewState.Counter.Active); // next customer approached
        Assert.False(result.NewState.Counter.Closed);
        Assert.Single(result.Events.OfType<CustomerApproached>()); // hero2's approach, same tick
    }

    [Fact]
    public void PresentItem_RoleMismatch_CustomerWalks_WithLegibleReason()
    {
        var striker = MakeHero(1, "striker", gold: 100); // strikers don't carry shields
        var vanguard = MakeHero(2, "vanguard", gold: 100); // keeps the queue non-exhausted (see above)
        var shield = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 5, weight: 3, name: "Oak Shield");
        var state = BaseState(Roster(striker, vanguard), shield) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(shield.Id, 15)) },
        };
        var kernel = Kernel();

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(shield.Id)));

        Assert.Empty(result.Events.OfType<CounterSaleClosed>());
        var walked = Assert.Single(result.Events.OfType<CustomerWalked>());
        Assert.Equal(striker.Id, walked.Hero);
        Assert.Equal(shield.Id, walked.Item);
        Assert.Contains("striker", walked.Reason);
        Assert.Equal(100, result.NewState.Heroes[1].Gold); // nothing spent
        Assert.Single(result.NewState.Player.Shelf);       // item stays shelved — nobody bought it
        Assert.Contains(1, result.NewState.Counter!.Served);
        Assert.Equal(new HeroId(2), result.NewState.Counter.Active);
    }

    [Fact]
    public void Fallback_ServeTwoOfSix_Close_RunsAtomicPassForExactlyFourUnservedInHeroIdOrder()
    {
        var heroes = Enumerable.Range(1, 6).Select(i => MakeHero(i, "vanguard", gold: 100)).ToArray();
        var counterItemA = MakeItem(1, ItemSlot.Weapon, attack: 4, defense: 0, weight: 3, name: "Counter Sword A");
        var counterItemB = MakeItem(2, ItemSlot.Weapon, attack: 4, defense: 0, weight: 3, name: "Counter Sword B");
        var fallbackItem = MakeItem(3, ItemSlot.Weapon, attack: 4, defense: 0, weight: 3, name: "Shelf Sword");
        var state = BaseState(Roster(heroes), counterItemA, counterItemB, fallbackItem) with
        {
            Player = PlayerState.NewGame(0) with
            {
                Shelf = ImmutableList.Create(
                    new ShelfEntry(counterItemA.Id, 10),
                    new ShelfEntry(counterItemB.Id, 10),
                    new ShelfEntry(fallbackItem.Id, 10)),
            },
        };
        var kernel = Kernel();

        // Tick 1: open — queue [1..6], hero1 approaches.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase);

        // Tick 2: present to hero1 — opens the round (PA4: no more instant sale).
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(counterItemA.Id))).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase);

        // Tick 3: accept hero1's own opening offer — closes the sale, hero2 approaches.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept))).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase); // still holding — queue not exhausted

        // Tick 4: present to hero2 — opens hero2's round.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(counterItemB.Id))).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase);

        // Tick 5: accept hero2's opening offer — closes the sale.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Accept))).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase);
        Assert.Equal(ImmutableSortedSet.Create(1, 2), state.Counter!.Served);

        // Tick 6: close — heroes 3,4,5,6 (unserved) run the atomic pass THIS tick.
        var closeResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CloseCounterAction()));

        // The single fallback-shelf item is contested: HeroId order settles it — hero 3 (lowest
        // unserved id) buys it (mirrors HeroShoppingSystem.HeroesShopInHeroIdOrder_FirstIdWinsContestedItem).
        var sold = Assert.Single(closeResult.Events.OfType<ItemSold>());
        Assert.Equal(new HeroId(3), sold.Buyer);
        Assert.Equal(fallbackItem.Id, sold.Item);

        // Heroes 1 and 2 never re-enter HeroShoppingSystem this tick — they already have their
        // counter-bought gear and are untouched (no double purchase, no pass event for them).
        Assert.Equal(counterItemA.Id, closeResult.NewState.Heroes[1].Gear.Weapon);
        Assert.Equal(counterItemB.Id, closeResult.NewState.Heroes[2].Gear.Weapon);
        Assert.Equal(fallbackItem.Id, closeResult.NewState.Heroes[3].Gear.Weapon);
        Assert.Null(closeResult.NewState.Heroes[4].Gear.Weapon);
        Assert.Null(closeResult.NewState.Heroes[5].Gear.Weapon);
        Assert.Null(closeResult.NewState.Heroes[6].Gear.Weapon);

        // The day advances exactly once and the session is torn down.
        Assert.Equal(1, closeResult.NewState.Day);
        Assert.Equal(DayPhase.Expedition, closeResult.NewState.Phase);
        Assert.Null(closeResult.NewState.Counter);
    }

    [Fact]
    public void SameSeed_SameSteppedActionSequence_TwiceByteIdentical()
    {
        GameState Run()
        {
            var hero = MakeHero(1, "vanguard", gold: 100);
            var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");
            var state = BaseState(Roster(hero), sword) with
            {
                Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 25)) },
            };
            var kernel = Kernel();

            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            return state;
        }

        Assert.Equal(SaveCodec.Serialize(Run()), SaveCodec.Serialize(Run()));
    }

    [Fact]
    public void SaveLoad_MidSession_RoundTripsThroughSaveCodec_ContinueEqualsUninterrupted()
    {
        var hero = MakeHero(1, "vanguard", gold: 100);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Iron Sword");

        GameState Fresh() => BaseState(Roster(hero), sword) with
        {
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 25)) },
        };

        var kernel = Kernel();

        // Uninterrupted: open, then present-and-close in one continuous run.
        var uninterrupted = kernel.Tick(Fresh(), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        uninterrupted = kernel.Tick(uninterrupted, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;

        // Interrupted: open, serialize/deserialize (a save mid-session), then continue.
        var midSession = kernel.Tick(Fresh(), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.NotNull(midSession.Counter); // an open session really does round-trip
        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(midSession));
        // NOTE: comparing CounterState via Assert.Equal is a trap — ImmutableList<T>/ImmutableSortedSet<T>
        // use reference equality, so two structurally-identical-but-freshly-deserialized instances
        // would spuriously fail. Compare through the serializer instead (byte-identical, which is
        // the actual determinism contract — KTD4).
        Assert.Equal(SaveCodec.Serialize(midSession), SaveCodec.Serialize(reloaded));
        var interrupted = kernel.Tick(reloaded, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;

        Assert.Equal(SaveCodec.Serialize(uninterrupted), SaveCodec.Serialize(interrupted));
    }
}
