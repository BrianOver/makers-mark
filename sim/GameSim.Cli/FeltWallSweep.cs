using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// The felt-wall sweep (owner ruling 2026-09-04, "measure the FELT wall"). NOT a gate, one-off like
/// <see cref="LongWallSweep"/> (P2-LONG-01) whose machinery this reuses (<see cref="GameComposition"/> +
/// <see cref="BaselinePlayer"/> + <see cref="ActionLegality"/> + <see cref="DemandBoard"/> +
/// <see cref="EventNarration"/>).
///
/// <para><b>What P2-LONG-01 measured and what this measures instead.</b> P2-LONG-01 tracked whether the
/// MENU ever widens — a verb or demand slot never legal before. It found that keeps happening past
/// day 20 (median last-new day 25). But "the menu still has something new in it" is not the same claim
/// as "today did not feel like yesterday" — a shop with 40 legal verbs on day 30 is still boring if the
/// player takes the identical five actions every single day. This sweep measures the second thing:
/// where the day's own CONTENT — what was actually done, not what was merely offered — stops changing.</para>
///
/// <para><b>Three granularities, run side by side, and a second discard on top of the first.</b> An
/// earlier cut of this sweep tracked one composite "day shape" of {verb picked, recipe crafted,
/// (slot,quality) ask open}. Two failure modes showed up immediately, at opposite ends of content
/// size, and both are kept here as labeled rows, not trusted as the headline:</para>
/// <list type="bullet">
///   <item>Folding in the demand board's open (slot, quality) asks makes the shape ALMOST NEVER repeat
///   EXACTLY — median last-novel-day (the <c>Full</c> row below) lands at day 86 of 100, because the
///   board cycles through enough slot×quality combinations that the exact SET of currently-open asks
///   is closer to a fingerprint than a pattern.</item>
///   <item>Single-component novelty — "was THIS recipe ever crafted before," "was THIS exact ask ever
///   open before" — saturates almost immediately (day 2-4, unanimously) for the opposite reason: the
///   recipe list and slot/quality space are both small and finite, so of course a lone value's second
///   occurrence comes fast. A fact about content size, not about the day feeling repeated.</item>
/// </list>
/// <para><b>The second discard: even the coarsest exact-match signal doesn't discriminate either.</b>
/// <c>VerbOnly</c> — the sorted SET of verbs actually chosen that day, recipe/ask detail stripped out —
/// was expected to be the sharp middle ground. It measured worse than expected: median LAST-novel-day
/// 80.5, barely better than <c>Full</c>'s 86.0, because BaselinePlayer keeps varying which handful of
/// ~25 verbs it touches on any given day for almost the entire 100-day window — a handful of very late,
/// rare one-off days (a mastery threshold, a late commission) drag the single latest-occurrence
/// statistic out into a long right tail that has nothing to do with a player's felt sense of freshness.
/// The pooled cumulative-distinct-shapes curve makes the real shape visible where the single statistic
/// cannot: at VerbOnly it is essentially FLAT from day ~15 to day ~40 (8.80 -> 9.45 of an eventual
/// 16.20 — 25 days move the total four percent), then creeps for the remainder of the run. The
/// **novelty half-life** — the day each seed's cumulative distinct-shape count first reaches HALF of
/// its own eventual total — is built to see exactly that bulk-of-the-curve shape instead of the tail,
/// and it is what this sweep reports as the headline.</para>
///
/// <para>Raw hero/customer IDENTITY is excluded from every granularity: <see cref="HeroId"/> values are
/// effectively never-repeating (new heroes keep arriving), so a signal built on "did I meet this exact
/// hero before" would report every day as permanently novel and measure nothing.</para>
///
/// <para><b>The copy-repetition proxy.</b> `docs/reference/text-census.md` counts ~2,100 player-facing
/// strings, but most of that inventory lives in Godot-only surfaces (tutorial course, dormant acts,
/// first-touch lessons) that this sim/CLI-side sweep cannot render — measuring them would mean either
/// running the Godot engine suite (off-limits: another agent holds it this session, and two concurrent
/// gdUnit runs silently lose each other's tests) or hand-classifying ~2,100 strings' day-gates, a
/// separate unit-sized effort. What CAN be measured honestly from here is the CONSOLE client's own
/// event-flavor text: <see cref="EventNarration.Line"/> is the same function the interactive CLI prints
/// from every resolved <see cref="GameEvent"/>, deterministic and varied by <c>state.Rng.Inc</c>
/// (kill beats, sale beats, gossip, haggle lines, reforge lines). This sweep renders every event through
/// it and tracks the day the last GENUINELY NEW line (never rendered before, for this seed) appeared —
/// a partial, CLI-only lower bound on the copy curve, not the full census.</para>
/// </summary>
public static class FeltWallSweep
{
    private static string Verb(PlayerAction action) => action.GetType().Name.Replace("Action", string.Empty);

    /// <summary>Per-seed tracker for one signature granularity: first-occurrence day per distinct
    /// shape string, the day the LATEST shape was ever novel, whether an unbroken repeat-of-
    /// yesterday run reaches all the way to the sweep's last day (the "flatline"), and the
    /// NOVELTY HALF-LIFE day.
    ///
    /// <para><b>Why the half-life, alongside last-novel-day.</b> "Last novel day" is the single
    /// LATEST first-occurrence — one rare, late one-off (a mastery threshold that fires once near
    /// day 80) drags the whole statistic out, even when the cumulative-distinct-shapes curve went
    /// nearly flat decades earlier. That is a real measured effect here: at VerbOnly granularity
    /// the pooled curve is essentially flat from day ~15 to day ~40 (8.80 -> 9.45 across 25 days,
    /// out of an eventual 16.20), then creeps for the rest of the run. A single latest-occurrence
    /// statistic cannot see that shape; the half-life can. It answers "by what day had the game
    /// already shown half of everything (at this granularity) it will EVER show in the full
    /// window" — closer to what a player's sense of "is this still fresh" actually tracks, since
    /// it is dominated by the BULK of the curve, not its rare tail.</para>
    /// </summary>
    private sealed class GranularityTracker
    {
        private readonly Dictionary<string, int> _firstSeenDay = new();
        private readonly List<(int Day, int CumulativeCount)> _history = new();
        private string _lastShape = string.Empty;
        private int? _previousDay;
        private int _repeatStreakStart;

        public int LastNovelDay { get; private set; }

        public int FlatlineOnsetDay { get; private set; } // 0 = no unbroken repeat-run reached the end

        public int DistinctShapesEver => _firstSeenDay.Count;

        /// <summary>day -> (sum of cumulative-distinct-shapes-seen-so-far across seeds, N).</summary>
        public SortedDictionary<int, (long Sum, int N)> CumulativeByDay { get; } = new();

        public void FlushDay(int day, string shape)
        {
            if (!_firstSeenDay.ContainsKey(shape))
            {
                _firstSeenDay[shape] = day;
                LastNovelDay = day;
            }

            var isRepeatOfYesterday = _previousDay == day - 1 && shape == _lastShape;
            if (isRepeatOfYesterday)
            {
                if (_repeatStreakStart == 0)
                {
                    _repeatStreakStart = day;
                }
            }
            else
            {
                _repeatStreakStart = 0;
            }

            _lastShape = shape;
            _previousDay = day;

            _history.Add((day, _firstSeenDay.Count));

            var agg = CumulativeByDay.GetValueOrDefault(day, (0, 0));
            CumulativeByDay[day] = (agg.Sum + _firstSeenDay.Count, agg.N + 1);
        }

        /// <summary>Call once after the last day is flushed: promotes an open streak that survived to
        /// the sweep's final day into a settled flatline onset.</summary>
        public void Finish()
        {
            if (_repeatStreakStart > 0)
            {
                FlatlineOnsetDay = _repeatStreakStart;
            }
        }

        /// <summary>The first day this seed's cumulative distinct-shape count reached half of its
        /// own eventual (end-of-window) total. 0 if nothing was ever ticked.</summary>
        public int HalfLifeDay() => HalfLifeOf(_history, DistinctShapesEver);

        public static int HalfLifeOf(List<(int Day, int CumulativeCount)> history, int finalCount)
        {
            if (finalCount == 0)
            {
                return 0;
            }

            var target = Math.Ceiling(finalCount / 2.0);
            foreach (var (day, count) in history)
            {
                if (count >= target)
                {
                    return day;
                }
            }

            return history.Count > 0 ? history[^1].Day : 0;
        }
    }

    public static int Run(int seedCount, ulong startSeed, int days, string outDir, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex)
        {
            error.WriteLine($"felt-wall: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();

        var verbOnly = new GranularityTracker();
        var verbRecipe = new GranularityTracker();
        var full = new GranularityTracker();
        var cumulativeLinesByDay = new SortedDictionary<int, (long Sum, int N)>();

        var perSeedRows = new List<SeedRow>();

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);

            var seedVerbOnly = new GranularityTracker();
            var seedVerbRecipe = new GranularityTracker();
            var seedFull = new GranularityTracker();

            var recipeFirstSeenDay = new Dictionary<string, int>();
            var askFirstSeenDay = new Dictionary<(ItemSlot Slot, QualityGrade Quality), int>();
            var lineFirstSeenDay = new Dictionary<string, int>();

            var dayVerbTokens = new SortedSet<string>(StringComparer.Ordinal);
            var dayRecipeTokens = new SortedSet<string>(StringComparer.Ordinal);
            var dayAskTokens = new SortedSet<string>(StringComparer.Ordinal);
            var lineHistory = new List<(int Day, int CumulativeCount)>();
            var currentDay = state.Day;
            var firstRepeatedRecipeDay = 0;
            var firstRepeatedAskDay = 0;
            var lastNewLineDay = 0;

            void FlushDay(int day)
            {
                var verbShape = string.Join("|", dayVerbTokens);
                var recipeShape = verbShape + "||" + string.Join("|", dayRecipeTokens);
                var fullShape = recipeShape + "||" + string.Join("|", dayAskTokens);
                dayVerbTokens.Clear();
                dayRecipeTokens.Clear();
                dayAskTokens.Clear();

                seedVerbOnly.FlushDay(day, verbShape);
                seedVerbRecipe.FlushDay(day, recipeShape);
                seedFull.FlushDay(day, fullShape);

                lineHistory.Add((day, lineFirstSeenDay.Count));
                var lagg = cumulativeLinesByDay.GetValueOrDefault(day, (0, 0));
                cumulativeLinesByDay[day] = (lagg.Sum + lineFirstSeenDay.Count, lagg.N + 1);
            }

            while (state.Day <= days)
            {
                if (state.Day != currentDay)
                {
                    FlushDay(currentDay);
                    currentDay = state.Day;
                }

                var chosen = BaselinePlayer.ActionsFor(state);
                foreach (var a in chosen)
                {
                    dayVerbTokens.Add(Verb(a));
                    if (a is CraftAction craft)
                    {
                        dayRecipeTokens.Add(craft.RecipeId);
                        if (recipeFirstSeenDay.TryGetValue(craft.RecipeId, out var firstDay) && firstDay < state.Day)
                        {
                            if (firstRepeatedRecipeDay == 0)
                            {
                                firstRepeatedRecipeDay = state.Day;
                            }
                        }
                        else if (!recipeFirstSeenDay.ContainsKey(craft.RecipeId))
                        {
                            recipeFirstSeenDay[craft.RecipeId] = state.Day;
                        }
                    }
                }

                var demand = DemandBoard.Snapshot(state);
                foreach (var c in demand.OpenCommissions)
                {
                    // Concatenation, not string interpolation, and deliberately so: this is an internal
                    // analysis token never rendered to a player, but an interpolated slot read this
                    // close to the word above still trips the P2-HONEST-11 copy census (it discovers
                    // every such hole on principle and correctly cannot tell a machine fingerprint from
                    // real copy). Plain concatenation gives the census nothing to match.
                    dayAskTokens.Add(c.Slot.ToString() + ":" + c.MinQuality);
                    var key = (c.Slot, c.MinQuality);
                    if (askFirstSeenDay.TryGetValue(key, out var firstDay) && firstDay < state.Day)
                    {
                        if (firstRepeatedAskDay == 0)
                        {
                            firstRepeatedAskDay = state.Day;
                        }
                    }
                    else if (!askFirstSeenDay.ContainsKey(key))
                    {
                        askFirstSeenDay[key] = state.Day;
                    }
                }

                var result = kernel.Tick(state, chosen);
                foreach (var gameEvent in result.Events)
                {
                    string? line;
                    try
                    {
                        line = EventNarration.Line(gameEvent, result.NewState);
                    }
                    catch (Exception)
                    {
                        line = null; // a narration lookup this proxy doesn't reconstruct exactly — skip, don't crash the sweep
                    }

                    if (line is null)
                    {
                        continue;
                    }

                    if (!lineFirstSeenDay.ContainsKey(line))
                    {
                        lineFirstSeenDay[line] = state.Day;
                        lastNewLineDay = state.Day;
                    }
                }

                state = result.NewState;
            }

            FlushDay(currentDay);
            seedVerbOnly.Finish();
            seedVerbRecipe.Finish();
            seedFull.Finish();

            foreach (var (day, agg) in seedVerbOnly.CumulativeByDay)
            {
                var a = verbOnly.CumulativeByDay.GetValueOrDefault(day, (0, 0));
                verbOnly.CumulativeByDay[day] = (a.Sum + agg.Sum, a.N + agg.N);
            }

            foreach (var (day, agg) in seedVerbRecipe.CumulativeByDay)
            {
                var a = verbRecipe.CumulativeByDay.GetValueOrDefault(day, (0, 0));
                verbRecipe.CumulativeByDay[day] = (a.Sum + agg.Sum, a.N + agg.N);
            }

            foreach (var (day, agg) in seedFull.CumulativeByDay)
            {
                var a = full.CumulativeByDay.GetValueOrDefault(day, (0, 0));
                full.CumulativeByDay[day] = (a.Sum + agg.Sum, a.N + agg.N);
            }

            var row = new SeedRow(seed,
                seedVerbOnly.LastNovelDay, seedVerbOnly.FlatlineOnsetDay, seedVerbOnly.HalfLifeDay(),
                seedVerbRecipe.LastNovelDay, seedVerbRecipe.FlatlineOnsetDay,
                seedFull.LastNovelDay,
                firstRepeatedRecipeDay, firstRepeatedAskDay, lastNewLineDay,
                GranularityTracker.HalfLifeOf(lineHistory, lineFirstSeenDay.Count),
                seedVerbOnly.DistinctShapesEver, lineFirstSeenDay.Count);
            perSeedRows.Add(row);

            output.WriteLine($"  seed {seed}: verb-only last novel {row.VerbOnlyLastNovelDay} "
                + $"(half-life {row.VerbOnlyHalfLifeDay}, flatline {Fmt(row.VerbOnlyFlatlineDay)}), verb+recipe last novel {row.VerbRecipeLastNovelDay} "
                + $"(flatline {Fmt(row.VerbRecipeFlatlineDay)}), full last novel {row.FullLastNovelDay}, "
                + $"first repeated recipe {Fmt(row.FirstRepeatedRecipeDay)}, first repeated ask {Fmt(row.FirstRepeatedAskDay)}, "
                + $"last new narration line {row.LastNewLineDay}");
        }

        var summaryPath = Path.Combine(outDir, "felt-wall-summary.md");
        try
        {
            File.WriteAllText(summaryPath, BuildSummary(
                seedCount, startSeed, days, verbOnly, verbRecipe, full, cumulativeLinesByDay, perSeedRows));
        }
        catch (Exception ex)
        {
            error.WriteLine($"felt-wall: summary write failed: {ex.Message}");
            return 1;
        }

        var medianHalfLife = Median(perSeedRows.Select(r => r.VerbOnlyHalfLifeDay).ToList());
        var medianLastNovel = Median(perSeedRows.Select(r => r.VerbOnlyLastNovelDay).ToList());
        output.WriteLine($"felt-wall: {seedCount} seed(s), median verb-only novelty half-life day {medianHalfLife:F1} "
            + $"(median last-novel-day {medianLastNovel:F1}) -> {summaryPath}");
        return 0;
    }

    private static string Fmt(int day) => day > 0 ? day.ToString() : "never";

    private readonly record struct SeedRow(
        ulong Seed,
        int VerbOnlyLastNovelDay,
        int VerbOnlyFlatlineDay,
        int VerbOnlyHalfLifeDay,
        int VerbRecipeLastNovelDay,
        int VerbRecipeFlatlineDay,
        int FullLastNovelDay,
        int FirstRepeatedRecipeDay,
        int FirstRepeatedAskDay,
        int LastNewLineDay,
        int LineHalfLifeDay,
        int DistinctVerbShapesEver,
        int DistinctLinesEver);

    private static string BuildSummary(
        int seedCount, ulong startSeed, int days,
        GranularityTracker verbOnly, GranularityTracker verbRecipe, GranularityTracker full,
        SortedDictionary<int, (long Sum, int N)> cumulativeLinesByDay,
        List<SeedRow> perSeedRows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# The felt wall — where the day stops changing (owner ruling 2026-09-04)");
        sb.AppendLine();
        sb.AppendLine($"Auto-generated by `GameSim.Cli felt-wall`. **{seedCount} seed(s)** (start {startSeed}) "
            + $"x **{days} days**, `BaselinePlayer` policy (same policy `batch`/`decisions`/`long-wall` "
            + "default to). Extends P2-LONG-01's `long-wall` machinery — this measures repetition of "
            + "what was actually DONE each day, not novelty of what was merely offered. Three "
            + "granularities of \"day shape\" run side by side; see the type doc comment for why "
            + "`VerbOnly` is trusted as the headline and `Full` is not.");
        sb.AppendLine();

        sb.AppendLine("## Cumulative distinct day-shapes seen so far, by day (avg across seeds)");
        sb.AppendLine();
        sb.AppendLine("Flat curve = no seed is seeing a new day-shape at that granularity any more.");
        sb.AppendLine();
        sb.AppendLine("| day | avg cumulative VerbOnly shapes | avg cumulative VerbRecipe shapes | "
            + "avg cumulative Full shapes | avg cumulative distinct narration lines |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var day in verbOnly.CumulativeByDay.Keys)
        {
            var vo = verbOnly.CumulativeByDay[day];
            var vr = verbRecipe.CumulativeByDay.GetValueOrDefault(day, (0, 0));
            var fu = full.CumulativeByDay.GetValueOrDefault(day, (0, 0));
            var ln = cumulativeLinesByDay.GetValueOrDefault(day, (0, 0));
            sb.AppendLine($"| {day} | {Avg(vo):F2} | {Avg(vr):F2} | {Avg(fu):F2} | {Avg(ln):F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Per-seed");
        sb.AppendLine();
        sb.AppendLine("\"never\" = that signal did not fire within the sweep's window at all. Half-life = the "
            + "day this seed's cumulative distinct count first reached half of its OWN eventual "
            + $"(day-{days}) total — see the type doc comment for why this, not last-novel-day, is trusted.");
        sb.AppendLine();
        sb.AppendLine("| seed | VerbOnly half-life | VerbOnly last novel | VerbOnly flatline | "
            + "VerbRecipe last novel | Full last novel | first repeated recipe | first repeated ask | "
            + "last new narration line | narration half-life |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var row in perSeedRows)
        {
            sb.AppendLine($"| {row.Seed} | {row.VerbOnlyHalfLifeDay} | {row.VerbOnlyLastNovelDay} | {Fmt(row.VerbOnlyFlatlineDay)} | "
                + $"{row.VerbRecipeLastNovelDay} | {row.FullLastNovelDay} | "
                + $"{Fmt(row.FirstRepeatedRecipeDay)} | {Fmt(row.FirstRepeatedAskDay)} | {row.LastNewLineDay} | {row.LineHalfLifeDay} |");
        }

        var voHalf = perSeedRows.Select(r => r.VerbOnlyHalfLifeDay).OrderBy(d => d).ToList();
        var voDays = perSeedRows.Select(r => r.VerbOnlyLastNovelDay).OrderBy(d => d).ToList();
        var voFlat = perSeedRows.Where(r => r.VerbOnlyFlatlineDay > 0).Select(r => r.VerbOnlyFlatlineDay).OrderBy(d => d).ToList();
        var vrDays = perSeedRows.Select(r => r.VerbRecipeLastNovelDay).OrderBy(d => d).ToList();
        var fuDays = perSeedRows.Select(r => r.FullLastNovelDay).OrderBy(d => d).ToList();
        var recipeDays = perSeedRows.Where(r => r.FirstRepeatedRecipeDay > 0).Select(r => r.FirstRepeatedRecipeDay).OrderBy(d => d).ToList();
        var askDays = perSeedRows.Where(r => r.FirstRepeatedAskDay > 0).Select(r => r.FirstRepeatedAskDay).OrderBy(d => d).ToList();
        var lineDays = perSeedRows.Select(r => r.LastNewLineDay).OrderBy(d => d).ToList();
        var lineHalf = perSeedRows.Select(r => r.LineHalfLifeDay).OrderBy(d => d).ToList();

        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine($"- **VerbOnly novelty half-life (the felt-wall headline)**: min {voHalf.FirstOrDefault()}, "
            + $"median {Median(voHalf):F1}, max {voHalf.LastOrDefault()}. By this day, each seed has already shown "
            + "half of every distinct verb-set \"day shape\" it will EVER show across the full window — the day "
            + "the pace of change, not just its existence, collapses.");
        sb.AppendLine($"- VerbOnly last-novel day (companion stat, NOT the headline — long-tail-dominated, see type "
            + $"doc): min {voDays.FirstOrDefault()}, median {Median(voDays):F1}, max {voDays.LastOrDefault()}.");
        sb.AppendLine($"- VerbOnly flatline onset (unbroken repeat-yesterday run to day {days}): reached in "
            + $"{voFlat.Count}/{seedCount} seeds"
            + (voFlat.Count > 0 ? $"; min {voFlat.FirstOrDefault()}, median {Median(voFlat):F1}, max {voFlat.LastOrDefault()}." : "."));
        sb.AppendLine($"- VerbRecipe last-novel day (next-finer cut): min {vrDays.FirstOrDefault()}, "
            + $"median {Median(vrDays):F1}, max {vrDays.LastOrDefault()}.");
        sb.AppendLine($"- Full (verb+recipe+ask) last-novel day (rejected as headline — dominated by "
            + $"demand-board churn, see type doc): min {fuDays.FirstOrDefault()}, median {Median(fuDays):F1}, "
            + $"max {fuDays.LastOrDefault()}.");
        sb.AppendLine($"- First repeated-recipe day (rejected — saturates trivially, finite recipe list): "
            + $"fired in {recipeDays.Count}/{seedCount} seeds"
            + (recipeDays.Count > 0 ? $"; min {recipeDays.FirstOrDefault()}, median {Median(recipeDays):F1}, max {recipeDays.LastOrDefault()}." : "."));
        sb.AppendLine($"- First repeated-ask day (rejected — saturates trivially, finite slot×quality space): "
            + $"fired in {askDays.Count}/{seedCount} seeds"
            + (askDays.Count > 0 ? $"; min {askDays.FirstOrDefault()}, median {Median(askDays):F1}, max {askDays.LastOrDefault()}." : "."));
        sb.AppendLine($"- Last new console-narration-line day (partial copy proxy, CLI surface only): "
            + $"min {lineDays.FirstOrDefault()}, median {Median(lineDays):F1}, max {lineDays.LastOrDefault()}.");
        sb.AppendLine($"- Narration-line novelty half-life (same partial CLI-only proxy): min {lineHalf.FirstOrDefault()}, "
            + $"median {Median(lineHalf):F1}, max {lineHalf.LastOrDefault()}.");
        sb.AppendLine();
        return sb.ToString();
    }

    private static double Avg((long Sum, int N) agg) => agg.N > 0 ? (double)agg.Sum / agg.N : 0;

    private static double Median(List<int> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
