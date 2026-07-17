using Analytics;
using GameSim.Chronicle;

// Chronicle analytics (U14).
// Usage: dotnet run --project tools/Analytics -- <run.json | runs-dir> [more...]
// Emits a markdown tuning report to stdout.

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: analytics <run.json | runs-dir> [more...]");
    return 1;
}

var files = new List<string>();
// anomalies.md lands in the first DIRECTORY arg only: a directory = "analyze this corpus" (the
// loop's trigger file must reflect it); bare file args = ad-hoc inspection, stdout only — never
// silently overwrite the corpus trigger with a one-file view.
string? outDir = null;
foreach (var arg in args)
{
    if (Directory.Exists(arg))
    {
        outDir ??= arg;
        files.AddRange(Directory.EnumerateFiles(arg, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal));
    }
    else if (File.Exists(arg))
    {
        files.Add(arg);
    }
    else
    {
        Console.Error.WriteLine($"not found: {arg}");
        return 1;
    }
}

if (files.Count == 0)
{
    Console.Error.WriteLine("no run files found — play in the CLI and use its 'export' command first.");
    return 1;
}

var runs = new List<ChronicleData>();
var skipped = 0;
foreach (var file in files)
{
    try
    {
        var chronicle = ChronicleCodec.Deserialize(File.ReadAllText(file));

        // Valid JSON that isn't a chronicle ('{}') binds missing record params to null —
        // reject it here so no downstream consumer NREs mid-corpus.
        if (chronicle.Heroes is null || chronicle.Events is null)
        {
            Console.Error.WriteLine($"skipping non-chronicle json: {file}");
            skipped++;
            continue;
        }

        runs.Add(chronicle);
    }
    catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException
                                   or IOException or UnauthorizedAccessException)
    {
        // Locked/unreadable files (a batch still flushing, AV scan) skip like malformed ones —
        // one bad file must never abort the corpus (plan U3).
        Console.Error.WriteLine($"skipping unreadable/malformed chronicle: {file} ({ex.Message})");
        skipped++;
    }
}

if (runs.Count == 0)
{
    // Failing corpus: remove the stale trigger file too — yesterday's anomalies.md lying around
    // with live-looking repro pointers is worse than no file (the loop reads it as current).
    if (outDir is not null)
    {
        var stale = Path.Combine(outDir, "anomalies.md");
        if (File.Exists(stale))
        {
            File.Delete(stale);
            Console.Error.WriteLine("stale anomalies.md removed (corpus unreadable).");
        }
    }

    Console.Error.WriteLine("no readable run files.");
    return 1;
}

if (skipped > 0)
{
    Console.Error.WriteLine($"WARNING: {skipped} of {files.Count} file(s) skipped — corpus baselines cover {runs.Count} run(s) only.");
}

Console.WriteLine(Report.Build(runs));

// Anomaly pass (observability plan U3): severity-ranked heavy events with repro pointers.
// Written next to the corpus when a directory was given; always echoed to stdout.
var anomalies = Anomalies.Detect(runs);
var report = Anomalies.Render(anomalies, runs.Count);
Console.WriteLine(report);
if (outDir is not null)
{
    // Always written (file-only invocations included) — this file is the loop's trigger and must
    // never go silently stale while stdout implies success.
    var path = Path.Combine(outDir, "anomalies.md");
    File.WriteAllText(path, report);
    Console.Error.WriteLine($"anomalies written: {path} ({anomalies.Count} hit(s))");
}

return 0;
