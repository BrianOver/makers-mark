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
// U-T6: decision-log sweep, additive to (never overlapping with) the chronicle sweep above — see
// DecisionLog.cs's own doc for why this is a different file FORMAT (.jsonl, PlaytestLog's session
// trail) from a different SOURCE (the Godot client, not sim/GameSim.Cli batch).
var logFiles = new List<string>();
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
        logFiles.AddRange(Directory.EnumerateFiles(arg, "*.jsonl", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal));
    }
    else if (File.Exists(arg))
    {
        if (arg.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            logFiles.Add(arg);
        }
        else
        {
            files.Add(arg);
        }
    }
    else
    {
        Console.Error.WriteLine($"not found: {arg}");
        return 1;
    }
}

if (files.Count == 0 && logFiles.Count == 0)
{
    Console.Error.WriteLine("no run files found — play in the CLI and use its 'export' command first.");
    return 1;
}

// Chronicle lane: unchanged from before U-T6, and skipped entirely (never a hard error) when the
// invocation asked for decision logs only (files.Count == 0) — see the decision-log lane below.
if (files.Count > 0)
{
    var runs = new List<ChronicleData>();
    // Positionally paired with `runs` (see Anomalies.Detect's own doc comment on why index pairing,
    // not a seed lookup) — recovered per-file since ChronicleData carries no policy field itself
    // (sim purity, KTD2; BatchRunner instead tags the FILENAME, see Anomalies.InferPolicyFromFileName).
    var policies = new List<string>();
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
            policies.Add(Anomalies.InferPolicyFromFileName(file));
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
    var anomalies = Anomalies.Detect(runs, policies);
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
}

// U-T6 (register #164): decision-log lane, independent of the chronicle lane above — a corpus with
// no .jsonl files prints the honest "no decision rows found" line (DecisionLog.Report's own empty
// contract) rather than staying silent, so an owner sweeping runs/ can tell "nobody has played with
// logging on" apart from "this tool forgot to look."
var decisionRows = new List<DecisionLog.DecisionRow>();
foreach (var logFile in logFiles)
{
    decisionRows.AddRange(DecisionLog.ParseFile(logFile));
}

Console.WriteLine(DecisionLog.Report(decisionRows));

return 0;
