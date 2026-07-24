using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;

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

    /// <summary>The shelf item this hero/class combination reads best, or <see langword="null"/>
    /// for an empty shelf. Iterates in ItemId order so a score tie always resolves the same way
    /// (determinism — no hidden dictionary-order dependency).</summary>
    private static ItemId? BestRoleFitItem(GameState state, Hero hero, ClassDefinition heroClass)
    {
        ItemId? best = null;
        var bestScore = int.MinValue;

        foreach (var entry in state.Player.Shelf.OrderBy(e => e.Item.Value))
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                continue; // defensive: a shelf entry outliving its item should never crash the morning
            }

            var score = RoleFitScore(state, hero, heroClass, item);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Item;
            }
        }

        return best;
    }

    /// <summary>Higher is a better opener for this hero: a Shield presented to a shield-allowed
    /// anchor class ranks first (mirrors <see cref="HaggleResolver"/>'s own opener-bonus signal —
    /// <c>IsStrongRoleFitPresent</c>), then by gear-score gain over whatever already fills that
    /// slot. A role-mismatch or over-weight item still scores (never excluded outright — the
    /// policy always presents SOMETHING rather than stalling on a one-item shelf), just at the
    /// bottom, so the customer walks with a legible reason instead of the morning hanging.</summary>
    private static int RoleFitScore(GameState state, Hero hero, ClassDefinition heroClass, Item item)
    {
        if (item.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
        {
            return -1000;
        }

        if (heroClass.MaxItemWeight is { } cap && item.Stats.Weight > cap)
        {
            return -900;
        }

        var equippedScore = hero.Gear.Slot(item.Slot) is { } equippedId
                             && state.Items.TryGetValue(equippedId.Value, out var equipped)
            ? equipped.Stats.Attack + equipped.Stats.Defense
            : 0;
        var gain = item.Stats.Attack + item.Stats.Defense - equippedScore;
        var roleFitBonus = item.Slot == ItemSlot.Shield && heroClass.AllowsShield ? 1000 : 0;
        return roleFitBonus + gain;
    }
}
