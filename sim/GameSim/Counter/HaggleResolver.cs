using GameSim.Classes;
using GameSim.Contracts;

namespace GameSim.Counter;

/// <summary>
/// PA4 (plan 2026-07-21-002, PKD6/PKD7): applies <see cref="WillingnessModel"/>'s pure math to
/// live <see cref="GameState"/> — opening a haggle round on a strong presentment, resolving one
/// <see cref="HaggleResponseAction"/> (Accept / HoldFirm / Counter), and closing a sale. Every
/// method here is a pure function of (state, hero, item, meters) — ZERO RNG (spec §Determinism
/// model), and every gold movement mirrors <see cref="Heroes.HeroShoppingSystem"/>'s conservation
/// shape exactly (hero pays, player receives — nothing minted, nothing destroyed).
/// <para>PKD7 (influence, never orders): the only hero field this ever writes is
/// <see cref="Hero.MoodPermille"/> — gold, gear, and pack. It NEVER touches party formation, floor
/// choice, or expedition resolution; those systems don't even read <see cref="CounterState"/>.</para>
/// </summary>
internal static class HaggleResolver
{
    /// <summary>A shown item strongly fits the hero's role today: a Shield presented to a
    /// shield-bearing anchor (Recettear: "Vanguard overpays for a fitting shield"). Extend this
    /// (data-driven) if a future class's fit signal needs more nuance — kept minimal here so the
    /// anti-solved-meta pin (role-fit + mood beats one global markup) has a concrete, testable lever
    /// distinct from the class factor table.</summary>
    private static bool IsStrongRoleFitPresent(ClassDefinition heroClass, Item item) =>
        item.Slot == ItemSlot.Shield && heroClass.AllowsShield;

    /// <summary>A suggested item lands on a complementary EMPTY slot the hero would actually wear —
    /// role-fit checks mirror <see cref="Heroes.ShoppingAi"/> (shield-capable, under the weight cap)
    /// so the upsell bonus only fires for a slot this hero could plausibly buy into.</summary>
    private static bool IsComplementaryEmptySlot(ClassDefinition heroClass, Hero hero, Item item)
    {
        if (item.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
        {
            return false;
        }

        if (heroClass.MaxItemWeight is { } cap && item.Stats.Weight > cap)
        {
            return false;
        }

        return hero.Gear.Slot(item.Slot) is null;
    }

    /// <summary>Opens round 1 for a freshly presented, genuinely-desired item (the caller already
    /// confirmed a <see cref="Heroes.ShoppingVerdict"/> Buy): seeds the opener Interest bonus on a
    /// strong role-fit, computes true willingness, and sets the hero's opening (lowball) standing
    /// offer at the round-1 floor. Emits <see cref="CustomerCountered"/> for the opening offer.</summary>
    public static CounterState OpenRound(CounterState counter, Hero hero, ClassDefinition heroClass, Item item, int listPrice, IEventSink events)
    {
        var interest = IsStrongRoleFitPresent(heroClass, item)
            ? WillingnessModel.AddInterest(counter.InterestPermille, WillingnessModel.RoleFitOpenerBonusPermille)
            : counter.InterestPermille;

        const int round = 1;
        var trueWillingness = WillingnessModel.TrueWillingness(listPrice, hero.Gold, hero.ClassId, interest, hero.MoodPermille);
        var (floor, _) = WillingnessModel.Band(trueWillingness, round);

        events.Emit(new CustomerCountered(hero.Id, floor));

        return counter with
        {
            Round = round,
            InterestPermille = interest,
            StandingOfferGold = floor,
        };
    }

    /// <summary>Upsell (SuggestItem): bumps session Interest when the suggestion lands on a
    /// complementary empty slot; a legal no-op otherwise (never rejects — PA3 contract).</summary>
    public static CounterState ApplySuggestBonus(CounterState counter, Hero hero, ClassDefinition heroClass, Item item) =>
        IsComplementaryEmptySlot(heroClass, hero, item)
            ? counter with { InterestPermille = WillingnessModel.AddInterest(counter.InterestPermille, WillingnessModel.UpsellInterestBonusPermille) }
            : counter;

    /// <summary>Resolves one <see cref="HaggleResponseAction"/> against the CURRENT standing offer
    /// (set by <see cref="OpenRound"/> or a prior HoldFirm). Legality (afford/positive price) is
    /// checked here, before any state mutation — a typed rejection, zero RNG, matching the
    /// CounterHandlers rejection contract.</summary>
    public static (GameState State, RejectedAction? Rejected) ResolveHaggleResponse(
        GameState state, CounterState counter, Hero hero, Item item, ShelfEntry shelfEntry,
        HaggleResponseAction action, IEventSink events) =>
        action.Kind switch
        {
            HaggleResponseKind.Accept =>
                (CloseSale(state, counter, hero, item, shelfEntry, counter.StandingOfferGold!.Value, pinned: false, events), null),

            HaggleResponseKind.HoldFirm =>
                (HoldFirm(state, counter, hero, item, shelfEntry.Price, events), null),

            HaggleResponseKind.Counter => ResolveCounter(state, counter, hero, item, shelfEntry, action, events),

            _ => (state, new RejectedAction(action, $"CounterHandlers cannot resolve haggle kind {action.Kind}.")),
        };

    /// <summary>HoldFirm: consumes one Patience round. Patience exhausted → the customer walks
    /// (typed reason). Otherwise the round advances (capped) and the band recomputes — the
    /// Recettear shift that can turn a refused round-1 price into an accepted round-2 one.</summary>
    private static GameState HoldFirm(GameState state, CounterState counter, Hero hero, Item item, int listPrice, IEventSink events)
    {
        var patience = counter.PatienceRounds - 1;
        if (patience <= 0)
        {
            events.Emit(new CustomerWalked(hero.Id, item.Id, "the customer's patience ran out"));
            return CounterQueueSystem.Advance(state, counter with { PatienceRounds = 0 }, hero.Id, events);
        }

        var nextRound = Math.Min(counter.Round + 1, WillingnessModel.MaxRounds);
        var trueWillingness = WillingnessModel.TrueWillingness(listPrice, hero.Gold, hero.ClassId, counter.InterestPermille, hero.MoodPermille);
        var (floor, _) = WillingnessModel.Band(trueWillingness, nextRound);

        events.Emit(new CustomerCountered(hero.Id, floor));

        return state with
        {
            Counter = counter with { Round = nextRound, PatienceRounds = patience, StandingOfferGold = floor },
        };
    }

    /// <summary>Counter(price): three outcomes, all of which close the sale (the countered price
    /// always trades — the only question is how the hero feels about it). Above the round's
    /// ceiling is a fleece (Goodwill/mood penalty); inside the pin window is a read (mood bonus,
    /// <c>Pinned: true</c>); anything else is a plain sale at the named price.</summary>
    private static (GameState, RejectedAction?) ResolveCounter(
        GameState state, CounterState counter, Hero hero, Item item, ShelfEntry shelfEntry,
        HaggleResponseAction action, IEventSink events)
    {
        if (action.Price is not { } price || price <= 0)
        {
            return (state, new RejectedAction(action, "Counter requires a positive price."));
        }

        if (price > hero.Gold)
        {
            return (state, new RejectedAction(action, $"Countered price {price}g exceeds what the hero can afford ({hero.Gold}g)."));
        }

        var trueWillingness = WillingnessModel.TrueWillingness(shelfEntry.Price, hero.Gold, hero.ClassId, counter.InterestPermille, hero.MoodPermille);
        var (_, ceiling) = WillingnessModel.Band(trueWillingness, counter.Round);

        if (price > ceiling)
        {
            var fleecedSession = counter with { GoodwillPermille = counter.GoodwillPermille - WillingnessModel.FleeceGoodwillPenaltyPermille };
            return (CloseSale(state, fleecedSession, hero, item, shelfEntry, price, pinned: false, events, moodDelta: -WillingnessModel.FleeceMoodPenalty), null);
        }

        if (WillingnessModel.IsPin(price, trueWillingness))
        {
            return (CloseSale(state, counter, hero, item, shelfEntry, price, pinned: true, events, moodDelta: WillingnessModel.PinMoodBonus), null);
        }

        return (CloseSale(state, counter, hero, item, shelfEntry, price, pinned: false, events), null);
    }

    /// <summary>Closes the sale at <paramref name="price"/>: gold moves exactly (hero pays, player
    /// receives — U7 conservation), gear equips / consumables pack (mirrors
    /// <see cref="Heroes.HeroShoppingSystem"/>'s purchase application), an optional mood delta
    /// lands on the hero (PKD7: influence only), then the session advances to the next customer.</summary>
    private static GameState CloseSale(
        GameState state, CounterState counter, Hero hero, Item item, ShelfEntry shelfEntry,
        int price, bool pinned, IEventSink events, int? moodDelta = null)
    {
        var boughtHero = item.Effect is not null
            ? hero with { Gold = hero.Gold - price, Pack = hero.Pack.Add(item.Id) }
            : hero with { Gold = hero.Gold - price, Gear = hero.Gear.WithSlot(item.Slot, item.Id) };

        var updatedHero = moodDelta is { } delta
            ? boughtHero with { MoodPermille = boughtHero.MoodPermille + delta }
            : boughtHero;

        var newState = state with
        {
            Heroes = state.Heroes.SetItem(hero.Id.Value, updatedHero),
            Player = state.Player with
            {
                Gold = state.Player.Gold + price,
                Shelf = state.Player.Shelf.Remove(shelfEntry),
            },
        };

        events.Emit(new CounterSaleClosed(hero.Id, item.Id, price, pinned));

        return CounterQueueSystem.Advance(newState, counter, hero.Id, events);
    }
}
