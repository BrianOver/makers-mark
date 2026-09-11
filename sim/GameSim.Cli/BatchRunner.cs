using System.Collections.Immutable;
using System.Text.Json;
using GameSim;
using GameSim.Chronicle;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Professions;

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
///
/// P2-HONEST-13 (register #164): alongside the chronicle JSON, each seed also writes a sibling
/// <c>*.decisions.jsonl</c> — every <see cref="TickResult.Traces"/> the kernel drained across the
/// whole campaign, one JSON object per line. This is deliberately the SAME on-disk shape
/// <c>godot/scripts/PlaytestLog.cs</c>'s <c>Decision</c> channel already writes
/// (<c>{"kind":"decision","what":...,"chose":...,"why":...,"candidates":...}</c>), so
/// <c>tools/Analytics</c>'s existing <c>DecisionLog</c> reader — built for that channel and until
/// now fed by nothing but a human playtest with <c>MM_PLAYTEST_LOG</c> set — picks these up with
/// zero changes to Analytics itself. <see cref="TickResult.Traces"/> stays exactly where
/// <c>DecisionTraceTests</c> pins it (the kernel's return value, never <see cref="GameState"/>):
/// this reads the return value on every tick and writes it out here at the edge, the same way the
/// chronicle write below does for state.
/// </summary>
public static class BatchRunner
{
    public const string Usage =
        "usage: batch --seeds <count> [--seed <startSeed>] [--days <days>] [--out <dir>] "
        + "[--policy baseline|counter|apprentice|handforge|latemastery|alchemy|tanning|engineering] "
        + "[--hand indifferent|average|skilled]";

    /// <summary>
    /// The player policy a sweep drives (U0: <see cref="CounterPlayer"/> was previously
    /// unreachable — hardcoded to <see cref="BaselinePlayer"/>). Default stays
    /// <see cref="Policy.Baseline"/> so <c>BaselinePlayerPinTests</c> and the golden corpus never move.
    /// P2-ONBOARD-03 adds <see cref="Policy.Apprentice"/> (<see cref="ApprenticePlayer"/>), the
    /// guided-course policy the P2-ONBOARD-04 seed search sweeps against — this axis was built
    /// forward-fit for exactly that (see this class's own doc comment).
    /// 2026-09-03 owner ruling adds <see cref="Policy.HandForge"/> (<see cref="HandForgePlayer"/>) —
    /// the first policy on this axis that actually hand-forges (submits a real
    /// <c>ForgeTraceInput</c>), closing the coverage blind spot #686 found.
    /// P2-OQ9's second talent-pacing measurement adds <see cref="Policy.LateMastery"/>
    /// (<see cref="LateMasteryPlayer"/>) — the same hand-forge loop, with the two mastery talents
    /// deferred behind every other node the tree allows, so the resulting quality curve can be
    /// compared against <see cref="Policy.HandForge"/>'s greedy-order one.
    /// P2-OQ10 adds <see cref="Policy.AlchemyPuzzle"/>/<see cref="Policy.TanningPuzzle"/>/
    /// <see cref="Policy.EngineeringPuzzle"/> (<see cref="AlchemyPuzzlePlayer"/>/
    /// <see cref="TanningPuzzlePlayer"/>/<see cref="EngineeringPuzzlePlayer"/>) — each selects ITS
    /// OWN profession alone (<see cref="PolicyStartingProfession"/> routes the campaign through
    /// <see cref="GameComposition.NewCampaign(ulong,string)"/> instead of the blacksmith-default
    /// overload) and actually walks its scorer's active-craft path, closing the blind spot no
    /// existing policy ever touched: none of them crafts outside <c>RecipeTable.All</c> (blacksmith),
    /// so these three professions had ZERO measured crafts of any kind before this axis.
    /// </summary>
    public enum Policy
    {
        Baseline,
        Counter,
        Apprentice,
        HandForge,
        LateMastery,
        AlchemyPuzzle,
        TanningPuzzle,
        EngineeringPuzzle,
    }

    /// <summary>Parsed batch parameters. Defaults: 20 seeds starting at 1, 100 days, runs/, baseline
    /// policy, average hand.</summary>
    public sealed record BatchArgs(
        int SeedCount, ulong StartSeed, int Days, string OutDir, Policy PlayerPolicy,
        CraftHand Hand = CraftHand.Average);

    /// <summary>Parse args after the `batch` token. Null (with an error line) = invalid.</summary>
    public static BatchArgs? Parse(string[] args, TextWriter error)
    {
        var seedCount = 20;
        var startSeed = 1UL;
        var days = 100;
        var outDir = "runs";
        var policyArg = "baseline";
        var handArg = "average";

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
                case "--hand" when i + 1 < args.Length:
                    handArg = args[i + 1];
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

        if (ParsePolicy(policyArg) is not { } policy)
        {
            error.WriteLine($"batch: unknown --policy '{policyArg}' ({PolicyNames})");
            error.WriteLine(Usage);
            return null;
        }

        CraftHand hand;
        switch (handArg.ToLowerInvariant())
        {
            case "indifferent":
                hand = CraftHand.Indifferent;
                break;
            case "average":
                hand = CraftHand.Average;
                break;
            case "skilled":
                hand = CraftHand.Skilled;
                break;
            default:
                error.WriteLine($"batch: unknown --hand '{handArg}' (expected 'indifferent', 'average', or 'skilled')");
                error.WriteLine(Usage);
                return null;
        }

        if (hand != CraftHand.Average && !HandAware(policy))
        {
            // Refused rather than silently ignored: a sweep that thinks it measured a skilled hand
            // and actually measured an auto-crafting policy is exactly the mis-read P2-OQ11 exists
            // to stop (see CraftHand's class doc).
            error.WriteLine($"batch: --hand {handArg} needs a policy that plays a craft minigame "
                + $"(handforge, latemastery, alchemy, tanning, engineering) — '{policyArg}' auto-crafts");
            return null;
        }

        return new BatchArgs(seedCount, startSeed, days, outDir, policy, hand);
    }

    /// <summary>The <c>--policy</c> values this CLI accepts, rendered for an error line. One
    /// string, shared by every sweep that offers the axis, so a new policy is spelled out in one
    /// place instead of in each command's own error message.</summary>
    internal const string PolicyNames =
        "expected 'baseline', 'counter', 'apprentice', 'handforge', 'latemastery', 'alchemy', 'tanning', or 'engineering'";

    /// <summary>Map a <c>--policy</c> argument onto its <see cref="Policy"/>, or null when the
    /// argument names no policy. Shared by <see cref="Parse"/> and every other sweep that offers the
    /// same axis (<see cref="ArcStallSweep"/>), so the two can never drift apart on spelling.</summary>
    internal static Policy? ParsePolicy(string policyArg) => policyArg.ToLowerInvariant() switch
    {
        "baseline" => Policy.Baseline,
        "counter" => Policy.Counter,
        "apprentice" => Policy.Apprentice,
        "handforge" => Policy.HandForge,
        "latemastery" => Policy.LateMastery,
        "alchemy" => Policy.AlchemyPuzzle,
        "tanning" => Policy.TanningPuzzle,
        "engineering" => Policy.EngineeringPuzzle,
        _ => null,
    };

    /// <summary>Does this policy submit a real craft-minigame input, so that a
    /// <see cref="CraftHand"/> means anything to it?</summary>
    internal static bool HandAware(Policy policy) => policy
        is Policy.HandForge or Policy.LateMastery
        or Policy.AlchemyPuzzle or Policy.TanningPuzzle or Policy.EngineeringPuzzle;

    /// <summary>Lowercase name embedded in the chronicle filename (corpus hygiene) — matches the
    /// <c>--policy</c> value, plus the <c>--hand</c> value when it is not the default, so a filename
    /// always tells you which policy AND which skill level produced it. The default hand is left off
    /// deliberately: every chronicle written before <c>--hand</c> existed was an average hand, so
    /// omitting it keeps the existing corpus's names meaning exactly what they always meant.</summary>
    internal static string PolicyFileTag(Policy policy, CraftHand hand) =>
        hand == CraftHand.Average
            ? PolicyFileTag(policy)
            : $"{PolicyFileTag(policy)}-{hand.ToString().ToLowerInvariant()}";

    /// <inheritdoc cref="PolicyFileTag(Policy, CraftHand)"/>
    internal static string PolicyFileTag(Policy policy) => policy switch
    {
        Policy.Counter => "counter",
        Policy.Apprentice => "apprentice",
        Policy.HandForge => "handforge",
        Policy.LateMastery => "latemastery",
        Policy.AlchemyPuzzle => "alchemy",
        Policy.TanningPuzzle => "tanning",
        Policy.EngineeringPuzzle => "engineering",
        _ => "baseline",
    };

    /// <summary>The scripted policy driving this sweep (defaults to <see cref="BaselinePlayer"/> —
    /// never changes for an existing caller that omits <c>--policy</c>).</summary>
    internal static Func<GameState, ImmutableList<PlayerAction>> PolicyFn(Policy policy, CraftHand hand) => policy switch
    {
        Policy.Counter => CounterPlayer.ActionsFor,
        Policy.Apprentice => ApprenticePlayer.ActionsFor,
        Policy.HandForge => state => HandForgePlayer.ActionsFor(state, hand),
        Policy.LateMastery => state => LateMasteryPlayer.ActionsFor(state, hand),
        Policy.AlchemyPuzzle => state => AlchemyPuzzlePlayer.ActionsFor(state, hand),
        Policy.TanningPuzzle => state => TanningPuzzlePlayer.ActionsFor(state, hand),
        Policy.EngineeringPuzzle => state => EngineeringPuzzlePlayer.ActionsFor(state, hand),
        _ => BaselinePlayer.ActionsFor,
    };

    /// <summary>
    /// P2-OQ10: the three new puzzle-coverage policies each need their OWN profession selected from
    /// day 1 (<see cref="ProfessionHandlers.MaxSelected"/> caps a save at 1-2, and none of these
    /// three is blacksmith) — null for every existing policy, which stays on the blacksmith-default
    /// <see cref="GameComposition.NewCampaign(ulong)"/> overload exactly as before.
    /// </summary>
    internal static string? PolicyStartingProfession(Policy policy) => policy switch
    {
        Policy.AlchemyPuzzle => AlchemyProfession.Id,
        Policy.TanningPuzzle => TanningProfession.Id,
        Policy.EngineeringPuzzle => EngineeringProfession.Id,
        _ => null,
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

                // Named as its own explicit glob rather than folded into "batch-*.json" above: an
                // extension-suffix pattern (".json" vs ".jsonl") is exactly the shape where Windows'
                // legacy 8.3-short-name matching can silently make one glob catch both, or neither,
                // depending on volume settings nobody here controls. Spelled out, it is correct either way.
                foreach (var stale in Directory.EnumerateFiles(batch.OutDir, "batch-*.decisions.jsonl", SearchOption.TopDirectoryOnly))
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
        var policyFn = PolicyFn(batch.PlayerPolicy, batch.Hand);
        var policyTag = PolicyFileTag(batch.PlayerPolicy, batch.Hand);
        var startingProfession = PolicyStartingProfession(batch.PlayerPolicy);
        for (var i = 0; i < batch.SeedCount; i++)
        {
            var seed = batch.StartSeed + (ulong)i;
            var state = startingProfession is null
                ? GameComposition.NewCampaign(seed)
                : GameComposition.NewCampaign(seed, startingProfession);
            var traces = ImmutableList.CreateBuilder<DecisionTrace>();
            while (state.Day <= batch.Days)
            {
                var result = kernel.Tick(state, policyFn(state));
                state = result.NewState;
                traces.AddRange(result.Traces);
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

            var decisionsPath = Path.Combine(batch.OutDir, $"batch-seed{seed}-days{batch.Days}-{policyTag}.decisions.jsonl");
            try
            {
                WriteDecisionLog(decisionsPath, traces);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"batch: decisions write failed for '{decisionsPath}': {ex.Message}");
                return 1; // same fail-loudly contract as the chronicle write above
            }

            output.WriteLine($"  seed {seed}: {batch.Days} days, {state.EventLog.Count} events, {traces.Count} decision trace(s) -> {path}");
        }

        output.WriteLine($"batch complete: {batch.SeedCount} chronicle(s) in {batch.OutDir}");
        return 0;
    }

    /// <summary>
    /// Writes <paramref name="traces"/> as one <c>kind:"decision"</c> JSON object per line, in the
    /// SAME shape <c>PlaytestLog.Decision</c> writes it (see this class's doc comment) so
    /// <c>tools/Analytics</c>'s <c>DecisionLog</c> reads it unmodified. <see cref="DecisionTrace"/>
    /// carries no candidate count (it is a "what and why" record, not a "what and how many options"
    /// one — that's <c>PlaytestLog.Decision</c>'s own, richer shape), so <c>candidates</c> is always
    /// -1 here, exactly <c>DecisionLog.DecisionRow</c>'s documented "the caller could not say".
    /// <see cref="DecisionTrace.Detail"/> folds into <c>why</c> when non-empty rather than being
    /// dropped, since it is the numbers behind the reason and this format has no field of its own
    /// for it. Writes nothing (not even an empty file) when <paramref name="traces"/> is empty — a
    /// run that traced no decisions leaves no sibling file to confuse "no traces" with "no file yet".
    /// Internal (not private) so the round-trip tests can drive it directly with a synthetic trace
    /// list, rather than only through a real campaign that may or may not trace anything this run.
    /// </summary>
    internal static void WriteDecisionLog(string path, IReadOnlyList<DecisionTrace> traces)
    {
        if (traces.Count == 0)
        {
            return;
        }

        using var writer = new StreamWriter(path, append: false);
        foreach (var trace in traces)
        {
            var row = new { kind = "decision", what = trace.What, chose = trace.Chosen, why = DecisionWhy(trace), candidates = -1 };
            writer.WriteLine(JsonSerializer.Serialize(row));
        }
    }

    /// <summary>The <c>why</c> field's fold of <see cref="DecisionTrace.Reason"/> and
    /// <see cref="DecisionTrace.Detail"/> — internal (not private) so the round-trip test can predict
    /// it without duplicating the fold rule by hand.</summary>
    internal static string DecisionWhy(DecisionTrace trace) =>
        trace.Detail.Length == 0 ? trace.Reason : $"{trace.Reason} ({trace.Detail})";
}
