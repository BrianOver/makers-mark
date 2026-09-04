using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// P2-LONG-01 one-off measurement sweep. This is NOT permanent telemetry and gates nothing — it
/// re-dates the "day-11 wall" (the day the player's question stops changing) on the CURRENT
/// build, using the same machinery <c>batch</c>/<c>decisions</c> already exercise
/// (<see cref="GameComposition"/> + <see cref="BaselinePlayer"/> + <see cref="ActionLegality"/> +
/// <see cref="DemandBoard"/>), and adds one extra column the wave's own opening exhibit needs:
/// the Emberfall floor-5 (the campaign's climax boss, <see cref="ArcState.ClimaxDay"/>) clear
/// rate, so an unreachable ending is caught before the expensive wave is built on top of it
/// instead of at that wave's last unit.
///
/// Two "what's new today" signals, tracked per seed with their first-occurrence day:
/// <list type="bullet">
///   <item>legal-verb variety — the distinct <see cref="PlayerAction"/> verbs
///   <see cref="ActionLegality.LegalActions"/> offers that day, across every phase;</item>
///   <item>demand-slot variety — the distinct <see cref="ItemSlot"/> values the open commission
///   board (<see cref="DemandBoard.Snapshot"/>) is asking for that day.</item>
/// </list>
/// A seed's own wall is the LATER of its last genuinely-new verb day and its last genuinely-new
/// slot day — the day after which neither signal ever introduces anything the seed has not
/// already seen.
/// </summary>
public static class LongWallSweep
{
    private static string Verb(PlayerAction action) => action.GetType().Name.Replace("Action", string.Empty);

    public static int Run(int seedCount, ulong startSeed, int days, string outDir, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex)
        {
            error.WriteLine($"long-wall: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();

        // day -> (sum of that-day distinct-count across seeds, seed count contributing)
        var legalVarietyByDay = new SortedDictionary<int, (long Sum, int N)>();
        var slotVarietyByDay = new SortedDictionary<int, (long Sum, int N)>();

        // verb/slot -> list of first-occurrence day, one entry per seed that ever saw it.
        var verbFirstDays = new Dictionary<string, List<int>>();
        var slotFirstDays = new Dictionary<ItemSlot, List<int>>();

        var perSeedRows = new List<(ulong Seed, int LastNewVerbDay, int LastNewSlotDay, int ClimaxDay)>();

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);

            var firstSeenVerbDay = new Dictionary<string, int>();
            var firstSeenSlotDay = new Dictionary<ItemSlot, int>();
            var dayLegalVerbs = new HashSet<string>();
            var daySlots = new HashSet<ItemSlot>();
            var currentDay = state.Day;

            void FlushDay(int day)
            {
                var lv = legalVarietyByDay.GetValueOrDefault(day, (0, 0));
                legalVarietyByDay[day] = (lv.Sum + dayLegalVerbs.Count, lv.N + 1);
                var sv = slotVarietyByDay.GetValueOrDefault(day, (0, 0));
                slotVarietyByDay[day] = (sv.Sum + daySlots.Count, sv.N + 1);
                dayLegalVerbs.Clear();
                daySlots.Clear();
            }

            while (state.Day <= days)
            {
                if (state.Day != currentDay)
                {
                    FlushDay(currentDay);
                    currentDay = state.Day;
                }

                var phase = state.Phase;
                var legal = ActionLegality.LegalActions(state, phase);
                foreach (var a in legal)
                {
                    var v = Verb(a);
                    dayLegalVerbs.Add(v);
                    if (!firstSeenVerbDay.ContainsKey(v))
                    {
                        firstSeenVerbDay[v] = state.Day;
                    }
                }

                var demand = DemandBoard.Snapshot(state);
                foreach (var c in demand.OpenCommissions)
                {
                    daySlots.Add(c.Slot);
                    if (!firstSeenSlotDay.ContainsKey(c.Slot))
                    {
                        firstSeenSlotDay[c.Slot] = state.Day;
                    }
                }

                var chosen = BaselinePlayer.ActionsFor(state);
                state = kernel.Tick(state, chosen).NewState;
            }

            FlushDay(currentDay);

            foreach (var (v, d) in firstSeenVerbDay)
            {
                if (!verbFirstDays.TryGetValue(v, out var list))
                {
                    verbFirstDays[v] = list = new List<int>();
                }

                list.Add(d);
            }

            foreach (var (s, d) in firstSeenSlotDay)
            {
                if (!slotFirstDays.TryGetValue(s, out var list))
                {
                    slotFirstDays[s] = list = new List<int>();
                }

                list.Add(d);
            }

            var lastNewVerbDay = firstSeenVerbDay.Values.DefaultIfEmpty(0).Max();
            var lastNewSlotDay = firstSeenSlotDay.Values.DefaultIfEmpty(0).Max();
            perSeedRows.Add((seed, lastNewVerbDay, lastNewSlotDay, state.Arc.ClimaxDay));

            output.WriteLine($"  seed {seed}: last new verb day {lastNewVerbDay}, "
                + $"last new demand-slot day {lastNewSlotDay}, climax day {state.Arc.ClimaxDay}");
        }

        var summaryPath = Path.Combine(outDir, "long-wall-summary.md");
        try
        {
            File.WriteAllText(summaryPath, BuildSummary(
                seedCount, startSeed, days, legalVarietyByDay, slotVarietyByDay,
                verbFirstDays, slotFirstDays, perSeedRows));
        }
        catch (Exception ex)
        {
            error.WriteLine($"long-wall: summary write failed: {ex.Message}");
            return 1;
        }

        var climaxHits = perSeedRows.Count(r => r.ClimaxDay > 0);
        output.WriteLine($"long-wall: {seedCount} seed(s), Emberfall floor-5 clear rate "
            + $"{climaxHits}/{seedCount} -> {summaryPath}");
        return 0;
    }

    private static string BuildSummary(
        int seedCount, ulong startSeed, int days,
        SortedDictionary<int, (long Sum, int N)> legalVarietyByDay,
        SortedDictionary<int, (long Sum, int N)> slotVarietyByDay,
        Dictionary<string, List<int>> verbFirstDays,
        Dictionary<ItemSlot, List<int>> slotFirstDays,
        List<(ulong Seed, int LastNewVerbDay, int LastNewSlotDay, int ClimaxDay)> perSeedRows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# P2-LONG-01 — the day-11 wall, re-dated");
        sb.AppendLine();
        sb.AppendLine($"Auto-generated by `GameSim.Cli long-wall`. **{seedCount} seed(s)** "
            + $"(start {startSeed}) x **{days} days**, `BaselinePlayer` policy (same policy "
            + "`batch`/`decisions` default to).");
        sb.AppendLine();

        sb.AppendLine("## Legal-verb variety per day (avg distinct verbs offered that day, across seeds)");
        sb.AppendLine();
        sb.AppendLine("| day | avg legal-verb variety | avg demand-slot variety |");
        sb.AppendLine("|---|---|---|");
        foreach (var day in legalVarietyByDay.Keys)
        {
            var lv = legalVarietyByDay[day];
            var sv = slotVarietyByDay.GetValueOrDefault(day, (0, 0));
            var lvAvg = lv.N > 0 ? (double)lv.Sum / lv.N : 0;
            var svAvg = sv.N > 0 ? (double)sv.Sum / sv.N : 0;
            sb.AppendLine($"| {day} | {lvAvg:F2} | {svAvg:F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## First-occurrence day per legal verb (across seeds that ever saw it)");
        sb.AppendLine();
        sb.AppendLine("| verb | min first day | median first day | max first day | seeds seeing it |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var (verb, list) in verbFirstDays.OrderBy(kv => Median(kv.Value)).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"| {verb} | {list.Min()} | {Median(list):F1} | {list.Max()} | {list.Count}/{seedCount} |");
        }

        sb.AppendLine();
        sb.AppendLine("## First-occurrence day per demand slot (across seeds that ever saw it)");
        sb.AppendLine();
        sb.AppendLine("| slot | min first day | median first day | max first day | seeds seeing it |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var (slot, list) in slotFirstDays.OrderBy(kv => Median(kv.Value)))
        {
            sb.AppendLine($"| {slot} | {list.Min()} | {Median(list):F1} | {list.Max()} | {list.Count}/{seedCount} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Per-seed: last genuinely-new day, and the Emberfall floor-5 (climax) clear day");
        sb.AppendLine();
        sb.AppendLine("\"Last new verb/slot day\" = the day the LATEST first-occurrence landed for that seed — "
            + "after this day, nothing the sweep tracks ever introduces anything the seed has not already "
            + "seen. \"Climax day\" is `ArcState.ClimaxDay` (0 = Emberfall's floor 5 never fell in this "
            + $"seed's {days}-day run).");
        sb.AppendLine();
        sb.AppendLine("| seed | last new verb day | last new slot day | last new (either) day | climax day |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var row in perSeedRows)
        {
            var lastEither = Math.Max(row.LastNewVerbDay, row.LastNewSlotDay);
            sb.AppendLine($"| {row.Seed} | {row.LastNewVerbDay} | {row.LastNewSlotDay} | {lastEither} | "
                + $"{(row.ClimaxDay > 0 ? row.ClimaxDay.ToString() : "never")} |");
        }

        var climaxHits = perSeedRows.Count(r => r.ClimaxDay > 0);
        var lastNewDays = perSeedRows.Select(r => Math.Max(r.LastNewVerbDay, r.LastNewSlotDay)).OrderBy(d => d).ToList();

        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine($"- Emberfall floor-5 (climax) clear rate: **{climaxHits}/{seedCount}** "
            + $"({(seedCount > 0 ? 100.0 * climaxHits / seedCount : 0):F0}%) within {days} days.");
        sb.AppendLine($"- Last genuinely-new day across seeds: min {lastNewDays.FirstOrDefault()}, "
            + $"median {Median(lastNewDays):F1}, max {lastNewDays.LastOrDefault()}.");
        sb.AppendLine();
        return sb.ToString();
    }

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
