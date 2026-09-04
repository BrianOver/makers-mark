#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The owner finding this unit answers (2026-09-04): <c>HeroPanel</c> rendered a chip literally
/// labeled "Rank" off <c>Hero.Xp</c> thresholds, while the quantity that actually decides who a
/// hero marches with and how deep — <c>Hero.LadderRank</c> — rendered nowhere in the client under
/// ANY label. The fix (same PR) splits the old chip into two honestly named ones: "Veterancy" (the
/// XP-tier name, now read straight off <c>GameSim.Heroes.HeroRank</c> instead of a stale local
/// copy) and "Venue" (<c>Hero.LadderRank</c>'s own rung, named after the live venue(s) at that
/// rung rather than an invented vocabulary).
///
/// <para>This is the regression tripwire for that specific class of bug — two different quantities
/// rendered under the same word. It scans every <c>StatChip("...")</c> call site under
/// <c>godot/scripts</c> — the one call shape a chip label renders through (same discovery idiom as
/// <c>TrinketChipCensusTests</c>: a source census, not a hand-typed file:line list) — and proves
/// (1) no chip anywhere is labeled the bare, retired word "Rank", and (2) each of this unit's two
/// new labels, "Venue" and "Veterancy", has exactly ONE call site, so no second definition can
/// quietly compete with it for the same word later.</para>
///
/// <para>The negative lookbehind on the label regex excludes <c>NamedStatChip(...)</c> (a distinct
/// helper whose first string argument is a node NAME, not a rendered label) — without it, "Named"
/// immediately preceding "StatChip(" would still match on the substring and misattribute a node id
/// as a chip label.</para>
/// </summary>
[TestSuite]
public class HeroStandingLabelCensusTests
{
    private static readonly Regex ChipLabel = new(@"(?<!\w)StatChip\(\s*""([^""]*)""");

    [TestCase]
    public void NoChipAnywhere_IsLabeledTheRetiredWord_Rank()
    {
        var hits = AllLabelHits().Where(h => h.Label == "Rank").ToList();

        AssertThat(hits.Count)
            .OverrideFailureMessage(
                "A chip labeled bare \"Rank\" exists again -- this is the exact word collision "
                + "HeroPanel's Venue/Veterancy split fixed (LadderRank vs. the XP-tier both read "
                + "\"Rank\"). Name it honestly instead of reusing the retired word:\n  "
                + string.Join("\n  ", hits.Select(h => $"{h.File}:{h.Line}")))
            .IsEqual(0);
    }

    [TestCase]
    public void Venue_And_Veterancy_EachHaveExactlyOneCallSite()
    {
        var venue = AllLabelHits().Where(h => h.Label == "Venue").ToList();
        var veterancy = AllLabelHits().Where(h => h.Label == "Veterancy").ToList();

        AssertThat(venue.Count)
            .OverrideFailureMessage(
                "\"Venue\" chip label should have exactly one call site (HeroPanel); found: "
                + string.Join(", ", venue.Select(h => $"{h.File}:{h.Line}")))
            .IsEqual(1);
        AssertThat(veterancy.Count)
            .OverrideFailureMessage(
                "\"Veterancy\" chip label should have exactly one call site (HeroPanel); found: "
                + string.Join(", ", veterancy.Select(h => $"{h.File}:{h.Line}")))
            .IsEqual(1);
    }

    /// <summary>Denominator guard: the scan must actually find chip labels at all, or the two
    /// checks above would pass vacuously against a broken regex.</summary>
    [TestCase]
    public void RealScan_FindsChipLabels()
    {
        AssertThat(AllLabelHits().Count)
            .OverrideFailureMessage("The StatChip(\"...\") regex found nothing -- the census is broken, not the codebase.")
            .IsGreater(20);
    }

    private static List<(string File, int Line, string Label)> AllLabelHits()
    {
        var hits = new List<(string, int, string)>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            var code = File.ReadAllText(absolute);
            foreach (Match m in ChipLabel.Matches(code))
            {
                var line = code[..m.Index].Count(c => c == '\n') + 1;
                hits.Add((relative, line, m.Groups[1].Value));
            }
        }

        return hits;
    }

    private static List<(string Relative, string Absolute)> SourceFiles()
    {
        var repoRoot = RepoRoot();
        var full = Path.Combine(repoRoot, "godot", "scripts");

        var files = new List<(string, string)>();
        foreach (var path in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
        }

        AssertThat(files.Count)
            .OverrideFailureMessage(
                $"Only found {files.Count} .cs files under {full} -- too few to trust a source scan "
                + "against. RepoRoot is resolving somewhere unexpected, not this floor.")
            .IsGreater(50);

        return files.OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        AssertThat(dir is not null)
            .OverrideFailureMessage("Could not find Game.sln walking up from the test assembly.")
            .IsTrue();

        return dir!.FullName;
    }
}
#endif
