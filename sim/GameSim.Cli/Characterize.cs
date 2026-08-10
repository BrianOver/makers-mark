using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// The forward-ladder plan's L3 characterization harness (plan 2026-08-10-003, §11.8's fix):
/// measures the SAME signal the plan's gate rule reads — party power by day, first-floor-reached
/// days, gold, and (new) graduation days/power — under <see cref="BaselinePlayer"/> on the main
/// seed plus a sweep. A DATA TOOL, not a gate: it asserts nothing, prints raw tables, and exits 0.
/// Reused three times across L3 (pre-recipe, post-recipe, post-gate) to prove the gate values were
/// set from a measurement, not a guess (§11.6's "measure, don't guess" idiom).
///
/// <c>PartyPower</c> here is <see cref="CombatMath.EffectivePower"/> averaged over every ALIVE hero
/// at each day's Morning boundary — the same units venue <c>Gate</c> values are expressed in, so a
/// printed power number reads directly against a gate constant.
/// </summary>
public static class Characterize
{
    public const string Usage =
        "usage: characterize --seeds <s1,s2,...> [--days <days>] [--sample <everyNdays>]";

    public sealed record Args(ImmutableList<ulong> Seeds, int Days, int SampleEvery);

    public static Args? Parse(string[] args, TextWriter error)
    {
        ImmutableList<ulong>? seeds = null;
        var days = 100;
        var sampleEvery = 5;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seeds" when i + 1 < args.Length:
                    var parts = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var parsed = ImmutableList.CreateBuilder<ulong>();
                    foreach (var part in parts)
                    {
                        if (!ulong.TryParse(part, out var s))
                        {
                            error.WriteLine($"characterize: bad seed '{part}' in --seeds");
                            return null;
                        }

                        parsed.Add(s);
                    }

                    seeds = parsed.ToImmutable();
                    i++;
                    break;
                case "--days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var d):
                    days = d;
                    i++;
                    break;
                case "--sample" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n):
                    sampleEvery = n;
                    i++;
                    break;
                default:
                    error.WriteLine($"characterize: unknown or malformed arg '{args[i]}'");
                    error.WriteLine(Usage);
                    return null;
            }
        }

        if (seeds is null || seeds.IsEmpty || days <= 0 || sampleEvery <= 0)
        {
            error.WriteLine("characterize: --seeds is required; --days/--sample must be positive");
            error.WriteLine(Usage);
            return null;
        }

        return new Args(seeds, days, sampleEvery);
    }

    private sealed record DaySample(int Day, int AvgPower, int MaxDeepestFloor, int Gold, int Alive);

    private sealed record FirstEvents(
        int FirstFloor3Day,
        int FirstFloor4Day,
        int FirstFloor5Day,
        int FirstGrad1Day,
        int Grad1PartyPower,
        int Grad1PartySize,
        int FirstGrad2Day,
        int Grad2PartyPower,
        int Grad2PartySize);

    private sealed record SeedResult(
        ulong Seed,
        ImmutableList<DaySample> Samples,
        FirstEvents Events,
        int AliveAtEnd,
        int MaxDeepestAtEnd,
        CampaignAct FinalAct,
        int ActIIStartDay,
        int ActIIIStartDay,
        int EndingDay);

    public static int Run(Args a, TextWriter output, TextWriter error)
    {
        var results = ImmutableList.CreateBuilder<SeedResult>();
        foreach (var seed in a.Seeds)
        {
            results.Add(RunOne(seed, a.Days, a.SampleEvery));
        }

        foreach (var r in results)
        {
            output.WriteLine($"=== seed {r.Seed} ===");
            output.WriteLine("day  avgPower  maxDeepestFloor  gold  alive");
            foreach (var s in r.Samples)
            {
                output.WriteLine($"{s.Day,3}  {s.AvgPower,8}  {s.MaxDeepestFloor,15}  {s.Gold,4}  {s.Alive,5}");
            }

            var e = r.Events;
            output.WriteLine($"first floor-3 day: {Fmt(e.FirstFloor3Day)}");
            output.WriteLine($"first floor-4 day: {Fmt(e.FirstFloor4Day)}");
            output.WriteLine($"first floor-5 day: {Fmt(e.FirstFloor5Day)}");
            output.WriteLine($"first graduation (rank0->1) day: {Fmt(e.FirstGrad1Day)}"
                + (e.FirstGrad1Day > 0 ? $" (party power {e.Grad1PartyPower}, {e.Grad1PartySize} graduate(s))" : string.Empty));
            output.WriteLine($"first Gloomwood boss (rank1->2) day: {Fmt(e.FirstGrad2Day)}"
                + (e.FirstGrad2Day > 0 ? $" (party power {e.Grad2PartyPower}, {e.Grad2PartySize} graduate(s))" : string.Empty));
            output.WriteLine($"alive at end: {r.AliveAtEnd}, max deepest floor at end: {r.MaxDeepestAtEnd}");
            output.WriteLine($"arc: {r.FinalAct} (ActII day {Fmt(r.ActIIStartDay)}, ActIII day {Fmt(r.ActIIIStartDay)}, Ending day {Fmt(r.EndingDay)})");
            output.WriteLine(string.Empty);
        }

        return 0;
    }

    private static string Fmt(int day) => day > 0 ? day.ToString() : "never";

    private static SeedResult RunOne(ulong seed, int days, int sampleEvery)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        var samples = ImmutableList.CreateBuilder<DaySample>();

        var firstFloor3 = -1;
        var firstFloor4 = -1;
        var firstFloor5 = -1;
        var firstGrad1Day = -1;
        var grad1Power = -1;
        var grad1Size = -1;
        var firstGrad2Day = -1;
        var grad2Power = -1;
        var grad2Size = -1;

        for (var tick = 0; tick < days * 5; tick++) // 5-phase day (staged resolution)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;

            foreach (var gameEvent in result.Events)
            {
                // Independent trackers (not mutually exclusive — a floor-5 record also satisfies
                // >=3 and >=4), so these are separate ifs, never a switch/pattern chain that would
                // only ever fire the first (>=3) arm.
                if (gameEvent is FloorRecordSet { Floor: >= 3 } && firstFloor3 < 0)
                {
                    firstFloor3 = state.Day;
                }

                if (gameEvent is FloorRecordSet { Floor: >= 4 } && firstFloor4 < 0)
                {
                    firstFloor4 = state.Day;
                }

                if (gameEvent is FloorRecordSet { Floor: >= 5 } && firstFloor5 < 0)
                {
                    firstFloor5 = state.Day;
                }

                if (gameEvent is VenueGraduated grad)
                {
                    if (grad.NewRank == 1 && firstGrad1Day < 0)
                    {
                        firstGrad1Day = state.Day;
                        (grad1Power, grad1Size) = PartyPowerOf(state, grad.Graduates);
                    }
                    else if (grad.NewRank == 2 && firstGrad2Day < 0)
                    {
                        firstGrad2Day = state.Day;
                        (grad2Power, grad2Size) = PartyPowerOf(state, grad.Graduates);
                    }
                }
            }

            if (state.Phase == DayPhase.Morning && (state.Day % sampleEvery == 0 || state.Day == 1))
            {
                samples.Add(SampleOf(state));
            }
        }

        var finalHeroes = state.Heroes.Values.ToList();
        var finalAlive = finalHeroes.Count(h => h.Alive);
        var finalDeepest = finalHeroes.Count == 0 ? 0 : finalHeroes.Max(h => h.DeepestFloorReached);

        return new SeedResult(
            seed,
            samples.ToImmutable(),
            new FirstEvents(firstFloor3, firstFloor4, firstFloor5, firstGrad1Day, grad1Power, grad1Size, firstGrad2Day, grad2Power, grad2Size),
            finalAlive,
            finalDeepest,
            state.Arc.Act,
            state.Arc.ActIIStartDay,
            state.Arc.ActIIIStartDay,
            state.Arc.EndingDay);
    }

    private static DaySample SampleOf(GameState state)
    {
        var allHeroes = state.Heroes.Values.ToList();
        var alive = allHeroes.Where(h => h.Alive).ToList();
        var avgPower = alive.Count == 0 ? 0 : alive.Sum(h => CombatMath.EffectivePower(h, state.Items)) / alive.Count;
        var maxDeepest = allHeroes.Count == 0 ? 0 : allHeroes.Max(h => h.DeepestFloorReached);
        return new DaySample(state.Day, avgPower, maxDeepest, state.Player.Gold, alive.Count);
    }

    private static (int Power, int Size) PartyPowerOf(GameState state, ImmutableList<HeroId> graduates)
    {
        var heroes = graduates
            .Where(h => state.Heroes.ContainsKey(h.Value))
            .Select(h => state.Heroes[h.Value])
            .ToList();
        var power = heroes.Count == 0 ? 0 : heroes.Sum(h => CombatMath.EffectivePower(h, state.Items)) / heroes.Count;
        return (power, heroes.Count);
    }
}
