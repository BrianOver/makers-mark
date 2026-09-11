using Analytics;
using GameSim.Chronicle;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Cli;

/// <summary>
/// The telemetry batch farm (observability plan U2, R4): parse surface, determinism of the
/// produced chronicles, and loud failure on bad input. Small day-counts keep this fast-lane.
/// </summary>
public class BatchRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "makersmark-batch-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort temp cleanup (AV/indexer holds throw either type on Windows)
        }
    }

    [Fact]
    public void Batch_WritesOneParseableChroniclePerSeed()
    {
        var args = BatchRunner.Parse(["--seeds", "2", "--days", "3", "--out", _dir], TextWriter.Null);
        Assert.NotNull(args);

        var exit = BatchRunner.Run(args!, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exit);
        var files = Directory.GetFiles(_dir, "batch-seed*-days3-baseline.json").OrderBy(f => f).ToList();
        Assert.Equal(2, files.Count);
        foreach (var file in files)
        {
            var chronicle = ChronicleCodec.Deserialize(File.ReadAllText(file));
            Assert.Equal(4, chronicle.Day);          // ran through the END of day 3
            Assert.NotEmpty(chronicle.Events);        // a real world happened
            Assert.NotEmpty(chronicle.Heroes);
        }
    }

    [Fact]
    public void Batch_SameSeed_IsByteIdenticalAcrossRuns()
    {
        var args = BatchRunner.Parse(["--seeds", "1", "--seed", "42", "--days", "3", "--out", _dir], TextWriter.Null);
        Assert.NotNull(args);

        Assert.Equal(0, BatchRunner.Run(args!, TextWriter.Null, TextWriter.Null));
        // Scoped to the chronicle specifically: a run now also writes a *.decisions.jsonl sidecar
        // (P2-HONEST-13), so the dir legitimately holds two files per seed.
        var path = Directory.GetFiles(_dir, "*.json", SearchOption.TopDirectoryOnly).Single();
        var first = File.ReadAllText(path);

        Assert.Equal(0, BatchRunner.Run(args!, TextWriter.Null, TextWriter.Null));
        var second = File.ReadAllText(path);

        Assert.Equal(first, second); // determinism: the farm re-produces identical bytes
    }

    [Theory]
    [InlineData("--seeds", "0")]
    [InlineData("--days", "0")]
    [InlineData("--seeds", "banana")]
    [InlineData("--bogus", "1")]
    [InlineData("--policy", "rival")]
    public void Parse_RejectsBadArgs(string flag, string value)
    {
        using var err = new StringWriter();
        var args = BatchRunner.Parse([flag, value], err);

        Assert.Null(args);
        Assert.Contains("batch", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Defaults_Are20SeedsFrom1For100DaysToRuns()
    {
        var args = BatchRunner.Parse([], TextWriter.Null);

        Assert.NotNull(args);
        Assert.Equal(20, args!.SeedCount);
        Assert.Equal(1UL, args.StartSeed);
        Assert.Equal(100, args.Days);
        Assert.Equal("runs", args.OutDir);
        Assert.Equal(BatchRunner.Policy.Baseline, args.PlayerPolicy); // U0: default must stay Baseline
    }

    [Fact]
    public void Policy_Counter_IsSelectableAndTagsItsOwnFilename()
    {
        // U0: CounterPlayer was previously unreachable from the batch farm (hardcoded selection).
        // Selecting it must (a) actually run CounterPlayer's scripted counter-session loop and
        // (b) tag its own filename distinctly so it never collides with a baseline corpus.
        var args = BatchRunner.Parse(["--seeds", "1", "--days", "3", "--out", _dir, "--policy", "counter"], TextWriter.Null);
        Assert.NotNull(args);
        Assert.Equal(BatchRunner.Policy.Counter, args!.PlayerPolicy);

        Assert.Equal(0, BatchRunner.Run(args, TextWriter.Null, TextWriter.Null));

        var file = Assert.Single(Directory.GetFiles(_dir, "batch-seed*-days3-counter.json"));
        var chronicle = ChronicleCodec.Deserialize(File.ReadAllText(file));
        Assert.Equal(4, chronicle.Day); // ran through the END of day 3, same as the baseline path
    }

    [Fact]
    public void Policy_Apprentice_IsSelectableAndTagsItsOwnFilename()
    {
        // P2-ONBOARD-03: ApprenticePlayer was previously unreachable from the batch farm (only
        // baseline/counter existed). Selecting it must (a) actually run ApprenticePlayer's guided
        // course and (b) tag its own filename distinctly so it never collides with the other
        // policies' corpora.
        var args = BatchRunner.Parse(["--seeds", "1", "--days", "3", "--out", _dir, "--policy", "apprentice"], TextWriter.Null);
        Assert.NotNull(args);
        Assert.Equal(BatchRunner.Policy.Apprentice, args!.PlayerPolicy);

        Assert.Equal(0, BatchRunner.Run(args, TextWriter.Null, TextWriter.Null));

        var file = Assert.Single(Directory.GetFiles(_dir, "batch-seed*-days3-apprentice.json"));
        var chronicle = ChronicleCodec.Deserialize(File.ReadAllText(file));
        Assert.Equal(4, chronicle.Day); // ran through the END of day 3, same as the baseline path
    }

    [Fact]
    public void Policy_HandForge_IsSelectableAndTagsItsOwnFilename()
    {
        // 2026-09-03 owner ruling: HandForgePlayer was previously unreachable from the batch farm
        // (only baseline/counter/apprentice existed, and none of them ever hand-forges). Selecting
        // it must (a) actually run HandForgePlayer's hand-forge-plus-echo loop and (b) tag its own
        // filename distinctly so it never collides with the other policies' corpora.
        var args = BatchRunner.Parse(["--seeds", "1", "--days", "3", "--out", _dir, "--policy", "handforge"], TextWriter.Null);
        Assert.NotNull(args);
        Assert.Equal(BatchRunner.Policy.HandForge, args!.PlayerPolicy);

        Assert.Equal(0, BatchRunner.Run(args, TextWriter.Null, TextWriter.Null));

        var file = Assert.Single(Directory.GetFiles(_dir, "batch-seed*-days3-handforge.json"));
        var chronicle = ChronicleCodec.Deserialize(File.ReadAllText(file));
        Assert.Equal(4, chronicle.Day); // ran through the END of day 3, same as the baseline path
    }

    [Fact]
    public void Policy_LateMastery_IsSelectableAndTagsItsOwnFilename()
    {
        // P2-OQ9's second talent-pacing measurement: LateMasteryPlayer was previously unreachable
        // from the batch farm (only baseline/counter/apprentice/handforge existed). Selecting it
        // must (a) actually run LateMasteryPlayer's reordered-talent hand-forge loop and (b) tag
        // its own filename distinctly so it never collides with the other policies' corpora.
        var args = BatchRunner.Parse(["--seeds", "1", "--days", "3", "--out", _dir, "--policy", "latemastery"], TextWriter.Null);
        Assert.NotNull(args);
        Assert.Equal(BatchRunner.Policy.LateMastery, args!.PlayerPolicy);

        Assert.Equal(0, BatchRunner.Run(args, TextWriter.Null, TextWriter.Null));

        var file = Assert.Single(Directory.GetFiles(_dir, "batch-seed*-days3-latemastery.json"));
        var chronicle = ChronicleCodec.Deserialize(File.ReadAllText(file));
        Assert.Equal(4, chronicle.Day); // ran through the END of day 3, same as the baseline path
    }

    [Fact]
    public void SweepCleansStaleBatchFiles_ButSingleSeedRepro_DoesNot()
    {
        // A sweep owns the dir's batch-*.json namespace (stale params would skew corpus baselines);
        // a single-seed run (anomaly repro) must NEVER wipe a corpus it was mistakenly pointed at.
        var sweep = BatchRunner.Parse(["--seeds", "2", "--days", "2", "--out", _dir], TextWriter.Null);
        Assert.Equal(0, BatchRunner.Run(sweep!, TextWriter.Null, TextWriter.Null));
        var stale = Path.Combine(_dir, "batch-seed999-days50.json");
        File.WriteAllText(stale, "{}");
        var export = Path.Combine(_dir, "run-seed7-day3.json"); // interactive export: always untouched
        File.WriteAllText(export, "{}");

        // Single-seed repro: stale corpus files survive.
        var repro = BatchRunner.Parse(["--seeds", "1", "--seed", "5", "--days", "2", "--out", _dir], TextWriter.Null);
        Assert.Equal(0, BatchRunner.Run(repro!, TextWriter.Null, TextWriter.Null));
        Assert.True(File.Exists(stale));
        Assert.True(File.Exists(export));

        // Sweep: stale batch file cleaned, interactive export preserved.
        Assert.Equal(0, BatchRunner.Run(sweep!, TextWriter.Null, TextWriter.Null));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(export));
    }

    // ---- P2-HONEST-13 (register #164): TickResult.Traces reaches a sibling *.decisions.jsonl,
    // in the exact shape tools/Analytics.DecisionLog already reads ---------------------------------

    [Fact]
    public void Batch_DecisionsSidecar_IsExactlyWhatTheKernelDrained()
    {
        // Independently re-drive the SAME seed/policy through the raw kernel (KTD5: determinism
        // guarantees this is byte-identical to what BatchRunner.Run does internally) to get the
        // traces the kernel ACTUALLY produced, then assert the sidecar file — read back through
        // Analytics's own DecisionLog, the real reader — carries exactly those, in order. This is
        // the property the unit is about: never one hand-picked trace string, whatever the kernel
        // drained this run.
        const ulong seed = 42;
        const int days = 5;
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);
        var expected = new List<DecisionTrace>();
        while (state.Day <= days)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;
            expected.AddRange(result.Traces);
        }

        Assert.NotEmpty(expected); // sanity: a 5-day baseline campaign really does trace something

        var args = BatchRunner.Parse(["--seeds", "1", "--seed", seed.ToString(), "--days", days.ToString(), "--out", _dir], TextWriter.Null);
        Assert.Equal(0, BatchRunner.Run(args!, TextWriter.Null, TextWriter.Null));

        var file = Assert.Single(Directory.GetFiles(_dir, "batch-seed*-days5-baseline.decisions.jsonl"));
        var rows = DecisionLog.ParseFile(file);

        Assert.Equal(expected.Count, rows.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].What, rows[i].What);
            Assert.Equal(expected[i].Chosen, rows[i].Chose);
            Assert.Equal(BatchRunner.DecisionWhy(expected[i]), rows[i].Why);
            Assert.Equal(-1, rows[i].Candidates); // DecisionTrace carries no candidate count
        }
    }

    [Fact]
    public void WriteDecisionLog_NoTraces_WritesNoFile()
    {
        // A tick sequence that traces nothing has nothing to explain — no sidecar, not an empty
        // one, so a corpus sweep can tell "never played with logging" apart from "played, traced
        // nothing" (DecisionLog.Report's own empty-input contract distinguishes the same two shapes
        // on the reading side). Exercised directly (not via a hoped-for untraced campaign day) so
        // the property under test — empty in, no file out — never depends on which seed happens to
        // avoid every haggle/craft/reforge path this run.
        var path = Path.Combine(_dir, "empty.decisions.jsonl");
        Directory.CreateDirectory(_dir);

        BatchRunner.WriteDecisionLog(path, []);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void WriteDecisionLog_WritesEveryTrace_ThatDecisionLogParsesBackIdentically_InOrder()
    {
        // Synthetic traces, deliberately covering both Detail shapes (empty and non-empty) and
        // repeated `What` slugs — the property is "whatever the kernel drained", not one hand-picked
        // trace string, so this must hold for an arbitrary trace list, not just a real campaign's.
        var traces = new List<DecisionTrace>
        {
            new("quality-roll", "Superior", "auto-craft ceiling", "isAutoCraft=True"),
            new("haggle-band", "round 1 opened", "no detail this time"), // Detail defaults to ""
            new("quality-roll", "Common", "performance-driven roll", "performanceGrade=200"),
        };
        var path = Path.Combine(_dir, "synthetic.decisions.jsonl");
        Directory.CreateDirectory(_dir);

        BatchRunner.WriteDecisionLog(path, traces);
        var rows = DecisionLog.ParseFile(path);

        Assert.Equal(traces.Count, rows.Count);
        for (var i = 0; i < traces.Count; i++)
        {
            Assert.Equal(traces[i].What, rows[i].What);
            Assert.Equal(traces[i].Chosen, rows[i].Chose);
            Assert.Equal(BatchRunner.DecisionWhy(traces[i]), rows[i].Why);
            Assert.Equal(-1, rows[i].Candidates);
        }
    }

    [Fact]
    public void Run_UnwritableOut_FailsLoudly()
    {
        // A path that cannot be a directory: nested under an existing FILE.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var args = BatchRunner.Parse(["--seeds", "1", "--days", "1", "--out", Path.Combine(blocker, "sub")], TextWriter.Null);
        Assert.NotNull(args);

        using var err = new StringWriter();
        var exit = BatchRunner.Run(args!, TextWriter.Null, err);

        Assert.Equal(1, exit);
        Assert.Contains("batch:", err.ToString(), StringComparison.Ordinal);
    }
}
