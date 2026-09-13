using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-MEMORY-21: the heirloom reforge's lineage sentence ("forged from the X of Y", stamped onto
/// <c>Item.HeirloomLineage</c>) has exactly ONE producer in code —
/// <c>GameSim.Crafting.HeirloomHandlers.LineageOf</c> — so the reforge row's preview
/// (<c>LegendsWall.RenderReforgeOptions</c>) and the handler's actual write can never quietly
/// drift apart. This census DISCOVERS every production source file whose CODE spells the
/// template's own literal words, rather than trusting a hand-typed list of "the two places that
/// use it" — the same discovery-not-a-list idiom <c>CommissionSlotCopyCensusTests</c>/
/// <c>GearWornCheckCensusTests</c> already established. A second hand-typed copy of the sentence
/// anywhere else is the exact regression this unit's own PR body warns about: "a preview that is a
/// second copy of a sentence is a preview that will silently stop matching what actually gets
/// written."
///
/// <para>Comment lines (<c>//</c>/<c>///</c>) are excluded from the scan on purpose: two
/// pre-existing doc comments (<c>Contracts/Items.cs</c>, <c>Drama/ProvenanceQuery.cs</c>)
/// illustrate the sentence's shape in prose ("forged from the blade of Sera Deepfall") without
/// ever constructing it — they are documentation about the one producer, not a second producer.
/// This census cares only whether CODE builds the sentence a second time.</para>
/// </summary>
public class HeirloomLineageTemplateCensusTests
{
    /// <summary>relative path → reason citing the ruling that grants the exception.</summary>
    private static readonly Dictionary<string, string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

    // Anchored on the template's own literal words (not "forged" alone, which also appears in
    // unrelated prose like "the forge never closes") — this exact phrase only appears as part of
    // the one interpolated sentence HeirloomHandlers.LineageOf builds.
    private static readonly Regex LineagePhrase = new("forged from the ");

    private static readonly string ProducerFile =
        Path.Combine("sim", "GameSim", "Crafting", "HeirloomHandlers.cs").Replace('\\', '/');

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = Exceptions.Where(e => !citation.IsMatch(e.Value)).Select(e => e.Key).ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  " + string.Join("\n  ", uncited));
    }

    /// <summary>The regression proof: the exact shape a second hand-typed copy would take, standalone
    /// so this detector's own correctness never depends on a real second copy still existing in the
    /// tree.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtASecondHandTypedCopyInCode()
    {
        const string aSecondCopy = "var preview = $\"forged from the {item.Name} of {heroName}\";";
        Assert.Matches(LineagePhrase, aSecondCopy);
        Assert.Contains(CodeLines(aSecondCopy), line => LineagePhrase.IsMatch(line));
    }

    /// <summary>Negative control #1: mentioning "forge"/"forged" alone (the codebase says "the forge
    /// never closes" all over its docs) must never trip this — only the full template phrase does.
    /// </summary>
    [Fact]
    public void NegativeControl_UnrelatedForgeProseIsNotFlagged()
    {
        const string unrelatedProse = "// the forge never closes -- a reforge IS a craft";
        Assert.DoesNotMatch(LineagePhrase, unrelatedProse);
    }

    /// <summary>Negative control #2: a doc comment ILLUSTRATING the sentence's shape (this
    /// codebase's own real <c>Items.cs</c>/<c>ProvenanceQuery.cs</c> comments) is documentation about
    /// the one producer, not a second one — <see cref="CodeLines"/> must exclude it.</summary>
    [Fact]
    public void NegativeControl_ADocCommentIllustratingTheSentenceIsNotCode()
    {
        const string docComment =
            "    /// from a fallen hero's worn gear (\"forged from the blade of Sera Deepfall\"), or null";

        Assert.Matches(LineagePhrase, docComment); // the phrase IS there...
        Assert.DoesNotContain(CodeLines(docComment), line => LineagePhrase.IsMatch(line)); // ...but not as code.
    }

    [Fact]
    public void TheLineageTemplate_HasExactlyOneCodeProducer_HeirloomHandlersItself()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            if (relative == ProducerFile || Exceptions.ContainsKey(relative))
            {
                continue;
            }

            var code = File.ReadAllText(absolute);
            if (CodeLines(code).Any(LineagePhrase.IsMatch))
            {
                violations.Add(relative);
            }
        }

        Assert.True(violations.Count == 0,
            "The lineage template's own words appear in CODE outside HeirloomHandlers.LineageOf, "
            + "its one producer — a second hand-typed copy will silently stop matching the real "
            + "write the moment either side changes alone:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>Every line of <paramref name="code"/> that is not a <c>//</c>/<c>///</c> comment —
    /// a plain leading-whitespace-then-slashes check, not a real C# tokenizer (same "structural
    /// heuristic, not a parser" honesty disclaimer this codebase's other censuses carry).</summary>
    private static IEnumerable<string> CodeLines(string code) =>
        code.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static List<(string Relative, string Absolute)> SourceFiles()
    {
        var repoRoot = RepoRoot();
        var dirs = new[] { "sim/GameSim", "sim/GameSim.Cli", "godot/scripts" };

        var files = new List<(string, string)>();
        foreach (var dir in dirs)
        {
            var full = Path.Combine(repoRoot, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
            }
        }

        return files.OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }
}
