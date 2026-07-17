using GameSim.Chronicle;
using GameSim.Cli;

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
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void Batch_WritesOneParseableChroniclePerSeed()
    {
        var args = BatchRunner.Parse(["--seeds", "2", "--days", "3", "--out", _dir], TextWriter.Null);
        Assert.NotNull(args);

        var exit = BatchRunner.Run(args!, TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exit);
        var files = Directory.GetFiles(_dir, "batch-seed*-days3.json").OrderBy(f => f).ToList();
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
        var path = Directory.GetFiles(_dir).Single();
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
