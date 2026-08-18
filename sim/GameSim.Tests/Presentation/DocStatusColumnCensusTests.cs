using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GameSim.Tests.Presentation;

/// <summary>
/// <c>CLAUDE.md</c> rule 8: "No checkboxes, status banners, or shipped/unwired counts in any doc —
/// progress lives in <c>git log</c> and PRs. A doc caught asserting what git contradicts is deleted
/// in your current PR, not corrected. A stale doc is not clutter; it is an instruction the next
/// session obeys."
///
/// <para><b>The cost, measured rather than imagined.</b> On 2026-08-17 the owner playtest register
/// still carried <c>open</c> against register items #159, #166 and #167 — all three of which had
/// merged to <c>main</c> the previous day (#531, #533, #541, #547). A worker lane was dispatched
/// against those rows and spent its entire budget proving the work already existed. The register was
/// not lying on purpose; it was edited less often than the code it described, which is the permanent
/// condition of every status column that has ever been written.</para>
///
/// <para><b>Why a test and not a promise.</b> Rule 8 has been in <c>CLAUDE.md</c> the whole time and
/// the column was written anyway, because a rule that nothing executes is a rule that is followed
/// only when someone happens to remember it. This is the fast lane, so it fires on every push.</para>
///
/// <para><b>What counts as a status assertion</b> is deliberately narrow: a markdown table column
/// literally headed <c>Status</c>, and a markdown task checkbox. Both are structural — they cannot be
/// mistaken for prose that happens to describe history ("shipped by #453"), which stays legal because
/// a sentence naming a merged PR is a citation, not a claim about the present.</para>
/// </summary>
public class DocStatusColumnCensusTests
{
    /// <summary>
    /// <c>docs/design/MAKERS-MARK.md</c> is the single plan of record and carries two status columns:
    /// the §8 "ledger, at a glance" BUILT/NOT-BUILT table and the §11.4 critical-path table, whose
    /// Status cells also hold owner rulings and blocked-by reasoning that exist nowhere else.
    ///
    /// <para>It is exempt because removing those columns is a real editorial unit — a DONE row does
    /// not belong on a critical path at all, so the fix is to delete the row and keep its ruling, not
    /// to blank a cell — and doing it silently inside an unrelated PR would lose planning content.
    /// The exemption is a booking, not an endorsement: that table has already been caught wrong once
    /// in its own text, where a row corrects itself from "3 of 4" to "2 of 4".</para>
    ///
    /// <para>The count below is pinned, so adding a second exemption is a red build that has to be
    /// argued for in a diff — the same shape as the constitution's own exception pinning.</para>
    /// </summary>
    private static readonly (string Path, string Reason)[] Exemptions =
    [
        ("docs/design/MAKERS-MARK.md",
            "plan of record; §8 ledger + §11.4 critical-path Status cells carry owner rulings and "
            + "blocked-by reasoning. Removing them is its own editorial unit — delete the DONE rows, "
            + "keep the rulings — not a blanked cell inside an unrelated PR."),
    ];

    private const int PinnedExemptionCount = 1;

    [Fact]
    public void NoDoc_CarriesAStatusColumn_BecauseGitOutranksEveryDoc()
    {
        var offenders = DocFiles()
            .Where(f => !IsExempt(f))
            .Where(f => File.ReadLines(f.Full).Any(IsStatusColumnHeader))
            .Select(f => f.Relative)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} doc(s) carry a table column headed 'Status'. A status column is edited "
            + "less often than the code it describes, so it eventually asserts what git contradicts — "
            + "and the next session obeys it (CLAUDE.md rule 8). Record what was asked and the evidence "
            + "for it; let `git log --grep` answer what shipped:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoDoc_CarriesATaskCheckbox_BecauseAnUntickedBoxIsAnInstruction()
    {
        var offenders = DocFiles()
            .Where(f => !IsExempt(f))
            .Where(f => File.ReadLines(f.Full).Any(IsTaskCheckbox))
            .Select(f => f.Relative)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} doc(s) carry markdown task checkboxes. An unticked box outlives the "
            + "work it described and reads as an open instruction forever (CLAUDE.md rule 8):\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Both directions, so an exemption can neither be added nor silently kept after the doc
    /// it excuses has been cleaned up.</summary>
    [Fact]
    public void TheExemptions_ArePinned_AndEachNamesADocThatStillExists()
    {
        Assert.True(
            Exemptions.Length == PinnedExemptionCount,
            $"The exemption list holds {Exemptions.Length} row(s) against a pinned "
            + $"{PinnedExemptionCount}. Adding one is allowed, but only as a deliberate diff that "
            + "also moves this number — an exemption nobody had to argue for is not an exemption.");

        foreach (var (path, reason) in Exemptions)
        {
            Assert.True(
                File.Exists(Path.Combine(RepoRoot(), path.Replace('/', Path.DirectorySeparatorChar))),
                $"Exempted doc '{path}' no longer exists — delete the exemption row in the same PR.");

            Assert.True(
                reason.Length >= 40,
                $"Exemption '{path}' needs a written reason, not a placeholder.");

            var full = Path.Combine(RepoRoot(), path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(
                File.ReadLines(full).Any(IsStatusColumnHeader) || File.ReadLines(full).Any(IsTaskCheckbox),
                $"Exempted doc '{path}' no longer carries a status column or checkbox — the exemption "
                + "is now excusing nothing. Delete the row.");
        }
    }

    private static bool IsStatusColumnHeader(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('|'))
        {
            return false;
        }

        return trimmed
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Any(cell => cell.Trim().Trim('*').Equals("Status", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTaskCheckbox(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("- [ ]", StringComparison.Ordinal)
            || t.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("* [ ]", StringComparison.Ordinal)
            || t.StartsWith("* [x]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExempt((string Full, string Relative) f) =>
        Exemptions.Any(e => e.Path.Equals(f.Relative, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Full, string Relative)> DocFiles()
    {
        var docs = Path.Combine(RepoRoot(), "docs");
        return Directory
            .EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/')));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
