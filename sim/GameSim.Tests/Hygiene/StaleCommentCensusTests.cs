using System.Reflection;
using System.Text.RegularExpressions;
using GameSim.Contracts;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// CLAUDE.md rule 8: "a stale doc comment is not clutter; it is an instruction the next session
/// obeys." This is the shape that actually happened (T10): <c>QualityRoller.cs</c>'s doc comment
/// wrote out <c>performanceGrade ?? 550</c> as a worked example of the real code's
/// <c>performanceGrade ?? AutoCraftGrade</c>. PR #583 changed <c>AutoCraftGrade</c> from 550 to
/// 800 twelve days before this was caught; the comment never moved. A later session read the
/// comment, believed it, and wrote a planning document on the wrong arithmetic, which then
/// propagated into a status report — three generations of one stale comment becoming an
/// instruction.
///
/// <para>Three checks, deny-by-default, same idiom as
/// <c>Presentation.ClientAuthorityCensusTests</c> — a pinned exception must cite the ruling that
/// grants it:</para>
/// <para>1. <see cref="NoCommentRestatesAConstsOldValue"/> — the actual historical shape: a
/// comment writing <c>identifier ?? NUMBER</c> where the real code nearby writes
/// <c>identifier ?? SomeConstName</c> and NUMBER no longer matches that const's value.</para>
/// <para>2. <see cref="NoCommentNamesAConstWithTheWrongValue"/> — a comment that names a const
/// directly (<c>AutoCraftGrade = 800</c>) with a stale number.</para>
/// <para>3. <see cref="NoCommentMisstatesPhaseCountOrRegistration"/> — the two cheaper scans:
/// "ALL (THREE|FOUR|FIVE) phases" cross-checked against the real <see cref="DayPhase"/> member
/// count, and a "not yet wired/registered" claim cross-checked against
/// <c>GameComposition.cs</c>.</para>
/// </summary>
public class StaleCommentCensusTests
{
    private static readonly Dictionary<(string File, string Anchor), string> Exceptions = new();
    private const int ExpectedExceptionCount = 0;

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void NoCommentRestatesAConstsOldValue()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SimSourceFiles())
        {
            var (code, comments) = SplitCommentsFromCode(File.ReadAllText(absolute));
            var consts = ConstIntsOf(code);

            // Real code's own "identifier ?? ConstName" pairings — the left side is whatever gets
            // defaulted (a parameter, usually), the right side must be a const declared right here.
            var codePairs = NullCoalesceIdent.Matches(code)
                .Select(m => (Left: m.Groups[1].Value, Const: m.Groups[2].Value))
                .Where(p => consts.ContainsKey(p.Const))
                .ToList();

            foreach (Match m in NullCoalesceNumber.Matches(comments))
            {
                var left = m.Groups[1].Value;
                var number = int.Parse(m.Groups[2].Value);
                var match = codePairs.FirstOrDefault(p => p.Left == left);
                if (match.Left is null || consts[match.Const] == number)
                {
                    continue;
                }

                var key = (relative, m.Value.Trim());
                if (Exceptions.ContainsKey(key))
                {
                    continue;
                }

                violations.Add($"{relative} [{m.Value.Trim()}] — {match.Const} is now {consts[match.Const]}, not {number}");
            }
        }

        Assert.True(violations.Count == 0,
            "A doc comment restates a `?? const` default as a stale bare number (T10's exact "
            + "QualityRoller/AutoCraftGrade shape). Fix the number, or name the const instead of "
            + "inlining it:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void NoCommentNamesAConstWithTheWrongValue()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SimSourceFiles())
        {
            var (code, comments) = SplitCommentsFromCode(File.ReadAllText(absolute));
            var consts = ConstIntsOf(code);

            foreach (Match m in NamedNumber.Matches(comments))
            {
                var name = m.Groups[1].Value;
                var number = int.Parse(m.Groups[2].Value);
                if (!consts.TryGetValue(name, out var actual) || actual == number)
                {
                    continue;
                }

                var key = (relative, m.Value.Trim());
                if (Exceptions.ContainsKey(key))
                {
                    continue;
                }

                violations.Add($"{relative} [{m.Value.Trim()}] — {name} is now {actual}, not {number}");
            }
        }

        Assert.True(violations.Count == 0,
            "A doc comment names a const declared in this same file with a value that no longer "
            + "matches:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void NoCommentMisstatesPhaseCountOrRegistration()
    {
        var realPhaseCount = Enum.GetValues<DayPhase>().Length;
        var compositionRoot = File.ReadAllText(Path.Combine(SimRoot(), "GameComposition.cs"));
        var violations = new List<string>();

        foreach (var (relative, absolute) in SimSourceFiles())
        {
            var (code, comments) = SplitCommentsFromCode(File.ReadAllText(absolute));

            foreach (Match m in PhaseCountWord.Matches(comments))
            {
                var claimed = m.Groups[1].Value.ToUpperInvariant() switch
                {
                    "THREE" => 3, "FOUR" => 4, "FIVE" => 5, _ => -1,
                };
                if (claimed == realPhaseCount || Exceptions.ContainsKey((relative, m.Value)))
                {
                    continue;
                }

                violations.Add($"{relative} [{m.Value}] — DayPhase has {realPhaseCount} members, not {claimed}");
            }

            if (NotYetWired.IsMatch(comments))
            {
                var className = Regex.Match(code, @"\bclass\s+(\w+)").Groups[1].Value;
                if (className.Length > 0 && compositionRoot.Contains($"new {className}(", StringComparison.Ordinal)
                    && !Exceptions.ContainsKey((relative, "not yet wired")))
                {
                    violations.Add($"{relative} [{className}] — claims it is not yet registered, but "
                        + "GameComposition.cs already constructs it");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "A comment misstates the phase count or this handler's registration status — both "
            + "cross-checked against the real source, never trusted from prose:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>The regression proof: QualityRoller's actual pre-fix pairing, reduced to a
    /// standalone snippet so this detector's own correctness never depends on the real bug still
    /// existing anywhere in the tree to scan.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualQualityRollerDrift()
    {
        const string historicalFile = """
            private const int AutoCraftGrade = 800;

            /// effective = clamp(performanceGrade ?? 550, 0, 1000) + jitter
            public static int RollActive(int? performanceGrade)
            {
                var grade = performanceGrade ?? AutoCraftGrade;
                return grade;
            }
            """;

        var (code, comments) = SplitCommentsFromCode(historicalFile);
        var consts = ConstIntsOf(code);
        var codePairs = NullCoalesceIdent.Matches(code)
            .Select(m => (Left: m.Groups[1].Value, Const: m.Groups[2].Value))
            .Where(p => consts.ContainsKey(p.Const))
            .ToList();

        var found = NullCoalesceNumber.Matches(comments).Cast<Match>()
            .Any(m => codePairs.Any(p => p.Left == m.Groups[1].Value)
                      && int.Parse(m.Groups[2].Value) != consts[codePairs[0].Const]);

        Assert.True(found, "The detector would not have caught the actual QualityRoller drift.");
    }

    private static readonly Regex NullCoalesceIdent = new(@"\b(\w+)\s*\?\?\s*(\w+)\b");
    private static readonly Regex NullCoalesceNumber = new(@"\b(\w+)\s*\?\?\s*(-?\d+)\b");
    // Negative lookbehind excludes a const name that is really the tail of a FORMULA (e.g.
    // ForgeScorer.cs: "1000/DevScale = 250 per-mille" — the comment restates the RESULT of
    // dividing by DevScale, not DevScale's own value; DevScale itself is still 4).
    private static readonly Regex NamedNumber = new(@"(?<![/*+-])\b([A-Z]\w*)\s*=\s*(-?\d+)\b");
    private static readonly Regex ConstDecl = new(@"\bconst\s+int\s+(\w+)\s*=\s*(-?\d+)\s*;");
    private static readonly Regex PhaseCountWord = new(@"ALL (THREE|FOUR|FIVE) phases", RegexOptions.IgnoreCase);
    private static readonly Regex NotYetWired = new(@"NOT yet (?:wired|registered)", RegexOptions.IgnoreCase);

    /// <summary>Const-int declarations in this file, by name. A name declared more than once in the
    /// SAME file (distinct classes each with their own same-named const — <c>World.cs</c>'s two
    /// <c>CadenceDays</c>, say) is ambiguous for a file-scoped doc-comment cross-check and is
    /// dropped rather than guessed at, UNLESS every declaration agrees on the value.</summary>
    private static Dictionary<string, int> ConstIntsOf(string code) =>
        ConstDecl.Matches(code)
            .Select(m => (Name: m.Groups[1].Value, Value: int.Parse(m.Groups[2].Value)))
            .GroupBy(c => c.Name)
            .Where(g => g.Select(c => c.Value).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Value);

    /// <summary>Splits a source file into (code-with-comments-blanked, comments-only) so the two
    /// halves never cross-contaminate a match — a const declaration only counts from real code,
    /// and a stale restatement only counts from a comment.</summary>
    private static (string Code, string Comments) SplitCommentsFromCode(string source)
    {
        var blockComments = Regex.Matches(source, @"/\*.*?\*/", RegexOptions.Singleline).Select(m => m.Value);
        var lineComments = Regex.Matches(source, @"//[^\n]*").Select(m => m.Value);
        var comments = string.Join("\n", blockComments.Concat(lineComments));

        var code = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"//[^\n]*", " ");
        return (code, comments);
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

    private static string SimRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        var root = Path.Combine(dir!.FullName, "sim", "GameSim");
        Assert.True(Directory.Exists(root), $"Expected the sim core at {root}.");
        return root;
    }
}
