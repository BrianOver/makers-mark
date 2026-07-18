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
/// <param name="Standing">Per-faction standing (factionId → integer, neutral 0), the P5 U2
/// drama-layer state (R4/KTD2). TRAILING-OPTIONAL and defaults <c>null</c> — a collection is not a
/// compile-time constant, so it cannot default to <c>.Empty</c> in a positional record (mirrors
/// <see cref="GearSet.Trinket"/>, which defaults null). Null means neutral everywhere, so a pre-core
/// save without the member loads as no-standing / no behavior change (see <see cref="StandingFor"/>).
/// The map materializes on the first <see cref="WithStanding"/> write.</param>
public sealed record PlayerState(
    int Gold,
    ImmutableSortedDictionary<string, int> Materials,
    ImmutableSortedDictionary<string, ImmutableSortedSet<string>> Talents,
    ImmutableSortedSet<string> SelectedProfessions,
    ImmutableList<ShelfEntry> Shelf,
    ImmutableSortedDictionary<string, int>? Standing = null)
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

    /// <summary>
    /// A fresh save with a CHOSEN starting profession and starter materials (Playable Core
    /// R4/KD3). Sibling of <see cref="NewGame(int)"/>, which stays byte-identical as the
    /// blacksmith-default compatibility baseline for the CLI, replays, and existing saves.
    /// </summary>
    public static PlayerState NewGame(
        int startingGold, string profession, ImmutableSortedDictionary<string, int> starterMaterials) => new(
        startingGold,
        starterMaterials,
        ImmutableSortedDictionary<string, ImmutableSortedSet<string>>.Empty,
        ImmutableSortedSet.Create(profession),
        ImmutableList<ShelfEntry>.Empty);

    /// <summary>The unlocked talent node ids for <paramref name="profession"/> (empty if none yet).</summary>
    public ImmutableSortedSet<string> TalentsFor(string profession) =>
        Talents.TryGetValue(profession, out var set) ? set : ImmutableSortedSet<string>.Empty;

    /// <summary>Returns a copy with <paramref name="node"/> added to <paramref name="profession"/>'s talent set.</summary>
    public PlayerState WithTalent(string profession, string node) =>
        this with { Talents = Talents.SetItem(profession, TalentsFor(profession).Add(node)) };

    /// <summary>Whether <paramref name="profession"/> is among the selected professions.</summary>
    public bool IsSelected(string profession) => SelectedProfessions.Contains(profession);

    /// <summary>
    /// This player's standing with <paramref name="factionId"/>; 0 (neutral) when
    /// <see cref="Standing"/> is null or the faction is absent (R4/KTD2). Absent and null both
    /// read as neutral, so a pre-core save behaves exactly like a fresh one until the player trades.
    /// </summary>
    public int StandingFor(string factionId) =>
        Standing is not null && Standing.TryGetValue(factionId, out var value) ? value : 0;

    /// <summary>
    /// Returns a copy with <paramref name="factionId"/>'s standing set to <paramref name="value"/>,
    /// materializing the map on first write (KTD2 — <see cref="Standing"/> defaults null, not
    /// <c>.Empty</c>). Uses the default comparer to match <see cref="Materials"/>, keeping saves
    /// byte-stable across a round-trip.
    /// </summary>
    public PlayerState WithStanding(string factionId, int value) =>
        this with { Standing = (Standing ?? ImmutableSortedDictionary<string, int>.Empty).SetItem(factionId, value) };
}
