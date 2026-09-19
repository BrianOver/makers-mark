using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Cli;

/// <summary>
/// P2-HONEST-32 measurement sweep (docs/design/MAKERS-MARK.md §11.13) — NOT a gate, one-off like
/// <see cref="SlotSpendSweep"/>/<see cref="FeltWallSweep"/>, whose machinery
/// (<see cref="GameComposition"/>, <see cref="BatchRunner"/>'s policy axis) this reuses.
///
/// <para><b>What it answers.</b> <c>godot/scripts/panels/MineWatch.cs</c> reads
/// <see cref="DenThreatShifted"/> in four places (a flash callout, a monster-silhouette pick, and two
/// narration lines) but no 2,000-sim-day run ever emits one for the Mine, because
/// <see cref="DirectorSystem.TickDens"/>'s fixed daily increment (18‰) is dwarfed by its per-clear
/// relief (30‰) at the roster's median clear rate — a wired screen with a permanently-silent event
/// behind it, link 4's "show only what the sim decided" with nothing to show.</para>
///
/// <para><b>Two passes.</b> <see cref="Run"/> first measures the Mine's den meter under the CURRENT
/// constants (the census this unit's prompt calls for), then — once <c>DirectorSystem</c>'s constants
/// are retuned in the same PR — measures it again under the retuned constants, so the PR body carries
/// both tables and the retune's effect is provable from the sweep itself, not asserted.</para>
/// </summary>
public static class DenSweep
{
    public static int Run(int seedCount, ulong startSeed, int days, string outDir, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"den-sweep: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();
        var rows = new List<SeedRow>();
        var dayRows = new List<string> { "policy,seed,day,clears,surge" };
        var clearsHistogram = new SortedDictionary<int, int>();

        foreach (var policyName in new[] { "baseline", "forgecounter" })
        {
            var policy = policyName == "baseline" ? BatchRunner.Policy.Baseline : BatchRunner.Policy.ForgeCounter;
            var policyFn = BatchRunner.PolicyFn(policy, CraftHand.Average);
            var startingProfession = BatchRunner.PolicyStartingProfession(policy);

            for (var i = 0; i < seedCount; i++)
            {
                var seed = startSeed + (ulong)i;
                var row = MeasureSeed(kernel, policyFn, startingProfession, seed, days, policyName, clearsHistogram, dayRows);
                rows.Add(row);
                output.WriteLine($"  {policyName} seed {seed}: max {row.MaxMeter}‰, days>=tier1 {row.DaysTier1}, "
                    + $"days>=tier2 {row.DaysTier2}, days>=tier3 {row.DaysTier3}, median true clears/day {row.MedianClearsPerDay:F1}");
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "den-sweep-days.csv"), dayRows);
        var summaryPath = Path.Combine(outDir, "den-sweep-summary.md");
        File.WriteAllText(summaryPath, BuildSummary(seedCount, startSeed, days, rows, clearsHistogram));

        var allDaysTier1 = rows.Count(r => r.DaysTier1 > 0);
        var allDaysTier3 = rows.Count(r => r.DaysTier3 > 0);
        var histTotal = clearsHistogram.Values.Sum();
        var histLine = histTotal == 0
            ? "(no samples)"
            : string.Join(", ", clearsHistogram.Select(kv =>
                $"{(kv.Key == 5 ? "5+" : kv.Key.ToString())} clears: {kv.Value} days ({(double)kv.Value / histTotal:P1})"));
        output.WriteLine($"den-sweep: {rows.Count} seed-runs, {allDaysTier1} reached tier1 at least once, "
            + $"{allDaysTier3} reached tier3 at least once -> {summaryPath}");
        output.WriteLine($"den-sweep: clears/day histogram (pooled, {histTotal} day-samples): {histLine}");
        return 0;
    }

    private static SeedRow MeasureSeed(
        GameKernel kernel,
        Func<GameState, ImmutableList<PlayerAction>> driverFn,
        string? startingProfession,
        ulong seed,
        int days,
        string policyName,
        SortedDictionary<int, int> clearsHistogram,
        List<string> dayRows)
    {
        var state = startingProfession is null
            ? GameComposition.NewCampaign(seed)
            : GameComposition.NewCampaign(seed, startingProfession);

        var currentDay = state.Day;
        var clearsToday = 0;
        var clearsPerDay = new List<int>();
        var maxMeter = 0;
        var daysTier1 = 0;
        var daysTier2 = 0;
        var daysTier3 = 0;
        var surgeToday = 0;

        void FlushDay()
        {
            // The day flipped: LastNightExpeditions now holds the night just revealed — the exact
            // slice DirectorSystem.TickDens is about to relieve against.
            clearsToday = DirectorSystem.ClearsLastNight(state, VenueRegistry.MineId);
            clearsPerDay.Add(clearsToday);
            dayRows.Add($"{policyName},{seed},{currentDay},{clearsToday},{surgeToday}");
            surgeToday = 0;
            var bucket = Math.Min(clearsToday, 5);
            clearsHistogram[bucket] = clearsHistogram.GetValueOrDefault(bucket) + 1;
            clearsToday = 0;

            var meter = state.Venues.TryGetValue(VenueRegistry.MineId, out var mine) ? mine.InfectionPerMille : 0;
            maxMeter = Math.Max(maxMeter, meter);
            var tier = DirectorSystem.DenTier(meter);
            if (tier >= 1)
            {
                daysTier1++;
            }

            if (tier >= 2)
            {
                daysTier2++;
            }

            if (tier >= 3)
            {
                daysTier3++;
            }
        }

        while (state.Day <= days)
        {
            if (state.Day != currentDay)
            {
                FlushDay();
                currentDay = state.Day;
            }

            var chosen = driverFn(state);
            var result = kernel.Tick(state, chosen);
            surgeToday += result.Events.Count(e => e is IncidentFired { VenueId: VenueRegistry.MineId });
            state = result.NewState;
        }

        FlushDay();

        var sortedClears = clearsPerDay.OrderBy(c => c).ToList();
        var medianClears = sortedClears.Count == 0 ? 0.0 : sortedClears[sortedClears.Count / 2];

        return new SeedRow(policyName, seed, maxMeter, daysTier1, daysTier2, daysTier3, medianClears);
    }

    private readonly record struct SeedRow(
        string Policy, ulong Seed, int MaxMeter, int DaysTier1, int DaysTier2, int DaysTier3, double MedianClearsPerDay);

    private static string BuildSummary(
        int seedCount, ulong startSeed, int days, List<SeedRow> rows, SortedDictionary<int, int> clearsHistogram)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# P2-HONEST-32 den-threat reachability sweep");
        sb.AppendLine();
        sb.AppendLine($"`{seedCount}` seed(s) per policy (start {startSeed}) x `{days}` days, "
            + "`baseline` + `forgecounter` policies. Measures the Mine's den meter "
            + "(`GameState.Venues[\"mine\"].InfectionPerMille`) as `DirectorSystem.TickDens` actually "
            + "advances it inside the kernel — not a replay, the real tick.");
        sb.AppendLine();
        sb.AppendLine("| policy | seed | max meter (‰) | days >= tier1 (250‰) | days >= tier2 (500‰) | "
            + "days >= tier3 (750‰) | median true clears/day |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {row.Policy} | {row.Seed} | {row.MaxMeter} | {row.DaysTier1} | {row.DaysTier2} | "
                + $"{row.DaysTier3} | {row.MedianClearsPerDay:F1} |");
        }

        sb.AppendLine();
        var tier1Seeds = rows.Count(r => r.DaysTier1 > 0);
        var tier3Seeds = rows.Count(r => r.DaysTier3 > 0);
        sb.AppendLine($"- Seeds reaching tier1 at least once: {tier1Seeds}/{rows.Count}");
        sb.AppendLine($"- Seeds reaching tier3 at least once: {tier3Seeds}/{rows.Count}");
        sb.AppendLine($"- Max meter ever observed: {rows.Select(r => r.MaxMeter).DefaultIfEmpty(0).Max()}‰");
        return sb.ToString();
    }
}
