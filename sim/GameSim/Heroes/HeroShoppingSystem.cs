using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Morning shopping (R7, R16-morning-half): each ALIVE hero, in HeroId order, browses
/// the player shelf AND the rival shelf and buys the single best affordable upgrade
/// across both — best value = gear-score gain per gold (<see cref="ShoppingAi"/>).
/// Earlier heroes shop first, so later heroes see a thinner shelf: strictly sequential
/// and deterministic. Draws no RNG.
///
/// After the gear pass, the CONSUMABLE pass (P2) runs in the same HeroId order: a hero
/// with an empty <see cref="Hero.Pack"/> buys the single cheapest shelf item with a
/// Heal effect it can afford (player shelf preferred on price tie), at most one per
/// hero per Morning. Consumables are keyed off <see cref="ConsumableEffect"/> DATA and
/// never enter the gear pass (they carry no gear score).
///
/// Event cap (documented behavior): <see cref="HeroPassedOnItem"/> is emitted only for
/// PLAYER-shelf items — the player needs to know why their stock didn't sell (R8/AE4).
/// Rival-shelf passes stay silent to avoid event spam the player can't act on.
///
/// PA3/PKD5: while a stepped counter session is OPEN and UNFINISHED (<see cref="GameState.Counter"/>
/// is <c>{ Closed: false }</c>), this system does nothing at all — those heroes are still queued for
/// (or mid-) counter service, resolved by <see cref="Counter.CounterQueueSystem"/> instead, and running
/// the atomic pass early would shop them twice. On the CLOSING tick (<c>Counter.Closed == true</c>,
/// set by <see cref="Counter.CounterHandlers"/>'s <c>CloseCounterAction</c> or by the queue running
/// dry) this system runs its normal pass but SKIPS every hero already in <see cref="CounterState.Served"/>
/// — nobody shops twice, nobody starves. <see cref="GameState.Counter"/> null (the default — the ONLY
/// path <c>BaselinePlayer</c>/the balance gate ever exercise) takes the exact original unconditional
/// loop, byte-identical to pre-Phase-A (the atomic-equivalence pin).
/// </summary>
public sealed class HeroShoppingSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "hero-shopping";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        if (state.Counter is { Closed: false })
        {
            return state; // stepped session still open — CounterQueueSystem owns these heroes
        }

        var served = state.Counter?.Served; // non-null only on the closing tick (PKD5 fallback gate)

        // Snapshot the id order up front; ImmutableSortedDictionary keys are already
        // ascending HeroId.Value — the deterministic shopping order.
        foreach (var heroId in state.Heroes.Keys.ToImmutableArray())
        {
            var hero = state.Heroes[heroId];
            if (!hero.Alive || served is { } s && s.Contains(heroId))
            {
                continue; // dead heroes never shop (R7 permadeath); counter-served heroes don't shop twice
            }

            state = ShopOnce(state, hero, events);
        }

        // Consumable pass (P2), after the whole gear pass: gold spent on gear is gone,
        // so the pass reads each hero's post-gear purse.
        foreach (var heroId in state.Heroes.Keys.ToImmutableArray())
        {
            var hero = state.Heroes[heroId];
            if (!hero.Alive || served is { } s && s.Contains(heroId))
            {
                continue;
            }

            state = ShopConsumableOnce(state, hero, events);
        }

        return state;
    }

    /// <summary>One hero's whole morning: evaluate both shelves, buy at most one item.</summary>
    private static GameState ShopOnce(GameState state, Hero hero, IEventSink events)
    {
        var candidates = CollectCandidates(state);

        // Pick the single best Buy across both shops. Strict "better than" keeps the
        // comparison pure; ItemIds are unique, so IsBetterValue is a total order.
        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Item.Effect is not null)
            {
                continue; // consumables shop in their own pass (P2) — no gear score here
            }

            var verdict = ShoppingAi.EvaluateItem(hero, candidate.Item, candidate.Price, state.Items);
            candidate.Verdict = verdict;
            if (verdict.Kind != ShoppingVerdictKind.Buy)
            {
                continue;
            }

            if (best is null || ShoppingAi.IsBetterValue(
                    verdict.GearScoreGain, candidate.Price, candidate.Item.Id,
                    best.Verdict!.GearScoreGain, best.Price, best.Item.Id))
            {
                best = candidate;
            }
        }

        // Legible passes (R8): every player-shelf item the hero looked at and did not
        // buy gets a reasoned event — including buyable items that lost on value.
        // (A null verdict means the item wasn't judged in this pass — consumables.)
        foreach (var candidate in candidates)
        {
            if (!candidate.FromPlayerShelf || candidate.Verdict is null || ReferenceEquals(candidate, best))
            {
                continue;
            }

            var reason = candidate.Verdict.Kind == ShoppingVerdictKind.Pass
                ? candidate.Verdict.Reason
                : $"picked {best!.Item.Name} instead — better gear score per gold";
            events.Emit(new HeroPassedOnItem(hero.Id, candidate.Item.Id, reason));
        }

        return best is null ? state : ApplyPurchase(state, hero, best, events);
    }

    /// <summary>
    /// One hero's consumable restock (P2): only when the pack is EMPTY, buy the single
    /// cheapest affordable Heal item across both shelves — player shelf wins price
    /// ties, lower ItemId settles the rest. At most one purchase per hero per Morning.
    /// </summary>
    private static GameState ShopConsumableOnce(GameState state, Hero hero, IEventSink events)
    {
        if (hero.Pack.Count > 0)
        {
            return state; // still stocked from an earlier day — no browsing, no events
        }

        var candidates = CollectCandidates(state);

        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Item.Effect is not { Kind: ConsumableKind.Heal })
            {
                continue; // behavior keyed off the effect DATA, never recipe ids
            }

            var verdict = ShoppingAi.EvaluateConsumable(hero, candidate.Item, candidate.Price);
            candidate.Verdict = verdict;
            if (verdict.Kind != ShoppingVerdictKind.Buy)
            {
                continue;
            }

            if (best is null || ShoppingAi.IsBetterConsumable(
                    candidate.Price, candidate.FromPlayerShelf, candidate.Item.Id,
                    best.Price, best.FromPlayerShelf, best.Item.Id))
            {
                best = candidate;
            }
        }

        // Legible passes mirror the gear pass: every player-shelf Heal item the hero
        // looked at and did not buy gets a reasoned event (R8/AE4).
        foreach (var candidate in candidates)
        {
            if (!candidate.FromPlayerShelf || candidate.Verdict is null || ReferenceEquals(candidate, best))
            {
                continue;
            }

            var reason = candidate.Verdict.Kind == ShoppingVerdictKind.Pass
                ? candidate.Verdict.Reason
                : $"picked {best!.Item.Name} instead — cheaper on the day";
            events.Emit(new HeroPassedOnItem(hero.Id, candidate.Item.Id, reason));
        }

        return best is null ? state : ApplyPurchase(state, hero, best, events);
    }

    private static List<Candidate> CollectCandidates(GameState state)
    {
        var candidates = new List<Candidate>(state.Player.Shelf.Count + state.RivalShelf.Count);
        AddShelf(candidates, state, state.Player.Shelf, fromPlayerShelf: true);
        AddShelf(candidates, state, state.RivalShelf, fromPlayerShelf: false);
        return candidates;
    }

    private static void AddShelf(List<Candidate> candidates, GameState state, ImmutableList<ShelfEntry> shelf, bool fromPlayerShelf)
    {
        foreach (var entry in shelf)
        {
            // Defensive: a shelf entry whose item is missing from the catalog is
            // un-evaluable — skip silently rather than crash the morning.
            if (state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                candidates.Add(new Candidate(entry, item, fromPlayerShelf));
            }
        }
    }

    private static GameState ApplyPurchase(GameState state, Hero hero, Candidate bought, IEventSink events)
    {
        // Consumables go into the pack (P2); gear equips into the item's slot. A
        // replaced gear item is simply dropped from the gear set (kept simple by
        // design): it stays in GameState.Items, so its maker's-mark history survives,
        // but nobody bears it. Resale/trade-in is out of U5's scope.
        var updatedHero = bought.Item.Effect is not null
            ? hero with
            {
                Gold = hero.Gold - bought.Price,
                Pack = hero.Pack.Add(bought.Item.Id),
            }
            : hero with
            {
                Gold = hero.Gold - bought.Price,
                Gear = hero.Gear.WithSlot(bought.Item.Slot, bought.Item.Id),
            };
        state = state with { Heroes = state.Heroes.SetItem(hero.Id.Value, updatedHero) };

        if (bought.FromPlayerShelf)
        {
            // Player sale: credit the forge and clear the shelf slot (R16, R17 loop).
            state = state with
            {
                Player = state.Player with
                {
                    Gold = state.Player.Gold + bought.Price,
                    Shelf = state.Player.Shelf.Remove(bought.Entry),
                },
            };
        }
        else
        {
            // Rival sale: the rival's gold is not modeled — the item just leaves the shelf.
            state = state with { RivalShelf = state.RivalShelf.Remove(bought.Entry) };
        }

        events.Emit(new ItemSold(bought.Item.Id, hero.Id, bought.Price, bought.FromPlayerShelf));
        return state;
    }

    /// <summary>One shelf entry under evaluation. Mutable Verdict keeps the pass loop single-pass.</summary>
    private sealed class Candidate(ShelfEntry entry, Item item, bool fromPlayerShelf)
    {
        public ShelfEntry Entry { get; } = entry;
        public Item Item { get; } = item;
        public bool FromPlayerShelf { get; } = fromPlayerShelf;
        public int Price => Entry.Price;
        public ShoppingVerdict? Verdict { get; set; }
    }
}
