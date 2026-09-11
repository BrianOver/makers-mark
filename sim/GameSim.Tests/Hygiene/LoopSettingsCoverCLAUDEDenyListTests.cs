using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// The overnight loop's permission overlay (<c>tools/loop/settings.json</c>) is a second copy of a
/// decision <c>CLAUDE.md</c> already made — its multi-agent deny-list. A second copy is exactly the
/// failure this repo keeps paying for, so this derives the required set FROM <c>CLAUDE.md</c> and
/// fails when the overlay stops covering it.
///
/// <para><b>Why an overlay exists at all rather than trusting the rule.</b> The deny-list in
/// <c>CLAUDE.md</c> is a sentence a model may or may not obey. The overlay is a harness deny: the
/// tool call fails. Under an unattended run at 3am that difference is the whole safety story, and
/// the system this loop borrows from is the cautionary tale — its "human-gated" claims rested on
/// prose plus a client-side hook while its <c>main</c> had no server-side protection at all.</para>
///
/// <para><b>And the opposite failure, which is the sharper one.</b> That same system's unattended
/// profile denied every <c>gh pr merge</c> — the exact command its own loop's merge step runs — and
/// its test suite pinned that behaviour rather than catching it. A deny-list that blocks the loop's
/// own hands is worse than no deny-list, because it fails at 3am in a way nobody sees until morning.
/// <see cref="TheOverlayNeverDeniesACommandTheLoopItselfMustIssue"/> is that guard.</para>
/// </summary>
public class LoopSettingsCoverCLAUDEDenyListTests
{
    /// <summary>The deny-list sentence in <c>CLAUDE.md</c>'s multi-agent section, and the backticked
    /// paths inside it. Anchored on the sentence rather than a line number so a reflow cannot
    /// silently empty the derived set.</summary>
    private static readonly Regex DenyListSentence =
        new(@"\*\*Deny-list[^*]*\*\*:?(?<body>[^\r\n]+)", RegexOptions.Compiled);

    private static readonly Regex BacktickedPath = new(@"`([^`]+)`", RegexOptions.Compiled);

    [Fact]
    public void EveryPathCLAUDEDenyListsIsDeniedByTheLoopOverlay()
    {
        var claudePaths = DenyListedPaths();

        // Fixture guard, never a skip: an empty derived set would make every assertion below
        // vacuously true, which is precisely the shape of a guard that stopped covering its family.
        Assert.True(claudePaths.Count >= 5,
            $"Derived only {claudePaths.Count} deny-listed paths from CLAUDE.md — the sentence moved or "
            + "changed shape, and this guard is now proving nothing. Fix the parse, never the assertion.");

        var denied = OverlayDenyEntries();
        var uncovered = claudePaths
            .Where(p => !denied.Any(d => d.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(uncovered.Count == 0,
            "tools/loop/settings.json does not deny paths CLAUDE.md's own deny-list names, so an "
            + "unattended run could edit them with nothing but a sentence stopping it:\n  "
            + string.Join("\n  ", uncovered));
    }

    [Fact]
    public void BothEditAndWriteAreDeniedForEveryCoveredPath()
    {
        // Denying Edit but not Write (or the reverse) reads as covered in a diff and is not. The
        // two tools reach the same bytes.
        var denied = OverlayDenyEntries();
        var editTargets = TargetsOf(denied, "Edit");
        var writeTargets = TargetsOf(denied, "Write");

        var editOnly = editTargets.Except(writeTargets, StringComparer.OrdinalIgnoreCase).ToList();
        var writeOnly = writeTargets.Except(editTargets, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(editOnly.Count == 0 && writeOnly.Count == 0,
            "A path denied to one of Edit/Write but not the other is not actually protected:\n"
            + $"  Edit-only: {string.Join(", ", editOnly)}\n  Write-only: {string.Join(", ", writeOnly)}");
    }

    [Fact]
    public void TheOverlayNeverDeniesACommandTheLoopItselfMustIssue()
    {
        // The source system's measured defect: its unattended profile denied `gh pr merge`, which
        // is what its own Step 7 runs, and its tests pinned the denial instead of catching it. The
        // gate that stops a bad merge here is the branch ruleset, server-side — not a client
        // setting, which anything running as the user could talk its way past anyway.
        var loopCommands = new[] { "gh pr merge", "gh pr create", "gh pr checks", "dotnet test", "git commit" };
        var denied = OverlayDenyEntries();

        var selfBlocking = loopCommands
            .Where(c => denied.Any(d => d.Contains(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(selfBlocking.Count == 0,
            "tools/loop/settings.json denies a command the loop's own procedure issues, so an "
            + "unattended run would stall on it with nobody awake to approve:\n  "
            + string.Join("\n  ", selfBlocking));
    }

    [Fact]
    public void TheOverlayIsDenyOnly()
    {
        // An allow-list would be a third copy of what CLAUDE.md and the ruleset already decide, and
        // the one that silently stops covering its family when a tool is added. Deny entries only
        // ever subtract: a stale one costs a prompt, never a breach.
        using var document = JsonDocument.Parse(File.ReadAllText(OverlayPath()));
        var permissions = document.RootElement.GetProperty("permissions");

        Assert.False(permissions.TryGetProperty("allow", out _),
            "tools/loop/settings.json grew an allow-list. Deny-only is the point — see its own $comment.");
    }

    private static List<string> DenyListedPaths()
    {
        var claude = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));
        var sentence = DenyListSentence.Match(claude);
        if (!sentence.Success)
        {
            return [];
        }

        return BacktickedPath.Matches(sentence.Groups["body"].Value)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    private static List<string> OverlayDenyEntries()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(OverlayPath()));
        return document.RootElement
            .GetProperty("permissions")
            .GetProperty("deny")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToList();
    }

    /// <summary>The path inside a <c>Tool(path)</c> deny entry, for the named tool only.</summary>
    private static List<string> TargetsOf(IEnumerable<string> denied, string tool)
    {
        var pattern = new Regex($@"^{Regex.Escape(tool)}\((?<target>.+)\)$");
        return denied
            .Select(d => pattern.Match(d))
            .Where(m => m.Success)
            .Select(m => m.Groups["target"].Value)
            .ToList();
    }

    private static string OverlayPath() => Path.Combine(RepoRoot(), "tools", "loop", "settings.json");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repo root (no Game.sln above the test binary).");
    }
}
