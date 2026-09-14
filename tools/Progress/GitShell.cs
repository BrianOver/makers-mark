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
    /// <summary>
    /// Every place this run fell back to incomplete data instead of failing. Empty on a healthy
    /// run.
    ///
    /// <para><b>Why a list rather than a throw.</b> Two reads here are deliberately tolerant: a
    /// `git fetch` that fails leaves the tool reconciling against whatever `origin/main` this
    /// checkout already knows, and a `gh pr list` that fails leaves it with no open-PR index. For a
    /// HUMAN reading the report that is the right call -- a warning on stderr beside a report that
    /// is 95% right beats no report at all in a sandbox with no network.</para>
    ///
    /// <para><b>And why it is the wrong call for a machine.</b> Both degradations move units in the
    /// same direction: a stale ref makes a unit that landed an hour ago read Unbuilt, and an empty
    /// PR index makes every Open unit read Unbuilt. So the failure mode of a silent fallback is a
    /// frontier FULL of work that is already done or already in flight -- an unattended run would
    /// rebuild it, and every census downstream would report None for want of data while the exit
    /// code still said zero. That is the exact shape of the green-over-a-real-defect failure this
    /// whole tool exists to catch, so `--frontier` reads this list and refuses outright.</para>
    /// </summary>
    public static IReadOnlyList<string> Degradations => DegradationLog;

    private static readonly List<string> DegradationLog = new();

    private static void Degraded(string what)
    {
        DegradationLog.Add(what);
        Console.Error.WriteLine($"warning: {what}");
    }

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

    /// <summary>Best-effort for a human reader, recorded as a degradation for a machine one: a
    /// network-unavailable sandbox should not hard-fail the report, but a frontier computed
    /// against a stale `origin/main` would hand back units that landed since the last successful
    /// fetch. See <see cref="Degradations"/>.</summary>
    public static void TryFetchOriginMain(string repoRoot)
    {
        var (code, _, stderr) = Run(repoRoot, "git", "fetch", "origin", "main", "--quiet");
        if (code != 0)
        {
            Degraded($"git fetch origin main failed, so every landedness call below is against a "
                + $"possibly stale local ref ({stderr.Trim()})");
        }
    }

    /// <summary>
    /// Every tracked SOURCE line on <paramref name="gitRef"/> that writes one of
    /// <paramref name="unitIds"/> into its text, keyed by unit id, classified as a code hit or a
    /// comment hit — the input for the <see cref="SourceTaggedUnbuilt"/> warning.
    ///
    /// <para>Source only, never <c>docs/</c>: the plan itself names every id by definition, so
    /// including it would match all of them and say nothing. One `git grep` process for the whole
    /// id set rather than one per unit; a no-match run exits 1, which is the empty answer and not a
    /// failure, so only a code above 1 throws.</para>
    ///
    /// <para><b>Why the full line, not <c>--only-matching</c>.</b> The prior version captured only
    /// the matched id text, so it could never tell a real implementation from a doc-comment
    /// forward-reference deferring one ("U33 gives her a graduation line; this unit ships the
    /// mechanism the fact rides on, not the voice" is a real line on main) — every hit refused
    /// dispatch regardless. Capturing the whole line (plus <c>-n</c> for the line number, so a
    /// reader can jump straight to the site) is what <see cref="IsCommentHit"/> needs to
    /// classify.</para>
    /// </summary>
    public static Dictionary<string, List<SourceTagHit>> ListSourceTagSites(
        string repoRoot, string gitRef, IEnumerable<string> unitIds)
    {
        var ids = unitIds.Distinct(StringComparer.Ordinal).ToList();
        var sites = new Dictionary<string, List<SourceTagHit>>(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return sites;
        }

        var args = new List<string> { "grep", "-n", "--fixed-strings" };
        foreach (var id in ids)
        {
            args.Add("-e");
            args.Add(id);
        }

        args.Add(gitRef);
        args.Add("--");
        args.AddRange(["*.cs", "*.gd", "*.ps1", "*.py", "*.yml"]);

        // This tool's own tree is excluded, and the exclusion is load-bearing rather than tidy: its
        // fixtures name real unit ids as test data (`P2-PROOF-04`, `U41`, `P2-PEOPLE-08`), and a
        // fixture is evidence of nothing about whether that unit shipped. Without this the tool
        // reports warnings caused by itself — measured on the first run, three of eleven.
        args.AddRange([":(exclude)tools/Progress/*", ":(exclude)tools/Progress.Tests/*"]);

        var (code, stdout, stderr) = Run(repoRoot, "git", args.ToArray());
        if (code > 1)
        {
            throw new InvalidOperationException($"git grep over {ids.Count} unit ids failed: {stderr}");
        }

        // `<rev>:<path>:<lineno>:<content>` — the rev is exactly the `gitRef` we passed (no colon
        // in a ref name like `origin/main`) and the path has no ':' in this repo, so stripping the
        // known rev prefix and then splitting the remainder on the next two colons is exact; the
        // content itself is free to contain colons (a URL, a ratio) because nothing after the
        // second colon is split any further.
        var revPrefix = gitRef + ":";
        foreach (var line in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.StartsWith(revPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[revPrefix.Length..];
            var pathEnd = rest.IndexOf(':');
            if (pathEnd < 0)
            {
                continue;
            }

            var path = rest[..pathEnd];
            var afterPath = rest[(pathEnd + 1)..];
            var lineEnd = afterPath.IndexOf(':');
            if (lineEnd < 0)
            {
                continue;
            }

            var lineNoText = afterPath[..lineEnd];
            var content = afterPath[(lineEnd + 1)..];
            if (path.Length == 0 || !int.TryParse(lineNoText, out var lineNo))
            {
                continue;
            }

            // git grep guarantees the line matches at least one of the OR'd `-e` patterns but
            // (without --only-matching) does not say which — so every id is checked against this
            // one line's content directly, same fixed-string semantics as the grep itself.
            foreach (var id in ids)
            {
                var matchIndex = content.IndexOf(id, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    continue;
                }

                if (!sites.TryGetValue(id, out var hits))
                {
                    hits = [];
                    sites[id] = hits;
                }

                if (!hits.Any(h => h.Path == path && h.Line == lineNo))
                {
                    hits.Add(new SourceTagHit(path, lineNo, IsCommentHit(content, matchIndex)));
                }
            }
        }

        return sites;
    }

    /// <summary>
    /// Whether a comment marker sits before <paramref name="matchIndex"/> on this line, or the
    /// whole line is a block-comment continuation. Pure and unit-tested directly against literal
    /// strings — deliberately git-agnostic.
    ///
    /// <para>Covers the four inline markers the tracked extensions actually use — <c>//</c> (which
    /// also covers <c>///</c>, a superset match), <c>#</c> (<c>*.ps1</c>/<c>*.py</c>/<c>*.yml</c>),
    /// and <c>&lt;!--</c> — plus a line whose trimmed start is <c>*</c> or <c>/*</c>, the
    /// Javadoc/JSDoc-style block-comment continuation that carries no marker of its own on the
    /// line the id sits on.</para>
    ///
    /// <para><b>Which way this fails.</b> A marker missed here reads the line as CODE, which keeps
    /// today's conservative refuse-and-verify behaviour — no unit is ever newly unblocked by a
    /// classifier miss. Only a marker correctly found downgrades a hit to a note. So the failure
    /// mode of this heuristic is safe in the direction it fails.</para>
    /// </summary>
    public static bool IsCommentHit(string lineContent, int matchIndex)
    {
        var trimmed = lineContent.TrimStart();
        if (trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return true;
        }

        if (matchIndex <= 0)
        {
            return false;
        }

        var before = lineContent[..matchIndex];
        return before.Contains("//", StringComparison.Ordinal)
            || before.Contains('#')
            || before.Contains("<!--", StringComparison.Ordinal);
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
            Degraded($"gh pr list --state {state} failed, so the {state}-PR index is EMPTY rather "
                + $"than absent -- every unit it would have marked reads as unbuilt ({stderr.Trim()})");
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
