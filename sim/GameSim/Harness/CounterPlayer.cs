using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;

namespace GameSim.Harness;

/// <summary>
/// PA5 (plan 2026-07-21-002): the scripted stepped-counter policy — open the counter, present the
/// active customer the shelf's best role-fit item, respond to any standing offer by countering at
/// the round's band-center (a deterministic "read the hero" move that exercises
/// <see cref="HaggleResolver"/>'s pin/fleece math, not just the trivial Accept path), and close once
/// there is nothing left to present. Same purity contract as <see cref="BaselinePlayer"/> — a pure
/// function of <see cref="GameState"/>, no IO, no RNG of its own, no wall clock.
///
/// <para><see cref="BaselinePlayer"/> is UNTOUCHED and never forked: this is a separate policy that
/// lives beside it in <c>Harness/</c> so the determinism suite (and, optionally, the batch farm) can
/// drive a full stepped Morning deterministically. Nothing wires this into
/// <see cref="Tests.Balance.BalanceSimTests"/> or the CLI's default loop — the atomic-equivalence
/// pin (PA3) depends on <see cref="BaselinePlayer"/> never opening the counter.</para>
///
/// <para>Handles the two "nothing to do" mornings without error (no exceptions, no stall): an empty
/// shelf closes the session as soon as the active customer has nothing to look at; an empty queue
/// (no living heroes) closes immediately after <see cref="OpenCounterAction"/> resolves it to a
/// no-active-customer state.</para>
/// </summary>
public static class CounterPlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        if (state.Phase != DayPhase.Morning)
        {
            return actions.ToImmutable(); // the counter only ever opens during Morning (PKD5)
        }

        var counter = state.Counter;
        if (counter is null)
        {
            actions.Add(new OpenCounterAction()); // no session yet this morning — open one
            return actions.ToImmutable();
        }

        if (counter.Closed)
        {
            return actions.ToImmutable(); // already closing this tick — nothing left for this policy to do
        }

        if (counter.Active is not { } activeId
            || !state.Heroes.TryGetValue(activeId.Value, out var hero)
            || !hero.Alive)
        {
            // No customer at the counter — a valid open state (empty queue, PKD6), but this
            // policy's job is done: close so the day can move on.
            actions.Add(new CloseCounterAction());
            return actions.ToImmutable();
        }

        if (counter.Round > 0 && counter.StandingOfferGold is { } standingOffer && counter.Presented is not null)
        {
            // A round is open — counter at THIS round's band-center. Never Accept/HoldFirm: a
            // band-center counter still routes through HaggleResolver.ResolveCounter (pin, fleece,
            // or a plain in-band sale), which is the coverage this policy exists to exercise.
            var listPrice = state.Player.Shelf.FirstOrDefault(e => e.Item == counter.Presented)?.Price ?? standingOffer;
            var presentedQuality = state.Items.TryGetValue(counter.Presented.Value.Value, out var presentedItem)
                ? presentedItem.Quality
                : QualityGrade.Common;
            var trueWillingness = WillingnessModel.TrueWillingness(
                listPrice, hero.Gold, hero.ClassId, counter.InterestPermille, hero.MoodPermille, presentedQuality);
            var (floor, ceiling) = WillingnessModel.Band(trueWillingness, counter.Round);
            var center = Math.Clamp((floor + ceiling) / 2, 1, hero.Gold);
            actions.Add(new HaggleResponseAction(HaggleResponseKind.Counter, center));
            return actions.ToImmutable();
        }

        // Nothing presented yet this round — show the active customer the shelf's best
        // role-fit item. An empty shelf means there is nothing left to sell: close instead of
        // stalling the morning.
        var heroClass = ClassRegistry.Require(hero.ClassId);
        var best = BestRoleFitItem(state, hero, heroClass);
        actions.Add(best is { } chosen ? new PresentItemAction(chosen) : new CloseCounterAction());
        return actions.ToImmutable();
    }

    /// <summary>The shelf item this hero/class combination reads best — since P2-HONEST-45, the
    /// largest upgrade they can wear and afford (or a consumable they still need), per
    /// <see cref="UpgradeFitScore"/> — or <see langword="null"/> for an empty shelf. Iterates in
    /// ItemId order so a score tie always resolves the same way (determinism — no hidden
    /// dictionary-order dependency).
    ///
    /// <para>Internal (not private) so <see cref="ForgeCounterPlayer"/> (P2-HONEST-30) can compose
    /// this exact "show the best role-fit item" opener instead of duplicating it — the two policies
    /// share the presenting rule and diverge only on how they RESPOND to the resulting offer.</para>
    /// </summary>
    internal static ItemId? BestRoleFitItem(GameState state, Hero hero, ClassDefinition heroClass)
    {
        ItemId? best = null;
        var bestScore = int.MinValue;

        foreach (var entry in state.Player.Shelf.OrderBy(e => e.Item.Value))
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                continue; // defensive: a shelf entry outliving its item should never crash the morning
            }

            var score = UpgradeFitScore(state, hero, heroClass, item, entry.Price);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Item;
            }
        }

        return best;
    }

    /// <summary>P2-HONEST-45 (docs/design/MAKERS-MARK.md §11.16): how good an OPENER this shelf
    /// piece is for this customer, at this price. Higher wins.
    ///
    /// <para><b>What this replaced, and why.</b> The original scorer added a flat +1,000 to any
    /// Shield shown to a shield-capable class regardless of gain — a ROLE TOKEN, not an offer. §11.16
    /// measured the cost: 2,142 of 5,655 walks (38%) read "current Buckler is better" or "current
    /// Kite Shield is better" and 1,773 more (31%) "no gear-score improvement", because the opener
    /// kept showing a customer the shield they were already wearing. Every counter number on record
    /// — the fleece rate, the pin rate, the walk reasons — is read through this opener, so an opener
    /// that opens on something the customer cannot use makes the whole corpus lie about decision 2
    /// (price for the sale or the relationship): you cannot price for a relationship over an item
    /// the hero was never going to take.</para>
    ///
    /// <para><b>The rule.</b> Ask <see cref="ShoppingAi"/> the same question the hero will ask
    /// themselves — never re-derive weight, shield or affordability rules here, because a second
    /// copy of them drifts. A piece the hero would BUY ranks first, biggest gear-score gain leading;
    /// then a consumable they still need (<see cref="TraitEffects.ConsumableStockTargetFor"/> above
    /// their current <see cref="Hero.Pack"/>); then anything they could wear and afford but do not
    /// want; then the refusals, worst last. Nothing is excluded outright — a one-item shelf must
    /// still get an opener rather than stalling the morning — so the bottom tiers only decide WHICH
    /// unusable piece gets shown when nothing usable is on the shelf.</para>
    ///
    /// <para>Pure and deterministic: integer tiers, no RNG, no clock. Ties are settled by the
    /// caller's ItemId ordering, so the lowest item id always wins a tie.</para>
    /// </summary>
    internal static int UpgradeFitScore(GameState state, Hero hero, ClassDefinition heroClass, Item item, int price)
    {
        if (item.Slot == ItemSlot.Consumable)
        {
            // Consumables carry no gear score, so ShoppingAi judges them on affordability alone —
            // and whether the hero WANTS one is their own stocking trait's business, not ours.
            if (ShoppingAi.EvaluateConsumable(hero, item, price).Kind != ShoppingVerdictKind.Buy)
            {
                return TierUnaffordable * TierStride;
            }

            return hero.Pack.Count < TraitEffects.ConsumableStockTargetFor(hero)
                ? TierNeededConsumable * TierStride
                : TierUsableNotWanted * TierStride;
        }

        var verdict = ShoppingAi.EvaluateItem(hero, heroClass, item, price, state.Items);
        return verdict.PassReason switch
        {
            // A real upgrade: rank by the gain the hero themselves scored it at.
            PassReasonKind.None => (TierUpgrade * TierStride) + Math.Clamp(verdict.GearScoreGain, 0, TierStride - 1),

            // Wearable and affordable — they just already have better, or won't give up storied gear.
            PassReasonKind.NotAnUpgrade or PassReasonKind.Sentimental => TierUsableNotWanted * TierStride,

            // Wearable and affordable, refused on principle (a veteran and sub-grade work).
            PassReasonKind.QualityTooLow => TierQualityRefused * TierStride,

            PassReasonKind.CannotAfford => TierUnaffordable * TierStride,

            // RoleMismatch, TooHeavy — the customer cannot carry it at all.
            _ => TierUnwearable * TierStride,
        };
    }

    /// <summary>Tier width. Only <see cref="TierUpgrade"/> carries a magnitude inside its tier (the
    /// gear-score gain), and a gain can never reach this, so a tier boundary is never crossed by a
    /// gain — which is what makes "usable beats unusable" a property rather than a tuning.</summary>
    private const int TierStride = 1_000_000;

    internal const int TierUpgrade = 5;           // they would buy it
    internal const int TierNeededConsumable = 4;  // pack under their own stocking target, and affordable
    internal const int TierUsableNotWanted = 3;   // can wear it, can afford it, doesn't want it
    internal const int TierQualityRefused = 2;    // can wear it, can afford it, won't trust the work
    internal const int TierUnaffordable = 1;      // can wear it, cannot pay for it
    internal const int TierUnwearable = 0;        // cannot carry it at all
}
