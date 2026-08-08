using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests;

/// <summary>
/// The keystone of CLAUDE.md rule 12. The seven laws are the game; this file is what stops them from
/// becoming decoration.
///
/// <para><b>The problem it solves.</b> A law written in a document decays — this repo deleted 100+
/// documents that had rotted into instructions the next session obeyed wrongly. A law written as a
/// test decays differently: someone renames the file, or deletes the assertion while fixing
/// something else, and nothing anywhere notices. So the law list and its enforcement are welded
/// together here. Editing the list in CLAUDE.md without touching a test is a red build. Deleting or
/// renaming a tripwire without touching CLAUDE.md is a red build, by name.</para>
///
/// <para><b>Why this makes a LIVING design document safe.</b> `THE-GAME.md` may grow freely — it is
/// description, and rule 8 already settles who wins when a doc disagrees with the code. What may not
/// move freely is this list of seven. A law changes when an owner ruling, its tripwire, and rule 12
/// change together in one PR, which is exactly the ceremony a constitutional change deserves. That
/// CLAUDE.md is on the multi-agent deny-list makes the whole thing owner-mediated by construction.
/// </para>
/// </summary>
public class ConstitutionTests
{
    /// <summary>
    /// The seven laws: slug, the phrase that must appear in CLAUDE.md rule 12, and the test files
    /// that enforce it. Two laws share a file where they are genuinely the same property measured
    /// once (the client decides nothing; the sim stays pure), and one law is enforced twice because
    /// it has both a token half and a proof half.
    /// </summary>
    private static readonly (string Slug, string Phrase, string[] Files)[] Laws =
    [
        ("influence-never-orders", "influence never orders",
            ["sim/GameSim.Tests/Kernel/HeroSovereigntyCensusTests.cs"]),
        ("no-decision-timers", "no timers on decisions",
            ["sim/GameSim.Tests/Presentation/ClientAuthorityCensusTests.cs"]),
        ("verbs-change-outcomes", "every verb changes an outcome",
            ["sim/GameSim.Tests/Balance/VerbConsequenceFloorTests.cs"]),
        ("show-only-sim-decided", "show only what the sim decided",
            ["sim/GameSim.Tests/Presentation/ClientAuthorityCensusTests.cs"]),
        ("sim-purity-determinism", "sim purity and determinism",
            ["sim/GameSim.Tests/Kernel/SimPurityCensusTests.cs",
             "sim/GameSim.Tests/Kernel/DeterminismTests.cs"]),
        ("no-runtime-llm", "no runtime LLMs in the sim",
            ["sim/GameSim.Tests/Kernel/SimPurityCensusTests.cs"]),
        ("skipping-stays-legal", "skipping stays legal",
            ["sim/GameSim.Tests/Economy/NoSoftlockTests.cs"]),
    ];

    private const int ExpectedLawCount = 7;

    [Fact]
    public void ThereAreExactlySevenLaws()
        => Assert.True(Laws.Length == ExpectedLawCount,
            $"The constitution is pinned at {ExpectedLawCount} laws; this table now holds "
            + $"{Laws.Length}. Adding or removing one is a change to what the game IS, and it lands "
            + "as its own owner-authored PR alongside the CLAUDE.md edit and the tripwire.");

    [Fact]
    public void EveryLaw_IsEnforcedByAFileThatExistsAndStillCarriesItsTag()
    {
        var broken = new List<string>();

        foreach (var (slug, _, files) in Laws)
        {
            foreach (var relative in files)
            {
                var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    broken.Add($"{slug}: {relative} does not exist");
                    continue;
                }
                if (!File.ReadAllText(path).Contains($"LAW:{slug}", StringComparison.Ordinal))
                    broken.Add($"{slug}: {relative} no longer carries its LAW:{slug} tag");
            }
        }

        Assert.True(broken.Count == 0,
            "A law lost its enforcement. Whoever removed it should say so out loud — that is what "
            + "this failure is for:\n  " + string.Join("\n  ", broken));
    }

    [Fact]
    public void NoLawTagExists_ThatTheConstitutionDoesNotKnowAbout()
    {
        var known = Laws.Select(l => l.Slug).ToHashSet(StringComparer.Ordinal);
        var tag = new Regex(@"LAW:([a-z0-9-]+)");
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var found = 0;

        foreach (var path in SourceFiles())
        {
            foreach (Match m in tag.Matches(File.ReadAllText(path)))
            {
                found++;
                var slug = m.Groups[1].Value;
                if (!known.Contains(slug))
                    unknown.Add($"{slug} (in {Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/')})");
            }
        }

        // Denominator: a scan that walked the wrong tree finds no tags and reports no unknown ones,
        // which reads exactly like a clean run. Every law is tagged at least once, so anything under
        // the law count means the walk is broken, not that the repo is tidy.
        Assert.True(found >= ExpectedLawCount,
            $"Only {found} LAW: tags were found across sim/ and godot/ — fewer than the "
            + $"{ExpectedLawCount} laws that are each supposed to carry one. The scan is broken.");

        Assert.True(unknown.Count == 0,
            "A test claims to enforce a law the constitution has never heard of. Either add it to "
            + "CLAUDE.md rule 12 and to the table in this file, or stop calling it a law:\n  "
            + string.Join("\n  ", unknown));
    }

    /// <summary>
    /// The weld. Two copies of the constitution exist by necessity — the prose one every session
    /// reads, and the executable one. This is what stops them drifting apart, which is the failure
    /// mode this repo names most often and has been bitten by most.
    /// </summary>
    [Fact]
    public void EveryLaw_IsWrittenInClaudeMdRule12()
    {
        var claudeMd = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        var start = claudeMd.IndexOf("12. **The seven laws are the game", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "CLAUDE.md has no rule 12. The laws' prose home is gone; either it moved (fix this test "
            + "in the same PR) or it was deleted (do not).");

        var end = claudeMd.IndexOf("\n\n", start, StringComparison.Ordinal);
        var rule12 = end > start ? claudeMd[start..end] : claudeMd[start..];

        var missing = Laws
            .Where(l => !rule12.Contains(l.Phrase, StringComparison.OrdinalIgnoreCase))
            .Select(l => $"{l.Slug} — expected the phrase \"{l.Phrase}\"")
            .ToList();

        Assert.True(missing.Count == 0,
            "A law is enforced by a test but no longer stated in CLAUDE.md rule 12, so the file every "
            + "session actually reads has stopped saying what the game is:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The compass. Rule 12 stops a session breaking a law; nothing stops a session building
    /// something lawful that serves nothing — which is the drift that actually happens on autonomous
    /// runs. The cause is structural rather than a discipline failure: a file that opens with build
    /// commands and branch rules teaches whoever reads it to reason about process, and the game
    /// itself was a link to another document a session might never open. So the game comes first in
    /// the file, and this test keeps it there.
    ///
    /// <para>It asserts position, not just presence: the game must appear before the commands. A
    /// later edit that keeps the words but pushes them below the tooling has undone the entire point
    /// while leaving every phrase intact.</para>
    /// </summary>
    [Fact]
    public void ClaudeMd_LeadsWithTheGame_BeforeTheTooling()
    {
        var claudeMd = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        var game = claudeMd.IndexOf("## The game, before anything else", StringComparison.Ordinal);
        var commands = claudeMd.IndexOf("## Commands", StringComparison.Ordinal);

        Assert.True(game >= 0,
            "CLAUDE.md no longer opens with the game. Every session reads this file and reasons from "
            + "what it finds first; without this section the default frame is 'what is broken' rather "
            + "than 'what does this game need', which is the drift it exists to prevent.");
        Assert.True(commands < 0 || game < commands,
            "The game section is still in CLAUDE.md but now sits below the tooling. A session reads "
            + "top-down and forms its frame before it gets there, so this is the same failure with "
            + "the words still present.");

        var opening = claudeMd[game..(commands > game ? commands : claudeMd.Length)];

        // The product sentence and the five links, checked by their load-bearing phrases. These are
        // the same claims THE-GAME.md makes; if one is edited away, a session loses the frame the
        // whole file is built to give it.
        string[] required =
        [
            "provably turned on work your hands did",
            "provably yours",
            "four honest channels",
            "their own judgment",
            "proves it mattered",
            "town's memory",
        ];

        var missing = required
            .Where(phrase => !opening.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            "The opening no longer states what the game is. Missing:\n  " + string.Join("\n  ", missing));

        // The Serves: line has to be introduced here, where work is chosen — not only in §11.6 where
        // it reads as paperwork filed after the fact.
        Assert.Contains("Serves:", opening, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles()
    {
        foreach (var area in new[] { Path.Combine("sim"), Path.Combine("godot", "scripts"), Path.Combine("godot", "tests") })
        {
            var root = Path.Combine(RepoRoot(), area);
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                yield return path;
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }
}
