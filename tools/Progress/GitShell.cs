using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Progress;

public sealed record GitLogEntry(string Sha, string Subject);

/// <summary>The only impure surface: shells out to `git` and `gh`. Everything it returns is a
/// plain record or collection so Program.cs can hand it straight to the pure parser/reconciler.
/// This tool never writes anything back into the repo — it only reads.</summary>
public static class GitShell
{
    // Unit-separator byte: cannot appear in a sha or a commit subject, unlike ':' or '|' which
    // subjects use freely. git's own format-string escape (%x1f) emits the raw 0x1F byte; the C#
    // side splits on the same byte via its \x1f escape — no literal control character sits in
    // this source file.
    private const char FieldSeparator = '\x1f';

    public static string FindRepoRoot(string startDir)
    {
        var (code, stdout, stderr) = Run(startDir, "git", "rev-parse", "--show-toplevel");
        if (code != 0)
        {
            throw new InvalidOperationException($"git rev-parse --show-toplevel failed: {stderr}");
        }

        return stdout.Trim().Replace('\\', '/');
    }

    /// <summary>Best-effort: keeps the local `origin/main` ref current. A network-unavailable
    /// sandbox should not hard-fail the tool — it just reconciles against whatever `origin/main`
    /// this checkout already knows about, and says so on stderr.</summary>
    public static void TryFetchOriginMain(string repoRoot)
    {
        var (code, _, stderr) = Run(repoRoot, "git", "fetch", "origin", "main", "--quiet");
        if (code != 0)
        {
            Console.Error.WriteLine($"warning: git fetch origin main failed, using existing local ref ({stderr.Trim()})");
        }
    }

    public static HashSet<string> ListTrackedFiles(string repoRoot, string gitRef)
    {
        var (code, stdout, stderr) = Run(repoRoot, "git", "ls-tree", "-r", "--name-only", gitRef);
        if (code != 0)
        {
            throw new InvalidOperationException($"git ls-tree -r --name-only {gitRef} failed: {stderr}");
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                set.Add(trimmed);
            }
        }

        return set;
    }

    public static IReadOnlyList<GitLogEntry> GetLog(string repoRoot, string gitRef)
    {
        var (code, stdout, stderr) = Run(repoRoot, "git", "log", "--reverse", "--pretty=format:%H%x1f%s", gitRef);
        if (code != 0)
        {
            throw new InvalidOperationException($"git log {gitRef} failed: {stderr}");
        }

        var entries = new List<GitLogEntry>();
        foreach (var line in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var sep = line.IndexOf(FieldSeparator);
            if (sep < 0)
            {
                continue;
            }

            entries.Add(new GitLogEntry(line[..sep], line[(sep + 1)..]));
        }

        return entries;
    }

    public static string GetOwnerRepo(string repoRoot)
    {
        var (code, stdout, stderr) = Run(repoRoot, "git", "remote", "get-url", "origin");
        if (code != 0)
        {
            throw new InvalidOperationException($"git remote get-url origin failed: {stderr}");
        }

        var url = stdout.Trim();
        // Handles both "https://github.com/Owner/Repo.git" and "git@github.com:Owner/Repo.git".
        var m = Regex.Match(url, @"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(?:\.git)?/?$");
        if (!m.Success)
        {
            throw new InvalidOperationException($"could not parse owner/repo from origin url '{url}'");
        }

        return $"{m.Groups["owner"].Value}/{m.Groups["repo"].Value}";
    }

    /// <summary><see cref="Body"/> and <see cref="MergedAt"/> are what the receipt census reads
    /// (rule 12's `Serves:` line lives in the body, not the title) — fetched in the same call as
    /// title so there is no second round trip per PR. <see cref="MergedAt"/> is null for open
    /// PRs.</summary>
    public sealed record PrRecord(int Number, string Title, string Body, DateTimeOffset? MergedAt);

    public static IReadOnlyList<PrRecord> ListPrs(string repoRoot, string ownerRepo, string state)
    {
        var (code, stdout, stderr) = Run(repoRoot, "gh", "pr", "list",
            "--repo", ownerRepo, "--state", state, "--json", "number,title,body,mergedAt", "--limit", "2000");
        if (code != 0)
        {
            Console.Error.WriteLine($"warning: gh pr list --state {state} failed, treating as empty ({stderr.Trim()})");
            return Array.Empty<PrRecord>();
        }

        using var doc = JsonDocument.Parse(stdout);
        var list = new List<PrRecord>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            DateTimeOffset? mergedAt = el.TryGetProperty("mergedAt", out var m) && m.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(m.GetString()!)
                : null;
            list.Add(new PrRecord(
                el.GetProperty("number").GetInt32(),
                el.GetProperty("title").GetString() ?? "",
                el.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                mergedAt));
        }

        return list;
    }

    /// <summary>For every path `git log --diff-filter=A` ever added on <paramref name="gitRef"/>,
    /// the commit (sha + subject) that added it — newest add wins when a path was added more than
    /// once (deleted and recreated), since that is the commit that put the file's current
    /// incarnation on the branch. One call over full history, not one per path: cheap (a few
    /// thousand lines) and avoids spawning a git process per unit's "new" file.</summary>
    public static IReadOnlyDictionary<string, GitLogEntry> GetFileCreationCommits(string repoRoot, string gitRef)
    {
        var (code, stdout, stderr) = Run(repoRoot, "git", "log", gitRef,
            "--diff-filter=A", "--name-only", "--pretty=format:COMMIT\x1f%H\x1f%s");
        if (code != 0)
        {
            throw new InvalidOperationException($"git log --diff-filter=A {gitRef} failed: {stderr}");
        }

        var result = new Dictionary<string, GitLogEntry>(StringComparer.Ordinal);
        GitLogEntry? current = null;
        foreach (var rawLine in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            if (rawLine.Length >= 7 && rawLine.StartsWith("COMMIT", StringComparison.Ordinal) && rawLine[6] == FieldSeparator)
            {
                var rest = rawLine[7..];
                var sep = rest.IndexOf(FieldSeparator);
                current = sep < 0 ? null : new GitLogEntry(rest[..sep], rest[(sep + 1)..]);
                continue;
            }

            var path = rawLine.Trim();
            if (path.Length == 0 || current is null)
            {
                continue;
            }

            if (!result.ContainsKey(path))
            {
                result[path] = current;
            }
        }

        return result;
    }

    /// <summary>The date CLAUDE.md rule 12's receipt line first required a specific literal form
    /// (`git log -S`, so it is derived from history rather than hand-pinned as a constant that
    /// would itself drift). Merged PRs from before this date predate the rule and are not
    /// candidates for a "missing receipt" finding. Returns null if the marker text is not found
    /// (e.g. CLAUDE.md was rewritten past recognition) — callers should then skip the date-gated
    /// finding rather than guess.</summary>
    public static DateTimeOffset? GetReceiptRuleEffectiveDate(string repoRoot, string gitRef)
    {
        var (code, stdout, _) = Run(repoRoot, "git", "log", gitRef, "--reverse",
            "--format=%cI", "-S", "Serves: P<n>", "--", "CLAUDE.md");
        if (code != 0)
        {
            return null;
        }

        var first = stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null ? null : DateTimeOffset.Parse(first);
    }

    public static string ReadFile(string repoRoot, string relativePath)
    {
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static (int Code, string Stdout, string Stderr) Run(string workingDir, string exe, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {exe}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
