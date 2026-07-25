using System.Collections.Immutable;
using GameSim;
using GameSim.Chronicle;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// The non-human telemetry farm (observability plan U2, R4): seed-sweep simulations under the
/// shared <see cref="BaselinePlayer"/> policy, one chronicle JSON per seed, no interaction.
/// This is a DATA FARM, not a gate — it asserts nothing; `tools/Analytics` judges the output.
/// File IO lives here at the edge (KTD2: the sim itself stays pure). Output filenames are
/// deterministic (seed + days, no wall clock) so a re-run overwrites rather than accumulates.
///
/// Arg surface is forward-fit for later axes (player-policy personas, tuning A/B) — new flags
/// slot in without breaking `batch --seeds N --days M [--seed S] [--out DIR]` callers.
/// </summary>
public static class BatchRunner
{
    public const string Usage =
        "usage: batch --seeds <count> [--seed <startSeed>] [--days <days>] [--out <dir>] [--policy baseline|counter]";

    /// <summary>
    /// The player policy a sweep drives (U0: <see cref="CounterPlayer"/> was previously
    /// unreachable — hardcoded to <see cref="BaselinePlayer"/>). Default stays
    /// <see cref="Policy.Baseline"/> so <c>BaselinePlayerPinTests</c> and the golden corpus never move.
    /// </summary>
    public enum Policy
    {
        Baseline,
        Counter,
    }

    /// <summary>Parsed batch parameters. Defaults: 20 seeds starting at 1, 100 days, runs/, baseline policy.</summary>
    public sealed record BatchArgs(int SeedCount, ulong StartSeed, int Days, string OutDir, Policy PlayerPolicy);

    /// <summary>Parse args after the `batch` token. Null (with an error line) = invalid.</summary>
    public static BatchArgs? Parse(string[] args, TextWriter error)
    {
        var seedCount = 20;
        var startSeed = 1UL;
        var days = 100;
        var outDir = "runs";
        var policyArg = "baseline";

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
                case "--out" when i + 1 < args.Length:
                    outDir = args[i + 1];
                    i++;
                    break;
                case "--policy" when i + 1 < args.Length:
                    policyArg = args[i + 1];
                    i++;
                    break;
                default:
                    error.WriteLine($"batch: unknown or malformed arg '{args[i]}'");
                    error.WriteLine(Usage);
                    return null;
            }
        }

        if (seedCount <= 0 || days <= 0)
        {
            error.WriteLine("batch: --seeds and --days must be positive");
            error.WriteLine(Usage);
            return null;
        }

        if (startSeed > ulong.MaxValue - (ulong)(seedCount - 1))
        {
            // Unchecked wrap would silently duplicate low seeds and overwrite their chronicles.
            error.WriteLine($"batch: seed range {startSeed}+{seedCount} overflows — lower --seed or --seeds");
            return null;
        }

        Policy policy;
        switch (policyArg.ToLowerInvariant())
        {
            case "baseline":
                policy = Policy.Baseline;
                break;
            case "counter":
                policy = Policy.Counter;
                break;
            default:
                error.WriteLine($"batch: unknown --policy '{policyArg}' (expected 'baseline' or 'counter')");
                error.WriteLine(Usage);
                return null;
        }

        return new BatchArgs(seedCount, startSeed, days, outDir, policy);
    }

    /// <summary>Lowercase name embedded in the chronicle filename (corpus hygiene) — matches the
    /// <c>--policy</c> value so a filename always tells you which policy produced it.</summary>
    private static string PolicyFileTag(Policy policy) => policy switch
    {
        Policy.Counter => "counter",
        _ => "baseline",
    };

    /// <summary>The scripted policy driving this sweep (defaults to <see cref="BaselinePlayer"/> —
    /// never changes for an existing caller that omits <c>--policy</c>).</summary>
    private static Func<GameState, ImmutableList<PlayerAction>> PolicyFn(Policy policy) => policy switch
    {
        Policy.Counter => CounterPlayer.ActionsFor,
        _ => BaselinePlayer.ActionsFor,
    };

    /// <summary>
    /// Run the sweep: for each seed, a fresh campaign ticked to the END of day <c>Days</c>
    /// (i.e. until <c>state.Day &gt; Days</c>) under <see cref="BatchArgs.PlayerPolicy"/>
    /// (<see cref="BaselinePlayer"/> unless <c>--policy counter</c> selects
    /// <see cref="CounterPlayer"/>), then the chronicle serialized to
    /// <c>{outDir}/batch-seed{seed}-days{days}-{policy}.json</c> — the policy tag rides in the
    /// filename so a counter-policy sweep never accumulates next to (or gets mistaken for) a
    /// baseline corpus.
    /// Returns 0 on success, 1 on any failure (reported to <paramref name="error"/>).
    /// </summary>
    public static int Run(BatchArgs batch, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(batch.OutDir);

            // Corpus hygiene: filenames embed seed+days, so a SWEEP with different params would
            // ACCUMULATE next to stale chronicles and silently skew every corpus baseline in
            // Analytics — a sweep owns the dir's batch-*.json namespace and clears it first.
            // Single-seed runs (anomaly repros) deliberately do NOT clean: a repro pointed at the
            // corpus dir by mistake must never wipe 20 chronicles to write 1.
            // Interactive exports (run-*.json) are always untouched.
            if (batch.SeedCount > 1)
            {
                foreach (var stale in Directory.EnumerateFiles(batch.OutDir, "batch-*.json", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(stale);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"batch: cannot prepare output dir '{batch.OutDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();
        var policyFn = PolicyFn(batch.PlayerPolicy);
        var policyTag = PolicyFileTag(batch.PlayerPolicy);
        for (var i = 0; i < batch.SeedCount; i++)
        {
            var seed = batch.StartSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= batch.Days)
            {
                state = kernel.Tick(state, policyFn(state)).NewState;
            }

            var path = Path.Combine(batch.OutDir, $"batch-seed{seed}-days{batch.Days}-{policyTag}.json");
            try
            {
                File.WriteAllText(path, ChronicleCodec.Serialize(ChronicleCodec.FromState(seed, state)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"batch: write failed for '{path}': {ex.Message}");
                return 1; // fail loudly, never a partial silent success
            }

            output.WriteLine($"  seed {seed}: {batch.Days} days, {state.EventLog.Count} events -> {path}");
        }

        output.WriteLine($"batch complete: {batch.SeedCount} chronicle(s) in {batch.OutDir}");
        return 0;
    }
}
