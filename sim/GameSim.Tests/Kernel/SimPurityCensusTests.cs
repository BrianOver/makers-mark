using System.Reflection;
using System.Text.RegularExpressions;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

// LAW:sim-purity-determinism
// LAW:no-runtime-llm

/// <summary>
/// CLAUDE.md rule 12's tripwire for rule 4 (sim purity) and for "no runtime LLMs in the sim".
///
/// <para><b>Why this exists.</b> Rule 4 is the oldest law in the project and, until this file, the
/// least enforced: the csproj structurally prevents a Godot reference, and the golden replay catches
/// determinism breaks that happen to change a fingerprint — but "no wall-clock reads", "no RNG
/// outside the injected stream", and "no transcendental <c>Math.*</c>" were enforced by nothing but
/// ~30 hand-written file-header comments promising to obey them. A comment is not a gate. This file
/// makes the promise executable: it reads every <c>sim/GameSim/**/*.cs</c>, strips comments, and
/// fails BY NAME on any banned token that is not pinned below with a reason citing the owner ruling
/// that granted it.</para>
///
/// <para><b>THE HONEST FRAMING.</b> This is a TOKEN census, not a purity proof. It proves no source
/// line mentions a forbidden API; it proves nothing about a violation reached indirectly (a helper
/// in another assembly that reads the clock, reflection, a delegate handed in from outside). The
/// determinism proof itself remains the golden replay in <c>DeterminismTests</c> — this census is
/// the cheap tripwire that catches the honest mistake before the expensive test catches the
/// expensive one. Do not let a future reader mistake a green run here for "the sim is pure."</para>
/// </summary>
public class SimPurityCensusTests
{
    /// <summary>
    /// Every banned token, with the clause of rule 4 (or rule 12) it defends. Ordered most-specific
    /// first so a match reports the sharpest name — <c>Random.Shared</c> before <c>Random</c>.
    /// </summary>
    private static readonly (string Name, Regex Pattern, string Law)[] Banned =
    [
        ("DateTime",        new Regex(@"\bDateTimeOffset\b|\bDateTime\b"),          "no wall-clock reads in the sim"),
        ("Stopwatch",       new Regex(@"\bStopwatch\b"),                            "no wall-clock reads in the sim"),
        ("Environment.Tick", new Regex(@"\bEnvironment\.TickCount\d*\b"),           "no wall-clock reads in the sim"),
        ("Random",          new Regex(@"\bnew Random\b|\bRandom\.Shared\b"),        "no RNG outside the kernel's injected stream"),
        ("Guid.NewGuid",    new Regex(@"\bGuid\.NewGuid\b"),                        "no RNG outside the kernel's injected stream"),
        ("Math.transcendental", new Regex(@"\bMath\.(Sin|Cos|Tan|Exp|Log|Log10|Pow|Sqrt|Asin|Acos|Atan|Atan2)\b"),
                                                                                    "no transcendental Math.* (cross-OS float drift)"),
        ("floating point",  new Regex(@"\b(float|double|decimal)\b"),               "integer math only (cross-OS float drift)"),
        ("Godot",           new Regex(@"\bGodot\b"),                                "sim/GameSim has ZERO Godot references"),
        ("network",         new Regex(@"\bHttpClient\b|\bSystem\.Net\b|\bWebSocket\b"),
                                                                                    "no runtime LLMs, and no runtime anything else, in the sim"),
    ];

    /// <summary>
    /// The drift ledger. Every entry is a deliberate grant: <c>(relative path, token name)</c> mapped
    /// to a reason that MUST cite the owner ruling permitting it (<c>§11.7.x</c> or <c>P&lt;n&gt;</c>),
    /// asserted mechanically by <see cref="EveryPinnedException_CitesTheRulingThatGrantedIt"/>. This
    /// table is the point of the whole file: drift that cannot be prevented is at least counted,
    /// named, and reasoned, in a compiled file that fails when it goes stale.
    /// </summary>
    private static readonly Dictionary<(string File, string Token), string> Exceptions = new();

    /// <summary>
    /// Pinned count, the <c>ExpectedActionCount = 24</c> idiom applied to exceptions: adding a grant
    /// is a red build before it is a reviewed diff, so no one grants themselves an exception quietly.
    /// </summary>
    private const int ExpectedExceptionCount = 0;

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned exception count is {ExpectedExceptionCount}; the table now holds "
            + $"{Exceptions.Count}. Granting an exception must be a deliberate, reviewed diff.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File} [{e.Key.Token}]: \"{e.Value}\"")
            .ToList();

        Assert.True(uncited.Count == 0,
            "A purity exception with no owner ruling behind it is drift wearing a reason. Cite the "
            + "§11.7.x ruling or P<n> plan item that granted it:\n  " + string.Join("\n  ", uncited));
    }

    [Fact]
    public void NoSimSourceFile_UsesABannedApi_UnlessPinnedWithAReason()
    {
        var files = SimSourceFiles();

        // The denominator guard (the green-54 lesson): a census that scanned nothing passes forever.
        Assert.True(files.Count >= 40,
            $"Only {files.Count} sim source files were scanned — the census found nothing to police, "
            + "which is indistinguishable from a clean run. Check the repo-root walk in SimRoot().");

        var violations = new List<string>();
        foreach (var (relative, absolute) in files)
        {
            var code = StripComments(File.ReadAllText(absolute));
            foreach (var (name, pattern, law) in Banned)
            {
                if (!pattern.IsMatch(code)) continue;
                if (Exceptions.ContainsKey((relative, name))) continue;
                violations.Add($"{relative} [{name}] — {law}");
            }
        }

        Assert.True(violations.Count == 0,
            "Sim purity (CLAUDE.md rules 4 and 12) is broken in these files. The fix is never to "
            + "soften this test — either the code stops doing it, or an owner ruling grants a pinned "
            + "exception with a citation:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void NoPinnedException_IsStale()
    {
        var files = SimSourceFiles().ToDictionary(f => f.Relative, f => f.Absolute);
        var stale = new List<string>();

        foreach (var ((file, token), _) in Exceptions)
        {
            if (!files.TryGetValue(file, out var absolute))
            {
                stale.Add($"{file} [{token}] — file no longer exists");
                continue;
            }
            var rule = Banned.FirstOrDefault(b => b.Name == token);
            if (rule.Pattern is null)
            {
                stale.Add($"{file} [{token}] — no banned token by that name");
                continue;
            }
            if (!rule.Pattern.IsMatch(StripComments(File.ReadAllText(absolute))))
                stale.Add($"{file} [{token}] — the violation is gone; delete the exception");
        }

        Assert.True(stale.Count == 0,
            "Stale exceptions are the failure mode this whole design exists to prevent: a pinned "
            + "grant that outlives its violation is a doc asserting what git contradicts (rule 8), "
            + "living in a test file:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The no-runtime-LLM law, structurally: the sim assembly may reference nothing but the BCL. An
    /// HTTP client, a model SDK, or any third-party package would appear here long before it appeared
    /// in a token scan of source we happened to remember to read.
    /// </summary>
    [Fact]
    public void SimAssembly_ReferencesNothingButTheBcl()
    {
        var foreign = typeof(GameKernel).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                     && !n.Equals("netstandard", StringComparison.Ordinal)
                     && !n.Equals("mscorlib", StringComparison.Ordinal))
            .ToList();

        Assert.True(foreign.Count == 0,
            "The sim took a dependency outside the BCL. Rule 4 (purity) and rule 12 (no runtime LLMs "
            + "in the sim) both live here: " + string.Join(", ", foreign));
    }

    /// <summary>Comment-stripped source. Without this the census reports every file whose header
    /// comment PROMISES not to use floats — which is most of them, and which would have made this
    /// test useless on its first run.</summary>
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return source;
    }

    private static List<(string Relative, string Absolute)> SimSourceFiles()
    {
        var root = SimRoot();
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), p))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Walks up from the test assembly to the repo root (the directory holding
    /// <c>Game.sln</c>) so this works from any worktree, any checkout path, and CI alike.</summary>
    private static string SimRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        var sim = Path.Combine(dir!.FullName, "sim", "GameSim");
        Assert.True(Directory.Exists(sim), $"Expected the sim at {sim}.");
        return sim;
    }
}
