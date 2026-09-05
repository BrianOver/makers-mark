using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// Late-campaign economy trajectory measurement (docs/design/MAKERS-MARK.md — anomaly-coverage
/// task, 2026-09-04): `tools/Analytics`'s six detectors can only see an inflation runaway
/// (<c>gold-mint-spike</c>) or a shop that never sold at all (<c>dead-shop</c> over the WHOLE run);
/// nothing watches for a shop that sold fine for 60 days and then died. Before adding a collapse
/// detector, this measures whether that shape is real in <see cref="BaselinePlayer"/>'s own
/// balance-gate corpus, and if so, from which day.
///
/// Samples <see cref="PlayerState.Gold"/>, total materials held, and shelf size directly from
/// <see cref="GameState"/> at each Morning boundary (ground truth — an exported chronicle carries
/// only the FINAL state plus an event log, so a stock-level trajectory needs a fresh re-run, the
/// same reason <see cref="Characterize"/> re-runs rather than reads chronicles). Flow metrics
/// (crafted/sold/bought) are tallied from that same tick's events, windowed between samples.
///
/// A DATA TOOL (Characterize/LongWallSweep precedent): asserts nothing, prints raw tables, exits 0.
/// </summary>
public static class EconTrajectory
{
    public const string Usage =
        "usage: econ-trajectory --seeds <count> [--seed <startSeed>] [--days <days>] [--sample <everyNdays>]";

    public sealed record Args(int SeedCount, ulong StartSeed, int Days, int SampleEvery);

    public static Args? Parse(string[] args, TextWriter error)
    {
        var seedCount = 20;
        var startSeed = 1UL;
        var days = 100;
        var sampleEvery = 5;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seeds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n):
                    seedCount = n;
                    i++;
                    break;
                case "--seed" when i + 1 < args.Length && ulong.TryParse(args[i + 1], out var s):
                    startSeed = s;
                    i++;
                    break;
                case "--days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var d):
                    days = d;
                    i++;
                    break;
                case "--sample" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n2):
                    sampleEvery = n2;
                    i++;
                    break;
                default:
                    error.WriteLine($"econ-trajectory: unknown or malformed arg '{args[i]}'");
                    error.WriteLine(Usage);
                    return null;
            }
        }

        if (seedCount <= 0 || days <= 0 || sampleEvery <= 0)
        {
            error.WriteLine("econ-trajectory: --seeds/--days/--sample must be positive");
            error.WriteLine(Usage);
            return null;
        }

        return new Args(seedCount, startSeed, days, sampleEvery);
    }

    /// <summary>One sampled Morning boundary: stock levels AT that day, flow counts WINDOWED since
    /// the previous sample (so a 5-day sample window reports "sold 3 this window", not a
    /// cumulative total that would need a second subtraction to read).</summary>
    private sealed record DaySample(
        int Day, int Gold, int MaterialsHeld, int ShelfCount,
        int Crafted, int PlayerSales, int CounterSales, int RivalSales, int MatBoughtQty,
        int CumCommissionsPosted, int CumCommissionsFulfilled, int CumCommissionsExpired);

    private sealed record SeedResult(ulong Seed, int EndingDay, int ClimaxDay, ImmutableList<DaySample> Samples);

    public static int Run(Args a, TextWriter output, TextWriter error)
    {
        var results = ImmutableList.CreateBuilder<SeedResult>();
        for (var i = 0; i < a.SeedCount; i++)
        {
            results.Add(RunOne(a.StartSeed + (ulong)i, a.Days, a.SampleEvery));
        }

        foreach (var r in results)
        {
            output.WriteLine($"=== seed {r.Seed} (ending day {Fmt(r.EndingDay)}, climax day {Fmt(r.ClimaxDay)}) ===");
            output.WriteLine("day  gold  matHeld  shelf  crafted  pSales  cSales  rSales  matBuy  commP  commF  commX");
            foreach (var s in r.Samples)
            {
                output.WriteLine(
                    $"{s.Day,3}  {s.Gold,5}  {s.MaterialsHeld,7}  {s.ShelfCount,5}  {s.Crafted,7}  "
                    + $"{s.PlayerSales,6}  {s.CounterSales,6}  {s.RivalSales,6}  {s.MatBoughtQty,6}  "
                    + $"{s.CumCommissionsPosted,5}  {s.CumCommissionsFulfilled,5}  {s.CumCommissionsExpired,5}");
            }

            output.WriteLine(string.Empty);
        }

        // Pooled view: mean of stock metrics, sum of flow metrics, counted ONLY over seeds still
        // IN-CAMPAIGN at that sample day (EndingDay == 0 means never ended within the horizon = always
        // counted; otherwise a day past EndingDay is the afterlife — Anomalies.PlayableHorizon's own
        // rationale, measured true on a real sweep: 29 of 30 prior anomaly hits described a town
        // nobody plays).
        output.WriteLine("=== pooled (mean stock / windowed-sum flow, seeds still in-campaign only) ===");
        output.WriteLine("day  active  avgGold  avgMatHeld  avgShelf  winCrafted  winPSales  winCSales  winMatBuy  cumCommF  cumCommX");
        var allDays = results.SelectMany(r => r.Samples.Select(s => s.Day)).Distinct().OrderBy(d => d).ToList();
        foreach (var day in allDays)
        {
            var atDay = results
                .Where(r => r.EndingDay == 0 || day <= r.EndingDay)
                .Select(r => r.Samples.FirstOrDefault(s => s.Day == day))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
            if (atDay.Count == 0)
            {
                continue;
            }

            output.WriteLine(
                $"{day,3}  {atDay.Count,6}  {atDay.Average(s => s.Gold),8:F0}  {atDay.Average(s => s.MaterialsHeld),10:F1}  "
                + $"{atDay.Average(s => s.ShelfCount),8:F1}  {atDay.Sum(s => s.Crafted),10}  {atDay.Sum(s => s.PlayerSales),9}  "
                + $"{atDay.Sum(s => s.CounterSales),9}  {atDay.Sum(s => s.MatBoughtQty),9}  "
                + $"{atDay.Sum(s => s.CumCommissionsFulfilled),8}  {atDay.Sum(s => s.CumCommissionsExpired),8}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("ending days (0 = never within horizon): " + string.Join(", ", results.Select(r => Fmt(r.EndingDay))));

        return 0;
    }

    private static string Fmt(int day) => day > 0 ? day.ToString() : "never";

    private static SeedResult RunOne(ulong seed, int days, int sampleEvery)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        var samples = ImmutableList.CreateBuilder<DaySample>();

        var craftedWindow = 0;
        var playerSalesWindow = 0;
        var counterSalesWindow = 0;
        var rivalSalesWindow = 0;
        var matBoughtQtyWindow = 0;
        var cumCommP = 0;
        var cumCommF = 0;
        var cumCommX = 0;

        for (var tick = 0; tick < days * 5; tick++) // 5-phase day (staged resolution)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;

            foreach (var gameEvent in result.Events)
            {
                switch (gameEvent)
                {
                    case ItemCrafted:
                        craftedWindow++;
                        break;
                    case ItemSold { FromPlayerShop: true }:
                        playerSalesWindow++;
                        break;
                    case ItemSold:
                        rivalSalesWindow++;
                        break;
                    case CounterSaleClosed:
                        counterSalesWindow++;
                        break;
                    case MaterialPurchased mp:
                        matBoughtQtyWindow += mp.Quantity;
                        break;
                    case CommissionPosted:
                        cumCommP++;
                        break;
                    case CommissionFulfilled:
                        cumCommF++;
                        break;
                    case CommissionExpired:
                        cumCommX++;
                        break;
                }
            }

            if (state.Phase == DayPhase.Morning && (state.Day % sampleEvery == 0 || state.Day == 1))
            {
                samples.Add(new DaySample(
                    state.Day, state.Player.Gold, state.Player.Materials.Values.Sum(), state.Player.Shelf.Count,
                    craftedWindow, playerSalesWindow, counterSalesWindow, rivalSalesWindow, matBoughtQtyWindow,
                    cumCommP, cumCommF, cumCommX));
                craftedWindow = 0;
                playerSalesWindow = 0;
                counterSalesWindow = 0;
                rivalSalesWindow = 0;
                matBoughtQtyWindow = 0;
            }
        }

        return new SeedResult(seed, state.Arc.EndingDay, state.Arc.ClimaxDay, samples.ToImmutable());
    }
}
