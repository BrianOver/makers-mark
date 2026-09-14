using System.Reflection;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-26 ("the night's narration is shown or stops being composed"): the Evening ledger
/// used to call <c>ExpeditionNarrator.Retell</c> every evening and throw away everything but the
/// attribution beats and the Halt closer (<c>CollapsedTale</c>'s filter) — the departure line and
/// every floor's tension prose (floor-enter, quaff, kill, hurt, flee) were composed and never
/// read. The fix deleted <c>Retell</c> (its only production caller was that one call site) and
/// gave the ledger <c>ExpeditionNarrator.AttributionRecap</c>, which composes nothing but beats
/// and the closer in the first place.
///
/// <para>This is a token census over <c>godot/scripts/**/*.cs</c>, same idiom as
/// <c>SimPurityCensusTests</c>: it does not prove no Godot surface ever composes discarded prose
/// (a future helper could re-derive the same shape under a new name) — it proves no Godot source
/// calls the three sim entry points whose job IS building that prose
/// (<see cref="GameSim.Narrative.ExpeditionNarrator.FloorBeats"/>,
/// <see cref="GameSim.Narrative.ExpeditionNarrator.Departure"/>, and the deleted
/// <c>ExpeditionNarrator.Retell</c>, kept banned here as a name even though calling it is now also
/// a compile error, so the guard still reads as intentional if a differently-shaped convenience
/// wrapper is ever added back under the same name). A pinned exception must cite the ruling that
/// grants it — nobody grants themselves one quietly.</para>
/// </summary>
public class LedgerNarrationCensusTests
{
    private static readonly (string Name, string Token, string Reason)[] Banned =
    [
        ("ExpeditionNarrator.Retell", "ExpeditionNarrator.Retell(",
            "deleted by P2-HONEST-26 — the whole-tale composer with no reader"),
        ("ExpeditionNarrator.FloorBeats", "ExpeditionNarrator.FloorBeats(",
            "builds per-floor tension prose (floor-enter/quaff/kill/hurt/flee) the Evening ledger " +
            "does not render — that surface is the CLI's staged drip only"),
        ("ExpeditionNarrator.Departure", "ExpeditionNarrator.Departure(",
            "builds the departure line the Evening ledger does not render"),
    ];

    private static readonly Dictionary<(string File, string Token), string> Exceptions = new();
    private const int ExpectedExceptionCount = 0;

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned exception count is {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void NoGodotSourceFile_ComposesTheDiscardedNarration_UnlessPinnedWithAReason()
    {
        var files = GodotScriptFiles();

        // The denominator guard (the green-54 lesson): a census that scanned nothing passes forever.
        Assert.True(files.Count >= 40,
            $"Only {files.Count} godot/scripts files were scanned — check the walk in GodotScriptsRoot().");

        var violations = new List<string>();
        foreach (var (relative, absolute) in files)
        {
            var code = File.ReadAllText(absolute);
            foreach (var (name, token, reason) in Banned)
            {
                if (!code.Contains(token, StringComparison.Ordinal)) continue;
                if (Exceptions.ContainsKey((relative, name))) continue;
                violations.Add($"{relative} [{name}] — {reason}");
            }
        }

        Assert.True(violations.Count == 0,
            "A Godot source file composes the evening narration P2-HONEST-26 stopped generating "
            + "because nothing reads it. Either the composed prose gets a real surface (an owner "
            + "decision, cited as a pinned exception here) or the call is removed:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void NoPinnedException_IsStale()
    {
        var files = GodotScriptFiles().ToDictionary(f => f.Relative, f => f.Absolute);
        var stale = new List<string>();

        foreach (var ((file, token), _) in Exceptions)
        {
            if (!files.TryGetValue(file, out var absolute))
            {
                stale.Add($"{file} [{token}] — file no longer exists");
                continue;
            }

            var rule = Banned.FirstOrDefault(b => b.Name == token);
            if (rule.Token is null)
            {
                stale.Add($"{file} [{token}] — no banned token by that name");
                continue;
            }

            if (!File.ReadAllText(absolute).Contains(rule.Token, StringComparison.Ordinal))
                stale.Add($"{file} [{token}] — the call is gone; delete the exception");
        }

        Assert.True(stale.Count == 0,
            "A pinned exception that outlives its call is a doc asserting what git contradicts "
            + "(rule 8), living in a test file:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>Ledger still renders the pride payload (negative control on the OTHER half of this
    /// unit): <c>AttributionRecap</c> — the replacement call — is present exactly where the deleted
    /// <c>Retell</c> call used to be, so the census above isn't just failing to find anything.</summary>
    [Fact]
    public void LedgerModal_StillCallsTheAttributionRecapReplacement()
    {
        var root = RepoRoot();
        var ledger = Path.Combine(root, "godot", "scripts", "panels", "LedgerModal.cs");
        Assert.True(File.Exists(ledger), $"Expected {ledger}.");

        Assert.Contains("ExpeditionNarrator.AttributionRecap(", File.ReadAllText(ledger));
    }

    private static List<(string Relative, string Absolute)> GodotScriptFiles()
    {
        var root = GodotScriptsRoot();
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), p))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private static string GodotScriptsRoot() => Path.Combine(RepoRoot(), "godot", "scripts");

    /// <summary>Walks up from the test assembly to the repo root (the directory holding
    /// <c>Game.sln</c>) so this works from any worktree, any checkout path, and CI alike.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }
}
