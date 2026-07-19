using FlavorForge;
using FlavorForge.Emit;
using FlavorForge.Generation;
using FlavorForge.Model;

// FlavorForge (P008): dev-time flavor-pack generator. Every candidate is gated through the real
// GameSim.Flavor.FlavorEngine.TryRenderTemplate before it can be proposed or emitted (KTD-C).
// Default mode writes a review proposal; --emit splices accepted lines into the existing pack
// file (KTD-F). This tool never runs at game runtime — it lives only under tools/, and is never
// referenced by sim/GameSim or godot/ (R13/R14).
//
// Usage: flavorforge --surface <tavern|faction|ledger|narrator> (--stub | --endpoint <url> --model <id>)
//                     [--api-shape ollama|openai] [--count N] [--emit] [--pack-file PATH]
//                     [--out DIR] [--config PATH]

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string? surfaceName = null;
string? endpoint = null;
string? model = null;
string? apiShapeArg = null;
int? count = null;
var emit = false;
var useStub = false;
string? packFileOverride = null;
string? outOverride = null;
string? configPath = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--surface":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--surface requires a value"); return 1; }
            surfaceName = args[++i];
            break;
        case "--endpoint":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--endpoint requires a value"); return 1; }
            endpoint = args[++i];
            break;
        case "--model":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--model requires a value"); return 1; }
            model = args[++i];
            break;
        case "--api-shape":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--api-shape requires a value"); return 1; }
            apiShapeArg = args[++i];
            break;
        case "--count":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--count requires a value"); return 1; }
            if (!int.TryParse(args[++i], out var parsedCount) || parsedCount <= 0)
            {
                Console.Error.WriteLine($"--count must be a positive integer, got '{args[i]}'");
                return 1;
            }

            count = parsedCount;
            break;
        case "--emit":
            emit = true;
            break;
        case "--stub":
            useStub = true;
            break;
        case "--pack-file":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--pack-file requires a value"); return 1; }
            packFileOverride = args[++i];
            break;
        case "--out":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--out requires a value"); return 1; }
            outOverride = args[++i];
            break;
        case "--config":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--config requires a value"); return 1; }
            configPath = args[++i];
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (configPath is not null)
{
    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"--config file not found: {configPath}");
        return 1;
    }

    var config = ForgeConfig.Load(configPath);
    endpoint ??= config.Endpoint;
    model ??= config.Model;
    count ??= config.CandidateCount;
    apiShapeArg ??= config.ApiShape;
}

count ??= 6;
apiShapeArg ??= "ollama";

if (surfaceName is null)
{
    Console.Error.WriteLine("--surface is required");
    PrintUsage();
    return 1;
}

if (!SurfaceContract.TryResolve(surfaceName, out var surface) || surface is null)
{
    Console.Error.WriteLine($"unknown surface '{surfaceName}' — valid: {string.Join(", ", SurfaceContract.All.Keys)}");
    return 1;
}

IFlavorModelClient client;
if (useStub)
{
    client = new StubModelClient();
}
else if (endpoint is not null)
{
    if (model is null)
    {
        Console.Error.WriteLine("--model is required with --endpoint");
        return 1;
    }

    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri))
    {
        Console.Error.WriteLine($"--endpoint is not a valid URL: '{endpoint}'");
        return 1;
    }

    if (!TryParseApiShape(apiShapeArg, out var shape))
    {
        Console.Error.WriteLine($"--api-shape must be 'ollama' or 'openai', got '{apiShapeArg}'");
        return 1;
    }

    client = new LocalHttpModelClient(baseUri, model, shape);
}
else
{
    Console.Error.WriteLine("specify --stub for a dry run, or --endpoint <url> --model <id> for a live local model");
    return 1;
}

var repoRoot = RepoLayout.FindRepoRoot();
var packFilePath = packFileOverride ?? RepoLayout.PackFilePath(repoRoot, surface);
var proposalsDir = outOverride ?? RepoLayout.DefaultProposalsDirectory(repoRoot);

try
{
    var results = await CandidateGenerator.GenerateSurfaceAsync(client, surface, count.Value).ConfigureAwait(false);

    var totalAccepted = 0;
    var totalRejected = 0;
    var totalDuplicates = 0;
    foreach (var result in results)
    {
        Console.Error.WriteLine(
            $"{result.Key}: {result.Accepted.Count} accepted / {result.RejectedCount} rejected / {result.DuplicateCount} dupes");
        totalAccepted += result.Accepted.Count;
        totalRejected += result.RejectedCount;
        totalDuplicates += result.DuplicateCount;
    }

    Console.Error.WriteLine(
        $"TOTAL ({surface.Name}): {totalAccepted} accepted / {totalRejected} rejected / {totalDuplicates} dupes across {results.Count} cells");

    if (emit)
    {
        var acceptedByKey = results
            .Where(r => r.Accepted.Count > 0)
            .ToDictionary(r => r.Key, r => (IReadOnlyList<string>)r.Accepted, StringComparer.Ordinal);

        if (acceptedByKey.Count == 0)
        {
            Console.Error.WriteLine("nothing accepted — pack file left untouched");
            return 0;
        }

        try
        {
            PackEmitter.SpliceFile(packFilePath, acceptedByKey);
        }
        catch (PackEmitAnchorNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.Error.WriteLine($"emitted into {packFilePath} — re-run the fast lane and re-pin any shifted prose golden (KTD-E)");
    }
    else
    {
        var path = ProposalWriter.Write(proposalsDir, surface.Name, results);
        Console.Error.WriteLine($"proposal written: {path} (review it, then re-run with --emit to splice into the pack)");
    }

    return 0;
}
catch (FlavorModelUnavailableException ex)
{
    Console.Error.WriteLine($"model unavailable: {ex.Message}");
    return 1;
}
finally
{
    if (client is IDisposable disposable)
    {
        disposable.Dispose();
    }
}

static bool TryParseApiShape(string? value, out LocalModelApiShape shape)
{
    switch (value?.ToLowerInvariant())
    {
        case "ollama":
            shape = LocalModelApiShape.OllamaGenerate;
            return true;
        case "openai":
            shape = LocalModelApiShape.OpenAiChat;
            return true;
        default:
            shape = default;
            return false;
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("usage: flavorforge --surface <tavern|faction|ledger|narrator> (--stub | --endpoint <url> --model <id>)");
    Console.Error.WriteLine("                    [--api-shape ollama|openai] [--count N] [--emit]");
    Console.Error.WriteLine("                    [--pack-file PATH] [--out DIR] [--config PATH]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --stub            deterministic dry run, zero network IO (no real candidates)");
    Console.Error.WriteLine("  --endpoint/--model  live local model (Ollama :11434 or LM Studio :1234) — see README.md");
    Console.Error.WriteLine("  --count           candidates requested per (baseKey, voice) cell (default 6)");
    Console.Error.WriteLine("  --emit            splice accepted lines into the pack file (default: write a proposal only)");
    Console.Error.WriteLine("  --pack-file       override the target pack file (tests point this at a fixture)");
    Console.Error.WriteLine("  --out             override the proposals directory");
    Console.Error.WriteLine("  --config          JSON file overlay for endpoint/model/count (see config.sample.json)");
}
