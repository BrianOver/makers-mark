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

    public sealed record PrRecord(int Number, string Title);

    public static IReadOnlyList<PrRecord> ListPrs(string repoRoot, string ownerRepo, string state)
    {
        var (code, stdout, stderr) = Run(repoRoot, "gh", "pr", "list",
            "--repo", ownerRepo, "--state", state, "--json", "number,title", "--limit", "2000");
        if (code != 0)
        {
            Console.Error.WriteLine($"warning: gh pr list --state {state} failed, treating as empty ({stderr.Trim()})");
            return Array.Empty<PrRecord>();
        }

        using var doc = JsonDocument.Parse(stdout);
        var list = new List<PrRecord>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new PrRecord(el.GetProperty("number").GetInt32(), el.GetProperty("title").GetString() ?? ""));
        }

        return list;
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
