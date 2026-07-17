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
foreach (var arg in args)
{
    if (Directory.Exists(arg))
    {
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
foreach (var file in files)
{
    try
    {
        runs.Add(ChronicleCodec.Deserialize(File.ReadAllText(file)));
    }
    catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
    {
        Console.Error.WriteLine($"skipping malformed chronicle: {file} ({ex.Message})");
    }
}

if (runs.Count == 0)
{
    Console.Error.WriteLine("no readable run files.");
    return 1;
}

Console.WriteLine(Report.Build(runs));

// Anomaly pass (observability plan U3): severity-ranked heavy events with repro pointers.
// Written next to the corpus when a directory was given; always echoed to stdout.
var anomalies = Anomalies.Detect(runs);
var report = Anomalies.Render(anomalies, runs.Count);
Console.WriteLine(report);
var outDir = args.FirstOrDefault(Directory.Exists);
if (outDir is not null)
{
    var path = Path.Combine(outDir, "anomalies.md");
    File.WriteAllText(path, report);
    Console.Error.WriteLine($"anomalies written: {path} ({anomalies.Count} hit(s))");
}

return 0;
