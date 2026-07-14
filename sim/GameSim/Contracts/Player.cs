using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>An item on the player's shelf with an asking price (R16).</summary>
public sealed record ShelfEntry(ItemId Item, int Price);

/// <summary>
/// The player's side of the world (A1): gold, raw materials by grade key,
/// unlocked talent node ids, and the shop shelf.
/// Materials use a sorted dictionary so serialization is byte-stable.
/// </summary>
public sealed record PlayerState(
    int Gold,
    ImmutableSortedDictionary<string, int> Materials,
    ImmutableSortedSet<string> Talents,
    ImmutableList<ShelfEntry> Shelf)
{
    public static PlayerState NewGame(int startingGold) => new(
        startingGold,
        ImmutableSortedDictionary<string, int>.Empty,
        ImmutableSortedSet<string>.Empty,
        ImmutableList<ShelfEntry>.Empty);
}
