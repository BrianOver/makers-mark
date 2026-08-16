using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Drama;

/// <summary>One hero's position in the projected counter queue: who they are, the slot they will
/// open with (null only for a full-loadout hero with nothing on the shelf worth asking about —
/// <see cref="CounterForecast.Wants"/>'s "just browsing" case), and their own gold on hand — never
/// a rounded or invented figure.</summary>
public sealed record CounterAsk(HeroId Hero, ItemSlot? WantSlot, int Gold);

/// <summary>
/// U1 (§11.11, "tomorrow's asks, in front of tonight's shelf"): the pure projection of the counter
/// queue <see cref="Counter.CounterHandlers"/>'s <c>ApplyOpen</c> builds when the counter actually
/// opens. Same contract shape as <see cref="Heroes.RaidForecast"/> — copied verbatim from that
/// class's own doc: this layers a read-only projection over EXACTLY the ordering/selection logic
/// the handler already runs, so the board can never disagree with what the handler forms.
///
/// <para><see cref="Queue"/> extracts <c>ApplyOpen</c>'s own comparator (U8's "regulars first":
/// relationship band descending, then <see cref="HeroId"/> ascending) into one function both the
/// handler and every UI caller run — a handler bug and a display bug can no longer independently
/// drift apart, by construction rather than by two tests agreeing. <see cref="Wants"/> extracts
/// <c>CustomerVoice.WantLine</c>'s (godot/scripts/ui/CustomerVoice.cs) own want-selection logic the
/// same way: the first empty gear slot in the fixed Weapon/Shield/Armor order, or — for a full
/// loadout — whichever CURRENT shelf item is the largest genuine (Buy-verdict) upgrade, or null
/// when nothing on the shelf would help (the "just browsing" case). A want this function does not
/// name is a want the counter cannot ask for.</para>
///
/// <para>Pure, allocation-only: no RNG draw, no wall clock, no <c>Math.*</c> (KTD2) — a projection
/// that perturbed the shared stream would change every subsequent roll, so this draws none.</para>
/// </summary>
public static class CounterForecast
{
    /// <summary>The ordered queue <c>ApplyOpen</c> would build if the counter opened THIS instant:
    /// alive heroes only, higher <see cref="RelationshipBand"/> first, <see cref="HeroId"/> value
    /// breaking ties — <see cref="Counter.CounterHandlers"/>'s own comparator, extracted rather than
    /// duplicated. Empty when no hero is alive (a valid, arranging-only counter session).</summary>
    public static ImmutableList<CounterAsk> Queue(GameState state) =>
        state.Heroes.Values
            .Where(h => h.Alive)
            .OrderByDescending(h => (int)RelationshipBands.For(h.Id, state))
            .ThenBy(h => h.Id.Value)
            .Select(h => new CounterAsk(h.Id, Wants(h, state), h.Gold))
            .ToImmutableList();

    /// <summary>What this hero opens with — <c>CustomerVoice.WantLine</c>'s own slot pick,
    /// extracted: the first empty gear slot (<see cref="RaidForecast.MissingItemSlots"/>, fixed
    /// Weapon/Shield/Armor order), or — for a full loadout — the slot of whichever shelf item is
    /// the largest genuine upgrade (the highest gear-score-gain <c>Buy</c> verdict
    /// <see cref="ShoppingAi.EvaluateItem"/> returns for any shelf entry), or null when nothing on
    /// the shelf would help them. Never names a want the sim would refuse if presented.</summary>
    public static ItemSlot? Wants(Hero hero, GameState state)
    {
        var missing = RaidForecast.MissingItemSlots(hero.Gear);
        if (missing.Count > 0)
        {
            return missing[0];
        }

        var heroClass = ClassRegistry.Require(hero.ClassId);
        ItemSlot? bestSlot = null;
        var bestGain = 0;
        foreach (var entry in state.Player.Shelf)
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                continue;
            }

            var verdict = ShoppingAi.EvaluateItem(hero, heroClass, item, entry.Price, state.Items);
            if (verdict.Kind == ShoppingVerdictKind.Buy && verdict.GearScoreGain > bestGain)
            {
                bestGain = verdict.GearScoreGain;
                bestSlot = item.Slot;
            }
        }

        return bestSlot;
    }
}
