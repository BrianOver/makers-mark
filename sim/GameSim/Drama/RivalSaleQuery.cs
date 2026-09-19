using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// P2-MEMORY-26 ("the rival takes a name"): pure derivation of "a hero bought rival iron tonight
/// while your matching piece sat on the shelf before today" — the fact that turns the rival from a
/// percentage into a person who took something from you (link 5, decision 2).
///
/// <para><b>The honest predicate.</b> "Sat there before today" is <see
/// cref="ShelfEntry.StockedDay"/> strictly less than the sale's own day — never same-day. The
/// counter-session tick order inside one day cannot prove which of two same-day events happened
/// first, so a same-day stock is silence, not a guess (KTD law "show only what the sim decided").
/// A piece the player sold earlier the SAME day is already gone from <see cref="PlayerState.Shelf"/>
/// by the time this reads the end-of-day state, so it is excluded for free — it did not "sit there"
/// at night either way.</para>
///
/// <para><b>Which shelf piece.</b> A rival sale can match several player pieces in the same <see
/// cref="ItemSlot"/> that all cleared the day gate. <see cref="Match"/> keeps exactly one — the
/// piece a hero would most plausibly have weighed against the rival's item — ordered the same way
/// <see cref="GameSim.Heroes.ShoppingAi"/> orders a hero's own judgment: quality first (a hero
/// checks what the piece IS before what it costs), then price, then the lower <see
/// cref="ItemId"/> settles a true tie deterministically.</para>
///
/// <para>Pure query, no event, no RNG: reads only <see cref="GameState.EventLog"/>, <see
/// cref="GameState.Items"/>, and the CURRENT <see cref="PlayerState.Shelf"/> — the same read-only
/// shape as <see cref="RivalAbsenceQuery"/>.</para>
/// </summary>
public static class RivalSaleQuery
{
    /// <summary>One rival sale matched to the player's own shelved piece it beat out. Names and
    /// prices are resolved at render time — this record carries only the ids and the recorded
    /// numbers, never invented prose.</summary>
    public sealed record Match(HeroId Buyer, ItemId RivalItem, int RivalPrice, ItemId YourItem, int YourPrice);

    /// <summary>Every rival sale on <paramref name="day"/> that beat a still-comparable player
    /// piece, in <see cref="GameState.EventLog"/> order.</summary>
    public static ImmutableList<Match> ForDay(GameState state, int day)
    {
        var matches = ImmutableList.CreateBuilder<Match>();
        foreach (var evt in state.EventLog)
        {
            if (evt is ItemSold sold && sold.Day == day && MatchFor(state, sold) is { } match)
            {
                matches.Add(match);
            }
        }

        return matches.ToImmutable();
    }

    /// <summary>The single player shelf piece <paramref name="sold"/> beat out, or null when
    /// <paramref name="sold"/> was not a rival sale, the sold item is unresolvable, or no shelved
    /// piece in the same slot sat there before <paramref name="sold"/>'s day.</summary>
    public static Match? MatchFor(GameState state, ItemSold sold)
    {
        if (sold.FromPlayerShop)
        {
            return null; // this unit is about the RIVAL taking a sale, not the player's own.
        }

        if (!state.Items.TryGetValue(sold.Item.Value, out var rivalItem))
        {
            return null;
        }

        ShelfEntry? bestEntry = null;
        Item? bestItem = null;
        foreach (var entry in state.Player.Shelf)
        {
            // Strictly before today — the honest predicate this unit's brief names explicitly.
            if (entry.StockedDay >= sold.Day)
            {
                continue;
            }

            if (!state.Items.TryGetValue(entry.Item.Value, out var candidate) || candidate.Slot != rivalItem.Slot)
            {
                continue;
            }

            if (bestEntry is null || IsMoreComparable(candidate, entry, bestItem!, bestEntry))
            {
                bestEntry = entry;
                bestItem = candidate;
            }
        }

        return bestEntry is null ? null : new Match(sold.Buyer, sold.Item, sold.Price, bestEntry.Item, bestEntry.Price);
    }

    /// <summary>True when <paramref name="candidate"/> is the piece a hero would have compared the
    /// rival's item against ahead of <paramref name="current"/>: higher quality first, then the
    /// cheaper price, then the lower <see cref="ItemId"/> for a deterministic tie-break.</summary>
    private static bool IsMoreComparable(Item candidate, ShelfEntry candidateEntry, Item current, ShelfEntry currentEntry)
    {
        if (candidate.Quality != current.Quality)
        {
            return candidate.Quality > current.Quality;
        }

        if (candidateEntry.Price != currentEntry.Price)
        {
            return candidateEntry.Price < currentEntry.Price;
        }

        return candidateEntry.Item.Value < currentEntry.Item.Value;
    }
}
