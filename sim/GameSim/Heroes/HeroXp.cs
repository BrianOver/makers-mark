namespace GameSim.Heroes;

/// <summary>
/// Phase B (B1c, R-B3): the deterministic, RNG-free XP grant an expedition survivor earns at the
/// Evening reveal, plus the cosmetic rank ladder <see cref="Hero.Xp"/> crosses. Integer-only, no
/// wall clock. Deliberately reads THIS expedition's own facts (<c>ExpeditionResult</c>), never a
/// hero's cumulative <see cref="Hero.Memories"/> tally — summing lifetime kills/saves here would
/// double-count every future expedition (Memories accumulate forever; a per-run XP grant must not).
///
/// TRIPWIRE (fable fix 3): this module NEVER writes <see cref="Hero.Level"/> — <c>CombatMath.cs:29,32</c>
/// read <c>Hero.Level</c> into Attack/Defense, so touching it is a Class-2/Balance-breaking change.
/// Rank is a pure label off <see cref="Hero.Xp"/> thresholds; the XP→Level flip is deliberately
/// deferred to Phase C's hardening window (KTD-B2).
/// </summary>
public static class HeroXp
{
    /// <summary>Flat XP for surviving the expedition at all.</summary>
    public const int SurviveXp = 10;

    /// <summary>XP per floor of this expedition's deepest floor cleared.</summary>
    public const int PerFloorXp = 5;

    /// <summary>XP per killing blow or lethal save THIS survivor is credited with this expedition
    /// (from <c>ExpeditionResult.Beats</c> — never the lifetime <see cref="Hero.Memories"/> tally).</summary>
    public const int PerBeatXp = 15;

    /// <summary>The XP a surviving hero earns for one expedition.</summary>
    public static int ForExpedition(int deepestFloorCleared, int creditedBeats) =>
        SurviveXp + Math.Max(0, deepestFloorCleared) * PerFloorXp + Math.Max(0, creditedBeats) * PerBeatXp;
}

/// <summary>
/// Phase B (B1c): the cosmetic rank ladder off accrued <see cref="Hero.Xp"/> — a label only,
/// never a mechanical effect. Ascending thresholds; a hero's rank is the highest one their XP
/// has reached or passed.
/// </summary>
public static class HeroRank
{
    /// <summary>(XP threshold, rank name), ascending. APPEND ONLY if the ladder ever grows —
    /// changing an existing threshold moves every save's displayed rank.</summary>
    public static readonly (int Threshold, string Name)[] Ladder =
    [
        (0, "Novice"),
        (50, "Delver"),
        (150, "Journeyman"),
        (300, "Veteran"),
        (500, "Champion"),
        (800, "Legend"),
    ];

    /// <summary>The rank name for a given XP total — the highest threshold not exceeding it.</summary>
    public static string For(int xp)
    {
        var rank = Ladder[0].Name;
        foreach (var (threshold, name) in Ladder)
        {
            if (xp < threshold)
            {
                break;
            }

            rank = name;
        }

        return rank;
    }
}
