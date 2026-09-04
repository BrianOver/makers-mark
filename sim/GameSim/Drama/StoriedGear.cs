using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Drama;

/// <summary>
/// One piece of worn gear that has crossed its own bearer's storied threshold, with the recorded
/// facts that put it there: who is carrying it, and how many deeds (Kills + Saves) their
/// <see cref="Hero.Memories"/> record for it. Facts only — no total across the player's work, no
/// ratio, no rank against other gear, no score.
/// </summary>
public sealed record StoriedGearInfo(ItemId Item, HeroId Bearer, string BearerName, int Deeds, int Threshold);

/// <summary>
/// M2b: the promotion the sim has been making silently for months, said out loud. Pure read model
/// over <see cref="Hero.Memories"/> and <see cref="Hero.Gear"/> (the <see cref="ProvenanceQuery"/>/
/// <see cref="LegendQuery"/> family): no state change, no event, no RNG, no wall clock, callable any
/// number of times by a card, a wall, or a counter bubble.
///
/// <para><b>This adds no rule.</b> Every number here is read back out of the ONE gate that already
/// runs — <see cref="ShoppingAi"/>'s storied-gear loyalty check: once a worn item's deeds reach
/// <see cref="ShoppingAi.SentimentalDeedThreshold"/>, shifted per hero by
/// <see cref="TraitEffects.SentimentalDeedThresholdFor"/> along the Sentiment trait axis, the hero
/// will not trade it for a merely-marginal upgrade and passes with
/// <see cref="PassReasonKind.Sentimental"/>. That behaviour has been changing which items heroes
/// keep, differently per hero, with nothing on screen saying so. This file is the query that lets a
/// screen say it; the threshold itself does not move (moving it would be a balance change,
/// rendering it is not).</para>
///
/// <para><b>Deliberately the hero's threshold, never the bare constant.</b> A Sentimental hero
/// clings two deeds sooner and a Practical one effectively never does
/// (<see cref="TraitEffects.SentimentalThresholdSteps"/>/<see cref="TraitEffects.PracticalThresholdSteps"/>),
/// so the same blade with the same deed count is storied to one bearer and ordinary to another.
/// That divergence is meant to be NOTICED in behaviour, not explained in copy: nothing here names a
/// trait, and no surface should.</para>
/// </summary>
public static class StoriedGear
{
    /// <summary>The deed count THIS hero's gear must reach to be storied — the trait-shifted
    /// threshold the gate itself uses, never <see cref="ShoppingAi.SentimentalDeedThreshold"/>
    /// raw.</summary>
    public static int ThresholdFor(Hero hero) =>
        TraitEffects.SentimentalDeedThresholdFor(hero, ShoppingAi.SentimentalDeedThreshold);

    /// <summary>Deeds (Kills + Saves) this hero's own memories record for one item — the same count
    /// the gate reads, from the same method, so the two can never disagree.</summary>
    public static int DeedsFor(Hero hero, ItemId item) => ShoppingAi.WornDeeds(hero, item);

    /// <summary>
    /// The storied record for <paramref name="item"/>, or null when nothing has promoted it: not
    /// worn by anyone living, or worn but still short of that bearer's threshold. An honest empty
    /// state — callers render nothing for null, never a fallback line.
    ///
    /// <para>Living bearers only. The gate this renders can only fire when the hero next shops, and
    /// the dead never shop again; a fallen hero's gear is the Legends Wall's memorial and heirloom
    /// business, and listing it here too would say the same object twice in two registers.</para>
    /// </summary>
    public static StoriedGearInfo? For(GameState state, ItemId item)
    {
        if (!state.Items.TryGetValue(item.Value, out var resolved))
        {
            return null;
        }

        // Heroes is an ImmutableSortedDictionary keyed by HeroId.Value, so this walk is
        // deterministic; gear is exclusive, so at most one living hero can match.
        foreach (var hero in state.Heroes.Values)
        {
            if (!hero.Alive || hero.Gear.Slot(resolved.Slot) != item)
            {
                continue;
            }

            var deeds = DeedsFor(hero, item);
            var threshold = ThresholdFor(hero);
            return deeds >= threshold
                ? new StoriedGearInfo(item, hero.Id, hero.Name, deeds, threshold)
                : null;
        }

        return null;
    }

    /// <summary>
    /// Every storied piece in the world right now, in hero-id then slot order (Weapon, Shield,
    /// Armor, Trinket) so two runs of the same state list them identically.
    /// </summary>
    public static ImmutableList<StoriedGearInfo> All(GameState state)
    {
        var found = ImmutableList.CreateBuilder<StoriedGearInfo>();
        foreach (var hero in state.Heroes.Values)
        {
            if (!hero.Alive)
            {
                continue;
            }

            var threshold = ThresholdFor(hero);
            foreach (var slot in GearSlotOrder)
            {
                if (hero.Gear.Slot(slot) is not { } worn)
                {
                    continue;
                }

                var deeds = DeedsFor(hero, worn);
                if (deeds >= threshold)
                {
                    found.Add(new StoriedGearInfo(worn, hero.Id, hero.Name, deeds, threshold));
                }
            }
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// The item card's line, in the register <c>P2-PROOF</c> already blessed for the memorial ("The
    /// blow read 15. Her shield drank 2."): the recorded deed count, and the one thing the sim has
    /// actually decided because of it. Past tense, no adjective on the work itself, and — the
    /// condition this whole unit ships under — no total across the player's work, no ratio, no
    /// percentage of the party, no rank against other gear, no score. Empty string for a null
    /// record, so a caller can concatenate without branching.
    /// </summary>
    public static string Clause(StoriedGearInfo? info) => info is null
        ? string.Empty
        : $"Storied — {info.BearerName} has carried it through {info.Deeds} {FightsWord(info.Deeds)}, "
          + "and won't trade it away for a small upgrade.";

    /// <summary>Slot order for <see cref="All"/> — fixed, so listing order never depends on a
    /// dictionary walk or a runtime detail.</summary>
    private static readonly ItemSlot[] GearSlotOrder =
    {
        ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor, ItemSlot.Trinket,
    };

    /// <summary>The noun for a deed count. A Sentimental hero's threshold clamps to 1 deed, so
    /// "1 fights" is reachable copy — public so every surface naming the count (the item card here,
    /// the Legends Wall's storied rows) words it the same way.</summary>
    public static string FightsWord(int deeds) => deeds == 1 ? "fight" : "fights";
}
