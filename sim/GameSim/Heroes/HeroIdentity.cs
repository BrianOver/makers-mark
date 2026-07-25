using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Phase B (B1d, R-B4): disambiguates duplicate hero names at READ time only — a pure derived-view
/// helper (the <see cref="RelationshipBands"/> precedent: module-side, not a deny-listed
/// <c>Contracts</c> type, no new sim state). Deliberately does NOT touch <see cref="Hero.Name"/> at
/// recruit-gen: mutating the stored name would poison Phase B2's <c>(HeroId, Name)</c> trait-hash
/// input and would need a golden re-pin for zero mechanical benefit. Instead, a hero who shares
/// their bare name with an earlier-recruited hero (lower <see cref="HeroId"/> — arrival order) is
/// shown with a collision-ordinal epithet ("the Younger", "the Third", ...) wherever this is called;
/// the first (lowest-id) namesake keeps the bare name.
/// </summary>
public static class HeroIdentity
{
    /// <summary>The name to DISPLAY for a hero — bare name unless another living-or-dead roster
    /// entry shares it, in which case every namesake but the first (lowest HeroId) gets an ordinal
    /// epithet. An unknown hero id falls back to its raw id string (defensive; never throws).</summary>
    public static string DisplayName(HeroId hero, GameState state)
    {
        if (!state.Heroes.TryGetValue(hero.Value, out var target))
        {
            return hero.ToString();
        }

        var namesakesAscending = state.Heroes.Values
            .Where(h => h.Name == target.Name)
            .OrderBy(h => h.Id.Value)
            .ToList();

        if (namesakesAscending.Count <= 1)
        {
            return target.Name;
        }

        var ordinal = namesakesAscending.FindIndex(h => h.Id == hero); // 0-based arrival position
        return ordinal <= 0 ? target.Name : $"{target.Name} {Epithet(ordinal)}";
    }

    /// <summary>The collision-ordinal epithet for the Nth (0-based, N&gt;=1) namesake.</summary>
    private static string Epithet(int ordinal) => ordinal switch
    {
        1 => "the Younger",
        2 => "the Third",
        3 => "the Fourth",
        _ => $"the {ordinal + 1}th",
    };
}
