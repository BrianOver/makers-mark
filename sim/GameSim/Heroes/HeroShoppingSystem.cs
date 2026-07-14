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
/// Event cap (documented behavior): <see cref="HeroPassedOnItem"/> is emitted only for
/// PLAYER-shelf items — the player needs to know why their stock didn't sell (R8/AE4).
/// Rival-shelf passes stay silent to avoid event spam the player can't act on.
/// </summary>
public sealed class HeroShoppingSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "hero-shopping";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // Snapshot the id order up front; ImmutableSortedDictionary keys are already
        // ascending HeroId.Value — the deterministic shopping order.
        foreach (var heroId in state.Heroes.Keys.ToImmutableArray())
        {
            var hero = state.Heroes[heroId];
            if (!hero.Alive)
            {
                continue; // dead heroes never shop (R7 permadeath)
            }

            state = ShopOnce(state, hero, events);
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
        foreach (var candidate in candidates)
        {
            if (!candidate.FromPlayerShelf || ReferenceEquals(candidate, best))
            {
                continue;
            }

            var reason = candidate.Verdict!.Kind == ShoppingVerdictKind.Pass
                ? candidate.Verdict.Reason
                : $"picked {best!.Item.Name} instead — better gear score per gold";
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
        // Equip into the item's slot. The previous item is simply dropped from the
        // gear set (kept simple by design): it stays in GameState.Items, so its
        // maker's-mark history survives, but nobody bears it. Resale/trade-in is
        // out of U5's scope.
        var updatedHero = hero with
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
