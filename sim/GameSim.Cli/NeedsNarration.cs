using System.Collections.Immutable;
using GameSim.Heroes;

namespace GameSim.Cli;

/// <summary>
/// Phase B (B4, R-B7): renders <see cref="NeedsSystem.Snapshot"/> into the needs-lite demand-board
/// surface — pure formatting only (KTD-5, the <see cref="DemandNarration"/> precedent): no state
/// mutation, no RNG draw, no new event. Two kinds of line:
/// <list type="bullet">
/// <item><description><see cref="CrossingLines"/> — one-shot beats, printed only the day a hero's
/// streak actually crosses a threshold (telegraph / boycott-begins / recovery), so this stays a
/// bark, not a repeated status dump.</description></item>
/// <item><description><see cref="StandingLines"/> — the persistent "who's currently unhappy" list
/// for the <c>demand</c> verb's full dump, shown every day a hero is telegraphed or boycotting.</description></item>
/// </list>
/// </summary>
public static class NeedsNarration
{
    /// <summary>The day-of crossing beats — telegraph warning, boycott biting, and recovery —
    /// exactly the three lines the plan's own worked example names ("Sera has found nothing worth
    /// buying for N days" -> boycott -> welcome back). Empty on a day nobody crosses a threshold.</summary>
    public static ImmutableList<string> CrossingLines(ImmutableList<NeedsEntry> snapshot)
    {
        var lines = ImmutableList.CreateBuilder<string>();
        foreach (var entry in snapshot)
        {
            if (entry.TelegraphedToday)
            {
                lines.Add($"  ⚠ {entry.HeroName} has found nothing worth buying for {entry.StreakDays} days — a rival stall is starting to look better.");
            }

            if (entry.BoycottBeganToday)
            {
                lines.Add($"  ✂ {entry.HeroName} has had enough — {entry.StreakDays} days empty-handed, and now their coin goes to the rival stall instead.");
            }

            if (entry.RecoveredToday)
            {
                lines.Add($"  ↩ {entry.HeroName} finally found something worth buying — welcome back to the counter.");
            }
        }

        return lines.ToImmutable();
    }

    /// <summary>The persistent "currently unhappy" list (the <c>demand</c> verb's full dump):
    /// every telegraphed-or-boycotting hero, one line each, shown every day regardless of whether
    /// today crossed a threshold — so a player checking mid-window can still see who's at risk.</summary>
    public static ImmutableList<string> StandingLines(ImmutableList<NeedsEntry> snapshot)
    {
        var lines = ImmutableList.CreateBuilder<string>();
        lines.Add("  -- needs (unmet demand) --");
        if (snapshot.IsEmpty)
        {
            lines.Add("    (nobody's gone without a sale long enough to notice)");
            return lines.ToImmutable();
        }

        foreach (var entry in snapshot)
        {
            var status = entry.Boycotting ? "BOYCOTTING (favors the rival stall)" : "telegraphed (warning window)";
            lines.Add($"    {entry.Hero} {entry.HeroName}: {entry.StreakDays} days without a player purchase — {status}");
        }

        return lines.ToImmutable();
    }
}
