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

var runs = files.Select(f => ChronicleCodec.Deserialize(File.ReadAllText(f))).ToList();
Console.WriteLine(Report.Build(runs));
return 0;
