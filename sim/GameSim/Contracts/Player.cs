using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>An item on the player's shelf with an asking price (R16).</summary>
public sealed record ShelfEntry(ItemId Item, int Price);

/// <summary>
/// The player's side of the world (A1): gold, raw materials by grade key, unlocked talent
/// node ids PER PROFESSION, the selected professions (pick 1–2), and the shop shelf.
/// Materials and <see cref="Talents"/> use sorted collections so serialization is byte-stable
/// (KTD4). Comparer choice matches the codebase convention for serialized player collections
/// (the default comparer, as <see cref="Materials"/> and the former flat talent set use), so a
/// save/load round-trip is byte-identical — System.Text.Json rebuilds sorted collections with
/// the default comparer, and every live set is built the same way.
/// </summary>
/// <param name="Talents">Profession key → the set of unlocked talent node ids for that
/// profession. A flat set no longer works: talents are scoped per profession (P1).</param>
/// <param name="SelectedProfessions">The professions this save has taken (1–2). Only recipes
/// whose profession is selected may be crafted.</param>
public sealed record PlayerState(
    int Gold,
    ImmutableSortedDictionary<string, int> Materials,
    ImmutableSortedDictionary<string, ImmutableSortedSet<string>> Talents,
    ImmutableSortedSet<string> SelectedProfessions,
    ImmutableList<ShelfEntry> Shelf)
{
    /// <summary>
    /// A fresh save: no materials, no talents, and the blacksmith selected — so existing
    /// single-profession behaviour is preserved out of the box (P1).
    /// </summary>
    public static PlayerState NewGame(int startingGold) => new(
        startingGold,
        ImmutableSortedDictionary<string, int>.Empty,
        ImmutableSortedDictionary<string, ImmutableSortedSet<string>>.Empty,
        ImmutableSortedSet.Create("blacksmith"),
        ImmutableList<ShelfEntry>.Empty);

    /// <summary>The unlocked talent node ids for <paramref name="profession"/> (empty if none yet).</summary>
    public ImmutableSortedSet<string> TalentsFor(string profession) =>
        Talents.TryGetValue(profession, out var set) ? set : ImmutableSortedSet<string>.Empty;

    /// <summary>Returns a copy with <paramref name="node"/> added to <paramref name="profession"/>'s talent set.</summary>
    public PlayerState WithTalent(string profession, string node) =>
        this with { Talents = Talents.SetItem(profession, TalentsFor(profession).Add(node)) };

    /// <summary>Whether <paramref name="profession"/> is among the selected professions.</summary>
    public bool IsSelected(string profession) => SelectedProfessions.Contains(profession);
}
