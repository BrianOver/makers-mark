using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// P2-PEOPLE-28 ("hold it for Torvald"), link 5's own line for decision 1: the two facts a hold
/// produces once the story moves past the hold itself — the hero it was held for actually came for
/// it, or it is STILL waiting for them tonight. Pure read model over <see cref="GameState.EventLog"/>
/// and (for the still-waiting half) the CURRENT <see cref="PlayerState.Shelf"/> — same shape as
/// <see cref="RivalSaleQuery"/>. Zero RNG, callable any number of times.
/// </summary>
public static class EarmarkQuery
{
    /// <summary>One piece bought tonight by the hero it was held for.</summary>
    public sealed record HeldSale(HeroId Hero, ItemId Item);

    /// <summary>One piece still sitting held, unsold, as of tonight — earmarked strictly before today.</summary>
    public sealed record StillWaiting(ItemId Item, HeroId Hero);

    /// <summary>
    /// Every player-shelf sale on <paramref name="day"/> whose buyer is the hero the piece was last
    /// earmarked for. <see cref="ItemSold.FromPlayerShop"/> only (the rival never earmarks) — the
    /// buyer always matches when a hold is found, because
    /// <see cref="Heroes.HeroShoppingSystem.IsHeldForSomeoneElse"/> excludes every other hero as a
    /// shopping candidate before the sale can happen at all; this only names the fact. The shelf
    /// entry itself is gone by the time this reads <see cref="GameState"/> (a sale removes it), so
    /// the hold is read back from the log rather than the live shelf, per the unit's own brief.
    /// </summary>
    public static ImmutableList<HeldSale> ForDay(GameState state, int day)
    {
        var results = ImmutableList.CreateBuilder<HeldSale>();
        for (var i = 0; i < state.EventLog.Count; i++)
        {
            if (state.EventLog[i] is ItemSold { FromPlayerShop: true } sold && sold.Day == day
                && LastEarmarkBefore(state, sold.Item, i) is { } hero)
            {
                results.Add(new HeldSale(hero, sold.Item));
            }
        }

        return results.ToImmutable();
    }

    /// <summary>The hero <paramref name="item"/> was held for immediately before log index
    /// <paramref name="beforeIndex"/> — the most recent <see cref="ShelfEarmarked"/> for that item
    /// strictly earlier in the log, or null when it was never earmarked or was last cleared.</summary>
    private static HeroId? LastEarmarkBefore(GameState state, ItemId item, int beforeIndex)
    {
        for (var i = beforeIndex - 1; i >= 0; i--)
        {
            if (state.EventLog[i] is ShelfEarmarked marked && marked.Item == item)
            {
                return marked.Hero;
            }
        }

        return null;
    }

    /// <summary>
    /// Every shelf entry still earmarked tonight, held since strictly before <paramref name="day"/>
    /// — mirrors <see cref="RivalSaleQuery"/>'s own "sat there before today" honesty rule (KTD law
    /// "show only what the sim decided"): a hold placed THIS SAME day is not yet a story about
    /// waiting, it is still today's own decision.
    /// </summary>
    public static ImmutableList<StillWaiting> WaitingTonight(GameState state, int day)
    {
        var results = ImmutableList.CreateBuilder<StillWaiting>();
        foreach (var entry in state.Player.Shelf)
        {
            if (entry.EarmarkedFor is { } hero && EarmarkedSince(state, entry.Item) is { } since && since < day)
            {
                results.Add(new StillWaiting(entry.Item, hero));
            }
        }

        return results.ToImmutable();
    }

    /// <summary>The day the CURRENT hold on <paramref name="item"/> began — the most recent
    /// <see cref="ShelfEarmarked"/> for it, read backward from the end of the log. The caller only
    /// ever asks this for an item <see cref="GameState.Player"/>'s own shelf already proves is
    /// earmarked, so a null return here would mean the log and the live shelf disagree.</summary>
    private static int? EarmarkedSince(GameState state, ItemId item)
    {
        for (var i = state.EventLog.Count - 1; i >= 0; i--)
        {
            if (state.EventLog[i] is ShelfEarmarked marked && marked.Item == item)
            {
                return marked.Day;
            }
        }

        return null;
    }
}
